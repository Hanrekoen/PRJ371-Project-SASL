using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Convai.Editor.AI
{
    internal enum ConvaiDiagnosticSeverity
    {
        Error,
        Warning,
        Info
    }

    internal sealed class ConvaiDiagnosticIssue
    {
        public string Code { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
        public string Evidence { get; set; }
        public long AffectedInstanceId { get; set; }
        public bool AutoFixable { get; set; }
        public string SuggestedTool { get; set; }
        public object SuggestedArguments { get; set; }
    }

    internal sealed class ConvaiConversationDiagnosis
    {
        public bool Success { get; set; } = true;
        public string FailureCode { get; set; } = string.Empty;
        public string FailureMessage { get; set; } = string.Empty;
        public bool ReadyToRun { get; set; }
        public string Mode { get; set; }
        public List<ConvaiDiagnosticIssue> Issues { get; } = new();
        public object Configuration { get; set; }
        public object Runtime { get; set; }
    }

    internal static class ConvaiConversationHealthAnalyzer
    {
        public static ConvaiConversationDiagnosis Analyze(ConvaiDiagnoseConversationRequest request)
        {
            request ??= new ConvaiDiagnoseConversationRequest();
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return new ConvaiConversationDiagnosis
                {
                    Success = false,
                    FailureCode = "NO_ACTIVE_SCENE",
                    FailureMessage = "No loaded active scene is available."
                };
            }

            ConvaiManager[] managers = ConvaiConversationAuthoringService.GetSceneComponents<ConvaiManager>(
                scene, request.IncludeInactive);
            ConvaiRoomManager[] rooms = ConvaiConversationAuthoringService.GetSceneComponents<ConvaiRoomManager>(
                scene, request.IncludeInactive);
            ConvaiPlayer[] players = ConvaiConversationAuthoringService.GetSceneComponents<ConvaiPlayer>(
                scene, request.IncludeInactive);
            ConvaiCharacter[] allCharacters = ConvaiConversationAuthoringService.GetSceneComponents<ConvaiCharacter>(
                scene, request.IncludeInactive);
            ConvaiCharacter[] characters = allCharacters;
            if (request.CharacterInstanceId != 0)
            {
                if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, request.IncludeInactive,
                        out ConvaiCharacter focused, out string error))
                {
                    return new ConvaiConversationDiagnosis
                    {
                        Success = false,
                        FailureCode = ConvaiMcpResolvers.CharacterErrorCode,
                        FailureMessage = error
                    };
                }

                characters = new[] { focused };
            }

            var diagnosis = new ConvaiConversationDiagnosis
            {
                Mode = EditorApplication.isPlaying ? "PlayMode" : "EditMode"
            };
            ConvaiRoomManager room = rooms.Length == 1 ? rooms[0] : null;
            ConvaiManager manager = managers.Length == 1 ? managers[0] : null;

            if (EditorApplication.isPlaying && room != null && !string.IsNullOrWhiteSpace(room.LastSessionErrorCode))
            {
                Add(diagnosis, "SESSION_ERROR", ConvaiDiagnosticSeverity.Error,
                    "The latest Convai session failed.",
                    $"{room.LastSessionErrorCode}: {room.LastSessionErrorMessage}", room.gameObject,
                    false, "Unity.ReadConsole", new { types = new[] { "Error", "Warning" } });
            }

            ConvaiSettings settings = ConvaiSettings.Instance;
            bool credentialsConfigured = settings != null && settings.HasApiKey;
            if (!credentialsConfigured)
            {
                Add(diagnosis, "PROJECT_API_KEY_MISSING", ConvaiDiagnosticSeverity.Error,
                    "Convai credentials are not configured.",
                    "ConvaiSettings.HasApiKey is false. The key value was not read or returned.", null,
                    false, string.Empty, new { manual = "Edit > Project Settings > Convai SDK" });
            }

            if (managers.Length == 0)
                Add(diagnosis, "MANAGER_MISSING", ConvaiDiagnosticSeverity.Error,
                    "Active scene has no ConvaiManager.", "Manager count is 0.", null, true,
                    "Convai.SetupConversationScene", new { dryRun = true });
            else if (managers.Length > 1)
                Add(diagnosis, "MANAGER_DUPLICATE", ConvaiDiagnosticSeverity.Error,
                    "Active scene has multiple ConvaiManager components.", $"Manager count is {managers.Length}.",
                    null, false, "Convai.InspectScene", new { includeInactive = true });

            if (rooms.Length == 0)
                Add(diagnosis, "ROOM_MISSING", ConvaiDiagnosticSeverity.Error,
                    "Active scene has no ConvaiRoomManager.", "Room manager count is 0.", null, true,
                    "Convai.ConfigureRoom", new { targetInstanceId = manager != null ? Id(manager.gameObject) : 0, dryRun = true });
            else if (rooms.Length > 1)
                Add(diagnosis, "ROOM_DUPLICATE", ConvaiDiagnosticSeverity.Error,
                    "Active scene has multiple ConvaiRoomManager components.", $"Room manager count is {rooms.Length}.",
                    null, false, "Convai.InspectScene", new { includeInactive = true });

            if (players.Length == 0)
                Add(diagnosis, "PLAYER_MISSING", ConvaiDiagnosticSeverity.Error,
                    "Active scene has no ConvaiPlayer.", "Player count is 0.", null, true,
                    "Convai.SetupConversationScene", new { dryRun = true });
            else if (players.Length > 1 && (manager == null || ReadReference(manager, "_explicitPlayer") == null))
                Add(diagnosis, "PLAYER_AMBIGUOUS", ConvaiDiagnosticSeverity.Error,
                    "Multiple players exist and the manager has no explicit player binding.",
                    $"Player count is {players.Length}.", manager != null ? manager.gameObject : null, false,
                    "Convai.ConfigurePlayer", new { managerInstanceId = manager != null ? Id(manager.gameObject) : 0, dryRun = true });

            Object explicitPlayer = manager != null ? ReadReference(manager, "_explicitPlayer") : null;
            if (manager != null && players.Length == 1 && explicitPlayer != players[0])
                Add(diagnosis, "PLAYER_OWNERSHIP_MISSING", ConvaiDiagnosticSeverity.Error,
                    "ConvaiManager does not explicitly own the scene player.",
                    $"Expected explicit player instance {Id(players[0].gameObject)}.", manager.gameObject, true,
                    "Convai.ConfigurePlayer", new
                    {
                        targetInstanceId = Id(players[0].gameObject),
                        managerInstanceId = Id(manager.gameObject),
                        dryRun = true
                    });

            if (allCharacters.Length == 0)
                Add(diagnosis, "CHARACTER_MISSING", ConvaiDiagnosticSeverity.Error,
                    "Active scene has no ConvaiCharacter.", "Character count is 0.", null, true,
                    "Convai.SetupConversationScene", new { dryRun = true });

            Object[] explicitCharacters = manager != null
                ? ReadReferences(manager, "_explicitCharacters")
                : Array.Empty<Object>();
            foreach (ConvaiCharacter character in allCharacters)
            {
                if (manager != null && !explicitCharacters.Contains(character))
                    Add(diagnosis, "CHARACTER_OWNERSHIP_MISSING", ConvaiDiagnosticSeverity.Error,
                        $"ConvaiManager does not explicitly own character '{character.gameObject.name}'.",
                        $"Character instance {Id(character.gameObject)} is absent from _explicitCharacters.",
                        character.gameObject, true, "Convai.ConfigureCharacter", new
                        {
                            targetInstanceId = Id(character.gameObject),
                            managerInstanceId = Id(manager.gameObject),
                            dryRun = true
                        });
            }

            Object explicitConversationTarget = manager != null
                ? ReadReference(manager, "_explicitConversationTarget")
                : null;
            if (manager != null && allCharacters.Length > 0 && explicitConversationTarget == null)
                Add(diagnosis, "CONVERSATION_TARGET_MISSING", ConvaiDiagnosticSeverity.Error,
                    "ConvaiManager has no explicit conversation target.",
                    "_explicitConversationTarget is empty.", manager.gameObject, true,
                    "Convai.ConfigureCharacter", new
                    {
                        targetInstanceId = Id(allCharacters[0].gameObject),
                        managerInstanceId = Id(manager.gameObject),
                        dryRun = true
                    });
            else if (explicitConversationTarget != null &&
                     (explicitConversationTarget is not ConvaiCharacter explicitTargetCharacter ||
                      !allCharacters.Contains(explicitTargetCharacter)))
                Add(diagnosis, "CONVERSATION_TARGET_INVALID", ConvaiDiagnosticSeverity.Error,
                    "ConvaiManager conversation target is not a character in the active scene.",
                    $"Explicit target instance is {Id(explicitConversationTarget)}.", manager.gameObject, false,
                    "Convai.InspectScene", new { includeInactive = true });

            foreach (ConvaiCharacter character in characters)
            {
                if (string.IsNullOrWhiteSpace(character.CharacterId))
                    Add(diagnosis, "CHARACTER_ID_MISSING", ConvaiDiagnosticSeverity.Error,
                        $"Character '{character.gameObject.name}' has no Character ID.",
                        "Effective CharacterId is empty.", character.gameObject, true,
                        "Convai.ConfigureCharacter", new { targetInstanceId = Id(character.gameObject), dryRun = true });
                else if (!ConvaiConversationAuthoringService.IsValidCharacterId(character.CharacterId))
                    Add(diagnosis, "CHARACTER_ID_INVALID", ConvaiDiagnosticSeverity.Error,
                        $"Character '{character.gameObject.name}' has an invalid Character ID.",
                        character.CharacterId, character.gameObject, true,
                        "Convai.ConfigureCharacter", new { targetInstanceId = Id(character.gameObject), dryRun = true });

                if (character.GetComponent<ConvaiAudioOutput>() == null)
                    Add(diagnosis, "CHARACTER_AUDIO_OUTPUT_MISSING", ConvaiDiagnosticSeverity.Error,
                        $"Character '{character.gameObject.name}' cannot play remote audio through the recommended output component.",
                        "ConvaiAudioOutput is missing.", character.gameObject, true,
                        "Convai.ConfigureCharacter",
                        new { targetInstanceId = Id(character.gameObject), addAudioOutput = true, dryRun = true });
                if (character.GetComponent<AudioSource>() == null)
                    Add(diagnosis, "CHARACTER_AUDIO_SOURCE_MISSING", ConvaiDiagnosticSeverity.Error,
                        $"Character '{character.gameObject.name}' has no AudioSource.",
                        "AudioSource is missing.", character.gameObject, true,
                        "Convai.ConfigureCharacter",
                        new { targetInstanceId = Id(character.gameObject), addAudioOutput = true, dryRun = true });
            }

            foreach (IGrouping<string, ConvaiCharacter> duplicate in allCharacters
                         .Where(character => !string.IsNullOrWhiteSpace(character.CharacterId))
                         .GroupBy(character => character.CharacterId, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                Add(diagnosis, "CHARACTER_ID_DUPLICATE", ConvaiDiagnosticSeverity.Error,
                    "Multiple scene characters use the same Character ID.",
                    $"{duplicate.Key} is assigned to {string.Join(", ", duplicate.Select(item => item.gameObject.name))}.",
                    null, false, "Convai.InspectScene", new { includeInactive = true });
            }

            if (room != null && room.EffectiveConnectionType == ConvaiConnectionType.Video &&
                room.EffectiveVisionContextEnabled && !ConvaiSceneQueries.HasCompleteVisionPipeline(room.gameObject))
            {
                Add(diagnosis, "VIDEO_PIPELINE_INCOMPLETE", ConvaiDiagnosticSeverity.Error,
                    "Video room is missing its dynamic-vision publisher or frame source.",
                    "Expected IVisionPublisher and IVisionFrameSource under ConvaiRoomManager.", room.gameObject,
                    false, "Convai.GetGuidance", new { topic = "Vision" });
            }

            if (EditorApplication.isPlaying && room != null && room.CurrentState.ToString() == "Disconnected" &&
                string.IsNullOrWhiteSpace(room.LastSessionErrorCode))
            {
                Add(diagnosis, "SESSION_DISCONNECTED", ConvaiDiagnosticSeverity.Warning,
                    "Conversation room is currently disconnected.",
                    $"connectOnStart={room.EffectiveConnectOnStart}, attempts={room.ConnectAttemptCount}.",
                    room.gameObject, false, "Unity.ReadConsole", new { types = new[] { "Error", "Warning" } });
            }

            diagnosis.ReadyToRun = diagnosis.Issues.All(issue => issue.Severity != ConvaiDiagnosticSeverity.Error.ToString());
            diagnosis.Configuration = new
            {
                credentialsConfigured,
                activeScene = scene.name,
                managerCount = managers.Length,
                roomCount = rooms.Length,
                playerCount = players.Length,
                characterCount = allCharacters.Length,
                managerInstanceId = manager != null ? Id(manager.gameObject) : 0,
                roomInstanceId = room != null ? Id(room.gameObject) : 0,
                effectiveConnectionType = room?.EffectiveConnectionType.ToString() ?? string.Empty,
                effectiveInputMode = room?.EffectiveTurnTakingOptions.Mode.ToString() ?? string.Empty,
                connectOnStart = room?.EffectiveConnectOnStart ?? false,
                serverEndpoint = room?.EffectiveServerEndpoint.ToString() ?? string.Empty,
                visionMode = room?.EffectiveVisionContextMode.ToString() ?? string.Empty,
                visionContextEnabled = room?.EffectiveVisionContextEnabled ?? false,
                pushToTalkKey = room?.PushToTalkKey.ToString() ?? string.Empty,
                explicitPlayerInstanceId = SceneObjectId(explicitPlayer),
                explicitCharacterInstanceIds = explicitCharacters.Select(SceneObjectId).ToArray(),
                explicitConversationTargetInstanceId = SceneObjectId(explicitConversationTarget),
                player = players.Length == 1
                    ? new { instanceId = Id(players[0].gameObject), players[0].PlayerName, players[0].PlayerId }
                    : null,
                characters = characters.Select(character => new
                {
                    instanceId = Id(character.gameObject),
                    character.CharacterId,
                    character.CharacterName,
                    hasAudioOutput = character.GetComponent<ConvaiAudioOutput>() != null,
                    hasAudioSource = character.GetComponent<AudioSource>() != null
                }).ToArray()
            };
            diagnosis.Runtime = new
            {
                managerInitialized = manager?.IsInitialized ?? false,
                managerConnected = manager?.IsConnected ?? false,
                sessionState = room?.CurrentState.ToString() ?? string.Empty,
                roomConnected = room?.IsConnected ?? false,
                roomName = room?.CurrentRoomName ?? string.Empty,
                sessionId = room?.CurrentSessionId ?? string.Empty,
                characterSessionId = room?.CurrentCharacterSessionId ?? string.Empty,
                micMuted = room?.IsMicMuted ?? false,
                requiresUserGestureForAudio = room?.RequiresUserGestureForAudio ?? false,
                connectAttemptCount = room?.ConnectAttemptCount ?? 0,
                reconnectCount = room?.ReconnectCount ?? 0,
                lastSessionErrorCode = room?.LastSessionErrorCode ?? string.Empty,
                lastSessionErrorMessage = room?.LastSessionErrorMessage ?? string.Empty,
                characters = characters.Select(character => new
                {
                    instanceId = Id(character.gameObject),
                    sessionState = character.SessionState.ToString(),
                    character.IsCharacterReady,
                    character.IsInConversation,
                    character.IsSpeaking
                }).ToArray()
            };
            return diagnosis;
        }

        private static void Add(ConvaiConversationDiagnosis diagnosis, string code,
            ConvaiDiagnosticSeverity severity, string message, string evidence, GameObject affected,
            bool autoFixable, string suggestedTool, object suggestedArguments)
        {
            diagnosis.Issues.Add(new ConvaiDiagnosticIssue
            {
                Code = code,
                Severity = severity.ToString(),
                Message = message,
                Evidence = evidence,
                AffectedInstanceId = Id(affected),
                AutoFixable = autoFixable,
                SuggestedTool = suggestedTool,
                SuggestedArguments = suggestedArguments
            });
        }

        private static long Id(Object value) => ConvaiConversationAuthoringService.EntityIdOf(value);

        private static long SceneObjectId(Object value) =>
            Id(value is Component component ? component.gameObject : value);

        private static Object ReadReference(ConvaiManager manager, string propertyName)
        {
            var serialized = new SerializedObject(manager);
            return serialized.FindProperty(propertyName)?.objectReferenceValue;
        }

        private static Object[] ReadReferences(ConvaiManager manager, string propertyName)
        {
            var serialized = new SerializedObject(manager);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray) return Array.Empty<Object>();
            var values = new Object[property.arraySize];
            for (int i = 0; i < property.arraySize; i++)
                values[i] = property.GetArrayElementAtIndex(i).objectReferenceValue;
            return values;
        }

    }
}
