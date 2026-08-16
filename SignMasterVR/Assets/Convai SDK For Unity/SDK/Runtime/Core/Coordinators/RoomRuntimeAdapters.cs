using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Infrastructure.Networking;

namespace Convai.Runtime.Core.Coordinators
{
    internal interface IRoomConnectionRuntimeAdapter
    {
        public SessionState CurrentState { get; }
        public bool IsConnected { get; }
        public string CurrentRoomName { get; }
        public string CurrentSessionId { get; }
        public bool CanConnect { get; }
        public bool HasStarted { get; }
        public event Action<SessionStateChanged> StateChanged;

        public Task<bool> WaitForStartCompletionAsync(CancellationToken ct);
        public bool PrepareConnection();
        public Task<bool> WaitForConnectionResolutionAsync(CancellationToken ct);
        public Task<RoomConnectionAttemptResult> ConnectAsync(CancellationToken ct);
        public Task DisconnectAsync(CancellationToken ct);
        public void NotifyStateChanged(SessionStateChanged stateChanged);
    }

    internal interface IRoomAudioRuntimeAdapter
    {
        public bool IsMicMuted { get; }

        public void SetMicMuted(bool muted);
        public Task StartListeningAsync(int microphoneIndex, CancellationToken ct);
        public Task StopListeningAsync(CancellationToken ct);
        public bool SetCharacterMuted(string characterId, bool muted);
        public bool IsCharacterMuted(string characterId);
    }
}
