using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.Infrastructure.Protocol
{
    /// <summary>
    ///     Stateless protocol gateway responsible for translating raw JSON envelopes into
    ///     strongly-typed message callbacks. Transport layers push packets through
    ///     <see cref="ProcessIncoming" /> and supply handlers via <see cref="RegisterHandler" />.
    /// </summary>
    public sealed class ProtocolGateway
    {
        private readonly StringComparer _comparer = StringComparer.OrdinalIgnoreCase;
        private readonly Dictionary<string, IProtocolMessageHandler> _handlers;
        private readonly Action<string> _logDebug;
        private readonly Action<string> _logError;
        private readonly JsonSerializer _serializer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ProtocolGateway" /> class.
        /// </summary>
        /// <param name="serializerSettings">Optional JSON serializer settings used to materialize payloads.</param>
        /// <param name="logDebug">Optional debug logger.</param>
        /// <param name="logError">Optional error logger.</param>
        public ProtocolGateway(JsonSerializerSettings serializerSettings = null, Action<string> logDebug = null,
            Action<string> logError = null)
        {
            _handlers = new Dictionary<string, IProtocolMessageHandler>(_comparer);
            _serializer = JsonSerializer.Create(serializerSettings ?? new JsonSerializerSettings());
            _logDebug = logDebug ?? (_ => { });
            _logError = logError ?? (_ => { });
        }

        /// <summary>
        ///     Registers a handler for a message type.
        /// </summary>
        /// <param name="messageType">Message type discriminator.</param>
        /// <param name="handler">Handler invoked for matching messages.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="messageType" /> is null/empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler" /> is null.</exception>
        public void RegisterHandler(string messageType, Action<ProtocolMessage> handler)
        {
            if (string.IsNullOrWhiteSpace(messageType))
                throw new ArgumentException("Message type cannot be null or empty", nameof(messageType));

            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _handlers[messageType] = new DelegateMessageHandler(handler);
        }

        /// <summary>
        ///     Registers a typed handler for a message type.
        /// </summary>
        /// <typeparam name="TPayload">Payload type mapped from the message <c>payload</c> / <c>data</c> field.</typeparam>
        /// <param name="messageType">Message type discriminator.</param>
        /// <param name="handler">Handler invoked for matching messages.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="messageType" /> is null/empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler" /> is null.</exception>
        public void RegisterHandler<TPayload>(string messageType, Action<ProtocolMessage<TPayload>> handler)
        {
            if (string.IsNullOrWhiteSpace(messageType))
                throw new ArgumentException("Message type cannot be null or empty", nameof(messageType));

            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _handlers[messageType] = new DelegateMessageHandler<TPayload>(_serializer, handler);
        }

        /// <summary>
        ///     Processes an incoming packet and dispatches it to a registered handler.
        /// </summary>
        /// <param name="packet">Incoming transport packet.</param>
        public void ProcessIncoming(ProtocolPacket packet)
        {
            if (packet.Payload.IsEmpty)
            {
                _logDebug("[ProtocolGateway] Ignoring empty payload.");
                return;
            }

            string json;
            try
            {
                json = Encoding.UTF8.GetString(packet.Payload.Span);
            }
            catch (Exception ex)
            {
                _logError($"[ProtocolGateway] Failed to decode UTF-8 payload: {ex.Message}");
                return;
            }

            ProtocolEnvelope envelope;
            try
            {
                envelope = ProtocolEnvelope.Parse(json);
            }
            catch (Exception ex)
            {
                _logError($"[ProtocolGateway] Envelope parse error: {ex.Message}\nJSON: {json}");
                return;
            }

            ProtocolMessage message = new(envelope, packet);

            if (envelope.Type.Contains("user", StringComparison.OrdinalIgnoreCase) ||
                envelope.Type.Contains("transcription", StringComparison.OrdinalIgnoreCase))
            {
                _logDebug(
                    $"[ProtocolGateway] Received message type='{envelope.Type}', payload={envelope.Payload}");
            }

            if (!_handlers.TryGetValue(envelope.Type, out IProtocolMessageHandler handler))
            {
                _logDebug($"[ProtocolGateway] No handler registered for message type '{envelope.Type}'.");
                return;
            }

            try
            {
                handler.Handle(message);
            }
            catch (Exception ex)
            {
                _logError($"[ProtocolGateway] Handler threw for type '{envelope.Type}': {ex.Message}");
            }
        }

        private interface IProtocolMessageHandler
        {
            public void Handle(ProtocolMessage message);
        }

        private sealed class DelegateMessageHandler : IProtocolMessageHandler
        {
            private readonly Action<ProtocolMessage> _handler;

            public DelegateMessageHandler(Action<ProtocolMessage> handler)
            {
                _handler = handler;
            }

            public void Handle(ProtocolMessage message) => _handler(message);
        }

        private sealed class DelegateMessageHandler<TPayload> : IProtocolMessageHandler
        {
            private readonly Action<ProtocolMessage<TPayload>> _handler;
            private readonly JsonSerializer _serializer;

            public DelegateMessageHandler(JsonSerializer serializer, Action<ProtocolMessage<TPayload>> handler)
            {
                _serializer = serializer;
                _handler = handler;
            }

            public void Handle(ProtocolMessage message)
            {
                TPayload payload = default;
                if (message.Envelope.Payload != null && message.Envelope.Payload.Type != JTokenType.Null)
                    payload = message.Envelope.Payload.ToObject<TPayload>(_serializer);

                _handler(new ProtocolMessage<TPayload>(message.Envelope, message.Packet, payload));
            }
        }
    }

    /// <summary>Transport packet wrapper used by the protocol gateway.</summary>
    public readonly struct ProtocolPacket
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ProtocolPacket" /> struct.
        /// </summary>
        /// <param name="payload">Raw payload bytes.</param>
        /// <param name="participantId">Participant identifier associated with the packet.</param>
        /// <param name="topic">Packet topic label.</param>
        /// <param name="isReliable">Whether the transport marked the packet as reliable.</param>
        public ProtocolPacket(ReadOnlyMemory<byte> payload, string participantId, string topic, bool isReliable)
        {
            Payload = payload;
            ParticipantId = participantId ?? string.Empty;
            Topic = topic ?? string.Empty;
            IsReliable = isReliable;
        }

        /// <summary>Gets the raw payload bytes.</summary>
        public ReadOnlyMemory<byte> Payload { get; }

        /// <summary>Gets the participant identifier associated with the packet.</summary>
        public string ParticipantId { get; }

        /// <summary>Gets the packet topic label.</summary>
        public string Topic { get; }

        /// <summary>Gets a value indicating whether the packet is reliable.</summary>
        public bool IsReliable { get; }
    }

    /// <summary>Parsed protocol envelope extracted from an incoming JSON message.</summary>
    public sealed class ProtocolEnvelope
    {
        private ProtocolEnvelope(string version, string type, string id, JObject raw, JToken payload, string json)
        {
            Version = version;
            Type = type;
            Id = id;
            Raw = raw;
            Payload = payload;
            Json = json;
        }

        /// <summary>Gets the protocol envelope version.</summary>
        public string Version { get; }

        /// <summary>Gets the message type discriminator.</summary>
        public string Type { get; }

        /// <summary>Gets the message identifier.</summary>
        public string Id { get; }

        /// <summary>Gets the raw parsed JSON object.</summary>
        public JObject Raw { get; }

        /// <summary>Gets the extracted payload token (from <c>payload</c> or <c>data</c>).</summary>
        public JToken Payload { get; }

        /// <summary>Gets the original JSON string.</summary>
        public string Json { get; }

        /// <summary>
        ///     Parses a JSON message into a <see cref="ProtocolEnvelope" />.
        /// </summary>
        /// <param name="json">Raw JSON string.</param>
        /// <returns>The parsed envelope.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the JSON is missing a required <c>type</c> field.</exception>
        public static ProtocolEnvelope Parse(string json)
        {
            JObject obj = JObject.Parse(json);
            string version = obj.Value<string>("v") ?? string.Empty;
            string type = obj.Value<string>("type") ?? string.Empty;
            string id = obj.Value<string>("id") ?? string.Empty;
            JToken payload = obj["payload"] ?? obj["data"] ?? JValue.CreateNull();

            if (string.IsNullOrEmpty(type))
                throw new InvalidOperationException("Protocol message missing 'type' field.");

            return new ProtocolEnvelope(version, type, id, obj, payload, json);
        }
    }

    /// <summary>Protocol message wrapper containing the parsed envelope and source packet.</summary>
    public readonly struct ProtocolMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ProtocolMessage" /> struct.
        /// </summary>
        /// <param name="envelope">Parsed envelope.</param>
        /// <param name="packet">Source packet.</param>
        public ProtocolMessage(ProtocolEnvelope envelope, ProtocolPacket packet)
        {
            Envelope = envelope;
            Packet = packet;
        }

        /// <summary>Gets the parsed envelope.</summary>
        public ProtocolEnvelope Envelope { get; }

        /// <summary>Gets the source packet.</summary>
        public ProtocolPacket Packet { get; }
    }

    /// <summary>Typed protocol message wrapper containing the parsed envelope, source packet, and payload.</summary>
    /// <typeparam name="TPayload">Payload type mapped from the envelope.</typeparam>
    public readonly struct ProtocolMessage<TPayload>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ProtocolMessage{TPayload}" /> struct.
        /// </summary>
        /// <param name="envelope">Parsed envelope.</param>
        /// <param name="packet">Source packet.</param>
        /// <param name="payload">Typed payload mapped from the envelope token.</param>
        public ProtocolMessage(ProtocolEnvelope envelope, ProtocolPacket packet, TPayload payload)
        {
            Envelope = envelope;
            Packet = packet;
            Payload = payload;
        }

        /// <summary>Gets the parsed envelope.</summary>
        public ProtocolEnvelope Envelope { get; }

        /// <summary>Gets the source packet.</summary>
        public ProtocolPacket Packet { get; }

        /// <summary>Gets the typed payload.</summary>
        public TPayload Payload { get; }
    }
}
