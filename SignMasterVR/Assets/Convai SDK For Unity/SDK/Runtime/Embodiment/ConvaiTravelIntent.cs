using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Animation;
using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Says where this character is going, so the rest of the character can behave accordingly —
    ///     most visibly, so it watches the road while it walks instead of staring at wherever it is
    ///     headed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>You usually do not add this.</b> It appears by itself the moment a character
    ///         actually moves, and disappears with the character. Add it by hand only to change the
    ///         detection thresholds below, or to switch the automatic detection off.
    ///     </para>
    ///     <para>
    ///         <b>It works with any movement code.</b> Travel is resolved from three sources, best
    ///         first:
    ///     </para>
    ///     <list type="number">
    ///         <item>
    ///             <description>
    ///                 What you tell it — <see cref="ReportTravel(Vector3, float)" /> or
    ///                 <see cref="ReportTravelTo" />. Highest fidelity, and the only option that can
    ///                 state a remaining distance. Reports expire (see
    ///                 <see cref="TravelReportTimeoutSeconds" />) so a caller that stops reporting, or
    ///                 dies mid-move, cannot leave the character travelling forever.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 What the Convai NavMesh Locomotion component pushes, when the character has
    ///                 one. This is the default setup and needs no code at all.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 What it can see for itself — the character's own movement, measured directly.
    ///                 This is what covers a character driven by a Character Controller, by root
    ///                 motion, by a tween, or by navigation code that has nothing to do with Convai.
    ///                 It cannot know a destination, so it reports the direction only.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Separately from all three, <see cref="SetSubject(Transform)" /> declares what the
    ///         journey is <em>about</em> — the place being walked to, or the person being followed —
    ///         which is what earns the periodic glances. Action steps that name a target declare it
    ///         for you.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Embodiment/Travel Intent")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(EmbodimentExecutionOrders.Context + 1)]
    public sealed class ConvaiTravelIntent : MonoBehaviour, IEmbodimentTickable, ITravelIntentSource
    {
        /// <summary>
        ///     Direction smoothing rate. Fast enough to lead a turn, slow enough that a passed path
        ///     corner does not step the look point across the room. Not serialized: it is a
        ///     correctness constant for the consumer contract (see <see cref="TravelIntent" />),
        ///     not a taste setting.
        /// </summary>
        private const float DirectionBlendSpeed = 6f;

        /// <summary>Below this, a "speed" is numerical noise rather than movement.</summary>
        private const float SpeedEpsilon = 0.001f;

        // No [Header] on serialized fields: the Convai inspector groups these into its own
        // sections, and a Header decorator would draw a second, unstyled title inside them.
        [SerializeField]
        [Tooltip("Notice movement on this character's own, without any Convai locomotion component " +
                 "or code telling it. Turn this off to have the character count as travelling only " +
                 "when something explicitly says so.")]
        private bool detectMovementAutomatically = true;

        [SerializeField, Min(0.01f)]
        [Tooltip("How fast the character has to move before it counts as going somewhere, in metres " +
                 "per second. Below this is settling, jitter, or turning on the spot.")]
        private float movementSpeedThreshold = 0.35f;

        [SerializeField, Min(0f)]
        [Tooltip("How long that movement has to keep up before it counts, in seconds. Stops a single " +
                 "shove or a one-frame teleport from reading as a journey.")]
        private float movementSustainSeconds = 0.25f;

        [SerializeField, Min(0.05f)]
        [Tooltip("How long a reported journey stays valid without being repeated, in seconds. Code " +
                 "that stops reporting — or is destroyed mid-move — falls back instead of leaving " +
                 "the character travelling forever.")]
        private float reportTimeoutSeconds = 0.5f;

        [SerializeField, Min(0.1f)]
        [Tooltip("The speed treated as 'full effort' when normalizing how fast the character is " +
                 "going. Only used when nothing else supplies one.")]
        private float referenceTravelSpeed = 3.6f;

        private EmbodimentContext _context;
        private CharacterServiceRegistry.ServiceToken _token;

        /// <summary>
        ///     The transform whose movement <em>is</em> the character's movement. Resolved from the
        ///     embodiment context rather than assumed to be this component's own transform: a
        ///     hand-placed instance on a child object would otherwise measure a local position that
        ///     never changes while the character walks, and report that it is standing still.
        /// </summary>
        private Transform _root;

        // Smoothed output ---------------------------------------------------
        private Vector3 _smoothedDirection;
        private bool _hasSmoothedDirection;
        private TravelIntent _current = TravelIntent.None;

        // Observed-motion state ---------------------------------------------
        private Vector3 _lastLocalPosition;
        private bool _hasLastLocalPosition;
        private float _sustainedSeconds;

        // Reported (tier 1) state -------------------------------------------
        // The heartbeat ages on the tick rather than against Time.time, so it behaves identically
        // under a scaled or paused timescale and can be exercised without a running player loop.
        private bool _hasReport;
        private float _reportAgeSeconds;
        private Vector3 _reportedDirection;
        private float _reportedSpeed01;
        private float _reportedRemaining = float.PositiveInfinity;

        // Pushed (tier 2) state ---------------------------------------------
        private bool _hasPush;
        private Vector3 _pushedDirection;
        private float _pushedSpeed01;
        private float _pushedRemaining = float.PositiveInfinity;

        // Subject ------------------------------------------------------------
        private Transform _subjectTransform;
        private Vector3 _subjectPoint;
        private TravelSubjectKind _subjectKind;

        /// <summary>How long a reported journey stays valid without being repeated, in seconds.</summary>
        public float TravelReportTimeoutSeconds => reportTimeoutSeconds;

        /// <summary>
        ///     The transform that moves when the character moves. Falls back to this component's own
        ///     transform outside a resolved embodiment context (tests, a bare GameObject).
        /// </summary>
        private Transform Root => _root != null ? _root : transform;

        /// <summary>Whether the character is going somewhere right now.</summary>
        public bool IsTraveling => _current.IsTraveling;

        /// <summary>
        ///     Whether anything has declared what this journey is about. Without a subject the
        ///     character simply watches the road, which is the right default for a move nobody
        ///     attached a meaning to.
        /// </summary>
        public bool HasSubject => ResolveSubjectKind() != TravelSubjectKind.None;

        /// <summary>
        ///     Where this reading came from, for the inspector and for diagnosing "why is it looking
        ///     there?". Not part of the cross-module contract.
        /// </summary>
        public TravelSource Source { get; private set; } = TravelSource.NotTraveling;

        /// <summary>Where travel information can come from, best first.</summary>
        public enum TravelSource
        {
            /// <summary>The character is not going anywhere.</summary>
            NotTraveling = 0,

            /// <summary>Something called <c>ReportTravel</c>. Knows the most.</summary>
            Reported = 1,

            /// <summary>A Convai locomotion component pushed its own state.</summary>
            Locomotion = 2,

            /// <summary>Nobody said anything; the movement was noticed directly.</summary>
            Observed = 3
        }

        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        /// <summary>
        ///     Runs before every consumer in the cognition band so gaze reads this frame's travel,
        ///     not last frame's.
        /// </summary>
        int IEmbodimentTickable.TickOrder => EmbodimentExecutionOrders.ConversationFlow - 10;

        TravelIntent ITravelIntentSource.Current => _current;

        private void OnEnable()
        {
            if (!EmbodimentContext.TryResolveFor(this, out EmbodimentContext ctx))
            {
                enabled = false;
                return;
            }

            _context = ctx;
            _root = _context.CharacterRoot != null ? _context.CharacterRoot : transform;
            _token = _context.Provide<ITravelIntentSource>(this);
            _context.EnsureTickScheduler()?.Register(this);
        }

        private void OnDisable()
        {
            _context?.TickScheduler?.Unregister(this);
            _token.Release();
            _token = default;
            _context = null;
            _root = null;
            ResetState();
        }

        /// <summary>
        ///     States that the character is travelling in <paramref name="worldDirection" /> at
        ///     <paramref name="speed01" /> (0..1 of full effort). Call this every frame while the
        ///     movement lasts — a report that stops being repeated expires by itself.
        /// </summary>
        /// <remarks>
        ///     Use this when you move the character with your own code and want the full behavior.
        ///     If you do nothing at all, movement is noticed automatically and you still get the
        ///     character watching where it is going — you only lose the remaining-distance detail.
        /// </remarks>
        public void ReportTravel(Vector3 worldDirection, float speed01) =>
            ReportTravel(worldDirection, speed01, float.PositiveInfinity);

        /// <summary>
        ///     States that the character is travelling in <paramref name="worldDirection" /> with
        ///     <paramref name="remainingDistance" /> metres still to go.
        /// </summary>
        public void ReportTravel(Vector3 worldDirection, float speed01, float remainingDistance)
        {
            Vector3 flat = worldDirection;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) return;

            _hasReport = true;
            _reportAgeSeconds = 0f;
            _reportedDirection = flat.normalized;
            _reportedSpeed01 = Mathf.Clamp01(speed01);
            _reportedRemaining = remainingDistance;
        }

        /// <summary>
        ///     Convenience for the common case: the character is walking to
        ///     <paramref name="destination" />, and that destination is also what the journey is
        ///     about. Equivalent to a <see cref="ReportTravel(Vector3, float, float)" /> plus a
        ///     <see cref="SetSubject(Vector3)" />.
        /// </summary>
        public void ReportTravelTo(Vector3 destination, float speed01)
        {
            Vector3 toDestination = destination - Root.position;
            toDestination.y = 0f;

            ReportTravel(toDestination, speed01, toDestination.magnitude);
            SetSubject(destination);
        }

        /// <summary>Ends a reported journey immediately, without waiting for it to expire.</summary>
        public void ClearTravel()
        {
            _hasReport = false;
            _reportedRemaining = float.PositiveInfinity;
        }

        /// <summary>
        ///     Declares that this journey is about <paramref name="subject" /> — the person being
        ///     followed, or the thing being walked to. This is what earns the periodic glances;
        ///     without it the character simply watches the road.
        /// </summary>
        public void SetSubject(Transform subject)
        {
            _subjectTransform = subject;
            _subjectKind = subject != null ? TravelSubjectKind.Companion : TravelSubjectKind.None;
        }

        /// <summary>Declares that this journey is about a fixed place.</summary>
        public void SetSubject(Vector3 worldPosition)
        {
            _subjectTransform = null;
            _subjectPoint = worldPosition;
            _subjectKind = TravelSubjectKind.Destination;
        }

        /// <summary>Forgets what the journey was about. The character keeps watching the road.</summary>
        public void ClearSubject()
        {
            _subjectTransform = null;
            _subjectKind = TravelSubjectKind.None;
        }

        /// <summary>
        ///     Returns the travel intent on <paramref name="character" />, adding one if it has
        ///     none. Play mode only — nothing is ever added to a scene or prefab on disk.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This is why the component is not a <c>[RequireComponent]</c>. Serializing it onto
        ///         every character would have modified both shipped sample scenes and every existing
        ///         customer character on upgrade, to carry a component that does nothing until
        ///         something moves. It is provisioned at the moment it becomes true instead — the
        ///         same ownership pattern the gaze module already uses for its player anchor.
        ///     </para>
        ///     <para>
        ///         A hand-placed instance is found and used as-is, so the thresholds a user authored
        ///         always win over the defaults.
        ///     </para>
        /// </remarks>
        internal static ConvaiTravelIntent EnsureOn(GameObject character)
        {
            if (character == null || !UnityEngine.Application.isPlaying) return null;

            ConvaiTravelIntent existing = character.GetComponent<ConvaiTravelIntent>();
            return existing != null ? existing : character.AddComponent<ConvaiTravelIntent>();
        }

        /// <summary>
        ///     Push entry point for Convai locomotion components, which know more than observation
        ///     can (a real destination and a real remaining distance). Internal: customer code uses
        ///     <see cref="ReportTravel(Vector3, float, float)" />, which is the same thing with a
        ///     safety timeout.
        /// </summary>
        internal void PushLocomotionState(bool moving, Vector3 worldDirection, float speed01, float remainingDistance)
        {
            if (!moving)
            {
                _hasPush = false;
                return;
            }

            Vector3 flat = worldDirection;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
            {
                _hasPush = false;
                return;
            }

            _hasPush = true;
            _pushedDirection = flat.normalized;
            _pushedSpeed01 = Mathf.Clamp01(speed01);
            _pushedRemaining = remainingDistance;
        }

        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            bool traveling = ResolveTravel(deltaTime, out Vector3 direction, out float speed01, out float remaining);

            if (!traveling)
            {
                _hasSmoothedDirection = false;
                _smoothedDirection = Vector3.zero;
                _current = TravelIntent.None;
                Source = TravelSource.NotTraveling;
                return;
            }

            _current = new TravelIntent(
                true,
                SmoothDirection(direction, deltaTime),
                speed01,
                remaining,
                ResolveSubjectPosition(),
                ResolveSubjectKind());
        }

        /// <summary>
        ///     Picks this frame's travel facts from the best available source. Reported wins over
        ///     pushed wins over observed, and every tier is evaluated so a higher one recovering
        ///     takes over on the very next tick.
        /// </summary>
        private bool ResolveTravel(float deltaTime, out Vector3 direction, out float speed01, out float remaining)
        {
            bool observed = TickObservedMotion(deltaTime, out Vector3 observedDirection, out float observedSpeed01);

            if (_hasReport)
            {
                _reportAgeSeconds += deltaTime;
                if (_reportAgeSeconds > Mathf.Max(0.05f, reportTimeoutSeconds))
                    _hasReport = false;
            }

            if (_hasReport)
            {
                direction = _reportedDirection;
                speed01 = _reportedSpeed01;
                remaining = _reportedRemaining;
                Source = TravelSource.Reported;
                return true;
            }

            if (_hasPush)
            {
                direction = _pushedDirection;
                speed01 = _pushedSpeed01;
                remaining = _pushedRemaining;
                Source = TravelSource.Locomotion;
                return true;
            }

            if (observed)
            {
                direction = observedDirection;
                speed01 = observedSpeed01;
                remaining = float.PositiveInfinity;
                Source = TravelSource.Observed;
                return true;
            }

            direction = Vector3.zero;
            speed01 = 0f;
            remaining = float.PositiveInfinity;
            return false;
        }

        /// <summary>
        ///     Watches the character move and decides whether that counts as going somewhere.
        /// </summary>
        /// <remarks>
        ///     Measured in <b>parent-local</b> space, not world space. A character standing still on
        ///     a moving platform is the false positive that matters here — in world space it looks
        ///     exactly like walking, and the character would spend the whole ride staring down an
        ///     imaginary road.
        /// </remarks>
        private bool TickObservedMotion(float deltaTime, out Vector3 direction, out float speed01)
        {
            direction = Vector3.zero;
            speed01 = 0f;

            if (!detectMovementAutomatically)
            {
                _hasLastLocalPosition = false;
                _sustainedSeconds = 0f;
                return false;
            }

            Transform root = Root;
            Vector3 localPosition = root.localPosition;
            if (!_hasLastLocalPosition)
            {
                _lastLocalPosition = localPosition;
                _hasLastLocalPosition = true;
                return false;
            }

            Vector3 localDelta = localPosition - _lastLocalPosition;
            _lastLocalPosition = localPosition;

            Vector3 worldDelta = root.parent != null
                ? root.parent.TransformVector(localDelta)
                : localDelta;
            worldDelta.y = 0f;

            float speed = worldDelta.magnitude / deltaTime;
            if (speed < Mathf.Max(SpeedEpsilon, movementSpeedThreshold))
            {
                _sustainedSeconds = 0f;
                return false;
            }

            _sustainedSeconds += deltaTime;
            if (_sustainedSeconds < movementSustainSeconds) return false;

            direction = worldDelta.normalized;
            speed01 = Mathf.Clamp01(speed / Mathf.Max(0.1f, referenceTravelSpeed));
            return true;
        }

        /// <summary>
        ///     Eases the direction so a passed path corner reads as leading into the turn rather
        ///     than as the character's attention teleporting. Seeded on the first travelling frame
        ///     so a journey never ramps in from a stale heading.
        /// </summary>
        private Vector3 SmoothDirection(Vector3 target, float deltaTime)
        {
            if (!_hasSmoothedDirection)
            {
                _smoothedDirection = target;
                _hasSmoothedDirection = true;
                return _smoothedDirection;
            }

            float alpha = 1f - Mathf.Exp(-DirectionBlendSpeed * deltaTime);
            Vector3 blended = Vector3.Slerp(_smoothedDirection, target, alpha);
            blended.y = 0f;

            _smoothedDirection = blended.sqrMagnitude > 1e-6f ? blended.normalized : target;
            return _smoothedDirection;
        }

        private Vector3 ResolveSubjectPosition()
        {
            if (_subjectTransform != null) return _subjectTransform.position;
            return _subjectPoint;
        }

        /// <summary>
        ///     A companion subject whose transform has been destroyed stops being a subject, rather
        ///     than pinning the character's attention to the last place it stood.
        /// </summary>
        private TravelSubjectKind ResolveSubjectKind()
        {
            if (_subjectKind == TravelSubjectKind.Companion && _subjectTransform == null)
                return TravelSubjectKind.None;

            return _subjectKind;
        }

        private void ResetState()
        {
            _current = TravelIntent.None;
            Source = TravelSource.NotTraveling;
            _hasSmoothedDirection = false;
            _smoothedDirection = Vector3.zero;
            _hasLastLocalPosition = false;
            _sustainedSeconds = 0f;
            _hasReport = false;
            _reportAgeSeconds = 0f;
            _hasPush = false;
            _subjectTransform = null;
            _subjectKind = TravelSubjectKind.None;
        }

        private void OnValidate()
        {
            movementSpeedThreshold = Mathf.Max(0.01f, movementSpeedThreshold);
            movementSustainSeconds = Mathf.Max(0f, movementSustainSeconds);
            reportTimeoutSeconds = Mathf.Max(0.05f, reportTimeoutSeconds);
            referenceTravelSpeed = Mathf.Max(0.1f, referenceTravelSpeed);
        }
    }
}
