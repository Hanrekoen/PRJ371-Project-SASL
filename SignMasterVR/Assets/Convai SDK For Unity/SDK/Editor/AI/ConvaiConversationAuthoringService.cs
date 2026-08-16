using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Context;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Convai.Editor.AI
{
    internal sealed class ConvaiAuthoringResult
    {
        public bool Success { get; set; } = true;
        public string FailureCode { get; set; } = string.Empty;
        public string FailureMessage { get; set; } = string.Empty;
        public bool Complete { get; set; }
        public bool DryRun { get; set; }
        public long ManagerInstanceId { get; set; }
        public long RoomInstanceId { get; set; }
        public long PlayerInstanceId { get; set; }
        public long CharacterInstanceId { get; set; }
        public List<string> Changes { get; } = new();
        public List<string> BlockedSteps { get; } = new();
        public List<string> RequiredInputs { get; } = new();
        public List<string> Warnings { get; } = new();

        public static ConvaiAuthoringResult Failure(string code, string message, bool dryRun) => new()
        {
            Success = false,
            FailureCode = code,
            FailureMessage = message,
            DryRun = dryRun
        };
    }

    internal static class ConvaiConversationAuthoringService
    {
        private const string ManagerName = "[Convai Manager]";
        private const string PlayerName = "Convai Player";
        private const string CharacterName = "Convai Character";

        public static ConvaiAuthoringResult ConfigureRoom(ConvaiConfigureRoomRequest request)
        {
            request ??= new ConvaiConfigureRoomRequest();
            if (!CanMutate(out ConvaiAuthoringResult playModeFailure, request.DryRun)) return playModeFailure;
            if (!TryResolveGameObject(request.TargetInstanceId, out GameObject target, out string error))
                return ConvaiAuthoringResult.Failure("TARGET_NOT_FOUND", error, request.DryRun);
            if (!TryResolveRoomProfile(request.ConfigurationMode, request.ProfileAssetPath,
                    out ConvaiRoomManagerProfile profile, out error))
                return ConvaiAuthoringResult.Failure("INVALID_PROFILE_ASSET", error, request.DryRun);
            if (request.ConfigurationMode == ConvaiToolConfigurationMode.Inline &&
                !Enum.TryParse<KeyCode>(request.PushToTalkKey, true, out _))
                return ConvaiAuthoringResult.Failure(
                    "INVALID_PUSH_TO_TALK_KEY",
                    $"'{request.PushToTalkKey}' is not a Unity KeyCode.",
                    request.DryRun);
            if (RequiresDynamicVision(request, profile) && !ConvaiSceneQueries.HasCompleteVisionPipeline(target))
            {
                return ConvaiAuthoringResult.Failure(
                    "VISION_COMPONENTS_REQUIRED",
                    "Video with dynamic vision requires an IVisionPublisher and IVisionFrameSource below the room. Use Disabled for native video or configure vision first.",
                    request.DryRun);
            }

            var result = new ConvaiAuthoringResult { DryRun = request.DryRun };
            PreviewRoom(target, request, profile, result.Changes);
            if (!request.DryRun && result.Changes.Count > 0)
            {
                if (!ApplyUndoGroup("Configure Convai Room", () =>
                    {
                        ApplyRoom(target, request, profile);
                        MarkSceneDirty(target.scene);
                    }, out error))
                    return ConvaiAuthoringResult.Failure("MUTATION_FAILED", error, false);
            }

            ConvaiManager manager = target.GetComponent<ConvaiManager>();
            ConvaiRoomManager room = target.GetComponent<ConvaiRoomManager>();
            result.ManagerInstanceId = EntityIdOf(target);
            result.RoomInstanceId = EntityIdOf(target);
            result.Complete = request.DryRun || manager != null && room != null;
            return result;
        }

        public static ConvaiAuthoringResult ConfigurePlayer(ConvaiConfigurePlayerRequest request)
        {
            request ??= new ConvaiConfigurePlayerRequest();
            if (!CanMutate(out ConvaiAuthoringResult playModeFailure, request.DryRun)) return playModeFailure;
            if (!TryResolveGameObject(request.TargetInstanceId, out GameObject target, out string error))
                return ConvaiAuthoringResult.Failure("TARGET_NOT_FOUND", error, request.DryRun);
            if (!TryResolveManager(target.scene, request.ManagerInstanceId, out ConvaiManager manager,
                    out string managerWarning, out error))
                return ConvaiAuthoringResult.Failure("INVALID_MANAGER_TARGET", error, request.DryRun);

            var result = new ConvaiAuthoringResult { DryRun = request.DryRun };
            PreviewPlayer(target, request, manager, result.Changes);
            if (!string.IsNullOrEmpty(managerWarning)) result.Warnings.Add(managerWarning);
            if (!request.DryRun && result.Changes.Count > 0)
            {
                if (!ApplyUndoGroup("Configure Convai Player", () =>
                    {
                        ConvaiPlayer player = ApplyPlayer(target, request.PlayerName, request.PlayerId);
                        if (manager != null) BindPlayer(manager, player);
                        MarkSceneDirty(target.scene);
                    }, out error))
                    return ConvaiAuthoringResult.Failure("MUTATION_FAILED", error, false);
            }

            ConvaiPlayer configured = target.GetComponent<ConvaiPlayer>();
            result.PlayerInstanceId = EntityIdOf(target);
            result.ManagerInstanceId = manager != null ? EntityIdOf(manager.gameObject) : 0;
            result.Complete = manager != null && (request.DryRun || configured != null);
            if (manager == null) result.BlockedSteps.Add("Bind player to an explicit ConvaiManager.");
            return result;
        }

        public static ConvaiAuthoringResult ConfigureCharacter(ConvaiConfigureCharacterRequest request)
        {
            request ??= new ConvaiConfigureCharacterRequest();
            if (!CanMutate(out ConvaiAuthoringResult playModeFailure, request.DryRun)) return playModeFailure;
            if (!TryResolveGameObject(request.TargetInstanceId, out GameObject target, out string error))
                return ConvaiAuthoringResult.Failure("TARGET_NOT_FOUND", error, request.DryRun);
            if (!TryResolveCharacterProfile(request.ConfigurationMode, request.ProfileAssetPath,
                    out ConvaiCharacterProfile profile, out error))
                return ConvaiAuthoringResult.Failure("INVALID_PROFILE_ASSET", error, request.DryRun);
            string effectiveCharacterId = profile != null ? profile.CharacterId : request.CharacterId;
            if (!ValidateCharacterId(effectiveCharacterId, out error))
                return ConvaiAuthoringResult.Failure("INVALID_CHARACTER_ID", error, request.DryRun);
            if (!TryResolveManager(target.scene, request.ManagerInstanceId, out ConvaiManager manager,
                    out string managerWarning, out error))
                return ConvaiAuthoringResult.Failure("INVALID_MANAGER_TARGET", error, request.DryRun);

            var result = new ConvaiAuthoringResult { DryRun = request.DryRun };
            PreviewCharacter(target, request, profile, manager, result.Changes);
            if (!string.IsNullOrEmpty(managerWarning)) result.Warnings.Add(managerWarning);
            if (!request.DryRun && result.Changes.Count > 0)
            {
                if (!ApplyUndoGroup("Configure Convai Character", () =>
                    {
                        ConvaiCharacter character = ApplyCharacter(target, request, profile);
                        if (manager != null) BindCharacter(manager, character);
                        MarkSceneDirty(target.scene);
                    }, out error))
                    return ConvaiAuthoringResult.Failure("MUTATION_FAILED", error, false);
            }

            ConvaiCharacter configured = target.GetComponent<ConvaiCharacter>();
            effectiveCharacterId = profile != null ? profile.CharacterId : request.CharacterId?.Trim();
            result.CharacterInstanceId = EntityIdOf(target);
            result.ManagerInstanceId = manager != null ? EntityIdOf(manager.gameObject) : 0;
            if (string.IsNullOrWhiteSpace(effectiveCharacterId))
                result.RequiredInputs.Add("characterId");
            if (manager == null) result.BlockedSteps.Add("Bind character to an explicit ConvaiManager.");
            result.Complete = manager != null && !string.IsNullOrWhiteSpace(effectiveCharacterId) &&
                              (request.DryRun || configured != null &&
                                  (!request.AddAudioOutput || target.GetComponent<ConvaiAudioOutput>() != null));
            return result;
        }

        public static ConvaiAuthoringResult SetupConversationScene(ConvaiSetupConversationSceneRequest request)
        {
            request ??= new ConvaiSetupConversationSceneRequest();
            if (!CanMutate(out ConvaiAuthoringResult playModeFailure, request.DryRun)) return playModeFailure;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return ConvaiAuthoringResult.Failure("NO_ACTIVE_SCENE", "No loaded active scene is available.", request.DryRun);
            string error;

            ConvaiRoomManagerProfile roomProfile = null;
            if (!string.IsNullOrWhiteSpace(request.RoomProfileAssetPath) &&
                !TryResolveRoomProfile(ConvaiToolConfigurationMode.ExistingProfile, request.RoomProfileAssetPath,
                    out roomProfile, out error))
                return ConvaiAuthoringResult.Failure("INVALID_PROFILE_ASSET", error, request.DryRun);
            ConvaiCharacterProfile characterProfile = null;
            if (!string.IsNullOrWhiteSpace(request.CharacterProfileAssetPath) &&
                !TryResolveCharacterProfile(ConvaiToolConfigurationMode.ExistingProfile,
                    request.CharacterProfileAssetPath, out characterProfile, out error))
                return ConvaiAuthoringResult.Failure("INVALID_PROFILE_ASSET", error, request.DryRun);
            string requestedCharacterId = characterProfile != null ? characterProfile.CharacterId : request.CharacterId;
            if (!ValidateCharacterId(requestedCharacterId, out error))
                return ConvaiAuthoringResult.Failure("INVALID_CHARACTER_ID", error, request.DryRun);

            var result = new ConvaiAuthoringResult { DryRun = request.DryRun };
            GameObject managerTarget = ResolveSetupTarget<ConvaiManager>(
                scene, request.ManagerInstanceId, ManagerName, request.CreatePlaceholders, result, "manager", out error);
            if (error != null) return ConvaiAuthoringResult.Failure("INVALID_MANAGER_TARGET", error, request.DryRun);
            GameObject playerTarget = ResolveSetupTarget<ConvaiPlayer>(
                scene, request.PlayerInstanceId, PlayerName, request.CreatePlaceholders, result, "player", out error);
            if (error != null) return ConvaiAuthoringResult.Failure("INVALID_PLAYER_TARGET", error, request.DryRun);
            GameObject characterTarget = ResolveSetupTarget<ConvaiCharacter>(
                scene, request.CharacterInstanceId, CharacterName, request.CreatePlaceholders, result, "character", out error);
            if (error != null) return ConvaiAuthoringResult.Failure("INVALID_CHARACTER_TARGET", error, request.DryRun);
            if (roomProfile != null && RequiresDynamicVision(null, roomProfile) &&
                (managerTarget == null || !ConvaiSceneQueries.HasCompleteVisionPipeline(managerTarget)))
            {
                return ConvaiAuthoringResult.Failure(
                    "VISION_COMPONENTS_REQUIRED",
                    "The selected room profile enables dynamic vision but the room has no IVisionPublisher and IVisionFrameSource pipeline.",
                    request.DryRun);
            }

            bool createManager = managerTarget == null && CanCreate(result, "manager");
            bool createPlayer = playerTarget == null && CanCreate(result, "player");
            bool createCharacter = characterTarget == null && CanCreate(result, "character");
            if (createManager) result.Changes.Add($"Create {ManagerName} with ConvaiManager and ConvaiRoomManager.");
            if (createPlayer) result.Changes.Add($"Create standalone {PlayerName} with ConvaiPlayer.");
            if (createCharacter)
                result.Changes.Add($"Create visible Capsule {CharacterName} with ConvaiCharacter, AudioSource, and ConvaiAudioOutput.");

            var roomRequest = new ConvaiConfigureRoomRequest
            {
                ConfigurationMode = roomProfile != null
                    ? ConvaiToolConfigurationMode.ExistingProfile
                    : ConvaiToolConfigurationMode.Inline,
                ProfileAssetPath = request.RoomProfileAssetPath,
                ConnectionType = ConvaiConnectionType.Audio,
                InputMode = request.InputMode,
                ConnectOnStart = request.ConnectOnStart,
                ServerEndpoint = ConvaiServerEndpoint.Connect,
                VisionMode = ConvaiVisionContextMode.Auto,
                PushToTalkKey = "T",
                DryRun = request.DryRun
            };
            var characterRequest = new ConvaiConfigureCharacterRequest
            {
                ConfigurationMode = characterProfile != null
                    ? ConvaiToolConfigurationMode.ExistingProfile
                    : ConvaiToolConfigurationMode.Inline,
                ProfileAssetPath = request.CharacterProfileAssetPath,
                CharacterId = request.CharacterId,
                CharacterName = request.CharacterName,
                AddAudioOutput = true,
                DryRun = request.DryRun
            };

            if (managerTarget != null) PreviewRoom(managerTarget, roomRequest, roomProfile, result.Changes);
            if (playerTarget != null) PreviewPlayer(playerTarget,
                new ConvaiConfigurePlayerRequest { PlayerName = request.PlayerName, PlayerId = request.PlayerId },
                managerTarget?.GetComponent<ConvaiManager>(), result.Changes);
            if (characterTarget != null) PreviewCharacter(characterTarget, characterRequest, characterProfile,
                managerTarget?.GetComponent<ConvaiManager>(), result.Changes);

            if (!request.DryRun && result.Changes.Count > 0)
            {
                if (!ApplyUndoGroup("Setup Convai Conversation Scene", () =>
                    {
                        if (createManager)
                        {
                            managerTarget = new GameObject(ManagerName);
                            SceneManager.MoveGameObjectToScene(managerTarget, scene);
                            Undo.RegisterCreatedObjectUndo(managerTarget, "Create Convai Manager");
                        }

                        if (createPlayer)
                        {
                            playerTarget = new GameObject(PlayerName);
                            SceneManager.MoveGameObjectToScene(playerTarget, scene);
                            Undo.RegisterCreatedObjectUndo(playerTarget, "Create Convai Player");
                        }

                        if (createCharacter)
                        {
                            characterTarget = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                            characterTarget.name = CharacterName;
                            characterTarget.transform.position = new Vector3(0f, 1f, 0f);
                            SceneManager.MoveGameObjectToScene(characterTarget, scene);
                            Undo.RegisterCreatedObjectUndo(characterTarget, "Create Convai Character");
                        }

                        ConvaiManager manager = null;
                        if (managerTarget != null)
                        {
                            ApplyRoom(managerTarget, roomRequest, roomProfile);
                            manager = managerTarget.GetComponent<ConvaiManager>();
                        }

                        ConvaiPlayer player = playerTarget != null
                            ? ApplyPlayer(playerTarget, request.PlayerName, request.PlayerId)
                            : null;
                        ConvaiCharacter character = characterTarget != null
                            ? ApplyCharacter(characterTarget, characterRequest, characterProfile)
                            : null;
                        if (manager != null && player != null) BindPlayer(manager, player);
                        if (manager != null && character != null) BindCharacter(manager, character);
                        MarkSceneDirty(scene);
                    }, out error))
                    return ConvaiAuthoringResult.Failure("MUTATION_FAILED", error, false);
            }

            result.ManagerInstanceId = managerTarget != null ? EntityIdOf(managerTarget) : 0;
            result.RoomInstanceId = result.ManagerInstanceId;
            result.PlayerInstanceId = playerTarget != null ? EntityIdOf(playerTarget) : 0;
            result.CharacterInstanceId = characterTarget != null ? EntityIdOf(characterTarget) : 0;

            string effectiveCharacterId = characterProfile != null ? characterProfile.CharacterId : request.CharacterId;
            if (string.IsNullOrWhiteSpace(effectiveCharacterId)) result.RequiredInputs.Add("characterId");
            ConvaiSettings settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
                result.Warnings.Add("API key is not configured. Complete it manually in Edit > Project Settings > Convai SDK before Play Mode verification.");

            result.Complete = result.BlockedSteps.Count == 0 && result.RequiredInputs.Count == 0 &&
                              settings != null && settings.HasApiKey &&
                              (request.DryRun
                                  ? managerTarget != null || createManager
                                  : result.ManagerInstanceId != 0) &&
                              (request.DryRun ? playerTarget != null || createPlayer : result.PlayerInstanceId != 0) &&
                              (request.DryRun
                                  ? characterTarget != null || createCharacter
                                  : result.CharacterInstanceId != 0);
            return result;
        }

        public static T[] GetSceneComponents<T>(Scene scene, bool includeInactive = true) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded) return Array.Empty<T>();
            var values = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T[] found = root.GetComponentsInChildren<T>(includeInactive);
                values.AddRange(found.Where(value => value != null));
            }

            return values.ToArray();
        }

        public static bool IsValidCharacterId(string characterId) =>
            string.IsNullOrWhiteSpace(characterId) ||
            Guid.TryParseExact(characterId.Trim(), "D", out _);

        public static long EntityIdOf(Object value) => ConvaiMcpEntityRef.ToToolId(value);

        private static bool CanMutate(out ConvaiAuthoringResult failure, bool dryRun)
        {
            failure = null;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            failure = ConvaiAuthoringResult.Failure(
                "PLAY_MODE_ACTIVE",
                "Convai scene authoring tools are available only in Edit Mode.",
                dryRun);
            return false;
        }

        private static bool TryResolveGameObject(long instanceId, out GameObject gameObject, out string error)
        {
            gameObject = null;
            error = null;
            if (instanceId == 0)
            {
                error = "A non-zero target instance ID is required.";
                return false;
            }

            ConvaiMcpEntityRef.TryResolve(instanceId, out gameObject);
            Scene activeScene = SceneManager.GetActiveScene();
            if (gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded &&
                gameObject.scene == activeScene)
                return true;
            if (gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                error = $"Instance ID {instanceId} belongs to scene '{gameObject.scene.name}', not active scene '{activeScene.name}'.";
                gameObject = null;
                return false;
            }
            error = $"No loaded-scene GameObject found for instance ID {instanceId}.";
            gameObject = null;
            return false;
        }

        private static bool TryResolveManager(Scene scene, long instanceId, out ConvaiManager manager,
            out string warning, out string error)
        {
            manager = null;
            warning = null;
            error = null;
            if (instanceId != 0)
            {
                if (!TryResolveGameObject(instanceId, out GameObject target, out error)) return false;
                if (target.scene != scene)
                {
                    error = "Manager target must be in the same scene as the configured object.";
                    return false;
                }

                manager = target.GetComponent<ConvaiManager>();
                if (manager == null) error = "Manager target does not contain ConvaiManager.";
                return manager != null;
            }

            ConvaiManager[] managers = GetSceneComponents<ConvaiManager>(scene);
            if (managers.Length == 1)
            {
                manager = managers[0];
                return true;
            }

            warning = managers.Length == 0
                ? "No ConvaiManager exists in the target scene; ownership was not bound."
                : "Multiple ConvaiManager components exist; provide managerInstanceId to bind ownership.";
            return true;
        }

        private static bool TryResolveRoomProfile(ConvaiToolConfigurationMode mode, string path,
            out ConvaiRoomManagerProfile profile, out string error)
        {
            profile = null;
            error = null;
            if (mode == ConvaiToolConfigurationMode.Inline) return true;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "profileAssetPath is required in ExistingProfile mode.";
                return false;
            }

            profile = AssetDatabase.LoadAssetAtPath<ConvaiRoomManagerProfile>(path.Trim());
            if (profile != null) return true;
            error = $"No ConvaiRoomManagerProfile found at '{path}'.";
            return false;
        }

        private static bool TryResolveCharacterProfile(ConvaiToolConfigurationMode mode, string path,
            out ConvaiCharacterProfile profile, out string error)
        {
            profile = null;
            error = null;
            if (mode == ConvaiToolConfigurationMode.Inline) return true;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "profileAssetPath is required in ExistingProfile mode.";
                return false;
            }

            profile = AssetDatabase.LoadAssetAtPath<ConvaiCharacterProfile>(path.Trim());
            if (profile != null) return true;
            error = $"No ConvaiCharacterProfile found at '{path}'.";
            return false;
        }

        private static bool ValidateCharacterId(string characterId, out string error)
        {
            error = null;
            if (IsValidCharacterId(characterId)) return true;
            error = "Character ID must use UUID format xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.";
            return false;
        }

        private static bool RequiresDynamicVision(ConvaiConfigureRoomRequest request,
            ConvaiRoomManagerProfile profile)
        {
            ConvaiConnectionType connectionType = profile != null
                ? profile.ConnectionType
                : request.ConnectionType;
            ConvaiVisionContextMode visionMode = profile != null
                ? profile.VisionContextMode
                : request.VisionMode;
            return visionMode == ConvaiVisionContextMode.Enabled ||
                   visionMode == ConvaiVisionContextMode.Auto && connectionType == ConvaiConnectionType.Video;
        }

        private static void PreviewRoom(GameObject target, ConvaiConfigureRoomRequest request,
            ConvaiRoomManagerProfile profile, List<string> changes)
        {
            if (target.GetComponent<ConvaiManager>() == null) changes.Add("Add ConvaiManager.");
            ConvaiRoomManager room = target.GetComponent<ConvaiRoomManager>();
            if (room == null)
            {
                changes.Add("Add ConvaiRoomManager.");
                changes.Add(request.ConfigurationMode == ConvaiToolConfigurationMode.ExistingProfile
                    ? $"Assign room profile {request.ProfileAssetPath}."
                    : $"Configure Audio room defaults: {request.InputMode}, connectOnStart={request.ConnectOnStart}.");
                return;
            }

            if (request.ConfigurationMode == ConvaiToolConfigurationMode.ExistingProfile)
            {
                if (room.ConfigurationSource != ConvaiConfigSourceMode.Asset || room.RoomConfigAsset != profile)
                    changes.Add($"Assign room profile {request.ProfileAssetPath}.");
                return;
            }

            if (room.ConfigurationSource != ConvaiConfigSourceMode.Inline || room.RoomConfigAsset != null ||
                room.EffectiveConnectionType != request.ConnectionType ||
                room.EffectiveTurnTakingOptions.Mode != request.InputMode ||
                room.EffectiveConnectOnStart != request.ConnectOnStart ||
                room.EffectiveServerEndpoint != request.ServerEndpoint ||
                room.EffectiveVisionContextMode != request.VisionMode ||
                room.PushToTalkKey.ToString() != request.PushToTalkKey)
                changes.Add($"Configure inline room defaults: {request.ConnectionType}, {request.InputMode}, connectOnStart={request.ConnectOnStart}.");
        }

        private static void PreviewPlayer(GameObject target, ConvaiConfigurePlayerRequest request,
            ConvaiManager manager, List<string> changes)
        {
            ConvaiPlayer player = target.GetComponent<ConvaiPlayer>();
            string playerName = NormalizePlayerName(request.PlayerName);
            string playerId = NormalizePlayerId(request.PlayerId, playerName);
            if (player == null) changes.Add("Add ConvaiPlayer.");
            if (player == null || player.PlayerName != playerName || player.PlayerId != playerId)
                changes.Add($"Configure player identity '{playerName}'.");
            if (manager != null && !ManagerReferencesPlayer(manager, player))
                changes.Add($"Bind player to ConvaiManager '{manager.gameObject.name}'.");
        }

        private static void PreviewCharacter(GameObject target, ConvaiConfigureCharacterRequest request,
            ConvaiCharacterProfile profile, ConvaiManager manager, List<string> changes)
        {
            ConvaiCharacter character = target.GetComponent<ConvaiCharacter>();
            if (character == null) changes.Add("Add ConvaiCharacter.");
            if (request.ConfigurationMode == ConvaiToolConfigurationMode.ExistingProfile)
            {
                if (character == null || character.ConfigurationSource != ConvaiConfigSourceMode.Asset ||
                    character.CharacterConfigAsset != profile)
                    changes.Add($"Assign character profile {request.ProfileAssetPath}.");
            }
            else
            {
                string name = NormalizeCharacterName(request.CharacterName, target.name);
                string id = request.CharacterId?.Trim() ?? string.Empty;
                if (character == null || character.ConfigurationSource != ConvaiConfigSourceMode.Inline ||
                    character.CharacterConfigAsset != null || character.CharacterId != id ||
                    character.CharacterName != name)
                    changes.Add($"Configure inline character identity '{name}'.");
            }

            if (request.AddAudioOutput && target.GetComponent<AudioSource>() == null) changes.Add("Add AudioSource.");
            if (request.AddAudioOutput && target.GetComponent<ConvaiAudioOutput>() == null)
                changes.Add("Add ConvaiAudioOutput.");
            if (manager != null && !ManagerReferencesCharacter(manager, character))
                changes.Add($"Bind character to ConvaiManager '{manager.gameObject.name}' as active target.");
        }

        private static void ApplyRoom(GameObject target, ConvaiConfigureRoomRequest request,
            ConvaiRoomManagerProfile profile)
        {
            if (target.GetComponent<ConvaiManager>() == null) Undo.AddComponent<ConvaiManager>(target);
            ConvaiRoomManager room = target.GetComponent<ConvaiRoomManager>() ?? Undo.AddComponent<ConvaiRoomManager>(target);
            Undo.RecordObject(room, "Configure Convai Room");
            var serialized = new SerializedObject(room);
            SetEnum(serialized, "_configurationSource",
                request.ConfigurationMode == ConvaiToolConfigurationMode.ExistingProfile
                    ? (int)ConvaiConfigSourceMode.Asset
                    : (int)ConvaiConfigSourceMode.Inline);
            SetBool(serialized, "_configurationSourceInitialized", true);
            SetObject(serialized, "_roomConfigAsset", profile);
            if (request.ConfigurationMode == ConvaiToolConfigurationMode.Inline)
            {
                SetEnum(serialized, "_connectionType", (int)request.ConnectionType);
                SetEnum(serialized, "<ServerEndpoint>k__BackingField", (int)request.ServerEndpoint);
                SetBool(serialized, "<ConnectOnStart>k__BackingField", request.ConnectOnStart);
                SetEnum(serialized, "_visionContextMode", (int)request.VisionMode);
                SetEnum(serialized, "_pushToTalkKey",
                    (int)(KeyCode)Enum.Parse(typeof(KeyCode), request.PushToTalkKey, true));
                SetTurnTakingDefaults(serialized, request.InputMode);
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(room);
        }

        private static ConvaiPlayer ApplyPlayer(GameObject target, string requestedName, string requestedId)
        {
            ConvaiPlayer player = target.GetComponent<ConvaiPlayer>() ?? Undo.AddComponent<ConvaiPlayer>(target);
            string playerName = NormalizePlayerName(requestedName);
            string playerId = NormalizePlayerId(requestedId, playerName);
            if (player.PlayerName != playerName || player.PlayerId != playerId)
            {
                Undo.RecordObject(player, "Configure Convai Player");
                player.Configure(playerName, playerId);
                EditorUtility.SetDirty(player);
            }

            return player;
        }

        private static ConvaiCharacter ApplyCharacter(GameObject target, ConvaiConfigureCharacterRequest request,
            ConvaiCharacterProfile profile)
        {
            ConvaiCharacter character = target.GetComponent<ConvaiCharacter>() ?? Undo.AddComponent<ConvaiCharacter>(target);
            Undo.RecordObject(character, "Configure Convai Character");
            var serialized = new SerializedObject(character);
            SetEnum(serialized, "_configurationSource",
                request.ConfigurationMode == ConvaiToolConfigurationMode.ExistingProfile
                    ? (int)ConvaiConfigSourceMode.Asset
                    : (int)ConvaiConfigSourceMode.Inline);
            SetBool(serialized, "_configurationSourceInitialized", true);
            SetObject(serialized, "_characterConfigAsset", profile);
            if (request.ConfigurationMode == ConvaiToolConfigurationMode.Inline)
            {
                SetString(serialized, "_characterId", request.CharacterId?.Trim() ?? string.Empty);
                SetString(serialized, "_characterName", NormalizeCharacterName(request.CharacterName, target.name));
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(character);
            if (request.AddAudioOutput)
            {
                if (target.GetComponent<AudioSource>() == null) Undo.AddComponent<AudioSource>(target);
                if (target.GetComponent<ConvaiAudioOutput>() == null) Undo.AddComponent<ConvaiAudioOutput>(target);
            }

            return character;
        }

        private static void BindPlayer(ConvaiManager manager, ConvaiPlayer player)
        {
            if (manager == null || player == null || ManagerReferencesPlayer(manager, player)) return;
            Undo.RecordObject(manager, "Bind Convai Player");
            var serialized = new SerializedObject(manager);
            SetObject(serialized, "_explicitPlayer", player);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
        }

        private static void BindCharacter(ConvaiManager manager, ConvaiCharacter character)
        {
            if (manager == null || character == null) return;
            Undo.RecordObject(manager, "Bind Convai Character");
            var serialized = new SerializedObject(manager);
            SerializedProperty characters = Required(serialized, "_explicitCharacters");
            bool found = false;
            for (int i = 0; i < characters.arraySize; i++)
            {
                if (characters.GetArrayElementAtIndex(i).objectReferenceValue == character)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                int index = characters.arraySize;
                characters.InsertArrayElementAtIndex(index);
                characters.GetArrayElementAtIndex(index).objectReferenceValue = character;
            }

            SetObject(serialized, "_explicitConversationTarget", character);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
        }

        private static bool ManagerReferencesPlayer(ConvaiManager manager, ConvaiPlayer player)
        {
            if (manager == null || player == null) return false;
            var serialized = new SerializedObject(manager);
            return Required(serialized, "_explicitPlayer").objectReferenceValue == player;
        }

        private static bool ManagerReferencesCharacter(ConvaiManager manager, ConvaiCharacter character)
        {
            if (manager == null || character == null) return false;
            var serialized = new SerializedObject(manager);
            SerializedProperty characters = Required(serialized, "_explicitCharacters");
            for (int i = 0; i < characters.arraySize; i++)
                if (characters.GetArrayElementAtIndex(i).objectReferenceValue == character)
                    return Required(serialized, "_explicitConversationTarget").objectReferenceValue == character;
            return false;
        }

        private static GameObject ResolveSetupTarget<T>(Scene scene, long explicitId, string placeholderName,
            bool createPlaceholders, ConvaiAuthoringResult result, string label, out string error) where T : Component
        {
            error = null;
            if (explicitId != 0)
            {
                if (!TryResolveGameObject(explicitId, out GameObject target, out error)) return null;
                if (target.scene != scene)
                {
                    error = $"Explicit {label} target must belong to the active scene.";
                    return null;
                }

                return target;
            }

            T[] existing = GetSceneComponents<T>(scene);
            if (existing.Length == 1) return existing[0].gameObject;
            if (existing.Length > 1)
            {
                result.BlockedSteps.Add($"Select one of {existing.Length} existing {label} targets by instance ID.");
                result.RequiredInputs.Add(label + "InstanceId");
                return null;
            }

            if (createPlaceholders) return null;
            result.BlockedSteps.Add($"No {label} exists and placeholder creation is disabled.");
            result.RequiredInputs.Add(label + "InstanceId");
            return null;
        }

        private static bool CanCreate(ConvaiAuthoringResult result, string label) =>
            !result.RequiredInputs.Contains(label + "InstanceId");

        private static bool ApplyUndoGroup(string name, Action action, out string error)
        {
            error = null;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            try
            {
                action();
                Undo.CollapseUndoOperations(group);
                return true;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                error = exception.GetBaseException().Message;
                return false;
            }
        }

        private static void MarkSceneDirty(Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
        }

        private static string NormalizePlayerName(string value) =>
            string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim();

        private static string NormalizePlayerId(string value, string playerName) =>
            string.IsNullOrWhiteSpace(value) ? playerName : value.Trim();

        private static string NormalizeCharacterName(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static void SetTurnTakingDefaults(SerializedObject serialized, ConversationInputMode mode)
        {
            SerializedProperty root = Required(serialized, "_turnTakingOptions");
            SetRelativeEnum(root, "<Mode>k__BackingField", (int)mode);
            SetRelativeEnum(root, "<TurnDetection>k__BackingField", (int)TurnDetectionMode.UseDefault);
            SetRelativeEnum(root, "<InitialServerStt>k__BackingField", (int)ServerSttInitialState.UseDefault);
            SerializedProperty local = RequiredRelative(root, "<LocalAudioPolicy>k__BackingField");
            SetRelativeBool(local, "<StartMutedInPushToTalk>k__BackingField", true);
            SetRelativeBool(local, "<EnableAcousticEchoCancellation>k__BackingField", false);
            SetRelativeEnum(local, "<PushToTalkStartupMode>k__BackingField",
                (int)PushToTalkMicStartupMode.PrewarmMuted);
            SerializedProperty push = RequiredRelative(root, "<PushToTalkPolicy>k__BackingField");
            SetRelativeBool(push, "<EnableServerSttToggle>k__BackingField", true);
            SetRelativeBool(push, "<InterruptBotOnPress>k__BackingField", true);
            SetRelativeBool(push, "<RequireTurnCompletionBeforeNextPress>k__BackingField", true);
            SetRelativeInt(push, "<TurnCompletionTimeoutMs>k__BackingField",
                PushToTalkPolicy.DefaultTurnCompletionTimeoutMs);
            SetRelativeBool(push, "<AllowSpeechStoppedFallbackAfterSpeechStart>k__BackingField", false);
            SerializedProperty bargeIn = RequiredRelative(root, "<BargeIn>k__BackingField");
            SetRelativeBool(bargeIn, "<SmoothInterruption>k__BackingField", true);
            SetRelativeFloat(
                bargeIn,
                "<FadeOutSeconds>k__BackingField",
                BargeInOptions.DefaultFadeOutSeconds);
            SetRelativeEnum(
                bargeIn,
                "<ClientDetection>k__BackingField",
                (int)ClientBargeInMode.Disabled);
        }

        private static SerializedProperty Required(SerializedObject serialized, string path) =>
            serialized.FindProperty(path) ??
            throw new InvalidOperationException($"Required serialized property '{path}' was not found on {serialized.targetObject.GetType().Name}.");

        private static SerializedProperty RequiredRelative(SerializedProperty parent, string path) =>
            parent.FindPropertyRelative(path) ??
            throw new InvalidOperationException($"Required serialized property '{parent.propertyPath}.{path}' was not found.");

        private static void SetEnum(SerializedObject serialized, string path, int value) =>
            Required(serialized, path).intValue = value;

        private static void SetBool(SerializedObject serialized, string path, bool value) =>
            Required(serialized, path).boolValue = value;

        private static void SetString(SerializedObject serialized, string path, string value) =>
            Required(serialized, path).stringValue = value;

        private static void SetObject(SerializedObject serialized, string path, Object value) =>
            Required(serialized, path).objectReferenceValue = value;

        private static void SetRelativeEnum(SerializedProperty parent, string path, int value) =>
            RequiredRelative(parent, path).intValue = value;

        private static void SetRelativeBool(SerializedProperty parent, string path, bool value) =>
            RequiredRelative(parent, path).boolValue = value;

        private static void SetRelativeInt(SerializedProperty parent, string path, int value) =>
            RequiredRelative(parent, path).intValue = value;

        private static void SetRelativeFloat(SerializedProperty parent, string path, float value) =>
            RequiredRelative(parent, path).floatValue = value;
    }
}
