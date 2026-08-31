using System;
using System.Collections.Generic;
using System.IO;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.BuildPipeline
{
    public sealed partial class DeucarianBuildManagerWindow : EditorWindow
    {
        public const string MenuPath = DeucarianEditorUxStandards.MenuRoot + "/Build Manager...";
        public const string WindowTitle = "Build Pipeline Manager";

        internal const string CustomTargetKey = "__custom__";
        internal const string TargetPopupName = "deucarian-build-pipeline-target";
        internal const string SyncButtonName = "deucarian-build-pipeline-sync";
        internal const string ApplyButtonName = "deucarian-build-pipeline-apply";
        internal const string ValidateButtonName = "deucarian-build-pipeline-validate";
        internal const string BuildButtonName = "deucarian-build-pipeline-build";
        internal const string ContentName = "deucarian-build-pipeline-content";
        internal const string FooterName = "deucarian-build-pipeline-footer";

        private const string SelectedTargetSessionKey =
            "Deucarian.BuildPipeline.Manager.SelectedTarget";
        private const string WallpaperFadeName = "deucarian-build-pipeline-top-safe-fade";
        private const string CustomTargetLabel = "Custom Build Profile";
        private const double ProjectChangeDebounceSeconds = 0.35d;

        private readonly List<DeucarianBuildManagerProviderEntry> providerEntries =
            new List<DeucarianBuildManagerProviderEntry>();
        private readonly List<string> discoveryIssues = new List<string>();
        private readonly List<string> targetLabels = new List<string>();

        private DeucarianEditorWorkbench workbench;
        private DeucarianEditorWorkbenchFooter footer;
        private PopupField<string> targetPopup;
        private Label toolbarSummary;
        private Button synchronizeButton;
        private Button applyButton;
        private Button validateButton;
        private Button buildButton;
        private Vector2 scrollPosition;
        private string selectedTargetKey = CustomTargetKey;
        private BuildProfile customProfile;
        private DeucarianBuildEnvironment customEnvironment =
            DeucarianBuildEnvironment.Development;
        private string customOutputPath = "Builds";
        private DeucarianBuildValidationResult currentValidation =
            new DeucarianBuildValidationResult();
        private DeucarianBuildResult lastBuildResult;
        private string feedbackMessage = "Select a build target.";
        private DeucarianEditorStatus feedbackStatus = DeucarianEditorStatus.Info;
        private bool isBuilding;
        private bool projectChangeRefreshPending;
        private double projectChangeRefreshAt;
        private int discoveryRefreshCount;

        internal static IReadOnlyList<string> UserFacingMenuPathsForTests =>
            new[] { MenuPath };

        internal DeucarianEditorWorkbench WorkbenchForTests => workbench;
        internal DeucarianEditorWorkbenchFooter FooterForTests => footer;
        internal DeucarianBuildValidationResult ValidationForTests => currentValidation;
        internal bool BuildEnabledForTests => buildButton != null && buildButton.enabledSelf;
        internal bool HasAmbientAnimationForTests =>
            rootVisualElement.Q<VisualElement>(
                DeucarianEditorAmbientGlass.AmbientLayerName) != null;
        internal bool ProjectChangeRefreshPendingForTests => projectChangeRefreshPending;
        internal int DiscoveryRefreshCountForTests => discoveryRefreshCount;

        private DeucarianBuildManagerProviderEntry SelectedEntry
        {
            get
            {
                for (int i = 0; i < providerEntries.Count; i++)
                {
                    if (string.Equals(
                            providerEntries[i].Key,
                            selectedTargetKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return providerEntries[i];
                    }
                }

                return null;
            }
        }

        private BuildProfile SelectedProfile
        {
            get
            {
                DeucarianBuildManagerProviderEntry entry = SelectedEntry;
                return entry != null
                    ? AssetDatabase.LoadAssetAtPath<BuildProfile>(
                        entry.Target.BuildProfileAssetPath)
                    : customProfile;
            }
        }

        private DeucarianBuildEnvironment SelectedEnvironment =>
            SelectedEntry != null ? SelectedEntry.Target.Environment : customEnvironment;

        private string SelectedOutputPath =>
            SelectedEntry != null ? SelectedEntry.Target.OutputPath : customOutputPath;

        private string CurrentDisplayName =>
            SelectedEntry != null ? SelectedEntry.Label : CustomTargetLabel;
    }
}
