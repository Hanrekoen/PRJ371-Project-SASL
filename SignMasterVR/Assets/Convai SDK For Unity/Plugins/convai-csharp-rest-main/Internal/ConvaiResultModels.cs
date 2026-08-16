using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Convai.RestAPI.Internal
{
    /// <summary>
    /// Represents an end user from the /user/end-users/* APIs.
    /// </summary>
    [Serializable]
    public class EndUserDetails
    {
        public EndUserDetails(string endUserId, string lastActiveTs, string lastLtmUsageTs, Dictionary<string, object> metadata)
        {
            EndUserId = endUserId;
            LastActiveTs = lastActiveTs;
            LastLtmUsageTs = lastLtmUsageTs;
            Metadata = metadata ?? new Dictionary<string, object>();
        }

        [JsonProperty("end_user_id")] public string EndUserId { get; set; }
        [JsonProperty("last_active_ts")] public string LastActiveTs { get; set; }
        [JsonProperty("last_ltm_usage_ts")] public string LastLtmUsageTs { get; set; }
        [JsonProperty("end_user_metadata")] public Dictionary<string, object> Metadata { get; set; }

        /// <summary>
        /// Gets the display name from metadata, or a descriptive fallback with last active info.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (Metadata != null && Metadata.TryGetValue("name", out var name) && name != null)
                {
                    string nameStr = name.ToString();
                    if (!string.IsNullOrWhiteSpace(nameStr))
                    {
                        return nameStr;
                    }
                }

                string idPrefix = GetIdPrefix();
                string activeInfo = FormatLastActive();

                if (!string.IsNullOrEmpty(activeInfo))
                {
                    return $"{idPrefix} ({activeInfo})";
                }

                return idPrefix;
            }
        }

        /// <summary>
        /// Gets a short prefix from the end_user_id for identification.
        /// </summary>
        private string GetIdPrefix()
        {
            if (string.IsNullOrEmpty(EndUserId))
            {
                return "Unknown User";
            }

            string prefix = EndUserId.Length > 8 ? EndUserId.Substring(0, 8) : EndUserId;
            return $"User {prefix}";
        }

        /// <summary>
        /// Formats the last active timestamp for display.
        /// </summary>
        private string FormatLastActive()
        {
            if (string.IsNullOrEmpty(LastActiveTs))
            {
                return null;
            }

            try
            {
                if (System.DateTime.TryParse(LastActiveTs, out System.DateTime lastActive))
                {
                    var now = System.DateTime.UtcNow;
                    var diff = now - lastActive;

                    if (diff.TotalMinutes < 1)
                    {
                        return "just now";
                    }
                    if (diff.TotalHours < 1)
                    {
                        return $"{(int)diff.TotalMinutes}m ago";
                    }
                    if (diff.TotalDays < 1)
                    {
                        return $"{(int)diff.TotalHours}h ago";
                    }
                    if (diff.TotalDays < 30)
                    {
                        return $"{(int)diff.TotalDays}d ago";
                    }

                    return lastActive.ToString("MMM d");
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Gets a truncated version of the end_user_id for compact display.
        /// </summary>
        public string ShortId
        {
            get
            {
                if (string.IsNullOrEmpty(EndUserId))
                {
                    return "N/A";
                }

                if (EndUserId.Length <= 16)
                {
                    return EndUserId;
                }

                return EndUserId.Substring(0, 8) + "..." + EndUserId.Substring(EndUserId.Length - 4);
            }
        }
    }

    /// <summary>
    /// Response model for the /user/end-users/list API.
    /// </summary>
    [Serializable]
    public class EndUsersListResponse
    {
        [JsonProperty("end_users")] public List<EndUserDetails> EndUsers { get; set; }
        [JsonProperty("total_count")] public int TotalCount { get; set; }
        [JsonProperty("next_cursor")] public string NextCursor { get; set; }
        [JsonProperty("has_more")] public bool HasMore { get; set; }

        public static EndUsersListResponse Default()
        {
            return new EndUsersListResponse
            {
                EndUsers = new List<EndUserDetails>(),
                TotalCount = 0,
                NextCursor = null,
                HasMore = false
            };
        }
    }

    /// <summary>
    /// Response model for the /user/end-users/update API.
    /// </summary>
    [Serializable]
    public class EndUserUpdateResponse : EndUserDetails
    {
        public EndUserUpdateResponse(
            string endUserId,
            string lastActiveTs,
            string lastLtmUsageTs,
            Dictionary<string, object> metadata,
            string message = "")
            : base(endUserId, lastActiveTs, lastLtmUsageTs, metadata)
        {
            Message = message;
        }

        [JsonProperty("message")] public string Message { get; set; }
    }

    /// <summary>
    /// Response model for the /user/end-users/delete API.
    /// </summary>
    [Serializable]
    public class EndUserDeleteResponse
    {
        [JsonProperty("message")] public string Message { get; set; } = string.Empty;
        [JsonProperty("end_user_id")] public string EndUserId { get; set; } = string.Empty;
        [JsonProperty("deleted")] public bool Deleted { get; set; }
    }

    /// <summary>
    /// Single memory entry returned by the /memory/* APIs.
    /// </summary>
    [Serializable]
    public class MemoryRecord
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("memory")] public string Memory { get; set; } = string.Empty;
        [JsonProperty("created_at")] public string CreatedAt { get; set; } = string.Empty;
        [JsonProperty("updated_at")] public string UpdatedAt { get; set; } = string.Empty;
        [JsonProperty("metadata")] public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Result item returned when adding a memory.
    /// </summary>
    [Serializable]
    public class MemoryAddResult
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("event")] public string Event { get; set; } = string.Empty;
        [JsonProperty("memory")] public string Memory { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response model for the /memory/add API.
    /// </summary>
    [Serializable]
    public class AddMemoriesResponse
    {
        [JsonProperty("memories")] public List<MemoryAddResult> Memories { get; set; } = new();
    }

    /// <summary>
    /// Response model for the /memory/list API.
    /// </summary>
    [Serializable]
    public class MemoryListResponse
    {
        [JsonProperty("memories")] public List<MemoryRecord> Memories { get; set; } = new();
        [JsonProperty("total_count")] public int TotalCount { get; set; }
        [JsonProperty("page")] public int Page { get; set; }
        [JsonProperty("page_size")] public int PageSize { get; set; }
        [JsonProperty("has_more")] public bool HasMore { get; set; }
    }

    /// <summary>
    /// Response model for the /memory/delete API.
    /// </summary>
    [Serializable]
    public class MemoryDeleteResponse
    {
        [JsonProperty("message")] public string Message { get; set; } = string.Empty;
        [JsonProperty("memory_id")] public string MemoryId { get; set; } = string.Empty;
        [JsonProperty("deleted")] public bool Deleted { get; set; }
    }

    /// <summary>
    /// Response model for the /memory/delete-all API.
    /// </summary>
    [Serializable]
    public class MemoryDeleteAllResponse
    {
        [JsonProperty("message")] public string Message { get; set; } = string.Empty;
        [JsonProperty("character_id")] public string CharacterId { get; set; } = string.Empty;
        [JsonProperty("end_user_id")] public string EndUserId { get; set; } = string.Empty;
    }

    [Serializable]
    public class MemorySettings
    {
        public MemorySettings(bool isEnabled) => IsEnabled = isEnabled;

        public static MemorySettings Default() => new MemorySettings(false);

        [JsonProperty("enabled")] public bool IsEnabled { get; set; }
    }

    [Serializable]
    public class CharacterUpdateResponse
    {
        public CharacterUpdateResponse(string status) => Status = status;

        [JsonProperty("STATUS")] public string Status { get; private set; }
    }

    [Serializable]
    public class ReferralSourceStatus
    {
        [JsonProperty("referral_source_status")]
        public string ReferralSourceStatusProperty { get; set; } = string.Empty;

        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        public static ReferralSourceStatus Default()
        {
            return new ReferralSourceStatus(string.Empty, string.Empty);
        }

        public ReferralSourceStatus(string referralSourceStatusProperty = "", string status = "")
        {
            ReferralSourceStatusProperty = referralSourceStatusProperty;
            Status = status;
        }
    }

    [Serializable]
    public class ServerAnimationListResponse
    {
        public ServerAnimationListResponse(List<ServerAnimationItemResponse> animations, string transactionID, int totalPages, int currentPage, int totalItems)
        {
            Animations = animations;
            TransactionID = transactionID;
            TotalPages = totalPages;
            CurrentPage = currentPage;
            TotalItems = totalItems;
        }

        public static ServerAnimationListResponse Default()
        {
            return new ServerAnimationListResponse(new List<ServerAnimationItemResponse>(), string.Empty, 0, 0, 0);
        }

        [JsonProperty("animations")] public List<ServerAnimationItemResponse> Animations { get; private set; }
        [JsonProperty("transaction_id")] public string TransactionID { get; private set; }
        [JsonProperty("total_pages")] public int TotalPages { get; private set; }
        [JsonProperty("page")] public int CurrentPage { get; private set; }
        [JsonProperty("total")] public int TotalItems { get; private set; }
    }

    [Serializable]
    public class ServerAnimationItemResponse
    {
        public ServerAnimationItemResponse(string animationID, string animationName, string status, string thumbnailURL)
        {
            AnimationID = animationID;
            AnimationName = animationName;
            Status = status;
            ThumbnailURL = thumbnailURL;
        }

        [JsonProperty("animation_id")] public string AnimationID { get; private set; }
        [JsonProperty("animation_name")] public string AnimationName { get; private set; }
        [JsonProperty("status")] public string Status { get; private set; }
        [JsonProperty("thumbnail_gcp_file")] public string ThumbnailURL { get; private set; }
    }

    [Serializable]
    public class Animation
    {
        public Animation(string animationId, string userId, string animationName, string status, string csvGcpFile, string fbxGcpFile, string thumbnailGcpFile, int retryCount, AnimationVideos animationVideos, DateTime createdAt)
        {
            AnimationId = animationId;
            UserId = userId;
            AnimationName = animationName;
            Status = status;
            CsvGcpFile = csvGcpFile;
            FbxGcpFile = fbxGcpFile;
            ThumbnailGcpFile = thumbnailGcpFile;
            RetryCount = retryCount;
            AnimationVideos = animationVideos;
            CreatedAt = createdAt;
        }

        public static Animation Default()
        {
            return new Animation(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, AnimationVideos.Default(), DateTime.MinValue);
        }

        [JsonProperty("animation_id")] public string AnimationId { get; private set; }
        [JsonProperty("user_id")] public string UserId { get; private set; }
        [JsonProperty("animation_name")] public string AnimationName { get; private set; }
        [JsonProperty("status")] public string Status { get; private set; }
        [JsonProperty("csv_gcp_file")] public string CsvGcpFile { get; private set; }
        [JsonProperty("fbx_gcp_file")] public string FbxGcpFile { get; private set; }
        [JsonProperty("thumbnail_gcp_file")] public string ThumbnailGcpFile { get; private set; }
        [JsonProperty("retry_count")] public int RetryCount { get; private set; }
        [JsonProperty("animation_videos")] public AnimationVideos AnimationVideos { get; private set; }
        [JsonProperty("created_at")] public DateTime CreatedAt { get; private set; }
    }

    [Serializable]
    public class AnimationVideos
    {

        public AnimationVideos(string fpvVideo, string tpvVideo)
        {
            FpvVideo = fpvVideo;
            TpvVideo = tpvVideo;
        }

        public static AnimationVideos Default()
        {
            return new AnimationVideos(string.Empty, string.Empty);
        }

        [JsonProperty("fpv_video")] public string FpvVideo { get; private set; }
        [JsonProperty("tpv_video")] public string TpvVideo { get; private set; }
    }

    [Serializable]
    public class ServerAnimationDataResponse
    {
        public ServerAnimationDataResponse(string transactionId, Animation animation, UploadUrls uploadUrls)
        {
            TransactionId = transactionId;
            Animation = animation;
            UploadUrls = uploadUrls;
        }

        public static ServerAnimationDataResponse Default()
        {
            return new ServerAnimationDataResponse(string.Empty, Animation.Default(), UploadUrls.Default());
        }

        [JsonProperty("transaction_id")] public string TransactionId { get; private set; }
        [JsonProperty("animation")] public Animation Animation { get; private set; }
        [JsonProperty("upload_urls")] public UploadUrls UploadUrls { get; private set; }
    }

    [Serializable]
    public class UploadUrls
    {
        public UploadUrls(string fpvVideo, string tpvVideo)
        {
            FpvVideo = fpvVideo;
            TpvVideo = tpvVideo;
        }

        public static UploadUrls Default()
        {
            return new UploadUrls(string.Empty, string.Empty);
        }

        [JsonProperty("fpv_video")] public string FpvVideo { get; private set; }
        [JsonProperty("tpv_video")] public string TpvVideo { get; private set; }
    }

    [Serializable]
    public class RoomDetails
    {
        public RoomDetails(
            string token,
            string roomName,
            string sessionId,
            string roomURL,
            string characterSessionId = "",
            string speakerId = "",
            string transport = "livekit",
            string requestTraceId = "",
            string endUserId = "",
            Dictionary<string, object> endUserMetadata = null)
        {
            Token = token;
            RoomName = roomName;
            SessionId = sessionId;
            RoomURL = roomURL;
            CharacterSessionId = characterSessionId;
            SpeakerId = speakerId;
            Transport = transport;
            RequestTraceId = requestTraceId;
            EndUserId = endUserId;
            EndUserMetadata = endUserMetadata;
        }

        public static RoomDetails Default()
        {
            return new RoomDetails(string.Empty, string.Empty, string.Empty, string.Empty);
        }

        [JsonProperty("token")]
        public string Token { get; private set; }

        [JsonProperty("room_name")]
        public string RoomName { get; private set; }

        [JsonProperty("session_id")]
        public string SessionId { get; private set; }

        /// <summary>
        /// Backend request trace ID for correlating Unity-side connect logs with backend logs.
        /// </summary>
        [JsonProperty("request_trace_id")]
        public string RequestTraceId { get; private set; }

        [JsonProperty("room_url")]
        public string RoomURL { get; private set; }

        [JsonProperty("character_session_id")]
        public string CharacterSessionId { get; private set; }

        /// <summary>
        /// Optional backend-resolved speaker identifier.
        /// This is internal/backend-facing state and may be omitted from current WebRTC connect responses.
        /// </summary>
        [JsonProperty("speaker_id")]
        public string SpeakerId { get; private set; }

        /// <summary>
        /// Backend-resolved or echoed end-user identifier used for player/account scoped memory.
        /// </summary>
        [JsonProperty("end_user_id")]
        public string EndUserId { get; private set; }

        /// <summary>
        /// Backend-resolved or echoed metadata for the active end user.
        /// </summary>
        [JsonProperty("end_user_metadata")]
        public Dictionary<string, object> EndUserMetadata { get; private set; }

        /// <summary>
        /// Transport type (defaults to "livekit" if not provided by server).
        /// This field may not be present in all server responses.
        /// </summary>
        [JsonProperty("transport")]
        public string Transport { get; private set; }
    }
}
