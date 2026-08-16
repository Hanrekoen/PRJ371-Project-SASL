using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Target-loss search behavior: when the player target disappears (line-of-sight
    ///     occlusion or range exit) after the character had been continuously engaged with it
    ///     for at least <see cref="RequiredEngagementSeconds" />, the character does not decay
    ///     straight to ambient — it holds the last known point and performs a short burst of
    ///     searching saccades around it, biased toward the direction the target was last
    ///     moving, before releasing to the normal decay path.
    /// </summary>
    /// <remarks>
    ///     Pure POCO: no scene/transform dependency (the caller passes the observer position as
    ///     a plain <see cref="Vector3" />, not a transform). The caller supplies the player
    ///     candidate's validity, world point, and the character's gaze-origin position every
    ///     tick; this director derives a velocity estimate from the point deltas while the
    ///     target is valid, and produces a substitute world-space gaze point (last known point +
    ///     an offset along the character-relative lateral axis, within a yaw envelope) while
    ///     searching. The search aborts instantly the moment the player becomes valid again, the
    ///     dialogue state goes Idle, or it is disabled.
    /// </remarks>
    internal sealed class SearchDirector
    {
        private enum Phase
        {
            Idle,
            Searching
        }

        /// <summary>Continuous engagement required before a loss triggers a search.</summary>
        private const float RequiredEngagementSeconds = 2f;

        private const float MinFixationHoldSeconds = 0.4f;
        private const float MaxFixationHoldSeconds = 0.9f;
        private const float MinTotalSeconds = 1.5f;
        private const float MaxTotalSecondsSample = 3f;

        /// <summary>Yaw envelope for search fixation offsets (degrees).</summary>
        private const float MaxYawOffsetDegrees = 15f;

        /// <summary>Coarse degrees→meters conversion for the substitute world-space point (no observer distance is known to this POCO).</summary>
        private const float MetersPerDegree = 0.035f;

        /// <summary>Head participation while searching — a searching glance is head-light, not a full re-orientation.</summary>
        public const float SearchHeadContribution = 0.6f;

        private Phase _phase = Phase.Idle;

        private bool _hasLastPoint;
        private Vector3 _lastPoint;
        private Vector3 _lastObserverPosition;
        private Vector3 _velocity;
        private bool _prevPlayerValid;
        private float _engagedSeconds;

        private Vector3 _lateralAxis = Vector3.right;
        private float _lateralSign = 1f;
        private int _fixationCount;
        private int _fixationIndex;
        private float _fixationTimer;
        private float _fixationHoldSeconds;
        private float _totalTimer;
        private float _totalDurationSeconds;
        private float _currentYawOffsetDegrees;
        private Vector3 _searchPoint;
        private bool _fixationChangedThisTick;

        /// <summary>Whether a search is currently substituting for the lost target.</summary>
        public bool SearchActive => _phase == Phase.Searching;

        /// <summary>Current world-space search fixation point (valid only while <see cref="SearchActive" />).</summary>
        public Vector3 SearchPoint => _searchPoint;

        /// <summary>Head participation weight to use while searching.</summary>
        public float HeadContribution => SearchHeadContribution;

        /// <summary>Current fixation's yaw offset (degrees) around the last known point — test/diagnostics seam.</summary>
        public float CurrentYawOffsetDegrees => _currentYawOffsetDegrees;

        /// <summary>True on the tick a new fixation begins (search onset or fixation advance) — the eye stage should treat this as a fresh target.</summary>
        public bool FixationChangedThisTick => _fixationChangedThisTick;

        /// <summary>True on the tick a search transitions from active to inactive (completed, aborted, or suppressed).</summary>
        public bool JustReleased { get; private set; }

        /// <summary>Advances the director by one tick.</summary>
        /// <param name="playerValid">Whether the player candidate is currently valid (in range, unoccluded).</param>
        /// <param name="engagedWithPlayer">Whether the resolved gaze directive is currently committed to the player.</param>
        /// <param name="playerPoint">The player candidate's current world point (ignored unless <paramref name="playerValid" />).</param>
        /// <param name="observerPosition">
        ///     The character's gaze origin (head/eye pivot) this tick — used to build a
        ///     character-relative lateral basis for the search offsets and velocity bias so
        ///     they read as "searching sideways" regardless of which way the character faces.
        /// </param>
        /// <param name="dialogueState">Current dialogue state — search never runs during Idle.</param>
        /// <param name="featureEnabled">Profile toggle.</param>
        /// <param name="maxSearchSeconds">Hard cap on total search duration.</param>
        /// <param name="deltaTime">Tick delta time.</param>
        /// <param name="random">Deterministic random stream, threaded by ref.</param>
        /// <returns><see cref="SearchActive" /> after this tick.</returns>
        public bool Tick(
            bool playerValid,
            bool engagedWithPlayer,
            Vector3 playerPoint,
            Vector3 observerPosition,
            DialogueState dialogueState,
            bool featureEnabled,
            float maxSearchSeconds,
            float deltaTime,
            ref DeterministicEmbodimentRandom random)
        {
            JustReleased = false;
            bool wasActive = _phase == Phase.Searching;

            if (playerValid)
            {
                UpdateVelocity(playerPoint, deltaTime);
                _lastObserverPosition = observerPosition;
                _engagedSeconds = engagedWithPlayer ? _engagedSeconds + deltaTime : 0f;
                _prevPlayerValid = true;

                // The target reappeared: abort instantly, normal reacquisition takes over.
                EndSearch();
                if (wasActive) JustReleased = true;
                return false;
            }

            bool justLost = _prevPlayerValid;
            _prevPlayerValid = false;

            bool conversationActive = dialogueState != DialogueState.Idle;
            if (!featureEnabled || !conversationActive)
            {
                EndSearch();
                _engagedSeconds = 0f;
                if (wasActive) JustReleased = true;
                return false;
            }

            if (_phase == Phase.Idle)
            {
                if (justLost && _hasLastPoint && _engagedSeconds >= RequiredEngagementSeconds)
                    BeginSearch(maxSearchSeconds, ref random);
                else
                    return false;
            }

            TickSearch(deltaTime, ref random);
            if (wasActive && _phase == Phase.Idle) JustReleased = true;
            return _phase == Phase.Searching;
        }

        /// <summary>
        ///     Immediately ends an active search without waiting for completion or a player
        ///     reacquisition — e.g. a higher-priority scripted target (an explicit glance) has
        ///     taken over, so resuming a stale search afterwards would look robotic. A no-op
        ///     when no search is active. Does not re-arm the current loss event: the search will
        ///     not restart until the next fresh player-loss transition.
        /// </summary>
        public void Abort()
        {
            bool wasActive = _phase == Phase.Searching;
            EndSearch();
            if (wasActive) JustReleased = true;
        }

        /// <summary>Clears all internal state (disable/rebind).</summary>
        public void Reset()
        {
            _phase = Phase.Idle;
            _hasLastPoint = false;
            _lastPoint = Vector3.zero;
            _lastObserverPosition = Vector3.zero;
            _velocity = Vector3.zero;
            _prevPlayerValid = false;
            _engagedSeconds = 0f;
            _lateralAxis = Vector3.right;
            _lateralSign = 1f;
            _fixationCount = 0;
            _fixationIndex = 0;
            _fixationTimer = 0f;
            _fixationHoldSeconds = 0f;
            _totalTimer = 0f;
            _totalDurationSeconds = 0f;
            _currentYawOffsetDegrees = 0f;
            _searchPoint = Vector3.zero;
            _fixationChangedThisTick = false;
            JustReleased = false;
        }

        private void UpdateVelocity(Vector3 currentPoint, float deltaTime)
        {
            if (_hasLastPoint && deltaTime > 1e-5f)
            {
                Vector3 instantVelocity = (currentPoint - _lastPoint) / deltaTime;
                _velocity = Vector3.Lerp(_velocity, instantVelocity, 0.5f);
            }

            _lastPoint = currentPoint;
            _hasLastPoint = true;
        }

        private void BeginSearch(float maxSearchSeconds, ref DeterministicEmbodimentRandom random)
        {
            _phase = Phase.Searching;
            _totalTimer = 0f;
            float sampledTotal = random.Range(MinTotalSeconds, MaxTotalSecondsSample);
            _totalDurationSeconds = Mathf.Min(sampledTotal, Mathf.Max(0.1f, maxSearchSeconds));
            _fixationCount = random.Value < 0.5f ? 2 : 3;
            _fixationIndex = 0;

            // Character-relative lateral basis: perpendicular to the (horizontal) view
            // direction from the observer to the last known point, not a world axis — so the
            // search reads as sideways regardless of which way the character faces.
            Vector3 viewDir = _lastPoint - _lastObserverPosition;
            viewDir.y = 0f;
            _lateralAxis = viewDir.sqrMagnitude > 0.0001f
                ? Vector3.Cross(Vector3.up, viewDir.normalized)
                : Vector3.right;

            float lateralVelocity = Vector3.Dot(_velocity, _lateralAxis);
            _lateralSign = Mathf.Abs(lateralVelocity) > 0.001f ? Mathf.Sign(lateralVelocity) : (random.Value < 0.5f ? -1f : 1f);

            SampleFixation(0, ref random);
        }

        private void TickSearch(float deltaTime, ref DeterministicEmbodimentRandom random)
        {
            _fixationChangedThisTick = false;
            _totalTimer += deltaTime;
            _fixationTimer += deltaTime;

            if (_totalTimer >= _totalDurationSeconds)
            {
                EndSearch();
                return;
            }

            if (_fixationTimer >= _fixationHoldSeconds)
            {
                _fixationIndex++;
                if (_fixationIndex >= _fixationCount)
                {
                    EndSearch();
                    return;
                }

                SampleFixation(_fixationIndex, ref random);
            }
        }

        private void SampleFixation(int index, ref DeterministicEmbodimentRandom random)
        {
            float sign;
            if (index == 0)
            {
                // The first (and strongest) fixation always searches toward the direction the
                // target was last moving — the natural "it went that way" instinct.
                sign = _lateralSign;
            }
            else
            {
                float sameDirectionBias = index == 1 ? 0.75f : 0.6f;
                sign = random.Value < sameDirectionBias ? _lateralSign : -_lateralSign;
            }

            float magnitude = index == 0
                ? random.Range(8f, MaxYawOffsetDegrees)
                : random.Range(3f, MaxYawOffsetDegrees);

            _currentYawOffsetDegrees = Mathf.Clamp(sign * magnitude, -MaxYawOffsetDegrees, MaxYawOffsetDegrees);
            _fixationHoldSeconds = random.Range(MinFixationHoldSeconds, MaxFixationHoldSeconds);
            _fixationTimer = 0f;
            _fixationChangedThisTick = true;

            _searchPoint = _lastPoint + _lateralAxis * (_currentYawOffsetDegrees * MetersPerDegree);
        }

        private void EndSearch()
        {
            _phase = Phase.Idle;
            _fixationCount = 0;
            _fixationIndex = 0;
            _fixationTimer = 0f;
            _fixationHoldSeconds = 0f;
            _totalTimer = 0f;
            _totalDurationSeconds = 0f;
            _currentYawOffsetDegrees = 0f;
            _fixationChangedThisTick = false;
        }
    }
}
