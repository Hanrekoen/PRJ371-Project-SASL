using System;
using System.Threading;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;

namespace Convai.Runtime.Core.Providers
{
    /// <summary>
    ///     Abstraction for AI conversation backend.
    ///     Enables custom LLMs, offline AI, multimodal inputs, or third-party services.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Usage</b>: Injected into <see cref="ConvaiRuntimeBuilder" /> via <c>UseConversation(provider)</c>.
    ///         The default <c>ConvaiConversationProvider</c> connects to the Convai backend.
    ///     </para>
    ///     <para>
    ///         <b>Future Extensibility</b>: This interface is capability-based to support:
    ///         <list type="bullet">
    ///             <item>Custom LLM backends (local or cloud)</item>
    ///             <item>Multimodal inputs (text, audio, vision)</item>
    ///             <item>Tool/function calling</item>
    ///             <item>Streaming responses</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public interface IConversationProvider
    {
        /// <summary>
        ///     Unique identifier for this provider.
        /// </summary>
        public string ProviderId { get; }

        /// <summary>
        ///     Capabilities supported by this provider.
        /// </summary>
        public ConversationCapabilities Capabilities { get; }

        /// <summary>
        ///     Creates a new conversation session.
        /// </summary>
        /// <param name="request">Session configuration.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A new conversation session.</returns>
        public IConvaiOperation<IConversationSession> CreateSessionAsync(
            ConversationSessionRequest request,
            CancellationToken ct = default);
    }

    /// <summary>
    ///     An active conversation session with an AI character.
    /// </summary>
    public interface IConversationSession : IAsyncDisposable
    {
        /// <summary>Session identifier.</summary>
        public string SessionId { get; }

        /// <summary>Character ID this session is for.</summary>
        public string CharacterId { get; }

        /// <summary>Capabilities inherited from the provider.</summary>
        public ConversationCapabilities Capabilities { get; }

        /// <summary>Sends a message to the conversation.</summary>
        public IConvaiOperation<Unit> SendAsync(
            ConversationRequest request,
            CancellationToken ct = default);

        /// <summary>Opens the structured response stream for this session.</summary>
        public IConvaiStream<ConversationResponsePart> OpenResponseStream(CancellationToken ct = default);

        /// <summary>Event fired for conversation events (tool calls, state changes, etc.).</summary>
        public event Action<ConversationEvent> EventReceived;
    }

    /// <summary>
    ///     Capabilities of a conversation provider.
    /// </summary>
    [Flags]
    public enum ConversationCapabilities
    {
        /// <summary>No special capabilities.</summary>
        None = 0,

        /// <summary>Supports text input.</summary>
        TextInput = 1 << 0,

        /// <summary>Supports audio input.</summary>
        AudioInput = 1 << 1,

        /// <summary>Supports vision/image input.</summary>
        VisionInput = 1 << 2,

        /// <summary>Supports streaming responses.</summary>
        StreamingResponse = 1 << 3,

        /// <summary>Supports tool/function calling.</summary>
        ToolCalling = 1 << 4,

        /// <summary>Supports conversation history.</summary>
        History = 1 << 5,

        /// <summary>Standard Convai capabilities.</summary>
        ConvaiDefault = TextInput | AudioInput | StreamingResponse | History
    }

    /// <summary>
    ///     Request to create a new conversation session.
    /// </summary>
    public readonly struct ConversationSessionRequest
    {
        public ConversationSessionRequest(
            string characterId,
            string playerId,
            string displayName = null,
            bool enableHistory = true)
        {
            CharacterId = characterId;
            PlayerId = playerId;
            DisplayName = displayName ?? string.Empty;
            EnableHistory = enableHistory;
        }

        public string CharacterId { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public bool EnableHistory { get; }
    }

    /// <summary>
    ///     A request to send in a conversation.
    /// </summary>
    public readonly struct ConversationRequest
    {
        public ConversationRequest(string text = null, byte[] audioData = null, byte[] imageData = null)
        {
            Text = text;
            AudioData = audioData;
            ImageData = imageData;
        }

        public string Text { get; }
        public byte[] AudioData { get; }
        public byte[] ImageData { get; }
    }

    /// <summary>
    ///     A response part from the conversation.
    /// </summary>
    public readonly struct ConversationResponsePart
    {
        public ConversationResponsePart(string text, byte[] audioData = null, bool isFinal = false)
        {
            Text = text;
            AudioData = audioData;
            IsFinal = isFinal;
        }

        public string Text { get; }
        public byte[] AudioData { get; }
        public bool IsFinal { get; }
    }

    /// <summary>
    ///     Events from the conversation (tool calls, state changes, etc.).
    /// </summary>
    public readonly struct ConversationEvent
    {
        public ConversationEvent(string eventType, string data = null)
        {
            EventType = eventType;
            Data = data;
        }

        public string EventType { get; }
        public string Data { get; }
    }
}
