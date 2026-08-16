using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Editor.AI;
using Convai.Runtime.Components;
using Convai.Shared.Compatibility;
using Convai.Shared;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Modules.Narrative.Editor.AI
{
    public sealed class NarrativeSectionInput
    {
        public string SectionId { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
    }

    public sealed class NarrativeTemplateKeyInput
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public sealed class NarrativeTriggerInput
    {
        public long HostInstanceId { get; set; }
        public string TriggerId { get; set; } = string.Empty;
        public string TriggerName { get; set; } = string.Empty;
        public TriggerActivationMode ActivationMode { get; set; } = TriggerActivationMode.Collision;
        public float ProximityRadius { get; set; } = 3f;
        public float TimeDelay { get; set; }
        public bool TriggerOnce { get; set; } = true;
        public int PlayerLayerMask { get; set; } = -1;
        public string PlayerTag { get; set; } = "Player";
        public bool QueueUntilReady { get; set; } = true;
        public float MaxWaitTime { get; set; } = 30f;
    }

    public sealed class ConfigureNarrativeRequest
    {
        public long CharacterInstanceId { get; set; }
        public long ManagerHostInstanceId { get; set; }
        public NarrativeSectionInput[] Sections { get; set; } = Array.Empty<NarrativeSectionInput>();
        public NarrativeTemplateKeyInput[] TemplateKeys { get; set; } = Array.Empty<NarrativeTemplateKeyInput>();
        public NarrativeTriggerInput[] Triggers { get; set; } = Array.Empty<NarrativeTriggerInput>();
        public bool DryRun { get; set; } = true;
    }

    public sealed class DiagnoseNarrativeRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool IncludeInactive { get; set; } = true;
        public bool IncludeContent { get; set; }
    }

    public static class ConvaiNarrativeMcpTools
    {
        private const string ConfigureTool = "Convai.ConfigureNarrative";
        private const string DiagnoseTool = "Convai.DiagnoseNarrative";

        [McpTool(ConfigureTool, "Previews or configures Unity-side Convai narrative section mappings, template keys, and triggers. Preserves unrelated entries and UnityEvents; never contacts the backend.", "Configure Convai Narrative", Groups = new[] { "convai", "narrative" }, EnabledByDefault = true)]
        public static object Configure(JObject input) => Configure(input?.ToObject<ConfigureNarrativeRequest>());

        public static object Configure(ConfigureNarrativeRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return Failure("PLAY_MODE_ACTIVE", "Narrative authoring is available only in Edit Mode.");
            request ??= new ConfigureNarrativeRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Failure(ConvaiMcpResolvers.CharacterErrorCode, error);
            if (!ValidateInputs(request, out error)) return Failure("INVALID_INPUT", error);
            if (!ConvaiMcpResolvers.TryHost(request.ManagerHostInstanceId, character.gameObject, true,
                    out GameObject managerHost, out error))
                return Failure("INVALID_MANAGER_HOST", error);
            ConvaiNarrativeDesignManager manager = managerHost.GetComponent<ConvaiNarrativeDesignManager>();

            var changes = new List<string>();
            if (manager == null) changes.Add($"Add ConvaiNarrativeDesignManager to {managerHost.name}");
            if (manager == null || manager.CharacterComponent != character) changes.Add("Bind narrative manager character");
            CollectManagerChanges(manager, request, changes);
            foreach (NarrativeTriggerInput input in request.Triggers)
            {
                if (!ConvaiMcpResolvers.TryHost(input.HostInstanceId, character.gameObject, true,
                        out GameObject host, out error))
                    return Failure("INVALID_TRIGGER_HOST", error);
                ConvaiNarrativeDesignTrigger trigger = FindTrigger(host, input.TriggerId);
                if (trigger == null) changes.Add($"Add ConvaiNarrativeDesignTrigger to {host.name}");
                if (trigger == null || !TriggerMatches(trigger, input, character)) changes.Add($"Configure narrative trigger '{input.TriggerId}' on {host.name}");
            }

            var affected = new List<long> { ConvaiMcpEntityRef.ToToolId(character) };
            if (!request.DryRun && changes.Count > 0)
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Configure Convai Narrative");
                try
                {
                    manager ??= Undo.AddComponent<ConvaiNarrativeDesignManager>(managerHost);
                    ConfigureManager(manager, character, request);
                    affected.Add(ConvaiMcpEntityRef.ToToolId(manager));
                    foreach (NarrativeTriggerInput input in request.Triggers)
                    {
                        ConvaiMcpResolvers.TryHost(input.HostInstanceId, character.gameObject, true,
                            out GameObject host, out _);
                        ConvaiNarrativeDesignTrigger trigger = FindTrigger(host, input.TriggerId) ?? Undo.AddComponent<ConvaiNarrativeDesignTrigger>(host);
                        ConfigureTrigger(trigger, character, input);
                        affected.Add(ConvaiMcpEntityRef.ToToolId(trigger));
                    }
                    EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
                    Undo.CollapseUndoOperations(group);
                }
                catch (Exception exception)
                {
                    Undo.RevertAllDownToGroup(group);
                    return Failure("AUTHORING_FAILED", exception.Message);
                }
            }

            return Response(true, request.DryRun ? "Previewed Convai narrative configuration." : "Configured Convai narrative components.", new
            {
                dryRun = request.DryRun,
                complete = true,
                changes,
                blockedSteps = Array.Empty<string>(),
                requiredInputs = Array.Empty<string>(),
                warnings = Array.Empty<string>(),
                affectedInstanceIds = affected.Distinct().ToArray(),
                managerInstanceId = ConvaiMcpEntityRef.ToToolId(manager),
                backendWrites = false,
                sceneDirty = SceneManager.GetActiveScene().isDirty,
                sceneSaved = false
            });
        }

        [McpSchema(ConfigureTool)]
        public static object ConfigureSchema() => ConvaiMcpResponses.ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerSchema(),
                ["managerHostInstanceId"] = ConvaiMcpResponses.IntegerSchema(0),
                ["sections"] = ConvaiMcpResponses.ArraySchema(ConvaiMcpResponses.NestedObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["sectionId"] = ConvaiMcpResponses.StringSchema(),
                        ["sectionName"] = ConvaiMcpResponses.StringSchema()
                    },
                    "sectionId")),
                ["templateKeys"] = ConvaiMcpResponses.ArraySchema(ConvaiMcpResponses.NestedObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["key"] = ConvaiMcpResponses.StringSchema(),
                        ["value"] = ConvaiMcpResponses.StringSchema()
                    },
                    "key")),
                ["triggers"] = ConvaiMcpResponses.ArraySchema(ConvaiMcpResponses.NestedObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["hostInstanceId"] = ConvaiMcpResponses.IntegerSchema(0),
                        ["triggerId"] = ConvaiMcpResponses.StringSchema(),
                        ["triggerName"] = ConvaiMcpResponses.StringSchema(),
                        ["activationMode"] = ConvaiMcpResponses.StringEnumSchema(
                            new[] { "Collision", "Proximity", "Manual", "TimeBased" }),
                        ["proximityRadius"] = ConvaiMcpResponses.NumberSchema(3),
                        ["timeDelay"] = ConvaiMcpResponses.NumberSchema(0),
                        ["triggerOnce"] = ConvaiMcpResponses.BooleanSchema(true),
                        ["playerLayerMask"] = ConvaiMcpResponses.IntegerSchema(-1),
                        ["playerTag"] = ConvaiMcpResponses.StringSchema("Player"),
                        ["queueUntilReady"] = ConvaiMcpResponses.BooleanSchema(true),
                        ["maxWaitTime"] = ConvaiMcpResponses.NumberSchema(30)
                    },
                    "triggerId")),
                ["dryRun"] = ConvaiMcpResponses.BooleanSchema(true)
            },
            "characterInstanceId");
        [McpOutputSchema(ConfigureTool)] public static object ConfigureOutput() => StandardSchema();

        [McpTool(DiagnoseTool, "Diagnoses Unity-side Convai narrative character bindings, duplicate/orphaned sections, template keys, triggers, player filters, cached sync errors, and runtime trigger state. Content stays hidden by default.", "Diagnose Convai Narrative", Groups = new[] { "convai", "narrative", "validation" }, EnabledByDefault = true)]
        public static object Diagnose(JObject input) => Diagnose(input?.ToObject<DiagnoseNarrativeRequest>());

        public static object Diagnose(DiagnoseNarrativeRequest request)
        {
            request ??= new DiagnoseNarrativeRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Failure(ConvaiMcpResolvers.CharacterErrorCode, error);
            ConvaiNarrativeDesignManager[] managers = ConvaiObjectFind.All<ConvaiNarrativeDesignManager>(request.IncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude)
                .Where(value => value.gameObject.scene == SceneManager.GetActiveScene() && (value.CharacterComponent == character || value.CharacterComponent == null)).ToArray();
            ConvaiNarrativeDesignTrigger[] triggers = ConvaiObjectFind.All<ConvaiNarrativeDesignTrigger>(request.IncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude)
                .Where(value => value.gameObject.scene == SceneManager.GetActiveScene() && (value.CharacterComponent == character || value.CharacterComponent == null)).ToArray();
            var issues = new List<object>();
            if (managers.Length == 0) issues.Add(Issue("NARRATIVE_MANAGER_MISSING", "Error", "ConvaiNarrativeDesignManager is missing.", character.name, ConvaiMcpEntityRef.ToToolId(character), true));
            if (managers.Length > 1) issues.Add(Issue("NARRATIVE_MANAGER_DUPLICATE", "Error", "Multiple narrative managers target this character.", managers.Length.ToString(), ConvaiMcpEntityRef.ToToolId(character), false));
            foreach (ConvaiNarrativeDesignManager manager in managers)
            {
                if (manager.CharacterComponent != character) issues.Add(Issue("NARRATIVE_CHARACTER_UNBOUND", "Error", "Narrative manager character binding is missing.", manager.name, ConvaiMcpEntityRef.ToToolId(manager), true));
                foreach (IGrouping<string, UnitySectionEventConfig> duplicate in manager.SectionConfigs.Where(section => section != null).GroupBy(section => section.SectionId ?? string.Empty, StringComparer.Ordinal).Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
                    issues.Add(Issue("NARRATIVE_SECTION_DUPLICATE", "Error", "Narrative section IDs must be unique and non-empty.", duplicate.Key, ConvaiMcpEntityRef.ToToolId(manager), false));
                if (manager.OrphanedSectionCount > 0) issues.Add(Issue("NARRATIVE_SECTION_ORPHANED", "Warning", "Narrative manager contains orphaned sections.", manager.OrphanedSectionCount.ToString(), ConvaiMcpEntityRef.ToToolId(manager), false));
                foreach (IGrouping<string, UnityTemplateKeyConfig> duplicate in manager.TemplateKeyConfigs.Where(key => key != null).GroupBy(key => key.Key ?? string.Empty, StringComparer.Ordinal).Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
                    issues.Add(Issue("NARRATIVE_TEMPLATE_KEY_DUPLICATE", "Error", "Template keys must be unique and non-empty.", duplicate.Key, ConvaiMcpEntityRef.ToToolId(manager), false));
                if (!string.IsNullOrEmpty(manager.LastFetchError)) issues.Add(Issue("NARRATIVE_SYNC_FAILED", "Warning", "Last narrative sync failed.", "Cached sync error present", ConvaiMcpEntityRef.ToToolId(manager), false));
            }
            foreach (ConvaiNarrativeDesignTrigger trigger in triggers)
            {
                if (trigger.CharacterComponent != character) issues.Add(Issue("NARRATIVE_TRIGGER_UNBOUND", "Error", "Narrative trigger character binding is missing.", trigger.name, ConvaiMcpEntityRef.ToToolId(trigger), true));
                if (string.IsNullOrWhiteSpace(trigger.TriggerId)) issues.Add(Issue("NARRATIVE_TRIGGER_ID_MISSING", "Error", "Narrative trigger ID is missing.", trigger.name, ConvaiMcpEntityRef.ToToolId(trigger), false));
                if (!UnityEditorInternal.InternalEditorUtility.tags.Contains(trigger.PlayerTag)) issues.Add(Issue("NARRATIVE_PLAYER_TAG_INVALID", "Error", "Narrative trigger references an undefined player tag.", trigger.PlayerTag, ConvaiMcpEntityRef.ToToolId(trigger), false));
                if (trigger.PlayerLayer.value == 0) issues.Add(Issue("NARRATIVE_PLAYER_LAYER_EMPTY", "Warning", "Narrative trigger player layer mask is empty.", trigger.name, ConvaiMcpEntityRef.ToToolId(trigger), true));
                if (trigger.ValidationWarnings.Count > 0) issues.Add(Issue("NARRATIVE_TRIGGER_VALIDATION", "Warning", "Narrative trigger reports validation warnings.", trigger.ValidationWarnings.Count.ToString(), ConvaiMcpEntityRef.ToToolId(trigger), false));
            }

            return Response(true, issues.Count == 0 ? "Convai narrative setup is ready." : "Convai narrative diagnosis found issues.", new
            {
                ready = issues.Count == 0,
                issues,
                contentIncluded = request.IncludeContent,
                managers = managers.Select(manager => new
                {
                    instanceId = ConvaiMcpEntityRef.ToToolId(manager),
                    sectionCount = manager.SectionConfigs.Count,
                    sectionIds = manager.SectionConfigs.Select(section => section.SectionId).ToArray(),
                    templateKeyCount = manager.TemplateKeyConfigs.Count,
                    templateKeys = manager.TemplateKeyConfigs.Select(key => key.Key).ToArray(),
                    templateValues = request.IncludeContent ? manager.TemplateKeyConfigs.Select(key => key.Value).ToArray() : Array.Empty<string>(),
                    manager.IsFetching,
                    lastSyncTime = manager.LastSyncTime,
                    hasSyncError = !string.IsNullOrEmpty(manager.LastFetchError)
                }).ToArray(),
                triggers = triggers.Select(trigger => new
                {
                    instanceId = ConvaiMcpEntityRef.ToToolId(trigger),
                    triggerId = trigger.TriggerId,
                    triggerName = request.IncludeContent ? trigger.TriggerName : string.Empty,
                    activationMode = trigger.ActivationMode.ToString(),
                    trigger.TriggerOnce,
                    trigger.PlayerTag,
                    playerLayerMask = trigger.PlayerLayer.value,
                    runtimeStatus = trigger.CurrentStatus.ToString(),
                    trigger.HasTriggered,
                    trigger.PlayerInZone
                }).ToArray(),
                backendWrites = false
            });
        }

        [McpSchema(DiagnoseTool)]
        public static object DiagnoseSchema() => ConvaiMcpResponses.ClosedObjectSchemaWithoutRequired(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerSchema(0),
                ["includeInactive"] = ConvaiMcpResponses.BooleanSchema(true),
                ["includeContent"] = ConvaiMcpResponses.BooleanSchema(false)
            });
        [McpOutputSchema(DiagnoseTool)] public static object DiagnoseOutput() => StandardSchema();

        private static bool ValidateInputs(ConfigureNarrativeRequest request, out string error)
        {
            error = string.Empty;
            if ((request.Sections ?? Array.Empty<NarrativeSectionInput>()).Any(section => section == null || string.IsNullOrWhiteSpace(section.SectionId))) error = "Every section requires sectionId.";
            else if ((request.TemplateKeys ?? Array.Empty<NarrativeTemplateKeyInput>()).Any(key => key == null || string.IsNullOrWhiteSpace(key.Key))) error = "Every template key requires key.";
            else if ((request.Triggers ?? Array.Empty<NarrativeTriggerInput>()).Any(trigger => trigger == null || string.IsNullOrWhiteSpace(trigger.TriggerId))) error = "Every trigger requires triggerId.";
            else if ((request.Sections ?? Array.Empty<NarrativeSectionInput>()).GroupBy(section => section.SectionId.Trim(), StringComparer.Ordinal).Any(group => group.Count() > 1)) error = "Section IDs must be unique within one request.";
            else if ((request.TemplateKeys ?? Array.Empty<NarrativeTemplateKeyInput>()).GroupBy(key => key.Key.Trim(), StringComparer.Ordinal).Any(group => group.Count() > 1)) error = "Template keys must be unique within one request.";
            else if ((request.Triggers ?? Array.Empty<NarrativeTriggerInput>()).GroupBy(trigger => (trigger.HostInstanceId, trigger.TriggerId.Trim())).Any(group => group.Count() > 1)) error = "Trigger host and ID pairs must be unique within one request.";
            return string.IsNullOrEmpty(error);
        }

        private static void CollectManagerChanges(ConvaiNarrativeDesignManager manager, ConfigureNarrativeRequest request, ICollection<string> changes)
        {
            foreach (NarrativeSectionInput input in request.Sections ?? Array.Empty<NarrativeSectionInput>())
            {
                string sectionId = input.SectionId.Trim();
                string sectionName = input.SectionName ?? string.Empty;
                UnitySectionEventConfig existing = manager?.SectionConfigs.FirstOrDefault(section => section.SectionId == sectionId);
                if (existing == null || existing.SectionName != sectionName || existing.IsOrphaned) changes.Add($"Upsert narrative section '{sectionId}'");
            }
            foreach (NarrativeTemplateKeyInput input in request.TemplateKeys ?? Array.Empty<NarrativeTemplateKeyInput>())
            {
                string key = input.Key.Trim();
                string value = input.Value ?? string.Empty;
                UnityTemplateKeyConfig existing = manager?.TemplateKeyConfigs.FirstOrDefault(candidate => candidate.Key == key);
                if (existing == null || existing.Value != value) changes.Add($"Upsert narrative template key '{key}'");
            }
        }

        private static void ConfigureManager(ConvaiNarrativeDesignManager manager, ConvaiCharacter character, ConfigureNarrativeRequest request)
        {
            Undo.RecordObject(manager, "Configure Convai Narrative Manager");
            var serialized = new SerializedObject(manager);
            Require(serialized, "_characterComponent").objectReferenceValue = character;
            SerializedProperty sections = Require(serialized, "_sectionConfigs");
            foreach (NarrativeSectionInput input in request.Sections ?? Array.Empty<NarrativeSectionInput>())
            {
                string sectionId = input.SectionId.Trim();
                int index = FindArrayElement(sections, "_sectionId", sectionId);
                if (index < 0) { index = sections.arraySize; sections.InsertArrayElementAtIndex(index); ClearSection(sections.GetArrayElementAtIndex(index)); }
                SerializedProperty element = sections.GetArrayElementAtIndex(index);
                Require(element, "_sectionId").stringValue = sectionId;
                Require(element, "_sectionName").stringValue = input.SectionName ?? string.Empty;
                Require(element, "_isOrphaned").boolValue = false;
            }
            SerializedProperty keys = Require(serialized, "_templateKeys");
            foreach (NarrativeTemplateKeyInput input in request.TemplateKeys ?? Array.Empty<NarrativeTemplateKeyInput>())
            {
                string key = input.Key.Trim();
                int index = FindArrayElement(keys, "_key", key);
                if (index < 0) { index = keys.arraySize; keys.InsertArrayElementAtIndex(index); }
                SerializedProperty element = keys.GetArrayElementAtIndex(index);
                Require(element, "_key").stringValue = key;
                Require(element, "_value").stringValue = input.Value ?? string.Empty;
            }
            serialized.ApplyModifiedProperties();
        }

        private static void ClearSection(SerializedProperty element)
        {
            Require(element, "_sectionId").stringValue = string.Empty;
            Require(element, "_sectionName").stringValue = string.Empty;
            Require(element, "_isOrphaned").boolValue = false;
            Require(element, "_onSectionStart").FindPropertyRelative("m_PersistentCalls.m_Calls").ClearArray();
            Require(element, "_onSectionEnd").FindPropertyRelative("m_PersistentCalls.m_Calls").ClearArray();
        }

        private static void ConfigureTrigger(ConvaiNarrativeDesignTrigger trigger, ConvaiCharacter character, NarrativeTriggerInput input)
        {
            Undo.RecordObject(trigger, "Configure Convai Narrative Trigger");
            var serialized = new SerializedObject(trigger);
            Require(serialized, "_characterComponent").objectReferenceValue = character;
            Require(serialized, "_autoFindCharacter").boolValue = false;
            Require(serialized, "_triggerId").stringValue = input.TriggerId.Trim();
            Require(serialized, "_triggerName").stringValue = input.TriggerName ?? string.Empty;
            Require(serialized, "_activationMode").enumValueIndex = (int)input.ActivationMode;
            Require(serialized, "_proximityRadius").floatValue = Mathf.Max(0.1f, input.ProximityRadius);
            Require(serialized, "_timeDelay").floatValue = Mathf.Max(0f, input.TimeDelay);
            Require(serialized, "_triggerOnce").boolValue = input.TriggerOnce;
            Require(serialized, "_playerLayer").intValue = input.PlayerLayerMask;
            Require(serialized, "_playerTag").stringValue = input.PlayerTag ?? "Player";
            Require(serialized, "_queueUntilReady").boolValue = input.QueueUntilReady;
            Require(serialized, "_maxWaitTime").floatValue = Mathf.Max(0f, input.MaxWaitTime);
            serialized.ApplyModifiedProperties();
        }

        private static bool TriggerMatches(ConvaiNarrativeDesignTrigger trigger, NarrativeTriggerInput input, ConvaiCharacter character) =>
            trigger.CharacterComponent == character && trigger.TriggerId == input.TriggerId.Trim() && trigger.TriggerName == (input.TriggerName ?? string.Empty) &&
            trigger.ActivationMode == input.ActivationMode && Mathf.Approximately(trigger.ProximityRadius, Mathf.Max(0.1f, input.ProximityRadius)) &&
            Mathf.Approximately(trigger.TimeDelay, Mathf.Max(0f, input.TimeDelay)) && trigger.TriggerOnce == input.TriggerOnce &&
            trigger.PlayerLayer.value == input.PlayerLayerMask && trigger.PlayerTag == (input.PlayerTag ?? "Player") &&
            ReadBool(trigger, "_queueUntilReady") == input.QueueUntilReady &&
            Mathf.Approximately(ReadFloat(trigger, "_maxWaitTime"), Mathf.Max(0f, input.MaxWaitTime));

        private static ConvaiNarrativeDesignTrigger FindTrigger(GameObject host, string triggerId) =>
            host.GetComponents<ConvaiNarrativeDesignTrigger>()
                .FirstOrDefault(value => string.Equals(value.TriggerId, triggerId?.Trim(), StringComparison.Ordinal));

        private static int FindArrayElement(SerializedProperty array, string field, string value)
        {
            for (int i = 0; i < array.arraySize; i++) if (Require(array.GetArrayElementAtIndex(i), field).stringValue == value) return i;
            return -1;
        }

        private static SerializedProperty Require(SerializedObject serialized, string name) => serialized.FindProperty(name) ?? throw new InvalidOperationException($"Missing serialized property {name} on {serialized.targetObject.GetType().Name}.");
        private static SerializedProperty Require(SerializedProperty property, string name) => property.FindPropertyRelative(name) ?? throw new InvalidOperationException($"Missing serialized property {name}.");
        private static bool ReadBool(UnityEngine.Object target, string name) => Require(new SerializedObject(target), name).boolValue;
        private static float ReadFloat(UnityEngine.Object target, string name) => Require(new SerializedObject(target), name).floatValue;

        private static object Issue(string code, string severity, string message, string evidence, long id, bool fixable) =>
            ConvaiMcpResponses.Issue(code, severity, message, evidence, id, fixable, ConfigureTool,
                new { characterInstanceId = ResolveSuggestedCharacterId(id), dryRun = true });
        private static long ResolveSuggestedCharacterId(long affectedId)
        {
            UnityEngine.Object affected = ConvaiMcpEntityRef.Resolve(affectedId);
            if (affected is ConvaiCharacter direct) return ConvaiMcpEntityRef.ToToolId(direct);
            if (affected is Component component)
            {
                if (component is ConvaiNarrativeDesignManager manager && manager.CharacterComponent is ConvaiCharacter managerCharacter)
                    return ConvaiMcpEntityRef.ToToolId(managerCharacter);
                if (component is ConvaiNarrativeDesignTrigger trigger && trigger.CharacterComponent is ConvaiCharacter triggerCharacter)
                    return ConvaiMcpEntityRef.ToToolId(triggerCharacter);
                ConvaiCharacter character = component.GetComponentInParent<ConvaiCharacter>(true) ?? component.GetComponentInChildren<ConvaiCharacter>(true);
                if (character != null) return ConvaiMcpEntityRef.ToToolId(character);
            }
            return 0;
        }
        private static object Failure(string code, string message) => ConvaiMcpResponses.Failure(code, message);
        private static object Response(bool success, string message, object data) =>
            ConvaiMcpResponses.Envelope(success, message, data);
        private static object StandardSchema() => ConvaiMcpResponses.StandardResponseSchema(true);
    }

    public static class ConvaiNarrativeAssistantTools
    {
        [AgentTool("Configure Unity-side Convai narrative mappings, keys, and triggers without backend writes.", "Convai.ConfigureNarrative")]
        public static object ConfigureNarrative(long characterInstanceId, long managerHostInstanceId = 0, NarrativeSectionInput[] sections = null, NarrativeTemplateKeyInput[] templateKeys = null, NarrativeTriggerInput[] triggers = null, bool dryRun = true) => ConvaiNarrativeMcpTools.Configure(new ConfigureNarrativeRequest { CharacterInstanceId = characterInstanceId, ManagerHostInstanceId = managerHostInstanceId, Sections = sections ?? Array.Empty<NarrativeSectionInput>(), TemplateKeys = templateKeys ?? Array.Empty<NarrativeTemplateKeyInput>(), Triggers = triggers ?? Array.Empty<NarrativeTriggerInput>(), DryRun = dryRun });
        [AgentTool("Diagnose Unity-side Convai narrative configuration and sanitized runtime state.", "Convai.DiagnoseNarrative")]
        public static object DiagnoseNarrative(long characterInstanceId = 0, bool includeInactive = true, bool includeContent = false) => ConvaiNarrativeMcpTools.Diagnose(new DiagnoseNarrativeRequest { CharacterInstanceId = characterInstanceId, IncludeInactive = includeInactive, IncludeContent = includeContent });
    }
}
