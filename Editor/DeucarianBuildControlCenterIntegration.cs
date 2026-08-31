using System;
using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    [InitializeOnLoad]
    internal static class DeucarianBuildControlCenterIntegration
    {
        private const string PackageId = "com.deucarian.build-pipeline";

        static DeucarianBuildControlCenterIntegration()
        {
            DeucarianToolRegistry.Register(new DeucarianToolDescriptor(
                DeucarianToolIds.BuildManager,
                "Build Manager",
                "Validate and run project-owned build workflows.",
                DeucarianControlCenterArea.BuildAndPackages,
                DeucarianBuildManagerWindow.OpenWindow,
                PackageId,
                "build-settings.editor",
                new[] { "build profile", "webgl", "validation", "artifact" },
                20));
            DeucarianControlCenterRegistry.RegisterCardProvider(
                new DeucarianBuildCardProvider());
        }
    }

    internal readonly struct DeucarianBuildControlCenterSnapshot
    {
        public DeucarianBuildControlCenterSnapshot(
            int targetCount,
            int discoveryIssueCount,
            bool hasActiveProfile,
            string activeProfileName,
            bool hasValidationSnapshot,
            bool activeProfileRegistered,
            string targetName,
            string environmentName,
            int validationIssueCount)
        {
            TargetCount = Math.Max(0, targetCount);
            DiscoveryIssueCount = Math.Max(0, discoveryIssueCount);
            HasActiveProfile = hasActiveProfile;
            ActiveProfileName = activeProfileName ?? string.Empty;
            HasValidationSnapshot = hasValidationSnapshot;
            ActiveProfileRegistered = activeProfileRegistered;
            TargetName = targetName ?? string.Empty;
            EnvironmentName = environmentName ?? string.Empty;
            ValidationIssueCount = Math.Max(0, validationIssueCount);
        }

        public int TargetCount { get; }
        public int DiscoveryIssueCount { get; }
        public bool HasActiveProfile { get; }
        public string ActiveProfileName { get; }
        public bool HasValidationSnapshot { get; }
        public bool ActiveProfileRegistered { get; }
        public string TargetName { get; }
        public string EnvironmentName { get; }
        public int ValidationIssueCount { get; }
    }

    internal static class DeucarianBuildControlCenterStatus
    {
        private static DeucarianBuildControlCenterSnapshot cached;
        private static int activeProfileInstanceId;

        public static DeucarianBuildControlCenterSnapshot Capture()
        {
            BuildProfile profile = BuildProfile.GetActiveBuildProfile();
            if (profile == null)
            {
                return new DeucarianBuildControlCenterSnapshot(
                    cached.TargetCount,
                    cached.DiscoveryIssueCount,
                    false,
                    string.Empty,
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    0);
            }

            if (profile.GetInstanceID() != activeProfileInstanceId)
            {
                return new DeucarianBuildControlCenterSnapshot(
                    cached.TargetCount,
                    cached.DiscoveryIssueCount,
                    true,
                    profile.name,
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    0);
            }

            return cached;
        }

        public static void ValidateActiveProfile()
        {
            BuildProfile profile = BuildProfile.GetActiveBuildProfile();
            DeucarianBuildManagerDiscoveryResult discovery =
                DeucarianBuildManagerDiscovery.Discover();
            DeucarianBuildManagerProviderEntry entry = profile == null
                ? null
                : DeucarianUnityBuildBridge.FindEntryForProfile(profile, discovery);
            DeucarianBuildValidationResult validation = null;
            if (entry != null)
            {
                validation = DeucarianBuildDispatcher.Validate(
                    entry.Target,
                    new DeucarianBuildInvocation(
                        profile,
                        entry.Target.OutputPath,
                        entry.Target.DefaultBuildOptions,
                        DeucarianBuildInvocationSource.Programmatic));
            }

            Publish(profile, entry, validation, discovery.Entries.Count, discovery.Issues.Count);
        }

        internal static void Publish(
            BuildProfile profile,
            DeucarianBuildManagerProviderEntry entry,
            DeucarianBuildValidationResult validation,
            int targetCount,
            int discoveryIssueCount)
        {
            activeProfileInstanceId = profile != null ? profile.GetInstanceID() : 0;
            cached = new DeucarianBuildControlCenterSnapshot(
                targetCount,
                discoveryIssueCount,
                profile != null,
                profile != null ? profile.name : string.Empty,
                validation != null,
                entry != null,
                entry != null ? entry.Target.DisplayName : string.Empty,
                entry != null ? entry.Target.Environment.ToString() : string.Empty,
                validation != null ? validation.Issues.Count : 0);
        }
    }

    internal sealed class DeucarianBuildCardProvider :
        IDeucarianControlCenterCardProvider
    {
        public string Id => "com.deucarian.build-pipeline.status";

        public IEnumerable<DeucarianControlCenterCard> Capture(
            DeucarianControlCenterContext context)
        {
            yield return CreateCard(DeucarianBuildControlCenterStatus.Capture());
        }

        internal static DeucarianControlCenterCard CreateCard(
            DeucarianBuildControlCenterSnapshot snapshot)
        {
            DeucarianControlCenterStatus status;
            string statusText;
            if (!snapshot.HasActiveProfile)
            {
                status = DeucarianControlCenterStatus.Warning;
                statusText = "No active Build Profile";
            }
            else if (!snapshot.HasValidationSnapshot)
            {
                status = DeucarianControlCenterStatus.Info;
                statusText = "Validation not run";
            }
            else if (snapshot.DiscoveryIssueCount > 0)
            {
                status = DeucarianControlCenterStatus.Error;
                statusText = snapshot.DiscoveryIssueCount + " discovery issue(s)";
            }
            else if (!snapshot.ActiveProfileRegistered)
            {
                status = DeucarianControlCenterStatus.Warning;
                statusText = "Profile is not registered";
            }
            else if (snapshot.ValidationIssueCount > 0)
            {
                status = DeucarianControlCenterStatus.Warning;
                statusText = snapshot.ValidationIssueCount + " validation issue(s)";
            }
            else
            {
                status = DeucarianControlCenterStatus.Success;
                statusText = "Ready to build";
            }

            var details = new List<string>();
            if (snapshot.TargetCount > 0 || snapshot.HasValidationSnapshot)
            {
                details.Add(snapshot.TargetCount + " registered build target(s).");
            }

            if (snapshot.HasActiveProfile)
            {
                details.Add("Active profile: " + CleanName(snapshot.ActiveProfileName));
            }

            if (snapshot.ActiveProfileRegistered)
            {
                details.Add("Workflow: " + CleanName(snapshot.TargetName));
                details.Add("Environment: " + CleanName(snapshot.EnvironmentName));
            }

            var actions = new List<DeucarianControlCenterAction>
            {
                new DeucarianControlCenterAction(
                    "build-pipeline.open-manager",
                    "Open Build Manager",
                    DeucarianBuildManagerWindow.OpenWindow,
                    "Review validation before explicitly starting a build.")
            };
            if (snapshot.HasActiveProfile)
            {
                actions.Add(new DeucarianControlCenterAction(
                    "build-pipeline.validate-active",
                    "Validate Active Profile",
                    DeucarianBuildControlCenterStatus.ValidateActiveProfile,
                    "Run the package-owned validation workflow. This never starts a build."));
            }

            return new DeucarianControlCenterCard(
                "build-pipeline.status",
                DeucarianControlCenterArea.BuildAndPackages,
                "Build",
                "Review the active Build Profile and project-owned validation.",
                "com.deucarian.build-pipeline",
                status,
                statusText,
                20,
                details,
                actions,
                new[] { "build", "profile", "target", "validation", "webgl" });
        }

        private static string CleanName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unnamed" : value.Trim();
        }
    }
}