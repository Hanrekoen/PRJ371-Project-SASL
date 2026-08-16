/// <summary>
///     ConvaiManager partial: SDK event subscription forwarding.
///     Events are managed by the ConvaiEvents facade (created by ConvaiRuntimeHost).
/// </summary>

using System;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Runtime.Facades;
using Convai.Runtime.Presentation.Services.Utilities;

namespace Convai.Runtime.Components
{
    public partial class ConvaiManager
    {
        private bool _facadeEventsSubscribed;
        private readonly IdleDeadlineTracker _idleDeadlineTracker = new();
        private SubscriptionToken _idleOutboundMessageToken;

        /// <summary>
        ///     Subscribes to the ConvaiEvents facade events to forward them to public events.
        /// </summary>
        internal void SubscribeToFacadeEvents()
        {
            if (_facadeEventsSubscribed) return;

            ConvaiEvents events = _host?.Events;
            if (events == null) return;

            events.OnConnected += HandleRoomConnected;
            events.OnDisconnected += HandleRoomDisconnected;
            events.OnSessionError += HandleSessionError;
            events.OnSessionStateChanged += HandleIdleSessionStateChanged;
            events.OnUserIdleWarningReceived += HandleUserIdleWarningReceived;
            events.OnPlayerSpeakingStateChanged += HandleIdlePlayerSpeakingStateChanged;
            _idleOutboundMessageToken = events.Raw.Subscribe<OutboundRtviMessageSent>(HandleIdleOutboundMessageSent);

            _facadeEventsSubscribed = true;
        }

        internal void UnsubscribeFromFacadeEvents()
        {
            if (!_facadeEventsSubscribed) return;

            ConvaiEvents events = _host?.Events;
            if (events == null) return;

            events.OnConnected -= HandleRoomConnected;
            events.OnDisconnected -= HandleRoomDisconnected;
            events.OnSessionError -= HandleSessionError;
            events.OnSessionStateChanged -= HandleIdleSessionStateChanged;
            events.OnUserIdleWarningReceived -= HandleUserIdleWarningReceived;
            events.OnPlayerSpeakingStateChanged -= HandleIdlePlayerSpeakingStateChanged;
            if (_idleOutboundMessageToken != default)
                events.Raw.Unsubscribe(_idleOutboundMessageToken);
            _idleOutboundMessageToken = default;
            _idleDeadlineTracker.Clear();

            _facadeEventsSubscribed = false;
        }

        private void HandleRoomConnected()
        {
            RefreshOwnedAgentState();
            UpdateWebGLVoiceStartArmState();
            OnConnected?.Invoke();
        }

        private void HandleRoomDisconnected()
        {
            _webGLVoiceStartArmed = false;
            OnDisconnected?.Invoke();
        }

        private void HandleSessionError(SessionError error) => OnError?.Invoke(error);

        private void HandleUserIdleWarningReceived(UserIdleWarningReceived warning) =>
            _idleDeadlineTracker.Arm(warning);

        private void HandleIdlePlayerSpeakingStateChanged(PlayerSpeakingStateChanged stateChanged)
        {
            if (stateChanged.IsSpeaking)
                ClearIdleDeadline();
        }

        private void HandleIdleSessionStateChanged(SessionStateChanged stateChanged)
        {
            if (stateChanged.NewState != SessionState.Connected)
                ClearIdleDeadline();
        }

        private void HandleIdleOutboundMessageSent(OutboundRtviMessageSent message)
        {
            if (message.MessageType == "reset-idle-timer" ||
                IdleResetPolicy.IsServerVisibleActivity(message.MessageType))
                ClearIdleDeadline();
        }

        private void TickIdleDeadline()
        {
            if (!_idleDeadlineTracker.TryConsume(DateTime.UtcNow, out UserIdleTimeoutElapsed elapsed))
                return;

            _host?.Events?.Raw.Publish(elapsed);
        }

        private void ClearIdleDeadline() => _idleDeadlineTracker.Clear();
    }
}
