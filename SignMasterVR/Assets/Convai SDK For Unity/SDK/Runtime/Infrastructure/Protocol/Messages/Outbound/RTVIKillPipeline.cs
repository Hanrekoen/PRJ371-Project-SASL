namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound kill pipeline message.</summary>
    public sealed class RTVIKillPipeline : RTVISendMessageBase
    {
        public RTVIKillPipeline()
        {
            Type = "kill-pipeline";
            Data = new { };
        }
    }
}
