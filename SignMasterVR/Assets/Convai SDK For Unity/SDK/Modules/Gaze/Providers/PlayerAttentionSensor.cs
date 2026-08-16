using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Opt-in sensor that tells the character whether the player is looking at it. The
    ///     default signal is the main camera's forward ray (desktop: "is the character centered
    ///     on screen"); XR eye tracking plugs in through <see cref="IPlayerGazeRaySource" />
    ///     without any XR package dependency. The smoothed 0..1 signal is exposed as
    ///     <see cref="PlayerAttention" />, published to the backend as the dynamic-context key
    ///     <c>player_attention</c> (<c>looking_at_me</c>/<c>away</c>, edge-triggered), and can
    ///     make an idle character glance back sooner (profile
    ///     <c>curiosityRespondsToAttention</c>).
    /// </summary>
    /// <remarks>
    ///     The same reading is available about anything else in the scene through
    ///     <see cref="IsPlayerLookingAt(Transform)" /> — "are they looking at the panel" decided by
    ///     the same ray and the same cone as "are they looking at me", so a scene never ends up with
    ///     two disagreeing ideas of where the player's attention is.
    /// </remarks>
    [AddComponentMenu("Convai/Gaze/Advanced/Player Attention Sensor")]
    [DisallowMultipleComponent]
    public sealed class PlayerAttentionSensor : MonoBehaviour, IEmbodimentTickable
    {
        [SerializeField, Min(0.02f)]
        [Tooltip("Seconds between detection samples (the ray/cone test). The signal is smoothed every tick regardless.")]
        private float _detectionInterval = 0.1f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Base acceptance cone half-angle (degrees) at conversational distance.")]
        private float _baseHalfAngle = 6f;

        [SerializeField, Range(6f, 60f)]
        [Tooltip("Maximum acceptance cone half-angle (degrees). Caps how wide the cone grows at close range so point-blank does not always read as 'looking'.")]
        private float _maxHalfAngle = 28f;

        [SerializeField, Min(0f)]
        [Tooltip("Approximate angular radius (meters) of the character's head/upper body: widens the cone as the player gets closer.")]
        private float _characterAngularRadius = 0.35f;

        [SerializeField, Min(0f)]
        [Tooltip("Eye-line height (meters) above the character root used when no Head bone is resolved.")]
        private float _headHeight = 1.6f;

        [SerializeField, Min(0.02f)]
        [Tooltip("Rise time constant (seconds): attention builds quickly when the player looks over.")]
        private float _riseSeconds = 0.5f;

        [SerializeField, Min(0.02f)]
        [Tooltip("Fall time constant (seconds): attention lingers as the player looks away.")]
        private float _fallSeconds = 1.5f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Attention level at which 'looking_at_me' is published.")]
        private float _enterThreshold = 0.6f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Attention level at which it drops back to 'away' (kept below the enter threshold for hysteresis).")]
        private float _exitThreshold = 0.35f;

        [SerializeField]
        [Tooltip("Publish the looking/away state to the backend as the dynamic-context key 'player_attention'.")]
        private bool _publishToContext = true;

        [SerializeField]
        [Tooltip("Optional component supplying the player's gaze ray — an XR eye-tracking adapter, " +
                 "for example. Leave empty to aim from the player camera.")]
        private MonoBehaviour _gazeRaySourceComponent;

        private IPlayerGazeRaySource _instanceGazeRaySource;
        private readonly System.Collections.Generic.List<Renderer> _boundsScratch = new(8);
        private EmbodimentContext _context;
        private float _attention;
        private float _detectionTimer;
        private bool _lastLookTarget;
        private bool _looking;

        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        /// <summary>Smoothed 0..1 estimate of how much the player is looking at this character.</summary>
        public float PlayerAttention => _attention;

        /// <summary>Whether the sensor currently classifies the player as looking (post-hysteresis).</summary>
        public bool IsPlayerLooking => _looking;

        /// <summary>
        ///     The player's line of sight right now, as this sensor resolves it: its own gaze-ray
        ///     source first (an XR eye-tracking adapter, say), then the player camera's forward ray.
        /// </summary>
        /// <remarks>
        ///     Public because the resolution order is the part that is easy to get wrong. Code that
        ///     reads <c>Camera.main.transform.forward</c> itself works on desktop and then quietly
        ///     stops agreeing with the character the moment a project plugs in eye tracking — the
        ///     character reacts to where the player is really looking while everything else still
        ///     reacts to where their head is pointed.
        /// </remarks>
        /// <param name="ray">Where the player is looking from, and in which direction.</param>
        /// <returns><c>false</c> when there is no ray source and no camera to fall back on.</returns>
        public bool TryGetPlayerGazeRay(out Ray ray) =>
            PlayerAttentionMath.TryResolveGazeRay(EffectiveGazeRaySource, null, Camera.main, out ray);

        /// <summary>
        ///     Whether the player is looking at something in the scene — the same question
        ///     <see cref="IsPlayerLooking" /> answers about this character, asked about anything else.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Uses the same ray and the same distance-aware acceptance cone as this sensor's own
        ///         detection, so "are they looking at me" and "are they looking at that" are decided
        ///         by one rule rather than by two that drift apart. The cone is sized from what the
        ///         subject actually draws: a crate across the room needs a tighter cone than the
        ///         wall chart beside it, and a fixed angle is wrong for one of them whichever angle
        ///         is chosen.
        ///     </para>
        ///     <para>
        ///         This is an immediate reading with no smoothing and no hysteresis — unlike
        ///         <see cref="PlayerAttention" />, which is sampled on the sensor's own interval and
        ///         eased. A caller that wants "has been looking at it for a moment" should require
        ///         this to stay true for that long rather than trusting a single frame.
        ///     </para>
        /// </remarks>
        /// <param name="subject">What the player might be looking at.</param>
        /// <returns><c>false</c> for a null subject, or when there is no way to tell where the player is looking.</returns>
        public bool IsPlayerLookingAt(Transform subject)
        {
            if (subject == null) return false;

            ResolveSubjectAim(subject, out Vector3 point, out float radius);
            return IsPlayerLookingAt(point, radius);
        }

        /// <summary>
        ///     Whether the player is looking at a place in the room, for a subject that has no
        ///     transform — a spot on the floor, a point a character is about to walk to.
        /// </summary>
        /// <param name="worldPoint">The place they might be looking at.</param>
        /// <param name="subjectRadiusMeters">
        ///     Roughly how large the thing there is, as a radius in metres. It widens the acceptance
        ///     cone as the player gets closer, exactly as the character's own angular radius does:
        ///     nobody aims at the centre of a large object, and requiring it would answer no from
        ///     two metres away.
        /// </param>
        public bool IsPlayerLookingAt(Vector3 worldPoint, float subjectRadiusMeters)
        {
            if (!TryGetPlayerGazeRay(out Ray ray)) return false;

            return PlayerAttentionMath.IsLooking(
                ray, worldPoint, _baseHalfAngle, Mathf.Max(0f, subjectRadiusMeters), _maxHalfAngle);
        }

        /// <summary>
        ///     Where to aim at a subject and how large it is, measured from everything it draws.
        /// </summary>
        /// <remarks>
        ///     Aims at the drawn volume rather than the pivot, which sits on the floor for a great
        ///     many props — a test against the pivot answers "they are looking at the crate" only
        ///     while the player stares at the ground in front of it. The renderer list is reused
        ///     between calls so a caller polling this every frame allocates nothing.
        /// </remarks>
        private void ResolveSubjectAim(Transform subject, out Vector3 point, out float radius)
        {
            subject.GetComponentsInChildren(_boundsScratch);
            if (_boundsScratch.Count == 0)
            {
                point = subject.position;
                radius = _characterAngularRadius;
                return;
            }

            Bounds bounds = _boundsScratch[0].bounds;
            for (int i = 1; i < _boundsScratch.Count; i++) bounds.Encapsulate(_boundsScratch[i].bounds);

            _boundsScratch.Clear();
            point = bounds.center;
            radius = bounds.extents.magnitude;
        }

        /// <summary>
        ///     Sets this sensor's gaze-ray source — an XR eye-tracking adapter, for example.
        /// </summary>
        /// <remarks>
        ///     Per sensor, on purpose. This used to be backed by a <c>static</c> property shared by
        ///     every sensor in the process, which meant one scene's adapter silently drove characters
        ///     in another and nothing could be torn down cleanly. Assign the source here or in the
        ///     inspector field; with none set, the sensor aims from the player camera exactly as
        ///     before.
        /// </remarks>
        /// <summary>
        ///     Registers the source this sensor reads the player's gaze ray from — an XR
        ///     eye-tracking adapter, typically. Pass <c>null</c> to fall back to the player camera.
        /// </summary>
        /// <remarks>
        ///     The same thing can be done without code by assigning a component that implements
        ///     <see cref="IPlayerGazeRaySource" /> to <b>Gaze Ray Source Component</b> in the
        ///     Inspector. A source set here takes precedence over that field.
        /// </remarks>
        public void SetGazeRaySource(IPlayerGazeRaySource source) => _instanceGazeRaySource = source;

        /// <summary>The gaze-ray source in effect for this sensor, or <c>null</c> to use the camera.</summary>
        private IPlayerGazeRaySource EffectiveGazeRaySource =>
            _instanceGazeRaySource ?? _gazeRaySourceComponent as IPlayerGazeRaySource;

        private void OnValidate()
        {
            // The exit threshold must sit below the enter threshold or the hysteresis inverts.
            _exitThreshold = Mathf.Min(_exitThreshold, _enterThreshold);
        }

        private void OnEnable()
        {
            if (!EmbodimentContext.TryResolveFor(this, out _context)) return;
            _context.EnsureTickScheduler()?.Register(this);

            if (!UnityEngine.Application.isPlaying) return;

            // Self-announce so a sensor added to an already-live character is picked up by the
            // gaze controller (curiosity reciprocation + HUD) without a manual refresh call.
            Components.ConvaiGazeController controller =
                GetComponentInParent<Components.ConvaiGazeController>(true);
            if (controller != null) controller.RefreshProviders();
        }

        private void OnDisable()
        {
            _context?.TickScheduler?.Unregister(this);
            _attention = 0f;
            _detectionTimer = 0f;
            _lastLookTarget = false;
            _looking = false;
        }

        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (!TryEnsureContext()) return;

            _detectionTimer -= deltaTime;
            if (_detectionTimer <= 0f)
            {
                _detectionTimer = Mathf.Max(0.02f, _detectionInterval);
                _lastLookTarget = SampleLooking();
            }

            _attention = PlayerAttentionMath.Step(
                _attention, _lastLookTarget ? 1f : 0f, deltaTime, _riseSeconds, _fallSeconds);

            // The hysteresis state is maintained every tick (reciprocation and HUDs read it even
            // when context publishing is off); the backend write is only the edge side effect.
            if (PlayerAttentionMath.ResolvePublish(_looking, _attention, _enterThreshold, _exitThreshold, out bool nowLooking))
            {
                _looking = nowLooking;
                if (_publishToContext) PublishState(nowLooking);
            }
        }

        private bool SampleLooking()
        {
            Camera fallbackCamera = Camera.main;
            if (!PlayerAttentionMath.TryResolveGazeRay(
                    EffectiveGazeRaySource, null, fallbackCamera, out Ray ray))
                return false;

            return PlayerAttentionMath.IsLooking(
                ray, ResolveHeadPivot(), _baseHalfAngle, _characterAngularRadius, _maxHalfAngle);
        }

        private Vector3 ResolveHeadPivot()
        {
            IStandardRigBinding rigBinding = _context?.EnsureRigBinding();
            if (rigBinding != null && rigBinding.TryGetBone(StandardBone.Head, out Transform head) && head != null)
                return head.position;

            Transform root = _context != null ? _context.CharacterRoot : transform;
            return root.position + Vector3.up * _headHeight;
        }

        private void PublishState(bool looking)
        {
            // No backend character (e.g. an offline dev scene): the local signal still works for
            // reciprocation and HUDs; there is just nothing to publish to.
            ConvaiCharacter character = _context?.Character;
            if (character == null) return;

            character.DynamicContext.SetState(
                "player_attention",
                looking ? "looking_at_me" : "away",
                ConvaiRespondMode.Silent);
        }

        private bool TryEnsureContext()
        {
            if (_context != null) return true;
            return EmbodimentContext.TryResolveFor(this, out _context);
        }
    }
}
