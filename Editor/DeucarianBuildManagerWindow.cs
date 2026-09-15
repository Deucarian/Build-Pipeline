using System;
using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.BuildPipeline
{
    public sealed class DeucarianBuildManagerWindow : EditorWindow
    {
        public const string MenuPath = DeucarianEditorUxStandards.MenuRoot + "/Build Manager...";
        public const string WindowTitle = "Build Manager";
        internal const string CustomTargetKey = "__custom__";
        internal const string TargetPopupName = "deucarian-build-pipeline-target";
        internal const string SyncButtonName = "deucarian-build-pipeline-sync";
        internal const string ApplyButtonName = "deucarian-build-pipeline-apply";
        internal const string ValidateButtonName = "deucarian-build-pipeline-validate";
        internal const string BuildButtonName = "deucarian-build-pipeline-build";
        internal const string ContentName = "deucarian-build-pipeline-content";
        internal const string SelectedTargetSessionKey = "Deucarian.BuildPipeline.Manager.SelectedTarget";
        private const double ProjectChangeDebounceSeconds = 0.35d;
        internal readonly List<DeucarianBuildManagerProviderEntry> Entries = new List<DeucarianBuildManagerProviderEntry>();
        internal readonly List<string> DiscoveryIssues = new List<string>();
        internal readonly List<string> TargetLabels = new List<string>();
        private string selectedTargetKey = CustomTargetKey;
        private bool projectChangeRefreshPending;
        private double projectChangeRefreshAt;
        private int discoveryRefreshCount;
        private DeucarianBuildManagerView view;
        internal DeucarianBuildManagerActions Actions { get; private set; }
        internal BuildProfile CustomProfile { get; set; }
        internal DeucarianBuildEnvironment CustomEnvironment { get; set; } = DeucarianBuildEnvironment.Development;
        internal string CustomOutputPath { get; set; } = "Builds";
        internal DeucarianBuildValidationResult Validation { get; private set; } = new DeucarianBuildValidationResult();
        internal DeucarianBuildResult LastBuild { get; set; }
        internal string LastOutputPath { get; set; }
        internal string Feedback { get; private set; } = string.Empty;
        internal DeucarianEditorStatus FeedbackStatus { get; private set; } = DeucarianEditorStatus.Info;
        internal bool IsBuilding { get; set; }
        internal DeucarianBuildManagerProviderEntry SelectedEntry => Entries.Find(
            entry => string.Equals(entry.Key, selectedTargetKey, StringComparison.OrdinalIgnoreCase));
        internal BuildProfile SelectedProfile => SelectedEntry != null
            ? AssetDatabase.LoadAssetAtPath<BuildProfile>(SelectedEntry.Target.BuildProfileAssetPath) : CustomProfile;
        internal DeucarianBuildEnvironment Environment => SelectedEntry?.Target.Environment ?? CustomEnvironment;
        internal string OutputPath => SelectedEntry?.Target.OutputPath ?? CustomOutputPath;
        internal string DisplayName => SelectedEntry?.Label ?? "Custom Build Profile";
        internal int SelectedIndex => Math.Max(0, TargetLabels.IndexOf(DisplayName));
        internal static IReadOnlyList<string> UserFacingMenuPathsForTests => new[] { MenuPath };
        internal DeucarianBuildValidationResult ValidationForTests => Validation;
        internal bool BuildEnabledForTests => view?.BuildButton.enabledSelf ?? false;
        internal bool ProjectChangeRefreshPendingForTests => projectChangeRefreshPending;
        internal int DiscoveryRefreshCountForTests => discoveryRefreshCount;

        [MenuItem(MenuPath)]
        public static void OpenWindow()
        {
            if (Application.isBatchMode) return;
            var window = DeucarianEditorWindowPages.ShowStandalone<DeucarianBuildManagerWindow>(
                WindowTitle, new Vector2(640, 440));
            window.titleContent = DeucarianEditorIcons.GetPackageContent("editor", WindowTitle,
                "Validate and run project-owned builds.");
        }

        private void OnEnable()
        {
            minSize = new Vector2(640, 440);
            Actions = new DeucarianBuildManagerActions(this);
            selectedTargetKey = SessionState.GetString(SelectedTargetSessionKey, CustomTargetKey);
            EditorApplication.projectChanged += HandleProjectChanged;
            Undo.undoRedoPerformed += HandleProjectChanged;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= HandleProjectChanged;
            Undo.undoRedoPerformed -= HandleProjectChanged;
            CancelProjectChangeRefresh();
            view?.Dispose();
            view = null;
        }

        public void CreateGUI() => BuildView(rootVisualElement);
        internal void BuildView(VisualElement root)
        {
            view?.Dispose();
            view = null;
            RefreshDiscovery();
            view = new DeucarianBuildManagerView(root, this);
        }

        internal void RefreshDiscovery()
        {
            discoveryRefreshCount++;
            var discovery = DeucarianBuildManagerDiscovery.Discover();
            Entries.Clear(); Entries.AddRange(discovery.Entries);
            DiscoveryIssues.Clear(); DiscoveryIssues.AddRange(discovery.Issues);
            TargetLabels.Clear();
            foreach (var entry in Entries) TargetLabels.Add(entry.Label);
            TargetLabels.Add("Custom Build Profile");
            if (selectedTargetKey != CustomTargetKey && SelectedEntry == null)
                selectedTargetKey = Entries.Count > 0 ? Entries[0].Key : CustomTargetKey;
            SessionState.SetString(SelectedTargetSessionKey, selectedTargetKey);
            ValidateCurrent(false);
        }

        internal void SelectTarget(int index)
        {
            if (IsBuilding || index < 0 || index >= TargetLabels.Count) return;
            selectedTargetKey = index < Entries.Count ? Entries[index].Key : CustomTargetKey;
            SessionState.SetString(SelectedTargetSessionKey, selectedTargetKey);
            LastBuild = null; LastOutputPath = null;
            ValidateCurrent(false);
        }

        internal void OnFocus()
        {
            var active = BuildProfile.GetActiveBuildProfile();
            if (active == null) { RefreshView(); return; }
            string path = AssetDatabase.GetAssetPath(active).Replace('\\', '/');
            int index = Entries.FindIndex(entry => string.Equals(
                entry.Target.BuildProfileAssetPath.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && Entries[index].Key != selectedTargetKey) SelectTarget(index);
            else RefreshView();
        }

        private void OnSelectionChange()
        {
            if (SelectedEntry == null && Selection.activeObject is BuildProfile profile && !IsBuilding)
            { CustomProfile = profile; ValidateCurrent(false); }
        }

        internal void ValidateCurrent(bool reportFeedback)
        {
            var entry = SelectedEntry;
            var profile = SelectedProfile;
            Validation = ValidateBuildRequest(profile, Environment, OutputPath,
                entry?.Target.DefaultBuildOptions ?? BuildOptions.None, entry?.Target.ProjectValidation);
            DeucarianBuildControlCenterStatus.Publish(profile, entry, Validation, Entries.Count, DiscoveryIssues.Count);
            if (reportFeedback)
            {
                if (Validation.IsValid) DeucarianBuildPipelineLog.Info("Build validation passed for '" + DisplayName + "'.");
                else DeucarianBuildPipelineLog.Warning(Validation.Format("Build validation"));
            }
            SetFeedback(Validation.IsValid ? "Ready to build." : Validation.Issues.Count + " validation issue(s).",
                Validation.IsValid ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning);
        }

        internal static DeucarianBuildValidationResult ValidateBuildRequest(BuildProfile profile,
            DeucarianBuildEnvironment environment, string outputPath, BuildOptions buildOptions,
            Func<DeucarianBuildValidationResult> projectValidation) =>
            DeucarianBuildDispatcher.ValidateRequest(new DeucarianBuildRequest(profile, environment,
                outputPath, buildOptions), projectValidation, requireProjectRelativeOutput: true);

        internal static DeucarianBuildResult DispatchBuild(DeucarianBuildManagerProviderEntry entry,
            BuildProfile customBuildProfile, DeucarianBuildEnvironment customBuildEnvironment,
            string customBuildOutputPath) => entry != null
                ? DeucarianBuildDispatcher.BuildDefault(entry.Target, DeucarianBuildInvocationSource.BuildPipelineManager)
                : DeucarianBuildRunner.Build(new DeucarianBuildRequest(
                    customBuildProfile, customBuildEnvironment, customBuildOutputPath));

        internal void SetFeedback(string message, DeucarianEditorStatus status)
        { Feedback = message ?? string.Empty; FeedbackStatus = status; RefreshView(); }
        internal void RefreshView() { view?.Refresh(); Repaint(); }

        private void HandleProjectChanged()
        {
            projectChangeRefreshAt = EditorApplication.timeSinceStartup + ProjectChangeDebounceSeconds;
            if (projectChangeRefreshPending) return;
            projectChangeRefreshPending = true;
            EditorApplication.update += ProcessProjectChangeRefresh;
        }

        private void ProcessProjectChangeRefresh()
        {
            if (!projectChangeRefreshPending || IsBuilding ||
                EditorApplication.timeSinceStartup < projectChangeRefreshAt) return;
            CancelProjectChangeRefresh();
            if (this != null) RefreshDiscovery();
        }

        private void CancelProjectChangeRefresh()
        { projectChangeRefreshPending = false; EditorApplication.update -= ProcessProjectChangeRefresh; }
        internal void QueueProjectChangeRefreshForTests() => HandleProjectChanged();
        internal void FlushProjectChangeRefreshForTests()
        { projectChangeRefreshAt = double.MinValue; ProcessProjectChangeRefresh(); }
    }
}
