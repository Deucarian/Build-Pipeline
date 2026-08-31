using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Deucarian.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildManagerTests
    {
        private const string TestFolder = "Assets/__DeucarianBuildManagerTests";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }
        }

        [Test]
        public void PackageExposesExactlyOneBuildPipelineMenu()
        {
            string[] paths = typeof(DeucarianBuildManagerWindow).Assembly
                .GetTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false))
                .Cast<MenuItem>()
                .Select(item => item.menuItem)
                .Where(path => path.StartsWith(
                    DeucarianEditorUxStandards.MenuRoot + "/Build Manager...",
                    StringComparison.Ordinal))
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { DeucarianBuildManagerWindow.MenuPath },
                paths);
        }

        [Test]
        public void WorkbenchContainsStableToolbarContentAndFooter()
        {
            DeucarianBuildManagerWindow window =
                ScriptableObject.CreateInstance<DeucarianBuildManagerWindow>();
            try
            {
                window.CreateGUI();

                Assert.That(window.WorkbenchForTests, Is.Not.Null);
                Assert.That(window.FooterForTests, Is.Not.Null);
                Assert.That(window.minSize.x, Is.GreaterThanOrEqualTo(640f));
                Assert.That(
                    window.rootVisualElement.Q<PopupField<string>>(
                        DeucarianBuildManagerWindow.TargetPopupName),
                    Is.Not.Null);
                Assert.That(
                    window.rootVisualElement.Q<Button>(
                        DeucarianBuildManagerWindow.SyncButtonName),
                    Is.Not.Null);
                Assert.That(
                    window.rootVisualElement.Q<Button>(
                        DeucarianBuildManagerWindow.ApplyButtonName),
                    Is.Not.Null);
                Assert.That(
                    window.rootVisualElement.Q<Button>(
                        DeucarianBuildManagerWindow.ValidateButtonName),
                    Is.Not.Null);
                Assert.That(
                    window.rootVisualElement.Q<Button>(
                        DeucarianBuildManagerWindow.BuildButtonName),
                    Is.Not.Null);
                Assert.That(
                    window.rootVisualElement.Q<Button>(
                            DeucarianBuildManagerWindow.SyncButtonName)
                        .ClassListContains(
                            DeucarianEditorWorkbenchToolbar.StandardActionClass),
                    Is.True);
                Assert.That(
                    window.rootVisualElement.Q<Button>(
                            DeucarianBuildManagerWindow.BuildButtonName)
                        .ClassListContains(
                            DeucarianEditorWorkbenchToolbar.EmphasizedActionClass),
                    Is.True);
                VisualElement toolbar = window.WorkbenchForTests.Toolbar;
                Assert.That(
                    toolbar.ClassListContains(
                        DeucarianEditorWorkbenchToolbar.StableActionLanesClass),
                    Is.True);
                VisualElement actionLane = toolbar.Q<VisualElement>(
                    className: DeucarianEditorCommandBar.ActionGroupClass);
                Assert.That(actionLane, Is.Not.Null);
                Assert.That(
                    actionLane.Children().OfType<Button>().Select(button => button.name),
                    Is.EqualTo(new[]
                    {
                        DeucarianBuildManagerWindow.SyncButtonName,
                        DeucarianBuildManagerWindow.ApplyButtonName,
                        DeucarianBuildManagerWindow.ValidateButtonName,
                        DeucarianBuildManagerWindow.BuildButtonName
                    }));
                Assert.That(
                    window.rootVisualElement.Q<Button>(
                        DeucarianBuildManagerWindow.SyncButtonName).text,
                    Is.EqualTo("Sync Profiles"));
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>(
                        DeucarianBuildManagerWindow.ContentName),
                    Is.Not.Null);
                Assert.That(
                    window.rootVisualElement.Q<VisualElement>(
                        DeucarianBuildManagerWindow.FooterName),
                    Is.Not.Null);
                Assert.That(window.HasAmbientAnimationForTests, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ProjectChangesAreDebouncedAndCancelledWithWindowLifecycle()
        {
            DeucarianBuildManagerWindow window =
                ScriptableObject.CreateInstance<DeucarianBuildManagerWindow>();
            try
            {
                window.CreateGUI();
                int refreshCount = window.DiscoveryRefreshCountForTests;

                window.QueueProjectChangeRefreshForTests();
                window.QueueProjectChangeRefreshForTests();

                Assert.That(window.ProjectChangeRefreshPendingForTests, Is.True);
                Assert.That(window.DiscoveryRefreshCountForTests, Is.EqualTo(refreshCount));

                window.FlushProjectChangeRefreshForTests();

                Assert.That(window.ProjectChangeRefreshPendingForTests, Is.False);
                Assert.That(
                    window.DiscoveryRefreshCountForTests,
                    Is.EqualTo(refreshCount + 1));

                window.QueueProjectChangeRefreshForTests();
                MethodInfo onDisable = typeof(DeucarianBuildManagerWindow).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onDisable, Is.Not.Null);
                onDisable.Invoke(window, null);

                Assert.That(window.ProjectChangeRefreshPendingForTests, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void DiscoveryOrdersProvidersAndTargetsDeterministically()
        {
            DeucarianBuildManagerDiscoveryResult discovery =
                DeucarianBuildManagerDiscovery.DiscoverFromTypes(new[]
                {
                    typeof(LaterProvider),
                    typeof(EarlierProvider)
                });

            CollectionAssert.AreEqual(
                new[]
                {
                    "earlier/alpha",
                    "earlier/zulu",
                    "later/only"
                },
                discovery.Entries.Select(entry => entry.Key).ToArray());
            Assert.That(discovery.Issues, Is.Empty);
        }

        [Test]
        public void DiscoveryRejectsDuplicatesAndIsolatesProviderFailures()
        {
            DeucarianBuildManagerDiscoveryResult discovery =
                DeucarianBuildManagerDiscovery.DiscoverFromTypes(new[]
                {
                    typeof(EarlierProvider),
                    typeof(EarlierProviderDuplicate),
                    typeof(ThrowingConstructorProvider),
                    typeof(ThrowingTargetsProvider),
                    typeof(LaterProvider)
                });

            Assert.That(discovery.Entries.Any(entry => entry.Key == "later/only"), Is.True);
            Assert.That(discovery.Entries.Count(entry => entry.Key.StartsWith("earlier/")),
                Is.EqualTo(2));
            Assert.That(discovery.Issues.Any(issue => issue.Contains("Duplicate")), Is.True);
            Assert.That(discovery.Issues.Any(issue => issue.Contains("failed to initialize")),
                Is.True);
            Assert.That(discovery.Issues.Any(issue => issue.Contains("failed to enumerate")),
                Is.True);
        }

        [Test]
        public void TargetDescriptorRequiresStableConfigurationAndCallback()
        {
            Assert.Throws<ArgumentException>(() => new DeucarianBuildManagerTarget(
                string.Empty,
                "Target",
                string.Empty,
                "Assets/Profile.asset",
                DeucarianBuildEnvironment.Development,
                "Builds/Target",
                invocation => CreateResult()));
            Assert.Throws<ArgumentNullException>(() => new DeucarianBuildManagerTarget(
                "target",
                "Target",
                string.Empty,
                "Assets/Profile.asset",
                DeucarianBuildEnvironment.Development,
                "Builds/Target",
                (Func<DeucarianBuildInvocation, DeucarianBuildResult>)null));
        }

        [Test]
        public void RegisteredBuildDispatchInvokesProjectCallbackWithManagerInvocation()
        {
            int invocations = 0;
            DeucarianBuildResult expected = new DeucarianBuildResult();
            EarlierProvider provider = new EarlierProvider();
            const string profilePath = TestFolder + "/Registered.asset";
            BuildProfile profile = CreateProfile(
                profilePath,
                DeucarianBuildEnvironment.Production);
            DeucarianBuildInvocation captured = null;
            DeucarianBuildManagerTarget target = new DeucarianBuildManagerTarget(
                "registered",
                "Registered",
                string.Empty,
                profilePath,
                DeucarianBuildEnvironment.Production,
                "Builds/Registered",
                invocation =>
                {
                    invocations++;
                    captured = invocation;
                    return expected;
                });
            DeucarianBuildManagerProviderEntry entry =
                new DeucarianBuildManagerProviderEntry
                {
                    Provider = provider,
                    Target = target
                };

            DeucarianBuildResult actual = DeucarianBuildManagerWindow.DispatchBuild(
                entry,
                null,
                DeucarianBuildEnvironment.Development,
                string.Empty);

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(invocations, Is.EqualTo(1));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.BuildProfile, Is.SameAs(profile));
            Assert.That(captured.OutputPath, Is.EqualTo("Builds/Registered"));
            Assert.That(
                captured.Source,
                Is.EqualTo(DeucarianBuildInvocationSource.BuildPipelineManager));
        }

        [Test]
        public void UnityBridgePreservesNativeOutputOptionsAndRunsRegisteredCallback()
        {
            const string profilePath = TestFolder + "/Native.asset";
            BuildProfile profile = CreateProfile(
                profilePath,
                DeucarianBuildEnvironment.Development);
            DeucarianBuildInvocation captured = null;
            DeucarianBuildManagerProviderEntry entry = Entry(
                profilePath,
                DeucarianBuildEnvironment.Development,
                invocation =>
                {
                    captured = invocation;
                    return new DeucarianBuildResult();
                });
            DeucarianBuildManagerDiscoveryResult discovery =
                Discovery(entry);
            int fallbackCalls = 0;

            bool handled = DeucarianUnityBuildBridge.RouteBuild(
                new BuildPlayerOptions
                {
                    locationPathName = "Custom/NativeOutput",
                    options = BuildOptions.AutoRunPlayer | BuildOptions.CleanBuildCache
                },
                profile,
                discovery,
                ignored => DeucarianUnityBuildNoticeDecision.Build,
                ignored => fallbackCalls++,
                ignored => Assert.Fail("Manager should not open."),
                (ignored, message) => Assert.Fail(message));

            Assert.That(handled, Is.True);
            Assert.That(fallbackCalls, Is.Zero);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.OutputPath, Is.EqualTo("Custom/NativeOutput"));
            Assert.That(
                captured.AdditionalBuildOptions,
                Is.EqualTo(BuildOptions.AutoRunPlayer | BuildOptions.CleanBuildCache));
            Assert.That(
                captured.Source,
                Is.EqualTo(DeucarianBuildInvocationSource.UnityBuildProfiles));
        }

        [Test]
        public void UnityBridgeFallsBackForUnregisteredProfile()
        {
            const string profilePath = TestFolder + "/Unregistered.asset";
            BuildProfile profile = CreateProfile(
                profilePath,
                DeucarianBuildEnvironment.Development);
            int fallbackCalls = 0;

            bool handled = DeucarianUnityBuildBridge.RouteBuild(
                new BuildPlayerOptions(),
                profile,
                new DeucarianBuildManagerDiscoveryResult(),
                ignored => DeucarianUnityBuildNoticeDecision.Build,
                ignored => fallbackCalls++,
                ignored => Assert.Fail("Manager should not open."),
                (ignored, message) => Assert.Fail(message));

            Assert.That(handled, Is.False);
            Assert.That(fallbackCalls, Is.EqualTo(1));
        }

        [Test]
        public void UnityBridgeBlocksAmbiguousRegisteredProfile()
        {
            const string profilePath = TestFolder + "/Ambiguous.asset";
            BuildProfile profile = CreateProfile(
                profilePath,
                DeucarianBuildEnvironment.Development);
            DeucarianBuildManagerDiscoveryResult discovery =
                new DeucarianBuildManagerDiscoveryResult();
            int buildCalls = 0;
            discovery.Entries.Add(Entry(
                profilePath,
                DeucarianBuildEnvironment.Development,
                ignored =>
                {
                    buildCalls++;
                    return new DeucarianBuildResult();
                }));
            discovery.Entries.Add(Entry(
                profilePath,
                DeucarianBuildEnvironment.Development,
                ignored =>
                {
                    buildCalls++;
                    return new DeucarianBuildResult();
                }));
            int blockedCalls = 0;

            bool handled = DeucarianUnityBuildBridge.RouteBuild(
                new BuildPlayerOptions(),
                profile,
                discovery,
                ignored => DeucarianUnityBuildNoticeDecision.Build,
                ignored => Assert.Fail("Default build must not run."),
                ignored => Assert.Fail("Manager should not open."),
                (ignored, message) =>
                {
                    blockedCalls++;
                    StringAssert.Contains("Multiple registered build targets", message);
                });

            Assert.That(handled, Is.True);
            Assert.That(blockedCalls, Is.EqualTo(1));
            Assert.That(buildCalls, Is.Zero);
        }

        [Test]
        public void UnityBridgeCanCancelOrOpenManagerBeforeBuild()
        {
            const string profilePath = TestFolder + "/Prompt.asset";
            BuildProfile profile = CreateProfile(
                profilePath,
                DeucarianBuildEnvironment.Development);
            int buildCalls = 0;
            int managerCalls = 0;
            DeucarianBuildManagerProviderEntry entry = Entry(
                profilePath,
                DeucarianBuildEnvironment.Development,
                invocation =>
                {
                    buildCalls++;
                    return new DeucarianBuildResult();
                });
            DeucarianBuildManagerDiscoveryResult discovery = Discovery(entry);

            DeucarianUnityBuildBridge.RouteBuild(
                new BuildPlayerOptions(),
                profile,
                discovery,
                ignored => DeucarianUnityBuildNoticeDecision.Cancel,
                ignored => Assert.Fail("Default build should not run."),
                ignored => managerCalls++,
                (ignored, message) => Assert.Fail(message));
            DeucarianUnityBuildBridge.RouteBuild(
                new BuildPlayerOptions(),
                profile,
                discovery,
                ignored => DeucarianUnityBuildNoticeDecision.OpenManager,
                ignored => Assert.Fail("Default build should not run."),
                ignored => managerCalls++,
                (ignored, message) => Assert.Fail(message));

            Assert.That(buildCalls, Is.Zero);
            Assert.That(managerCalls, Is.EqualTo(1));
        }

        [Test]
        public void PackageSourcesContainNoConsumerIdentifiers()
        {
            PackageInfo package = PackageInfo.FindForAssembly(
                typeof(DeucarianBuildManagerWindow).Assembly);
            Assert.That(package, Is.Not.Null);

            string[] banned =
            {
                "Report" + " Viewer",
                "Report" + "Viewer",
                "Sim" + "ultria",
                "viewer" + "-ready"
            };
            string[] extensions = { ".cs", ".md", ".json", ".asmdef" };
            List<string> violations = new List<string>();
            foreach (string path in Directory.EnumerateFiles(
                         package.resolvedPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                if (!extensions.Contains(Path.GetExtension(path),
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = path.Substring(package.resolvedPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (relativePath.StartsWith(".git" + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string content = File.ReadAllText(path);
                for (int index = 0; index < banned.Length; index++)
                {
                    if (content.IndexOf(banned[index], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        violations.Add(relativePath + " contains banned identifier #" + index + ".");
                    }
                }
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        private static DeucarianBuildResult CreateResult()
        {
            return new DeucarianBuildResult();
        }

        private static BuildProfile CreateProfile(
            string assetPath,
            DeucarianBuildEnvironment environment)
        {
            BuildProfile profile = DeucarianBuildProfileUtility.CreateProfile(
                BuildTarget.WebGL,
                assetPath);
            new DeucarianWebGLBuildPolicy().ApplySettings(profile, environment);
            return profile;
        }

        private static DeucarianBuildManagerProviderEntry Entry(
            string profilePath,
            DeucarianBuildEnvironment environment,
            Func<DeucarianBuildInvocation, DeucarianBuildResult> buildAction)
        {
            DeucarianBuildManagerTarget target = new DeucarianBuildManagerTarget(
                "native",
                "Native",
                string.Empty,
                profilePath,
                environment,
                "Builds/Native",
                buildAction);
            return new DeucarianBuildManagerProviderEntry
            {
                Provider = new EarlierProvider(),
                Target = target,
                Key = "earlier/native",
                Label = "Earlier — Native"
            };
        }

        private static DeucarianBuildManagerDiscoveryResult Discovery(
            DeucarianBuildManagerProviderEntry entry)
        {
            DeucarianBuildManagerDiscoveryResult result =
                new DeucarianBuildManagerDiscoveryResult();
            result.Entries.Add(entry);
            return result;
        }

        public sealed class EarlierProvider : IDeucarianBuildManagerProvider
        {
            public string Id => "earlier";
            public string DisplayName => "Earlier";
            public int Order => 10;
            public bool CanSynchronize => true;

            public IReadOnlyList<DeucarianBuildManagerTarget> GetTargets()
            {
                return new[]
                {
                    Target("zulu", "Zulu"),
                    Target("alpha", "Alpha")
                };
            }

            public void Synchronize()
            {
            }
        }

        public sealed class EarlierProviderDuplicate : IDeucarianBuildManagerProvider
        {
            public string Id => "earlier";
            public string DisplayName => "Duplicate";
            public int Order => 0;
            public bool CanSynchronize => false;
            public IReadOnlyList<DeucarianBuildManagerTarget> GetTargets() =>
                new[] { Target("duplicate", "Duplicate") };
            public void Synchronize()
            {
            }
        }

        public sealed class LaterProvider : IDeucarianBuildManagerProvider
        {
            public string Id => "later";
            public string DisplayName => "Later";
            public int Order => 20;
            public bool CanSynchronize => false;
            public IReadOnlyList<DeucarianBuildManagerTarget> GetTargets() =>
                new[] { Target("only", "Only") };
            public void Synchronize()
            {
            }
        }

        public sealed class ThrowingConstructorProvider : IDeucarianBuildManagerProvider
        {
            public ThrowingConstructorProvider()
            {
                throw new InvalidOperationException("Constructor failure");
            }

            public string Id => "constructor";
            public string DisplayName => "Constructor";
            public int Order => 30;
            public bool CanSynchronize => false;
            public IReadOnlyList<DeucarianBuildManagerTarget> GetTargets() =>
                Array.Empty<DeucarianBuildManagerTarget>();
            public void Synchronize()
            {
            }
        }

        public sealed class ThrowingTargetsProvider : IDeucarianBuildManagerProvider
        {
            public string Id => "targets";
            public string DisplayName => "Targets";
            public int Order => 40;
            public bool CanSynchronize => false;

            public IReadOnlyList<DeucarianBuildManagerTarget> GetTargets()
            {
                throw new InvalidOperationException("Target enumeration failure");
            }

            public void Synchronize()
            {
            }
        }

        private static DeucarianBuildManagerTarget Target(string id, string displayName)
        {
            return new DeucarianBuildManagerTarget(
                id,
                displayName,
                string.Empty,
                "Assets/" + id + ".asset",
                DeucarianBuildEnvironment.Development,
                "Builds/" + id,
                invocation => CreateResult());
        }
    }
}
