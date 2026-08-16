using System;
using Convai.Domain.Logging;
using Convai.Modules.BodyAnimation.Core.Locomotion;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityEngine.AI;

namespace Convai.Modules.BodyAnimation.Components
{
    /// <summary>How <see cref="ConvaiNavMeshLocomotion.MoveTo" /> chooses the travel speed.</summary>
    public enum LocomotionSpeedProfile
    {
        /// <summary>Always walk.</summary>
        Walk = 0,

        /// <summary>Always jog.</summary>
        Jog = 1,

        /// <summary>Jog for far destinations, walk for near ones (see Auto Jog Distance).</summary>
        Auto = 2
    }

    /// <summary>
    ///     NavMesh movement authority for a Convai character. Wraps the
    ///     <see cref="NavMeshAgent" /> (the position authority — animation never moves the
    ///     capsule), owns character rotation (the agent's own rotation is disabled so turns
    ///     can be animation-driven), and publishes the readings the body animation system
    ///     syncs to: live speed, remaining distance, and path direction.
    /// </summary>
    /// <remarks>
    ///     Works standalone (any script can call <see cref="MoveTo" />) and is discovered
    ///     automatically by <see cref="ConvaiBodyAnimationController" /> for animation sync.
    ///     A missing <see cref="NavMeshAgent" /> is added with default settings on Awake.
    /// </remarks>
    [AddComponentMenu("Convai/Embodiment/NavMesh Locomotion")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class ConvaiNavMeshLocomotion : MonoBehaviour,
        ILocomotionDrive,
        IConvaiLocomotionSource,
        IConvaiLocomotionCommands,
        IConvaiManagedLocomotion,
        IConvaiAnchorAlignment
    {
        private const float ArrivalTolerance = 0.05f;
        private const float MovingSpeedEpsilon = 0.05f;
        private const float SampleMaxDistance = 2f;

        /// <summary>
        ///     The shortest braking run <see cref="StopGracefully" /> will ask for, in metres on the
        ///     reference rig. A character that is barely moving still needs a step to land on rather
        ///     than stopping between footfalls.
        /// </summary>
        private const float MinBrakingDistance = 0.35f;

        /// <summary>How many path corners the braking point search will walk. Paths are short here.</summary>
        private const int BrakingCornerCapacity = 16;

        // No [Header] on serialized fields: ConvaiNavMeshLocomotionEditor groups these into Convai
        // sections, and a Header decorator would draw a second, unstyled title inside them.
        [SerializeField]
        [Tooltip("Explicit agent reference. When empty, resolved from this object (or added).")]
        private NavMeshAgent _agent;

        [SerializeField]
        [Tooltip("Whether a move walks or jogs. Auto decides per destination from how far it is; " +
                 "Walk and Jog force one gait for every move.")]
        private LocomotionSpeedProfile _speedProfile = LocomotionSpeedProfile.Auto;

        [SerializeField, Min(0.5f)]
        [Tooltip("Auto profile: destinations farther than this are jogged to, nearer ones walked.")]
        private float _autoJogDistance = 6f;

        [SerializeField, Min(0f)]
        [Tooltip("Legs shorter than this always walk, even on the Jog profile — jogging " +
                 "needs room to accelerate, cruise, and plant a stop.")]
        private float _minJogDistance = 4.5f;

        [SerializeField, Min(0.5f)]
        [Tooltip("Agent acceleration (m/s²). Human-like walk/jog ramps sit around 3–5; " +
                 "Unity's default 8 reads as a lurch.")]
        private float _acceleration = 4f;

        [SerializeField, Min(0.1f)]
        [Tooltip("Walk speed (m/s). Overridden by the body animation config when a controller runs.")]
        private float _walkSpeed = 1.2f;

        [SerializeField, Min(0.1f)]
        [Tooltip("Jog speed (m/s). Overridden by the body animation config when a controller runs.")]
        private float _jogSpeed = 2.6f;

        [SerializeField, Min(10f)]
        [Tooltip("Turn rate (deg/s) used while following a path. Turn-in-place is animation-driven.")]
        private float _rotationDegreesPerSecond = 360f;

        [SerializeField]
        [Tooltip("Draw the current NavMesh path in the Scene view while this object is selected. " +
                 "Editor only — nothing is drawn in a build.")]
        private bool _drawGizmos = true;

        private bool _moving;
        private bool _frozen;
        private bool _ownsAgent;
        private bool _agentStateCaptured;
        private bool _initialUpdateRotation;
        private bool _initialUpdatePosition;
        private bool _initialAutoBraking;
        private float _initialAcceleration;
        private float _initialSpeed;
        private bool _initialIsStopped;
        private bool _initialRotationDrivenExternally;
        private Vector3 _destination;
        private float _gaitSpeedMultiplier = 1f;

        /// <summary>
        ///     True while the current move is a <see cref="StopGracefully" /> run-out: the character
        ///     is still walking, but to a braking point rather than to what it was sent to. Read at
        ///     the moment the move ends so it reports as a stop, not as an arrival.
        /// </summary>
        private bool _stoppingShort;

        /// <summary>
        ///     How much room the animation system wants for a stop that lands on a footfall, given a
        ///     speed. Null when no body animation controller is driving this character, in which case
        ///     <see cref="StopGracefully" /> falls back to the agent's own physical braking distance.
        /// </summary>
        private Func<float, float> _brakingDistanceProvider;

        private Vector3[] _brakingCorners;

        /// <summary>
        ///     The character's travel intent, resolved on the first move and kept. Null until then —
        ///     a character that never moves never grows the component.
        /// </summary>
        private ConvaiTravelIntent _travelIntent;

        /// <summary>
        ///     This character's rig motion scale, set once per build by
        ///     <see cref="SetMotionScale" />. 1 = no correction.
        /// </summary>
        private float _motionScale = 1f;

        /// <summary>Raised when a new destination is accepted (sampled onto the NavMesh).</summary>
        public event Action<Vector3> MoveStarted;

        /// <summary>Raised when movement ends. True = destination reached, false = canceled.</summary>
        public event Action<bool> MoveEnded;

        /// <summary>The wrapped agent (position authority).</summary>
        public NavMeshAgent Agent => _agent;

        /// <summary>True while the agent exists and its path is still being computed.</summary>
        public bool PathPending => _agent != null && _agent.pathPending;

        /// <summary>Live horizontal speed (m/s).</summary>
        public float Speed
        {
            get
            {
                if (_agent == null) return 0f;
                Vector3 velocity = _agent.velocity;
                velocity.y = 0f;
                return velocity.magnitude;
            }
        }

        /// <summary>Commanded travel speed (m/s) for the current move.</summary>
        public float DesiredSpeed => _agent != null ? _agent.speed : 0f;

        /// <summary>
        ///     Remaining travel distance, or 0 when idle. NavMeshAgent.remainingDistance
        ///     returns Infinity while the path is partially computed (multi-corner paths do
        ///     this intermittently), so unknown values fall back to the straight-line distance
        ///     to the destination — never Infinity, never a spurious spike.
        /// </summary>
        public float RemainingDistance
        {
            get
            {
                if (_agent == null || !_moving) return 0f;

                if (_agent.hasPath && !_agent.pathPending)
                {
                    float remaining = _agent.remainingDistance;
                    if (!float.IsInfinity(remaining))
                        return remaining;
                }

                Vector3 toDestination = _destination - transform.position;
                toDestination.y = 0f;
                return toDestination.magnitude;
            }
        }

        /// <summary>True while the agent is displacing the character (or about to).</summary>
        public bool IsMoving =>
            _moving && _agent != null &&
            (_agent.pathPending || Speed > MovingSpeedEpsilon ||
             (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance + ArrivalTolerance));

        /// <summary>
        ///     Signed yaw (degrees, +right/−left) from the character's forward to the current
        ///     steering direction. 0 when idle. Drives directional start/turn selection.
        /// </summary>
        public float SignedAngleToSteering
        {
            get
            {
                if (_agent == null || !_agent.hasPath) return 0f;

                Vector3 toSteering = _agent.steeringTarget - transform.position;
                toSteering.y = 0f;
                if (toSteering.sqrMagnitude < 1e-4f) return 0f;

                return Vector3.SignedAngle(transform.forward, toSteering, Vector3.up);
            }
        }

        /// <summary>
        ///     When true, path-follow rotation is suspended: the animation system (turn
        ///     clips, directional starts) is rotating the character instead.
        /// </summary>
        public bool RotationDrivenExternally { get; set; }

        /// <summary>Current destination while moving.</summary>
        public Vector3 Destination => _destination;

        /// <summary>
        ///     Sends the character to <paramref name="position" /> (sampled onto the NavMesh).
        ///     Returns false when no NavMesh is reachable near the position or the agent is
        ///     not on a NavMesh.
        /// </summary>
        public bool MoveTo(Vector3 position)
        {
            if (_agent == null || !_agent.isOnNavMesh)
            {
                ConvaiLogger.Warning(
                    $"[ConvaiNavMeshLocomotion] '{name}' cannot walk: it is not standing on a "
                    + "baked NavMesh. Bake a NavMesh for the floor (Window > AI > Navigation), and "
                    + "check the character starts on it.",
                    LogCategory.Animation);
                return false;
            }

            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, SampleMaxDistance, NavMesh.AllAreas))
            {
                ConvaiLogger.Warning(
                    $"[ConvaiNavMeshLocomotion] '{name}' cannot walk to {position}: there is no "
                    + $"walkable floor within {SampleMaxDistance}m of it. Move the destination onto "
                    + "the baked NavMesh, or extend the bake to cover it.",
                    LogCategory.Animation);
                return false;
            }

            bool gateThisStart = _animationGatesMotionStart && !IsMoving && !_managedMotion;
            _agent.speed = ResolveProfileSpeed(Vector3.Distance(transform.position, hit.position));
            if (!_agent.SetDestination(hit.position))
            {
                ConvaiLogger.Warning(
                    $"[ConvaiNavMeshLocomotion] '{name}' could not start walking to {hit.position}: "
                    + "the navigation agent refused the destination, which usually means it is on a "
                    + "separate piece of NavMesh with no path between them. Check the floor is one "
                    + "connected bake, or add an off-mesh link.",
                    LogCategory.Animation);
                return false;
            }

            _destination = hit.position;
            _moving = true;
            _stoppingShort = false;

            // Provisioned at the moment travel becomes true, so peers that care where the character
            // is going (gaze watches the road instead of staring at the destination) have the fact
            // from the first frame of the move rather than after movement is noticed second-hand.
            EnsureTravelIntent();

            // For animation-driven starts, the freshly planned path is held until the
            // locomotion layer has entered a matching turn/start/move state. Retargets of
            // an already-moving path are not re-gated.
            if (!_frozen)
            {
                if (gateThisStart || _motionStartGated)
                    HoldAgentForAnimationStart();
                else
                    _agent.isStopped = false;
            }

            ConvaiLogger.Debug(
                $"[ConvaiNavMeshLocomotion] '{name}' MoveTo {hit.position} " +
                $"(profile={_speedProfile}, speed={_agent.speed:F2} m/s).",
                LogCategory.Animation);
            MoveStarted?.Invoke(hit.position);
            return true;
        }

        /// <summary>
        ///     Cancels the current move immediately: the character halts where it stands, this
        ///     frame. Use for a genuine interruption — a new order arriving, the character being
        ///     taken over by something else. To end a walk because the character <em>decided</em>
        ///     to stop (waiting for someone, holding back), use <see cref="StopGracefully" />,
        ///     which walks the last stride out instead of deleting it.
        /// </summary>
        /// <remarks>
        ///     The agent's velocity is zeroed as well as its path cleared. Clearing the path alone
        ///     leaves the agent coasting down under its own acceleration — around 0.8 m at jog
        ///     speed — while the animation, told the move is over, has already settled: the
        ///     character slides across the floor with its feet standing still.
        /// </remarks>
        public void Stop()
        {
            if (_agent == null || !_moving) return;

            if (_agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.velocity = Vector3.zero;
            }

            _moving = false;
            _stoppingShort = false;
            ClearAnimationStartGate();

            ConvaiLogger.Debug(
                $"[ConvaiNavMeshLocomotion] '{name}' move canceled.", LogCategory.Animation);
            MoveEnded?.Invoke(false);
        }

        /// <summary>
        ///     Ends the current move the way a person ends a walk: by walking the last stride out.
        ///     The character keeps moving to a braking point a stride or two ahead on its current
        ///     path, so the agent decelerates under its own auto-braking and the animation system
        ///     gets the arrival it needs to play a planted stop. <see cref="MoveEnded" /> still
        ///     reports <c>false</c> when the run-out completes — the character stopped, it did not
        ///     get where it was sent.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why not just clear the path.</b> A cancelled move is instantaneous on the
        ///         animation side and gradual on the physics side, and the gap between the two is
        ///         the whole defect: the body glides on while the legs have already stopped. There
        ///         is no amount of blending that fixes it, because the two systems are being told
        ///         different things. Stopping by arriving early tells them the same thing.
        ///     </para>
        ///     <para>
        ///         <b>How far ahead.</b> The body animation controller supplies the distance its
        ///         chosen stop clip actually travels, so the braking point lands inside the window
        ///         the planted stop needs. Without a controller the agent's own physical braking
        ///         distance (v²/2a) is used, which still decelerates smoothly — it simply has no
        ///         footfall to land on.
        ///     </para>
        /// </remarks>
        /// <returns>
        ///     True when the character is now running out to a stop. False when there was nothing
        ///     to stop, or no walkable braking point could be found — in which case the move has
        ///     been cancelled outright via <see cref="Stop" /> and the caller need do nothing.
        /// </returns>
        public bool StopGracefully()
        {
            if (_agent == null || !_moving) return false;

            if (!_agent.isOnNavMesh || _agent.pathPending || _stoppingShort)
            {
                // Nothing to run out along: no mesh, no path yet, or the run-out is already in
                // flight and re-aiming it would move the destination backwards mid-stop.
                if (!_stoppingShort) Stop();
                return _stoppingShort;
            }

            float brakingDistance = ResolveBrakingDistance(Speed);

            // Already inside the braking envelope — the character has less path left than it
            // needs to shed its speed. Retargeting here would pull the destination BACKWARDS,
            // which the stop machinery reads as an aborted stop and answers with a lurch. Let
            // the move it is already on finish, and report it as a stop when it does.
            if (RemainingDistance <= brakingDistance)
            {
                _stoppingShort = true;
                return true;
            }

            if (!TryResolveBrakingPoint(brakingDistance, out Vector3 brakingPoint))
            {
                Stop();
                return false;
            }

            if (!_agent.SetDestination(brakingPoint))
            {
                Stop();
                return false;
            }

            // No MoveStarted here on purpose. This is the tail of the move already in flight, not
            // a new errand — announcing it would have peers that stand down for other people's
            // moves (the follow behavior) stand down for this character's own stop.
            _destination = brakingPoint;
            _stoppingShort = true;

            ConvaiLogger.Debug(
                $"[ConvaiNavMeshLocomotion] '{name}' stopping gracefully over {brakingDistance:F2}m " +
                $"from {Speed:F2} m/s.",
                LogCategory.Animation);
            return true;
        }

        /// <summary>
        ///     True while the current move is a <see cref="StopGracefully" /> run-out rather than
        ///     travel toward what the character was sent to.
        /// </summary>
        public bool IsStoppingGracefully => _moving && _stoppingShort;

        /// <summary>
        ///     How much room to leave for the stop. The animation system's answer wins when it has
        ///     one, because it knows what its stop clip travels; the agent's own physics is the
        ///     fallback for a character with no body animation controller.
        /// </summary>
        private float ResolveBrakingDistance(float speed)
        {
            float suggested = _brakingDistanceProvider?.Invoke(speed) ?? 0f;
            if (suggested > 0f)
                return suggested;

            return LocomotionBraking.PhysicalDistance(
                speed, _agent.acceleration, MinBrakingDistance * _motionScale);
        }

        /// <summary>
        ///     Finds the point <paramref name="distance" /> further along the path the character is
        ///     already walking, and samples it onto the NavMesh. Following the path rather than the
        ///     character's forward is what keeps a run-out that begins on a corner from braking
        ///     into a wall.
        /// </summary>
        private bool TryResolveBrakingPoint(float distance, out Vector3 point)
        {
            point = transform.position;

            _brakingCorners ??= new Vector3[BrakingCornerCapacity];
            int cornerCount = _agent.path.GetCornersNonAlloc(_brakingCorners);

            // False means the path is shorter than the braking run — the character is already
            // inside its stopping envelope and the point handed back is the path's own end, which
            // is exactly where it should brake to. Either way the point is walkable by
            // construction (it lies on a path the agent planned), so it is sampled only to snap
            // the height, never to decide whether it is reachable.
            LocomotionBraking.TryPointAlongPath(_brakingCorners, cornerCount, distance, out Vector3 candidate);
            if (cornerCount < 2)
                return false;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, SampleMaxDistance, NavMesh.AllAreas))
                return false;

            point = hit.position;
            return true;
        }

        /// <summary>
        ///     Lets the body animation controller answer "how much room does a stop need at this
        ///     speed", so <see cref="StopGracefully" /> brakes over the distance the chosen stop
        ///     clip actually travels. Cleared (null) when the controller tears its runtime down.
        /// </summary>
        internal void SetBrakingDistanceProvider(Func<float, float> provider) =>
            _brakingDistanceProvider = provider;

        /// <summary>Teleports the agent (path is cleared).</summary>
        public void Warp(Vector3 position)
        {
            if (_agent == null) return;
            _moving = false;
            _stoppingShort = false;
            ClearAnimationStartGate();
            _agent.Warp(position);
        }

        /// <summary>
        ///     Changes the travel speed profile. Applies to the current move immediately
        ///     (triggering a walk↔jog change if the regime flips) and to every later MoveTo.
        /// </summary>
        public void SetSpeedProfile(LocomotionSpeedProfile speedProfile)
        {
            _speedProfile = speedProfile;

            if (_moving && _agent != null && !_managedMotion)
            {
                _agent.speed = ResolveProfileSpeed(RemainingDistance);
                ConvaiLogger.Debug(
                    $"[ConvaiNavMeshLocomotion] '{name}' speed profile → {speedProfile} " +
                    $"(applied now: {_agent.speed:F2} m/s).",
                    LogCategory.Animation);
            }
        }

        /// <summary>Pushes authoritative speeds from the body animation config.</summary>
        public void ConfigureSpeeds(float walkSpeed, float jogSpeed)
        {
            _walkSpeed = Mathf.Max(0.1f, walkSpeed);
            _jogSpeed = Mathf.Max(_walkSpeed, jogSpeed);
        }

        /// <summary>
        ///     Emotion-driven gait speed scale, multiplied into the commanded walk/jog
        ///     speed at <see cref="ResolveProfileSpeed" /> — the same site <see cref="MoveTo" />
        ///     and <see cref="SetSpeedProfile" /> read from, so the existing measured-speed
        ///     rate-warp (in the locomotion layer's Move state) keeps foot sync by construction.
        ///     A no-op default of 1 leaves every commanded speed byte-identical to before this
        ///     feature existed. Re-applies immediately to an in-flight, unmanaged move so the
        ///     caller's own ~1s smoothing reads as a continuous speed change rather than only
        ///     taking effect on the next <see cref="MoveTo" /> call.
        /// </summary>
        internal void SetGaitSpeedMultiplier(float multiplier)
        {
            multiplier = multiplier > 0f ? multiplier : 0.01f;
            if (Mathf.Approximately(multiplier, _gaitSpeedMultiplier)) return;

            _gaitSpeedMultiplier = multiplier;
            if (_moving && _agent != null && !_managedMotion)
                _agent.speed = ResolveProfileSpeed(RemainingDistance);
        }

        /// <summary>Walk speed (m/s) commanded by the walk profile.</summary>
        public float WalkSpeed => _walkSpeed;

        /// <summary>Jog speed (m/s) commanded by the jog profile.</summary>
        public float JogSpeed => _jogSpeed;

        // ---------------------------------------------------------------- managed motion
        // During starts, stops, turns, and speed changes the animation system slaves the
        // agent's velocity to the clip's authored motion (distance-matched movement).

        private float _cachedAcceleration;
        private float _cachedSpeed;
        private bool _cachedAutoBraking;
        private bool _managedMotion;
        private bool _animationGatesMotionStart;
        private bool _motionStartGated;

        /// <summary>True while the animation system is slaving the agent's speed to a clip.</summary>
        public bool InManagedMotion => _managedMotion;

        /// <summary>
        ///     Lets the body animation layer hold brand-new paths until it has selected and
        ///     entered the matching start/turn/move state. Standalone NavMesh use leaves this
        ///     disabled so <see cref="MoveTo" /> keeps moving immediately.
        /// </summary>
        internal void SetAnimationStartGate(bool enabled)
        {
            _animationGatesMotionStart = enabled;
            if (!enabled)
                ReleaseAnimationStartGate();
        }

        private void HoldAgentForAnimationStart()
        {
            _motionStartGated = true;
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }

        private void ClearAnimationStartGate()
        {
            if (!_motionStartGated) return;

            _motionStartGated = false;
            if (_agent == null || !_agent.isOnNavMesh || _frozen) return;
            _agent.isStopped = false;
        }

        /// <summary>
        ///     Begins clip-slaved movement: rotation follows the animation, auto-braking is
        ///     suspended, and acceleration is raised so speed commands track tightly.
        /// </summary>
        internal void BeginManagedMotion()
        {
            if (_agent == null || _managedMotion) return;

            ReleaseAnimationStartGate();
            _cachedAcceleration = _agent.acceleration;
            _cachedAutoBraking = _agent.autoBraking;
            _cachedSpeed = _agent.speed;
            _agent.acceleration = 60f;
            _agent.autoBraking = false;
            RotationDrivenExternally = true;
            _managedMotion = true;
        }

        /// <summary>Commands the agent speed (m/s) for the current managed frame.</summary>
        internal void SetManagedSpeed(float speed)
        {
            if (_agent == null || !_managedMotion) return;
            _agent.speed = Mathf.Max(0.01f, speed);
        }

        /// <summary>Ends clip-slaved movement and restores normal path following.</summary>
        internal void EndManagedMotion()
        {
            if (_agent == null || !_managedMotion) return;

            _agent.acceleration = _cachedAcceleration;
            _agent.autoBraking = _cachedAutoBraking;
            _agent.speed = _cachedSpeed;
            RotationDrivenExternally = false;
            _managedMotion = false;
        }

        internal void ReleaseAnimationStartGate()
        {
            ClearAnimationStartGate();
        }

        /// <summary>
        ///     Freezes/unfreezes the agent in place (turn-in-place: the NavMeshAgent must not
        ///     translate while the turn clip rotates the character).
        /// </summary>
        internal void FreezeAgent(bool frozen)
        {
            _frozen = frozen;
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.isStopped = frozen || _motionStartGated;
            if (frozen)
                _agent.velocity = Vector3.zero;
        }

        private bool _rootAlignmentActive;
        private bool _cachedUpdatePosition;

        /// <summary>
        ///     Takes root-write authority for <c>PlayActionAt</c>'s alignment lerp: disables
        ///     <see cref="NavMeshAgent.updatePosition" /> so the agent's own position sync
        ///     never snaps/corrects the root while it is being written directly frame-to-frame.
        ///     Idempotent — a second call before <see cref="EndRootAlignment" /> is a no-op.
        /// </summary>
        internal void BeginRootAlignment()
        {
            if (_agent == null || _rootAlignmentActive) return;
            _rootAlignmentActive = true;
            _cachedUpdatePosition = _agent.updatePosition;
            _agent.updatePosition = false;
        }

        /// <summary>
        ///     Releases root-write authority: re-syncs the agent's internal simulation
        ///     position to <paramref name="rootPosition" /> via <see cref="NavMeshAgent.Warp" />
        ///     (so the agent doesn't think it's still at the pre-alignment spot) and restores
        ///     <see cref="NavMeshAgent.updatePosition" />. Every alignment exit path (natural
        ///     completion, mid-alignment cancel, teardown) must call this. Idempotent/safe when
        ///     no alignment is active.
        /// </summary>
        internal void EndRootAlignment(Vector3 rootPosition)
        {
            if (_agent == null || !_rootAlignmentActive) return;
            _rootAlignmentActive = false;
            if (_agent.isOnNavMesh)
                _agent.Warp(rootPosition);
            _agent.updatePosition = _cachedUpdatePosition;
        }

        /// <summary>Completes the current move from the animation side (planted stop landed).</summary>
        internal void CompleteMoveFromAnimation()
        {
            if (_agent == null || !_moving) return;

            if (_agent.isOnNavMesh)
                _agent.ResetPath();
            _moving = false;
            ClearAnimationStartGate();

            ConvaiLogger.Debug(
                $"[ConvaiNavMeshLocomotion] '{name}' move completed by planted stop " +
                $"(residual={Vector3.Distance(transform.position, _destination):F2}m).",
                LogCategory.Animation);
            RaiseMoveEnded();
        }

        /// <summary>
        ///     Announces the end of a move. A graceful stop reports as <c>false</c> however
        ///     smoothly it landed: the character reached its braking point, not the place it was
        ///     sent to, and a caller told "arrived" would carry on to the next step of an errand it
        ///     never finished.
        /// </summary>
        private void RaiseMoveEnded()
        {
            bool arrived = !_stoppingShort;
            _stoppingShort = false;
            MoveEnded?.Invoke(arrived);
        }

        private void Awake()
        {
            if (_agent == null && !TryGetComponent(out _agent))
            {
                _agent = gameObject.AddComponent<NavMeshAgent>();
                _ownsAgent = true;
                ConvaiLogger.Info(
                    $"[ConvaiNavMeshLocomotion] '{name}' had no NavMeshAgent — added one with default settings.",
                    LogCategory.Animation);
            }
        }

        private void OnEnable()
        {
            if (_agent == null) return;

            CaptureAgentState();

            // The animation system owns rotation; the agent owns position.
            _agent.updateRotation = false;
            _agent.autoBraking = true;
            _agent.acceleration = _acceleration;
        }

        private void OnDisable()
        {
            RestoreAgentState();
        }

        private void OnDestroy()
        {
            RestoreAgentState();
            if (!_ownsAgent || _agent == null) return;
            if (UnityEngine.Application.isPlaying) Destroy(_agent);
            else DestroyImmediate(_agent);
            _agent = null;
        }

        private void CaptureAgentState()
        {
            if (_agentStateCaptured || _agent == null) return;
            _initialUpdateRotation = _agent.updateRotation;
            _initialUpdatePosition = _agent.updatePosition;
            _initialAutoBraking = _agent.autoBraking;
            _initialAcceleration = _agent.acceleration;
            _initialSpeed = _agent.speed;
            _initialIsStopped = _agent.isOnNavMesh && _agent.isStopped;
            _initialRotationDrivenExternally = RotationDrivenExternally;
            _agentStateCaptured = true;
        }

        private void RestoreAgentState()
        {
            if (!_agentStateCaptured) return;

            EndRootAlignment(transform.position);
            EndManagedMotion();
            _motionStartGated = false;
            _animationGatesMotionStart = false;
            _frozen = false;

            if (_agent != null)
            {
                _agent.updateRotation = _initialUpdateRotation;
                _agent.updatePosition = _initialUpdatePosition;
                _agent.autoBraking = _initialAutoBraking;
                _agent.acceleration = _initialAcceleration;
                _agent.speed = _initialSpeed;
                if (_agent.isOnNavMesh)
                    _agent.isStopped = _initialIsStopped;
            }

            RotationDrivenExternally = _initialRotationDrivenExternally;
            _agentStateCaptured = false;
        }

        private void Update()
        {
            if (_agent == null) return;

            TickArrival();
            TickRotation();
            PushTravelIntent();
        }

        /// <summary>
        ///     Hands this move's facts to the character's travel intent, which is the seam peers read
        ///     to behave differently while travelling. Nothing is created here — only a move that
        ///     already provisioned one in <see cref="MoveTo" /> reports.
        /// </summary>
        /// <remarks>
        ///     The steering direction is preferred over the agent's velocity because velocity is zero
        ///     on the first frames of a move and during an animation-gated start, which would read as
        ///     "not travelling" exactly while the character is committing to the walk.
        /// </remarks>
        private void PushTravelIntent()
        {
            if (_travelIntent == null) return;

            if (!IsMoving)
            {
                _travelIntent.PushLocomotionState(false, Vector3.zero, 0f, float.PositiveInfinity);
                return;
            }

            Vector3 direction = _agent.steeringTarget - transform.position;
            if (direction.sqrMagnitude < 1e-4f)
                direction = _agent.velocity;

            float reference = Mathf.Max(0.1f, _jogSpeed);
            _travelIntent.PushLocomotionState(true, direction, Speed / reference, RemainingDistance);
        }

        /// <summary>
        ///     Finds or adds the character's travel intent. Cached — a move retarget must not pay a
        ///     <c>GetComponent</c>, and the component outlives any individual move.
        /// </summary>
        private void EnsureTravelIntent() =>
            _travelIntent ??= ConvaiTravelIntent.EnsureOn(gameObject);

        /// <summary>
        ///     Declares what this journey is about, so a peer can check on it periodically rather
        ///     than fixating. Safe before any move: the intent is provisioned on demand.
        /// </summary>
        internal void SetTravelSubject(Transform subject)
        {
            EnsureTravelIntent();
            _travelIntent?.SetSubject(subject);
        }

        /// <inheritdoc cref="SetTravelSubject(Transform)" />
        internal void SetTravelSubject(Vector3 worldPosition)
        {
            EnsureTravelIntent();
            _travelIntent?.SetSubject(worldPosition);
        }

        /// <summary>Forgets what the journey was about. The character keeps watching the road.</summary>
        internal void ClearTravelSubject() => _travelIntent?.ClearSubject();

        private void TickArrival()
        {
            if (!_moving || _agent.pathPending) return;

            bool reached = !_agent.hasPath ||
                           (_agent.remainingDistance <= _agent.stoppingDistance + ArrivalTolerance &&
                            Speed <= MovingSpeedEpsilon);
            if (!reached) return;

            _moving = false;
            ConvaiLogger.Debug(
                $"[ConvaiNavMeshLocomotion] '{name}' " +
                (_stoppingShort ? "ran out to a stop" : "destination reached") +
                $" (residual={Vector3.Distance(transform.position, _destination):F2}m).",
                LogCategory.Animation);
            RaiseMoveEnded();
        }

        private void TickRotation()
        {
            if (RotationDrivenExternally || _motionStartGated || !IsMoving) return;

            Vector3 direction = _agent.velocity;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
            {
                direction = _agent.steeringTarget - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude < 1e-4f) return;
            }

            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, _rotationDegreesPerSecond * Time.deltaTime);
        }

        private float ResolveProfileSpeed(float distance)
        {
            // AutoJogDistance/MinJogDistance are authored in metres against the reference
            // rig, like the clip metadata — scaled here at the point of use, never written
            // back to the serialized fields.
            float autoJogDistance = _autoJogDistance * _motionScale;
            float minJogDistance = _minJogDistance * _motionScale;

            // Gait planning: jogging is only committed when the leg has room for the
            // full accelerate → cruise → planted-stop arc; short legs read as a sprint
            // ending in a slammed stop otherwise.
            float baseSpeed = _speedProfile switch
            {
                LocomotionSpeedProfile.Walk => _walkSpeed,
                LocomotionSpeedProfile.Jog => distance >= minJogDistance ? _jogSpeed : _walkSpeed,
                _ => distance >= Mathf.Max(autoJogDistance, minJogDistance) ? _jogSpeed : _walkSpeed
            };

            // Emotion-driven gait scale, 1 (no-op) unless emotional gait is enabled.
            return baseSpeed * _gaitSpeedMultiplier;
        }

        /// <summary>
        ///     Stores this character's rig motion scale so <see cref="ResolveProfileSpeed" />
        ///     scales <c>AutoJogDistance</c>/<c>MinJogDistance</c> at the point of use. The
        ///     serialized fields — authored in metres against the reference rig — are never
        ///     mutated. Called once per build by the controller.
        /// </summary>
        internal void SetMotionScale(float scale)
        {
            _motionScale = scale > 0f ? scale : 1f;
        }

        /// <summary>
        ///     When this component added the <see cref="NavMeshAgent" /> itself (no agent the
        ///     user configured), derives height/radius/baseOffset from the actual rig instead of
        ///     leaving Unity's generic defaults (radius 0.5, height 2) on a character they may
        ///     not fit — a child-sized rig gets a child-sized capsule, a tall rig a tall one. An
        ///     agent the user configured themselves is never touched; that is the whole point of
        ///     the <see cref="_ownsAgent" /> gate.
        /// </summary>
        internal void ConfigureAgentDimensionsFromRig(Animator animator)
        {
            if (!_ownsAgent || _agent == null || animator == null) return;

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            float? headHeightAboveRoot = head != null ? head.position.y - transform.position.y : null;

            (float height, float radius) = NavMeshAgentDimensionResolver.Resolve(headHeightAboveRoot, animator.humanScale);

            _agent.height = height;
            _agent.radius = radius;
            _agent.baseOffset = 0f;

            ConvaiLogger.Info(
                $"[ConvaiNavMeshLocomotion] '{name}' derived NavMeshAgent dimensions from the rig: " +
                $"height={height:F2}m, radius={radius:F2}m, baseOffset=0.",
                LogCategory.Animation);
        }

        // ---------------------------------------------------------------- ILocomotionDrive
        // These members are internal on the public component to keep them out of the
        // customer API; the (internal) ILocomotionDrive interface requires them to be
        // implemented explicitly since implicit implementation would need public access.

        void ILocomotionDrive.FreezeAgent(bool frozen) => FreezeAgent(frozen);
        void ILocomotionDrive.BeginManagedMotion() => BeginManagedMotion();
        void ILocomotionDrive.SetManagedSpeed(float speed) => SetManagedSpeed(speed);
        void ILocomotionDrive.EndManagedMotion() => EndManagedMotion();
        void ILocomotionDrive.ReleaseAnimationStartGate() => ReleaseAnimationStartGate();
        void ILocomotionDrive.CompleteMoveFromAnimation() => CompleteMoveFromAnimation();
        void ILocomotionDrive.SetAnimationStartGate(bool enabled) => SetAnimationStartGate(enabled);
        void IConvaiManagedLocomotion.FreezeAgent(bool frozen) => FreezeAgent(frozen);
        void IConvaiManagedLocomotion.BeginManagedMotion() => BeginManagedMotion();
        void IConvaiManagedLocomotion.SetManagedSpeed(float speed) => SetManagedSpeed(speed);
        void IConvaiManagedLocomotion.EndManagedMotion() => EndManagedMotion();
        void IConvaiManagedLocomotion.ReleaseAnimationStartGate() => ReleaseAnimationStartGate();
        void IConvaiManagedLocomotion.CompleteMoveFromAnimation() => CompleteMoveFromAnimation();
        void IConvaiManagedLocomotion.SetAnimationStartGate(bool enabled) => SetAnimationStartGate(enabled);
        void IConvaiAnchorAlignment.BeginRootAlignment() => BeginRootAlignment();
        void IConvaiAnchorAlignment.EndRootAlignment(Vector3 rootPosition) => EndRootAlignment(rootPosition);

        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos || _agent == null || !_agent.hasPath) return;

            Gizmos.color = Color.cyan;
            Vector3[] corners = _agent.path.corners;
            for (int i = 1; i < corners.Length; i++)
                Gizmos.DrawLine(corners[i - 1], corners[i]);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_destination, 0.15f);

            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, _agent.velocity);
        }
    }
}
