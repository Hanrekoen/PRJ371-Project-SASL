using System;
using System.Collections.Generic;
using Convai.Application;
using Convai.Runtime;
using Convai.Editor.Actions;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.Presentation.Events;
using Convai.Shared.Types;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Editor.AI
{
    internal static class ConvaiFeatureAuthoringService
    {
        internal static object ConfigureActions(ConvaiConfigureActionsRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return ConvaiMcpTools.FeatureFailure("PLAY_MODE_ACTIVE", "Action authoring is available only in Edit Mode.");
            if (request == null || request.CharacterInstanceId == 0)
                return ConvaiMcpTools.FeatureFailure("TARGET_REQUIRED", "characterInstanceId is required.");
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return ConvaiMcpTools.FeatureFailure(ConvaiMcpResolvers.CharacterErrorCode, error);

            ConvaiActionDefinitionInput[] definitions = request.Definitions ?? Array.Empty<ConvaiActionDefinitionInput>();
            if (!ValidateActionInputs(character.gameObject.scene, definitions, request.Objects, request.Characters, out error))
                return ConvaiMcpTools.FeatureFailure("INVALID_ACTION_INPUT", error);

            var changes = new List<string>();
            var blocked = new List<string>();
            ConvaiActionConfigSource source = character.GetComponent<ConvaiActionConfigSource>();
            ConvaiActionDispatcher dispatcher = character.GetComponent<ConvaiActionDispatcher>();
            if (source == null) changes.Add("Add ConvaiActionConfigSource");
            if (dispatcher == null) changes.Add("Add ConvaiActionDispatcher");
            BuildActionDiff(source, definitions, request.Objects, request.Characters,
                request.InitialAttentionObject, changes, blocked);
            if (request.DryRun)
                return ConvaiMcpTools.FeatureAuthoring(true, blocked.Count == 0, changes, blocked,
                    Array.Empty<string>(), character);
            if (changes.Count == 0)
                return ConvaiMcpTools.FeatureAuthoring(false, blocked.Count == 0, changes, blocked,
                    Array.Empty<string>(), character);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure Convai Actions");
            try
            {
                source ??= Undo.AddComponent<ConvaiActionConfigSource>(character.gameObject);
                dispatcher ??= Undo.AddComponent<ConvaiActionDispatcher>(character.gameObject);
                Undo.RecordObject(source, "Configure Convai Actions");
                var serialized = new SerializedObject(source);
                UpsertDefinitions(serialized.FindProperty("_definitions"), definitions, source);
                UpsertTargets(serialized.FindProperty("_objects"), request.Objects, false);
                UpsertTargets(serialized.FindProperty("_characters"), request.Characters, true);
                serialized.FindProperty("_initialAttentionObject").stringValue = request.InitialAttentionObject?.Trim() ?? string.Empty;
                serialized.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                return ConvaiMcpTools.FeatureFailure("AUTHORING_FAILED", exception.Message);
            }

            IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics = ConvaiActionSetupReport.Validate(source);
            blocked.Clear();
            for (int i = 0; i < diagnostics.Count; i++)
                if (diagnostics[i].Severity == ConvaiActionConfigDiagnosticSeverity.Error) blocked.Add(diagnostics[i].Message);
            // Effective definitions (ActionSets merged + inline, auto-bound) so set-authored actions
            // are checked the same way as inline ones instead of only inspecting the raw inline list.
            IReadOnlyList<ConvaiActionDefinition> effectiveDefinitions = source.GetEffectiveDefinitions();
            for (int i = 0; i < effectiveDefinitions.Count; i++)
                if (ConvaiActionsAuthoringDefaults.IsUnwiredPlaceholder(effectiveDefinitions[i]?.Executor))
                    blocked.Add(ConvaiActionsAuthoringDefaults.UnwiredPlaceholderMessage(effectiveDefinitions[i].ActionName));
            return ConvaiMcpTools.FeatureAuthoring(false, blocked.Count == 0, changes, blocked, Array.Empty<string>(), character);
        }

        internal static object ConfigureTranscripts(ConvaiConfigureTranscriptsRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return ConvaiMcpTools.FeatureFailure("PLAY_MODE_ACTIVE", "Transcript authoring is available only in Edit Mode.");
            request ??= new ConvaiConfigureTranscriptsRequest();
            if (!ConvaiMcpResolvers.TryManager(request.ManagerInstanceId, true,
                    out ConvaiManager manager, out string error))
                return ConvaiMcpTools.FeatureFailure(ConvaiMcpResolvers.ManagerErrorCode, error);
            if (!ConvaiMcpResolvers.TryHost(request.HostInstanceId, manager.gameObject, false,
                    out GameObject host, out error))
                return ConvaiMcpTools.FeatureFailure("INVALID_TARGET", error);

            var changes = new List<string>();
            ConvaiTranscriptEventRelay existingRelay = host.GetComponent<ConvaiTranscriptEventRelay>();
            GameObject transcriptPrefab = null;
            if (request.Mode == ConvaiTranscriptToolMode.EventRelay)
            {
                if (existingRelay == null) changes.Add("Add ConvaiTranscriptEventRelay");
                else if (!RelayMatches(existingRelay, manager, request)) changes.Add("Update ConvaiTranscriptEventRelay");
            }
            else if (request.Mode is ConvaiTranscriptToolMode.ChatUI or ConvaiTranscriptToolMode.WorldSpaceChatUI)
            {
                string file = request.Mode == ConvaiTranscriptToolMode.ChatUI
                    ? "TranscriptUI_Chat.prefab"
                    : "TranscriptUI_Chat_WorldSpace.prefab";
                string path = $"Packages/com.convai.convai-sdk-for-unity/Prefabs/TranscriptUI/{file}";
                transcriptPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (transcriptPrefab == null)
                    return ConvaiMcpTools.FeatureFailure("PREFAB_NOT_FOUND", $"Shipped transcript prefab not found: {path}");
                if (!HasPrefabInstance(transcriptPrefab, host.scene))
                    changes.Add($"Instantiate shipped {request.Mode} prefab");
            }
            var blocked = new List<string>();
            if (ConvaiSettings.Instance == null || !ConvaiSettings.Instance.TranscriptSystemEnabled)
                blocked.Add("Enable Transcript System in Edit > Project Settings > Convai SDK.");
            if (request.DryRun)
                return ConvaiMcpTools.FeatureAuthoring(true, blocked.Count == 0, changes, blocked, Array.Empty<string>(), host.transform);
            if (changes.Count == 0)
                return ConvaiMcpTools.FeatureAuthoring(false, blocked.Count == 0, changes, blocked, Array.Empty<string>(), host.transform);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure Convai Transcripts");
            try
            {
                if (request.Mode == ConvaiTranscriptToolMode.EventRelay)
                {
                    ConvaiTranscriptEventRelay relay = host.GetComponent<ConvaiTranscriptEventRelay>() ?? Undo.AddComponent<ConvaiTranscriptEventRelay>(host);
                    Undo.RecordObject(relay, "Configure Transcript Relay");
                    var serialized = new SerializedObject(relay);
                    serialized.FindProperty("_manager").objectReferenceValue = manager;
                    serialized.FindProperty("_autoResolveManager").boolValue = false;
                    serialized.FindProperty("_finalOnly").boolValue = request.FinalOnly;
                    serialized.FindProperty("_ignoreInterimUpdates").boolValue = request.IgnoreInterim;
                    serialized.FindProperty("_characterIdFilter").stringValue = request.CharacterIdFilter ?? string.Empty;
                    serialized.ApplyModifiedProperties();
                }
                else if (request.Mode is ConvaiTranscriptToolMode.ChatUI or ConvaiTranscriptToolMode.WorldSpaceChatUI)
                {
                    if (!HasPrefabInstance(transcriptPrefab, host.scene))
                    {
                        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(transcriptPrefab, host.scene);
                        Undo.RegisterCreatedObjectUndo(instance, "Create Transcript UI");
                    }
                }
                EditorSceneManager.MarkSceneDirty(host.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                return ConvaiMcpTools.FeatureFailure("AUTHORING_FAILED", exception.Message);
            }
            return ConvaiMcpTools.FeatureAuthoring(false, blocked.Count == 0, changes, blocked, Array.Empty<string>(), host.transform);
        }

        private static bool ValidateActionInputs(
            Scene scene,
            ConvaiActionDefinitionInput[] definitions,
            ConvaiActionTargetInput[] objects,
            ConvaiActionTargetInput[] characters,
            out string error)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < definitions.Length; i++)
            {
                ConvaiActionDefinitionInput input = definitions[i];
                if (input == null || string.IsNullOrWhiteSpace(input.Name))
                {
                    error = $"Action definition #{i + 1} requires a name.";
                    return false;
                }
                if (!names.Add(input.Name.Trim()))
                {
                    error = $"Duplicate action definition '{input.Name.Trim()}'.";
                    return false;
                }
                if (input.ExecutorInstanceId != 0)
                {
                    MonoBehaviour executor = ResolveEntityObject(input.ExecutorInstanceId) as MonoBehaviour;
                    if (executor is not IConvaiActionExecutor || executor.gameObject.scene != scene)
                    {
                        error = $"Executor instance ID {input.ExecutorInstanceId} must implement IConvaiActionExecutor in the active scene.";
                        return false;
                    }
                }
            }

            if (!ValidateTargets(scene, objects, "object", out error) ||
                !ValidateTargets(scene, characters, "character", out error))
                return false;

            error = string.Empty;
            return true;
        }

        private static bool ValidateTargets(Scene scene, ConvaiActionTargetInput[] inputs, string label, out string error)
        {
            inputs ??= Array.Empty<ConvaiActionTargetInput>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < inputs.Length; i++)
            {
                ConvaiActionTargetInput input = inputs[i];
                if (input == null || string.IsNullOrWhiteSpace(input.Name))
                {
                    error = $"Action {label} #{i + 1} requires a name.";
                    return false;
                }
                if (!names.Add(input.Name.Trim()))
                {
                    error = $"Duplicate action {label} '{input.Name.Trim()}'.";
                    return false;
                }
                GameObject target = ResolveEntityObject(input.GameObjectInstanceId) as GameObject;
                if (target == null || target.scene != scene)
                {
                    error = $"Action {label} '{input.Name.Trim()}' requires a GameObject instance ID in the active scene.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private static void BuildActionDiff(
            ConvaiActionConfigSource source,
            ConvaiActionDefinitionInput[] definitions,
            ConvaiActionTargetInput[] objects,
            ConvaiActionTargetInput[] characters,
            string initialAttention,
            ICollection<string> changes,
            ICollection<string> blocked)
        {
            SerializedProperty definitionList = null;
            SerializedProperty objectList = null;
            SerializedProperty characterList = null;
            SerializedObject serialized = null;
            if (source != null)
            {
                serialized = new SerializedObject(source);
                definitionList = serialized.FindProperty("_definitions");
                objectList = serialized.FindProperty("_objects");
                characterList = serialized.FindProperty("_characters");
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                ConvaiActionDefinitionInput input = definitions[i];
                int index = definitionList == null ? -1 : FindByString(definitionList, "ActionName", input.Name);
                SerializedProperty item = index < 0 ? null : definitionList.GetArrayElementAtIndex(index);
                MonoBehaviour existingExecutor = item?.FindPropertyRelative("Executor")?.objectReferenceValue as MonoBehaviour;
                MonoBehaviour requestedExecutor = input.ExecutorInstanceId == 0
                    ? existingExecutor
                    : ResolveEntityObject(input.ExecutorInstanceId) as MonoBehaviour;
                if (item == null || !DefinitionMatches(item, input, requestedExecutor))
                    changes.Add($"Upsert action '{input.Name.Trim()}'");
                if (requestedExecutor == null || ConvaiActionsAuthoringDefaults.IsUnwiredPlaceholder(requestedExecutor))
                    blocked.Add(ConvaiActionsAuthoringDefaults.UnwiredPlaceholderMessage(input.Name.Trim()));
            }

            AddTargetDiffs(objectList, objects, false, changes);
            AddTargetDiffs(characterList, characters, true, changes);
            string normalizedAttention = initialAttention?.Trim() ?? string.Empty;
            if (source != null && !string.Equals(source.InitialAttentionObject ?? string.Empty, normalizedAttention, StringComparison.Ordinal))
                changes.Add("Update initial attention object");
        }

        private static bool DefinitionMatches(SerializedProperty item, ConvaiActionDefinitionInput input, MonoBehaviour executor)
        {
            if (!string.Equals(item.FindPropertyRelative("ActionName").stringValue, input.Name.Trim(), StringComparison.Ordinal) ||
                !string.Equals(item.FindPropertyRelative("Description").stringValue, input.Description ?? string.Empty, StringComparison.Ordinal) ||
                item.FindPropertyRelative("TargetRequirement").enumValueIndex != (int)input.TargetRequirement ||
                !Mathf.Approximately(item.FindPropertyRelative("TimeoutSeconds").floatValue, Mathf.Max(0f, input.TimeoutSeconds)) ||
                item.FindPropertyRelative("WaitForBotSpeech").boolValue != input.WaitForBotSpeech ||
                !Mathf.Approximately(item.FindPropertyRelative("DelayAfterBotSpeechSeconds").floatValue, Mathf.Max(0f, input.DelayAfterBotSpeechSeconds)) ||
                item.FindPropertyRelative("Executor").objectReferenceValue != executor)
                return false;
            // Enabled is serialized inverted as _disabled; a null input means "leave unchanged".
            if (input.Enabled.HasValue && item.FindPropertyRelative("_disabled").boolValue != !input.Enabled.Value)
                return false;
            // Category likewise: null leaves the authored value alone, and the comparison is against
            // the normalized name, so "  Counter " is not reported as a change to "Counter".
            if (input.Category != null &&
                !string.Equals(
                    item.FindPropertyRelative("_category").stringValue ?? string.Empty,
                    ConvaiActionDefinition.NormalizeCategory(input.Category),
                    StringComparison.Ordinal))
                return false;
            return ParametersMatch(item.FindPropertyRelative("Parameters"), input.Parameters);
        }

        private static bool ParametersMatch(SerializedProperty list, ConvaiActionParameterInput[] inputs)
        {
            inputs ??= Array.Empty<ConvaiActionParameterInput>();
            if (list.arraySize != inputs.Length) return false;
            for (int i = 0; i < inputs.Length; i++)
            {
                ConvaiActionParameterInput input = inputs[i] ?? new ConvaiActionParameterInput();
                SerializedProperty item = list.GetArrayElementAtIndex(i);
                if (!string.Equals(item.FindPropertyRelative("Name").stringValue, input.Name?.Trim() ?? string.Empty, StringComparison.Ordinal) ||
                    !string.Equals(item.FindPropertyRelative("Description").stringValue, input.Description ?? string.Empty, StringComparison.Ordinal) ||
                    item.FindPropertyRelative("Type").enumValueIndex != (int)input.Type ||
                    !string.Equals(item.FindPropertyRelative("Connector").stringValue, input.Connector ?? string.Empty, StringComparison.Ordinal))
                    return false;
                string[] choices = input.Choices ?? Array.Empty<string>();
                SerializedProperty choiceList = item.FindPropertyRelative("Choices");
                if (choiceList.arraySize != choices.Length) return false;
                for (int j = 0; j < choices.Length; j++)
                    if (!string.Equals(choiceList.GetArrayElementAtIndex(j).stringValue, choices[j] ?? string.Empty, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static void AddTargetDiffs(SerializedProperty list, ConvaiActionTargetInput[] inputs, bool character,
            ICollection<string> changes)
        {
            inputs ??= Array.Empty<ConvaiActionTargetInput>();
            for (int i = 0; i < inputs.Length; i++)
            {
                ConvaiActionTargetInput input = inputs[i];
                int index = list == null ? -1 : FindByString(list, "<Name>k__BackingField", input.Name);
                SerializedProperty item = index < 0 ? null : list.GetArrayElementAtIndex(index);
                GameObject target = ResolveEntityObject(input.GameObjectInstanceId) as GameObject;
                if (item == null ||
                    !string.Equals(item.FindPropertyRelative(character ? "<Bio>k__BackingField" : "<Description>k__BackingField").stringValue, input.Description ?? string.Empty, StringComparison.Ordinal) ||
                    item.FindPropertyRelative("<GameObjectReference>k__BackingField").objectReferenceValue != target)
                    changes.Add($"Upsert action {(character ? "character" : "object")} '{input.Name.Trim()}'");
            }
        }

        private static bool RelayMatches(ConvaiTranscriptEventRelay relay, ConvaiManager manager,
            ConvaiConfigureTranscriptsRequest request) =>
            relay.Manager == manager && !relay.AutoResolveManager && relay.FinalOnly == request.FinalOnly &&
            relay.IgnoreInterimUpdates == request.IgnoreInterim &&
            string.Equals(relay.CharacterIdFilter ?? string.Empty, request.CharacterIdFilter ?? string.Empty, StringComparison.Ordinal);

        private static bool HasPrefabInstance(GameObject prefab, Scene scene)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate == null || candidate.scene != scene || !candidate.scene.IsValid()) continue;
                if (PrefabUtility.GetCorrespondingObjectFromSource(candidate) == prefab) return true;
            }
            return false;
        }

        private static void UpsertDefinitions(SerializedProperty list, ConvaiActionDefinitionInput[] inputs, ConvaiActionConfigSource source)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                ConvaiActionDefinitionInput input = inputs[i];
                int index = FindByString(list, "ActionName", input.Name);
                if (index < 0) { index = list.arraySize; list.InsertArrayElementAtIndex(index); }
                SerializedProperty item = list.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("ActionName").stringValue = input.Name.Trim();
                item.FindPropertyRelative("Description").stringValue = input.Description ?? string.Empty;
                WriteParameters(item.FindPropertyRelative("Parameters"), input.Parameters);
                item.FindPropertyRelative("TargetRequirement").enumValueIndex = (int)input.TargetRequirement;
                item.FindPropertyRelative("TimeoutSeconds").floatValue = Mathf.Max(0f, input.TimeoutSeconds);
                item.FindPropertyRelative("WaitForBotSpeech").boolValue = input.WaitForBotSpeech;
                item.FindPropertyRelative("DelayAfterBotSpeechSeconds").floatValue = Mathf.Max(0f, input.DelayAfterBotSpeechSeconds);
                // Enabled is serialized inverted as _disabled; null leaves the authored value
                // unchanged (a fresh array element defaults to _disabled=false, i.e. enabled).
                if (input.Enabled.HasValue)
                    item.FindPropertyRelative("_disabled").boolValue = !input.Enabled.Value;
                // Written through serialization, which bypasses the Category property — so it is
                // normalized here with the same rule authoring uses. Null leaves it unchanged.
                if (input.Category != null)
                    item.FindPropertyRelative("_category").stringValue =
                        ConvaiActionDefinition.NormalizeCategory(input.Category);
                SerializedProperty executorProperty = item.FindPropertyRelative("Executor");
                MonoBehaviour executor = input.ExecutorInstanceId == 0
                    ? executorProperty.objectReferenceValue as MonoBehaviour
                    : ResolveEntityObject(input.ExecutorInstanceId) as MonoBehaviour;
                if (executor == null) executor = ConvaiActionsAuthoringDefaults.AddPlaceholderExecutor(source);
                executorProperty.objectReferenceValue = executor;
            }
        }

        private static void WriteParameters(SerializedProperty list, ConvaiActionParameterInput[] inputs)
        {
            inputs ??= Array.Empty<ConvaiActionParameterInput>();
            list.arraySize = inputs.Length;
            for (int i = 0; i < inputs.Length; i++)
            {
                ConvaiActionParameterInput input = inputs[i] ?? new ConvaiActionParameterInput();
                SerializedProperty item = list.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("Name").stringValue = input.Name?.Trim() ?? string.Empty;
                item.FindPropertyRelative("Description").stringValue = input.Description ?? string.Empty;
                item.FindPropertyRelative("Type").enumValueIndex = (int)input.Type;
                item.FindPropertyRelative("Connector").stringValue = input.Connector ?? string.Empty;
                SerializedProperty choices = item.FindPropertyRelative("Choices");
                string[] values = input.Choices ?? Array.Empty<string>();
                choices.arraySize = values.Length;
                for (int j = 0; j < values.Length; j++) choices.GetArrayElementAtIndex(j).stringValue = values[j] ?? string.Empty;
            }
        }

        private static void UpsertTargets(SerializedProperty list, ConvaiActionTargetInput[] inputs, bool character)
        {
            inputs ??= Array.Empty<ConvaiActionTargetInput>();
            for (int i = 0; i < inputs.Length; i++)
            {
                ConvaiActionTargetInput input = inputs[i];
                if (input == null || string.IsNullOrWhiteSpace(input.Name)) continue;
                const string nameField = "<Name>k__BackingField";
                int index = FindByString(list, nameField, input.Name);
                if (index < 0) { index = list.arraySize; list.InsertArrayElementAtIndex(index); }
                SerializedProperty item = list.GetArrayElementAtIndex(index);
                item.FindPropertyRelative(nameField).stringValue = input.Name.Trim();
                item.FindPropertyRelative(character ? "<Bio>k__BackingField" : "<Description>k__BackingField").stringValue = input.Description ?? string.Empty;
                item.FindPropertyRelative("<GameObjectReference>k__BackingField").objectReferenceValue = ResolveEntityObject(input.GameObjectInstanceId) as GameObject;
            }
        }

        private static int FindByString(SerializedProperty list, string field, string value)
        {
            for (int i = 0; i < list.arraySize; i++)
                if (string.Equals(list.GetArrayElementAtIndex(i).FindPropertyRelative(field)?.stringValue, value?.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static UnityEngine.Object ResolveEntityObject(long id) => ConvaiMcpEntityRef.Resolve(id);
    }
}
