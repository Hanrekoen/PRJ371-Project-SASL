namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound force-user-stopped-speaking message.</summary>
    public sealed class RTVIForceUserStoppedSpeaking : RTVISendMessageBase
    {
        public RTVIForceUserStoppedSpeaking()
        {
            Type = "force-user-stopped-speaking";
            Data = new { };
        }
    }
}
