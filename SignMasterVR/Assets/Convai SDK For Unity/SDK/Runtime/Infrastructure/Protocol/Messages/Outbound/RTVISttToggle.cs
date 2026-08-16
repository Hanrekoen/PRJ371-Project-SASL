namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound STT toggle message.</summary>
    public sealed class RTVISttToggle : RTVISendMessageBase
    {
        public RTVISttToggle(bool muted)
        {
            Type = "stt-toggle";
            Data = new { muted };
        }
    }
}
