using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Audio;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Async;

namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Implementation of <see cref="IRoomAudioCoordinator" /> for managing room audio.
    /// </summary>
    /// <remarks>
    ///     This coordinator wraps the existing audio infrastructure (AudioTrackManager,
    ///     RemoteAudioPreferenceManager) and exposes a simplified interface.
    /// </remarks>
    internal sealed class RoomAudioCoordinator : IRoomAudioCoordinator, IDisposable
    {
        private readonly IRoomAudioRuntimeAdapter _audioRuntimeAdapter;
        private readonly ILogger _logger;
        private bool _disposed;
        private bool _isMicMuted;

        public RoomAudioCoordinator(
            IRoomAudioRuntimeAdapter audioRuntimeAdapter,
            RemoteAudioPreferenceManager remoteAudioPreferences = null,
            ILogger logger = null)
        {
            _audioRuntimeAdapter = audioRuntimeAdapter ?? throw new ArgumentNullException(nameof(audioRuntimeAdapter));
            RemoteAudioPreferences = remoteAudioPreferences ?? new RemoteAudioPreferenceManager(null, logger);
            _logger = logger.WithTag(nameof(RoomAudioCoordinator));

            RemoteAudioPreferences.RemoteAudioEnabledChanged += OnRemoteAudioPreferenceChanged;
        }

        internal RemoteAudioPreferenceManager RemoteAudioPreferences { get; }

        /// <summary>
        ///     Disposes the coordinator and unsubscribes from events.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            RemoteAudioPreferences.RemoteAudioEnabledChanged -= OnRemoteAudioPreferenceChanged;
        }

        /// <inheritdoc />
        public bool IsMicMuted => _audioRuntimeAdapter.IsMicMuted;

        /// <inheritdoc />
        public bool IsListening { get; private set; }

        /// <inheritdoc />
        public event Action<bool> MicMuteChanged;

        /// <inheritdoc />
        public event Action<bool> ListeningStateChanged;

        /// <inheritdoc />
        public void SetMicMuted(bool muted)
        {
            _logger?.Debug($"SetMicMuted: {muted}");
            _audioRuntimeAdapter.SetMicMuted(muted);
            _isMicMuted = muted;
            MicMuteChanged?.Invoke(muted);
        }

        /// <inheritdoc />
        public IConvaiOperation<Unit> StartListeningAsync(int microphoneIndex = 0, CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(StartListeningAsyncCore(microphoneIndex, ct));

        /// <inheritdoc />
        public IConvaiOperation<Unit> StopListeningAsync(CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(StopListeningAsyncCore(ct));

        /// <inheritdoc />
        public void SetRemoteAudioEnabled(IConvaiCharacterAgent character, bool enabled)
        {
            if (character == null) return;

            _logger?.Debug($"SetRemoteAudioEnabled: {character.CharacterId} = {enabled}");
            RemoteAudioPreferences.SetRemoteAudioEnabled(character.CharacterId, enabled);
        }

        /// <inheritdoc />
        public bool IsRemoteAudioEnabled(IConvaiCharacterAgent character)
        {
            if (character == null) return false;
            return RemoteAudioPreferences.IsRemoteAudioEnabled(character.CharacterId);
        }

        private async Task<Unit> StartListeningAsyncCore(int microphoneIndex, CancellationToken ct)
        {
            _logger?.Debug($"StartListeningAsync: microphoneIndex={microphoneIndex}");

            await _audioRuntimeAdapter.StartListeningAsync(microphoneIndex, ct);

            IsListening = true;
            ListeningStateChanged?.Invoke(true);
            return Unit.Value;
        }

        private async Task<Unit> StopListeningAsyncCore(CancellationToken ct)
        {
            _logger?.Debug("StopListeningAsync");

            await _audioRuntimeAdapter.StopListeningAsync(ct);

            IsListening = false;
            ListeningStateChanged?.Invoke(false);
            return Unit.Value;
        }

        internal bool SetCharacterMuted(string characterId, bool muted) =>
            _audioRuntimeAdapter.SetCharacterMuted(characterId, muted);

        internal bool IsCharacterMuted(string characterId) =>
            _audioRuntimeAdapter.IsCharacterMuted(characterId);

        /// <summary>
        ///     Fired when remote audio enabled state changes for a character.
        /// </summary>
        public event Action<string, bool> RemoteAudioEnabledChanged;

        /// <summary>
        ///     Initializes remote audio preference for a character.
        /// </summary>
        public void InitializeRemoteAudioPreference(string characterId, bool enabled) =>
            RemoteAudioPreferences.InitializePreference(characterId, enabled);

        private void OnRemoteAudioPreferenceChanged(string characterId, bool enabled) =>
            RemoteAudioEnabledChanged?.Invoke(characterId, enabled);
    }
}
