using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.SceneMetadata;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;
using Chip = Convai.Editor.UI.ConvaiEditorChip;

namespace Convai.Editor.SceneMetadata
{
    /// <summary>
    ///     Convai inspector for <see cref="ConvaiSceneMetadataCollector" /> — controls when the
    ///     descriptions gathered from every <see cref="ConvaiObjectMetadata" /> in the scene are sent
    ///     to the room so Convai Characters can reference them.
    /// </summary>
    [CustomEditor(typeof(ConvaiSceneMetadataCollector))]
    internal sealed class ConvaiSceneMetadataCollectorEditor : ConvaiInspectorEditor
    {
        protected override string Title => "Scene Knowledge Collector";
        protected override string Subtitle => "Convai Scene Metadata Collector";

        protected override string Purpose =>
            "Gathers the descriptions from every Convai Object Metadata component in the scene and " +
            "sends them to the room so Convai Characters know what is around them.";

        protected override string EditorStateHostId => "ConvaiSceneMetadataCollectorEditor";

        protected override GUIContent StatusChip => CurrentChip?.Content;
        protected override Color StatusChipTint => CurrentChip?.Tint ?? Chips.Live.Tint;

        private Chip? CurrentChip
        {
            get
            {
                if (!EditorApplication.isPlaying)
                    return null;

                var collector = (ConvaiSceneMetadataCollector)target;
                return collector.isActiveAndEnabled ? Chips.Live : Chips.Inactive;
            }
        }
    }
}
