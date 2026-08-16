using System;
using System.Collections.Generic;
using Convai.RestAPI;
using Convai.Runtime.Actions;
using Convai.Shared.Actions;
using UnityEngine;

namespace Convai.Runtime.Room
{
    public enum ConversationInputMode
    {
        HandsFree = 0,
        PushToTalk = 1
    }

    public enum TurnDetectionMode
    {
        UseDefault = 0,
        Disabled = 1,
        Custom = 2
    }

    public enum ServerSttInitialState
    {
        UseDefault = 0,
        Enabled = 1,
        Disabled = 2
    }

    public enum PushToTalkMicStartupMode
    {
        PrewarmMuted = 0,
        OpenOnFirstPress = 1
    }

    public enum ClientBargeInMode
    {
        Disabled = 0,
        Silero = 1
    }

    [Serializable]
    public sealed class BargeInOptions
    {
        public const float DefaultFadeOutSeconds = 0.12f;
        public const float MinimumFadeOutSeconds = 0.04f;
        public const float MaximumFadeOutSeconds = 0.25f;

        [field: SerializeField]
        [field:
            Tooltip(
                "Fade character audio locally when an interruption is requested or confirmed, instead of waiting for playback to stop abruptly.")]
        public bool SmoothInterruption { get; set; } = true;

        [field: SerializeField]
        [field: Range(MinimumFadeOutSeconds, MaximumFadeOutSeconds)]
        [field:
            Tooltip(
                "How long character audio takes to fade to silence after an interruption is committed.")]
        public float FadeOutSeconds { get; set; } = DefaultFadeOutSeconds;

        [field: SerializeField]
        [field:
            Tooltip(
                "Optional native client-side speech detection. Automatic client interruption requires acoustic echo cancellation; otherwise detection only ducks character audio and server voice activity remains authoritative.")]
        public ClientBargeInMode ClientDetection { get; set; } = ClientBargeInMode.Disabled;

        public BargeInOptions Clone() =>
            new()
            {
                SmoothInterruption = SmoothInterruption,
                FadeOutSeconds = Mathf.Clamp(
                    FadeOutSeconds,
                    MinimumFadeOutSeconds,
                    MaximumFadeOutSeconds),
                ClientDetection = ClientDetection
            };

        public static BargeInOptions CreateDefault() => new();
    }

    [Serializable]
    public sealed class SmartTurnSettings
    {
        public const float DefaultStopSecs = 3f;
        public const int DefaultPreSpeechMs = 0;
        public const float DefaultMaxDurationSecs = 8f;

        [field: SerializeField]
        [field: Tooltip("How long the player needs to stay silent before the SDK treats the turn as finished.")]
        public float StopSecs { get; set; } = DefaultStopSecs;

        [field: SerializeField]
        [field: Tooltip("How much audio just before speech starts should be kept, in milliseconds.")]
        public int PreSpeechMs { get; set; } = DefaultPreSpeechMs;

        [field: SerializeField]
        [field: Tooltip("The maximum length of a single user turn before the SDK forces it to end.")]
        public float MaxDurationSecs { get; set; } = DefaultMaxDurationSecs;

        public SmartTurnSettings Clone() =>
            new() { StopSecs = StopSecs, PreSpeechMs = PreSpeechMs, MaxDurationSecs = MaxDurationSecs };

        public static SmartTurnSettings CreateDefault() => new();
    }

    [Serializable]
    public sealed class UserVadSettings
    {
        public const float DefaultConfidence = VadParams.DefaultConfidence;
        public const float DefaultStartSecs = VadParams.DefaultStartSecs;
        public const float DefaultStopSecs = VadParams.DefaultStopSecs;
        public const float DefaultMinVolume = VadParams.DefaultMinVolume;

        [field: SerializeField]
        [field: Tooltip("If enabled, the SDK omits vad_params and core-service applies ConnectRequest defaults.")]
        public bool UseServerDefault { get; set; } = true;

        [field: SerializeField]
        [field: Range(0f, 1f)]
        [field: Min(0f)]
        [field: Tooltip("Confidence threshold used by backend voice activity detection.")]
        public float Confidence { get; set; } = DefaultConfidence;

        [field: SerializeField]
        [field: Min(0f)]
        [field: Tooltip("Speech duration in seconds before backend VAD treats user speech as started.")]
        public float StartSecs { get; set; } = DefaultStartSecs;

        [field: SerializeField]
        [field: Min(0f)]
        [field: Tooltip("Silence duration in seconds before backend VAD treats user speech as stopped. Core-service may clamp this to 0.2 seconds when hands-free smart turn is active.")]
        public float StopSecs { get; set; } = DefaultStopSecs;

        [field: SerializeField]
        [field: Range(0f, 1f)]
        [field: Min(0f)]
        [field: Tooltip("Minimum volume threshold used by backend voice activity detection.")]
        public float MinVolume { get; set; } = DefaultMinVolume;

        /// <summary>
        ///     Maps runtime settings to the REST payload. Returns null when server defaults should be used.
        /// </summary>
        public VadParams ToTransportVadParams()
        {
            if (UseServerDefault)
                return null;

            return new VadParams
            {
                Confidence = Confidence,
                StartSecs = StartSecs,
                StopSecs = StopSecs,
                MinVolume = MinVolume
            };
        }

        public UserVadSettings Clone() =>
            new()
            {
                UseServerDefault = UseServerDefault,
                Confidence = Confidence,
                StartSecs = StartSecs,
                StopSecs = StopSecs,
                MinVolume = MinVolume
            };

        public static UserVadSettings CreateDefault() => new();
    }

    [Serializable]
    public sealed class LocalAudioPolicy
    {
        [field: SerializeField]
        [field: Tooltip("If enabled, the local microphone begins muted when push-to-talk mode is active.")]
        public bool StartMutedInPushToTalk { get; set; } = true;

        [field: SerializeField]
        [field:
            Tooltip(
                "Opt in to acoustic echo cancellation for hands-free speakerphone use. Primarily intended for Android and iOS device speakers.")]
        public bool EnableAcousticEchoCancellation { get; set; }

        [field: SerializeField]
        [field:
            Tooltip(
                "Choose whether the microphone is prepared in advance or opened only when the player presses the push-to-talk key.")]
        public PushToTalkMicStartupMode PushToTalkStartupMode { get; set; } = PushToTalkMicStartupMode.PrewarmMuted;

        public LocalAudioPolicy Clone() =>
            new()
            {
                StartMutedInPushToTalk = StartMutedInPushToTalk,
                EnableAcousticEchoCancellation = EnableAcousticEchoCancellation,
                PushToTalkStartupMode = PushToTalkStartupMode
            };

        public static LocalAudioPolicy CreateDefault() => new();
    }

    [Serializable]
    public sealed class PushToTalkPolicy
    {
        public const int DefaultTurnCompletionTimeoutMs = 5000;
        public const int DefaultReleaseTailMs = 1000;
        public const int MaximumReleaseTailMs = 5000;

        [field: SerializeField]
        [field: Range(0, MaximumReleaseTailMs)]
        [field:
            Tooltip(
                "Length of each bounded finalization window after push-to-talk release. The SDK first waits this long for a final ASR result. If needed, it sends the authoritative stop and keeps capture open for one additional window so the provider can finalize. Set to 0 to stop and close immediately.")]
        public int ReleaseTailMs { get; set; } = DefaultReleaseTailMs;

        [field: SerializeField]
        [field:
            Tooltip(
                "Mute and unmute backend speech-to-text during push-to-talk as an optimization for server cost and connection hygiene.")]
        public bool EnableServerSttToggle { get; set; } = true;

        [field: SerializeField]
        [field:
            Tooltip(
                "If enabled, pressing push-to-talk while the character is speaking interrupts the character so the player can talk immediately.")]
        public bool InterruptBotOnPress { get; set; } = true;

        [field: SerializeField]
        [field:
            Tooltip(
                "If enabled, the player must wait for the current character response to finish before starting another push-to-talk turn.")]
        public bool RequireTurnCompletionBeforeNextPress { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Fallback timeout used to unlock push-to-talk if a final completion event never arrives.")]
        public int TurnCompletionTimeoutMs { get; set; } = DefaultTurnCompletionTimeoutMs;

        [field: SerializeField]
        [field:
            Tooltip(
                "If enabled, a character speech-stopped event can clear push-to-talk waiting state after speech has actually started.")]
        public bool AllowSpeechStoppedFallbackAfterSpeechStart { get; set; }

        public PushToTalkPolicy Clone() =>
            new()
            {
                ReleaseTailMs = ReleaseTailMs,
                EnableServerSttToggle = EnableServerSttToggle,
                InterruptBotOnPress = InterruptBotOnPress,
                RequireTurnCompletionBeforeNextPress = RequireTurnCompletionBeforeNextPress,
                TurnCompletionTimeoutMs = TurnCompletionTimeoutMs,
                AllowSpeechStoppedFallbackAfterSpeechStart = AllowSpeechStoppedFallbackAfterSpeechStart
            };

        public static PushToTalkPolicy CreateDefault() => new();
    }

    [Serializable]
    public sealed class TurnTakingOptions
    {
        [field: SerializeField]
        [field: Tooltip("Choose between hands-free conversation and push-to-talk behavior for this room.")]
        public ConversationInputMode Mode { get; set; } = ConversationInputMode.HandsFree;

        [field: SerializeField]
        [field:
            Tooltip(
                "Controls automatic end-of-turn detection in Hands Free mode. Push To Talk ends the turn when the player releases the push-to-talk control.")]
        public TurnDetectionMode TurnDetection { get; set; } = TurnDetectionMode.UseDefault;

        [field: SerializeField]
        [field: Tooltip("Fine-tune how the SDK decides that the player has finished speaking.")]
        public SmartTurnSettings CustomTurnDetection { get; set; } = SmartTurnSettings.CreateDefault();

        [field: SerializeField]
        [field:
            Tooltip(
                "Controls whether backend speech-to-text starts enabled when the session begins. SDK Default is enabled for Hands Free and disabled for Push To Talk.")]
        public ServerSttInitialState InitialServerStt { get; set; } = ServerSttInitialState.UseDefault;

        [field: SerializeField]
        [field: Tooltip("Controls local microphone behavior on this device, including push-to-talk startup and optional AEC for speakerphone use.")]
        public LocalAudioPolicy LocalAudioPolicy { get; set; } = LocalAudioPolicy.CreateDefault();

        [field: SerializeField]
        [field:
            Tooltip(
                "Controls how push-to-talk behaves when talking, releasing, waiting for responses, and handling fallback rules.")]
        public PushToTalkPolicy PushToTalkPolicy { get; set; } = PushToTalkPolicy.CreateDefault();

        [field: SerializeField]
        [field:
            Tooltip(
                "Controls how character playback reacts when the player interrupts, including smooth local fading and optional client-side speech detection.")]
        public BargeInOptions BargeIn { get; set; } = BargeInOptions.CreateDefault();

        public TurnTakingOptions Clone() =>
            new()
            {
                Mode = Mode,
                TurnDetection = TurnDetection,
                CustomTurnDetection = CustomTurnDetection?.Clone() ?? SmartTurnSettings.CreateDefault(),
                InitialServerStt = InitialServerStt,
                LocalAudioPolicy = LocalAudioPolicy?.Clone() ?? LocalAudioPolicy.CreateDefault(),
                PushToTalkPolicy = PushToTalkPolicy?.Clone() ?? PushToTalkPolicy.CreateDefault(),
                BargeIn = BargeIn?.Clone() ?? BargeInOptions.CreateDefault()
            };

        public static TurnTakingOptions CreateHandsFreeDefault() => new()
        {
            Mode = ConversationInputMode.HandsFree,
            TurnDetection = TurnDetectionMode.UseDefault,
            InitialServerStt = ServerSttInitialState.UseDefault,
            LocalAudioPolicy = LocalAudioPolicy.CreateDefault(),
            PushToTalkPolicy = PushToTalkPolicy.CreateDefault(),
            BargeIn = BargeInOptions.CreateDefault()
        };

        public static TurnTakingOptions CreatePushToTalkDefault() => new()
        {
            Mode = ConversationInputMode.PushToTalk,
            TurnDetection = TurnDetectionMode.UseDefault,
            InitialServerStt = ServerSttInitialState.UseDefault,
            LocalAudioPolicy = LocalAudioPolicy.CreateDefault(),
            PushToTalkPolicy = PushToTalkPolicy.CreateDefault(),
            BargeIn = BargeInOptions.CreateDefault()
        };
    }

    [Serializable]
    public sealed class RoomSessionConnectOptions
    {
        [NonSerialized] private string _explicitAuthToken;

        [field: SerializeField]
        public TurnTakingOptions TurnTaking { get; set; } = TurnTakingOptions.CreateHandsFreeDefault();

        [field: SerializeField] public string EndUserId { get; set; }

        /// <summary>
        ///     Optional shared session key used to group multiplayer participants into the same room.
        /// </summary>
        [field: SerializeField] public string SharedSessionKey { get; set; }

        /// <summary>
        ///     Optional maximum number of participants for the shared session.
        /// </summary>
        [field: SerializeField] public int MaxNumParticipants { get; set; }

        public IReadOnlyDictionary<string, object> EndUserMetadata { get; set; }

        public ConvaiActionConfig ActionConfigOverride { get; set; }
        public List<ConvaiActionDefinition> ActionDefinitionsOverride { get; set; }

        public RoomSessionConnectOptions Clone()
        {
            var clone = new RoomSessionConnectOptions
            {
                TurnTaking = TurnTaking?.Clone() ?? TurnTakingOptions.CreateHandsFreeDefault(),
                EndUserId = EndUserId,
                SharedSessionKey = SharedSessionKey,
                MaxNumParticipants = MaxNumParticipants,
                EndUserMetadata = EndUserMetadata == null
                    ? null
                    : new Dictionary<string, object>(EndUserMetadata),
                ActionConfigOverride = ActionConfigOverride?.Clone(),
                ActionDefinitionsOverride = ActionDefinitionsOverride == null
                    ? null
                    : ConvaiActionDefinition.CloneList(ActionDefinitionsOverride)
            };

            clone._explicitAuthToken = _explicitAuthToken;
            return clone;
        }

        internal void SetExplicitAuthToken(string authToken) =>
            _explicitAuthToken = authToken?.Trim();

        internal string ConsumeExplicitAuthToken()
        {
            string authToken = _explicitAuthToken;
            _explicitAuthToken = null;
            return authToken;
        }

        internal void ClearExplicitAuthToken() => _explicitAuthToken = null;
    }
}
