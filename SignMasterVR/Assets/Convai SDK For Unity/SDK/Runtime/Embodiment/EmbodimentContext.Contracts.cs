using Convai.Domain.Embodiment.Interfaces;
using Convai.Runtime.Animation;
using Convai.Runtime.Animation.ProceduralPose;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     The typed read side of <see cref="EmbodimentContext" />'s cross-module contracts.
    /// </summary>
    /// <remarks>
    ///     Each accessor is one <see cref="CharacterServiceRegistry" /> probe, exposed as a named
    ///     property rather than making every consumer spell <c>GetService&lt;T&gt;()</c>: typing
    ///     <c>Context.</c> is how a module author discovers what a character offers. The registry is
    ///     the mechanism; these accessors are only vocabulary over it, kept in their own file so the
    ///     composition root next door stays about composition.
    /// </remarks>
    public sealed partial class EmbodimentContext
    {
        // Typed accessors over the registry — see the class remarks above.

        /// <summary>Currently registered conversation flow source (if any).</summary>
        internal IConversationFlowSource ConversationFlowSource => Services.Get<IConversationFlowSource>();

        /// <summary>Currently registered speech energy source (if any).</summary>
        internal ISpeechEnergyProvider SpeechEnergyProvider => Services.Get<ISpeechEnergyProvider>();

        /// <summary>Currently registered emotion state source (if any).</summary>
        internal IEmotionStateSource EmotionStateSource => Services.Get<IEmotionStateSource>();

        /// <summary>
        ///     Currently registered per-frame emotion state source (if any). A first-class contract
        ///     rather than something consumers discover by downcasting
        ///     <see cref="EmotionStateSource" />.
        /// </summary>
        internal IEmotionStateFrameSource EmotionStateFrameSource => Services.Get<IEmotionStateFrameSource>();

        /// <summary>Currently registered emotion mouth weight provider (if any).</summary>
        internal IEmotionMouthWeightProvider EmotionMouthProvider => Services.Get<IEmotionMouthWeightProvider>();

        /// <summary>Currently registered gaze source (if any).</summary>
        internal IGazeSource GazeSource => Services.Get<IGazeSource>();

        /// <summary>Currently registered full-body reorientation handler (if any).</summary>
        internal ICharacterReorientationHandler ReorientationHandler => Services.Get<ICharacterReorientationHandler>();

        /// <summary>Currently registered head-gesture channel (if any).</summary>
        internal IHeadGestureChannel HeadGestureChannel => Services.Get<IHeadGestureChannel>();

        /// <summary>Currently registered conversational gesture performer (if any).</summary>
        internal IConversationalGesturePerformer ConversationalGesturePerformer =>
            Services.Get<IConversationalGesturePerformer>();

        /// <summary>Currently registered body language source (if any).</summary>
        internal IBodyLanguageSource BodyLanguageSource => Services.Get<IBodyLanguageSource>();

        /// <summary>
        ///     Currently registered conversational motion budget (if any) — the Body Animation
        ///     talk-overlay owner's negotiation contract. A peer conversational-behavior module
        ///     reads this to share the upper body instead of ducking away from it; absence degrades
        ///     to the <see cref="IConversationalGesturePerformer" />-only binary-suppression contract.
        /// </summary>
        internal IConversationalMotionBudget ConversationalMotionBudget =>
            Services.Get<IConversationalMotionBudget>();

        /// <summary>Optional shared plan for phrase timing and discrete co-speech accents.</summary>
        internal ICoSpeechPerformanceSource CoSpeechPerformanceSource => Services.Get<ICoSpeechPerformanceSource>();

        /// <summary>
        ///     Currently registered eye-appearance driver (if any) — the optional pupil dilation
        ///     seam. Gaze publishes a normalized arousal signal here each tick when a driver is
        ///     registered; absence is a single null check, no per-frame cost.
        /// </summary>
        internal IEyeAppearanceDriver EyeAppearanceDriver => Services.Get<IEyeAppearanceDriver>();

        /// <summary>
        ///     Currently registered brow-cue sink (if any) — the optional eyebrow-gaze coordination
        ///     seam. Gaze publishes one-shot brow cues here; absence is a single null check.
        /// </summary>
        internal IBrowCueSink BrowCueSink => Services.Get<IBrowCueSink>();

        /// <summary>
        ///     Currently registered gaze glance handler (if any) — the point-glance coordination
        ///     seam. Body Animation requests a brief glance here when the character starts pointing.
        /// </summary>
        internal IGazeGlanceHandler GlanceHandler => Services.Get<IGazeGlanceHandler>();

        /// <summary>
        ///     Currently registered exertion source (if any) — the exertion-breathing seam. Body
        ///     Animation publishes a normalized locomotion-effort signal here; a peer module folds it
        ///     into breathing rate/depth when present, and degrades to a multiplier of exactly 1
        ///     when absent.
        /// </summary>
        internal IExertionSource ExertionSource => Services.Get<IExertionSource>();

        /// <summary>
        ///     Currently registered gaze command handler (if any) — the seam for sustained-gaze and
        ///     glance requests from Runtime action composites (point-at, gaze tour, lead-the-way).
        /// </summary>
        internal IGazeCommandHandler GazeCommandHandler => Services.Get<IGazeCommandHandler>();

        /// <summary>
        ///     Currently registered travel intent source (if any) — where the character is going.
        ///     Gaze reads it to watch the path while walking instead of staring at the destination;
        ///     absence is a single null check and restores the non-travel-aware behavior exactly.
        /// </summary>
        internal ITravelIntentSource TravelIntentSource => Services.Get<ITravelIntentSource>();

        /// <summary>
        ///     Currently registered action activity source (if any) — whether the character is
        ///     carrying out something it was asked to do. Conversation Flow reads it so an
        ///     errand counts as engagement and the character does not decay to Idle halfway
        ///     through one; absence is a single null check.
        /// </summary>
        internal IActionActivitySource ActionActivitySource => Services.Get<IActionActivitySource>();

        /// <summary>
        ///     Currently registered mood command handler (if any) — the seam for mood/emotion-beat
        ///     requests from Runtime action executors and composites.
        /// </summary>
        internal IMoodCommandHandler MoodCommandHandler => Services.Get<IMoodCommandHandler>();

        /// <summary>
        ///     Currently registered procedural pose compositor (if any) — internal Runtime
        ///     infrastructure for the shared spine/shoulder/head-gesture write chain. Gaze's
        ///     torso-aim entry routes through the same guard when one is registered, and degrades to
        ///     its own direct write path when it is not.
        /// </summary>
        internal ProceduralPoseCompositor ProceduralPoseCompositor => Services.Get<ProceduralPoseCompositor>();
    }
}
