using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     Whether one gated feature is turned on in config and whether the animation set actually
    ///     carries the content that feature needs to do anything. A feature can be enabled and
    ///     still be a no-op, or have content sitting unused behind a switch that is off — this is
    ///     what lets a surface say so instead of leaving the user to infer it from silence.
    /// </summary>
    public readonly struct BodyAnimationFeatureState
    {
        /// <summary>Whether config turns this feature on.</summary>
        public bool Enabled { get; }

        /// <summary>Whether the animation set carries the tagged/authored content this feature needs.</summary>
        public bool HasContent { get; }

        /// <summary>True only when the feature is both enabled and has content to act on.</summary>
        public bool IsEffective => Enabled && HasContent;

        /// <summary>
        ///     Enabled, but the set carries nothing for it to act on. Only meaningful for
        ///     features that have no procedural substitute — see
        ///     <see cref="BodyAnimationFeatureAvailability.CollectInertFeatureNames" />.
        /// </summary>
        public bool IsEnabledWithoutContent => Enabled && !HasContent;

        /// <summary>
        ///     The set carries the content, but the switch that would use it is off — the
        ///     authoring mistake that is invisible from the animation set alone.
        /// </summary>
        public bool IsContentWithoutEnable => !Enabled && HasContent;

        public BodyAnimationFeatureState(bool enabled, bool hasContent)
        {
            Enabled = enabled;
            HasContent = hasContent;
        }
    }

    /// <summary>
    ///     Build-time snapshot of what a given (set, config) pair can actually perform. Computed
    ///     once from the pair — allocation-free, safe to call every build and from editor code
    ///     with no live runtime.
    /// </summary>
    /// <remarks>
    ///     Two distinct things are modelled here and must not be confused.
    ///     <para>
    ///         <b>Switch-backed features</b> (<see cref="BeatGestures" />,
    ///         <see cref="ReferentialGestures" />, <see cref="AmbientActivities" />) have a config
    ///         toggle. Only these can be "on but unable to act", and only those among them with no
    ///         procedural substitute are reported as inert — referential gestures always resolve,
    ///         because a set that authors no referential clip hands the cue to a peer performer.
    ///     </para>
    ///     <para>
    ///         <b>Content tiers</b> (<see cref="GestureBrackets" />,
    ///         <see cref="MovingTalkAdditive" />, <see cref="CueTaggedActions" />) have no toggle
    ///         at all: they are attempted whenever the content exists and have a defined, correct
    ///         fallback when it doesn't. Their absence is a content-coverage fact, never a defect,
    ///         and reporting it as "inert" was misleading — it made a healthy set look broken.
    ///     </para>
    /// </remarks>
    public readonly struct BodyAnimationFeatureAvailability
    {
        public BodyAnimationFeatureState BeatGestures { get; }
        public BodyAnimationFeatureState ReferentialGestures { get; }
        public BodyAnimationFeatureState AmbientActivities { get; }
        public BodyAnimationFeatureState GestureBrackets { get; }
        public BodyAnimationFeatureState MovingTalkAdditive { get; }
        public BodyAnimationFeatureState CueTaggedActions { get; }

        /// <summary>
        ///     Number of playable idle variants. One is a complete, working setup — but the idle
        ///     variant interval and the Calmness scalar that stretches it have nothing to swap
        ///     between until there are two, which no other signal makes visible.
        /// </summary>
        public int IdleVariantCount { get; }

        /// <summary>
        ///     Number of playable Talk-pool variants. Below two, <c>Switch Talk Variant On Loop</c>
        ///     and the talk-variant crossfade have nothing to switch to.
        /// </summary>
        public int TalkVariantCount { get; }

        /// <summary>
        ///     Whether any idle or talk variant carries an emotion affinity. Without one, the
        ///     emotion-aware half of variant selection resolves to plain weighted selection.
        /// </summary>
        public bool HasEmotionAffinities { get; }

        public BodyAnimationFeatureAvailability(
            BodyAnimationFeatureState beatGestures,
            BodyAnimationFeatureState referentialGestures,
            BodyAnimationFeatureState ambientActivities,
            BodyAnimationFeatureState gestureBrackets,
            BodyAnimationFeatureState movingTalkAdditive,
            BodyAnimationFeatureState cueTaggedActions,
            int idleVariantCount = 0,
            int talkVariantCount = 0,
            bool hasEmotionAffinities = false)
        {
            BeatGestures = beatGestures;
            ReferentialGestures = referentialGestures;
            AmbientActivities = ambientActivities;
            GestureBrackets = gestureBrackets;
            MovingTalkAdditive = movingTalkAdditive;
            CueTaggedActions = cueTaggedActions;
            IdleVariantCount = idleVariantCount;
            TalkVariantCount = talkVariantCount;
            HasEmotionAffinities = hasEmotionAffinities;
        }

        /// <summary>
        ///     Pure computation from a (set, config) pair — no live controller/runtime required, so
        ///     it can run identically at build time and from Edit Mode tooling. Either argument
        ///     null yields every feature disabled/without content (never inert, since "enabled" is
        ///     false).
        /// </summary>
        public static BodyAnimationFeatureAvailability Compute(
            ConvaiBodyAnimationSet set, ConvaiBodyAnimationConfig config)
        {
            if (set == null || config == null) return default;

            bool hasBeat = false;
            bool hasReferential = false;
            bool hasAmbient = false;
            bool hasCueTagged = false;

            IReadOnlyList<ActionEntry> actions = set.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionEntry action = actions[i];
                if (action == null || !action.IsValid) continue;

                if (action.Ambient) hasAmbient = true;

                switch (action.Cue)
                {
                    case GestureCueKind.Beat:
                    case GestureCueKind.Emphatic:
                        hasBeat = true;
                        break;
                    case GestureCueKind.PalmToPlayer:
                    case GestureCueKind.HandToChest:
                    case GestureCueKind.IndicateObject:
                    case GestureCueKind.Enumerate:
                        hasReferential = true;
                        break;
                    case GestureCueKind.Affirmative:
                    case GestureCueKind.Negative:
                    case GestureCueKind.Greeting:
                    case GestureCueKind.Uncertain:
                        hasCueTagged = true;
                        break;
                }
            }

            bool hasBrackets =
                AnyTalkPoolHasBracket(set.Talks) ||
                AnyTalkPoolHasBracket(set.Listens) ||
                AnyTalkPoolHasBracket(set.Thinks);

            bool hasMovingTalkClip =
                AnyTalkPoolHasMovingClip(set.Talks) ||
                AnyTalkPoolHasMovingClip(set.Listens) ||
                AnyTalkPoolHasMovingClip(set.Thinks);

            return new BodyAnimationFeatureAvailability(
                beatGestures: new BodyAnimationFeatureState(config.EnableBeatGestures, hasBeat),
                referentialGestures: new BodyAnimationFeatureState(config.EnableReferentialGestures, hasReferential),
                ambientActivities: new BodyAnimationFeatureState(config.EnableAmbientActivities, hasAmbient),
                // No toggle exists for either of these: brackets are attempted whenever an entry
                // authors one, and cue-tagged actions are attempted whenever a scripted or
                // reactive request carries one — both are always "enabled" in the config sense.
                gestureBrackets: new BodyAnimationFeatureState(true, hasBrackets),
                movingTalkAdditive: new BodyAnimationFeatureState(config.MovingTalk == MovingTalkMode.Auto, hasMovingTalkClip),
                cueTaggedActions: new BodyAnimationFeatureState(true, hasCueTagged),
                idleVariantCount: CountValidIdles(set.Idles),
                talkVariantCount: CountValidTalks(set.Talks),
                hasEmotionAffinities: AnyIdleHasAffinity(set.Idles) || AnyTalkHasAffinity(set.Talks));
        }

        private static bool AnyTalkPoolHasBracket(IReadOnlyList<TalkEntry> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                TalkEntry entry = pool[i];
                if (entry != null && (entry.HasIntro || entry.HasOutro)) return true;
            }
            return false;
        }

        private static bool AnyTalkPoolHasMovingClip(IReadOnlyList<TalkEntry> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                TalkEntry entry = pool[i];
                if (entry != null && entry.IsValid && entry.ResolveMovingClip() != null) return true;
            }
            return false;
        }

        private static int CountValidIdles(IReadOnlyList<IdleEntry> pool)
        {
            int count = 0;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && pool[i].IsValid) count++;
            return count;
        }

        private static int CountValidTalks(IReadOnlyList<TalkEntry> pool)
        {
            int count = 0;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && pool[i].IsValid) count++;
            return count;
        }

        private static bool AnyIdleHasAffinity(IReadOnlyList<IdleEntry> pool)
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && pool[i].Affinities.Count > 0) return true;
            return false;
        }

        private static bool AnyTalkHasAffinity(IReadOnlyList<TalkEntry> pool)
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && pool[i].Affinities.Count > 0) return true;
            return false;
        }

        /// <summary>
        ///     Appends the plain-English name of each switch-backed feature that is turned on and
        ///     has nothing to act on — the only genuinely dead-switch state. Referential gestures
        ///     are deliberately excluded: they resolve either way, through an authored clip or a
        ///     peer performer. Content tiers without a switch are excluded too; their fallback is
        ///     the intended behavior, not a failure. Returns the number appended.
        /// </summary>
        public int CollectInertFeatureNames(List<string> names)
        {
            if (names == null) return 0;
            int before = names.Count;

            if (BeatGestures.IsEnabledWithoutContent) names.Add("Beat Gestures");
            if (AmbientActivities.IsEnabledWithoutContent) names.Add("Ambient Activities");

            return names.Count - before;
        }

        /// <summary>
        ///     The reciprocal of <see cref="CollectInertFeatureNames" />: the set authors the
        ///     content, but the switch that would play it is off. Without this the user tags a
        ///     clip, sees nothing happen, and has no way to learn why. Returns the number appended.
        /// </summary>
        public int CollectDormantContentNames(List<string> names)
        {
            if (names == null) return 0;
            int before = names.Count;

            if (BeatGestures.IsContentWithoutEnable) names.Add("Beat Gestures");
            if (ReferentialGestures.IsContentWithoutEnable) names.Add("Referential Gestures");
            if (AmbientActivities.IsContentWithoutEnable) names.Add("Ambient Activities");

            return names.Count - before;
        }
    }
}
