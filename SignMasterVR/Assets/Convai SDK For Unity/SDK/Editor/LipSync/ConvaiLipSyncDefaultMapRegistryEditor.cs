using System;
using System.Collections.Generic;
using Convai.Domain.Models.LipSync;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.LipSync.Profiles;
using Convai.Editor.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.LipSync.Editor
{
    [CustomEditor(typeof(ConvaiLipSyncDefaultMapRegistry))]
    internal sealed class ConvaiLipSyncDefaultMapRegistryEditor : ConvaiInspectorEditor
    {
        private static readonly GUIContent TotalLabel = new("Total");
        private static readonly GUIContent ProfilesLabel = new("Profiles");
        private static readonly GUIContent MappedLabel = new("Mapped");
        private static readonly GUIContent CoverageLabel = new("Coverage");

        private static readonly GUIContent AddMissingProfilesButton = new("Add Missing Profiles");
        private static readonly GUIContent RemoveEmptyButton = new("Remove Empty");
        private static readonly GUIContent SortByProfileButton = new("Sort by Profile");
        private static readonly GUIContent OpenButton = new("Open");
        private static readonly GUIContent FallbackIdLabel = new("Fallback ID");

        private readonly GUIContent _issuesChipContent = new(string.Empty);

        private ReorderableList _entriesList;
        private SerializedProperty _entriesProp;

        private ConvaiLipSyncDefaultMapRegistry Registry => (ConvaiLipSyncDefaultMapRegistry)target;

        protected override string Title => "Lip Sync Default Map Registry";

        protected override GUIContent StatusChip
        {
            get
            {
                int count = target != null ? Registry.ValidationIssues.Count : 0;
                if (count <= 0) return null;

                _issuesChipContent.text = $"{count} Issues";
                return _issuesChipContent;
            }
        }

        protected override Color StatusChipTint => Theme.StatusWarn;

        protected override void OnEnable()
        {
            base.OnEnable();

            _entriesProp = serializedObject.FindProperty("_entries");
            InitializeList();
        }

        protected override void DrawHeaderExtras()
        {
            IReadOnlyList<ConvaiLipSyncProfile> catalogProfiles = LipSyncProfileCatalog.GetProfiles();
            int totalEntries = _entriesProp != null ? _entriesProp.arraySize : 0;
            int catalogProfileCount = catalogProfiles?.Count ?? 0;
            int coveredProfiles = CountCoveredCatalogProfiles(catalogProfiles);
            float coverage = catalogProfileCount > 0 ? coveredProfiles / (float)catalogProfileCount * 100f : 0f;

            EditorGUILayout.BeginHorizontal();
            StatTile(TotalLabel, totalEntries.ToString(), Theme.Accent);
            StatTile(ProfilesLabel, catalogProfileCount.ToString(), Theme.Accent);
            StatTile(MappedLabel, coveredProfiles.ToString(), Theme.Accent);
            StatTile(CoverageLabel, $"{coverage:0.#}%", Theme.Accent);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        protected override void DrawBody()
        {
            DrawToolbar();
            EditorGUILayout.Space(4f);

            if (_entriesList != null) _entriesList.DoLayoutList();

            bool applied = serializedObject.ApplyModifiedProperties();
            if (applied) EditorUtility.SetDirty(target);

            DrawValidationIssues();
        }

        private void InitializeList()
        {
            if (_entriesProp == null) return;

            _entriesList = new ReorderableList(serializedObject, _entriesProp, true, true, true, true)
            {
                elementHeight = (EditorGUIUtility.singleLineHeight * 2f) + 10f
            };

            _entriesList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "Default Map Entries");
            };

            _entriesList.drawElementCallback = (rect, index, _, _) =>
            {
                SerializedProperty entry = _entriesProp.GetArrayElementAtIndex(index);
                SerializedProperty profileIdProp = entry.FindPropertyRelative("_profileId");
                SerializedProperty mapProp = entry.FindPropertyRelative("_defaultMap");

                var map = mapProp.objectReferenceValue as ConvaiLipSyncMapAsset;
                LipSyncProfileId mapProfile = map != null ? map.TargetProfileId : default;
                string fallbackProfile = LipSyncProfileId.Normalize(profileIdProp.stringValue);
                bool hasAuthoritativeProfile = map != null && mapProfile.IsValid;
                LipSyncProfileId resolvedProfile = hasAuthoritativeProfile
                    ? mapProfile
                    : new LipSyncProfileId(fallbackProfile);
                string displayName = ResolveDisplayProfileName(resolvedProfile);

                Rect mapRect = new(rect.x, rect.y + 2f, rect.width - 42f, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(mapRect, mapProp, GUIContent.none);

                Rect openRect = new(rect.xMax - 42f, rect.y + 2f, 40f, EditorGUIUtility.singleLineHeight);
                using (new EditorGUI.DisabledScope(map == null))
                {
                    if (GUI.Button(openRect, OpenButton))
                        Selection.activeObject = map;
                }

                Rect profileRect = new(rect.x, rect.y + EditorGUIUtility.singleLineHeight + 6f, rect.width,
                    EditorGUIUtility.singleLineHeight);
                if (hasAuthoritativeProfile)
                    EditorGUI.LabelField(profileRect, $"Profile: {displayName}");
                else
                {
                    Rect fallbackFieldRect = new(rect.x, profileRect.y, rect.width * 0.52f, profileRect.height);
                    Rect previewRect = new(rect.x + (rect.width * 0.54f), profileRect.y, rect.width * 0.46f,
                        profileRect.height);
                    EditorGUI.PropertyField(
                        fallbackFieldRect,
                        profileIdProp,
                        FallbackIdLabel);
                    EditorGUI.LabelField(previewRect, $"Profile: {displayName}");
                }
            };
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(AddMissingProfilesButton, GUILayout.Height(22f))) AddMissingProfilesFromCatalog();

            if (GUILayout.Button(RemoveEmptyButton, GUILayout.Height(22f))) RemoveEmptyEntries();

            if (GUILayout.Button(SortByProfileButton, GUILayout.Height(22f))) SortEntriesByResolvedProfile();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawValidationIssues()
        {
            IReadOnlyList<string> issues = Registry.ValidationIssues;
            if (issues == null || issues.Count == 0) return;

            WarningBox("Validation Issues Detected", "Resolve these before shipping.");
            for (int i = 0; i < issues.Count; i++)
                EditorGUILayout.LabelField($"- {issues[i]}", ConvaiEditorStyles.CaptionWrapped);
        }

        private int CountCoveredCatalogProfiles(IReadOnlyList<ConvaiLipSyncProfile> catalogProfiles)
        {
            if (catalogProfiles == null || catalogProfiles.Count == 0) return 0;

            var registry = Registry;
            int covered = 0;
            for (int i = 0; i < catalogProfiles.Count; i++)
            {
                ConvaiLipSyncProfile profile = catalogProfiles[i];
                if (profile == null) continue;

                if (registry.GetForProfile(profile.ProfileId) != null) covered++;
            }

            return covered;
        }

        private static string ResolveDisplayProfileName(LipSyncProfileId profileId)
        {
            if (LipSyncProfileCatalog.TryGetProfile(profileId, out ConvaiLipSyncProfile profile))
                return profile.DisplayName;

            return profileId.IsValid ? profileId.Value : "(Unresolved)";
        }

        private void AddMissingProfilesFromCatalog()
        {
            IReadOnlyList<ConvaiLipSyncProfile> profiles = LipSyncProfileCatalog.GetProfiles();
            if (profiles == null || profiles.Count == 0 || _entriesProp == null) return;

            HashSet<string> existingProfiles = new(StringComparer.Ordinal);
            for (int i = 0; i < _entriesProp.arraySize; i++)
            {
                SerializedProperty entry = _entriesProp.GetArrayElementAtIndex(i);
                SerializedProperty mapProp = entry.FindPropertyRelative("_defaultMap");
                SerializedProperty profileIdProp = entry.FindPropertyRelative("_profileId");
                var map = mapProp.objectReferenceValue as ConvaiLipSyncMapAsset;
                LipSyncProfileId resolved = map != null && map.TargetProfileId.IsValid
                    ? map.TargetProfileId
                    : new LipSyncProfileId(profileIdProp.stringValue);
                if (resolved.IsValid) existingProfiles.Add(resolved.Value);
            }

            int added = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                ConvaiLipSyncProfile profile = profiles[i];
                if (profile == null || !profile.ProfileId.IsValid ||
                    existingProfiles.Contains(profile.ProfileId.Value)) continue;

                int index = _entriesProp.arraySize;
                _entriesProp.InsertArrayElementAtIndex(index);
                SerializedProperty entry = _entriesProp.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("_profileId").stringValue = profile.ProfileId.Value;
                entry.FindPropertyRelative("_defaultMap").objectReferenceValue = null;
                added++;
            }

            if (added > 0)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private void RemoveEmptyEntries()
        {
            if (_entriesProp == null) return;

            bool removed = false;
            for (int i = _entriesProp.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entry = _entriesProp.GetArrayElementAtIndex(i);
                SerializedProperty mapProp = entry.FindPropertyRelative("_defaultMap");
                SerializedProperty profileIdProp = entry.FindPropertyRelative("_profileId");
                if (mapProp.objectReferenceValue != null) continue;

                if (!string.IsNullOrWhiteSpace(profileIdProp.stringValue)) continue;

                _entriesProp.DeleteArrayElementAtIndex(i);
                removed = true;
            }

            if (removed)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private void SortEntriesByResolvedProfile()
        {
            if (_entriesProp == null || _entriesProp.arraySize <= 1) return;

            List<EntrySnapshot> snapshots = new(_entriesProp.arraySize);
            for (int i = 0; i < _entriesProp.arraySize; i++)
            {
                SerializedProperty entry = _entriesProp.GetArrayElementAtIndex(i);
                SerializedProperty profileIdProp = entry.FindPropertyRelative("_profileId");
                SerializedProperty mapProp = entry.FindPropertyRelative("_defaultMap");
                var map = mapProp.objectReferenceValue as ConvaiLipSyncMapAsset;
                string resolvedProfile = map != null && map.TargetProfileId.IsValid
                    ? map.TargetProfileId.Value
                    : LipSyncProfileId.Normalize(profileIdProp.stringValue);
                snapshots.Add(new EntrySnapshot(resolvedProfile, profileIdProp.stringValue, map));
            }

            snapshots.Sort((left, right) =>
            {
                int profileCompare = string.CompareOrdinal(left.ResolvedProfile, right.ResolvedProfile);
                if (profileCompare != 0) return profileCompare;

                string leftName = left.Map != null ? left.Map.name : string.Empty;
                string rightName = right.Map != null ? right.Map.name : string.Empty;
                return string.CompareOrdinal(leftName, rightName);
            });

            for (int i = 0; i < snapshots.Count; i++)
            {
                SerializedProperty entry = _entriesProp.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("_profileId").stringValue = snapshots[i].ProfileId;
                entry.FindPropertyRelative("_defaultMap").objectReferenceValue = snapshots[i].Map;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private readonly struct EntrySnapshot
        {
            public EntrySnapshot(string resolvedProfile, string profileId, ConvaiLipSyncMapAsset map)
            {
                ResolvedProfile = resolvedProfile ?? string.Empty;
                ProfileId = profileId ?? string.Empty;
                Map = map;
            }

            public string ResolvedProfile { get; }
            public string ProfileId { get; }
            public ConvaiLipSyncMapAsset Map { get; }
        }
    }
}
