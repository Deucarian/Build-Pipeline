using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;
using Controls = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianBuildManagerView : IDisposable
    {
        private readonly DeucarianBuildManagerWindow owner;
        private readonly DeucarianEditorWorkspace workspace;
        private readonly DeucarianEditorWorkspaceForm form;
        private readonly DeucarianEditorWorkspaceForm details;
        private readonly PopupField<string> targets;
        private readonly PopupField<string> scenes;
        private readonly Label platform;
        private readonly Label heading;
        private readonly VisualElement statusIcon;
        private readonly VisualElement configuration;
        private readonly VisualElement issues;
        private readonly VisualElement customProfileRow;
        private readonly TextField output;
        private readonly Toggle development;
        private readonly Button browse;
        private readonly Button validate;
        private readonly Button synchronize;
        private readonly Button apply;
        private readonly Button selectProfile;
        private readonly Button openProfile;
        private readonly VisualElement lastRow;
        private readonly Label lastSummary;
        private readonly Button openOutput;
        private readonly Label feedback;
        private readonly Foldout sceneDetails;
        private readonly Label nextStep;
        private readonly Button chooseUnityProfile;
        private readonly DeucarianEditorSteps steps;
        internal Button BuildButton { get; }

        internal DeucarianBuildManagerView(VisualElement root, DeucarianBuildManagerWindow owner)
        {
            this.owner = owner;
            workspace = new DeucarianEditorWorkspace(root, Application.productName);
            workspace.Title.text = "Build Manager";
            workspace.Subtitle.text = "Validate Unity Build Profiles and create local application builds.";
            DeucarianEditorWorkspaceNavigation.Populate(workspace, DeucarianToolIds.BuildManager);
            targets = new PopupField<string>(new List<string>(owner.TargetLabels), owner.SelectedIndex)
                { name = DeucarianBuildManagerWindow.TargetPopupName };
            targets.RegisterValueChangedCallback(_ => owner.SelectTarget(targets.index));
            workspace.Scope.Add(Controls.Field("Profile", targets));
            platform = Controls.Label(string.Empty, "dw-badge");
            workspace.Scope.Add(platform);
            var scroll = Controls.Scroll(DeucarianBuildManagerWindow.ContentName);
            workspace.Content.Add(scroll);
            steps = new DeucarianEditorSteps("Choose profile", "Check settings", "Build locally");
            steps.Root.name = "build-workflow-steps";
            scroll.Add(steps.Root);
            configuration = Controls.Panel("build-configuration");
            scroll.Add(configuration);
            var body = new VisualElement();
            var status = Controls.IconPanel("build-readiness", DeucarianEditorIconIds.Info, body);
            status.AddToClassList("dw-panel-flush");
            statusIcon = status.Q(className: "dw-icon");
            heading = Controls.Label(string.Empty, "dw-feature-title");
            body.Add(heading);
            nextStep = Controls.Label(string.Empty, "dw-note");
            nextStep.name = "build-next-step";
            body.Add(nextStep);
            chooseUnityProfile = Controls.Button("Open Unity Build Profiles", BuildPlayerWindow.ShowBuildPlayerWindow);
            chooseUnityProfile.name = "build-open-profiles";
            body.Add(Controls.Actions(chooseUnityProfile));
            configuration.Add(status);
            form = new DeucarianEditorWorkspaceForm(body);
            var asset = form.Asset("build-custom-profile", "Build profile", typeof(BuildProfile),
                () => owner.CustomProfile, value => { owner.CustomProfile = value as BuildProfile; owner.ValidateCurrent(false); });
            customProfileRow = asset.parent;
            output = form.Text("build-output", "Output folder", () => owner.OutputPath,
                value => { owner.CustomOutputPath = value; owner.ValidateCurrent(false); });
            output.isDelayed = true;
            browse = Controls.IconButton(string.Empty, DeucarianEditorIconIds.Folder, owner.Actions.ChooseOutput);
            browse.tooltip = "Choose an output folder inside this project";
            output.parent.Add(browse);
            development = form.Toggle("build-development", "Development build",
                () => owner.Environment == DeucarianBuildEnvironment.Development,
                value => { owner.CustomEnvironment = value ? DeucarianBuildEnvironment.Development : DeucarianBuildEnvironment.Production; owner.ValidateCurrent(false); });
            development.tooltip = "Choose the requested policy. Apply policy explicitly if this profile needs to change.";
            scenes = new PopupField<string>(new List<string> { "No scenes" }, 0) { name = "build-scenes" };
            body.Add(Controls.Field("Scenes", scenes));
            scenes.tooltip = "Enabled scenes in this build. Selecting an entry locates its asset; it does not open or change the scene.";
            scenes.RegisterValueChangedCallback(_ => LocateScene(scenes.index - 1));
            issues = new VisualElement { name = "build-validation-issues" };
            body.Add(issues);
            validate = Named(Controls.Button("Validate", () => owner.ValidateCurrent(true)), DeucarianBuildManagerWindow.ValidateButtonName);
            BuildButton = Named(Controls.Button("Build", owner.Actions.Build, true), DeucarianBuildManagerWindow.BuildButtonName);
            body.Add(Controls.EndActions(validate, BuildButton));
            feedback = Controls.Label(string.Empty, "dw-note");
            body.Add(feedback);
            configuration.Add(Controls.Divider());
            var advanced = new Foldout { text = "Advanced settings", value = false };
            advanced.AddToClassList("dw-foldout");
            configuration.Add(advanced);
            details = new DeucarianEditorWorkspaceForm(advanced);
            details.ReadOnly("build-provider", "Provider", () => owner.SelectedEntry?.Provider.DisplayName ?? "Custom");
            details.ReadOnly("build-environment", "Policy", () => owner.Environment.ToString());
            selectProfile = Controls.Button("Select profile", owner.Actions.SelectProfile);
            openProfile = Controls.Button("Open in Unity", owner.Actions.OpenUnityProfile);
            apply = Named(Controls.Button("Apply policy", owner.Actions.ApplyPolicy), DeucarianBuildManagerWindow.ApplyButtonName);
            synchronize = Named(Controls.Button("Sync profiles", owner.Actions.Synchronize), DeucarianBuildManagerWindow.SyncButtonName);
            advanced.Add(Controls.Actions(selectProfile, openProfile));
            advanced.Add(Controls.Actions(apply, synchronize));
            details.Note(() => "Apply policy and Sync profiles change project assets. Both ask before making changes.");
            sceneDetails = new Foldout { text = "Scene paths", value = false };
            sceneDetails.AddToClassList("dw-foldout");
            advanced.Add(sceneDetails);
            var guide = new Foldout { text = "How builds work", value = false, name = "build-quick-start" };
            guide.AddToClassList("dw-foldout");
            guide.Add(Controls.Label("1. Choose a registered workflow, or select a custom Unity Build Profile. The profile owns the platform and included scenes.", "dw-note"));
            guide.Add(Controls.Label("2. Check the output folder and Development build setting, then Validate. Resolve the listed issues; validation does not change project assets.", "dw-note"));
            guide.Add(Controls.Label("3. Build creates local files in the output folder. It does not upload, deploy or change API environments. Open output appears after a successful build.", "dw-note"));
            guide.Add(Controls.Label("Development build controls debugging settings, not your backend environment. Apply policy updates only the selected profile; Sync profiles updates a registered provider's profiles. Both require confirmation.", "dw-note"));
            guide.Add(Controls.Button("Read build guide", () =>
                AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath("Packages/com.deucarian.build-pipeline/README.md"))));
            scroll.Add(guide);
            lastRow = Controls.Panel("build-last");
            lastRow.AddToClassList("dw-summary-row");
            lastRow.Add(Controls.Label("Last build", "dw-field-label"));
            lastSummary = Controls.Label("No build in this session", "dw-readonly");
            lastRow.Add(lastSummary);
            openOutput = Controls.IconButton("Open output", DeucarianEditorIconIds.ExternalLink,
                () => DeucarianBuildManagerActions.RevealOutput(owner.LastOutputPath));
            lastRow.Add(openOutput);
            scroll.Add(lastRow);
            Refresh();
        }

        internal void Refresh()
        {
            targets.choices = new List<string>(owner.TargetLabels);
            targets.SetValueWithoutNotify(owner.DisplayName);
            targets.SetEnabled(!owner.IsBuilding);
            platform.text = DeucarianBuildProfileUtility.GetTarget(owner.SelectedProfile).ToString();
            Controls.Show(platform, owner.SelectedProfile != null);
            form.Refresh();
            details.Refresh();
            bool custom = owner.SelectedEntry == null;
            Controls.Show(customProfileRow, custom);
            configuration.SetEnabled(!owner.IsBuilding);
            output.SetEnabled(custom);
            browse.SetEnabled(custom);
            development.SetEnabled(custom);
            if (!custom) output.tooltip = development.tooltip = "Owned by the selected workflow provider. Choose a different profile to change its configuration.";
            bool valid = owner.Validation.IsValid;
            steps.SetCurrent(owner.SelectedProfile == null ? 0 : valid ? 2 : 1);
            nextStep.text = owner.SelectedProfile == null
                ? "Choose a workflow above or assign a custom Build Profile. Create a profile in Unity if this project has none."
                : valid ? "Ready. Build creates local files in the selected output folder; it does not deploy them."
                : "Resolve the issues below, then Validate again. Apply policy is under Advanced settings if the profile needs updating.";
            Controls.Show(chooseUnityProfile.parent, owner.SelectedProfile == null);
            var status = owner.FeedbackStatus;
            heading.text = owner.IsBuilding ? "Building…" : status == DeucarianEditorStatus.Error
                ? "Action failed" : valid ? "Ready to build" : owner.SelectedProfile == null ? "Choose a build profile" : "Action required";
            string icon = status == DeucarianEditorStatus.Error ? DeucarianEditorIconIds.Error
                : valid ? DeucarianEditorIconIds.Success : DeucarianEditorIconIds.Warning;
            statusIcon.style.backgroundImage = new StyleBackground(DeucarianEditorIcons.GetIcon(icon));
            foreach (string kind in new[] { "success", "warning", "error", "info" })
                statusIcon.EnableInClassList("dw-status-" + kind, kind == status.ToString().ToLowerInvariant());
            issues.Clear();
            foreach (string issue in owner.Validation.Issues) issues.Add(Controls.Label(issue, "dw-note"));
            foreach (string issue in owner.DiscoveryIssues) issues.Add(Controls.Label(issue, "dw-note"));
            BuildButton.SetEnabled(!owner.IsBuilding && valid);
            apply.SetEnabled(owner.SelectedProfile != null);
            synchronize.SetEnabled(owner.SelectedEntry?.Provider.CanSynchronize ?? false);
            selectProfile.SetEnabled(owner.SelectedProfile != null);
            openProfile.SetEnabled(owner.SelectedProfile != null);
            feedback.text = owner.Feedback;
            Controls.Show(feedback, status == DeucarianEditorStatus.Error || owner.IsBuilding);
            UpdateScenes();
            var result = owner.LastBuild;
            var manifest = result?.ArtifactManifest;
            lastSummary.text = manifest == null ? "No build in this session" :
                result.BuildReport.summary.result + " · " + result.BuildReport.summary.platform;
            lastSummary.tooltip = manifest == null ? string.Empty :
                manifest.durationSeconds.ToString("0.0") + " s · " + manifest.environment + " · " + manifest.buildGuid;
            Controls.Show(openOutput, manifest != null);
            openOutput.SetEnabled(!owner.IsBuilding && DeucarianBuildManagerActions.OutputExists(owner.LastOutputPath));
        }

        private EditorBuildSettingsScene[] EffectiveScenes() => owner.SelectedProfile == null
            ? Array.Empty<EditorBuildSettingsScene>() : DeucarianBuildCompatibility.GetEffectiveScenes(owner.SelectedProfile);

        private void UpdateScenes()
        {
            var enabled = EffectiveScenes().Where(scene => scene != null && scene.enabled).ToArray();
            var labels = new List<string> { enabled.Length + (enabled.Length == 1 ? " scene" : " scenes") };
            sceneDetails.Clear();
            foreach (var scene in enabled)
            {
                labels.Add(scene.path);
                sceneDetails.Add(Controls.Label(scene.path, "dw-readonly"));
            }
            scenes.choices = labels;
            scenes.SetValueWithoutNotify(labels[0]);
            scenes.SetEnabled(enabled.Length > 0);
        }

        private void LocateScene(int index)
        {
            var enabled = EffectiveScenes().Where(scene => scene != null && scene.enabled).ToArray();
            if (index >= 0 && index < enabled.Length)
            {
                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(enabled[index].path);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            }
            scenes.SetValueWithoutNotify(scenes.choices[0]);
        }

        private static Button Named(Button button, string name) { button.name = name; return button; }
        public void Dispose() => workspace.Dispose();
    }
}
