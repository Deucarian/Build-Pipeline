using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildCiTests
    {
        private const string TestFolder =
            "Assets/__DeucarianBuildCiTests";
        private const string TestProfilePath =
            TestFolder + "/WebGL.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }
        }

        [Test]
        public void CatalogProjectsStableProviderAndTargetMetadata()
        {
            DeucarianBuildManagerDiscoveryResult discovery =
                new DeucarianBuildManagerDiscoveryResult();
            discovery.Entries.Add(CreateEntry(
                "viewer",
                "webgl-production",
                "Assets/Production.asset",
                DeucarianBuildEnvironment.Production,
                "Builds/WebGL"));

            DeucarianBuildTargetCatalog catalog =
                DeucarianBuildTargetRegistry.CreateCatalog(discovery);

            Assert.That(catalog.valid, Is.True);
            Assert.That(catalog.issues, Is.Empty);
            Assert.That(catalog.targets, Has.Count.EqualTo(1));
            Assert.That(
                catalog.targets[0].key,
                Is.EqualTo("viewer/webgl-production"));
            Assert.That(
                catalog.targets[0].buildProfileAssetPath,
                Is.EqualTo("Assets/Production.asset"));
            Assert.That(
                catalog.targets[0].environment,
                Is.EqualTo("Production"));
        }

        [Test]
        public void RegistryResolutionIsCaseInsensitiveAndFailsOnDiscoveryIssues()
        {
            DeucarianBuildManagerDiscoveryResult discovery =
                new DeucarianBuildManagerDiscoveryResult();
            DeucarianBuildManagerProviderEntry expected = CreateEntry(
                "viewer",
                "webgl-production",
                "Assets/Production.asset",
                DeucarianBuildEnvironment.Production,
                "Builds/WebGL");
            discovery.Entries.Add(expected);

            Assert.That(
                DeucarianBuildTargetRegistry.ResolveEntry(
                    "VIEWER/WEBGL-PRODUCTION",
                    discovery),
                Is.SameAs(expected));

            discovery.Issues.Add("broken provider");
            BuildFailedException exception =
                Assert.Throws<BuildFailedException>(() =>
                    DeucarianBuildTargetRegistry.ResolveEntry(
                        expected.Key,
                        discovery));
            Assert.That(
                exception.Message,
                Does.Contain("broken provider"));
        }

        [Test]
        public void RegisteredDispatchCarriesEnforcedAotModeIntoProjectCallback()
        {
            BuildProfile profile = CreateProfile();
            DeucarianAotSafetyMode? capturedMode = null;
            DeucarianBuildManagerTarget target =
                new DeucarianBuildManagerTarget(
                    "production",
                    "Production",
                    string.Empty,
                    TestProfilePath,
                    DeucarianBuildEnvironment.Production,
                    "Builds/Production",
                    invocation =>
                    {
                        capturedMode =
                            DeucarianBuildInvocationScope.CurrentAotSafetyMode;
                        return new DeucarianBuildResult();
                    });

            DeucarianBuildResult result =
                DeucarianBuildDispatcher.Build(
                    target,
                    new DeucarianBuildInvocation(
                        profile,
                        target.OutputPath,
                        target.DefaultBuildOptions,
                        DeucarianBuildInvocationSource.CommandLine,
                        DeucarianAotSafetyMode.Enforce));

            Assert.That(result, Is.Not.Null);
            Assert.That(
                capturedMode,
                Is.EqualTo(DeucarianAotSafetyMode.Enforce));
            Assert.That(
                DeucarianBuildInvocationScope.CurrentAotSafetyMode,
                Is.Null);
        }

        [Test]
        public void CommandLineParsesOptionsAndAotModeDeterministically()
        {
            Assert.That(
                DeucarianBuildCommandLine.ParseBuildOptions(
                    "CleanBuildCache,DetailedBuildReport"),
                Is.EqualTo(
                    BuildOptions.CleanBuildCache
                    | BuildOptions.DetailedBuildReport));
            Assert.That(
                DeucarianBuildCommandLine.ParseBuildOptions(null),
                Is.EqualTo(BuildOptions.None));
            Assert.That(
                DeucarianBuildCommandLine.ParseAotSafetyMode("enforce"),
                Is.EqualTo(DeucarianAotSafetyMode.Enforce));
            Assert.That(
                DeucarianBuildCommandLine.ParseAotSafetyMode(null),
                Is.EqualTo(DeucarianAotSafetyMode.Inherit));
            Assert.Throws<ArgumentException>(() =>
                DeucarianBuildCommandLine.ParseAotSafetyMode("magic"));
        }

        [Test]
        public void CommandResultWritesMachineReadableFailureEvidence()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "result.json");
            try
            {
                DeucarianBuildCommandResult result =
                    new DeucarianBuildCommandResult
                    {
                        action = "build",
                        target = "viewer/webgl-production",
                        success = false,
                        message = "validation failed",
                        errorType = typeof(BuildFailedException).FullName,
                        startedAtUtc = "start",
                        finishedAtUtc = "finish"
                    };

                result.WriteTo(path);

                string json = File.ReadAllText(path);
                Assert.That(json, Does.Contain("viewer/webgl-production"));
                Assert.That(json, Does.Contain("validation failed"));
                Assert.That(json, Does.Contain("\"success\": false"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static BuildProfile CreateProfile()
        {
            BuildProfile profile =
                DeucarianBuildProfileUtility.CreateProfile(
                    BuildTarget.WebGL,
                    TestProfilePath);
            new DeucarianWebGLBuildPolicy().ApplySettings(
                profile,
                DeucarianBuildEnvironment.Production);
            return profile;
        }

        private static DeucarianBuildManagerProviderEntry CreateEntry(
            string providerId,
            string targetId,
            string profilePath,
            DeucarianBuildEnvironment environment,
            string outputPath)
        {
            FakeProvider provider = new FakeProvider(providerId);
            DeucarianBuildManagerTarget target =
                new DeucarianBuildManagerTarget(
                    targetId,
                    targetId,
                    "Test target",
                    profilePath,
                    environment,
                    outputPath,
                    invocation => new DeucarianBuildResult());
            return new DeucarianBuildManagerProviderEntry
            {
                Provider = provider,
                Target = target,
                Key = providerId + "/" + targetId,
                Label = providerId + " — " + targetId
            };
        }

        private sealed class FakeProvider :
            IDeucarianBuildManagerProvider
        {
            public FakeProvider(string id)
            {
                Id = id;
            }

            public string Id { get; }
            public string DisplayName => Id;
            public int Order => 0;
            public bool CanSynchronize => false;

            public IReadOnlyList<DeucarianBuildManagerTarget> GetTargets()
            {
                return Array.Empty<DeucarianBuildManagerTarget>();
            }

            public void Synchronize()
            {
            }
        }
    }
}
