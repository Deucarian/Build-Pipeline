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
                    "Sync Build Profiles",
                    "This explicitly updates project-owned Build Profile assets. Review and commit their diffs.",
                    "Sync Profiles",
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
            DeucarianBuildManagerProviderEntry entry = SelectedEntry;
            BuildProfile profile = SelectedProfile;
            DeucarianBuildValidationResult result = ValidateBuildRequest(
                profile,
                SelectedEnvironment,
                SelectedOutputPath,
                entry != null
                    ? entry.Target.DefaultBuildOptions
                    : BuildOptions.None,
                entry?.Target.ProjectValidation);

            currentValidation = result;
            DeucarianBuildControlCenterStatus.Publish(
                profile,
                entry,
                result,
                providerEntries.Count,
                discoveryIssues.Count);
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

        internal static DeucarianBuildValidationResult ValidateBuildRequest(
            BuildProfile profile,
            DeucarianBuildEnvironment environment,
            string outputPath,
            BuildOptions buildOptions,
            Func<DeucarianBuildValidationResult> projectValidation)
        {
            return DeucarianBuildDispatcher.ValidateRequest(
                new DeucarianBuildRequest(
                    profile,
                    environment,
                    outputPath,
                    buildOptions),
                projectValidation,
                requireProjectRelativeOutput: true);
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
            projectChangeRefreshAt =
                EditorApplication.timeSinceStartup + ProjectChangeDebounceSeconds;
            if (projectChangeRefreshPending)
            {
                return;
            }

            projectChangeRefreshPending = true;
            EditorApplication.update -= ProcessProjectChangeRefresh;
            EditorApplication.update += ProcessProjectChangeRefresh;
        }

        private void ProcessProjectChangeRefresh()
        {
            if (!projectChangeRefreshPending
                || EditorApplication.timeSinceStartup < projectChangeRefreshAt)
            {
                return;
            }

            CancelProjectChangeRefresh();
            if (this != null)
            {
                RefreshDiscovery();
            }
        }
    }
}
