using System;
using System.IO;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildPathUtility
    {
        internal static string ToFullOutputPath(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("A build output path is required.", nameof(outputPath));
            }

            if (Path.IsPathRooted(outputPath))
            {
                return Path.GetFullPath(outputPath);
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, outputPath));
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
    }
}
