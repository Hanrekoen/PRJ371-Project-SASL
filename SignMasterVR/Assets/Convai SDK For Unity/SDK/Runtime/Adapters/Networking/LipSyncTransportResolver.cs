using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Logging;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Resolves lip sync transport options from character agents.
    /// </summary>
    internal static class LipSyncTransportResolver
    {
        public static LipSyncTransportOptions Resolve(
            IReadOnlyList<IConvaiCharacterAgent> characterList,
            IEventHub eventHub = null)
        {
            if (characterList == null || characterList.Count == 0)
            {
                ConvaiLogger.Debug(
                    "No characters available for lip sync capability discovery. Lip sync transport remains disabled.",
                    LogCategory.LipSync);
                return LipSyncTransportOptions.Disabled;
            }

            var resolvedOptions = new List<LipSyncTransportOptions>(characterList.Count);
            var resolvedSources = new List<string>(characterList.Count);

            foreach (IConvaiCharacterAgent characterAgent in characterList)
            {
                if (characterAgent is not MonoBehaviour characterMono) continue;

                ILipSyncCapabilitySource source = characterMono.GetComponent<ILipSyncCapabilitySource>() ??
                                                  characterMono.GetComponentInChildren<ILipSyncCapabilitySource>(true);

                if (source == null) continue;

                if (!source.TryGetLipSyncTransportOptions(out LipSyncTransportOptions options) ||
                    !options.IsValid) continue;

                resolvedOptions.Add(options);
                resolvedSources.Add(characterMono.name);
            }

            if (resolvedOptions.Count == 0)
            {
                ConvaiLogger.Debug(
                    "No valid lip sync capability source found. Lip sync transport remains disabled.",
                    LogCategory.LipSync);
                return LipSyncTransportOptions.Disabled;
            }

            var uniqueOptions = new Dictionary<string, LipSyncTransportOptions>(StringComparer.Ordinal);
            for (int i = 0; i < resolvedOptions.Count; i++)
            {
                LipSyncTransportOptions option = resolvedOptions[i];
                uniqueOptions[BuildLipSyncContractKey(option)] = option;
            }

            if (uniqueOptions.Count > 1)
            {
                string joinedSources = string.Join(", ", resolvedSources);
                ConvaiLogger.Error(
                    $"Conflicting lip sync contracts detected across characters ({joinedSources}). Lip sync transport has been disabled for this room.",
                    LogCategory.LipSync);
                eventHub?.Publish(SessionError.Create(
                    "lipsync.contract_conflict",
                    "Multiple characters advertise conflicting lip sync transport contracts in a single room."));
                return LipSyncTransportOptions.Disabled;
            }

            LipSyncTransportOptions selected = default;
            foreach (KeyValuePair<string, LipSyncTransportOptions> pair in uniqueOptions)
            {
                selected = pair.Value;
                break;
            }

            ConvaiLogger.Info(
                $"Lip sync transport resolved: provider={selected.Provider}, format={selected.Format}, profileId={selected.ProfileId}, fps={selected.OutputFps}, chunk={selected.ChunkSize}, sourceCount={selected.SourceBlendshapeNames.Count}",
                LogCategory.LipSync);
            return selected;
        }

        private static string BuildLipSyncContractKey(LipSyncTransportOptions options)
        {
            string sourceKey;
            if (options.SourceBlendshapeNames == null || options.SourceBlendshapeNames.Count == 0)
                sourceKey = string.Empty;
            else
            {
                // Normalize blendshape names so contract comparison is order-insensitive.
                string[] namesArray = new string[options.SourceBlendshapeNames.Count];
                for (int i = 0; i < options.SourceBlendshapeNames.Count; i++)
                    namesArray[i] = options.SourceBlendshapeNames[i];
                Array.Sort(namesArray, StringComparer.Ordinal);
                sourceKey = string.Join("|", namesArray);
            }

            return string.Concat(
                options.Provider, "::",
                options.Format, "::",
                options.ProfileId.Value, "::",
                options.EnableChunking ? "1" : "0", "::",
                options.ChunkSize.ToString(), "::",
                options.OutputFps.ToString(), "::",
                options.DeliverChunksAhead ? "ahead" : "paced", "::",
                sourceKey);
        }
    }
}
