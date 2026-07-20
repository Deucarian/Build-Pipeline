using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Deucarian.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianBuildManagerTests
    {
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
                    DeucarianEditorUxStandards.MenuRoot + "/Tools and Quality/Build Pipeline",
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
                CreateResult));
            Assert.Throws<ArgumentNullException>(() => new DeucarianBuildManagerTarget(
                "target",
                "Target",
                string.Empty,
                "Assets/Profile.asset",
                DeucarianBuildEnvironment.Development,
                "Builds/Target",
                null));
        }

        [Test]
        public void RegisteredBuildDispatchInvokesProjectCallback()
        {
            int invocations = 0;
            DeucarianBuildResult expected = new DeucarianBuildResult();
            EarlierProvider provider = new EarlierProvider();
            DeucarianBuildManagerTarget target = new DeucarianBuildManagerTarget(
                "registered",
                "Registered",
                string.Empty,
                "Assets/Missing.asset",
                DeucarianBuildEnvironment.Production,
                "Builds/Registered",
                () =>
                {
                    invocations++;
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
                CreateResult);
        }
    }
}
