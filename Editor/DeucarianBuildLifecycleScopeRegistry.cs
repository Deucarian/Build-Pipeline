using System.Collections.Generic;
using UnityEditor;

namespace Deucarian.BuildPipeline
{
    [InitializeOnLoad]
    internal static class DeucarianBuildLifecycleScopeRegistry
    {
        private static readonly List<DeucarianBuildLifecycleScopeSet> ActiveScopes =
            new List<DeucarianBuildLifecycleScopeSet>();

        static DeucarianBuildLifecycleScopeRegistry()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= RestoreBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreBeforeReload;
            EditorApplication.quitting -= RestoreBeforeQuit;
            EditorApplication.quitting += RestoreBeforeQuit;
        }

        internal static void Register(DeucarianBuildLifecycleScopeSet scopes)
        {
            if (scopes != null && !ActiveScopes.Contains(scopes))
            {
                ActiveScopes.Add(scopes);
            }
        }

        internal static void Unregister(DeucarianBuildLifecycleScopeSet scopes)
        {
            if (scopes != null)
            {
                ActiveScopes.Remove(scopes);
            }
        }

        internal static DeucarianBuildValidationResult RestoreAllForTests()
        {
            return RestoreAll();
        }

        internal static int ActiveCountForTests => ActiveScopes.Count;

        private static DeucarianBuildValidationResult RestoreAll()
        {
            DeucarianBuildValidationResult result =
                new DeucarianBuildValidationResult();
            DeucarianBuildLifecycleScopeSet[] snapshot = ActiveScopes.ToArray();
            for (int index = snapshot.Length - 1; index >= 0; index--)
            {
                result.AddRange(snapshot[index].Restore().Issues);
            }

            ActiveScopes.Clear();
            return result;
        }

        private static void RestoreBeforeReload()
        {
            RestoreAndReport("assembly reload");
        }

        private static void RestoreBeforeQuit()
        {
            RestoreAndReport("Editor shutdown");
        }

        private static void RestoreAndReport(string reason)
        {
            DeucarianBuildValidationResult result = RestoreAll();
            if (!result.IsValid)
            {
                DeucarianBuildPipelineLog.Error(
                    result.Format(
                        "Build lifecycle restoration failed before " + reason));
            }
        }
    }
}
