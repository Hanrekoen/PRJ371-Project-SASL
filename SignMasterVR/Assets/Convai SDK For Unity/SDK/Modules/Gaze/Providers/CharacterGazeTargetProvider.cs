using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Components;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Makes a Convai character look at, and be looked at by, other Convai characters
    ///     (character-to-character mutual gaze). Two independent toggles: <c>publishSelf</c>
    ///     registers this character as a lookable target, and <c>lookAtOthers</c> generates
    ///     gaze candidates for the other registered characters. Listeners look at whoever is
    ///     speaking; idle characters exchange occasional glances. The player anchor (priority
    ///     10) still outranks characters (priority 7) whenever the character is engaged.
    /// </summary>
    /// <remarks>
    ///     Add one component per participating character — identity, dialogue state, and the
    ///     eye-line point all come from the character's embodiment context, so there is no
    ///     per-character wiring beyond adding the component. It can be added or enabled at any
    ///     time (before or after the <see cref="ConvaiGazeController" />); the gaze point
    ///     upgrades from the eye-line fallback to the head bone automatically once the rig
    ///     binds.
    /// </remarks>
    [AddComponentMenu("Convai/Gaze/Advanced/Character Target")]
    [DisallowMultipleComponent]
    public sealed class CharacterGazeTargetProvider : MonoBehaviour
    {
        // No [Header] on serialized fields: the Convai inspector groups these into its own
        // sections, and a Header decorator would draw a second, unstyled title inside them.
        [SerializeField]
        [Tooltip("Register this character as a gaze target other characters can look at.")]
        private bool publishSelf = true;

        [SerializeField]
        [Tooltip("Generate gaze candidates for the other registered characters (look at them).")]
        private bool lookAtOthers = true;

        [SerializeField]
        [Tooltip("Priority tier. Keep between the player anchor (10) and world objects (5) so the " +
                 "player wins during conversation but characters beat background props.")]
        private int priority = 7;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (meters) beyond which another character is not a gaze candidate (0 = unlimited).")]
        private float maxDistance = 12f;

        [SerializeField, Min(0f)]
        [Tooltip("Distance (meters) below which another character is at full relevance.")]
        private float fullRelevanceDistance = 4f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Relevance of a NON-speaking character (a speaking character is always fully " +
                 "relevant, so listeners turn to the active speaker).")]
        private float idleGlanceRelevance = 0.35f;

        [SerializeField, Min(0f)]
        [Tooltip("Eye-line height (meters) other characters aim at until this character's head bone " +
                 "is resolved from the rig binding.")]
        private float eyeLineOffset = 1.6f;

        [SerializeField]
        [Tooltip("While idle (no engaged target), exchange occasional brief glances with nearby " +
                 "characters. Off leaves idle characters to their ambient life.")]
        private bool enableIdleGlances = true;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Minimum seconds between idle character glances.")]
        private float idleGlanceIntervalMin = 5f;

        [SerializeField, Range(2f, 60f)]
        [Tooltip("Maximum seconds between idle character glances.")]
        private float idleGlanceIntervalMax = 12f;

        [SerializeField, Range(0.3f, 4f)]
        [Tooltip("Duration (seconds) of one idle character glance.")]
        private float idleGlanceDuration = 1.4f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Engagement of an idle character glance (idle policy engagement is 0, so a glance " +
                 "needs its own commitment to actually land).")]
        private float idleGlanceEngagement = 0.5f;

        private EmbodimentContext _context;
        private readonly ConvaiCharacterGazeRegistry.Entry _entry = new();
        private bool _registered;
        private bool _subscribedRebind;

        /// <summary>Whether this character generates candidates for other characters.</summary>
        internal bool LookAtOthers => lookAtOthers;

        /// <summary>Whether idle character-to-character glances are enabled.</summary>
        internal bool EnableIdleGlances => enableIdleGlances && lookAtOthers;

        internal float IdleGlanceIntervalMin => idleGlanceIntervalMin;
        internal float IdleGlanceIntervalMax => Mathf.Max(idleGlanceIntervalMax, idleGlanceIntervalMin);
        internal float IdleGlanceDuration => idleGlanceDuration;
        internal float IdleGlanceEngagement => Mathf.Clamp01(idleGlanceEngagement);

        private void OnEnable() => HandleEnable();

        private void OnDisable() => HandleDisable();

        private void OnValidate()
        {
            idleGlanceIntervalMax = Mathf.Max(idleGlanceIntervalMax, idleGlanceIntervalMin);
            if (maxDistance > 0f) fullRelevanceDistance = Mathf.Min(fullRelevanceDistance, maxDistance);
        }

        /// <summary>
        ///     Full enable path (registration, rebind subscription, controller announce).
        ///     Internal seam so EditMode tests can drive the lifecycle explicitly — Unity does
        ///     not invoke <c>OnEnable</c> for plain MonoBehaviours outside play mode.
        /// </summary>
        internal void HandleEnable()
        {
            EmbodimentContext.TryResolveFor(this, out _context);
            _entry.Context = _context;
            _entry.Root = _context != null ? _context.CharacterRoot : transform;
            _entry.DisplayName = _entry.Root != null ? _entry.Root.name : name;
            _entry.EyeLineOffset = eyeLineOffset;
            _entry.RefreshHeadAnchor();

            if (_context != null && !_subscribedRebind)
            {
                _context.RigBindingChanged += OnRigBindingChanged;
                _subscribedRebind = true;
            }

            if (publishSelf) Publish();

            // A gaze controller that enabled before this provider existed re-scans here, so
            // adding the component to a live character starts mutual gaze without a manual
            // RefreshProviders call.
            ConvaiGazeController controller = GetComponentInParent<ConvaiGazeController>(true);
            if (controller != null) controller.RefreshProviders();
        }

        /// <summary>Disable counterpart of <see cref="HandleEnable" /> (test seam).</summary>
        internal void HandleDisable()
        {
            if (_context != null && _subscribedRebind)
            {
                _context.RigBindingChanged -= OnRigBindingChanged;
                _subscribedRebind = false;
            }

            Unpublish();
        }

        private void Publish()
        {
            if (_registered) return;
            ConvaiCharacterGazeRegistry.Register(_entry);
            _registered = true;
        }

        private void Unpublish()
        {
            if (!_registered) return;
            ConvaiCharacterGazeRegistry.Unregister(_entry);
            _registered = false;
        }

        private void OnRigBindingChanged(IStandardRigBinding rigBinding) => _entry.RefreshHeadAnchor(rigBinding);

        /// <summary>
        ///     Builds this observer's gaze candidate for one other registered character.
        ///     Returns <c>false</c> for this character's own entry (or another entry sharing
        ///     its context), an out-of-range or transformless entry, or when
        ///     <c>lookAtOthers</c> is off.
        /// </summary>
        internal bool TryBuildCandidate(
            Transform observerRoot,
            ConvaiCharacterGazeRegistry.Entry other,
            out GazeTargetCandidate candidate)
        {
            candidate = default;
            if (!lookAtOthers || other == null || other == _entry) return false;
            // Never target this character through another entry either (a second provider
            // somewhere in the same hierarchy would otherwise make it gaze at itself).
            if (_context != null && other.Context == _context) return false;
            if (other.Root != null && other.Root == _entry.Root) return false;

            // The first rig bind does not raise RigBindingChanged, so upgrade the fallback
            // anchor lazily — a dictionary lookup per unresolved entry per tick, nothing more.
            if (other.HeadIsFallback) other.RefreshHeadAnchor();
            if (!other.TryGetGazePoint(out Vector3 worldPoint)) return false;

            float distance = observerRoot != null
                ? Vector3.Distance(observerRoot.position, worldPoint)
                : 0f;
            if (maxDistance > 0f && distance > maxDistance) return false;

            float relevance = idleGlanceRelevance;
            bool otherSpeaking = other.Context != null &&
                                 other.Context.ConversationFlowSource != null &&
                                 other.Context.ConversationFlowSource.Current.Primary == DialogueState.Speaking;
            if (otherSpeaking) relevance = 1f;

            if (maxDistance > fullRelevanceDistance && distance > fullRelevanceDistance)
                relevance *= 1f - Mathf.InverseLerp(fullRelevanceDistance, maxDistance, distance);

            if (relevance <= 0f) return false;

            candidate = new GazeTargetCandidate(
                GazeTargetKind.Character,
                priority,
                relevance,
                other.Root != null ? other.Root : other.HeadAnchor,
                worldPoint,
                other.DisplayName);
            return true;
        }
    }
}
