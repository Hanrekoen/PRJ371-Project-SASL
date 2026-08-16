namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound interrupt bot message.</summary>
    public sealed class RTVIInterruptBot : RTVISendMessageBase
    {
        public RTVIInterruptBot()
        {
            Type = "interrupt-bot";
            Data = new { };
        }
    }
}
