using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    /// <summary>
    /// Synchronizes package-owned WebGL template sources into Unity's required
    /// project-owned Assets/WebGLTemplates location and applies the selection to
    /// project-owned Build Profiles.
    /// </summary>
    public static class DeucarianWebGLTemplateUtility
    {
        public static string SynchronizePackageTemplate(
            Assembly packageAssembly,
            string sourceRelativePath,
            string templateName)
        {
            if (packageAssembly == null)
            {
                throw new ArgumentNullException(nameof(packageAssembly));
            }

            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    packageAssembly);
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                throw new InvalidOperationException(
                    "The WebGL template owner package could not be resolved.");
            }

            string relativePath = RequireRelativePath(
                sourceRelativePath,
                nameof(sourceRelativePath));
            string sourcePath = Path.GetFullPath(Path.Combine(
                package.resolvedPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string packageRoot = Path.GetFullPath(package.resolvedPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!sourcePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The WebGL template source must stay inside its package.",
                    nameof(sourceRelativePath));
            }

            return SynchronizeTemplateDirectory(sourcePath, templateName);
        }

        public static string SynchronizeTemplateDirectory(
            string sourceDirectory,
            string templateName)
        {
            string name = RequireTemplateName(templateName);
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                throw new ArgumentException(
                    "A WebGL template source directory is required.",
                    nameof(sourceDirectory));
            }

            string source = Path.GetFullPath(sourceDirectory);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(
                    "The WebGL template source directory does not exist: " + source);
            }

            string sourceIndex = Path.Combine(source, "index.html");
            if (!File.Exists(sourceIndex))
            {
                throw new InvalidOperationException(
                    "The WebGL template source must contain index.html.");
            }

            string destination = GetProjectTemplateFullPath(name);
            Directory.CreateDirectory(destination);

            HashSet<string> expected = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string sourceFile in Directory.EnumerateFiles(
                         source,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, sourceFile);
                if (relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                expected.Add(relative.Replace('\\', '/'));
                string destinationFile = Path.Combine(destination, relative);
                string destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(sourceFile, destinationFile, true);
            }

            foreach (string destinationFile in Directory.EnumerateFiles(
                         destination,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(destination, destinationFile)
                    .Replace('\\', '/');
                if (relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                    expected.Contains(relative))
                {
                    continue;
                }

                File.Delete(destinationFile);
            }

            AssetDatabase.Refresh();
            return GetProjectTemplateAssetPath(name);
        }

        public static void ApplyTemplate(
            BuildProfile profile,
            string templateName)
        {
            string name = RequireTemplateName(templateName);
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (DeucarianBuildProfileUtility.GetTarget(profile) != BuildTarget.WebGL)
            {
                throw new InvalidOperationException(
                    "A WebGL template can only be applied to a WebGL Build Profile.");
            }

            DeucarianBuildProfileUtility.EnsurePlayerSettingsOverride(profile);
            using (DeucarianBuildProfileUtility.ActivateTemporarily(profile))
            {
                PlayerSettings.WebGL.template = "PROJECT:" + name;
                DeucarianBuildProfileUtility.PersistPlayerSettingsOverride(profile);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        public static DeucarianBuildValidationResult ValidateTemplate(
            BuildProfile profile,
            string templateName,
            IEnumerable<string> requiredRelativeFiles = null)
        {
            string name = RequireTemplateName(templateName);
            DeucarianBuildValidationResult result =
                new DeucarianBuildValidationResult();
            if (profile == null)
            {
                result.Add("A Build Profile is required.");
                return result;
            }

            if (DeucarianBuildProfileUtility.GetTarget(profile) != BuildTarget.WebGL)
            {
                result.Add("The selected Build Profile does not target WebGL.");
                return result;
            }

            if (!DeucarianBuildProfileSettingsSnapshot.TryCreate(
                    profile,
                    out DeucarianBuildProfileSettingsSnapshot settings,
                    out string issue))
            {
                result.Add(issue);
            }
            else if (!settings.TryGetString("webGLTemplate", out string actual))
            {
                result.Add(
                    "The WebGL template could not be read from the Build Profile override.");
            }
            else
            {
                string expected = "PROJECT:" + name;
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    result.Add(
                        "WebGL template drifted: expected " + expected +
                        ", found " + actual + ".");
                }
            }

            ValidateRequiredFile(result, name, "index.html");
            if (requiredRelativeFiles != null)
            {
                foreach (string relativeFile in requiredRelativeFiles)
                {
                    ValidateRequiredFile(
                        result,
                        name,
                        RequireRelativePath(relativeFile, nameof(requiredRelativeFiles)));
                }
            }

            return result;
        }

        public static string GetProjectTemplateAssetPath(string templateName)
        {
            return "Assets/WebGLTemplates/" + RequireTemplateName(templateName);
        }

        private static void ValidateRequiredFile(
            DeucarianBuildValidationResult result,
            string templateName,
            string relativeFile)
        {
            string root = GetProjectTemplateFullPath(templateName);
            string path = Path.GetFullPath(Path.Combine(
                root,
                relativeFile.Replace('/', Path.DirectorySeparatorChar)));
            string normalizedRoot = root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                result.Add("A required WebGL template file resolves outside the template root.");
                return;
            }

            if (!File.Exists(path))
            {
                result.Add(
                    "The WebGL template is missing required file '" +
                    relativeFile.Replace('\\', '/') + "'.");
            }
        }

        private static string GetProjectTemplateFullPath(string templateName)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "WebGLTemplates",
                RequireTemplateName(templateName)));
        }

        private static string RequireTemplateName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A WebGL template name is required.",
                    nameof(value));
            }

            string name = value.Trim();
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.Contains("/") ||
                name.Contains("\\") ||
                name == "." ||
                name == "..")
            {
                throw new ArgumentException(
                    "The WebGL template name must be a single safe folder name.",
                    nameof(value));
            }

            return name;
        }

        private static string RequireRelativePath(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A relative path is required.",
                    parameterName);
            }

            string path = value.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(path) ||
                path.Equals("..", StringComparison.Ordinal) ||
                path.StartsWith("../", StringComparison.Ordinal) ||
                path.Contains("/../"))
            {
                throw new ArgumentException(
                    "The path must stay inside its declared root.",
                    parameterName);
            }

            return path;
        }
    }
}
