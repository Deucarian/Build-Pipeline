using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    [Serializable]
    public sealed class DeucarianBuildArtifactManifest
    {
        public const string FileName = "deucarian-build-manifest.json";
        public const int CurrentSchemaVersion = 2;

        public int schemaVersion = CurrentSchemaVersion;
        public string packageVersion;
        public string unityVersion;
        public string environment;
        public string buildGuid;
        public double durationSeconds;
        public string settingsFingerprint;
        public DeucarianAotSafetyReport aotSafety =
            new DeucarianAotSafetyReport();
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
            File.WriteAllText(Path.Combine(outputDirectory, FileName), ToJson());
        }

        internal static DeucarianBuildArtifactManifest Create(
            DeucarianBuildRequest request,
            BuildReport report,
            string settingsFingerprint,
            long bootstrapBudgetBytes,
            DeucarianAotSafetyReport aotSafetyReport)
        {
            string outputFullPath = DeucarianBuildPathUtility.ToFullOutputPath(request.OutputPath);
            DeucarianBuildArtifactManifest manifest = new DeucarianBuildArtifactManifest
            {
                packageVersion = DeucarianBuildPackage.Version,
                unityVersion = Application.unityVersion,
                environment = request.Environment.ToString(),
                buildGuid = report.summary.guid.ToString(),
                durationSeconds = report.summary.totalTime.TotalSeconds,
                settingsFingerprint = settingsFingerprint,
                aotSafety = aotSafetyReport ?? new DeucarianAotSafetyReport()
            };

            if (Directory.Exists(outputFullPath))
            {
                string[] files = Directory.GetFiles(outputFullPath, "*", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length; i++)
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
        internal const string Version = "0.5.0";
    }
}
