namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for the module that owns mood and emotion-beat requests from Runtime action
    ///     executors and composites (set-mood, greet, present-object's optional mood lift, react).
    ///     Runtime requests mood changes through this interface instead of referencing the emotion
    ///     assembly directly; when no handler is registered every request is a no-op that returns
    ///     <c>false</c> so the caller can continue the action without the emotional flourish.
    /// </summary>
    /// <remarks>
    ///     Implemented by the Convai Emotion module's controller and registered on the character's
    ///     embodiment context (mirrors <see cref="ICharacterReorientationHandler" />). The
    ///     parameters mirror the Emotion module's own runtime mood API (label, intensity,
    ///     transition seconds) so a handler implementation can forward calls directly.
    /// </remarks>
    internal interface IMoodCommandHandler
    {
        /// <summary>
        ///     Requests the persistent mood be set to <paramref name="label" /> at
        ///     <paramref name="intensity" /> (0..1), blending over
        ///     <paramref name="transitionSeconds" />. Returns <c>true</c> when the handler accepts
        ///     the request.
        /// </summary>
        bool RequestMood(string label, float intensity, float transitionSeconds);

        /// <summary>
        ///     Requests a short, one-shot emotion beat (a transient expressive flourish that does
        ///     not change the persistent mood) at <paramref name="label" />/
        ///     <paramref name="intensity" /> (0..1). Returns <c>true</c> when the handler accepts
        ///     the request.
        /// </summary>
        bool RequestEmotionBeat(string label, float intensity);
    }
}
