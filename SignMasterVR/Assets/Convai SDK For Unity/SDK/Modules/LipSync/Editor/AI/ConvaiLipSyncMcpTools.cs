using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Editor.AI;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using Convai.Runtime.Components;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Modules.LipSync.Editor.AI
{
    public sealed class ConfigureLipSyncRequest
    {
        public long CharacterInstanceId { get; set; }
        public long[] MeshInstanceIds { get; set; } = Array.Empty<long>();
        public string Profile { get; set; } = "Auto";
        public string MappingAssetPath { get; set; } = string.Empty;
        public LipSyncLatencyMode LatencyMode { get; set; } = LipSyncLatencyMode.Balanced;
        public bool DryRun { get; set; } = true;
    }
    public sealed class DiagnoseLipSyncRequest { public long CharacterInstanceId { get; set; } public bool IncludeRuntimeMetrics { get; set; } = true; }

    public static class ConvaiLipSyncMcpTools
    {
        private const string ConfigureTool = "Convai.ConfigureLipSync";
        private const string DiagnoseTool = "Convai.DiagnoseLipSync";

        [McpTool(ConfigureTool, "Previews or configures Convai lip sync using existing meshes, shipped profiles, and optional existing maps. Never creates or mutates assets.", "Configure Convai Lip Sync", Groups = new[] { "convai", "lip-sync" }, EnabledByDefault = true)]
        public static object Configure(JObject input) => Configure(input?.ToObject<ConfigureLipSyncRequest>());
        public static object Configure(ConfigureLipSyncRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false, "Lip-sync authoring is available only in Edit Mode.", new { code = "PLAY_MODE_ACTIVE" });
            request ??= new ConfigureLipSyncRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });
            SkinnedMeshRenderer[] meshes = ResolveMeshes(character, request.MeshInstanceIds, out error);
            if (!string.IsNullOrEmpty(error)) return Response(false, error, new { code = "INVALID_MESH" });
            string profile = ResolveProfile(request.Profile, meshes, out error);
            if (!string.IsNullOrEmpty(error)) return Response(false, error, new { code = "PROFILE_AMBIGUOUS", requiredInputs = new[] { "profile" } });
            ConvaiLipSyncMapAsset map = string.IsNullOrWhiteSpace(request.MappingAssetPath) ? null : AssetDatabase.LoadAssetAtPath<ConvaiLipSyncMapAsset>(request.MappingAssetPath);
            if (!string.IsNullOrWhiteSpace(request.MappingAssetPath) && map == null) return Response(false, "mappingAssetPath must reference ConvaiLipSyncMapAsset.", new { code = "INVALID_MAPPING" });
            ConvaiLipSyncComponent component = character.GetComponent<ConvaiLipSyncComponent>();
            var changes = new List<string>();
            if (component == null) changes.Add("Add ConvaiLipSyncComponent");
            if (component == null || !string.Equals(component.LockedProfile.Value, profile, StringComparison.Ordinal))
                changes.Add($"Set profile '{profile}'");
            if (component == null || component.Mapping != map)
                changes.Add("Assign lip-sync mapping");
            if (component == null || !MeshesMatch(component.TargetMeshes, meshes))
                changes.Add($"Assign {meshes.Length} target mesh(es)");
            if (component == null || ReadLatencyMode(component) != request.LatencyMode)
                changes.Add($"Set latency {request.LatencyMode}");
            if (request.DryRun) return Authoring(true, meshes.Length > 0, changes, meshes.Length == 0 ? new[] { "meshInstanceIds" } : Array.Empty<string>(), character);
            if (changes.Count == 0) return Authoring(false, meshes.Length > 0, changes, meshes.Length == 0 ? new[] { "meshInstanceIds" } : Array.Empty<string>(), character);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Configure Convai Lip Sync");
            try
            {
                component ??= Undo.AddComponent<ConvaiLipSyncComponent>(character.gameObject);
                Undo.RecordObject(component, "Configure Convai Lip Sync");
                var serialized = new SerializedObject(component);
                serialized.FindProperty("_lockedProfileId").stringValue = profile;
                serialized.FindProperty("_mapping").objectReferenceValue = map;
                serialized.FindProperty("_latencyMode").enumValueIndex = (int)request.LatencyMode;
                SerializedProperty targets = serialized.FindProperty("_targetMeshes"); targets.arraySize = meshes.Length;
                for (int i = 0; i < meshes.Length; i++) targets.GetArrayElementAtIndex(i).objectReferenceValue = meshes[i];
                serialized.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(character.gameObject.scene); Undo.CollapseUndoOperations(group);
            }
            catch (Exception exception) { Undo.RevertAllDownToGroup(group); return Response(false, exception.Message, new { code = "AUTHORING_FAILED" }); }
            return Authoring(false, meshes.Length > 0, changes, meshes.Length == 0 ? new[] { "meshInstanceIds" } : Array.Empty<string>(), character);
        }
        [McpSchema(ConfigureTool)]
        public static object ConfigureSchema() => ConvaiMcpResponses.ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerSchema(),
                ["meshInstanceIds"] = ConvaiMcpResponses.ArraySchema(ConvaiMcpResponses.IntegerSchema()),
                ["profile"] = ConvaiMcpResponses.StringSchema("Auto"),
                ["mappingAssetPath"] = ConvaiMcpResponses.StringSchema(),
                ["latencyMode"] = ConvaiMcpResponses.StringEnumSchema(
                    new[] { "Balanced", "UltraLowLatency", "NetworkSafe", "Custom" }, "Balanced"),
                ["dryRun"] = ConvaiMcpResponses.BooleanSchema(true)
            },
            "characterInstanceId");
        [McpOutputSchema(ConfigureTool)] public static object ConfigureOutput() => StandardSchema();

        [McpTool(DiagnoseTool, "Diagnoses Convai lip-sync component, meshes, blendshape compatibility, mapping, profile, and sanitized runtime buffer state.", "Diagnose Convai Lip Sync", Groups = new[] { "convai", "lip-sync", "validation" }, EnabledByDefault = true)]
        public static object Diagnose(JObject input) => Diagnose(input?.ToObject<DiagnoseLipSyncRequest>());
        public static object Diagnose(DiagnoseLipSyncRequest request)
        {
            request ??= new DiagnoseLipSyncRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });
            ConvaiLipSyncComponent component = character.GetComponent<ConvaiLipSyncComponent>(); var issues = new List<object>();
            if (component == null) issues.Add(Issue("LIPSYNC_COMPONENT_MISSING", "Error", "ConvaiLipSyncComponent is missing.", character.name, ConvaiMcpEntityRef.ToToolId(character), true));
            else if (component.TargetMeshes.Count == 0) issues.Add(Issue("LIPSYNC_MESHES_MISSING", "Error", "No target meshes configured.", component.LockedProfile.Value, ConvaiMcpEntityRef.ToToolId(component), true));
            return Response(true, issues.Count == 0 ? "Convai lip sync is ready." : "Convai lip-sync diagnosis found issues.", new { ready = issues.Count == 0, issues, configuration = component == null ? null : new { componentInstanceId = ConvaiMcpEntityRef.ToToolId(component), profile = component.LockedProfile.Value, mapping = component.Mapping != null ? AssetDatabase.GetAssetPath(component.Mapping) : string.Empty, meshCount = component.TargetMeshes.Count }, runtime = component == null || !request.IncludeRuntimeMetrics ? null : new { isPlaying = component.IsPlaying, isTalking = component.IsTalking, isFadingOut = component.IsFadingOut, engineState = component.EngineState.ToString(), bufferedSeconds = component.GetTotalBufferedDuration(), streamSeconds = component.GetTotalStreamDuration(), headroomSeconds = component.GetHeadroom() } });
        }
        [McpSchema(DiagnoseTool)]
        public static object DiagnoseSchema() => ConvaiMcpResponses.ClosedObjectSchemaWithoutRequired(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerSchema(0),
                ["includeRuntimeMetrics"] = ConvaiMcpResponses.BooleanSchema(true)
            });
        [McpOutputSchema(DiagnoseTool)] public static object DiagnoseOutput() => StandardSchema();

        private static SkinnedMeshRenderer[] ResolveMeshes(ConvaiCharacter character, long[] ids, out string error)
        {
            error = string.Empty;
            if (ids == null || ids.Length == 0) return character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var result = new List<SkinnedMeshRenderer>();
            foreach (long id in ids)
            {
                ConvaiMcpEntityRef.TryResolve(id, out SkinnedMeshRenderer mesh);
                if (mesh == null || mesh.gameObject.scene != character.gameObject.scene)
                {
                    error = $"Invalid mesh instance ID {id}.";
                    return Array.Empty<SkinnedMeshRenderer>();
                }

                result.Add(mesh);
            }

            return result.Distinct().ToArray();
        }

        private static bool MeshesMatch(IReadOnlyList<SkinnedMeshRenderer> configured, IReadOnlyList<SkinnedMeshRenderer> requested) { if (configured == null || configured.Count != requested.Count) return false; for (int i = 0; i < configured.Count; i++) if (configured[i] != requested[i]) return false; return true; }
        private static LipSyncLatencyMode ReadLatencyMode(ConvaiLipSyncComponent component) { var serialized = new SerializedObject(component); return (LipSyncLatencyMode)serialized.FindProperty("_latencyMode").enumValueIndex; }
        private static string ResolveProfile(string requested, SkinnedMeshRenderer[] meshes, out string error) { error = string.Empty; if (!string.IsNullOrWhiteSpace(requested) && !string.Equals(requested, "Auto", StringComparison.OrdinalIgnoreCase)) { string normalized = LipSyncProfileId.Normalize(requested); if (normalized is LipSyncProfileId.ARKitValue or LipSyncProfileId.Cc4ExtendedValue or LipSyncProfileId.MetaHumanValue) return normalized; error = "Profile must be Auto, arkit, cc4_extended, or metahuman."; return string.Empty; } string[] ids = { LipSyncProfileId.ARKitValue, LipSyncProfileId.Cc4ExtendedValue, LipSyncProfileId.MetaHumanValue }; int best = 0, second = 0; string winner = string.Empty; var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (SkinnedMeshRenderer renderer in meshes) if (renderer?.sharedMesh != null) for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++) available.Add(renderer.sharedMesh.GetBlendShapeName(i)); foreach (string id in ids) { int score = LipSyncProfileCatalog.GetSourceBlendshapeNamesOrEmpty(new LipSyncProfileId(id)).Count(available.Contains); if (score > best) { second = best; best = score; winner = id; } else if (score > second) second = score; } if (best < 5 || best == second) { error = "Could not uniquely detect a lip-sync profile from target blendshapes."; return string.Empty; } return winner; }
        private static object Authoring(bool dryRun, bool complete, object changes, object required, Component target) => Response(true, dryRun ? "Previewed Convai lip-sync configuration." : "Configured Convai lip sync.", new { dryRun, complete, changes, blockedSteps = complete ? Array.Empty<string>() : required, requiredInputs = required, warnings = Array.Empty<string>(), affectedInstanceId = ConvaiMcpEntityRef.ToToolId(target), sceneDirty = SceneManager.GetActiveScene().isDirty, sceneSaved = false });
        private static object Issue(string code, string severity, string message, string evidence, long id, bool fixable) =>
            ConvaiMcpResponses.Issue(code, severity, message, evidence, id, fixable, ConfigureTool,
                new { characterInstanceId = id, dryRun = true });
        private static object Response(bool success, string message, object data) =>
            ConvaiMcpResponses.Envelope(success, message, data);
        private static object StandardSchema() => ConvaiMcpResponses.StandardResponseSchema(true);
    }

    public static class ConvaiLipSyncAssistantTools
    {
        [AgentTool("Configure Convai lip sync with shipped profiles and existing meshes/maps.", "Convai.ConfigureLipSync")]
        public static object ConfigureLipSync(long characterInstanceId, long[] meshInstanceIds = null, string profile = "Auto", string mappingAssetPath = "", LipSyncLatencyMode latencyMode = LipSyncLatencyMode.Balanced, bool dryRun = true) => ConvaiLipSyncMcpTools.Configure(new ConfigureLipSyncRequest { CharacterInstanceId = characterInstanceId, MeshInstanceIds = meshInstanceIds ?? Array.Empty<long>(), Profile = profile, MappingAssetPath = mappingAssetPath, LatencyMode = latencyMode, DryRun = dryRun });
        [AgentTool("Diagnose Convai lip-sync setup and runtime buffer state.", "Convai.DiagnoseLipSync")]
        public static object DiagnoseLipSync(long characterInstanceId = 0, bool includeRuntimeMetrics = true) => ConvaiLipSyncMcpTools.Diagnose(new DiagnoseLipSyncRequest { CharacterInstanceId = characterInstanceId, IncludeRuntimeMetrics = includeRuntimeMetrics });
    }
}
