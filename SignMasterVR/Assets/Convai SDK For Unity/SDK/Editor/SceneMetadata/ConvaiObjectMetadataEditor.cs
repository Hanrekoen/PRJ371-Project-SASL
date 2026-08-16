using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.SceneMetadata;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;
using Chip = Convai.Editor.UI.ConvaiEditorChip;

namespace Convai.Editor.SceneMetadata
{
    /// <summary>
    ///     Convai inspector for <see cref="ConvaiObjectMetadata" /> — lets a scene author describe one
    ///     object in plain language so Convai Characters know it exists and can talk about it, and
    ///     optionally expose live property values as dynamic context.
    /// </summary>
    [CustomEditor(typeof(ConvaiObjectMetadata))]
    internal sealed class ConvaiObjectMetadataEditor : ConvaiInspectorEditor
    {
        protected override string Title => "Scene Object Knowledge";
        protected override string Subtitle => "Convai Object Metadata";

        protected override string Purpose =>
            "Describes this object in plain language so Convai Characters know it exists and can " +
            "talk about it, and optionally tracks live property values that characters can react to.";

        protected override string EditorStateHostId => "ConvaiObjectMetadataEditor";

        protected override GUIContent StatusChip => CurrentChip.Content;
        protected override Color StatusChipTint => CurrentChip.Tint;

        private Chip CurrentChip
        {
            get
            {
                var metadata = (ConvaiObjectMetadata)target;
                if (!EditorApplication.isPlaying)
                    return metadata.IsValid ? Chips.Ready : Chips.NotSetUp;

                return metadata.IsRegistered ? Chips.Live : Chips.Inactive;
            }
        }
    }
}
