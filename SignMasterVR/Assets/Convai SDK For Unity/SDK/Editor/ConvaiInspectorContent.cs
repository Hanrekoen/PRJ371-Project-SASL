using UnityEngine;

namespace Convai.Editor
{
    internal static class ConvaiInspectorContent
    {
        internal static readonly GUIContent RoomSetupSource = new(
            "Room Setup Source",
            "Choose whether this component uses scene-specific advanced room settings or a reusable Room Manager Profile asset.");

        internal static readonly GUIContent RoomConfigAsset = new(
            "Room Manager Profile Asset",
            "A reusable asset that stores advanced room defaults you can share across scenes.");

        internal static readonly GUIContent ConnectionType = new(
            "Connection Type",
            "Select whether the conversation uses audio only or video features as part of the room connection.");

        internal static readonly GUIContent VideoTrackName = new(
            "Video Track Name",
            "The media track name used when the room publishes video frames.");

        internal static readonly GUIContent VisionContextMode = new(
            "Dynamic Vision Context",
            "Controls backend dynamic context vision. Auto follows the configured Connection Type (Video enables it); Enabled always uses video; Disabled never sends vision config.");

        internal static readonly GUIContent VisionInputSettings = new(
            "Frame Sampling",
            "Backend frame sampling settings sent as vision_input_config.");

        internal static readonly GUIContent VisionRespondModes = new(
            "Respond Modes",
            "Backend response-mode policy sent as respond_modes.");

        internal static readonly GUIContent Server = new(
            "Server",
            "Choose which backend route the room should use. Normal projects should use Connect.");

        internal static readonly GUIContent StartsConnected = new(
            "Starts Connected",
            "If enabled, the scene connects automatically when play starts.");

        internal static readonly GUIContent ConnectOnStart = new(
            "Connect On Start",
            "If enabled, this room connects automatically when play starts.");

        internal static readonly GUIContent TurnTaking = new(
            "Turn Taking",
            "Controls how the SDK decides when the player has finished talking and whether backend speech-to-text starts enabled.");

        internal static readonly GUIContent TurnTakingMode = new(
            "Mode",
            "Choose between hands-free conversation and push-to-talk behavior for this room.");

        internal static readonly GUIContent TurnDetection = new(
            "Turn Detection",
            "Controls automatic end-of-turn detection in Hands Free mode. Push To Talk ends the turn when the player releases the push-to-talk control.");

        internal static readonly GUIContent CustomTurnDetection = new(
            "Turn Detection Values",
            "Values used when Turn Detection is set to Custom Values. They stay visible in other modes so you can compare them with the resolved behavior.");

        internal static readonly GUIContent StopSecs = new(
            "Stop Secs",
            "How long the player needs to stay silent before the SDK treats the turn as finished.");

        internal static readonly GUIContent PreSpeechMs = new(
            "Pre Speech Ms",
            "How much audio just before speech starts should be kept, in milliseconds.");

        internal static readonly GUIContent MaxDurationSecs = new(
            "Max Duration Secs",
            "The maximum length of a single user turn before the SDK forces it to end.");

        internal static readonly GUIContent UserVad = new(
            "VAD",
            "Connect-time backend Silero VAD thresholds sent as vad_params. Applies on the discrete LLM/STT path; not used for realtime provider VAD or push-to-talk turn ending.");

        internal static readonly GUIContent UseServerDefaultVad = new(
            "Use Server Default",
            "If enabled, Unity omits vad_params and core-service applies its ConnectRequest defaults.");

        internal static readonly GUIContent VadConfidence = new(
            "Confidence",
            "Confidence threshold used by backend voice activity detection.");

        internal static readonly GUIContent VadStartSecs = new(
            "Start Secs",
            "Speech duration in seconds before backend VAD treats user speech as started.");

        internal static readonly GUIContent VadStopSecs = new(
            "Stop Secs",
            "Silence duration in seconds before backend VAD treats user speech as stopped. When hands-free smart turn is active, core-service may clamp this to 0.2 seconds.");

        internal static readonly GUIContent VadMinVolume = new(
            "Min Volume",
            "Minimum volume threshold used by backend voice activity detection.");

        internal static readonly GUIContent InitialServerStt = new(
            "Initial Server STT",
            "Controls whether backend speech-to-text starts enabled when the session begins. SDK Default is enabled for Hands Free and disabled for Push To Talk.");

        internal static readonly GUIContent LocalAudioPolicy = new(
            "Local Audio Policy",
            "Controls local microphone behavior on this device, including push-to-talk startup and optional AEC for speakerphone use.");

        internal static readonly GUIContent StartMutedInPushToTalk = new(
            "Start Muted In Push To Talk",
            "If enabled, the local microphone begins muted when push-to-talk mode is active.");

        internal static readonly GUIContent EnableAcousticEchoCancellation = new(
            "Enable Acoustic Echo Cancellation",
            "Opt in to acoustic echo cancellation for hands-free speakerphone use. Primarily intended for Android and iOS device speakers.");

        internal static readonly GUIContent PushToTalkStartupMode = new(
            "Push To Talk Startup Mode",
            "Choose whether the microphone is prepared in advance or opened only when the player presses the push-to-talk key.");

        internal static readonly GUIContent PushToTalkPolicy = new(
            "Push To Talk Policy",
            "Controls how push-to-talk behaves when talking, releasing, waiting for responses, and handling fallback rules.");

        internal static readonly GUIContent BargeIn = new(
            "Barge In",
            "Controls how character playback reacts when the player interrupts.");

        internal static readonly GUIContent SmoothInterruption = new(
            "Smooth Interruption",
            "Fade character playback locally when an interruption is requested or confirmed.");

        internal static readonly GUIContent BargeInFadeOutSeconds = new(
            "Fade Out Seconds",
            "How long character audio takes to fade to silence after an interruption is committed.");

        internal static readonly GUIContent ClientBargeInDetection = new(
            "Client Speech Detection",
            "Optional native client-side speech detection. Automatic client interruption requires acoustic echo cancellation; otherwise detection only ducks character audio.");

        internal static readonly GUIContent EnableServerSttToggle = new(
            "Enable Server STT Toggle",
            "Mute and unmute backend speech-to-text during push-to-talk as an optimization for server cost and connection hygiene.");

        internal static readonly GUIContent InterruptBotOnPress = new(
            "Interrupt Bot On Press",
            "If enabled, pressing push-to-talk while the character is speaking will interrupt the character so the player can talk immediately.");

        internal static readonly GUIContent RequireTurnCompletionBeforeNextPress = new(
            "Require Turn Completion Before Next Press",
            "If enabled, the player must wait for the current character response to finish before starting another push-to-talk turn.");

        internal static readonly GUIContent TurnCompletionTimeoutMs = new(
            "Turn Completion Timeout Ms",
            "Fallback timeout used to unlock push-to-talk if a final completion event never arrives.");

        internal static readonly GUIContent ReleaseTailMs = new(
            "Release Tail (ms)",
            "Length of each bounded finalization window after push-to-talk release. The SDK first waits this long for a final ASR result. If needed, it sends the stop and keeps capture open for one additional window so the provider can finalize. 0 stops and closes immediately.");

        internal static readonly GUIContent AllowSpeechStoppedFallbackAfterSpeechStart = new(
            "Allow Speech Stopped Fallback",
            "If enabled, a character speech-stopped event can clear push-to-talk waiting state after speech has actually started.");

        internal static readonly GUIContent HowThePlayerTalks = new(
            "How The Player Talks",
            "Choose whether this scene uses hands-free conversation or push-to-talk.");

        internal static readonly GUIContent PushToTalkKey = new(
            "Push To Talk Key",
            "The keyboard key the player holds to speak in push-to-talk mode.");

        internal static readonly GUIContent InterruptCharacterWhenPressed = new(
            "Interrupt Character When Pressed",
            "If enabled, pressing the push-to-talk key while the character is speaking interrupts the character first.");

        internal static readonly GUIContent WaitForCharacterToFinishBeforeTalkingAgain = new(
            "Wait For Character To Finish Before Talking Again",
            "If enabled, push-to-talk blocks a new press until the current character turn is considered finished.");

        internal static readonly GUIContent FallbackWaitTimeMs = new(
            "Fallback Wait Time (ms)",
            "How long push-to-talk should wait before clearing the current turn if no completion signal arrives.");

        internal static readonly GUIContent RoomRejoinTtlSeconds = new(
            "Room Rejoin TTL Seconds",
            "How long the room may be resumed after a disconnect before a full new join is required.");

        internal static readonly GUIContent ResumePolicy = new(
            "Resume Policy",
            "Controls whether the SDK tries to resume the same room session after reconnecting.");

        internal static readonly GUIContent MaxReconnectAttempts = new(
            "Max Reconnect Attempts",
            "The maximum number of reconnect attempts before the room gives up.");

        internal static readonly GUIContent SpawnAgentOnRejoin = new(
            "Spawn Agent On Rejoin",
            "If enabled, the backend can recreate the conversational agent when rejoining the room.");

        internal static readonly GUIContent StartWaitTimeoutMs = new(
            "Start Wait Timeout Ms",
            "How long the SDK waits for room startup before timing out.");

        internal static readonly GUIContent AutoMicStartDelaySeconds = new(
            "Auto Mic Start Delay Seconds",
            "Optional delay before the SDK starts the microphone automatically after connecting.");

        internal static readonly GUIContent Debug = new(
            "Debug",
            "Enable extra room-level diagnostic behavior and logging for troubleshooting.");

        internal static readonly GUIContent PlayerName = new(
            "Player Name",
            "Display name for the local player, used in transcripts and UI.");

        internal static readonly GUIContent PlayerId = new(
            "Player ID",
            "Optional local transcript identifier for the player. Leave it empty to reuse the player name. This is not the backend speaker ID.");

        internal static readonly GUIContent PlayerNameTagColor = new(
            "Name Tag Color",
            "Color used when the player appears in transcript or name-tag style UI.");
    }
}
