using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using Convai.Modules.BodyAnimation.Core.Layers;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Ambient idle life: when nobody has engaged the character in conversation for
    ///     a while, performs Ambient-tagged action entries (stretching, tidying, examining) on a
    ///     randomized cadence instead of standing motionless, and winds the activity down through
    ///     the entry's own graceful outro (<see cref="ActionLayer.RequestStop" />) the instant
    ///     <see cref="DialogueState" /> leaves Idle. Conservative by default: fully inert until
    ///     the animation set has at least one action tagged <see cref="ActionEntry.Ambient" />.
    /// </summary>
    /// <remarks>
    ///     Owned by <see cref="Components.ConvaiBodyAnimationController" />: constructed once the
    ///     runtime is built (mirrors <see cref="ReferentialGestureDirector" />'s construction),
    ///     ticked every embodiment tick, discarded on teardown. Pure poll-based state machine —
    ///     no event subscriptions — so the action layer's own <see cref="ActionLayer.ActiveActionName" />
    ///     is the single source of truth for "is my activity still running"; a manual
    ///     <c>PlayAction</c>/<c>StopAction</c> call that steals or clears the layer out from
    ///     under an ambient activity is simply detected on the next tick.
    /// </remarks>
    internal sealed class AmbientActivityDirector
    {
        /// <summary>Jitter fraction applied to <see cref="ConvaiBodyAnimationConfig.AmbientIntervalSeconds" />.</summary>
        private const float CadenceJitterFraction = 0.4f;

        private readonly ConvaiBodyAnimationConfig _config;
        private readonly ActionLayer _actionLayer;
        private readonly ConvaiBodyAnimationSet _set;
        private readonly Transform _characterRoot;
        private readonly AnimTrace _trace;

        private DeterministicEmbodimentRandom _random;

        private float _idleElapsedSeconds;
        private bool _cadenceRolled;
        private float _cadenceRemainingSeconds;
        private ActionEntry _activeEntry;
        private int _lastFiredIndex = -1;

        public AmbientActivityDirector(
            ConvaiBodyAnimationConfig config,
            ActionLayer actionLayer,
            ConvaiBodyAnimationSet set,
            Transform characterRoot,
            AnimTrace trace,
            uint randomSeed)
        {
            _config = config;
            _actionLayer = actionLayer;
            _set = set;
            _characterRoot = characterRoot;
            _trace = trace;
            _random = new DeterministicEmbodimentRandom(randomSeed);
        }

        /// <summary>True while an ambient activity this director started is currently playing.</summary>
        internal bool IsRunningAmbientActivityForTests => _activeEntry != null;

        /// <summary>Name of the ambient action last requested to stop, for tests. Empty when none.</summary>
        internal string LastStoppedActionNameForTests { get; private set; } = string.Empty;

        /// <summary>
        ///     Advances the delay → cadence → fire lifecycle. Gates that only matter at the
        ///     moment of firing (peer action busy, moving, a PlayActionAt runner in flight,
        ///     player proximity) are re-checked every attempt rather than blocking the idle
        ///     timer/cadence roll — a temporarily busy character simply defers to the next
        ///     cadence window instead of losing its accumulated idle time.
        /// </summary>
        /// <param name="hasConversationAnchor">
        ///     Whether the controller's single per-tick <c>ConversationAnchorResolver</c>
        ///     resolved a conversation partner position this tick. No anchor means the
        ///     proximity gate cannot block a fire, matching the previous "no camera" behavior.
        /// </param>
        /// <param name="conversationAnchor">World position of the resolved anchor; meaningful
        /// only when <paramref name="hasConversationAnchor" /> is true.</param>
        public void Tick(
            DialogueState dialogueState, float deltaTime, bool locomotionMoving, bool playActionAtRunnerActive,
            bool hasConversationAnchor = false, Vector3 conversationAnchor = default)
        {
            if (_config == null || !_config.EnableAmbientActivities)
            {
                StopActiveIfNeeded();
                ResetIdleClock();
                return;
            }

            if (dialogueState != DialogueState.Idle)
            {
                StopActiveIfNeeded();
                ResetIdleClock();
                return;
            }

            _idleElapsedSeconds += deltaTime;

            if (_activeEntry != null)
            {
                // The action layer's own state is the source of truth: our activity may have
                // finished on its own, or been superseded by an unrelated PlayAction call.
                bool stillOurs = _actionLayer != null && _actionLayer.IsActive &&
                                  _actionLayer.ActiveActionName == _activeEntry.ActionName;
                if (stillOurs) return;

                _activeEntry = null;
                RollNextCadence();
                return;
            }

            if (_idleElapsedSeconds < _config.AmbientStartDelaySeconds) return;

            if (!_cadenceRolled)
            {
                RollNextCadence();
                return;
            }

            _cadenceRemainingSeconds -= deltaTime;
            if (_cadenceRemainingSeconds > 0f) return;

            TryFire(locomotionMoving, playActionAtRunnerActive, hasConversationAnchor, conversationAnchor);
            RollNextCadence();
        }

        private void RollNextCadence()
        {
            float mean = _config.AmbientIntervalSeconds;
            float jitter = mean * CadenceJitterFraction;
            _cadenceRemainingSeconds = Mathf.Max(0.1f, _random.Range(mean - jitter, mean + jitter));
            _cadenceRolled = true;
        }

        private void ResetIdleClock()
        {
            _idleElapsedSeconds = 0f;
            _cadenceRolled = false;
            _cadenceRemainingSeconds = 0f;
        }

        private void StopActiveIfNeeded()
        {
            if (_activeEntry == null) return;

            LastStoppedActionNameForTests = _activeEntry.ActionName;
            if (_trace is { IsState: true })
                _trace.State($"Ambient activity '{_activeEntry.ActionName}' winding down — conversation engaged.");
            _actionLayer?.RequestStop();
            _activeEntry = null;
        }

        private void TryFire(
            bool locomotionMoving, bool playActionAtRunnerActive,
            bool hasConversationAnchor, Vector3 conversationAnchor)
        {
            if (_actionLayer == null || _actionLayer.IsActive) return;
            if (locomotionMoving || playActionAtRunnerActive) return;
            if (IsPlayerNear(hasConversationAnchor, conversationAnchor)) return;
            if (!TryPickEntry(out ActionEntry entry, out int index)) return;

            BodyAnimationActionHandle handle = _actionLayer.Play(entry, default);
            if (handle == null) return;

            _activeEntry = entry;
            _lastFiredIndex = index;
            if (_trace is { IsState: true })
                _trace.State($"Ambient activity: '{entry.ActionName}' started after {_idleElapsedSeconds:F1}s idle.");
        }

        /// <summary>
        ///     Picks one Ambient-tagged action, deterministically at random (own seeded stream,
        ///     no LINQ/alloc). Avoids repeating the previously-fired entry back-to-back when more
        ///     than one candidate exists. Returns <c>false</c> — silently — when the set has no
        ///     such content, the default for every shipped/existing asset (inert until
        ///     content exists).
        /// </summary>
        private bool TryPickEntry(out ActionEntry entry, out int selectedIndex)
        {
            entry = null;
            selectedIndex = -1;
            IReadOnlyList<ActionEntry> actions = _set?.Actions;
            if (actions == null || actions.Count == 0) return false;

            int matchCount = 0;
            int firstMatchIndex = -1;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionEntry candidate = actions[i];
                if (!candidate.IsValid || !candidate.Ambient) continue;

                if (firstMatchIndex < 0) firstMatchIndex = i;
                matchCount++;
            }

            if (matchCount == 0) return false;

            if (matchCount == 1)
            {
                entry = actions[firstMatchIndex];
                selectedIndex = firstMatchIndex;
                return true;
            }

            // MatchCount > 1: pick uniformly among every Ambient entry except the last-fired one.
            int roll = Mathf.Clamp(Mathf.FloorToInt(_random.Range(0f, matchCount - 1)), 0, matchCount - 2);
            int seen = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionEntry candidate = actions[i];
                if (!candidate.IsValid || !candidate.Ambient || i == _lastFiredIndex) continue;

                if (seen == roll)
                {
                    entry = candidate;
                    selectedIndex = i;
                    return true;
                }
                seen++;
            }

            return false;
        }

        /// <summary>
        ///     Whether the conversation partner is within <see cref="ConvaiBodyAnimationConfig.AmbientSuppressDistance" />
        ///     of the character. Resolved from the controller's single per-tick
        ///     <c>ConversationAnchorResolver</c> anchor instead of this director's own
        ///     camera lookup. Returns <c>false</c> (never blocking a fire) when no anchor was
        ///     resolved this tick — the resolver itself owns the once-only degradation log.
        /// </summary>
        private bool IsPlayerNear(bool hasConversationAnchor, Vector3 conversationAnchor)
        {
            if (_characterRoot == null || !hasConversationAnchor) return false;

            float distance = Vector3.Distance(_characterRoot.position, conversationAnchor);
            return distance < _config.AmbientSuppressDistance;
        }
    }
}
