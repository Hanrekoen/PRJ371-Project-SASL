using Convai.Domain.Emotion;

namespace Convai.Runtime.Emotion
{
    /// <summary>
    ///     Implemented by the authoring component (the emotion controller) that declares the
    ///     desired <see cref="EmotionDetectionMode" /> for a character. Discovered at connect time
    ///     through the character's component hierarchy.
    /// </summary>
    /// <remarks>
    ///     Lives in the runtime assembly because discovery is a Unity component-hierarchy concern
    ///     (<c>GetComponentInChildren</c>). When no source is found on a character, emotion
    ///     detection is treated as <see cref="EmotionDetectionMode.Off" />.
    /// </remarks>
    public interface IEmotionDetectionModeSource
    {
        /// <summary>The emotion detection mode authored on this component.</summary>
        EmotionDetectionMode EmotionDetectionMode { get; }
    }
}
