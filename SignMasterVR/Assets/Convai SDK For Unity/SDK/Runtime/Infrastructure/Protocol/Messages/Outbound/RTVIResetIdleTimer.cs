namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound reset idle timer message.</summary>
    public class RTVIResetIdleTimer : RTVISendMessageBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIResetIdleTimer" /> class.
        /// </summary>
        public RTVIResetIdleTimer()
        {
            Type = "reset-idle-timer";
            Data = new { };
        }
    }
}
