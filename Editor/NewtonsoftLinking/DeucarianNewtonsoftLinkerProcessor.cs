using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianNewtonsoftLinkerProcessor : IUnityLinkerProcessor
    {
        private const string OutputDirectory =
            "Library/Deucarian/BuildPipeline/NewtonsoftLinker";

        private static readonly StringComparer PathComparer =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(
            BuildReport report,
            UnityLinkerBuildPipelineData data)
        {
            if (report == null)
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker received no build report. "
                    + "Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            if (data == null)
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker received no linker build data. "
                    + "Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker could not determine the Unity project root. "
                    + "Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            string outputPath = Path.Combine(
                projectRoot,
                OutputDirectory,
                GetSafeFileName(data.target.ToString()) + ".link.xml");
            BuildFile[] buildFiles = report.GetFiles();
            string[] playerAssemblyPaths = GetPlayerAssemblyPaths(buildFiles);
            string[] resolverDirectories = GetResolverDirectories(buildFiles);
            return Generate(playerAssemblyPaths, resolverDirectories, outputPath);
        }

        internal static string Generate(string inputDirectory, string outputPath)
        {
            try
            {
                NewtonsoftJsonContractCatalog catalog =
                    NewtonsoftJsonContractDiscovery.Discover(inputDirectory);
                return NewtonsoftJsonLinkXmlWriter.Write(catalog, outputPath);
            }
            catch (BuildFailedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    new InvalidOperationException(
                        "Deucarian Newtonsoft linker could not generate its preservation file. "
                        + "Automatic contract preservation cannot be completed, so the build was stopped.",
                        exception));
            }
        }

        internal static string Generate(
            IEnumerable<string> playerAssemblyPaths,
            IEnumerable<string> resolverDirectories,
            string outputPath)
        {
            try
            {
                NewtonsoftJsonContractCatalog catalog =
                    NewtonsoftJsonContractDiscovery.Discover(
                        playerAssemblyPaths,
                        resolverDirectories);
                return NewtonsoftJsonLinkXmlWriter.Write(catalog, outputPath);
            }
            catch (BuildFailedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    new InvalidOperationException(
                        "Deucarian Newtonsoft linker could not generate its preservation file. "
                        + "Automatic contract preservation cannot be completed, so the build was stopped.",
                        exception));
            }
        }

        private static string[] GetPlayerAssemblyPaths(IEnumerable<BuildFile> buildFiles)
        {
            if (buildFiles == null)
            {
                throw new BuildFailedException(
                    "Deucarian Newtonsoft linker received no build files. "
                    + "Automatic contract preservation cannot be completed, so the build was stopped.");
            }

            string[] paths = buildFiles
                .Where(file => string.Equals(
                    file.role,
                    CommonRoles.managedLibrary,
                    StringComparison.Ordinal))
                .Select(file => file.path)
                .Where(IsManagedAssemblyPath)
                .Select(Path.GetFullPath)
                .Distinct(PathComparer)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            foreach (string path in paths)
            {
                if (!File.Exists(path))
                {
                    throw new BuildFailedException(
                        "Deucarian Newtonsoft linker could not find target player assembly '"
                        + path
                        + "'. Automatic contract preservation cannot be completed, so the build was stopped.");
                }
            }

            return paths;
        }

        private static string[] GetResolverDirectories(IEnumerable<BuildFile> buildFiles)
        {
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

        private static string GetSafeFileName(string value)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                builder.Append(Array.IndexOf(invalidCharacters, character) >= 0
                    ? '_'
                    : character);
            }

            return builder.ToString();
        }
    }
}
