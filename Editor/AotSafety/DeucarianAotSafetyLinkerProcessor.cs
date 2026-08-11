using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianAotSafetyLinkerProcessor :
        IUnityLinkerProcessor
    {
        private static readonly StringComparer PathComparer =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        public int callbackOrder => -1000;

        public string GenerateAdditionalLinkXmlFile(
            BuildReport report,
            UnityLinkerBuildPipelineData data)
        {
            if (report == null || data == null)
            {
                throw new BuildFailedException(
                    "Deucarian AOT safety did not receive the linker build context.");
            }

            DeucarianAotSafetySettings settings =
                DeucarianAotSafetySettings.Load();
            DeucarianAotSafetyMode mode =
                DeucarianBuildExecutionScope.CurrentAotSafetyMode
                ?? DeucarianAotSafetyMode.Audit;
            BuildFile[] files = report.GetFiles();
            string[] playerAssemblyPaths = GetPlayerAssemblyPaths(files);
            string[] resolverDirectories = GetResolverDirectories(files);
            DeucarianAotSafetyReport safetyReport =
                DeucarianAotSafetyScanner.Scan(
                    playerAssemblyPaths,
                    resolverDirectories,
                    settings,
                    mode);
            DeucarianAotSafetyBuildState.Merge(safetyReport);

            if (mode == DeucarianAotSafetyMode.Enforce
                && !safetyReport.passed)
            {
                throw new BuildFailedException(
                    safetyReport.FormatFailure(
                        "Deucarian AOT safety validation failed"));
            }

            return null;
        }

        private static string[] GetPlayerAssemblyPaths(
            IEnumerable<BuildFile> buildFiles)
        {
            if (buildFiles == null)
            {
                return Array.Empty<string>();
            }

            return buildFiles
                .Where(file => string.Equals(
                    file.role,
                    CommonRoles.managedLibrary,
                    StringComparison.Ordinal))
                .Select(file => file.path)
                .Where(IsManagedAssemblyPath)
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .Distinct(PathComparer)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetResolverDirectories(
            IEnumerable<BuildFile> buildFiles)
        {
            if (buildFiles == null)
            {
                return Array.Empty<string>();
            }

            return buildFiles
                .Select(file => file.path)
                .Where(IsManagedAssemblyPath)
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .Select(Path.GetDirectoryName)
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Distinct(PathComparer)
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsManagedAssemblyPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && string.Equals(
                       Path.GetExtension(path),
                       ".dll",
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
