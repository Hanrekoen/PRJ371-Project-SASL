using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter
    {
        private async Task<Unit> StartConversationAsyncCore(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(CharacterId))
            {
                throw new ConvaiOperationException(
                    SessionErrorCodes.ConfigCharacterIdMissing,
                    $"[ConvaiCharacter] [{CharacterName}] Cannot start: CharacterId is empty. " +
                    "Set the Character ID in the Inspector (find it on your Convai dashboard at https://convai.com).");
            }

            if (!IsInjected)
            {
                throw new ConvaiOperationException(
                    SessionErrorCodes.SessionInvalidState,
                    $"[ConvaiCharacter] [{_characterName}] Cannot start: dependencies not injected. " +
                    "Ensure ConvaiManager is in the scene and has run before calling StartConversationAsync().");
            }

            if (ConnectionService == null)
            {
                throw new ConvaiOperationException(
                    SessionErrorCodes.SessionInvalidState,
                    $"[ConvaiCharacter] [{_characterName}] Cannot start: ConnectionService is null");
            }

            if (SessionState == SessionState.Connected)
            {
                Logger?.Debug(
                    $"[{_characterName}] StartConversationAsync ignored: already connected");
                return Unit.Value;
            }

            if (SessionState == SessionState.Disconnecting)
            {
                throw new ConvaiOperationException(
                    SessionErrorCodes.SessionInvalidState,
                    $"[ConvaiCharacter] [{_characterName}] Cannot start while disconnecting");
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _destroyCts?.Token ?? CancellationToken.None);
            CancellationToken linkedToken = linkedCts.Token;

            Logger?.Debug($"[{_characterName}] Starting conversation...");

            IsCharacterReady = false;

            TaskCompletionSource<bool> readyTcs = new();
            lock (_characterReadyTcsLock)
            {
                _characterReadyTcs?.TrySetCanceled();
                _characterReadyTcs = readyTcs;
            }

            try
            {
                linkedToken.ThrowIfCancellationRequested();

                Logger?.Debug($"[{_characterName}] Awaiting connection service...");
                await ConnectionService.ConnectAsync(linkedToken);

                linkedToken.ThrowIfCancellationRequested();

                Logger?.Info(
                    $"[{_characterName}] Connection successful, waiting for character ready signal...");

                await WaitForCharacterReadyAsync(readyTcs, linkedToken);
                return Unit.Value;
            }
            catch (OperationCanceledException)
            {
                Logger?.Debug($"[{_characterName}] StartConversationAsync cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger?.Error($"[{_characterName}] Connection failed with exception: {ex.Message}");
                throw;
            }
            finally
            {
                lock (_characterReadyTcsLock)
                {
                    if (_characterReadyTcs == readyTcs)
                        _characterReadyTcs = null;
                }
            }
        }

        /// <summary>
        ///     Waits for the CharacterReady signal with optional timeout.
        /// </summary>
        private async Task<Unit> WaitForCharacterReadyAsync(TaskCompletionSource<bool> readyTcs,
            CancellationToken cancellationToken) =>
            await WaitForCharacterReadyInternalAsync(readyTcs, _characterReadyTimeoutSeconds, cancellationToken);

        /// <summary>
        ///     Internal implementation that waits for the CharacterReady signal with a specified timeout.
        /// </summary>
        private async Task<Unit> WaitForCharacterReadyInternalAsync(TaskCompletionSource<bool> readyTcs,
            float timeoutSeconds, CancellationToken cancellationToken)
        {
            if (IsCharacterReady)
            {
                Logger?.Debug($"[{_characterName}] Already character ready");
                return Unit.Value;
            }

            if (timeoutSeconds <= 0f)
            {
                Logger?.Debug($"[{_characterName}] Waiting indefinitely for character ready...");
                using (cancellationToken.Register(() => readyTcs.TrySetCanceled()))
                    await readyTcs.Task;
                return Unit.Value;
            }

            int timeoutMs = (int)(timeoutSeconds * 1000);
            Logger?.Debug(
                $"[{_characterName}] Waiting for character ready (timeout: {timeoutSeconds}s)...");

            using CancellationTokenSource timeoutCts = new(timeoutMs);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                using (combinedCts.Token.Register(() => readyTcs.TrySetCanceled()))
                    await readyTcs.Task;
                return Unit.Value;
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    Logger?.Warning(
                        $"[{_characterName}] Character ready timeout after {timeoutSeconds}s");
                    throw new ConvaiOperationException(
                        SessionErrorCodes.ConnectionTimeout,
                        $"[ConvaiCharacter] [{_characterName}] Character ready timeout after {timeoutSeconds}s");
                }

                throw;
            }
        }

        private async Task<Unit> WaitForCharacterReadyAsyncCore(float? timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (IsCharacterReady) return Unit.Value;

            if (SessionState != SessionState.Connected && SessionState != SessionState.Connecting)
            {
                throw new ConvaiOperationException(
                    SessionErrorCodes.SessionInvalidState,
                    $"[ConvaiCharacter] [{_characterName}] Cannot wait for CharacterReady: not connected (state={SessionState})");
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _destroyCts?.Token ?? CancellationToken.None);

            TaskCompletionSource<bool> readyTcs = new();
            lock (_characterReadyTcsLock)
            {
                if (_characterReadyTcs != null)
                    readyTcs = _characterReadyTcs;
                else
                    _characterReadyTcs = readyTcs;
            }

            try
            {
                float timeout = timeoutSeconds ?? _characterReadyTimeoutSeconds;
                return await WaitForCharacterReadyInternalAsync(readyTcs, timeout, linkedCts.Token);
            }
            finally
            {
                lock (_characterReadyTcsLock)
                {
                    if (_characterReadyTcs == readyTcs)
                        _characterReadyTcs = null;
                }
            }
        }

        private async Task<Unit> StopConversationAsyncCore(CancellationToken cancellationToken)
        {
            if (SessionState == SessionState.Disconnected) return Unit.Value;

            lock (_characterReadyTcsLock)
            {
                _characterReadyTcs?.TrySetCanceled();
                _characterReadyTcs = null;
            }

            IsCharacterReady = false;
            _isSpeaking = false;
            ResetEmotionState();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _destroyCts?.Token ?? CancellationToken.None);

            try
            {
                await ConnectionService.DisconnectAsync(cancellationToken: linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger?.Debug($"[{_characterName}] StopConversationAsync cancelled");
            }
            catch (Exception ex)
            {
                Logger?.Error($"[{_characterName}] Disconnect failed: {ex.Message}");
            }

            return Unit.Value;
        }

        private async Task<Unit> ResetAndRetryAsyncCore(CancellationToken cancellationToken)
        {
            if (SessionState != SessionState.Error)
            {
                throw new ConvaiOperationException(
                    SessionErrorCodes.SessionInvalidState,
                    $"[ConvaiCharacter] [{_characterName}] ResetAndRetryAsync called but state is {SessionState}, not Error");
            }

            Logger?.Info($"[{_characterName}] Resetting from Error and retrying connection...");
            IsCharacterReady = false;
            await StartConversationAsync(cancellationToken);
            return Unit.Value;
        }

        private async Task ToggleStartAsync()
        {
            try
            {
                bool remoteAudioEnabled = IsRemoteAudioEnabled;
                await StartConversationAsync();
                if (!remoteAudioEnabled)
                {
                    Logger?.Info(
                        $"[{_characterName}] Connected in text-only mode (remote audio disabled). " +
                        "Call EnableRemoteAudio() / ToggleRemoteAudio() to hear speech.");
                }
            }
            catch (Exception ex)
            {
                Logger?.Error($"[{_characterName}] Toggle start failed: {ex.Message}");
            }
        }

        private async Task ToggleStopAsync()
        {
            try
            {
                await StopConversationAsync();
            }
            catch (Exception ex)
            {
                Logger?.Error($"[{_characterName}] Toggle stop failed: {ex.Message}");
            }
        }

        /// <summary>Invokes a saved Narrative Design trigger by name.</summary>
        public void SendTrigger(string triggerName) =>
            NarrativeDesign.InvokeTrigger(triggerName);

        /// <summary>Sends inline narrative event context and lets Convai respond naturally.</summary>
        public void SendNarrativeEvent(string eventMessage) =>
            NarrativeDesign.InvokeEvent(eventMessage);

        /// <summary>Sends exact scripted narrative speech.</summary>
        public void SendNarrativeSpeech(string speechText) =>
            NarrativeDesign.InvokeSpeech(speechText);

        /// <summary>
        ///     Updates template keys for narrative design placeholder resolution.
        ///     Template keys like {PlayerName} in objectives will be replaced with the corresponding value.
        /// </summary>
        /// <param name="templateKeys">Dictionary of key-value pairs to update.</param>
        public void UpdateTemplateKeys(Dictionary<string, string> templateKeys)
        {
            NarrativeDesign.SetTemplateKeys(templateKeys);
        }

    }
}
