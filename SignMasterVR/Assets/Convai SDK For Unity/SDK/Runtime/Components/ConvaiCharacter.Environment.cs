using System;
using System.Collections.Generic;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Room;
using Convai.Runtime.SceneMetadata;
using Convai.Shared.Actions;
using UnityEngine;
using SceneMetadataMessage = Convai.Infrastructure.Protocol.Messages.SceneMetadata;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter
    {
        private readonly HashSet<string> _connectTimeEnvironmentNames = new(StringComparer.OrdinalIgnoreCase);
        private bool _hasPendingSceneMetadataSync;

        internal void MarkPendingSceneMetadataSync()
        {
            _hasPendingSceneMetadataSync = true;
            ScheduleDynamicContextFlush();
        }

        internal void SeedWorldObjectTrackedState(ConvaiObjectMetadata metadata)
        {
            if (metadata == null) return;
            metadata.SeedTrackedStateOntoCharacter(this);
        }

        private void SeedAllWorldObjectTrackedState()
        {
            ConvaiObjectMetadata[] objects = ConvaiMetadataRegistry.GetAllMetadata();
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                    objects[i].SeedTrackedStateOntoCharacter(this);
            }
        }

        internal static void NotifyAllCharactersSceneMetadataDirty(IAgentRegistry agentRegistry)
        {
            if (agentRegistry?.Characters == null) return;

            IReadOnlyList<IConvaiCharacterAgent> characters = agentRegistry.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] is ConvaiCharacter character)
                    character.MarkPendingSceneMetadataSync();
            }
        }

        private void CaptureEnvironmentSnapshotAtConnect()
        {
            _connectTimeEnvironmentNames.Clear();

            ConvaiActionConfig actionConfig = GetRuntimeActionConfig();
            if (actionConfig == null) return;

            AddEnvironmentSnapshotNames(actionConfig.Objects);
            AddEnvironmentSnapshotNames(actionConfig.Characters);
        }

        private void ClearEnvironmentSnapshotAtConnect()
        {
            _connectTimeEnvironmentNames.Clear();
            _hasPendingSceneMetadataSync = false;
        }

        private void AddEnvironmentSnapshotNames<T>(IReadOnlyList<T> entries) where T : class
        {
            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                string name = entries[i] switch
                {
                    ConvaiActionObjectDefinition actionObject => actionObject?.Name,
                    ConvaiActionCharacterDefinition actionCharacter => actionCharacter?.Name,
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(name))
                    _connectTimeEnvironmentNames.Add(name.Trim());
            }
        }

        private void FlushPendingSceneMetadata()
        {
            if (!_hasPendingSceneMetadataSync || ConnectionService is not IConvaiRoomConnectionService roomService)
                return;

            if (!IsInConversation || !roomService.IsConnected)
                return;

            List<SceneMetadataMessage> payload = BuildPostConnectSceneMetadataPayload();
            _hasPendingSceneMetadataSync = false;

            if (payload.Count == 0) return;

            if (!roomService.UpdateSceneMetadata(payload))
            {
                _hasPendingSceneMetadataSync = true;
                Logger?.Warning(
                    $"[{_characterName}] Failed to send post-connect scene metadata update");
            }
        }

        private List<SceneMetadataMessage> BuildPostConnectSceneMetadataPayload()
        {
            List<SceneMetadataMessage> allMetadata = ConvaiMetadataRegistry.GetSceneMetadataList();
            if (allMetadata.Count == 0) return allMetadata;

            if (_connectTimeEnvironmentNames.Count == 0)
                return allMetadata;

            var payload = new List<SceneMetadataMessage>(allMetadata.Count);
            for (int i = 0; i < allMetadata.Count; i++)
            {
                SceneMetadataMessage entry = allMetadata[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) continue;
                if (_connectTimeEnvironmentNames.Contains(entry.Name)) continue;

                payload.Add(entry);
            }

            return payload;
        }
    }
}
