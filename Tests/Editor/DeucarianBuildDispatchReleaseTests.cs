using System;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildDispatchReleaseTests
    {
        private const string TestFolder =
            "Assets/__DeucarianBuildDispatchReleaseTests";
        private const string ProfilePath = TestFolder + "/WebGL.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }
        }

        [Test]
        public void DispatcherRejectsNullCallbackResult()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildManagerTarget target = Target(profile, _ => null);

            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildDispatcher.Build(
                    target,
                    Invocation(profile)));

            Assert.That(failure.Message, Does.Contain("returned no result"));
        }

        [Test]
        public void DispatcherAcceptsLegacyOpaqueNonNullResult()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildResult expected = OpaqueResult();
            DeucarianBuildManagerTarget target = Target(profile, _ => expected);

            DeucarianBuildResult actual = DeucarianBuildDispatcher.Build(
                target,
                Invocation(profile));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(target.RequireCompleteResult, Is.False);
        }

        [Test]
        public void DispatcherRejectsResultWithoutBuildReport()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildManagerTarget target = Target(
                profile,
                _ => new DeucarianBuildResult
                {
                    ArtifactManifest = new DeucarianBuildArtifactManifest()
                },
                requireCompleteResult: true);

            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildDispatcher.Build(
                    target,
                    Invocation(profile)));

            Assert.That(failure.Message, Does.Contain("complete runner result"));
        }

        [Test]
        public void DispatcherRejectsResultWithoutArtifactManifest()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildResult result = CompleteResult();
            result.ArtifactManifest = null;
            DeucarianBuildManagerTarget target = Target(
                profile,
                _ => result,
                requireCompleteResult: true);

            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildDispatcher.Build(
                    target,
                    Invocation(profile)));

            Assert.That(failure.Message, Does.Contain("complete runner result"));
        }

        [Test]
        public void DispatcherAcceptsCompleteRunnerResult()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildResult expected = CompleteResult();
            DeucarianBuildManagerTarget target = Target(
                profile,
                _ => expected,
                requireCompleteResult: true);

            DeucarianBuildResult actual = DeucarianBuildDispatcher.Build(
                target,
                Invocation(profile));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(target.RequireCompleteResult, Is.True);
        }

        [Test]
        public void CommandLineRoutesExactTargetAndPreservesInvocation()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildInvocation captured = null;
            int fallbacks = 0;
            DeucarianBuildManagerTarget target = Target(
                profile,
                invocation =>
                {
                    captured = invocation;
                    return OpaqueResult();
                });
            DeucarianBuildManagerDiscoveryResult discovery = Discovery(target);

            DeucarianBuildCommandLine.Dispatch(
                profile,
                DeucarianBuildEnvironment.Development,
                "Builds/CI-Override",
                BuildOptions.BuildScriptsOnly | BuildOptions.AutoRunPlayer,
                discovery,
                request =>
                {
                    fallbacks++;
                    return OpaqueResult();
                });

            Assert.That(fallbacks, Is.Zero);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.BuildProfile, Is.SameAs(profile));
            Assert.That(captured.OutputPath, Is.EqualTo("Builds/CI-Override"));
            Assert.That(
                captured.AdditionalBuildOptions,
                Is.EqualTo(
                    BuildOptions.BuildScriptsOnly | BuildOptions.AutoRunPlayer));
            Assert.That(
                captured.Source,
                Is.EqualTo(DeucarianBuildInvocationSource.CommandLine));
        }

        [Test]
        public void CommandLineRejectsAmbiguousRegisteredProfile()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildManagerDiscoveryResult discovery = Discovery(
                Target(profile, _ => OpaqueResult(), "first"),
                Target(profile, _ => OpaqueResult(), "second"));

            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildCommandLine.Dispatch(
                    profile,
                    DeucarianBuildEnvironment.Development,
                    "Builds/CI",
                    BuildOptions.None,
                    discovery,
                    _ => OpaqueResult()));

            Assert.That(failure.Message, Does.Contain("Multiple registered"));
        }

        [Test]
        public void CommandLineFallsBackOnlyForUnregisteredProfile()
        {
            BuildProfile profile = CreateProfile();
            DeucarianBuildRequest captured = null;
            DeucarianBuildResult expected = OpaqueResult();

            DeucarianBuildResult actual = DeucarianBuildCommandLine.Dispatch(
                profile,
                DeucarianBuildEnvironment.Development,
                "Builds/Custom-CI",
                BuildOptions.CleanBuildCache,
                new DeucarianBuildManagerDiscoveryResult(),
                request =>
                {
                    captured = request;
                    return expected;
                });

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(captured.BuildProfile, Is.SameAs(profile));
            Assert.That(captured.OutputPath, Is.EqualTo("Builds/Custom-CI"));
            Assert.That(
                captured.AdditionalBuildOptions,
                Is.EqualTo(BuildOptions.CleanBuildCache));
        }

        [Test]
        public void CommandLineRejectsDiscoveryIssuesBeforeFallback()
        {
            const string sensitiveDetail =
                "https://redacted.invalid/product-definition";
            BuildProfile profile = CreateProfile();
            var discovery = new DeucarianBuildManagerDiscoveryResult();
            discovery.Issues.Add(
                "Build provider failed to enumerate: " + sensitiveDetail);
            bool fallbackInvoked = false;

            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildCommandLine.Dispatch(
                    profile,
                    DeucarianBuildEnvironment.Development,
                    "Builds/Custom-CI",
                    BuildOptions.None,
                    discovery,
                    _ =>
                    {
                        fallbackInvoked = true;
                        return OpaqueResult();
                    }));

            Assert.That(fallbackInvoked, Is.False);
            Assert.That(failure.Message, Does.Contain("discovery reported 1"));
            Assert.That(failure.Message, Does.Not.Contain(sensitiveDetail));
        }

        private static BuildProfile CreateProfile()
        {
            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                ProfilePath);
            new DeucarianWebGLBuildPolicy().ApplySettings(
                profile,
                DeucarianBuildEnvironment.Development);
            return profile;
        }

        private static DeucarianBuildManagerTarget Target(
            BuildProfile profile,
            Func<DeucarianBuildInvocation, DeucarianBuildResult> callback,
            string id = "target",
            bool requireCompleteResult = false)
        {
            return new DeucarianBuildManagerTarget(
                id,
                id,
                string.Empty,
                AssetDatabase.GetAssetPath(profile),
                DeucarianBuildEnvironment.Development,
                "Builds/Default",
                callback,
                requireCompleteResult: requireCompleteResult);
        }

        private static DeucarianBuildInvocation Invocation(BuildProfile profile)
        {
            return new DeucarianBuildInvocation(
                profile,
                "Builds/Default",
                BuildOptions.None,
                DeucarianBuildInvocationSource.Programmatic);
        }

        private static DeucarianBuildManagerDiscoveryResult Discovery(
            params DeucarianBuildManagerTarget[] targets)
        {
            DeucarianBuildManagerDiscoveryResult result =
                new DeucarianBuildManagerDiscoveryResult();
            for (int index = 0; index < targets.Length; index++)
            {
                result.Entries.Add(new DeucarianBuildManagerProviderEntry
                {
                    Target = targets[index]
                });
            }

            return result;
        }

        private static DeucarianBuildResult CompleteResult()
        {
#pragma warning disable SYSLIB0050
            BuildReport report = (BuildReport)FormatterServices
                .GetUninitializedObject(typeof(BuildReport));
#pragma warning restore SYSLIB0050
            return new DeucarianBuildResult
            {
                BuildReport = report,
                ArtifactManifest = new DeucarianBuildArtifactManifest()
            };
        }

        private static DeucarianBuildResult OpaqueResult()
        {
            return new DeucarianBuildResult();
        }
    }
}
