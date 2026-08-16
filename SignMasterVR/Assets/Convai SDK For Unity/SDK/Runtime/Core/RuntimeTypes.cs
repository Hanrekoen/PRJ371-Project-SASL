using System;

namespace Convai.Runtime.Core
{
    /// <summary>
    ///     Represents the lifecycle state of the Convai runtime.
    /// </summary>
    public enum RuntimeState
    {
        /// <summary>Runtime has been created but not yet started.</summary>
        Created,

        /// <summary>Runtime is starting (async initialization in progress).</summary>
        Starting,

        /// <summary>Runtime is running and fully operational.</summary>
        Running,

        /// <summary>Runtime is pausing (async pause in progress).</summary>
        Pausing,

        /// <summary>Runtime is paused (can be resumed).</summary>
        Paused,

        /// <summary>Runtime is resuming from pause.</summary>
        Resuming,

        /// <summary>Runtime is stopping (async shutdown in progress).</summary>
        Stopping,

        /// <summary>Runtime has stopped and cannot be restarted.</summary>
        Stopped,

        /// <summary>Runtime has been disposed.</summary>
        Disposed
    }

    /// <summary>
    ///     Reason for pausing the runtime.
    /// </summary>
    public enum RuntimePauseReason
    {
        /// <summary>No specific reason provided.</summary>
        None,

        /// <summary>Paused due to application losing focus.</summary>
        ApplicationFocusLost,

        /// <summary>Paused due to application going to background.</summary>
        ApplicationBackground,

        /// <summary>Paused by user request.</summary>
        UserRequested,

        /// <summary>Paused due to network disconnection.</summary>
        NetworkDisconnected,

        /// <summary>Paused due to scene transition.</summary>
        SceneTransition
    }

    /// <summary>
    ///     Controls how Convai presentation behaves while the Unity application is in the background.
    /// </summary>
    public enum RuntimeBackgroundPolicy
    {
        /// <summary>
        ///     Keep audio, transcript presentation, and LipSync advancing. Unity background execution is requested.
        /// </summary>
        ContinueAudibly = 0,

        /// <summary>
        ///     Keep the room connected while local audio, transcript presentation, and LipSync presentation are paused.
        /// </summary>
        PauseTimeline = 1,

        /// <summary>
        ///     Mute character audio locally while audio, transcript history, and LipSync continue advancing to live time.
        /// </summary>
        MuteButCatchUp = 2
    }

    /// <summary>
    ///     Event data for an application background-policy transition.
    /// </summary>
    public readonly struct RuntimeBackgroundStateChanged
    {
        public RuntimeBackgroundStateChanged(
            bool isBackgrounded,
            RuntimeBackgroundPolicy requestedPolicy,
            RuntimeBackgroundPolicy effectivePolicy,
            RuntimePauseReason reason,
            DateTime timestamp)
        {
            IsBackgrounded = isBackgrounded;
            RequestedPolicy = requestedPolicy;
            EffectivePolicy = effectivePolicy;
            Reason = reason;
            Timestamp = timestamp;
        }

        /// <summary>Whether Unity currently considers the application backgrounded.</summary>
        public bool IsBackgrounded { get; }

        /// <summary>The project or runtime policy requested by the caller.</summary>
        public RuntimeBackgroundPolicy RequestedPolicy { get; }

        /// <summary>The policy actually applied on the current platform.</summary>
        public RuntimeBackgroundPolicy EffectivePolicy { get; }

        /// <summary>The Unity lifecycle reason that caused the transition.</summary>
        public RuntimePauseReason Reason { get; }

        /// <summary>When the transition completed (UTC).</summary>
        public DateTime Timestamp { get; }

        public static RuntimeBackgroundStateChanged Create(
            bool isBackgrounded,
            RuntimeBackgroundPolicy requestedPolicy,
            RuntimeBackgroundPolicy effectivePolicy,
            RuntimePauseReason reason) =>
            new(isBackgrounded, requestedPolicy, effectivePolicy, reason, DateTime.UtcNow);
    }

    /// <summary>
    ///     Event data for runtime state changes.
    /// </summary>
    public readonly struct RuntimeStateChanged
    {
        /// <summary>Creates a new state change event.</summary>
        public RuntimeStateChanged(RuntimeState previousState, RuntimeState newState,
            RuntimePauseReason pauseReason = RuntimePauseReason.None)
        {
            PreviousState = previousState;
            NewState = newState;
            PauseReason = pauseReason;
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>The state before the change.</summary>
        public RuntimeState PreviousState { get; }

        /// <summary>The new state after the change.</summary>
        public RuntimeState NewState { get; }

        /// <summary>Reason for pause (if applicable).</summary>
        public RuntimePauseReason PauseReason { get; }

        /// <summary>When the state change occurred.</summary>
        public DateTime Timestamp { get; }
    }

    /// <summary>
    ///     Descriptor for a player to be registered with the runtime.
    /// </summary>
    public readonly struct PlayerDescriptor
    {
        /// <summary>Creates a new player descriptor.</summary>
        public PlayerDescriptor(string playerId, string displayName = null)
        {
            PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
            DisplayName = displayName ?? string.Empty;
        }

        /// <summary>Unique local player identifier for the player.</summary>
        public string PlayerId { get; }

        /// <summary>Display name for the player.</summary>
        public string DisplayName { get; }

        /// <summary>Whether this is a valid descriptor.</summary>
        public bool IsValid => !string.IsNullOrEmpty(PlayerId);
    }

    /// <summary>
    ///     Descriptor for a character to be registered with the runtime.
    /// </summary>
    public readonly struct CharacterDescriptor
    {
        /// <summary>Creates a new character descriptor.</summary>
        public CharacterDescriptor(string characterId, string characterName = null, string ownerId = null)
        {
            CharacterId = characterId ?? throw new ArgumentNullException(nameof(characterId));
            CharacterName = characterName ?? string.Empty;
            OwnerId = ownerId ?? string.Empty;
        }

        /// <summary>Unique character identifier from Convai dashboard.</summary>
        public string CharacterId { get; }

        /// <summary>Display name for the character.</summary>
        public string CharacterName { get; }

        /// <summary>Owner ID (player who controls this character).</summary>
        public string OwnerId { get; }

        /// <summary>Whether this is a valid descriptor.</summary>
        public bool IsValid => !string.IsNullOrEmpty(CharacterId);
    }
}
