namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound TTS toggle message.</summary>
    public sealed class RTVITtsToggle : RTVISendMessageBase
    {
        public RTVITtsToggle(bool enabled)
        {
            Type = "tts-toggle";
            Data = new { enabled };
        }
    }
}
