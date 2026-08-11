using System;
using System.IO;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianAotSafetyProjectInspector
    {
        internal static DeucarianAotSafetyReport Inspect(
            DeucarianAotSafetySettings settings,
            DeucarianAotSafetyMode mode)
        {
            DeucarianAotSafetyReport report = new DeucarianAotSafetyReport
            {
                mode = mode.ToString()
            };

            if (settings == null || !settings.rejectManualProjectLinkXml)
            {
                return report;
            }

            string assetsPath = Application.dataPath;
            if (string.IsNullOrWhiteSpace(assetsPath)
                || !Directory.Exists(assetsPath))
            {
                return report;
            }

            string projectRoot = Path.GetDirectoryName(assetsPath);
            string[] paths = Directory.GetFiles(
                assetsPath,
                "link.xml",
                SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.Ordinal);
            for (int i = 0; i < paths.Length; i++)
            {
                string relativePath = GetRelativePath(projectRoot, paths[i]);
                report.manualLinkXmlFiles.Add(relativePath);
                report.AddFinding(new DeucarianAotSafetyFinding
                {
                    category = "ManualLinkXml",
                    calledApi = relativePath,
                    message = "Manual project linker descriptor '" + relativePath
                              + "' is not allowed. Replace it with generated code or a verified AOT declaration."
                });
            }

            return report;
        }

        private static string GetRelativePath(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return path.Replace('\\', '/');
            }

            Uri rootUri = new Uri(
                Path.GetFullPath(root).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar);
            Uri pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
                .Replace('\\', '/');
        }
    }
}
