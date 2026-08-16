#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.LipSync.Editor
{
    /// <summary>Editor-only authoring operations for lip-sync map assets.</summary>
    internal static class ConvaiLipSyncMapAuthoring
    {
        private static readonly string[] BlendshapeNamePrefixes =
        {
            "CTRL_expressions_", "blendShape.", "bs_", "BS_", "Shape_", "CC_Base_", "CC_Game_", "RL_"
        };

        internal static void ClearMappings(this ConvaiLipSyncMapAsset mapping)
        {
            if (mapping == null) return;

            var serialized = new SerializedObject(mapping);
            serialized.FindProperty("_mappings").ClearArray();
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(mapping);
        }

        internal static void InitializeWithDefaults(this ConvaiLipSyncMapAsset mapping)
        {
            if (mapping == null) return;

            IReadOnlyList<string> sourceNames =
                LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(mapping.TargetProfileId);
            WriteMappings(mapping, sourceNames, null, BlendshapeMatchMode.Exact);
        }

        internal static void AutoDetectFromMeshes(
            this ConvaiLipSyncMapAsset mapping,
            IEnumerable<SkinnedMeshRenderer> meshes,
            BlendshapeMatchMode mode = BlendshapeMatchMode.Contains)
        {
            if (mapping == null || meshes == null) return;

            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var meshNames = new List<string>();
            foreach (SkinnedMeshRenderer renderer in meshes)
            {
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null) continue;

                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (uniqueNames.Add(name)) meshNames.Add(name);
                }
            }

            if (meshNames.Count == 0) return;

            IReadOnlyList<string> sourceNames =
                LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(mapping.TargetProfileId);
            if (sourceNames.Count == 0) sourceNames = meshNames;
            WriteMappings(mapping, sourceNames, meshNames, mode);
        }

        private static void WriteMappings(
            ConvaiLipSyncMapAsset mapping,
            IReadOnlyList<string> sourceNames,
            List<string> meshNames,
            BlendshapeMatchMode mode)
        {
            var serialized = new SerializedObject(mapping);
            SerializedProperty mappings = serialized.FindProperty("_mappings");
            mappings.ClearArray();

            for (int i = 0; i < sourceNames.Count; i++)
            {
                string source = sourceNames[i];
                string match = meshNames != null ? FindBestMatch(source, meshNames, mode) : source;
                bool enabled = !string.IsNullOrEmpty(match);

                mappings.InsertArrayElementAtIndex(i);
                SerializedProperty entry = mappings.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("sourceBlendshape").stringValue = source;
                entry.FindPropertyRelative("enabled").boolValue = enabled;
                entry.FindPropertyRelative("multiplier").floatValue = 1f;
                entry.FindPropertyRelative("offset").floatValue = 0f;
                entry.FindPropertyRelative("curveExponent").floatValue = 1f;
                entry.FindPropertyRelative("useOverrideValue").boolValue = false;
                entry.FindPropertyRelative("overrideValue").floatValue = 0f;
                entry.FindPropertyRelative("ignoreGlobalModifiers").boolValue = false;
                entry.FindPropertyRelative("clampMinValue").floatValue = 0f;
                entry.FindPropertyRelative("clampMaxValue").floatValue = 1f;

                SerializedProperty targets = entry.FindPropertyRelative("targetNames");
                targets.ClearArray();
                if (enabled)
                {
                    targets.InsertArrayElementAtIndex(0);
                    targets.GetArrayElementAtIndex(0).stringValue = match;
                }
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(mapping);
        }

        private static string FindBestMatch(string source, List<string> meshNames, BlendshapeMatchMode mode)
        {
            for (int i = 0; i < meshNames.Count; i++)
                if (string.Equals(meshNames[i], source, StringComparison.OrdinalIgnoreCase))
                    return meshNames[i];

            if (mode == BlendshapeMatchMode.Exact) return null;

            for (int i = 0; i < meshNames.Count; i++)
                if (meshNames[i].IndexOf(source, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    source.IndexOf(meshNames[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return meshNames[i];

            if (mode == BlendshapeMatchMode.Contains) return null;

            string cleanSource = CleanName(source);
            for (int i = 0; i < meshNames.Count; i++)
                if (string.Equals(CleanName(meshNames[i]), cleanSource, StringComparison.OrdinalIgnoreCase))
                    return meshNames[i];

            return null;
        }

        private static string CleanName(string name)
        {
            for (int i = 0; i < BlendshapeNamePrefixes.Length; i++)
                if (name.StartsWith(BlendshapeNamePrefixes[i], StringComparison.OrdinalIgnoreCase))
                    return name.Substring(BlendshapeNamePrefixes[i].Length);

            return name;
        }
    }
}
#endif
