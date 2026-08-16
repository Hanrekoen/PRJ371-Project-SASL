using System;
using System.Collections.Generic;
using System.Threading;
using Convai.Domain.DomainEvents.Session;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.NarrativeDesign;
using Convai.Runtime.Vision.Context;
using Convai.Shared.Types;
using RtviSceneMetadata = Convai.Infrastructure.Protocol.Messages.SceneMetadata;

namespace Convai.Runtime.Room
{
    /// <summary>
    ///     Provides Unity-facing access to Convai room connection state, events, and room surfaces.
    ///     Uses platform-agnostic abstractions for cross-platform compatibility.
    /// </summary>
    public interface IConvaiRoomConnectionService
    {
        /// <summary>
        ///     Gets the connection type configured for this room session.
        ///     Vision publishing is only active when this returns <see cref="ConvaiConnectionType.Video" />.
        /// </summary>
        public ConvaiConnectionType ConnectionType { get; }

        /// <summary>
        ///     Gets the current session state.
        /// </summary>
        public SessionState CurrentState { get; }

        /// <summary>
        ///     Indicates whether the SDK is currently connected to the Convai room.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        ///     Indicates whether the room has valid details (token, session, LiveKit room).
        /// </summary>
        public bool HasRoomDetails { get; }

        /// <summary>
        ///     Indicates whether a room ownership change has been accepted but still requires a disconnect/reconnect
        ///     before it becomes active.
        /// </summary>
        public bool HasPendingOwnershipReconnect { get; }

        /// <summary>
        ///     Gets the effective conversation input mode for the active session.
        ///     While connected, this reflects the live session mode. Otherwise it falls back to the configured defaults.
        /// </summary>
        public ConversationInputMode ActiveConversationInputMode { get; }

        /// <summary>
        ///     Gets the active room facade (null until connected).
        ///     Provides platform-agnostic access to room state and participants.
        /// </summary>
        public IRoomFacade CurrentRoom { get; }

        /// <summary>
        ///     Gets the RTVI handler for publishing transport payloads.
        /// </summary>
        public RTVIHandler RtvHandler { get; }

        /// <summary>
        ///     Raised whenever the room connection succeeds.
        /// </summary>
        public event Action Connected;

        /// <summary>
        ///     Raised whenever a lifecycle/session error occurs.
        /// </summary>
        public event Action<SessionError> OnSessionError;

        /// <summary>
        ///     Raised whenever the session state changes.
        ///     Unity-safe convenience event; EventHub also publishes SessionStateChanged for decoupled consumers.
        /// </summary>
        public event Action<SessionStateChanged> OnSessionStateChanged;

        /// <summary>
        ///     Raised whenever the effective active conversation input mode changes.
        /// </summary>
        public event Action<ConversationInputMode> ConversationInputModeChanged;

        /// <summary>
        ///     Initiates a connection workflow using the configured room manager.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>Operation resolving with the established room session.</returns>
        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Initiates a connection workflow using per-call session connect options.
        /// </summary>
        /// <param name="options">Per-call connection options that override configured defaults for this connect attempt.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>Operation resolving with the established room session.</returns>
        public IConvaiOperation<RoomSession> ConnectAsync(
            RoomSessionConnectOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Disconnects from the Convai room for the supplied reason.
        /// </summary>
        /// <param name="reason">High-level disconnect reason.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        public IConvaiOperation<Unit> DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Switches the active session between hands-free and push-to-talk without reconnecting.
        ///     This is only valid while the room is connected.
        /// </summary>
        public IConvaiOperation<Unit> SetConversationInputModeAsync(
            ConversationInputMode mode,
            CancellationToken cancellationToken = default);

        /// <summary>Sends a typed narrative trigger request to Convai.</summary>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool SendNarrativeTrigger(ConvaiNarrativeTriggerRequest request);

        /// <summary>
        ///     Updates scene metadata the backend can use for contextual grounding.
        /// </summary>
        /// <param name="sceneMetadata">Scene metadata entries to send.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool UpdateSceneMetadata(IReadOnlyList<RtviSceneMetadata> sceneMetadata);

        /// <summary>
        ///     Updates template keys for narrative design placeholder resolution.
        ///     Template keys like {PlayerName} in objectives will be replaced with the corresponding value.
        /// </summary>
        /// <param name="templateKeys">Dictionary of key-value pairs to update.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool UpdateTemplateKeys(Dictionary<string, string> templateKeys);

        /// <summary>
        ///     Enables or disables server-side text-to-speech output.
        /// </summary>
        public bool SetTtsEnabled(bool ttsEnabled);

        /// <summary>
        ///     Mutes or unmutes server-side speech-to-text processing.
        /// </summary>
        public bool SetSttMuted(bool muted);

        /// <summary>
        ///     Interrupts the bot's current speech output.
        /// </summary>
        public bool InterruptBot();

        /// <summary>
        ///     Terminates the current server-side pipeline/session.
        /// </summary>
        public bool KillPipeline();

        /// <summary>
        ///     Signals that the user has stopped speaking in push-to-talk flows.
        /// </summary>
        public bool ForceUserStoppedSpeaking();

        /// <summary>
        ///     Resets server-side idle timeout tracking for the current session.
        /// </summary>
        public bool ResetIdleTimer();

        /// <summary>
        ///     Requests backend dynamic vision buffer/status diagnostics for the current session.
        /// </summary>
        public bool RequestVisionStatus(string updateId = null);

        /// <summary>
        ///     Requests backend dynamic vision attachment/response behavior for the current session.
        /// </summary>
        public bool TriggerVision(ConvaiVisionTriggerRequest request);

        /// <summary>
        ///     Changes one input lane's respond mode for the rest of the session (acknowledged via
        ///     <see cref="Convai.Domain.DomainEvents.Vision.RespondModeUpdateResultReceived" />).
        /// </summary>
        public bool UpdateRespondMode(ConvaiRespondModeLane lane, ConvaiRespondMode mode, string updateId = null);
    }

    internal interface IConvaiDynamicContextTransport
    {
        public bool SendDynamicContext(ConvaiDynamicContextUpdate update);
    }
}
