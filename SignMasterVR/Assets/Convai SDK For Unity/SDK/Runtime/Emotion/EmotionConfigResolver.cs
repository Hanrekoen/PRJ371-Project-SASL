using Convai.Domain.Emotion;
using Convai.RestAPI;

namespace Convai.Runtime.Emotion
{
    /// <summary>
    ///     Maps a client-side <see cref="EmotionDetectionMode" /> to the <see cref="RoomEmotionConfig" />
    ///     sent on room connect. Shared by every transport so all platforms produce an identical
    ///     request for a given mode.
    /// </summary>
    public static class EmotionConfigResolver
    {
        /// <summary>
        ///     Returns the emotion config for <paramref name="mode" />, or <c>null</c> when emotion
        ///     detection is off (no <c>emotion_config</c> is then sent on connect).
        /// </summary>
        public static RoomEmotionConfig Resolve(EmotionDetectionMode mode)
        {
            return mode switch
            {
                EmotionDetectionMode.Llm => RoomEmotionConfig.Create("llm"),
                EmotionDetectionMode.Nrclex => RoomEmotionConfig.Create("nrclex"),
                _ => null
            };
        }
    }
}
