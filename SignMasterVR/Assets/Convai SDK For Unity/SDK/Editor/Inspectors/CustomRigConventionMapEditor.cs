using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Animation;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai inspector for <see cref="CustomRigConventionMap" /> — lets a user map Convai's
    ///     semantic blendshape ids to the concrete blendshape names on a custom character rig, for
    ///     rigs that don't already follow a recognised naming convention.
    /// </summary>
    [CustomEditor(typeof(CustomRigConventionMap))]
    internal sealed class CustomRigConventionMapEditor : ConvaiInspectorEditor
    {
        private CustomRigConventionMap Map => (CustomRigConventionMap)target;

        protected override string Title => "Custom Rig Convention";
        protected override string Subtitle => "Custom Rig Convention Map";
        protected override string Purpose =>
            "Maps Convai's semantic blendshape ids to the blendshape names on this character's rig. " +
            "Assign this asset when a rig uses its own naming instead of a recognised convention.";

        protected override GUIContent StatusChip
        {
            get
            {
                var blendshapes = Map.Blendshapes;
                if (blendshapes == null || blendshapes.Count == 0)
                    return Chips.NotSetUp.Content;

                for (int i = 0; i < blendshapes.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(blendshapes[i].BlendshapeName))
                        return Chips.NeedsAttention.Content;
                }

                return Chips.Ready.Content;
            }
        }

        protected override Color StatusChipTint
        {
            get
            {
                var blendshapes = Map.Blendshapes;
                if (blendshapes == null || blendshapes.Count == 0)
                    return Chips.NotSetUp.Tint;

                for (int i = 0; i < blendshapes.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(blendshapes[i].BlendshapeName))
                        return Chips.NeedsAttention.Tint;
                }

                return Chips.Ready.Tint;
            }
        }
    }
}
