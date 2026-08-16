using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when backend-controlled VAD STT gating starts or stops listening.
    /// </summary>
    public readonly struct VadSttStateChanged
    {
        public bool IsActive { get; }
        public DateTime Timestamp { get; }

        public VadSttStateChanged(bool isActive, DateTime timestamp)
        {
            IsActive = isActive;
            Timestamp = timestamp;
        }

        public static VadSttStateChanged Create(bool isActive) => new(isActive, DateTime.UtcNow);
        public static VadSttStateChanged Started() => Create(true);
        public static VadSttStateChanged Stopped() => Create(false);
    }
}
