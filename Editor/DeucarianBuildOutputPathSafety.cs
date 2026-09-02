using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildOutputPathSafety
    {
        private static readonly string[] ReservedProjectRoots =
        {
            ".git",
            "Assets",
            "Library",
            "Packages",
            "ProjectSettings",
            "UserSettings"
        };

        internal static bool TryValidate(
            string fullPath,
            out string issue)
        {
            return TryValidate(
                fullPath,
                File.GetAttributes,
                Directory.GetFileSystemEntries,
                out issue);
        }

        internal static bool TryValidate(
            string fullPath,
            Func<string, FileAttributes> getAttributes,
            Func<string, string[]> getEntries,
            out string issue)
        {
            issue = string.Empty;
            if (getAttributes == null || getEntries == null)
            {
                issue = "The build output boundary could not be inspected safely.";
                return false;
            }

            string projectRoot = GetProjectRoot();
            if (string.Equals(fullPath, projectRoot, PathComparison))
            {
                issue = "The Unity project root cannot be used as a build output.";
                return false;
            }

            string projectPrefix = WithSeparator(projectRoot);
            if (!fullPath.StartsWith(projectPrefix, PathComparison))
            {
                issue = "The build output must stay inside the Unity project.";
                return false;
            }

            string buildsRoot = NormalizeDirectoryPath(
                Path.Combine(projectRoot, "Builds"));
            if (string.Equals(fullPath, buildsRoot, PathComparison))
            {
                issue = "The project Builds root cannot be used as one build output.";
                return false;
            }

            if (IsReservedProjectPath(fullPath, projectRoot))
            {
                issue = "Unity and repository control directories cannot be used as build outputs.";
                return false;
            }

            string current = fullPath;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (!TryValidatePathSegment(
                        current,
                        getAttributes,
                        out issue))
                {
                    return false;
                }

                if (string.Equals(current, projectRoot, PathComparison))
                {
                    break;
                }

                string parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent)
                    || string.Equals(parent, current, PathComparison))
                {
                    issue = "The build output boundary could not be established safely.";
                    return false;
                }

                current = parent;
            }

            return !Directory.Exists(fullPath)
                   || TryValidateDirectoryTree(
                       fullPath,
                       getAttributes,
                       getEntries,
                       out issue);
        }

        internal static bool IsStrictBuildsChild(string fullPath)
        {
            string buildsRoot = NormalizeDirectoryPath(
                Path.Combine(GetProjectRoot(), "Builds"));
            return fullPath.StartsWith(
                WithSeparator(buildsRoot),
                PathComparison);
        }

        internal static bool TryValidateDirectoryTree(
            string root,
            out string issue)
        {
            return TryInspectDirectoryTree(
                root,
                File.GetAttributes,
                Directory.GetFileSystemEntries,
                null,
                out issue);
        }

        internal static bool TryValidateDirectoryTree(
            string root,
            Func<string, FileAttributes> getAttributes,
            Func<string, string[]> getEntries,
            out string issue)
        {
            return TryInspectDirectoryTree(
                root,
                getAttributes,
                getEntries,
                null,
                out issue);
        }

        internal static bool TryCollectFiles(
            string root,
            out List<string> files,
            out string issue)
        {
            files = new List<string>();
            return TryInspectDirectoryTree(
                root,
                File.GetAttributes,
                Directory.GetFileSystemEntries,
                files,
                out issue);
        }

        internal static bool TryCollectFiles(
            string root,
            Func<string, FileAttributes> getAttributes,
            Func<string, string[]> getEntries,
            out List<string> files,
            out string issue)
        {
            files = new List<string>();
            return TryInspectDirectoryTree(
                root,
                getAttributes,
                getEntries,
                files,
                out issue);
        }

        private static bool TryInspectDirectoryTree(
            string root,
            Func<string, FileAttributes> getAttributes,
            Func<string, string[]> getEntries,
            ICollection<string> files,
            out string issue)
        {
            issue = string.Empty;
            if (getAttributes == null || getEntries == null)
            {
                issue = "The build output tree could not be inspected safely.";
                return false;
            }

            try
            {
                Stack<string> pending = new Stack<string>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    string path = pending.Pop();
                    FileAttributes attributes = getAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        issue = "The build output contains a filesystem link.";
                        return false;
                    }

                    if ((attributes & FileAttributes.Directory) == 0)
                    {
                        files?.Add(path);
                        continue;
                    }

                    string[] entries = getEntries(path);
                    for (int index = 0; index < entries.Length; index++)
                    {
                        pending.Push(entries[index]);
                    }
                }

                return true;
            }
            catch (Exception)
            {
                issue = "The build output tree could not be inspected safely.";
                return false;
            }
        }

        internal static string NormalizeDirectoryPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            while (fullPath.Length > root.Length
                   && (fullPath[fullPath.Length - 1]
                       == Path.DirectorySeparatorChar
                       || fullPath[fullPath.Length - 1]
                       == Path.AltDirectorySeparatorChar))
            {
                fullPath = fullPath.Substring(0, fullPath.Length - 1);
            }

            return fullPath;
        }

        private static bool IsReservedProjectPath(
            string fullPath,
            string projectRoot)
        {
            for (int index = 0; index < ReservedProjectRoots.Length; index++)
            {
                string reserved = NormalizeDirectoryPath(Path.Combine(
                    projectRoot,
                    ReservedProjectRoots[index]));
                if (string.Equals(fullPath, reserved, PathComparison)
                    || fullPath.StartsWith(
                        WithSeparator(reserved),
                        PathComparison))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryValidatePathSegment(
            string path,
            Func<string, FileAttributes> getAttributes,
            out string issue)
        {
            issue = string.Empty;
            try
            {
                FileAttributes attributes = getAttributes(path);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    issue = "A build output path segment resolves to a file.";
                    return false;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    issue = "A build output path segment is a filesystem link.";
                    return false;
                }

                return true;
            }
            catch (FileNotFoundException)
            {
                return true;
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception)
            {
                issue = "The build output boundary could not be inspected safely.";
                return false;
            }
        }

        private static string GetProjectRoot()
        {
            return NormalizeDirectoryPath(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty);
        }

        private static string WithSeparator(string path)
        {
            return path.TrimEnd(
                       Path.DirectorySeparatorChar,
                       Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        }

        private static StringComparison PathComparison
        {
            get
            {
#if UNITY_EDITOR_WIN
                return StringComparison.OrdinalIgnoreCase;
#else
                return StringComparison.Ordinal;
#endif
            }
        }
    }
}
