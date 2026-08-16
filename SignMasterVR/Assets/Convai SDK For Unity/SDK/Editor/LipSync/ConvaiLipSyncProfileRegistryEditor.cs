#if UNITY_EDITOR
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.LipSync.Profiles;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;

namespace Convai.Modules.LipSync.Editor
{
    /// <summary>
    ///     Convai inspector for <see cref="ConvaiLipSyncProfileRegistry" /> — groups a set of lip
    ///     sync profile assets and sets the merge order they contribute to the catalog with.
    /// </summary>
    [CustomEditor(typeof(ConvaiLipSyncProfileRegistry))]
    internal sealed class ConvaiLipSyncProfileRegistryEditor : ConvaiInspectorEditor
    {
        private ConvaiLipSyncProfileRegistry Registry => (ConvaiLipSyncProfileRegistry)target;

        protected override string Title => "Lip Sync Profile Registry";
        protected override string Subtitle => "Lip Sync Profile Registry Asset";
        protected override string Purpose =>
            "A set of lip sync profiles Convai should load together. If two registries offer a " +
            "profile with the same name, the one with the higher priority below wins.";

        protected override GUIContent StatusChip =>
            Registry.Profiles.Count == 0 ? Chips.NotSetUp.Content : Chips.Ready.Content;

        protected override Color StatusChipTint =>
            Registry.Profiles.Count == 0 ? Chips.NotSetUp.Tint : Chips.Ready.Tint;
    }
}
#endif
