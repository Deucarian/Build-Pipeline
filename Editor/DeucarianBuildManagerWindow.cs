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
    public sealed class DeucarianBuildManagerWindow : EditorWindow
    {
        public const string MenuPath = DeucarianEditorUxStandards.MenuRoot + "/Build Pipeline";
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

        internal static IReadOnlyList<string> UserFacingMenuPathsForTests =>
            new[] { MenuPath };

        internal DeucarianEditorWorkbench WorkbenchForTests => workbench;
        internal DeucarianEditorWorkbenchFooter FooterForTests => footer;
        internal DeucarianBuildValidationResult ValidationForTests => currentValidation;
        internal bool BuildEnabledForTests => buildButton != null && buildButton.enabledSelf;

        [MenuItem(MenuPath)]
        public static void OpenWindow()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            DeucarianBuildManagerWindow window =
                GetWindow<DeucarianBuildManagerWindow>(WindowTitle);
            window.titleContent = DeucarianEditorIcons.GetPackageContent(
                "editor",
                WindowTitle,
                "Manage project-registered and custom build workflows.");
            window.minSize = new Vector2(640f, 440f);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            minSize = new Vector2(640f, 440f);
            EditorApplication.projectChanged -= HandleProjectChanged;
            EditorApplication.projectChanged += HandleProjectChanged;
            selectedTargetKey = SessionState.GetString(
                SelectedTargetSessionKey,
                CustomTargetKey);
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= HandleProjectChanged;
            workbench?.Dispose();
            workbench = null;
            footer = null;
            targetPopup = null;
            toolbarSummary = null;
            synchronizeButton = null;
            applyButton = null;
            validateButton = null;
            buildButton = null;
        }

        public void CreateGUI()
        {
            workbench?.Dispose();
            workbench = DeucarianEditorWorkbench.Create(
                rootVisualElement,
                new DeucarianEditorWorkbenchOptions
                {
                    IncludeToolbar = true,
                    IncludeFooter = true,
                    TopSafeFadeName = WallpaperFadeName
                });
            if (workbench.Toolbar == null || workbench.Content == null)
            {
                return;
            }

            BuildToolbar();
            IMGUIContainer content = workbench.AddImGuiContent(DrawContent, ContentName);
            content.style.flexGrow = 1f;
            content.style.minHeight = 0f;
            BuildFooter();
            RefreshDiscovery();
        }

        private void BuildToolbar()
        {
            workbench.Toolbar.Clear();
            if (targetLabels.Count == 0)
            {
                targetLabels.Add(CustomTargetLabel);
            }

            targetPopup = new PopupField<string>(targetLabels, 0)
            {
                name = TargetPopupName,
                tooltip = "Choose a project-registered workflow or a custom Build Profile."
            };
            targetPopup.style.minWidth = 260f;
            targetPopup.style.maxWidth = 360f;
            targetPopup.RegisterValueChangedCallback(HandleTargetChanged);
            workbench.Toolbar.Add(targetPopup);

            toolbarSummary = DeucarianEditorWorkbenchToolbar.CreateSummary(string.Empty);
            toolbarSummary.name = "deucarian-build-pipeline-summary";
            workbench.Toolbar.Add(toolbarSummary);
            workbench.Toolbar.Add(DeucarianEditorWorkbenchToolbar.CreateSpacer());

            synchronizeButton = DeucarianEditorWorkbenchToolbar.CreateActionButton(
                "Synchronize",
                HandleSynchronize,
                false);
            synchronizeButton.name = SyncButtonName;
            synchronizeButton.tooltip =
                "Create or synchronize the selected project's version-controlled Build Profiles.";
            workbench.Toolbar.Add(synchronizeButton);

            applyButton = DeucarianEditorWorkbenchToolbar.CreateActionButton(
                "Apply Policy",
                HandleApplyPolicy,
                false);
            applyButton.name = ApplyButtonName;
            applyButton.tooltip =
                "Explicitly apply the selected environment policy to the Build Profile.";
            workbench.Toolbar.Add(applyButton);

            validateButton = DeucarianEditorWorkbenchToolbar.CreateActionButton(
                "Validate",
                HandleValidate,
                false);
            validateButton.name = ValidateButtonName;
            validateButton.tooltip =
                "Validate profile drift and project-specific preflight rules.";
            workbench.Toolbar.Add(validateButton);

            buildButton = DeucarianEditorWorkbenchToolbar.CreateActionButton(
                "Build",
                HandleBuild,
                true);
            buildButton.name = BuildButtonName;
            buildButton.tooltip =
                "Run the selected workflow after validation succeeds.";
            workbench.Toolbar.Add(buildButton);
        }

        private void BuildFooter()
        {
            if (workbench.Footer == null)
            {
                return;
            }

            workbench.Footer.Clear();
            footer = DeucarianEditorWorkbenchSurfaces.CreateFooter(
                string.Empty,
                "Ready",
                feedbackMessage,
                "Open Output",
                HandleOpenOutput,
                "com.deucarian.build-pipeline " + DeucarianBuildPackage.Version);
            footer.Root.name = FooterName;
            footer.Action.tooltip =
                "Reveal the selected build output in the file browser.";
            workbench.Footer.Add(footer.Root);
        }

        private void RefreshDiscovery()
        {
            DeucarianBuildManagerDiscoveryResult discovery =
                DeucarianBuildManagerDiscovery.Discover();
            providerEntries.Clear();
            providerEntries.AddRange(discovery.Entries);
            discoveryIssues.Clear();
            discoveryIssues.AddRange(discovery.Issues);

            targetLabels.Clear();
            for (int i = 0; i < providerEntries.Count; i++)
            {
                targetLabels.Add(providerEntries[i].Label);
            }

            targetLabels.Add(CustomTargetLabel);
            int selectedIndex = FindSelectedIndex(selectedTargetKey);
            if (selectedIndex < 0)
            {
                selectedIndex = providerEntries.Count > 0 ? 0 : targetLabels.Count - 1;
                selectedTargetKey = KeyAt(selectedIndex);
            }

            if (targetPopup != null)
            {
                targetPopup.choices = targetLabels;
                targetPopup.SetValueWithoutNotify(targetLabels[selectedIndex]);
            }

            SessionState.SetString(SelectedTargetSessionKey, selectedTargetKey);
            ValidateCurrent(false);
            Repaint();
        }

        private void HandleTargetChanged(ChangeEvent<string> change)
        {
            int index = targetLabels.IndexOf(change.newValue);
            if (index < 0)
            {
                return;
            }

            selectedTargetKey = KeyAt(index);
            SessionState.SetString(SelectedTargetSessionKey, selectedTargetKey);
            lastBuildResult = null;
            ValidateCurrent(false);
            Repaint();
        }

        private void DrawContent()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            try
            {
                DrawConfigurationPanel();
                DrawValidationPanel();
                DrawLastBuildPanel();
                DrawDiscoveryIssues();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawConfigurationPanel()
        {
            DeucarianEditorWorkbenchGUI.DrawPanel("Configuration", () =>
            {
                DeucarianBuildManagerProviderEntry entry = SelectedEntry;
                if (entry != null)
                {
                    DeucarianBuildManagerTarget target = entry.Target;
                    DeucarianEditorWorkbenchGUI.DrawKeyValueRow(
                        "Provider",
                        entry.Provider.DisplayName);
                    DrawRegisteredProfile(target.BuildProfileAssetPath);
                    DeucarianEditorWorkbenchGUI.DrawKeyValueRow(
                        "Environment",
                        target.Environment.ToString());
                    DeucarianEditorWorkbenchGUI.DrawKeyValueRow("Output", target.OutputPath);
                    if (!string.IsNullOrWhiteSpace(target.Description))
                    {
                        GUILayout.Space(4f);
                        EditorGUILayout.LabelField(
                            target.Description,
                            DeucarianEditorWorkbenchGUI.MutedMiniLabelStyle);
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    customProfile = DeucarianEditorFields.DrawAssetFieldWithSelectButton(
                        "Build Profile",
                        customProfile);
                    customEnvironment = (DeucarianBuildEnvironment)EditorGUILayout.EnumPopup(
                        "Environment",
                        customEnvironment);
                    customOutputPath = EditorGUILayout.TextField("Output", customOutputPath);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ValidateCurrent(false);
                    }
                }
            });
        }

        private static void DrawRegisteredProfile(string assetPath)
        {
            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(assetPath);
            EditorGUILayout.BeginHorizontal();
            try
            {
                DeucarianEditorFields.DrawReadonlyTextField("Build Profile", assetPath);
                using (new EditorGUI.DisabledScope(profile == null))
                {
                    if (GUILayout.Button("Select", GUILayout.Width(72f)))
                    {
                        Selection.activeObject = profile;
                        EditorGUIUtility.PingObject(profile);
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawValidationPanel()
        {
            DeucarianEditorWorkbenchGUI.DrawPanel("Validation", () =>
            {
                bool valid = currentValidation != null && currentValidation.IsValid;
                DeucarianEditorWorkbenchGUI.DrawStatusRow(
                    valid ? "✓" : "!",
                    valid ? "Ready to build" : "Action required",
                    valid ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning);
                if (valid)
                {
                    EditorGUILayout.LabelField(
                        "The profile policy and project preflight are valid.",
                        DeucarianEditorWorkbenchGUI.MutedMiniLabelStyle);
                    return;
                }

                if (currentValidation == null || currentValidation.Issues.Count == 0)
                {
                    EditorGUILayout.HelpBox("Select a build target.", MessageType.Info);
                    return;
                }

                for (int i = 0; i < currentValidation.Issues.Count; i++)
                {
                    EditorGUILayout.HelpBox(currentValidation.Issues[i], MessageType.Warning);
                }
            });
        }

        private void DrawLastBuildPanel()
        {
            DeucarianEditorWorkbenchGUI.DrawPanel("Last Build", () =>
            {
                DeucarianBuildArtifactManifest manifest =
                    lastBuildResult != null ? lastBuildResult.ArtifactManifest : null;
                if (manifest == null)
                {
                    EditorGUILayout.LabelField(
                        "No build has completed in this manager session.",
                        DeucarianEditorWorkbenchGUI.MutedMiniLabelStyle);
                    return;
                }

                DeucarianEditorWorkbenchGUI.DrawKeyValueRow("Environment", manifest.environment);
                DeucarianEditorWorkbenchGUI.DrawKeyValueRow("Build GUID", manifest.buildGuid);
                DeucarianEditorWorkbenchGUI.DrawKeyValueRow(
                    "Duration",
                    manifest.durationSeconds.ToString("0.0") + " s");
                DeucarianEditorWorkbenchGUI.DrawKeyValueRow(
                    "Encoded bootstrap",
                    FormatBytes(manifest.budget.encodedBootstrapBytes));
                DeucarianEditorWorkbenchGUI.DrawStatusRow(
                    manifest.budget.passed ? "✓" : "!",
                    manifest.budget.passed ? "Budget passed" : "Budget failed",
                    manifest.budget.passed
                        ? DeucarianEditorStatus.Success
                        : DeucarianEditorStatus.Error);
            });
        }

        private void DrawDiscoveryIssues()
        {
            if (discoveryIssues.Count == 0)
            {
                return;
            }

            DeucarianEditorWorkbenchGUI.DrawPanel("Provider Diagnostics", () =>
            {
                for (int i = 0; i < discoveryIssues.Count; i++)
                {
                    EditorGUILayout.HelpBox(discoveryIssues[i], MessageType.Warning);
                }
            });
        }

        private void HandleSynchronize()
        {
            DeucarianBuildManagerProviderEntry entry = SelectedEntry;
            if (entry == null || !entry.Provider.CanSynchronize)
            {
                SetFeedback("The selected target does not provide profile synchronization.",
                    DeucarianEditorStatus.Info);
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Synchronize Build Profiles",
                    "This explicitly updates project-owned Build Profile assets. Review and commit their diffs.",
                    "Synchronize",
                    "Cancel"))
            {
                return;
            }

            try
            {
                entry.Provider.Synchronize();
                DeucarianBuildPipelineLog.Info(
                    "Synchronized build provider '" + entry.Provider.Id + "'.");
                SetFeedback("Build Profiles synchronized. Review and commit their diffs.",
                    DeucarianEditorStatus.Success);
                RefreshDiscovery();
            }
            catch (Exception exception)
            {
                HandleActionFailure("Profile synchronization failed", exception);
            }
        }

        private void HandleApplyPolicy()
        {
            BuildProfile profile = SelectedProfile;
            if (profile == null)
            {
                SetFeedback("The selected Build Profile is missing.", DeucarianEditorStatus.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Apply Build Policy",
                    "This explicitly updates a version-controlled Build Profile. Review and commit its diff.",
                    "Apply Policy",
                    "Cancel"))
            {
                return;
            }

            try
            {
                DeucarianBuildRunner.ApplyPolicy(profile, SelectedEnvironment);
                DeucarianBuildPipelineLog.Info(
                    "Applied the " + SelectedEnvironment + " policy to '"
                    + AssetDatabase.GetAssetPath(profile) + "'.");
                SetFeedback("Policy applied. Review and commit the Build Profile diff.",
                    DeucarianEditorStatus.Success);
                ValidateCurrent(false);
            }
            catch (Exception exception)
            {
                HandleActionFailure("Policy application failed", exception);
            }
        }

        private void HandleValidate()
        {
            ValidateCurrent(true);
        }

        private void HandleBuild()
        {
            ValidateCurrent(false);
            if (currentValidation == null || !currentValidation.IsValid)
            {
                SetFeedback("Build blocked until validation succeeds.",
                    DeucarianEditorStatus.Warning);
                return;
            }

            isBuilding = true;
            UpdateActionState();
            SetFeedback("Build in progress…", DeucarianEditorStatus.Info, true);
            try
            {
                DeucarianBuildResult result = DispatchBuild(
                    SelectedEntry,
                    customProfile,
                    customEnvironment,
                    customOutputPath);
                if (result == null || result.BuildReport == null || result.ArtifactManifest == null)
                {
                    throw new InvalidOperationException(
                        "The build workflow completed without a Deucarian build result.");
                }

                lastBuildResult = result;
                DeucarianBuildPipelineLog.Info(
                    "Build completed for '" + CurrentDisplayName + "'.");
                SetFeedback("Build completed successfully.", DeucarianEditorStatus.Success);
            }
            catch (Exception exception)
            {
                HandleActionFailure("Build failed", exception);
            }
            finally
            {
                isBuilding = false;
                UpdateActionState();
                Repaint();
            }
        }

        private void HandleOpenOutput()
        {
            string fullPath;
            try
            {
                fullPath = DeucarianBuildPathUtility.ToFullOutputPath(SelectedOutputPath);
            }
            catch
            {
                return;
            }

            if (Directory.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
            }
        }

        private void ValidateCurrent(bool reportFeedback)
        {
            DeucarianBuildValidationResult result = new DeucarianBuildValidationResult();
            BuildProfile profile = SelectedProfile;
            if (profile == null)
            {
                result.Add("The selected Build Profile is missing.");
            }
            else
            {
                try
                {
                    IDeucarianPlatformBuildPolicy policy = DeucarianBuildRunner.GetPolicy(profile);
                    result.AddRange(policy.ValidateProfile(profile, SelectedEnvironment).Issues);
                }
                catch (Exception exception)
                {
                    result.Add(exception.GetBaseException().Message);
                }
            }

            if (string.IsNullOrWhiteSpace(SelectedOutputPath))
            {
                result.Add("A build output path is required.");
            }
            else if (Path.IsPathRooted(SelectedOutputPath))
            {
                result.Add("The build output path must be project-relative.");
            }

            DeucarianBuildManagerProviderEntry entry = SelectedEntry;
            if (entry != null && entry.Target.ProjectValidation != null)
            {
                try
                {
                    DeucarianBuildValidationResult projectResult =
                        entry.Target.ProjectValidation();
                    if (projectResult == null)
                    {
                        result.Add("The project validation callback returned no result.");
                    }
                    else
                    {
                        result.AddRange(projectResult.Issues);
                    }
                }
                catch (Exception exception)
                {
                    result.Add("Project validation failed: "
                               + exception.GetBaseException().Message);
                }
            }

            currentValidation = result;
            if (reportFeedback)
            {
                if (result.IsValid)
                {
                    DeucarianBuildPipelineLog.Info(
                        "Build validation passed for '" + CurrentDisplayName + "'.");
                    SetFeedback("Validation passed.", DeucarianEditorStatus.Success);
                }
                else
                {
                    DeucarianBuildPipelineLog.Warning(result.Format("Build validation"));
                    SetFeedback("Validation found " + result.Issues.Count + " issue(s).",
                        DeucarianEditorStatus.Warning);
                }
            }
            else if (result.IsValid)
            {
                SetFeedback("Ready to build.", DeucarianEditorStatus.Success);
            }
            else
            {
                SetFeedback(result.Issues.Count + " validation issue(s).",
                    DeucarianEditorStatus.Warning);
            }

            UpdateActionState();
            Repaint();
        }

        private void UpdateActionState()
        {
            DeucarianBuildManagerProviderEntry entry = SelectedEntry;
            BuildProfile profile = SelectedProfile;
            synchronizeButton?.SetEnabled(
                !isBuilding && entry != null && entry.Provider.CanSynchronize);
            applyButton?.SetEnabled(!isBuilding && profile != null);
            validateButton?.SetEnabled(!isBuilding);
            buildButton?.SetEnabled(
                !isBuilding && currentValidation != null && currentValidation.IsValid);
            if (toolbarSummary != null)
            {
                toolbarSummary.text = SelectedEnvironment + " · " + SelectedOutputPath;
            }

            if (footer?.Action != null)
            {
                bool outputExists = false;
                try
                {
                    outputExists = Directory.Exists(
                        DeucarianBuildPathUtility.ToFullOutputPath(SelectedOutputPath));
                }
                catch
                {
                    // Invalid paths are already surfaced by validation.
                }

                footer.Action.SetEnabled(!isBuilding && outputExists);
            }
        }

        private void SetFeedback(
            string message,
            DeucarianEditorStatus status,
            bool busy = false)
        {
            feedbackMessage = message ?? string.Empty;
            feedbackStatus = status;
            if (footer != null)
            {
                footer.StatusLabel.text = status == DeucarianEditorStatus.Success
                    ? "Ready"
                    : status == DeucarianEditorStatus.Error
                        ? "Failed"
                        : status == DeucarianEditorStatus.Warning
                            ? "Review"
                            : busy ? "Building" : "Status";
                footer.Summary.text = feedbackMessage;
                footer.StatusIcon.text = status == DeucarianEditorStatus.Error
                    || status == DeucarianEditorStatus.Warning
                    ? "!"
                    : status == DeucarianEditorStatus.Success ? "✓" : "·";
                DeucarianEditorWorkbenchSurfaces.SetFooterStatus(footer, status);
                DeucarianEditorWorkbenchSurfaces.SetFooterBusy(footer, busy);
            }
        }

        private void HandleActionFailure(string heading, Exception exception)
        {
            string message = heading + ": " + exception.GetBaseException().Message;
            DeucarianBuildPipelineLog.Error(message);
            SetFeedback(message, DeucarianEditorStatus.Error);
            UpdateActionState();
            Repaint();
        }

        private void HandleProjectChanged()
        {
            RefreshDiscovery();
        }

        private void OnSelectionChange()
        {
            if (SelectedEntry == null && Selection.activeObject is BuildProfile profile)
            {
                customProfile = profile;
                ValidateCurrent(false);
            }
        }

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

        private int FindSelectedIndex(string key)
        {
            if (string.Equals(key, CustomTargetKey, StringComparison.Ordinal))
            {
                return providerEntries.Count;
            }

            for (int i = 0; i < providerEntries.Count; i++)
            {
                if (string.Equals(providerEntries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private string KeyAt(int index)
        {
            return index >= 0 && index < providerEntries.Count
                ? providerEntries[index].Key
                : CustomTargetKey;
        }

        private static string FormatBytes(long bytes)
        {
            return (bytes / (1024d * 1024d)).ToString("0.00") + " MiB";
        }

        internal static DeucarianBuildResult DispatchBuild(
            DeucarianBuildManagerProviderEntry entry,
            BuildProfile customBuildProfile,
            DeucarianBuildEnvironment customBuildEnvironment,
            string customBuildOutputPath)
        {
            return entry != null
                ? entry.Target.BuildAction()
                : DeucarianBuildRunner.Build(new DeucarianBuildRequest(
                    customBuildProfile,
                    customBuildEnvironment,
                    customBuildOutputPath));
        }
    }
}
