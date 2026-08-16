using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Room;
using Convai.Shared.Compatibility;
using UnityEngine;

namespace Convai.Runtime.Adapters.Networking
{
    public partial class ConvaiRoomManager
    {
        public IConvaiOperation<Unit> SetConversationInputModeAsync(
            ConversationInputMode mode,
            CancellationToken cancellationToken = default)
        {
            ConvaiOperationException invalidStateException = CreateInvalidConversationInputModeStateException();
            if (invalidStateException != null)
                return ConvaiOperation<Unit>.Failed(invalidStateException);

            return ConvaiOperation<Unit>.FromTask(SetConversationInputModeAsyncCore(mode, cancellationToken));
        }

        private async Task<Unit> SetConversationInputModeAsyncCore(
            ConversationInputMode mode,
            CancellationToken cancellationToken)
        {
            await _conversationInputModeTransitionGate.WaitAsync(cancellationToken);
            try
            {
                ConvaiOperationException invalidStateException = CreateInvalidConversationInputModeStateException();
                if (invalidStateException != null)
                    throw invalidStateException;

                ConversationInputMode currentMode = ActiveConversationInputMode;
                if (currentMode == mode)
                    return Unit.Value;

                _conversationInputModeTransitionInProgress = true;

                TurnTakingOptions currentSourceOptions = _sessionTurnTakingSourceOptions?.Clone();
                if (currentSourceOptions == null)
                {
                    throw new ConvaiOperationException(
                        SessionErrorCodes.SessionInvalidState,
                        "[ConvaiRoomManager] Conversation input mode switch requires an active connected session.");
                }

                TurnTakingOptions nextSourceOptions = currentSourceOptions.Clone();
                nextSourceOptions.Mode = mode;

                ResolvedTurnTakingOptions currentResolvedOptions = CurrentResolvedTurnTakingOptions;
                ResolvedTurnTakingOptions nextResolvedOptions =
                    TurnTakingOptionsResolver.ResolveFromSource(nextSourceOptions);

                _logger?.Info(
                    $"Switching conversation input mode: {currentMode} -> {mode}.",
                    LogCategory.SDK);

                switch ((currentMode, mode))
                {
                    case (ConversationInputMode.PushToTalk, ConversationInputMode.HandsFree):
                        await TransitionPushToTalkToHandsFreeAsync(
                            currentResolvedOptions,
                            nextSourceOptions,
                            nextResolvedOptions,
                            cancellationToken);
                        break;

                    case (ConversationInputMode.HandsFree, ConversationInputMode.PushToTalk):
                        await TransitionHandsFreeToPushToTalkAsync(
                            currentResolvedOptions,
                            nextSourceOptions,
                            nextResolvedOptions,
                            cancellationToken);
                        break;

                    default:
                        UpdateConnectedSessionTurnTakingState(nextSourceOptions, nextResolvedOptions);
                        break;
                }

                return Unit.Value;
            }
            finally
            {
                _conversationInputModeTransitionInProgress = false;
                _conversationInputModeTransitionGate.Release();
            }
        }

        private async Task TransitionPushToTalkToHandsFreeAsync(
            ResolvedTurnTakingOptions currentResolvedOptions,
            TurnTakingOptions nextSourceOptions,
            ResolvedTurnTakingOptions nextResolvedOptions,
            CancellationToken cancellationToken)
        {
            PreparePushToTalkControllersForModeTransition(
                nextResolvedOptions,
                "runtime-switch:push-to-talk-to-handsfree");

            UpdateConnectedSessionTurnTakingState(nextSourceOptions, nextResolvedOptions);

            bool aecChanged = currentResolvedOptions.EnableAcousticEchoCancellation !=
                              nextResolvedOptions.EnableAcousticEchoCancellation;
            await EnsureMicrophonePublishedForRuntimeModeAsync(aecChanged, cancellationToken);

            SetMicMuted(false);
            SetSttMuted(false);
        }

        private async Task TransitionHandsFreeToPushToTalkAsync(
            ResolvedTurnTakingOptions currentResolvedOptions,
            TurnTakingOptions nextSourceOptions,
            ResolvedTurnTakingOptions nextResolvedOptions,
            CancellationToken cancellationToken)
        {
            ForceUserStoppedSpeaking();
            PreparePushToTalkControllersForModeTransition(
                nextResolvedOptions,
                "runtime-switch:handsfree-to-push-to-talk");

            UpdateConnectedSessionTurnTakingState(nextSourceOptions, nextResolvedOptions);

            if (nextResolvedOptions.EnableServerSttToggle)
                SetSttMuted(true);

            if (nextResolvedOptions.PushToTalkStartupMode == PushToTalkMicStartupMode.OpenOnFirstPress)
            {
                await StopListeningAsync(cancellationToken).AsTask();
                SetMicMuted(true);
                return;
            }

            bool aecChanged = currentResolvedOptions.EnableAcousticEchoCancellation !=
                              nextResolvedOptions.EnableAcousticEchoCancellation;
            await EnsureMicrophonePublishedForRuntimeModeAsync(aecChanged, cancellationToken);
            SetMicMuted(nextResolvedOptions.StartMutedInPushToTalk);
        }

        private async Task EnsureMicrophonePublishedForRuntimeModeAsync(
            bool republishIfAlreadyPublished,
            CancellationToken cancellationToken)
        {
            if (_roomAudioRuntimeAdapter != null)
            {
                await _roomAudioRuntimeAdapter.EnsurePublishedMicrophoneAsync(
                    republishIfAlreadyPublished,
                    cancellationToken);
                return;
            }

            await StartListeningAsync(cancellationToken: cancellationToken).AsTask();
        }

        private void PreparePushToTalkControllersForModeTransition(
            ResolvedTurnTakingOptions nextResolvedOptions,
            string reason)
        {
            foreach (ConvaiPushToTalkController controller in ResolveActivePushToTalkControllers())
                controller.PrepareForConversationInputModeTransition(nextResolvedOptions, reason);
        }

        private List<ConvaiPushToTalkController> ResolveActivePushToTalkControllers()
        {
            ConvaiManager manager = ResolveOwningManager();
            ConvaiPushToTalkController[] controllers =
                ConvaiObjectFind.All<ConvaiPushToTalkController>(FindObjectsInactive.Include);
            var matches = new List<ConvaiPushToTalkController>(controllers.Length);
            for (int i = 0; i < controllers.Length; i++)
            {
                ConvaiPushToTalkController controller = controllers[i];
                if (controller == null)
                    continue;

                if (manager == null || controller.BelongsToManager(manager))
                    matches.Add(controller);
            }

            return matches;
        }

        private ConvaiManager ResolveOwningManager()
        {
            ConvaiManager activeManager = ConvaiManager.ActiveManager;
            if (activeManager != null &&
                activeManager.TryGetRoomManager(out ConvaiRoomManager activeRoomManager) &&
                ReferenceEquals(activeRoomManager, this))
            {
                return activeManager;
            }

            return GetComponent<ConvaiManager>();
        }

        private ConvaiOperationException CreateInvalidConversationInputModeStateException()
        {
            if (CurrentState == SessionState.Connected && IsConnected && _hasConnectedSessionTurnTakingState)
                return null;

            return new ConvaiOperationException(
                SessionErrorCodes.SessionInvalidState,
                $"[ConvaiRoomManager] Conversation input mode switching requires an active connected room. Current state: {CurrentState}.");
        }
    }
}
