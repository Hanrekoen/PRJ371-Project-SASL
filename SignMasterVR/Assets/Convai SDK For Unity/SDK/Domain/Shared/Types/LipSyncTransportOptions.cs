using System;
using System.Collections.Generic;
using Convai.Domain.Models.LipSync;

namespace Convai.Shared.Types
{
    /// <summary>
    ///     Transport-level lip sync options negotiated from character capabilities.
    ///     Playback targets are determined solely by the Lip Sync component's assigned target meshes.
    /// </summary>
    [Serializable]
    public readonly struct LipSyncTransportOptions
    {
        private static readonly IReadOnlyList<string> EmptySourceBlendshapeNames = Array.Empty<string>();

        /// <summary>Server-side frames buffer duration in seconds (e.g. 0.3f). Sent with room connection when lip sync is enabled.</summary>
        public const float DefaultFramesBufferDuration = 0.3f;

        public static readonly LipSyncTransportOptions Disabled = new(
            false,
            string.Empty,
            default,
            string.Empty,
            EmptySourceBlendshapeNames,
            false,
            0,
            0,
            0f,
            false);

        /// <summary>
        ///     Creates an immutable transport contract used by networking and parser layers.
        /// </summary>
        public LipSyncTransportOptions(
            bool enabled,
            string provider,
            LipSyncProfileId profileId,
            string format,
            IReadOnlyList<string> sourceBlendshapeNames,
            bool enableChunking,
            int chunkSize,
            int outputFps,
            float framesBufferDuration,
            bool deliverChunksAhead = false)
        {
            Enabled = enabled;
            Provider = provider ?? string.Empty;
            ProfileId = profileId;
            Format = format ?? string.Empty;
            SourceBlendshapeNames = sourceBlendshapeNames ?? EmptySourceBlendshapeNames;
            EnableChunking = enableChunking;
            ChunkSize = chunkSize;
            OutputFps = outputFps;
            FramesBufferDuration = Math.Max(0f, framesBufferDuration);
            DeliverChunksAhead = deliverChunksAhead;
        }

        /// <summary>Whether lip sync transport is enabled for this character.</summary>
        public bool Enabled { get; }

        /// <summary>Name of transport provider expected by the backend.</summary>
        public string Provider { get; }

        /// <summary>Profile identifier used for channel schema and rig compatibility.</summary>
        public LipSyncProfileId ProfileId { get; }

        /// <summary>Backend format hint associated with the active profile.</summary>
        public string Format { get; }

        /// <summary>Ordered source channel layout expected by parser and playback runtime.</summary>
        public IReadOnlyList<string> SourceBlendshapeNames { get; }

        /// <summary>Whether server emits chunked payloads.</summary>
        public bool EnableChunking { get; }

        /// <summary>Expected frames per chunk when chunking is enabled.</summary>
        public int ChunkSize { get; }

        /// <summary>Expected output frame rate for incoming payload timing.</summary>
        public int OutputFps { get; }

        /// <summary>Server-side frame buffer duration in seconds.</summary>
        public float FramesBufferDuration { get; }

        /// <summary>Whether the backend may send indexed NeuroSync chunks ahead of audio playback.</summary>
        public bool DeliverChunksAhead { get; }

        /// <summary>
        ///     Validates the minimum required fields for an enabled transport contract.
        /// </summary>
        public bool IsValid =>
            Enabled &&
            !string.IsNullOrWhiteSpace(Provider) &&
            ProfileId.IsValid &&
            !string.IsNullOrWhiteSpace(Format) &&
            SourceBlendshapeNames != null &&
            SourceBlendshapeNames.Count > 0 &&
            ChunkSize > 0 &&
            OutputFps > 0;
    }
}
