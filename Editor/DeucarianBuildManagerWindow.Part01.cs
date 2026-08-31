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
    public sealed partial class DeucarianBuildManagerWindow
    {


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

        internal static void OpenWindowForEntry(
            DeucarianBuildManagerProviderEntry entry)
        {
            if (entry == null)
            {
                OpenWindow();
                return;
            }

            SessionState.SetString(SelectedTargetSessionKey, entry.Key);
            OpenWindow();
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
            CancelProjectChangeRefresh();
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

        private void OnFocus()
        {
            AlignSelectionWithActiveProfile();
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
            RemoveAnimatedAmbientLayer();
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
            workbench.Toolbar.AddToClassList(
                DeucarianEditorWorkbenchToolbar.StableActionLanesClass);
            DeucarianEditorCommandBarLanes lanes =
                DeucarianEditorCommandBar.CreateLanes(workbench.Toolbar);
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
            lanes.Leading.Add(targetPopup);

            toolbarSummary = lanes.Summary;
            toolbarSummary.name = "deucarian-build-pipeline-summary";

            synchronizeButton = DeucarianEditorWorkbenchToolbar.CreateActionButton(
                "Sync Profiles",
                HandleSynchronize,
                false);
            synchronizeButton.name = SyncButtonName;
            synchronizeButton.tooltip =
                "Create or refresh every Build Profile registered by this project. "
                + "This changes version-controlled profile assets.";
            AddToolbarAction(lanes.Trailing, synchronizeButton);

            applyButton = DeucarianEditorWorkbenchToolbar.CreateActionButton(
                "Apply Policy",
                HandleApplyPolicy,
                false);
            applyButton.name = ApplyButtonName;
            applyButton.tooltip =
                "Update only the selected Build Profile with the environment's required settings. "
                + "This changes the version-controlled profile asset.";
            AddToolbarAction(lanes.Trailing, applyButton);

            validateButton = DeucarianEditorWorkbenchToolbar.CreateActionButton(
                "Validate",
                HandleValidate,
                false);
            validateButton.name = ValidateButtonName;
            validateButton.tooltip =
                "Check profile settings and project preflight rules without changing assets.";
            AddToolbarAction(lanes.Trailing, validateButton);

            buildButton = DeucarianEditorWorkbenchToolbar.CreateActionButton(
                "Build",
                HandleBuild,
                true);
            buildButton.name = BuildButtonName;
            buildButton.tooltip =
                "Validate, then run the selected workflow and write to the displayed output folder.";
            AddToolbarAction(lanes.Trailing, buildButton);
        }

        private static void AddToolbarAction(VisualElement actionLane, Button button)
        {
            button.style.flexGrow = 1f;
            button.style.flexShrink = 1f;
            actionLane.Add(button);
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
            discoveryRefreshCount++;
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

        private void AlignSelectionWithActiveProfile()
        {
            BuildProfile activeProfile = BuildProfile.GetActiveBuildProfile();
            string activePath = activeProfile != null
                ? AssetDatabase.GetAssetPath(activeProfile)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(activePath))
            {
                return;
            }

            for (int i = 0; i < providerEntries.Count; i++)
            {
                DeucarianBuildManagerProviderEntry entry = providerEntries[i];
                if (!string.Equals(
                        activePath.Replace('\\', '/'),
                        entry.Target.BuildProfileAssetPath.Replace('\\', '/'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                selectedTargetKey = entry.Key;
                SessionState.SetString(SelectedTargetSessionKey, selectedTargetKey);
                if (targetPopup != null)
                {
                    targetPopup.SetValueWithoutNotify(entry.Label);
                }

                ValidateCurrent(false);
                return;
            }
        }

        private void DrawContent()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            try
            {
                DrawConfigurationPanel();
                DrawActionGuidePanel();
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

        private static void DrawActionGuidePanel()
        {
            DeucarianEditorWorkbenchGUI.DrawPanel("Actions", () =>
            {
                EditorGUILayout.LabelField(
                    "For a normal build: Validate, then Build.",
                    EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Sync Profiles — Create or refresh all profiles registered by the project; "
                    + "changes profile assets.\n"
                    + "Apply Policy — Update only the selected profile's environment settings; "
                    + "changes that profile asset.\n"
                    + "Validate — Check profile settings and project preflight without changing "
                    + "anything.\n"
                    + "Build — Validate, then create the build in the displayed output folder.",
                    MessageType.None);
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

                    if (GUILayout.Button("Open in Unity", GUILayout.Width(104f)))
                    {
                        BuildProfile.SetActiveBuildProfile(profile);
                        BuildPlayerWindow.ShowBuildPlayerWindow();
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
