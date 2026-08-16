namespace Convai.Infrastructure.Networking.Transport
{
    public interface IRealtimeTransportAccessor
    {
        public TransportCapabilities Capabilities { get; }
        public IRealtimeTransport Transport { get; }
    }
}
