using System.Collections.Generic;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal readonly struct LipSyncRuntimeConfig
    {
        public LipSyncProfileId ProfileId { get; }
        public ConvaiLipSyncMapAsset Mapping { get; }
        public IReadOnlyList<SkinnedMeshRenderer> TargetMeshes { get; }
        public float FadeOutDuration { get; }
        public float SmoothingFactor { get; }
        public float TimeOffsetSeconds { get; }
        public float MaxBufferedSeconds { get; }
        public float MinResumeHeadroomSeconds { get; }
        public bool DeliverChunksAhead { get; }
        public float FadeInDuration { get; }

        public LipSyncRuntimeConfig(
            LipSyncProfileId profileId,
            ConvaiLipSyncMapAsset mapping,
            IReadOnlyList<SkinnedMeshRenderer> targetMeshes,
            float fadeOutDuration,
            float smoothingFactor,
            float timeOffsetSeconds,
            float maxBufferedSeconds,
            float minResumeHeadroomSeconds,
            bool deliverChunksAhead,
            float fadeInDuration = 0.1f)
        {
            ProfileId = profileId;
            Mapping = mapping;
            TargetMeshes = targetMeshes;
            FadeOutDuration = fadeOutDuration;
            SmoothingFactor = smoothingFactor;
            TimeOffsetSeconds = timeOffsetSeconds;
            MaxBufferedSeconds = maxBufferedSeconds;
            MinResumeHeadroomSeconds = minResumeHeadroomSeconds;
            DeliverChunksAhead = deliverChunksAhead;
            FadeInDuration = fadeInDuration;
        }

        public LipSyncEngineConfig ToEngineConfig()
        {
            return new LipSyncEngineConfig(
                FadeOutDuration,
                SmoothingFactor,
                TimeOffsetSeconds,
                MaxBufferedSeconds,
                MinResumeHeadroomSeconds,
                DeliverChunksAhead,
                FadeInDuration);
        }

        public static LipSyncRuntimeConfig CreateNormalized(
            string profileId,
            ConvaiLipSyncMapAsset mapping,
            IReadOnlyList<SkinnedMeshRenderer> targetMeshes,
            float fadeOutDuration,
            float smoothingFactor,
            float timeOffsetSeconds,
            float maxBufferedSeconds,
            float minResumeHeadroomSeconds,
            bool deliverChunksAhead,
            float fadeInDuration)
        {
            LipSyncProfileId requested = new(profileId);
            LipSyncProfileId canonical = LipSyncProfileCatalog.CanonicalizeProfileId(requested);
            return new LipSyncRuntimeConfig(
                canonical,
                mapping,
                targetMeshes,
                fadeOutDuration,
                smoothingFactor,
                timeOffsetSeconds,
                maxBufferedSeconds,
                minResumeHeadroomSeconds,
                deliverChunksAhead,
                fadeInDuration);
        }
    }
}
