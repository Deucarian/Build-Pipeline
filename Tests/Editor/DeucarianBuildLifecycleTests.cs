using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildLifecycleTests
    {
        private const string TestFolder =
            "Assets/__DeucarianBuildLifecycleTests";
        private const string TestProfilePath = TestFolder + "/WebGL.asset";
        private const string ApplicableOutput = "Builds/__LifecycleApplicable";

        private static readonly List<string> Events = new List<string>();

        [SetUp]
        public void SetUp()
        {
            Events.Clear();
            TestContributor.Enabled = false;
            TestContributor.PrebuildIssue = string.Empty;
            TestContributor.ArtifactIssue = string.Empty;
            TestContributor.RequiredOptions = BuildOptions.None;
            TestContributor.ThrowDuringDispose = false;
            TestContributor.LastPreparationFailure = null;
            TestContributor.LastRestorationFailure = null;
            FirstContributor.ThrowDuringPrepare = false;
            SecondContributor.ThrowDuringPrepare = false;
            ConditionalConstructorContributor.ThrowDuringConstruction = false;
            ApplicabilityContributor.ThrowDuringApplicability = false;
        }

        [TearDown]
        public void TearDown()
        {
            SetUp();
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }
        }

        [Test]
        public void DiscoveryOrdersContributorsDeterministically()
        {
            DeucarianBuildLifecycleDiscoveryResult discovery =
                DeucarianBuildLifecycleDiscovery.DiscoverFromTypes(new[]
                {
                    typeof(SecondContributor),
                    typeof(FirstContributor),
                    typeof(LaterContributor)
                });

            CollectionAssert.AreEqual(
                new[] { "tests.first", "tests.second", "tests.later" },
                discovery.Entries.Select(entry => entry.Id).ToArray());
            Assert.That(discovery.Issues, Is.Empty);
        }

        [Test]
        public void DiscoveryRejectsDuplicateIds()
        {
            DeucarianBuildLifecycleDiscoveryResult discovery =
                DeucarianBuildLifecycleDiscovery.DiscoverFromTypes(new[]
                {
                    typeof(FirstContributor),
                    typeof(FirstContributor)
                });

            Assert.That(discovery.Entries.Count, Is.EqualTo(1));
            Assert.That(
                discovery.Issues.Any(issue => issue.Contains("Duplicate")),
                Is.True);
        }

        [Test]
        public void ConstructorFailureIsSanitized()
        {
            ConditionalConstructorContributor.ThrowDuringConstruction = true;

            DeucarianBuildLifecycleDiscoveryResult discovery =
                DeucarianBuildLifecycleDiscovery.DiscoverFromTypes(new[]
                {
                    typeof(ConditionalConstructorContributor)
                });

            Assert.That(discovery.Entries, Is.Empty);
            Assert.That(discovery.Issues.Single(), Does.Contain("InvalidOperationException"));
            Assert.That(
                discovery.Issues.Single(),
                Does.Not.Contain("private failure detail"));
        }

        [Test]
        public void ApplicabilityFailureIsSanitizedAndInvalidatesSelection()
        {
            ApplicabilityContributor.ThrowDuringApplicability = true;
            DeucarianBuildLifecycleDiscoveryResult discovery =
                DeucarianBuildLifecycleDiscovery.DiscoverFromTypes(new[]
                {
                    typeof(ApplicabilityContributor)
                });

            DeucarianBuildLifecycleSelection selection =
                DeucarianBuildLifecyclePipeline.SelectAndValidate(
                    Request(),
                    discovery);

            Assert.That(selection.Entries, Is.Empty);
            Assert.That(selection.Validation.IsValid, Is.False);
            Assert.That(
                selection.Validation.Issues.Single(),
                Does.Contain("InvalidOperationException"));
            Assert.That(
                selection.Validation.Issues.Single(),
                Does.Not.Contain("private failure detail"));
        }

        [Test]
        public void ApplicabilityFiltersContributorsBeforeValidation()
        {
            TestContributor.Enabled = true;
            DeucarianBuildLifecycleDiscoveryResult discovery = Discovery(
                typeof(FirstContributor),
                typeof(SecondContributor));

            DeucarianBuildLifecycleSelection applicable =
                DeucarianBuildLifecyclePipeline.SelectAndValidate(
                    Request(),
                    discovery);
            DeucarianBuildLifecycleSelection notApplicable =
                DeucarianBuildLifecyclePipeline.SelectAndValidate(
                    Request("Builds/Other"),
                    discovery);

            Assert.That(applicable.Entries.Count, Is.EqualTo(2));
            Assert.That(notApplicable.Entries, Is.Empty);
        }

        [Test]
        public void PreparationAndRestorationUseOppositeDeterministicOrders()
        {
            TestContributor.Enabled = true;
            DeucarianBuildLifecycleSelection selection = Selection(
                typeof(SecondContributor),
                typeof(FirstContributor));

            using (DeucarianBuildLifecycleScopeSet scope =
                   DeucarianBuildLifecyclePipeline.Prepare(
                       Request(),
                       selection))
            {
                CollectionAssert.AreEqual(
                    new[] { "prepare:tests.first", "prepare:tests.second" },
                    Events);
            }

            CollectionAssert.AreEqual(
                new[]
                {
                    "prepare:tests.first",
                    "prepare:tests.second",
                    "dispose:tests.second",
                    "dispose:tests.first"
                },
                Events);
        }

        [Test]
        public void PartialPreparationFailureRestoresEarlierScopesInReverse()
        {
            TestContributor.Enabled = true;
            SecondContributor.ThrowDuringPrepare = true;
            DeucarianBuildLifecycleSelection selection = Selection(
                typeof(FirstContributor),
                typeof(SecondContributor));

            BuildFailedException exception = Assert.Throws<BuildFailedException>(() =>
                DeucarianBuildLifecyclePipeline.Prepare(
                    Request(),
                    selection));

            CollectionAssert.AreEqual(
                new[]
                {
                    "prepare:tests.first",
                    "prepare:tests.second",
                    "dispose:tests.first"
                },
                Events);
            Assert.That(exception.Message, Does.Contain("tests.second"));
            Assert.That(
                exception.Message,
                Does.Not.Contain("private failure detail"));
            Assert.That(exception.GetBaseException(), Is.SameAs(exception));
            Assert.That(exception.InnerException, Is.Null);
            Exception diagnosticCause =
                DeucarianSanitizedBuildFailure.GetCause(exception);
            Assert.That(diagnosticCause, Is.TypeOf<InvalidOperationException>());
            Assert.That(
                diagnosticCause.InnerException,
                Is.SameAs(TestContributor.LastPreparationFailure));
            Assert.That(
                TestContributor.LastPreparationFailure.StackTrace,
                Does.Contain(nameof(TestContributor.Prepare)));
        }

        [Test]
        public void PartialPreparationPreservesPrimaryAndCleanupFailures()
        {
            TestContributor.Enabled = true;
            TestContributor.ThrowDuringDispose = true;
            SecondContributor.ThrowDuringPrepare = true;
            DeucarianBuildLifecycleSelection selection = Selection(
                typeof(FirstContributor),
                typeof(SecondContributor));

            BuildFailedException failure = Assert.Throws<BuildFailedException>(() =>
                DeucarianBuildLifecyclePipeline.Prepare(
                    Request(),
                    selection));

            Assert.That(failure.InnerException, Is.Null);
            AggregateException aggregate =
                (AggregateException)DeucarianSanitizedBuildFailure.GetCause(
                    failure);
            Assert.That(aggregate.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(
                aggregate.InnerExceptions[0].InnerException,
                Is.SameAs(TestContributor.LastPreparationFailure));
            Assert.That(
                aggregate.InnerExceptions[1].InnerException,
                Is.SameAs(TestContributor.LastRestorationFailure));
            Assert.That(
                TestContributor.LastPreparationFailure.StackTrace,
                Does.Contain(nameof(TestContributor.Prepare)));
            Assert.That(
                TestContributor.LastRestorationFailure.StackTrace,
                Does.Contain(nameof(ActionScope.Dispose)));
            Assert.That(failure.Message, Does.Contain("tests.second"));
            Assert.That(failure.Message, Does.Contain("tests.first"));
            Assert.That(failure.Message, Does.Not.Contain("private failure detail"));
            Assert.That(failure.Message, Does.Not.Contain("private restoration detail"));
            Assert.That(failure.GetBaseException(), Is.SameAs(failure));
        }

        [Test]
        public void PrebuildAndArtifactValidationAreAggregated()
        {
            TestContributor.Enabled = true;
            TestContributor.PrebuildIssue = "shared prebuild issue";
            TestContributor.ArtifactIssue = "shared artifact issue";
            DeucarianBuildLifecycleSelection selection = Selection(
                typeof(FirstContributor),
                typeof(SecondContributor));

            Assert.That(selection.Validation.Issues.Count, Is.EqualTo(2));
            DeucarianBuildValidationResult artifacts =
                DeucarianBuildLifecyclePipeline.ValidateArtifacts(
                    Request(),
                    new DeucarianBuildArtifactManifest(),
                    selection);

            Assert.That(artifacts.Issues.Count, Is.EqualTo(2));
        }

        [Test]
        public void RunnerValidationAggregatesPolicyAndLifecycleWithoutPreparing()
        {
            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                TestProfilePath);
            new DeucarianWebGLBuildPolicy().ApplySettings(
                profile,
                DeucarianBuildEnvironment.Development);
            TestContributor.Enabled = true;
            TestContributor.PrebuildIssue = "lifecycle blocker";
            DeucarianBuildLifecycleDiscoveryResult discovery = Discovery(
                typeof(FirstContributor));

            DeucarianBuildValidationResult validation =
                DeucarianBuildRunner.Validate(
                    new DeucarianBuildRequest(
                        profile,
                        DeucarianBuildEnvironment.Production,
                        ApplicableOutput),
                    discovery);

            Assert.That(
                validation.Issues.Any(issue => issue.Contains("lifecycle blocker")),
                Is.True);
            Assert.That(
                validation.Issues.Any(issue => issue.Contains("drifted")),
                Is.True);
            Assert.That(Events, Is.Empty, "Validation must not prepare build state.");
        }

        [Test]
        public void DispatcherAndManagerValidateActualInvocationLifecycle()
        {
            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                TestProfilePath);
            new DeucarianWebGLBuildPolicy().ApplySettings(
                profile,
                DeucarianBuildEnvironment.Development);
            TestContributor.Enabled = true;
            TestContributor.RequiredOptions = BuildOptions.BuildScriptsOnly;
            TestContributor.PrebuildIssue = "actual invocation lifecycle blocker";
            DeucarianBuildManagerTarget target =
                new DeucarianBuildManagerTarget(
                    "tests.lifecycle",
                    "Lifecycle",
                    string.Empty,
                    TestProfilePath,
                    DeucarianBuildEnvironment.Development,
                    "Builds/Default",
                    _ => new DeucarianBuildResult());
            DeucarianBuildInvocation invocation =
                new DeucarianBuildInvocation(
                    profile,
                    ApplicableOutput,
                    BuildOptions.BuildScriptsOnly,
                    DeucarianBuildInvocationSource.UnityBuildProfiles);

            DeucarianBuildValidationResult dispatcher =
                DeucarianBuildDispatcher.Validate(target, invocation);
            DeucarianBuildValidationResult manager =
                DeucarianBuildManagerWindow.ValidateBuildRequest(
                    profile,
                    DeucarianBuildEnvironment.Development,
                    ApplicableOutput,
                    BuildOptions.BuildScriptsOnly,
                    null);

            Assert.That(
                dispatcher.Issues,
                Has.Some.Contains("actual invocation lifecycle blocker"));
            Assert.That(
                manager.Issues,
                Has.Some.Contains("actual invocation lifecycle blocker"));
            Assert.That(Events, Is.Empty,
                "Manager and dispatcher validation must remain passive.");
        }

        [Test]
        public void RestorationFailurePreservesPrimaryFailureAndSanitizesCleanup()
        {
            TestContributor.Enabled = true;
            TestContributor.ThrowDuringDispose = true;
            bool completed = false;
            DeucarianBuildLifecycleScopeSet scopes =
                DeucarianBuildLifecyclePipeline.Prepare(
                    Request(),
                    Selection(typeof(FirstContributor)));

            const string privatePrimaryDetail =
                "sensitive primary failure detail";
            InvalidOperationException primary = new InvalidOperationException(
                privatePrimaryDetail);
            BuildFailedException failure = Assert.Throws<BuildFailedException>(
                () => DeucarianBuildRunner.ExecuteWithLifecycleRestoration<int>(
                    scopes,
                    () => throw primary,
                    _ => completed = true));

            Assert.That(
                failure.Message,
                Does.Contain(nameof(InvalidOperationException)));
            Assert.That(failure.Message, Does.Contain("tests.first"));
            Assert.That(
                failure.Message,
                Does.Not.Contain(privatePrimaryDetail));
            Assert.That(
                failure.GetBaseException().Message,
                Does.Not.Contain(privatePrimaryDetail));
            Assert.That(
                failure.ToString(),
                Does.Not.Contain(privatePrimaryDetail));
            Assert.That(
                failure.Message,
                Does.Not.Contain("private restoration detail"));
            Assert.That(failure.GetBaseException(), Is.SameAs(failure));
            Assert.That(failure.InnerException, Is.Null);
            AggregateException aggregate =
                (AggregateException)DeucarianSanitizedBuildFailure.GetCause(
                    failure);
            Assert.That(aggregate.InnerExceptions[0], Is.SameAs(primary));
            Assert.That(
                aggregate.InnerExceptions[0].StackTrace,
                Does.Contain(nameof(RestorationFailurePreservesPrimaryFailureAndSanitizesCleanup)));
            Assert.That(
                aggregate.InnerExceptions[1].InnerException,
                Is.SameAs(TestContributor.LastRestorationFailure));
            Assert.That(completed, Is.False,
                "A failed build must not write its success manifest.");
            CollectionAssert.AreEqual(
                new[] { "prepare:tests.first", "dispose:tests.first" },
                Events);
        }

        [Test]
        public void SuccessfulCompletionRunsAfterLifecycleRestoration()
        {
            TestContributor.Enabled = true;
            DeucarianBuildLifecycleScopeSet scopes =
                DeucarianBuildLifecyclePipeline.Prepare(
                    Request(),
                    Selection(typeof(FirstContributor)));

            int result = DeucarianBuildRunner.ExecuteWithLifecycleRestoration(
                scopes,
                () =>
                {
                    Events.Add("build");
                    return 42;
                },
                value => Events.Add("complete:" + value));

            Assert.That(result, Is.EqualTo(42));
            CollectionAssert.AreEqual(
                new[]
                {
                    "prepare:tests.first",
                    "build",
                    "dispose:tests.first",
                    "complete:42"
                },
                Events);
        }

        private static DeucarianBuildLifecycleDiscoveryResult Discovery(
            params Type[] types)
        {
            return DeucarianBuildLifecycleDiscovery.DiscoverFromTypes(types);
        }

        private static DeucarianBuildLifecycleSelection Selection(
            params Type[] types)
        {
            return DeucarianBuildLifecyclePipeline.SelectAndValidate(
                Request(),
                Discovery(types));
        }

        private static DeucarianBuildRequest Request(
            string output = ApplicableOutput)
        {
            return new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Development,
                OutputPath = output
            };
        }

        public abstract class TestContributor :
            IDeucarianBuildLifecycleContributor
        {
            internal static bool Enabled { get; set; }
            internal static string PrebuildIssue { get; set; }
            internal static string ArtifactIssue { get; set; }
            internal static BuildOptions RequiredOptions { get; set; }
            internal static bool ThrowDuringDispose { get; set; }
            internal static Exception LastPreparationFailure { get; set; }
            internal static Exception LastRestorationFailure { get; set; }

            public abstract string Id { get; }
            public abstract int Order { get; }
            protected virtual bool FailsPreparation => false;

            public virtual bool AppliesTo(DeucarianBuildRequest request)
            {
                return Enabled
                       && request != null
                       && request.OutputPath == ApplicableOutput
                       && (request.AdditionalBuildOptions & RequiredOptions)
                       == RequiredOptions;
            }

            public DeucarianBuildValidationResult ValidateBeforeBuild(
                DeucarianBuildRequest request)
            {
                DeucarianBuildValidationResult result =
                    new DeucarianBuildValidationResult();
                result.Add(PrebuildIssue);
                return result;
            }

            public IDisposable Prepare(DeucarianBuildRequest request)
            {
                Events.Add("prepare:" + Id);
                if (FailsPreparation)
                {
                    LastPreparationFailure = new InvalidOperationException(
                        "private failure detail from preparation");
                    throw LastPreparationFailure;
                }

                return new ActionScope(() =>
                {
                    Events.Add("dispose:" + Id);
                    if (ThrowDuringDispose)
                    {
                        LastRestorationFailure = new InvalidOperationException(
                            "private restoration detail");
                        throw LastRestorationFailure;
                    }
                });
            }

            public DeucarianBuildValidationResult ValidateGeneratedArtifacts(
                DeucarianBuildRequest request,
                DeucarianBuildArtifactManifest manifest)
            {
                DeucarianBuildValidationResult result =
                    new DeucarianBuildValidationResult();
                result.Add(ArtifactIssue);
                return result;
            }
        }

        public sealed class FirstContributor : TestContributor
        {
            internal static bool ThrowDuringPrepare { get; set; }
            public override string Id => "tests.first";
            public override int Order => 10;
            protected override bool FailsPreparation => ThrowDuringPrepare;
        }

        public sealed class SecondContributor : TestContributor
        {
            internal static bool ThrowDuringPrepare { get; set; }
            public override string Id => "tests.second";
            public override int Order => 10;
            protected override bool FailsPreparation => ThrowDuringPrepare;
        }

        public sealed class LaterContributor : TestContributor
        {
            public override string Id => "tests.later";
            public override int Order => 20;
        }

        public sealed class ConditionalConstructorContributor : TestContributor
        {
            internal static bool ThrowDuringConstruction { get; set; }

            public ConditionalConstructorContributor()
            {
                if (ThrowDuringConstruction)
                {
                    throw new InvalidOperationException(
                        "private failure detail from construction");
                }
            }

            public override string Id => "tests.constructor";
            public override int Order => 30;
        }

        public sealed class ApplicabilityContributor : TestContributor
        {
            internal static bool ThrowDuringApplicability { get; set; }
            public override string Id => "tests.applicability";
            public override int Order => 40;

            public override bool AppliesTo(DeucarianBuildRequest request)
            {
                if (ThrowDuringApplicability)
                {
                    throw new InvalidOperationException(
                        "private failure detail from applicability");
                }

                return false;
            }
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
