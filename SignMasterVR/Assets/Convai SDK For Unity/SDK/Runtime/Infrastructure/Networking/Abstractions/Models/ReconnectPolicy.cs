namespace Convai.Infrastructure.Networking.Models
{
    /// <summary>
    ///     Policy controlling reconnection behavior and timeout settings.
    /// </summary>
    public sealed class ReconnectPolicy
    {
        /// <summary>
        ///     Creates a new reconnect policy with the specified settings.
        /// </summary>
        public ReconnectPolicy(
            double roomRejoinTtlSeconds = 60.0,
            ResumePolicy resumePolicy = ResumePolicy.ResumeIfPossible,
            int maxReconnectAttempts = 3,
            bool spawnAgentOnRejoin = true,
            int startWaitTimeoutMs = 5000,
            float autoMicStartDelaySeconds = 0.5f)
        {
            RoomRejoinTtlSeconds = roomRejoinTtlSeconds;
            ResumePolicy = resumePolicy;
            MaxReconnectAttempts = maxReconnectAttempts;
            SpawnAgentOnRejoin = spawnAgentOnRejoin;
            StartWaitTimeoutMs = startWaitTimeoutMs;
            AutoMicStartDelaySeconds = autoMicStartDelaySeconds;
        }

        /// <summary>
        ///     Time-to-live in seconds for room rejoin eligibility.
        ///     After this duration since disconnect, a new room will be created instead of rejoining.
        ///     Default: 60 seconds.
        /// </summary>
        public double RoomRejoinTtlSeconds { get; }

        /// <summary>
        ///     Whether to attempt resuming the previous conversation via character_session_id.
        /// </summary>
        public ResumePolicy ResumePolicy { get; }

        /// <summary>
        ///     Maximum number of reconnect attempts before giving up.
        ///     Default: 3.
        /// </summary>
        public int MaxReconnectAttempts { get; }

        /// <summary>
        ///     Whether to spawn the agent when rejoining an existing room.
        ///     Default: true.
        /// </summary>
        public bool SpawnAgentOnRejoin { get; }

        /// <summary>
        ///     Timeout in milliseconds when waiting for Start() to complete during connection.
        ///     Default: 5000 (5 seconds).
        /// </summary>
        public int StartWaitTimeoutMs { get; }

        /// <summary>
        ///     Delay in seconds before starting the microphone after connection.
        ///     Default: 0.5 seconds.
        /// </summary>
        public float AutoMicStartDelaySeconds { get; }

        /// <summary>
        ///     Default reconnect policy with sensible defaults.
        /// </summary>
        public static ReconnectPolicy Default => new();

        /// <summary>
        ///     Policy that always creates a new room (no rejoin attempt).
        /// </summary>
        public static ReconnectPolicy AlwaysCreateNew => new(
            0,
            ResumePolicy.AlwaysFresh);

        /// <inheritdoc />
        public override string ToString() =>
            $"[ReconnectPolicy TTL={RoomRejoinTtlSeconds}s, Resume={ResumePolicy}, MaxAttempts={MaxReconnectAttempts}, StartTimeout={StartWaitTimeoutMs}ms]";
    }

    /// <summary>
    ///     Defines the conversation resume behavior on reconnect.
    /// </summary>
    public enum ResumePolicy
    {
        /// <summary>
        ///     Always start a fresh conversation (new character_session_id).
        /// </summary>
        AlwaysFresh,

        /// <summary>
        ///     Attempt to resume the previous conversation; fall back to fresh if resume fails.
        /// </summary>
        ResumeIfPossible,

        /// <summary>
        ///     Always resume; fail if resume is not possible.
        /// </summary>
        AlwaysResume
    }
}
