using UnityEngine;
using Tokens = Convai.Editor.UI.ConvaiEditorTokens;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The status-chip vocabulary of the Convai editor design system: the small pill in the
    ///     top-right of every Convai header, and the words it is allowed to say.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>A chip answers one question, and which question depends on the mode.</b> Out of Play
    ///         mode it reports <em>setup health</em> — is this component ready to work?
    ///         (<see cref="Ready" />, <see cref="NeedsAttention" />, <see cref="NotSetUp" />,
    ///         <see cref="ActionNeeded" />.) In Play mode it reports <em>runtime state</em> — what is it
    ///         doing right now? (<see cref="Live" />, <see cref="Idle" />, <see cref="Inactive" />.)
    ///         Nothing else belongs in a chip.
    ///     </para>
    ///     <para>
    ///         This table exists because eight surfaces had settled on a chip reading "Editor" for the
    ///         not-playing state while their siblings said "Ready", so the same state had two names
    ///         depending on which component you selected. "Editor" was also the least useful thing a
    ///         chip could say: it told the user the mode they were already in, and said nothing about
    ///         whether the component would actually work when they pressed Play. Every chip now
    ///         carries information.
    ///     </para>
    ///     <para>
    ///         Each entry pairs the label with its tint, because a chip whose colour disagrees with its
    ///         word is worse than no chip — an amber "Ready" reads as a rendering fault. Use
    ///         <see cref="ConvaiEditorChip.Content" /> and <see cref="ConvaiEditorChip.Tint" /> from a
    ///         Convai editor's <c>StatusChip</c> and <c>StatusChipTint</c> overrides.
    ///     </para>
    ///     <para>
    ///         A module that genuinely needs a state this table cannot express should add it here with
    ///         a sentence saying what it means, rather than spelling a one-off label at the call site.
    ///         Contents are cached, so reading one per repaint allocates nothing.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorChips
    {
        #region Setup health — what the chip says outside Play mode

        /// <summary>Configured and healthy; it will work when the scene plays.</summary>
        internal static readonly ConvaiEditorChip Ready = new(
            "Ready", "Configured and healthy — this will work when you press Play.", Tokens.StatusReady);

        /// <summary>Configured, but something regressed and should be reviewed.</summary>
        internal static readonly ConvaiEditorChip NeedsAttention = new(
            "Needs Attention", "Configured, but something should be reviewed below.", Tokens.StatusWarn);

        /// <summary>No content or profile assigned yet — the first-run state.</summary>
        internal static readonly ConvaiEditorChip NotSetUp = new(
            "Not Set Up", "This component has nothing assigned yet.", Tokens.StatusIdle);

        /// <summary>Something must be resolved before this component can be set up at all.</summary>
        internal static readonly ConvaiEditorChip ActionNeeded = new(
            "Action Needed", "Something must be resolved before setup can run.", Tokens.StatusError);

        #endregion

        #region Runtime state — what the chip says in Play mode

        /// <summary>Running and doing its job.</summary>
        internal static readonly ConvaiEditorChip Live = new(
            "Live", "Running now.", Tokens.AccentBright);

        /// <summary>Running, but with nothing to do at this moment.</summary>
        internal static readonly ConvaiEditorChip Idle = new(
            "Idle", "Running, but nothing is happening right now.", Tokens.StatusIdle);

        /// <summary>Playing, but this component did not start — usually a setup problem.</summary>
        internal static readonly ConvaiEditorChip Inactive = new(
            "Inactive", "The scene is playing, but this component did not start.", Tokens.StatusWarn);

        #endregion

        /// <summary>
        ///     Picks between <see cref="Live" /> and <see cref="Idle" /> for the common case of a
        ///     component that is running and either busy or not.
        /// </summary>
        internal static ConvaiEditorChip Running(bool busy) => busy ? Live : Idle;
    }

    /// <summary>One entry of the <see cref="ConvaiEditorChips" /> vocabulary: a label and its tint.</summary>
    internal readonly struct ConvaiEditorChip
    {
        internal ConvaiEditorChip(string label, string tooltip, Color tint)
        {
            Content = new GUIContent(label, tooltip);
            Tint = tint;
        }

        /// <summary>Cached label and tooltip, so reading this per repaint allocates nothing.</summary>
        internal GUIContent Content { get; }

        /// <summary>The pill tint that agrees with this label's meaning.</summary>
        internal Color Tint { get; }
    }
}
