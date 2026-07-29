using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Deucarian.BuildPipeline
{
    internal enum DeucarianUnityBuildNoticeDecision
    {
        Build,
        Cancel,
        OpenManager
    }

    [InitializeOnLoad]
    internal static class DeucarianUnityBuildBridge
    {
        private const int NoticeVersion = 1;
        private const string NoticeKeyPrefix =
            "Deucarian.BuildPipeline.UnityBuildNotice.";

        static DeucarianUnityBuildBridge()
        {
            EditorApplication.delayCall -= Register;
            EditorApplication.delayCall += Register;
        }

        internal static void Register()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(HandleBuild);
        }

        internal static void HandleBuild(BuildPlayerOptions options)
        {
            RouteBuild(
                options,
                BuildProfile.GetActiveBuildProfile(),
                DeucarianBuildManagerDiscovery.Discover(),
                ShowNotice,
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer,
                DeucarianBuildManagerWindow.OpenWindowForEntry,
                ShowBlockedBuild);
        }

        internal static bool RouteBuild(
            BuildPlayerOptions options,
            BuildProfile activeProfile,
            DeucarianBuildManagerDiscoveryResult discovery,
            Func<DeucarianBuildManagerProviderEntry, DeucarianUnityBuildNoticeDecision>
                notice,
            Action<BuildPlayerOptions> defaultBuild,
            Action<DeucarianBuildManagerProviderEntry> openManager,
            Action<DeucarianBuildManagerProviderEntry, string> blockedBuild)
        {
            if (defaultBuild == null)
            {
                throw new ArgumentNullException(nameof(defaultBuild));
            }

            List<DeucarianBuildManagerProviderEntry> matches =
                FindEntriesForProfile(activeProfile, discovery);
            if (matches.Count == 0)
            {
                defaultBuild(options);
                return false;
            }

            DeucarianBuildManagerProviderEntry entry = matches[0];
            if (matches.Count > 1)
            {
                blockedBuild?.Invoke(
                    entry,
                    "Multiple registered build targets use this Build Profile. "
                    + "Give every target its own profile before building.");
                return true;
            }

            if (!entry.Target.SupportsInvocationOverrides)
            {
                blockedBuild?.Invoke(
                    entry,
                    "The registered build callback uses the legacy parameterless contract. "
                    + "Update it to accept DeucarianBuildInvocation before building this "
                    + "profile through Unity.");
                return true;
            }

            DeucarianUnityBuildNoticeDecision decision =
                notice != null
                    ? notice(entry)
                    : DeucarianUnityBuildNoticeDecision.Build;
            if (decision == DeucarianUnityBuildNoticeDecision.Cancel)
            {
                return true;
            }

            if (decision == DeucarianUnityBuildNoticeDecision.OpenManager)
            {
                openManager?.Invoke(entry);
                return true;
            }

            try
            {
                DeucarianBuildDispatcher.Build(
                    entry.Target,
                    new DeucarianBuildInvocation(
                        activeProfile,
                        options.locationPathName,
                        options.options,
                        DeucarianBuildInvocationSource.UnityBuildProfiles));
            }
            catch (Exception exception)
            {
                blockedBuild?.Invoke(
                    entry,
                    exception.GetBaseException().Message);
            }

            return true;
        }

        internal static DeucarianBuildManagerProviderEntry FindEntryForProfile(
            BuildProfile profile)
        {
            return FindEntryForProfile(
                profile,
                DeucarianBuildManagerDiscovery.Discover());
        }

        internal static DeucarianBuildManagerProviderEntry FindEntryForProfile(
            BuildProfile profile,
            DeucarianBuildManagerDiscoveryResult discovery)
        {
            List<DeucarianBuildManagerProviderEntry> matches =
                FindEntriesForProfile(profile, discovery);

            if (matches.Count <= 1)
            {
                return matches.Count == 1 ? matches[0] : null;
            }

            DeucarianBuildPipelineLog.Error(
                "Multiple registered build targets use Build Profile '"
                + NormalizePath(AssetDatabase.GetAssetPath(profile))
                + "'. Native Unity build routing is blocked for it.");
            return null;
        }

        internal static bool IsProfileRegistered(BuildProfile profile)
        {
            return FindEntriesForProfile(
                    profile,
                    DeucarianBuildManagerDiscovery.Discover())
                .Count > 0;
        }

        private static List<DeucarianBuildManagerProviderEntry> FindEntriesForProfile(
            BuildProfile profile,
            DeucarianBuildManagerDiscoveryResult discovery)
        {
            List<DeucarianBuildManagerProviderEntry> matches =
                new List<DeucarianBuildManagerProviderEntry>();
            if (profile == null || discovery == null)
            {
                return matches;
            }

            string profilePath = NormalizePath(AssetDatabase.GetAssetPath(profile));
            if (string.IsNullOrEmpty(profilePath))
            {
                return matches;
            }

            for (int i = 0; i < discovery.Entries.Count; i++)
            {
                DeucarianBuildManagerProviderEntry entry = discovery.Entries[i];
                if (string.Equals(
                        profilePath,
                        NormalizePath(entry.Target.BuildProfileAssetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(entry);
                }
            }

            return matches;
        }

        private static DeucarianUnityBuildNoticeDecision ShowNotice(
            DeucarianBuildManagerProviderEntry entry)
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(GetNoticeKey(), false))
            {
                return DeucarianUnityBuildNoticeDecision.Build;
            }

            int decision = EditorUtility.DisplayDialogComplex(
                "Deucarian Build Pipeline",
                "'" + entry.Label + "' is managed by the Deucarian Build Pipeline.\n\n"
                + "Unity's selected output and Build or Build And Run action will be "
                + "preserved. Project validation, build preparation, manifest generation, "
                + "and artifact checks will also run.",
                "Build with Deucarian",
                "Cancel",
                "Open Pipeline Manager");
            if (decision == 0)
            {
                EditorPrefs.SetBool(GetNoticeKey(), true);
                return DeucarianUnityBuildNoticeDecision.Build;
            }

            return decision == 2
                ? DeucarianUnityBuildNoticeDecision.OpenManager
                : DeucarianUnityBuildNoticeDecision.Cancel;
        }

        private static void ShowBlockedBuild(
            DeucarianBuildManagerProviderEntry entry,
            string message)
        {
            string fullMessage =
                "The managed build was blocked.\n\n" + message;
            DeucarianBuildPipelineLog.Error(fullMessage);
            if (Application.isBatchMode)
            {
                throw new BuildPlayerWindow.BuildMethodException(message);
            }

            if (EditorUtility.DisplayDialog(
                    "Deucarian Build Blocked",
                    fullMessage,
                    "Open Pipeline Manager",
                    "Cancel"))
            {
                DeucarianBuildManagerWindow.OpenWindowForEntry(entry);
            }
        }

        private static string GetNoticeKey()
        {
            return NoticeKeyPrefix
                   + NoticeVersion
                   + "."
                   + Hash128.Compute(Application.dataPath);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/');
        }
    }
}
