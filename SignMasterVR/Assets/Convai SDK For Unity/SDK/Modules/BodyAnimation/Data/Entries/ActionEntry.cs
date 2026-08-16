using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>Which part of the skeleton an action/gesture drives while it plays.</summary>
    public enum ActionMaskMode
    {
        /// <summary>Whole skeleton. Locomotion input is suspended while the action plays.</summary>
        FullBody = 0,

        /// <summary>Upper body only (set's upper-body mask). Stacks over idle, talk, and movement.</summary>
        UpperBody = 1,

        /// <summary>A custom <see cref="AvatarMask" /> supplied on the entry.</summary>
        CustomMask = 2
    }

    /// <summary>How the action's main clip repeats.</summary>
    public enum ActionLoopMode
    {
        /// <summary>Play the main clip once, then blend out (or into the outro).</summary>
        PlayOnce = 0,

        /// <summary>Repeat the main clip <see cref="ActionEntry.LoopCount" /> times.</summary>
        LoopCount = 1,

        /// <summary>Loop the main clip until <c>StopAction</c> is called (or a timeout fires).</summary>
        HoldUntilStopped = 2
    }

    /// <summary>
    ///     One named action or gesture the backend (or game code) can trigger, e.g.
    ///     <c>"dance"</c>, <c>"clap"</c>, <c>"think"</c>. Supports a single clip or an
    ///     intro/loop/outro triplet, per-entry masking, loop policies, and interruption rules.
    /// </summary>
    [Serializable]
    public sealed class ActionEntry
    {
        [Header("Identity")]
        [SerializeField]
        [Tooltip("Primary action name. Matching is case-insensitive; spaces/underscores are equivalent.")]
        private string _actionName;

        [SerializeField]
        [Tooltip("Alternative names that resolve to this entry (e.g. \"hello\" for \"wave\").")]
        private List<string> _aliases = new();

        [Header("Clips")]
        [SerializeField]
        [Tooltip("Main clip. Loops per Loop Mode when intro/outro are present or looping is requested.")]
        private AnimationClip _clip;

        [SerializeField]
        [Tooltip("Optional lead-in played once before the main clip.")]
        private AnimationClip _introClip;

        [SerializeField]
        [Tooltip("Optional wind-down played once after the main clip finishes or is stopped.")]
        private AnimationClip _outroClip;

        [Header("Playback")]
        [SerializeField] private ActionMaskMode _maskMode = ActionMaskMode.FullBody;

        [SerializeField]
        [Tooltip("Used when Mask Mode is Custom Mask.")]
        private AvatarMask _customMask;

        [SerializeField] private ActionLoopMode _loopMode = ActionLoopMode.PlayOnce;

        [SerializeField]
        [Min(1)]
        [Tooltip("Repetitions when Loop Mode is Loop Count.")]
        private int _loopCount = 1;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Playback speed multiplier.")]
        private float _speed = 1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Per-entry action target weight. Existing assets default to full weight.")]
        private float _targetWeight = 1f;

        [Header("Rules")]
        [SerializeField]
        [Tooltip("Full-body actions stop the NavMeshAgent while playing. Disable to let the " +
                 "action play over a moving character (rarely desirable for full-body clips).")]
        private bool _suspendsLocomotion = true;

        [SerializeField]
        [Tooltip("Whether a newly requested action may interrupt this one mid-playback.")]
        private bool _interruptible = true;

        [Header("Transitions (−1 = use config default)")]
        [SerializeField] private float _fadeInSecondsOverride = -1f;
        [SerializeField] private float _fadeOutSecondsOverride = -1f;

        [Header("Conversational Cue")]
        [SerializeField]
        [Tooltip("Semantic cue this entry answers (e.g. Affirmative for \"yes\"). None = not " +
                 "reachable through IConversationalGesturePerformer.TryPerform; only explicit " +
                 "PlayAction/backend calls can still play it.")]
        private GestureCueKind _cue = GestureCueKind.None;

        [Header("Anchor Alignment (optional)")]
        [SerializeField]
        [Tooltip("Default approach/alignment data for PlayActionAt (walk to an anchor, root-align, " +
                 "then play this action — e.g. sit / pick-up / use-prop). An explicit options " +
                 "argument on the call overrides these authored defaults.")]
        private ActionAnchorOptions _anchorOptions = new();

        [Header("Full-Body Hold — Conversation Overlays")]
        [SerializeField]
        [Tooltip("For a full-body Hold Until Stopped action (e.g. sitting at a desk): keep the " +
                 "talk, pointing, and beat/referential gesture overlays playing at full weight " +
                 "instead of ducking them to zero, so the character stays conversationally alive " +
                 "while seated. Caveat: standing-authored talk clips still transplant onto the " +
                 "seated pose through the talk layer's upper-body-minus-spine mask — results vary " +
                 "with how different the seated silhouette is from standing.")]
        private bool _allowConversationOverlays;

        [Header("Ambient Activity")]
        [SerializeField]
        [Tooltip("Tags this entry as ambient idle content (e.g. stretching, tidying, examining) " +
                 "the character may perform on its own after a period with nobody engaging it in " +
                 "conversation. Purely an authoring tag — mirrors Cue — with no behavior of its " +
                 "own; only takes effect through ConvaiBodyAnimationConfig.EnableAmbientActivities.")]
        private bool _ambient;

        public string ActionName => _actionName;
        public IReadOnlyList<string> Aliases => _aliases;
        public AnimationClip Clip => _clip;
        public AnimationClip IntroClip => _introClip;
        public AnimationClip OutroClip => _outroClip;
        public ActionMaskMode MaskMode => _maskMode;
        public AvatarMask CustomMask => _customMask;
        public ActionLoopMode LoopMode => _loopMode;
        public int LoopCount => _loopCount;
        public float Speed => _speed;
        public float TargetWeight => Mathf.Clamp01(_targetWeight);
        public bool SuspendsLocomotion => _suspendsLocomotion;
        public bool Interruptible => _interruptible;
        public float FadeInSecondsOverride => _fadeInSecondsOverride;
        public float FadeOutSecondsOverride => _fadeOutSecondsOverride;

        /// <summary>
        ///     Semantic cue this entry answers when driven through
        ///     <c>IConversationalGesturePerformer.TryPerform</c>. <see cref="GestureCueKind.None" />
        ///     (the default for every pre-existing, untagged asset) means the entry is only
        ///     reachable by name via <c>PlayAction</c>/backend actions.
        /// </summary>
        public GestureCueKind Cue => _cue;

        /// <summary>
        ///     Authored default approach/alignment data for <c>PlayActionAt</c>. Never null —
        ///     an entry with no explicit anchor authoring simply carries
        ///     <see cref="ActionAnchorOptions" />'s field defaults.
        /// </summary>
        public ActionAnchorOptions AnchorOptions => _anchorOptions;

        /// <summary>
        ///     True when this full-body Hold Until Stopped action should NOT duck the talk,
        ///     pointing, and beat/referential gesture overlays while it plays — the
        ///     "seated conversation" flag. See the field tooltip for the standing-clip
        ///     transplant caveat. Default false: every pre-existing, untagged asset keeps
        ///     today's full-body duck behavior unchanged.
        /// </summary>
        public bool AllowConversationOverlays => _allowConversationOverlays;

        /// <summary>
        ///     True when this entry is tagged as ambient idle content —
        ///     <see cref="Core.Policy.AmbientActivityDirector" /> may pick it while the
        ///     character has been Idle for a while. An authoring tag only; has no effect unless
        ///     <c>ConvaiBodyAnimationConfig.EnableAmbientActivities</c> is on.
        /// </summary>
        public bool Ambient => _ambient;

        public bool HasIntro => _introClip != null;
        public bool HasOutro => _outroClip != null;
        public bool IsValid => _clip != null && !string.IsNullOrWhiteSpace(_actionName);

        /// <summary>
        ///     Case-insensitive match against the primary name and aliases. Spaces, dashes,
        ///     and underscores are treated as equivalent so <c>"pick up"</c>, <c>"pick_up"</c>,
        ///     and <c>"Pick-Up"</c> all resolve to the same entry.
        /// </summary>
        public bool Matches(string nameOrAlias)
        {
            if (string.IsNullOrWhiteSpace(nameOrAlias)) return false;

            if (NamesEquivalent(_actionName, nameOrAlias)) return true;

            for (int i = 0; i < _aliases.Count; i++)
            {
                if (NamesEquivalent(_aliases[i], nameOrAlias))
                    return true;
            }

            return false;
        }

        /// <summary>Canonical comparison key: lowercase, separators collapsed.</summary>
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            Span<char> buffer = stackalloc char[name.Length];
            int length = 0;
            foreach (char c in name.Trim())
            {
                char mapped = c is ' ' or '-' or '_' ? '_' : char.ToLowerInvariant(c);
                if (mapped == '_' && length > 0 && buffer[length - 1] == '_') continue;
                buffer[length++] = mapped;
            }

            return new string(buffer[..length]);
        }

        private static bool NamesEquivalent(string a, string b) =>
            NormalizeName(a) == NormalizeName(b) && !string.IsNullOrEmpty(NormalizeName(a));

        internal void Initialize(
            string actionName,
            AnimationClip clip,
            ActionMaskMode maskMode,
            ActionLoopMode loopMode = ActionLoopMode.PlayOnce,
            AnimationClip introClip = null,
            AnimationClip outroClip = null,
            IEnumerable<string> aliases = null)
        {
            _actionName = actionName;
            _clip = clip;
            _maskMode = maskMode;
            _loopMode = loopMode;
            _introClip = introClip;
            _outroClip = outroClip;
            _aliases = aliases != null ? new List<string>(aliases) : new List<string>();
        }

        /// <summary>Editor/wizard writers. Not part of the public runtime API.</summary>
        internal void SetCue(GestureCueKind cue) => _cue = cue;

        /// <summary>Editor/wizard writers and tests. Not part of the public runtime API.</summary>
        internal void SetAmbient(bool ambient) => _ambient = ambient;
    }
}
