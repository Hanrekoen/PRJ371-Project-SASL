using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.Logging;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Core.Behaviors;
using Convai.Runtime;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using Convai.Shared.Compatibility;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Joint attention: notices what the PLAYER is looking at and glances there too, then
    ///     returns to whatever the gaze policy dictates — the "I see what caught your eye"
    ///     beat. Opt-in, built entirely on the public
    ///     <see cref="ConvaiGazeController.GlanceAt(Transform, float)" /> /
    ///     <see cref="ConvaiGazeController.GlanceAt(Vector3, float)" /> API, so it needs no
    ///     changes to the gaze core. Can optionally publish the noticed object's name as
    ///     dynamic context so the backend can talk about it.
    /// </summary>
    /// <remarks>
    ///     The player's gaze ray is cone-tested against every registered world-object gaze
    ///     candidate (<see cref="ConvaiGazeTarget" /> and <see cref="WorldObjectGazeTargetProvider" />)
    ///     at a fixed ~5 Hz evaluation cadence, not every frame. A continuous dwell on the same
    ///     object for <see cref="DwellSeconds" /> — with a one-evaluation grace so a single
    ///     missed sample does not reset it — is treated as "the player noticed this"; the
    ///     character then glances there after a short, randomized reaction delay. Per-object and
    ///     global cooldowns keep it from ping-ponging.
    /// </remarks>
    [AddComponentMenu("Convai/Gaze/Advanced/Joint Attention")]
    [DisallowMultipleComponent]
    public sealed class GazeJointAttention : MonoBehaviour, IEmbodimentTickable
    {
        [SerializeField, Range(1f, 30f)]
        [Tooltip("Cone half-angle (degrees) used to decide whether the player's gaze ray is aimed at a candidate object.")]
        private float coneAngleDegrees = 8f;

        [SerializeField, Min(0f)]
        [Tooltip("Maximum distance (meters) from the player at which a candidate object can be noticed.")]
        private float maxDistanceMeters = 12f;

        [SerializeField, Range(0.1f, 3f)]
        [Tooltip("Continuous dwell (seconds) the player must hold on the same object before the character notices it.")]
        private float dwellSeconds = 0.7f;

        [SerializeField, Range(0.3f, 4f)]
        [Tooltip("Duration (seconds) of the glance at the noticed object.")]
        private float glanceDurationSeconds = 1.4f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Minimum delay (seconds) between noticing and glancing ('registers it, then looks').")]
        private float reactionDelayMinSeconds = 0.2f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Maximum delay (seconds) between noticing and glancing.")]
        private float reactionDelayMaxSeconds = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("Per-object cooldown (seconds): the same object is not re-glanced within this window.")]
        private float cooldownSeconds = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("Minimum interval (seconds) between any two joint-attention glances.")]
        private float globalMinIntervalSeconds = 4f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Seconds between player-gaze evaluations (~5 Hz by default). Lower is more responsive, more work.")]
        private float evaluationIntervalSeconds = 0.2f;

        [SerializeField]
        [Tooltip("Publish the noticed object's name to the backend dynamic context so it can be talked about.")]
        private bool publishAttentionContext;

        [SerializeField]
        [Tooltip("Active while the character is idle.")]
        private bool activeWhenIdle = true;

        [SerializeField]
        [Tooltip("Active while the character is listening to the player.")]
        private bool activeWhenListening = true;

        private const string AttentionContextKey = "player_attending_to";

        private EmbodimentContext _context;
        private ConvaiGazeController _controller;
        private IPlayerGazeRaySource _instanceGazeRaySource;
        private DeterministicEmbodimentRandom _random;
        private readonly JointAttentionDirector _director = new();

        private readonly List<JointAttentionCandidate> _candidateBuffer = new(16);
        private readonly Dictionary<long, Transform> _idToTransform = new(16);

        private float _evaluationAccumulator;
        private bool _loggedInert;
        private bool _loggedNoRaySource;

        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        /// <summary>Cone half-angle (degrees) used for the player's-looking-at-it test.</summary>
        public float ConeAngleDegrees
        {
            get => coneAngleDegrees;
            set => coneAngleDegrees = Mathf.Clamp(value, 1f, 30f);
        }

        /// <summary>Maximum distance (meters) a candidate object can be noticed from.</summary>
        public float MaxDistanceMeters
        {
            get => maxDistanceMeters;
            set => maxDistanceMeters = Mathf.Max(0f, value);
        }

        /// <summary>Continuous dwell (seconds) required before the character notices an object.</summary>
        public float DwellSeconds
        {
            get => dwellSeconds;
            set => dwellSeconds = Mathf.Clamp(value, 0.1f, 3f);
        }

        /// <summary>Duration (seconds) of the glance at the noticed object.</summary>
        public float GlanceDurationSeconds
        {
            get => glanceDurationSeconds;
            set => glanceDurationSeconds = Mathf.Clamp(value, 0.3f, 4f);
        }

        /// <summary>Minimum delay (seconds) between noticing and glancing.</summary>
        public float ReactionDelayMinSeconds
        {
            get => reactionDelayMinSeconds;
            set => reactionDelayMinSeconds = Mathf.Clamp(value, 0f, 2f);
        }

        /// <summary>Maximum delay (seconds) between noticing and glancing.</summary>
        public float ReactionDelayMaxSeconds
        {
            get => reactionDelayMaxSeconds;
            set => reactionDelayMaxSeconds = Mathf.Clamp(value, 0f, 2f);
        }

        /// <summary>Per-object cooldown (seconds) between repeated glances at the same object.</summary>
        public float CooldownSeconds
        {
            get => cooldownSeconds;
            set => cooldownSeconds = Mathf.Max(0f, value);
        }

        /// <summary>Minimum interval (seconds) between any two joint-attention glances.</summary>
        public float GlobalMinIntervalSeconds
        {
            get => globalMinIntervalSeconds;
            set => globalMinIntervalSeconds = Mathf.Max(0f, value);
        }

        /// <summary>Seconds between player-gaze evaluations.</summary>
        public float EvaluationIntervalSeconds
        {
            get => evaluationIntervalSeconds;
            set => evaluationIntervalSeconds = Mathf.Clamp(value, 0.05f, 1f);
        }

        /// <summary>Whether the noticed object's name is published to the backend dynamic context.</summary>
        public bool PublishAttentionContext
        {
            get => publishAttentionContext;
            set => publishAttentionContext = value;
        }

        /// <summary>Whether joint attention is active while the character is idle.</summary>
        public bool ActiveWhenIdle
        {
            get => activeWhenIdle;
            set => activeWhenIdle = value;
        }

        /// <summary>Whether joint attention is active while the character is listening.</summary>
        public bool ActiveWhenListening
        {
            get => activeWhenListening;
            set => activeWhenListening = value;
        }

        /// <summary>Overrides the player gaze-ray source for this character only.</summary>
        /// <summary>
        ///     Registers the source this component reads the player's gaze ray from — an XR
        ///     eye-tracking adapter, typically. Pass <c>null</c> to fall back to the player camera.
        /// </summary>
        public void SetGazeRaySource(IPlayerGazeRaySource source) => _instanceGazeRaySource = source;

        private void Awake() => _random = DeterministicEmbodimentRandom.Create(this);

        private void OnValidate() => reactionDelayMaxSeconds = Mathf.Max(reactionDelayMaxSeconds, reactionDelayMinSeconds);

        private void OnEnable()
        {
            if (!EmbodimentContext.TryResolveFor(this, out _context))
            {
                LogInertOnce("No EmbodimentContext found in the parent hierarchy; component is inert.");
                enabled = false;
                return;
            }

            _controller = GetComponentInParent<ConvaiGazeController>(true);
            if (_controller == null)
            {
                LogInertOnce("No ConvaiGazeController found in the parent hierarchy; component is inert.");
                enabled = false;
                return;
            }

            _context.EnsureTickScheduler()?.Register(this);
            _director.Reset();
            _evaluationAccumulator = 0f;
            _loggedNoRaySource = false;
        }

        private void OnDisable()
        {
            if (publishAttentionContext && !string.IsNullOrEmpty(_director.AttendedObjectName))
                ClearPublishedAttention();

            _context?.TickScheduler?.Unregister(this);
            _director.Reset();
            _evaluationAccumulator = 0f;
        }

        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (_controller == null) return;

            _evaluationAccumulator += deltaTime;
            if (_evaluationAccumulator < evaluationIntervalSeconds) return;

            float evalDeltaTime = _evaluationAccumulator;
            _evaluationAccumulator = 0f;

            if (!TryResolvePlayerRay(out Ray ray))
            {
                if (!_loggedNoRaySource)
                {
                    ConvaiLogger.Warning(
                        "[GazeJointAttention] No player gaze ray available (no ray source and no main camera); will keep retrying quietly.",
                        LogCategory.Gaze);
                    _loggedNoRaySource = true;
                }

                return;
            }

            BuildCandidates();
            bool active = IsDialogueStateActive();

            _director.Tick(
                in ray,
                _candidateBuffer,
                evalDeltaTime,
                active,
                coneAngleDegrees,
                maxDistanceMeters,
                dwellSeconds,
                reactionDelayMinSeconds,
                reactionDelayMaxSeconds,
                cooldownSeconds,
                globalMinIntervalSeconds,
                ref _random);

            if (_director.HasGlanceToFire) FireGlance();

            if (publishAttentionContext && _director.AttendedChangedThisTick) PublishAttended();
        }

        /// <summary>
        ///     Resolves the player's gaze ray from this component's own source, falling back to the
        ///     player camera.
        /// </summary>
        /// <remarks>
        ///     The second argument used to be <c>PlayerAttentionSensor.DefaultGazeRaySource</c>, a
        ///     process-global static. It is gone; a character that wants an XR eye-tracking source
        ///     sets it on its own component.
        /// </remarks>
        private bool TryResolvePlayerRay(out Ray ray) =>
            PlayerAttentionMath.TryResolveGazeRay(
                _instanceGazeRaySource, null, Camera.main, out ray);

        private void BuildCandidates()
        {
            _candidateBuffer.Clear();
            _idToTransform.Clear();

            Transform root = _context != null ? _context.CharacterRoot : transform;

            IReadOnlyList<ConvaiGazeTarget> gazeTargets = ConvaiGazeTarget.ActiveTargets;
            for (int i = 0; i < gazeTargets.Count; i++)
            {
                ConvaiGazeTarget target = gazeTargets[i];
                if (target == null) continue;
                if (target.TryGetCandidate(root, out GazeTargetCandidate candidate)) AddCandidate(root, in candidate);
            }

            IReadOnlyList<WorldObjectGazeTargetProvider> worldObjects = WorldObjectGazeTargetProvider.ActiveProviders;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                WorldObjectGazeTargetProvider provider = worldObjects[i];
                if (provider == null) continue;
                if (provider.TryGetCandidate(root, out GazeTargetCandidate candidate)) AddCandidate(root, in candidate);
            }
        }

        private void AddCandidate(Transform root, in GazeTargetCandidate candidate)
        {
            Transform target = candidate.Target;
            if (target == null) return;
            // Never joint-attend to this character's own gaze targets — that is eye contact,
            // already handled by the eye-contact lock, not "the player noticed something".
            if (target == root || target.IsChildOf(root)) return;

            // The id is carried at full width and resolved back through _idToTransform when the
            // glance fires. It used to be narrowed to ConvaiObjectId.Of(target).GetHashCode(), which
            // made two candidates whose ids happened to hash alike resolve to each other's transform
            // — a silent, non-deterministic glance at the wrong object.
            long id = ConvaiObjectId.Of(target);
            if (id == 0L) return; // null or destroyed; nothing to glance at and no id to key on.

            _candidateBuffer.Add(new JointAttentionCandidate(id, candidate.WorldPoint, candidate.DebugName));
            _idToTransform[id] = target;
        }

        private void FireGlance()
        {
            if (_idToTransform.TryGetValue(_director.GlanceTargetId, out Transform target) && target != null)
                _controller.GlanceAt(target, glanceDurationSeconds);
            else
                _controller.GlanceAt(_director.GlanceWorldPoint, glanceDurationSeconds);
        }

        private bool IsDialogueStateActive()
        {
            DialogueState state = _context?.ConversationFlowSource?.Current.Primary ?? DialogueState.Idle;
            return state switch
            {
                DialogueState.Idle => activeWhenIdle,
                DialogueState.Listening => activeWhenListening,
                _ => false
            };
        }

        private void PublishAttended()
        {
            ConvaiCharacter character = _context?.Character;
            if (character == null) return;

            if (string.IsNullOrEmpty(_director.AttendedObjectName))
                character.DynamicContext.RemoveState(AttentionContextKey);
            else
                character.DynamicContext.SetState(
                    AttentionContextKey, _director.AttendedObjectName, ConvaiRespondMode.Silent);
        }

        private void ClearPublishedAttention()
        {
            ConvaiCharacter character = _context?.Character;
            character?.DynamicContext.RemoveState(AttentionContextKey);
        }

        private void LogInertOnce(string message)
        {
            if (_loggedInert) return;
            _loggedInert = true;
            ConvaiLogger.Warning($"[GazeJointAttention] {message}", LogCategory.Gaze);
        }
    }
}
