using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.BodyAnimation.Core.Layers;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.SceneMetadata;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Referential gestures. Subscribes
    ///     to the character's spoken-line feed the same way
    ///     <c>Convai.Modules.Gaze.Providers.GazeReferentialGlances</c> does
    ///     (<c>ConvaiCharacter.OnTranscriptReceived</c>, final utterance only), matches the line
    ///     via <see cref="ReferentialGestureMatcher" />, and — for at most one matched class per
    ///     line, in priority order (IndicateObject &gt; PalmToPlayer &gt; HandToChest &gt;
    ///     Enumerate), subject to a per-class cooldown and a global refractory window — plays it
    ///     through <see cref="TalkLayer.TryPlayReferentialGesture" />, the same additive
    ///     one-shot machinery onset-driven beat gestures use. Conservative by default: fully
    ///     inert until the animation set has at least one action tagged with a referential cue.
    /// </summary>
    /// <remarks>
    ///     Owned by <see cref="Components.ConvaiBodyAnimationController" />: constructed once
    ///     the runtime is built (mirrors <see cref="ConversationalGesturePerformer" />'s
    ///     construction), discarded on teardown. Never throws on a null/degraded set — every
    ///     lookup degrades to "no match" or "no fire".
    /// </remarks>
    internal sealed class ReferentialGestureDirector
    {
        public event Action<GestureCueKind, bool> GestureResolved;
        private readonly ConvaiBodyAnimationConfig _config;
        private readonly TalkLayer _talkLayer;
        private readonly ActionLayer _actionLayer;
        private readonly PointingLayer _pointingLayer;

        private readonly Dictionary<GestureCueKind, float> _classCooldownUntil = new();
        private readonly List<string> _nameScratch = new(16);

        private float _globalRefractoryUntil;
        private bool _resolutionPublishedThisUtterance;

        public ReferentialGestureDirector(
            ConvaiBodyAnimationConfig config, TalkLayer talkLayer, ActionLayer actionLayer, PointingLayer pointingLayer)
        {
            _config = config;
            _talkLayer = talkLayer;
            _actionLayer = actionLayer;
            _pointingLayer = pointingLayer;
        }

        /// <summary>
        ///     Feeds a spoken line to the director as if the character had just said it — the
        ///     path the final backend transcript takes (<c>ConvaiCharacter.OnTranscriptReceived</c>,
        ///     <c>isFinal == true</c>). Reads registered object names from
        ///     <see cref="ConvaiMetadataRegistry" />, same source the Gaze referential-glance
        ///     precedent uses, then evaluates at the current time.
        /// </summary>
        public void NotifyUtterance(string utterance)
        {
            _nameScratch.Clear();
            ConvaiObjectMetadata[] all = ConvaiMetadataRegistry.GetValidMetadata();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) _nameScratch.Add(all[i].ObjectName);

            TryFireForUtterance(utterance, _nameScratch, Time.time);
        }

        /// <summary>
        ///     Pure decision + fire path, internal so tests can drive it without a scene,
        ///     character, or metadata registry. Matches <paramref name="utterance" />, then —
        ///     for the highest-priority matched class not currently on cooldown, and only when
        ///     the global refractory window has elapsed — attempts to play the gesture. Returns
        ///     <c>true</c> exactly when a gesture was fired (at most one per call). A failed
        ///     attempt (no tagged content, or the talk layer refuses due to suppression) falls
        ///     through to the next matched class rather than aborting the whole line.
        /// </summary>
        internal bool TryFireForUtterance(string utterance, IReadOnlyList<string> objectNames, float now)
        {
            if (_config == null || !_config.EnableReferentialGestures) return false;
            if (now < _globalRefractoryUntil) return false;
            _resolutionPublishedThisUtterance = false;

            ReferentialGestureMatcher.MatchResult match = ReferentialGestureMatcher.Match(utterance, objectNames);
            if (!match.HasMatch) return false;

            if (match.HasObjectMention && TryFireClass(GestureCueKind.IndicateObject, now)) return true;
            if (match.SecondPerson && TryFireClass(GestureCueKind.PalmToPlayer, now)) return true;
            if (match.FirstPerson && TryFireClass(GestureCueKind.HandToChest, now)) return true;
            if (match.Ordinal && TryFireClass(GestureCueKind.Enumerate, now)) return true;

            return false;
        }

        private bool TryFireClass(GestureCueKind kind, float now)
        {
            if (_classCooldownUntil.TryGetValue(kind, out float until) && now < until) return false;
            if (_talkLayer == null) return false;

            // An action carrying AllowConversationOverlays (a seated-conversation hold)
            // must not suppress referential gestures — it's a conversation pose, not arm
            // ownership. SuppressesConversationOverlays folds that check in at the source.
            bool suppressedByPeers =
                (_actionLayer != null && _actionLayer.SuppressesConversationOverlays) ||
                (_pointingLayer != null && _pointingLayer.IsActive);
            bool played = _talkLayer.TryPlayReferentialGesture(kind, suppressedByPeers);
            if (played)
            {
                PublishOnce(kind, true);
                ArmCooldowns(kind, now);
                return true;
            }

            // Not played — and the reason decides what happens next. Only ONE refusal is a
            // hand-off: the set authors no clip tagged with this cue, so the gesture the
            // character meant can still be performed by a peer performer. Every other refusal
            // (a peer layer owns the arms, the talk layer is not in a speaking pose, an
            // interruption froze it) means this gesture must not happen at all — so nothing is
            // published, nothing is handed over, and no budget is consumed, leaving the next
            // spoken line a fair attempt.
            if (suppressedByPeers || _talkLayer.HasContentForCue(kind)) return false;

            PublishOnce(kind, false);

            // The window is armed on the hand-off exactly as it is on a local performance:
            // either way the character's referential-gesture budget for this window is spent.
            // Without this, a set with no referential clips would hand one off on every single
            // line while an authored set is held to one per window.
            ArmCooldowns(kind, now);
            return false;
        }

        private void PublishOnce(GestureCueKind kind, bool authoredPlayed)
        {
            if (_resolutionPublishedThisUtterance) return;

            _resolutionPublishedThisUtterance = true;
            GestureResolved?.Invoke(kind, authoredPlayed);
        }

        private void ArmCooldowns(GestureCueKind kind, float now)
        {
            _classCooldownUntil[kind] = now + Mathf.Max(0f, _config.ReferentialGestureClassCooldownSeconds);
            _globalRefractoryUntil = now + Mathf.Max(0f, _config.ReferentialGestureRefractorySeconds);
        }
    }
}
