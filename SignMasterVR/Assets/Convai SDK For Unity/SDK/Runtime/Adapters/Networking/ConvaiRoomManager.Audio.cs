using System.Threading;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Room;

namespace Convai.Runtime.Adapters.Networking
{
    public partial class ConvaiRoomManager
    {
        public void SetMicMuted(bool mute)
        {
            _logger?.Debug($"SetMicMuted called: mute={mute}");
            _roomAudioCoordinator?.SetMicMuted(mute);
        }

        public IConvaiOperation<Unit> StartListeningAsync(int microphoneIndex = 0,
            CancellationToken cancellationToken = default) =>
            _roomAudioCoordinator?.StartListeningAsync(microphoneIndex, cancellationToken) ??
            ConvaiOperation<Unit>.Succeeded(Unit.Value);

        public IConvaiOperation<Unit> StopListeningAsync(CancellationToken cancellationToken = default) =>
            _roomAudioCoordinator?.StopListeningAsync(cancellationToken) ??
            ConvaiOperation<Unit>.Succeeded(Unit.Value);

        public bool SetCharacterMuted(string characterId, bool muted) =>
            !string.IsNullOrEmpty(characterId) &&
            (_roomAudioCoordinator?.SetCharacterMuted(characterId, muted) ?? false);

        public bool IsCharacterMuted(string characterId) =>
            !string.IsNullOrEmpty(characterId) && (_roomAudioCoordinator?.IsCharacterMuted(characterId) ?? false);

        public bool SetRemoteAudioEnabled(string characterId, bool enabled)
        {
            if (string.IsNullOrEmpty(characterId) || _roomAudioCoordinator == null)
                return false;

            if (!AgentRegistry.TryGetCharacter(characterId, out IConvaiCharacterAgent character))
                return false;

            _roomAudioCoordinator.SetRemoteAudioEnabled(character, enabled);
            _convaiRoomController?.ApplyRemoteAudioPreference(characterId, enabled);
            return true;
        }

        public bool IsRemoteAudioEnabled(string characterId)
        {
            if (string.IsNullOrEmpty(characterId) || _roomAudioCoordinator == null)
                return false;

            return AgentRegistry.TryGetCharacter(characterId, out IConvaiCharacterAgent character) &&
                   _roomAudioCoordinator.IsRemoteAudioEnabled(character);
        }

        public bool ToggleMicMute()
        {
            bool newState = !IsMicMuted;
            SetMicMuted(newState);
            return newState;
        }

        public bool SetCharacterAudioMuted(IConvaiCharacterAgent character, bool mute) =>
            character != null && SetCharacterMuted(character.CharacterId, mute);

        public bool MuteCharacter(IConvaiCharacterAgent character) => SetCharacterAudioMuted(character, true);

        public bool UnmuteCharacter(IConvaiCharacterAgent character) => SetCharacterAudioMuted(character, false);

        public bool IsCharacterAudioMuted(IConvaiCharacterAgent character) =>
            character != null && IsCharacterMuted(character.CharacterId);

        public bool TryGetCharacterAudioPlayhead(string characterId, out double playedSeconds)
        {
            playedSeconds = 0d;
            if (string.IsNullOrEmpty(characterId) || _audioTrackManager == null) return false;

            return _audioTrackManager.TryGetAudioPlayhead(characterId, out playedSeconds);
        }

        bool IConvaiRoomAudioTimelineService.TryGetCharacterAudioTimeline(
            string characterId,
            out AudioTimelineSnapshot snapshot)
        {
            snapshot = default;
            return !string.IsNullOrEmpty(characterId) &&
                   _audioTrackManager != null &&
                   _audioTrackManager.TryGetAudioTimeline(characterId, out snapshot);
        }

        bool IConvaiRoomAudioMediaTimelineService.TryGetCharacterAudioMediaTimeline(
            string characterId,
            out AudioMediaTimelineSnapshot snapshot)
        {
            snapshot = default;
            return !string.IsNullOrEmpty(characterId) &&
                   _audioTrackManager != null &&
                   _audioTrackManager.TryGetAudioMediaTimeline(characterId, out snapshot);
        }

        private void HandleMicMuteChanged(bool isMuted)
        {
            _eventHub?.Publish(Domain.DomainEvents.Runtime.MicMuteChanged.Create(isMuted));
            MicMuteChanged?.Invoke(isMuted);
        }
    }
}
