using Deucarian.Editor;
using UnityEditor;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildManagerNavigation
    {
        internal static IDeucarianEditorPage CreatePage() =>
            DeucarianEditorWindowPages.Create<DeucarianBuildManagerWindow>(
                (window, content) => window.BuildView(content),
                activate: (window, route) => window.OnFocus());

        internal static void OpenWindowForEntry(DeucarianBuildManagerProviderEntry entry)
        {
            if (entry != null)
                SessionState.SetString(DeucarianBuildManagerWindow.SelectedTargetSessionKey, entry.Key);
            DeucarianBuildManagerWindow.OpenWindow();
        }
    }
}
