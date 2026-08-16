namespace Convai.Domain.Errors
{
    /// <summary>
    ///     Canonical hierarchical error codes for all SDK runtime flows.
    ///     Single source of truth for error codes across the SDK.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All error codes follow a hierarchical dot.notation format: {category}.{subcategory}_{detail}
    ///     </para>
    ///     <para>
    ///         Categories:
    ///         <list type="bullet">
    ///             <item><c>connection.*</c> - Network connection and authentication errors</item>
    ///             <item><c>transport.*</c> - LiveKit/WebRTC transport layer errors</item>
    ///             <item><c>audio.*</c> - Microphone and audio playback errors</item>
    ///             <item><c>vision.*</c> - Camera, webcam, and video publishing errors</item>
    ///             <item><c>server.*</c> - Backend pipeline errors and quota limits</item>
    ///             <item><c>session.*</c> - Session state and lifecycle errors</item>
    ///             <item><c>protocol.*</c> - RTVI message parsing errors</item>
    ///             <item><c>config.*</c> - Configuration and settings errors</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         Usage: Pass these codes to <see cref="DomainEvents.Session.SessionError" /> for structured error reporting.
    ///     </para>
    /// </remarks>
    public static class SessionErrorCodes
    {
        #region Connection Errors

        /// <summary>Authentication failed (invalid API key, expired token).</summary>
        public const string ConnectionAuthFailed = "connection.auth_failed";

        /// <summary>Provided connection token is invalid.</summary>
        public const string ConnectionInvalidToken = "connection.invalid_token";

        /// <summary>Rate limited by server.</summary>
        public const string ConnectionRateLimited = "connection.rate_limited";

        /// <summary>Service is temporarily unavailable (e.g., HTTP 503).</summary>
        public const string ConnectionServiceUnavailable = "connection.service_unavailable";

        /// <summary>Connection attempt timed out.</summary>
        public const string ConnectionTimeout = "connection.timeout";

        /// <summary>Network error during connection (DNS, socket, etc.).</summary>
        public const string ConnectionNetworkError = "connection.network_error";

        /// <summary>Server returned an error (5xx status codes).</summary>
        public const string ConnectionServerError = "connection.server_error";

        /// <summary>Resource not found (character, room).</summary>
        public const string ConnectionNotFound = "connection.not_found";

        /// <summary>Bad request (invalid parameters).</summary>
        public const string ConnectionBadRequest = "connection.bad_request";

        /// <summary>Generic connection failure.</summary>
        public const string ConnectionFailed = "connection.failed";

        /// <summary>Failed to resolve a short-lived auth token before connecting.</summary>
        public const string ConnectionAuthTokenFetchFailed = "connection.auth_token_fetch_failed";

        /// <summary>Connect API validation error (HTTP 422).</summary>
        public const string ConnectionConnectValidationError = "connection.connect_validation_error";

        /// <summary>Connect API rejected an invalid session identifier.</summary>
        public const string ConnectionConnectInvalidSessionId = "connection.connect_invalid_session_id";

        /// <summary>Connect API rejected the provided API key.</summary>
        public const string ConnectionConnectInvalidApiKey = "connection.connect_invalid_api_key";

        /// <summary>Realtime access is not enabled for this account or environment.</summary>
        public const string ConnectionConnectRealtimeNotAllowed = "connection.connect_realtime_not_allowed";

        /// <summary>The requested character was not found or is not accessible.</summary>
        public const string ConnectionConnectCharacterNotFound = "connection.connect_character_not_found";

        /// <summary>The concurrent session limit for the plan has been reached.</summary>
        public const string ConnectionConnectConcurrencyLimitReached =
            "connection.connect_concurrency_limit_reached";

        /// <summary>The backend could not create or resolve a speaker due to limit constraints.</summary>
        public const string ConnectionConnectSpeakerLimitReached = "connection.connect_speaker_limit_reached";

        /// <summary>The backend failed while starting the bot pipeline.</summary>
        public const string ConnectionConnectBotStartFailed = "connection.connect_bot_start_failed";

        /// <summary>An unhandled server exception occurred during connect.</summary>
        public const string ConnectionConnectUnhandledServerException =
            "connection.connect_unhandled_server_exception";

        #endregion

        #region Transport Errors

        /// <summary>LiveKit transport error.</summary>
        public const string TransportLivekitError = "transport.livekit_error";

        /// <summary>ICE connection failed during WebRTC negotiation.</summary>
        public const string TransportIceFailed = "transport.ice_failed";

        /// <summary>Peer connection failed.</summary>
        public const string TransportPeerConnectionFailed = "transport.peer_connection_failed";

        /// <summary>Signal connection disconnected.</summary>
        public const string TransportSignalDisconnected = "transport.signal_disconnected";

        #endregion

        #region Audio Errors

        /// <summary>Microphone is unavailable.</summary>
        public const string AudioMicUnavailable = "audio.mic_unavailable";

        /// <summary>Microphone permission denied by user.</summary>
        public const string AudioMicPermissionDenied = "audio.mic_permission_denied";

        /// <summary>Failed to publish microphone track.</summary>
        public const string AudioMicPublishFailed = "audio.mic_publish_failed";

        #endregion

        #region Vision Errors (Camera/Webcam)

        /// <summary>Target camera became unavailable or was destroyed.</summary>
        public const string VisionCameraLost = "vision.camera_lost";

        /// <summary>No camera was assigned and Camera.main is null.</summary>
        public const string VisionCameraNotFound = "vision.camera_not_found";

        /// <summary>Camera component is disabled.</summary>
        public const string VisionCameraDisabled = "vision.camera_disabled";

        /// <summary>No webcam devices found on the system.</summary>
        public const string VisionDeviceNotFound = "vision.device_not_found";

        /// <summary>Specified webcam device is not available.</summary>
        public const string VisionDeviceUnavailable = "vision.device_unavailable";

        /// <summary>Webcam failed to start within the timeout period.</summary>
        public const string VisionDeviceInitTimeout = "vision.device_init_timeout";

        /// <summary>Webcam was disconnected during capture.</summary>
        public const string VisionDeviceDisconnected = "vision.device_disconnected";

        /// <summary>Failed to create RenderTexture.</summary>
        public const string VisionRenderTextureFailed = "vision.render_texture_failed";

        /// <summary>RenderTexture not available after initialization timeout.</summary>
        public const string VisionRenderTextureTimeout = "vision.render_texture_timeout";

        /// <summary>GPU resources exhausted or unavailable.</summary>
        public const string VisionGpuResourcesFailed = "vision.gpu_resources_failed";

        /// <summary>Failed to publish video track to LiveKit room.</summary>
        public const string VisionPublishFailed = "vision.publish_failed";

        /// <summary>Room is not connected, cannot publish video.</summary>
        public const string VisionRoomNotConnected = "vision.room_not_connected";

        /// <summary>Video track manager not initialized.</summary>
        public const string VisionTrackManagerNotReady = "vision.track_manager_not_ready";

        /// <summary>Webcam permission was denied by the user.</summary>
        public const string VisionPermissionDenied = "vision.permission_denied";

        /// <summary>Webcam permission request timed out.</summary>
        public const string VisionPermissionTimeout = "vision.permission_timeout";

        /// <summary>Frame source component is null or missing.</summary>
        public const string VisionFrameSourceNull = "vision.frame_source_null";

        /// <summary>Vision capture was already stopped.</summary>
        public const string VisionAlreadyStopped = "vision.already_stopped";

        /// <summary>Unknown vision error occurred.</summary>
        public const string VisionUnknown = "vision.unknown";

        #endregion

        #region Server Errors

        /// <summary>Non-fatal pipeline error reported by the backend.</summary>
        public const string ServerError = "server.error";

        /// <summary>Fatal pipeline error reported by the backend (session will be terminated).</summary>
        public const string ServerFatalError = "server.fatal_error";

        /// <summary>Usage quota exceeded (daily, monthly, etc.). Pipeline is terminated.</summary>
        public const string ServerUsageLimitReached = "server.usage_limit_reached";

        #endregion

        #region Session Errors

        /// <summary>Session token expired.</summary>
        public const string SessionTokenExpired = "session.token_expired";

        /// <summary>Invalid state transition attempted.</summary>
        public const string SessionInvalidState = "session.invalid_state";

        /// <summary>Session was cancelled by user.</summary>
        public const string SessionCancelled = "session.cancelled";

        #endregion

        #region Protocol Errors

        /// <summary>Invalid protocol message received.</summary>
        public const string ProtocolMessageInvalid = "protocol.message_invalid";

        /// <summary>Failed to parse protocol message.</summary>
        public const string ProtocolParseFailed = "protocol.parse_failed";

        #endregion

        #region Config Errors

        /// <summary>API key is missing or not configured.</summary>
        public const string ConfigApiKeyMissing = "config.api_key_missing";

        /// <summary>Auth Token mode has no registered provider or configured endpoint.</summary>
        public const string ConfigAuthTokenProviderMissing = "config.auth_token_provider_missing";

        /// <summary>The auth-token endpoint is not HTTPS or an HTTP loopback development URL.</summary>
        public const string ConfigAuthTokenEndpointInvalid = "config.auth_token_endpoint_invalid";

        /// <summary>An explicit auth-token connection was requested while another auth mode is selected.</summary>
        public const string ConfigAuthTokenModeRequired = "config.auth_token_mode_required";

        /// <summary>Character ID is missing or not configured.</summary>
        public const string ConfigCharacterIdMissing = "config.character_id_missing";

        #endregion

        #region Utility Methods

        /// <summary>
        ///     Gets a human-readable description for an error code.
        /// </summary>
        /// <param name="errorCode">The error code.</param>
        /// <returns>Human-readable description.</returns>
        public static string GetDescription(string errorCode)
        {
            return errorCode switch
            {
                // Connection
                ConnectionAuthFailed => "Authentication failed",
                ConnectionInvalidToken => "Connection token is invalid",
                ConnectionRateLimited => "Rate limited by server",
                ConnectionServiceUnavailable => "Service is temporarily unavailable",
                ConnectionTimeout => "Connection attempt timed out",
                ConnectionNetworkError => "Network error during connection",
                ConnectionServerError => "Server returned an error",
                ConnectionNotFound => "Resource not found",
                ConnectionBadRequest => "Bad request",
                ConnectionFailed => "Connection failed",
                ConnectionAuthTokenFetchFailed => "Failed to fetch an auth token",
                ConnectionConnectValidationError => "Connect request failed validation",
                ConnectionConnectInvalidSessionId => "Connect request used an invalid session ID",
                ConnectionConnectInvalidApiKey => "Connect request used an invalid API key",
                ConnectionConnectRealtimeNotAllowed => "Realtime access is not allowed",
                ConnectionConnectCharacterNotFound => "Character not found or inaccessible",
                ConnectionConnectConcurrencyLimitReached => "Concurrent session limit reached",
                ConnectionConnectSpeakerLimitReached => "Speaker limit reached",
                ConnectionConnectBotStartFailed => "Failed to start bot",
                ConnectionConnectUnhandledServerException => "Unhandled server exception during connect",

                // Transport
                TransportLivekitError => "LiveKit transport error",
                TransportIceFailed => "ICE connection failed",
                TransportPeerConnectionFailed => "Peer connection failed",
                TransportSignalDisconnected => "Signal connection disconnected",

                // Audio
                AudioMicUnavailable => "Microphone is unavailable",
                AudioMicPermissionDenied => "Microphone permission denied",
                AudioMicPublishFailed => "Failed to publish microphone track",

                // Vision
                VisionCameraLost => "Target camera became unavailable or was destroyed",
                VisionCameraNotFound => "No camera was assigned and Camera.main is null",
                VisionCameraDisabled => "Camera component is disabled",
                VisionDeviceNotFound => "No webcam devices found on the system",
                VisionDeviceUnavailable => "Specified webcam device is not available",
                VisionDeviceInitTimeout => "Webcam failed to start within the timeout period",
                VisionDeviceDisconnected => "Webcam was disconnected during capture",
                VisionRenderTextureFailed => "Failed to create RenderTexture",
                VisionRenderTextureTimeout => "RenderTexture not available after initialization timeout",
                VisionGpuResourcesFailed => "GPU resources exhausted or unavailable",
                VisionPublishFailed => "Failed to publish video track to LiveKit room",
                VisionRoomNotConnected => "Room is not connected, cannot publish video",
                VisionTrackManagerNotReady => "Video track manager not initialized",
                VisionPermissionDenied => "Webcam permission was denied by the user",
                VisionPermissionTimeout => "Webcam permission request timed out",
                VisionFrameSourceNull => "Frame source component is null or missing",
                VisionAlreadyStopped => "Vision capture was already stopped",
                VisionUnknown => "Unknown vision error occurred",

                // Server
                ServerError => "Non-fatal pipeline error reported by the backend",
                ServerFatalError => "Fatal pipeline error reported by the backend",
                ServerUsageLimitReached => "Usage quota exceeded",

                // Session
                SessionTokenExpired => "Session token expired",
                SessionInvalidState => "Invalid state transition attempted",
                SessionCancelled => "Session was cancelled by user",

                // Protocol
                ProtocolMessageInvalid => "Invalid protocol message received",
                ProtocolParseFailed => "Failed to parse protocol message",

                // Config
                ConfigApiKeyMissing => "API key is missing or not configured",
                ConfigAuthTokenProviderMissing => "Auth token provider or endpoint is not configured",
                ConfigAuthTokenEndpointInvalid => "Auth token endpoint URL is invalid",
                ConfigAuthTokenModeRequired => "Auth Token mode is required",
                ConfigCharacterIdMissing => "Character ID is missing or not configured",

                _ => $"Unknown error: {errorCode}"
            };
        }

        /// <summary>
        ///     Gets the category prefix from an error code.
        /// </summary>
        /// <param name="errorCode">The error code.</param>
        /// <returns>Category prefix (e.g., "connection" from "connection.timeout").</returns>
        public static string GetCategory(string errorCode)
        {
            if (string.IsNullOrEmpty(errorCode))
                return "unknown";

            int dotIndex = errorCode.IndexOf('.');
            return dotIndex > 0 ? errorCode.Substring(0, dotIndex) : errorCode;
        }

        /// <summary>
        ///     Determines if an error code represents a recoverable error.
        /// </summary>
        /// <param name="errorCode">The error code.</param>
        /// <returns>True if the error may be recoverable via retry.</returns>
        public static bool IsRecoverable(string errorCode)
        {
            if (string.IsNullOrEmpty(errorCode))
                return false;

            // Transient errors that may succeed on retry
            return errorCode == ConnectionTimeout ||
                   errorCode == ConnectionNetworkError ||
                   errorCode == ConnectionServerError ||
                   errorCode == ConnectionServiceUnavailable ||
                   errorCode == ConnectionRateLimited ||
                   errorCode == ConnectionConnectConcurrencyLimitReached ||
                   errorCode == ConnectionConnectBotStartFailed ||
                   errorCode == ConnectionConnectUnhandledServerException ||
                   errorCode == ConnectionAuthTokenFetchFailed ||
                   errorCode == TransportIceFailed ||
                   errorCode == TransportSignalDisconnected ||
                   errorCode == VisionDeviceInitTimeout ||
                   errorCode == VisionRenderTextureTimeout ||
                   errorCode == ServerError;
        }

        #endregion
    }
}
