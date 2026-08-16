using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Modules.Emotion.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.Inspectors
{
    /// <summary>
    ///     Inspectors for the embodiment components a user finds on their character without having
    ///     added them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The project's rule is that infrastructure a user cannot see is infrastructure they
    ///         cannot debug (<c>EmbodimentContext.RuntimeInfrastructureHideFlags</c>), so nothing here
    ///         is hidden. The cost of that rule is components appearing unannounced, and the answer is
    ///         that each one introduces itself.
    ///     </para>
    ///     <para>
    ///         Gaze's <c>GazeAttentionRequests</c> already did this. Emotion's adapter did not — same
    ///         situation, same <c>[RequireComponent]</c>, and it presented as a nameless empty
    ///         component. These editors close that gap.
    ///     </para>
    /// </remarks>
    internal abstract class EmbodimentAutoAddedComponentEditor : ConvaiInspectorEditor
    {
        private static readonly GUIContent ChipAuto = new(
            "Added For You", "Convai added this alongside the feature that needs it.");

        /// <summary>Product name shown in the header — never the class name.</summary>
        protected abstract string DisplayTitle { get; }

        /// <summary>Which feature put it here.</summary>
        protected abstract string AddedBy { get; }

        /// <summary>The paragraph answering "what is this, and may I remove it?".</summary>
        protected abstract string WhatThisDoes { get; }

        protected sealed override string Title => DisplayTitle;

        protected sealed override string Subtitle => $"Added by {AddedBy}";

        protected sealed override GUIContent StatusChip => ChipAuto;

        protected sealed override Color StatusChipTint => ConvaiEditorTheme.StatusInfo;

        protected sealed override void DrawBody() => InfoBox("What this is", WhatThisDoes);
    }

    /// <summary>Introduces the mood adapter Emotion brings with it.</summary>
    [CustomEditor(typeof(MoodCommandHandlerAdapter))]
    internal sealed class MoodCommandHandlerAdapterEditor : EmbodimentAutoAddedComponentEditor
    {
        protected override string DisplayTitle => "Mood Requests";

        protected override string AddedBy => "Emotion";

        protected override string WhatThisDoes =>
            "Nothing to configure. It is the doorway actions and other Convai systems use to say " +
            "\"feel this way\": Set Mood and one-off emotion beats arrive here and are handed to " +
            "Emotion. Removing or disabling it leaves the character's own expression working but " +
            "stops those actions from being able to change its mood.";
    }
}
