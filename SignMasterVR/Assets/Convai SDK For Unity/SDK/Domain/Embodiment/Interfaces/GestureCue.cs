namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Semantic categories a scripted or backend-driven gesture request can carry. Consumed
    ///     by <see cref="IConversationalGesturePerformer" /> to resolve a matching clip.
    /// </summary>
    /// <remarks>
    ///     <see cref="Emphatic" /> and <see cref="Beat" /> are data-model slots reserved for a
    ///     future co-speech gesticulation director: no shipped animation set tags a clip with
    ///     either kind today, so cues of these kinds always resolve to "no mapping" until content
    ///     exists. They are part of the enum now so authored content and future callers do not
    ///     need a breaking change when that content lands.
    /// </remarks>
    public enum GestureCueKind
    {
        /// <summary>No gesture requested. Always refused by <see cref="IConversationalGesturePerformer.TryPerform" />.</summary>
        None = 0,

        /// <summary>An affirmative beat (e.g. "yes", agreement).</summary>
        Affirmative = 1,

        /// <summary>A negative beat (e.g. "no", disagreement).</summary>
        Negative = 2,

        /// <summary>A greeting or farewell beat (e.g. "hi", "bye").</summary>
        Greeting = 3,

        /// <summary>An uncertainty/thinking beat (e.g. "hmm", pondering).</summary>
        Uncertain = 4,

        /// <summary>
        ///     An emphatic co-speech beat. Reserved: no shipped content tags this kind yet
        ///     (see remarks on <see cref="GestureCueKind" />).
        /// </summary>
        Emphatic = 5,

        /// <summary>
        ///     A generic rhythmic co-speech beat. Reserved: no shipped content tags this kind
        ///     yet (see remarks on <see cref="GestureCueKind" />).
        /// </summary>
        Beat = 6,

        /// <summary>
        ///     Referential-gesture content tag — a small palm-open-toward-player
        ///     gesture, fired when the character's spoken line contains a second-person word
        ///     ("you", "your", "yours", "yourself"). Reserved: no shipped content tags this
        ///     kind yet — the referential-gesture director is fully inert until an
        ///     <c>ActionEntry</c> is authored with it.
        /// </summary>
        PalmToPlayer = 7,

        /// <summary>
        ///     Referential-gesture content tag — a hand-to-chest gesture, fired when
        ///     the character's spoken line contains a first-person word ("I", "me", "my",
        ///     "mine", "myself"). Reserved: no shipped content tags this kind yet — the
        ///     referential-gesture director is fully inert until an <c>ActionEntry</c> is
        ///     authored with it.
        /// </summary>
        HandToChest = 8,

        /// <summary>
        ///     Referential-gesture content tag — an indicate/point-toward gesture,
        ///     fired when the character's spoken line mentions a registered scene object's
        ///     name. Reserved: no shipped content tags this kind yet — the referential-gesture
        ///     director is fully inert until an <c>ActionEntry</c> is authored with it.
        /// </summary>
        IndicateObject = 9,

        /// <summary>
        ///     Referential-gesture content tag — an enumerate beat, fired when the
        ///     character's spoken line contains an ordinal or number word ("first", "second",
        ///     "three", …). Reserved: no shipped content tags this kind yet — the
        ///     referential-gesture director is fully inert until an <c>ActionEntry</c> is
        ///     authored with it.
        /// </summary>
        Enumerate = 10
    }

    /// <summary>
    ///     A single request to perform a semantic gesture, e.g. from a scripted call, a backend
    ///     action, or a reacting affirm/negate beat. Zero-alloc value type.
    /// </summary>
    public readonly struct GestureCue
    {
        /// <summary>The semantic category of gesture requested.</summary>
        public GestureCueKind Kind { get; }

        /// <summary>Relative intensity/emphasis of the request, typically 0..1+. Default 1.</summary>
        public float Intensity { get; }

        public GestureCue(GestureCueKind kind, float intensity = 1f)
        {
            Kind = kind;
            Intensity = intensity;
        }

        /// <summary>The no-op cue: <see cref="GestureCueKind.None" /> at zero intensity.</summary>
        public static GestureCue None => new(GestureCueKind.None, 0f);
    }

    /// <summary>
    ///     How much of a <see cref="IConversationalGesturePerformer" />'s skeleton is currently
    ///     unavailable to procedural body-language systems layered on top of it.
    /// </summary>
    public enum GestureSuppression
    {
        /// <summary>Nothing is suppressed; posture, breath, and gesture cues are all eligible.</summary>
        None = 0,

        /// <summary>
        ///     The upper body is busy (e.g. locomotion, upper-body talk coverage): semantic
        ///     gesture clips are refused, but posture (at reduced weight) and breath stay live.
        /// </summary>
        UpperBody = 1,

        /// <summary>
        ///     The whole body is busy (e.g. a full-body action, a turn-in-place, or full-body
        ///     talk coverage): gesture cues are refused and procedural posture/breath should
        ///     fade to zero to avoid fighting the active full-body motion.
        /// </summary>
        FullBody = 2
    }

    /// <summary>Terminal outcome reported for a <see cref="GestureCue" /> performance.</summary>
    public enum GesturePerformanceResult
    {
        /// <summary>The gesture clip chain played to completion.</summary>
        Completed = 0,

        /// <summary>The gesture was interrupted mid-playback by another action/gesture.</summary>
        Interrupted = 1,

        /// <summary>
        ///     The gesture was cancelled before or during playback without a normal finish
        ///     (e.g. the performer or its owning component was disabled/torn down).
        /// </summary>
        Cancelled = 2
    }
}
