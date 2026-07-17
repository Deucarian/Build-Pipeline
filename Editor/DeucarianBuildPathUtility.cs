using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildPathUtility
    {
        private static readonly string[] ReservedProjectRoots =
        {
            ".git",
            ".github",
            ".codex",
            ".agents",
            "Assets",
            "Docs",
            "Documentation",
            "Documentation~",
            "Packages",
            "ProjectSettings",
            "UserSettings",
            "Library",
            "Temp",
            "Logs"
        };

        internal static string ToFullOutputPath(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("A build output path is required.", nameof(outputPath));
            }

            if (Path.IsPathRooted(outputPath))
            {
                throw new ArgumentException(
                    "The build output path must be project-relative.",
                    nameof(outputPath));
            }

            ValidateRelativeSegments(outputPath);

            string projectRoot = GetProjectRoot();
            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, outputPath));
            if (!IsStrictDescendant(projectRoot, fullPath))
            {
                throw new ArgumentException(
                    "The build output path must resolve inside the project and cannot be the project root.",
                    nameof(outputPath));
            }

            for (int i = 0; i < ReservedProjectRoots.Length; i++)
            {
                string reservedRoot = Path.GetFullPath(
                    Path.Combine(projectRoot, ReservedProjectRoots[i]));
                if (IsSameOrDescendant(reservedRoot, fullPath))
                {
                    throw new ArgumentException(
                        "The build output path cannot resolve inside the reserved project directory '"
                        + ReservedProjectRoots[i] + "'.",
                        nameof(outputPath));
                }
            }

            EnsureNoReparsePointAncestors(projectRoot, fullPath, outputPath);
            return fullPath;
        }

        internal static string CleanProjectContainedOutputDirectory(string outputPath)
        {
            string fullPath = ToFullOutputPath(outputPath);
            if (File.Exists(fullPath))
            {
                throw new ArgumentException(
                    "The build output resolves to a file instead of a directory.",
                    nameof(outputPath));
            }

            if (!Directory.Exists(fullPath))
            {
                return fullPath;
            }

            EnsureNoReparsePointDescendants(fullPath, outputPath);
            if (Directory.GetFileSystemEntries(fullPath).Length > 0
                && !HasValidBuildManifest(fullPath))
            {
                throw new InvalidOperationException(
                    "Refusing to clean a non-empty output directory that is not owned "
                    + "by a prior Deucarian build.");
            }

            Directory.Delete(fullPath, true);
            return fullPath;
        }

        internal static string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(rootPath)));
            Uri fileUri = new Uri(Path.GetFullPath(fullPath));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString())
                .Replace('\\', '/');
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string GetProjectRoot()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new InvalidOperationException("Unity did not provide a project root path.");
            }

            return Path.GetFullPath(projectRoot);
        }

        private static bool IsStrictDescendant(string parentPath, string candidatePath)
        {
            return !string.Equals(parentPath, candidatePath, StringComparison.Ordinal)
                   && candidatePath.StartsWith(
                       AppendDirectorySeparator(parentPath),
                       StringComparison.Ordinal);
        }

        private static bool IsSameOrDescendant(string parentPath, string candidatePath)
        {
            return string.Equals(parentPath, candidatePath, ReservedPathComparison)
                   || candidatePath.StartsWith(
                       AppendDirectorySeparator(parentPath),
                       ReservedPathComparison);
        }

        private static void ValidateRelativeSegments(string outputPath)
        {
            string[] segments = outputPath.Split(
                new[] { '/', '\\' },
                StringSplitOptions.None);
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "The build output path cannot contain '..' traversal segments.",
                        nameof(outputPath));
                }

                if (segment.IndexOf(':') >= 0)
                {
                    throw new ArgumentException(
                        "The build output path cannot contain ':' in a path segment.",
                        nameof(outputPath));
                }

                if (ContainsInvalidWindowsPathCharacter(segment))
                {
                    throw new ArgumentException(
                        "The build output path contains an invalid path character.",
                        nameof(outputPath));
                }

                if (segment.EndsWith(".", StringComparison.Ordinal)
                    || segment.EndsWith(" ", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Build output path segments cannot end with a dot or space.",
                        nameof(outputPath));
                }

                if (IsDosShortNameShape(segment))
                {
                    throw new ArgumentException(
                        "The build output path cannot use a DOS short-name-shaped segment.",
                        nameof(outputPath));
                }

                if (IsWindowsDeviceBasename(segment))
                {
                    throw new ArgumentException(
                        "The build output path cannot use a reserved Windows device name.",
                        nameof(outputPath));
                }
            }
        }

        private static bool ContainsInvalidWindowsPathCharacter(string segment)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                char character = segment[i];
                if (character < ' '
                    || character == '<'
                    || character == '>'
                    || character == (char)34
                    || character == '|'
                    || character == '?'
                    || character == '*')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDosShortNameShape(string segment)
        {
            string basename = GetWindowsBasename(segment);
            int tildeIndex = basename.LastIndexOf('~');
            if (tildeIndex < 0 || tildeIndex == basename.Length - 1)
            {
                return false;
            }

            for (int i = tildeIndex + 1; i < basename.Length; i++)
            {
                if (basename[i] < '0' || basename[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsWindowsDeviceBasename(string segment)
        {
            string basename = GetWindowsBasename(segment);
            if (string.Equals(basename, "CON", StringComparison.OrdinalIgnoreCase)
                || string.Equals(basename, "PRN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(basename, "AUX", StringComparison.OrdinalIgnoreCase)
                || string.Equals(basename, "NUL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(basename, "CONIN$", StringComparison.OrdinalIgnoreCase)
                || string.Equals(basename, "CONOUT$", StringComparison.OrdinalIgnoreCase)
                || string.Equals(basename, "CLOCK$", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return basename.Length == 4
                   && IsWindowsDeviceNumber(basename[3])
                   && (basename.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                       || basename.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetWindowsBasename(string segment)
        {
            int extensionIndex = segment.IndexOf('.');
            return extensionIndex >= 0
                ? segment.Substring(0, extensionIndex)
                : segment;
        }

        private static bool IsWindowsDeviceNumber(char character)
        {
            return (character >= '1' && character <= '9')
                   || character == '\u00B9'
                   || character == '\u00B2'
                   || character == '\u00B3';
        }

        private static bool IsNamedBuildEnvironment(string value)
        {
            return string.Equals(
                       value,
                       nameof(DeucarianBuildEnvironment.Development),
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       nameof(DeucarianBuildEnvironment.Production),
                       StringComparison.Ordinal);
        }

        private static void EnsureNoReparsePointAncestors(
            string projectRoot,
            string fullPath,
            string outputPath)
        {
            string relativePath = fullPath.Substring(
                AppendDirectorySeparator(projectRoot).Length);
            string[] parts = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            string currentPath = projectRoot;
            for (int i = 0; i < parts.Length; i++)
            {
                currentPath = Path.Combine(currentPath, parts[i]);
                if (!Directory.Exists(currentPath) && !File.Exists(currentPath))
                {
                    break;
                }

                FileAttributes attributes = File.GetAttributes(currentPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArgumentException(
                        "The build output path cannot traverse a symbolic link or reparse point.",
                        nameof(outputPath));
                }
            }
        }

        private static void EnsureNoReparsePointDescendants(
            string rootPath,
            string outputPath)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(rootPath);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] entries = Directory.GetFileSystemEntries(directory);
                for (int i = 0; i < entries.Length; i++)
                {
                    FileAttributes attributes = File.GetAttributes(entries[i]);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new ArgumentException(
                            "The build output directory cannot contain a symbolic link "
                            + "or reparse point.",
                            nameof(outputPath));
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entries[i]);
                    }
                }
            }
        }

        private static bool HasValidBuildManifest(string outputDirectory)
        {
            string manifestPath = Path.Combine(
                outputDirectory,
                DeucarianBuildArtifactManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                DeucarianBuildArtifactManifest manifest =
                    JsonUtility.FromJson<DeucarianBuildArtifactManifest>(json);
                if (manifest == null
                    || !string.Equals(
                        NormalizeManifestJson(json),
                        NormalizeManifestJson(manifest.ToJson()),
                        StringComparison.Ordinal))
                {
                    return false;
                }

                return manifest.schemaVersion
                       == DeucarianBuildArtifactManifest.CurrentSchemaVersion
                       && !string.IsNullOrWhiteSpace(manifest.packageVersion)
                       && !string.IsNullOrWhiteSpace(manifest.unityVersion)
                       && !string.IsNullOrWhiteSpace(manifest.buildGuid)
                       && manifest.budget != null
                       && manifest.artifacts != null
                       && IsNamedBuildEnvironment(manifest.environment);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string NormalizeManifestJson(string json)
        {
            return (json ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .TrimEnd('\n');
        }

        private static StringComparison ReservedPathComparison =>
            Application.platform == RuntimePlatform.WindowsEditor
            || Application.platform == RuntimePlatform.OSXEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }
}
