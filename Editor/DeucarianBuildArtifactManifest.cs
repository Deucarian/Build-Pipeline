using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    [Serializable]
    public sealed class DeucarianBuildArtifactManifest
    {
        public const string FileName = "deucarian-build-manifest.json";
        public const int CurrentSchemaVersion = 3;

        public int schemaVersion = CurrentSchemaVersion;
        public string packageVersion;
        public string unityVersion;
        public string environment;
        public string buildProfileGuid;
        public string compatibilityFingerprint;
        public string buildGuid;
        public double durationSeconds;
        public string settingsFingerprint;
        public DeucarianBuildBudgetResult budget = new DeucarianBuildBudgetResult();
        public List<DeucarianBuildArtifact> artifacts = new List<DeucarianBuildArtifact>();

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public void WriteTo(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
            }

            Directory.CreateDirectory(outputDirectory);
            string manifestPath = Path.Combine(outputDirectory, FileName);
            string temporaryPath = manifestPath + ".tmp";
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                File.WriteAllText(temporaryPath, ToJson());
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }

                File.Move(temporaryPath, manifestPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        internal static DeucarianBuildArtifactManifest Create(
            DeucarianBuildRequest request,
            BuildReport report,
            string settingsFingerprint,
            long bootstrapBudgetBytes,
            BuildOptions effectiveBuildOptions)
        {
            string outputFullPath = DeucarianBuildPathUtility.ToFullOutputPath(request.OutputPath);
            DeucarianBuildArtifactManifest manifest = new DeucarianBuildArtifactManifest
            {
                packageVersion = DeucarianBuildPackage.Version,
                unityVersion = Application.unityVersion,
                environment = request.Environment.ToString(),
                buildProfileGuid = GetBuildProfileGuid(request),
                compatibilityFingerprint =
                    DeucarianBuildCompatibility.CreateFingerprint(
                        request,
                        effectiveBuildOptions),
                buildGuid = report.summary.guid.ToString(),
                durationSeconds = report.summary.totalTime.TotalSeconds,
                settingsFingerprint = settingsFingerprint
            };

            if (Directory.Exists(outputFullPath))
            {
                if (!DeucarianBuildOutputPathSafety.TryCollectFiles(
                        outputFullPath,
                        out List<string> files,
                        out string issue))
                {
                    throw new UnityEditor.Build.BuildFailedException(
                        "Artifact manifest generation failed:\n- " + issue);
                }

                files.Sort(StringComparer.Ordinal);
                for (int i = 0; i < files.Count; i++)
                {
                    string relativePath = DeucarianBuildPathUtility.GetRelativePath(outputFullPath, files[i]);
                    if (string.Equals(relativePath, FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    DeucarianBuildArtifact artifact = DeucarianBuildArtifactClassifier.Classify(
                        files[i],
                        relativePath);
                    manifest.artifacts.Add(artifact);
                }
            }

            long encodedBootstrapBytes = 0;
            long rawBootstrapBytes = 0;
            for (int i = 0; i < manifest.artifacts.Count; i++)
            {
                DeucarianBuildArtifact artifact = manifest.artifacts[i];
                if (!artifact.preEngineBootstrap)
                {
                    continue;
                }

                encodedBootstrapBytes += artifact.encodedBytes;
                rawBootstrapBytes += artifact.rawBytes;
            }

            manifest.budget.limitBytes = bootstrapBudgetBytes;
            manifest.budget.encodedBootstrapBytes = encodedBootstrapBytes;
            manifest.budget.rawBootstrapBytes = rawBootstrapBytes;
            manifest.budget.passed = request.Environment != DeucarianBuildEnvironment.Production
                                     || encodedBootstrapBytes <= bootstrapBudgetBytes;
            return manifest;
        }

        private static string GetBuildProfileGuid(DeucarianBuildRequest request)
        {
            if (request == null || request.BuildProfile == null)
            {
                return string.Empty;
            }

            string profilePath = UnityEditor.AssetDatabase.GetAssetPath(
                request.BuildProfile);
            return string.IsNullOrWhiteSpace(profilePath)
                ? string.Empty
                : UnityEditor.AssetDatabase.AssetPathToGUID(profilePath);
        }
    }

    [Serializable]
    public sealed class DeucarianBuildArtifact
    {
        public string relativePath;
        public string classification;
        public string encoding;
        public long encodedBytes;
        public long rawBytes;
        public bool preEngineBootstrap;
    }

    [Serializable]
    public sealed class DeucarianBuildBudgetResult
    {
        public long limitBytes;
        public long encodedBootstrapBytes;
        public long rawBootstrapBytes;
        public bool passed;
    }

    internal static class DeucarianBuildPackage
    {
        internal const string Version = "0.6.0";
    }
}
