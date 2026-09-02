using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.Build;
using UnityEngine;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildAttemptReleaseTests
    {
        private readonly List<string> temporaryOutputs = new List<string>();

        [SetUp]
        public void SetUp()
        {
            DeucarianBuildLifecycleScopeRegistry.RestoreAllForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DeucarianBuildLifecycleScopeRegistry.RestoreAllForTests();
            for (int index = 0; index < temporaryOutputs.Count; index++)
            {
                if (Directory.Exists(temporaryOutputs[index]))
                {
                    Directory.Delete(temporaryOutputs[index], true);
                }
            }

            temporaryOutputs.Clear();
        }

        [Test]
        public void BuildFailureInvalidatesPreviousSuccessManifest()
        {
            InvalidOperationException primary =
                new InvalidOperationException("build failed");

            AssertActionFailureInvalidates(primary);
        }

        [Test]
        public void ArtifactFailureInvalidatesPreviousSuccessManifest()
        {
            BuildFailedException primary =
                new BuildFailedException("artifact validation failed");

            AssertActionFailureInvalidates(primary);
        }

        [Test]
        public void RestorationFailureInvalidatesPreviousSuccessManifest()
        {
            string output = CreateOutput("restoration");
            WritePreviousManifest(output);
            bool completed = false;
            InvalidOperationException restorationFailure =
                new InvalidOperationException("private cleanup detail");
            DeucarianBuildLifecycleScopeSet scopes = ScopeSet(
                new DeucarianBuildLifecycleScopeSet.PreparedScope
                {
                    Id = "tests.restoration",
                    Scope = new ActionScope(() => throw restorationFailure)
                });

            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildRunner.ExecuteBuildAttempt(
                    output,
                    scopes,
                    () => 42,
                    value => completed = true));

            Assert.That(failure.Message, Does.Contain("restoration"));
            Assert.That(failure.Message, Does.Not.Contain("private cleanup detail"));
            Assert.That(failure.GetBaseException(), Is.SameAs(failure));
            Assert.That(failure.InnerException, Is.Null);
            AggregateException diagnosticCause =
                (AggregateException)DeucarianSanitizedBuildFailure.GetCause(
                    failure);
            Assert.That(diagnosticCause, Is.Not.Null);
            Assert.That(
                diagnosticCause.InnerExceptions[0].InnerException,
                Is.SameAs(restorationFailure));
            Assert.That(completed, Is.False);
            Assert.That(File.Exists(ManifestPath(output)), Is.False);
        }

        [Test]
        public void ManifestWriteFailureCannotLeavePreviousSuccessMarker()
        {
            string output = CreateOutput("write");
            WritePreviousManifest(output);
            DeucarianBuildLifecycleScopeSet scopes = ScopeSet();
            string temporaryManifest = ManifestPath(output) + ".tmp";

            Assert.Catch<Exception>(() =>
                DeucarianBuildRunner.ExecuteBuildAttempt(
                    output,
                    scopes,
                    () => new DeucarianBuildArtifactManifest
                    {
                        packageVersion = DeucarianBuildPackage.Version,
                        buildGuid = Guid.NewGuid().ToString("N")
                    },
                    manifest =>
                    {
                        Directory.CreateDirectory(temporaryManifest);
                        manifest.WriteTo(output);
                    }));

            Assert.That(File.Exists(ManifestPath(output)), Is.False);
            Assert.That(Directory.Exists(temporaryManifest), Is.True,
                "The failure sentinel proves the atomic writer was attempted.");
        }

        [Test]
        public void SuccessfulCompletionPublishesOneAtomicManifest()
        {
            string output = CreateOutput("success");
            WritePreviousManifest(output);
            DeucarianBuildArtifactManifest expected =
                new DeucarianBuildArtifactManifest
                {
                    packageVersion = DeucarianBuildPackage.Version,
                    buildGuid = "new-success"
                };

            DeucarianBuildRunner.ExecuteBuildAttempt(
                output,
                ScopeSet(),
                () => expected,
                manifest => manifest.WriteTo(output));

            string json = File.ReadAllText(ManifestPath(output));
            Assert.That(json, Does.Contain("new-success"));
            Assert.That(File.Exists(ManifestPath(output) + ".tmp"), Is.False);
        }

        [Test]
        public void ReloadRecoveryRestoresReverseOrderExactlyOnce()
        {
            List<string> events = new List<string>();
            DeucarianBuildLifecycleScopeSet scopes = ScopeSet(
                Prepared("first", () => events.Add("first")),
                Prepared("second", () => events.Add("second")));
            Assert.That(
                DeucarianBuildLifecycleScopeRegistry.ActiveCountForTests,
                Is.EqualTo(1));

            DeucarianBuildValidationResult recovery =
                DeucarianBuildLifecycleScopeRegistry.RestoreAllForTests();
            DeucarianBuildLifecycleScopeRegistry.RestoreAllForTests();
            scopes.Restore();

            Assert.That(recovery.IsValid, Is.True);
            CollectionAssert.AreEqual(new[] { "second", "first" }, events);
            Assert.That(
                DeucarianBuildLifecycleScopeRegistry.ActiveCountForTests,
                Is.Zero);
        }

        [Test]
        public void ReloadRecoveryReportsFailureAndContinuesRestoration()
        {
            List<string> events = new List<string>();
            ScopeSet(
                Prepared("first", () => events.Add("first")),
                Prepared("failing", () =>
                {
                    events.Add("failing");
                    throw new InvalidOperationException("private recovery detail");
                }),
                Prepared("last", () => events.Add("last")));

            DeucarianBuildValidationResult recovery =
                DeucarianBuildLifecycleScopeRegistry.RestoreAllForTests();

            Assert.That(recovery.IsValid, Is.False);
            Assert.That(recovery.Issues, Has.Some.Contains("failing"));
            Assert.That(
                recovery.Issues,
                Has.None.Contains("private recovery detail"));
            CollectionAssert.AreEqual(
                new[] { "last", "failing", "first" },
                events);
            Assert.That(
                DeucarianBuildLifecycleScopeRegistry.ActiveCountForTests,
                Is.Zero);
        }

        private void AssertActionFailureInvalidates(Exception primary)
        {
            string output = CreateOutput("action");
            WritePreviousManifest(output);
            DeucarianBuildLifecycleScopeSet scopes = ScopeSet();

            Exception actual = Assert.Catch<Exception>(() =>
                DeucarianBuildRunner.ExecuteBuildAttempt<int>(
                    output,
                    scopes,
                    () => throw primary,
                    value => Assert.Fail("A failed attempt cannot complete.")));

            Assert.That(actual, Is.SameAs(primary));
            Assert.That(File.Exists(ManifestPath(output)), Is.False);
        }

        private string CreateOutput(string label)
        {
            string projectRoot = Path.GetFullPath(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty);
            string output = Path.Combine(
                projectRoot,
                "Temp",
                "__DeucarianBuildAttemptReleaseTests-" + label + "-"
                + Guid.NewGuid().ToString("N"));
            temporaryOutputs.Add(output);
            Directory.CreateDirectory(output);
            return output;
        }

        private static void WritePreviousManifest(string output)
        {
            File.WriteAllText(ManifestPath(output), "previous success");
        }

        private static string ManifestPath(string output)
        {
            return Path.Combine(
                output,
                DeucarianBuildArtifactManifest.FileName);
        }

        private static DeucarianBuildLifecycleScopeSet ScopeSet(
            params DeucarianBuildLifecycleScopeSet.PreparedScope[] scopes)
        {
            return new DeucarianBuildLifecycleScopeSet(
                new List<DeucarianBuildLifecycleScopeSet.PreparedScope>(scopes));
        }

        private static DeucarianBuildLifecycleScopeSet.PreparedScope Prepared(
            string id,
            Action action)
        {
            return new DeucarianBuildLifecycleScopeSet.PreparedScope
            {
                Id = id,
                Scope = new ActionScope(action)
            };
        }

        private sealed class ActionScope : IDisposable
        {
            private readonly Action action;
            private bool disposed;

            internal ActionScope(Action action)
            {
                this.action = action;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                action();
            }
        }
    }
}
