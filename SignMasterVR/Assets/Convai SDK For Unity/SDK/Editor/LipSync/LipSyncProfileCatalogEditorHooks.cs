#if UNITY_EDITOR
using Convai.Modules.LipSync.Profiles;
using UnityEditor;

namespace Convai.Modules.LipSync.Editor
{
    [InitializeOnLoad]
    internal static class LipSyncProfileCatalogEditorHooks
    {
        static LipSyncProfileCatalogEditorHooks()
        {
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.projectChanged += Invalidate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange _) => Invalidate();

        private static void Invalidate() => LipSyncProfileCatalog.InvalidateCachesForEditor();
    }
}
#endif
