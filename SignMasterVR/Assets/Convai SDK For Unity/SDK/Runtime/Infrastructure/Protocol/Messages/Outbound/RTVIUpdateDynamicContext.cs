using Convai.Runtime.DynamicContext;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Message to update the bot's temporary runtime (ephemeral) context in a unified way.
    /// </summary>
    public class RTVIUpdateDynamicContext : RTVISendMessageBase
    {
        public RTVIUpdateDynamicContext(ConvaiDynamicContextUpdate update)
        {
            Type = "context-update";
            Data = new DynamicContext(update);
        }

    }
}
