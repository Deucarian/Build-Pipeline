using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildPipelineTests
    {
        private const string TestFolder = "Assets/__DeucarianBuildPipelineTests";
        private const string TestProfilePath = TestFolder + "/WebGL.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }
        }

        [Test]
        public void EnvironmentMappingsAreDeterministicAndDistinct()
        {
            DeucarianWebGLBuildPolicy policy = new DeucarianWebGLBuildPolicy();

            CollectionAssert.Contains(
                policy.GetExpectedSettings(DeucarianBuildEnvironment.Development),
                "compression=Disabled");
            CollectionAssert.Contains(
                policy.GetExpectedSettings(DeucarianBuildEnvironment.Production),
                "compression=Brotli");
            Assert.That(
                policy.GetSettingsFingerprint(DeucarianBuildEnvironment.Development),
                Is.Not.EqualTo(policy.GetSettingsFingerprint(DeucarianBuildEnvironment.Production)));
        }

        [Test]
        public void SynchronizationAppliesPolicyAndOppositeEnvironmentDetectsDrift()
        {
            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                TestProfilePath);
            DeucarianWebGLBuildPolicy policy = new DeucarianWebGLBuildPolicy();

            policy.ApplySettings(profile, DeucarianBuildEnvironment.Development);

            Assert.That(
                policy.ValidateProfile(profile, DeucarianBuildEnvironment.Development).IsValid,
                Is.True);
            Assert.That(
                policy.ValidateProfile(profile, DeucarianBuildEnvironment.Production).IsValid,
                Is.False);

            string serializedProfile = File.ReadAllText(TestProfilePath);
            StringAssert.Contains("webGLCompressionFormat: 2", serializedProfile);
            StringAssert.Contains("webWasm2023: 1", serializedProfile);
        }

        [Test]
        public void RequestValidationRejectsMissingRequestAndOutput()
        {
            Assert.Throws<ArgumentNullException>(() => DeucarianBuildRunner.Build(null));

            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                TestProfilePath);
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildRunner.Build(
                    new DeucarianBuildRequest(
                        profile,
                        DeucarianBuildEnvironment.Development,
                        string.Empty)));
        }

        [Test]
        public void ArtifactClassifierReportsEncodedAndRawBrotliSizes()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "0123456789abcdef.data.br");
            Directory.CreateDirectory(directory);
            try
            {
                byte[] raw = new byte[128 * 1024];
                for (int i = 0; i < raw.Length; i++)
                {
                    raw[i] = (byte)(i % 17);
                }

                using (FileStream output = File.Create(filePath))
                using (BrotliStream encoder = new BrotliStream(
                           output,
                           System.IO.Compression.CompressionLevel.Optimal))
                {
                    encoder.Write(raw, 0, raw.Length);
                }

                DeucarianBuildArtifact artifact =
                    DeucarianBuildArtifactClassifier.Classify(
                        filePath,
                        "Build/0123456789abcdef.data.br");

                Assert.That(artifact.classification, Is.EqualTo("data"));
                Assert.That(artifact.encoding, Is.EqualTo("br"));
                Assert.That(artifact.rawBytes, Is.EqualTo(raw.Length));
                Assert.That(artifact.encodedBytes, Is.LessThan(raw.Length));
                Assert.That(artifact.preEngineBootstrap, Is.True);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void ProductionGateAcceptsHashedBrotliPayloadWithinBudget()
        {
            DeucarianBuildArtifactManifest manifest = CreatePassingProductionManifest();
            DeucarianBuildRequest request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production
            };

            DeucarianBuildValidationResult result =
                new DeucarianWebGLBuildPolicy().ValidateGeneratedArtifacts(request, manifest);

            Assert.That(result.IsValid, Is.True, result.Format("Artifact validation"));
        }

        [Test]
        public void ProductionGateRejectsBudgetDebugRawAndDevelopmentContextViolations()
        {
            DeucarianBuildArtifactManifest manifest = CreatePassingProductionManifest();
            manifest.budget.passed = false;
            manifest.budget.encodedBootstrapBytes = manifest.budget.limitBytes + 1;
            manifest.artifacts.Add(new DeucarianBuildArtifact
            {
                relativePath = "Build/player.symbols.json",
                classification = "debug-symbols",
                encoding = "identity"
            });
            manifest.artifacts.Add(new DeucarianBuildArtifact
            {
                relativePath = "Build/player.wasm",
                classification = "wasm",
                encoding = "identity"
            });
            manifest.artifacts.Add(new DeucarianBuildArtifact
            {
                relativePath = "StreamingAssets/development-context.json",
                classification = "support",
                encoding = "identity"
            });

            DeucarianBuildValidationResult result =
                new DeucarianWebGLBuildPolicy().ValidateGeneratedArtifacts(
                    new DeucarianBuildRequest
                    {
                        Environment = DeucarianBuildEnvironment.Production
                    },
                    manifest);

            Assert.That(result.IsValid, Is.False);
            StringAssert.Contains("above", result.Format("validation"));
            StringAssert.Contains("debug symbols", result.Format("validation"));
            StringAssert.Contains("not Brotli", result.Format("validation"));
            StringAssert.Contains("development context", result.Format("validation"));
        }

        [Test]
        public void ManifestSerializationContainsVersionsFingerprintAndBudget()
        {
            DeucarianBuildArtifactManifest manifest = CreatePassingProductionManifest();
            manifest.packageVersion = "0.2.0";
            manifest.unityVersion = Application.unityVersion;
            manifest.settingsFingerprint = "abc123";

            string json = manifest.ToJson();

            StringAssert.Contains("\"packageVersion\": \"0.2.0\"", json);
            StringAssert.Contains("\"settingsFingerprint\": \"abc123\"", json);
            StringAssert.Contains("\"passed\": true", json);
        }

        private static DeucarianBuildArtifactManifest CreatePassingProductionManifest()
        {
            DeucarianBuildArtifactManifest manifest = new DeucarianBuildArtifactManifest();
            manifest.budget.limitBytes = DeucarianWebGLBuildPolicy.ProductionBootstrapBudgetBytes;
            manifest.budget.encodedBootstrapBytes = 10 * 1024 * 1024;
            manifest.budget.passed = true;
            manifest.artifacts.Add(CreatePayload("0123456789abcdef.data.br", "data"));
            manifest.artifacts.Add(CreatePayload("1234567890abcdef.framework.js.br", "framework"));
            manifest.artifacts.Add(CreatePayload("2345678901abcdef.wasm.br", "wasm"));
            return manifest;
        }

        private static DeucarianBuildArtifact CreatePayload(string fileName, string classification)
        {
            return new DeucarianBuildArtifact
            {
                relativePath = "Build/" + fileName,
                classification = classification,
                encoding = "br",
                encodedBytes = 1,
                rawBytes = 2,
                preEngineBootstrap = true
            };
        }
    }
}
