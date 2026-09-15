using System;
using System.IO;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace Deucarian.BuildPipeline
{
    internal sealed class DeucarianBuildManagerActions
    {
        private readonly DeucarianBuildManagerWindow owner;
        internal DeucarianBuildManagerActions(DeucarianBuildManagerWindow owner) => this.owner = owner;

        internal void Synchronize()
        {
            var entry = owner.SelectedEntry;
            if (owner.IsBuilding || entry == null || !entry.Provider.CanSynchronize) return;
            if (!EditorUtility.DisplayDialog("Sync Build Profiles",
                "This explicitly updates project-owned Build Profile assets. Review and commit their diffs.",
                "Sync Profiles", "Cancel")) return;
            try
            {
                entry.Provider.Synchronize();
                DeucarianBuildPipelineLog.Info("Synchronized build provider '" + entry.Provider.Id + "'.");
                owner.RefreshDiscovery();
            }
            catch (Exception exception) { Fail("Profile synchronization failed", exception); }
        }

        internal void ApplyPolicy()
        {
            var profile = owner.SelectedProfile;
            if (owner.IsBuilding || profile == null) return;
            if (!EditorUtility.DisplayDialog("Apply Build Policy",
                "This explicitly updates a version-controlled Build Profile. Review and commit its diff.",
                "Apply Policy", "Cancel")) return;
            try
            {
                DeucarianBuildRunner.ApplyPolicy(profile, owner.Environment);
                DeucarianBuildPipelineLog.Info("Applied the " + owner.Environment + " policy to '" +
                    AssetDatabase.GetAssetPath(profile) + "'.");
                owner.ValidateCurrent(false);
            }
            catch (Exception exception) { Fail("Policy application failed", exception); }
        }

        internal void Build()
        {
            if (owner.IsBuilding) return;
            owner.ValidateCurrent(false);
            if (!owner.Validation.IsValid) return;
            owner.IsBuilding = true;
            owner.SetFeedback("Build in progress…", DeucarianEditorStatus.Info);
            string output = owner.OutputPath;
            try
            {
                var result = DeucarianBuildManagerWindow.DispatchBuild(owner.SelectedEntry,
                    owner.CustomProfile, owner.CustomEnvironment, owner.CustomOutputPath);
                if (result?.BuildReport == null || result.ArtifactManifest == null)
                    throw new InvalidOperationException("The workflow completed without a Deucarian build result.");
                owner.LastBuild = result;
                owner.LastOutputPath = output;
                DeucarianBuildPipelineLog.Info("Build completed for '" + owner.DisplayName + "'.");
                owner.SetFeedback("Build completed successfully.", DeucarianEditorStatus.Success);
            }
            catch (Exception exception) { Fail("Build failed", exception); }
            finally { owner.IsBuilding = false; owner.RefreshView(); }
        }

        internal void SelectProfile()
        {
            if (owner.SelectedProfile == null) return;
            Selection.activeObject = owner.SelectedProfile;
            EditorGUIUtility.PingObject(owner.SelectedProfile);
        }

        internal void OpenUnityProfile()
        {
            if (owner.IsBuilding || owner.SelectedProfile == null) return;
            BuildProfile.SetActiveBuildProfile(owner.SelectedProfile);
            BuildPlayerWindow.ShowBuildPlayerWindow();
        }

        internal void ChooseOutput()
        {
            if (owner.IsBuilding || owner.SelectedEntry != null) return;
            string selected = EditorUtility.OpenFolderPanel("Build output folder", owner.OutputPath, string.Empty);
            if (string.IsNullOrEmpty(selected)) return;
            string project = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(selected);
            owner.CustomOutputPath = full.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(project.Length + 1).Replace('\\', '/') : full;
            owner.ValidateCurrent(false);
        }

        internal static bool OutputExists(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && Directory.Exists(DeucarianBuildPathUtility.ToFullOutputPath(path)); }
            catch (Exception) { return false; }
        }

        internal static void RevealOutput(string path)
        {
            if (OutputExists(path)) EditorUtility.RevealInFinder(DeucarianBuildPathUtility.ToFullOutputPath(path));
        }

        private void Fail(string heading, Exception exception)
        {
            string message = heading + ": " + exception.GetBaseException().Message;
            DeucarianBuildPipelineLog.Error(message);
            owner.SetFeedback(message, DeucarianEditorStatus.Error);
        }
    }
}
