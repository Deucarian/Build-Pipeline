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


        private void CancelProjectChangeRefresh()
        {
            projectChangeRefreshPending = false;
            EditorApplication.update -= ProcessProjectChangeRefresh;
        }

        private void RemoveAnimatedAmbientLayer()
        {
            VisualElement ambientLayer = rootVisualElement.Q<VisualElement>(
                DeucarianEditorAmbientGlass.AmbientLayerName);
            ambientLayer?.RemoveFromHierarchy();
        }

        internal void QueueProjectChangeRefreshForTests()
        {
            HandleProjectChanged();
        }

        internal void FlushProjectChangeRefreshForTests()
        {
            projectChangeRefreshAt = double.MinValue;
            ProcessProjectChangeRefresh();
        }

        private void OnSelectionChange()
        {
            if (SelectedEntry == null && Selection.activeObject is BuildProfile profile)
            {
                customProfile = profile;
                ValidateCurrent(false);
            }
        }

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
                ? DeucarianBuildDispatcher.BuildDefault(
                    entry.Target,
                    DeucarianBuildInvocationSource.BuildPipelineManager)
                : DeucarianBuildRunner.Build(new DeucarianBuildRequest(
                    customBuildProfile,
                    customBuildEnvironment,
                    customBuildOutputPath));
        }
    }
}
