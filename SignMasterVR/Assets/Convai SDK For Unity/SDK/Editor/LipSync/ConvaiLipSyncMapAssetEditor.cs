#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Editor.Inspectors;
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Modules.LipSync.Profiles;
using Convai.Runtime.Logging;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.LipSync.Editor
{
    /// <summary>
    ///     Production inspector for <see cref="ConvaiLipSyncMapAsset" />.
    ///     Provides mapping authoring, validation signals, and bulk editing workflows.
    /// </summary>
    [CustomEditor(typeof(ConvaiLipSyncMapAsset))]
    internal sealed class ConvaiLipSyncMapAssetEditor : ConvaiInspectorEditor
    {
        private MappingStats BuildMappingStats()
        {
            int totalMappings = _mappingsProp != null ? _mappingsProp.arraySize : 0;
            int enabledCount = 0;
            int mappedEnabledCount = 0;

            for (int i = 0; i < totalMappings; i++)
            {
                SerializedProperty entry = _mappingsProp.GetArrayElementAtIndex(i);
                SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");
                SerializedProperty targetNamesProp = entry.FindPropertyRelative("targetNames");

                if (enabledProp == null || !enabledProp.boolValue) continue;

                enabledCount++;
                if (targetNamesProp != null && targetNamesProp.arraySize > 0) mappedEnabledCount++;
            }

            float coverage = enabledCount > 0
                ? mappedEnabledCount / (float)enabledCount * 100f
                : 0f;
            return new MappingStats(
                totalMappings,
                enabledCount,
                mappedEnabledCount,
                enabledCount - mappedEnabledCount,
                coverage);
        }

        private static string GetProfileDisplayName(LipSyncProfileId profile)
        {
            if (LipSyncProfileCatalog.TryGetProfile(profile, out ConvaiLipSyncProfile profileAsset))
                return profileAsset.DisplayName;

            return profile.IsValid ? profile.Value : "(none)";
        }

        #region Configuration Section

        private void DrawConfigurationSection()
        {
            if (!DrawSection(SectionConfigurationId, ConfigurationTitle, Glyphs.Profile)) return;

            DrawSectionBody(() =>
            {
                DrawTargetProfileSelector();

                GUILayout.Space(4);

                EditorGUILayout.PropertyField(_descriptionProp, DescriptionLabel);

                GUILayout.Space(8);

                EditorGUILayout.LabelField("Global Modifiers", ConvaiEditorStyles.SectionTitle);

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_globalMultiplierProp, GlobalMultiplierLabel);
                EditorGUILayout.PropertyField(_globalOffsetProp, GlobalOffsetLabel);
                EditorGUILayout.PropertyField(_allowUnmappedPassthroughProp, AllowUnmappedLabel);
                EditorGUI.indentLevel--;
            });
        }

        #endregion

        #region Tools Section

        private void DrawToolsSection()
        {
            if (!DrawSection(SectionToolsId, ToolsTitle, Glyphs.Discovery)) return;

            DrawSectionBody(() =>
            {
                bool hasPreviewMesh = HasPreviewMeshForDropdown();

                using (ConvaiEditorFrame.Panel())
                {
                    ConvaiEditorControls.GroupCaption("ADD BLENDSHAPES");
                    EditorGUILayout.LabelField(
                        "Choose one input method for creating mapping entries.",
                        ConvaiEditorStyles.CaptionWrapped);

                    ConvaiEditorControls.GroupCaption("From Mesh (Auto-Detect)");
                    DrawPreviewMeshListEditor();

                    int totalBlendshapes = GetPreviewMeshBlendshapeCount();
                    if (totalBlendshapes >= 0)
                    {
                        Color previousDetectColor = GUI.color;
                        GUI.color = totalBlendshapes > 0 ? ConvaiGreen : ConvaiWarning;
                        EditorGUILayout.LabelField($"Detected Blendshapes: {totalBlendshapes}", ConvaiEditorStyles.MicroLabel);
                        GUI.color = previousDetectColor;
                    }

                    EditorGUI.BeginDisabledGroup(!hasPreviewMesh);
                    if (PrimaryButton(AutoDetectButton, 24f)) ShowAutoDetectMenu();
                    EditorGUI.EndDisabledGroup();

                    ConvaiEditorControls.GroupCaption("From Mapping Text");
                    EditorGUILayout.BeginHorizontal();

                    Color previousImportBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = ConvaiGreenLight;
                    if (GUILayout.Button("Import Mapping File...", ConvaiEditorStyles.MiniButton)) ImportMappingFromFile();
                    GUI.backgroundColor = previousImportBgColor;

                    if (GUILayout.Button("Paste Mapping Text", ConvaiEditorStyles.MiniButton)) ImportMappingFromClipboard();

                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.Space(4);

                using (ConvaiEditorFrame.Panel())
                {
                    ConvaiEditorControls.GroupCaption("MAPPING ACTIONS");
                    EditorGUILayout.BeginHorizontal();

                    Color previousActionsBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = ConvaiGreen;
                    if (GUILayout.Button("Initialize Defaults", GUILayout.Height(24)))
                    {
                        if (EditorUtility.DisplayDialog(
                                "Initialize Defaults",
                                "This will clear all existing mappings and create default entries for the current profile. Continue?",
                                "Yes", "Cancel"))
                        {
                            Undo.RecordObject(_mapping, "Initialize Lip Sync Defaults");
                            _mapping.InitializeWithDefaults();
                            EditorUtility.SetDirty(_mapping);
                        }
                    }

                    GUI.backgroundColor = previousActionsBgColor;

                    if (GUILayout.Button("Clear All", ConvaiEditorStyles.MiniButton))
                    {
                        if (EditorUtility.DisplayDialog(
                                "Clear All Mappings",
                                "This will remove all mapping entries. Continue?",
                                "Yes", "Cancel"))
                        {
                            Undo.RecordObject(_mapping, "Clear Lip Sync Mappings");
                            _mapping.ClearMappings();
                            EditorUtility.SetDirty(_mapping);
                        }
                    }

                    if (GUILayout.Button("Sort A-Z", ConvaiEditorStyles.MiniButton)) SortMappings();

                    if (GUILayout.Button("Copy Mapping JSON", ConvaiEditorStyles.MiniButton)) CopyMappingAsJsonToClipboard();

                    EditorGUILayout.EndHorizontal();
                }
            });
        }

        #endregion

        private void DrawPreviewMeshListEditor()
        {
            _previewMeshes ??= new List<SkinnedMeshRenderer>();

            for (int i = 0; i < _previewMeshes.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                float labelWidth = GetDynamicLabelWidth($"Preview Mesh {i + 1}");
                Rect rowRect = EditorGUILayout.GetControlRect();
                var labelRect = new Rect(rowRect.x, rowRect.y, labelWidth, rowRect.height);
                var fieldRect = new Rect(labelRect.xMax + 4f, rowRect.y, rowRect.width - labelWidth - 32f,
                    rowRect.height);
                var removeRect = new Rect(fieldRect.xMax + 4f, rowRect.y, 28f, rowRect.height);

                EditorGUI.LabelField(labelRect, $"Preview Mesh {i + 1}");
                EditorGUI.BeginChangeCheck();
                _previewMeshes[i] = (SkinnedMeshRenderer)EditorGUI.ObjectField(fieldRect, _previewMeshes[i],
                    typeof(SkinnedMeshRenderer), true);
                if (EditorGUI.EndChangeCheck()) _meshNamesNeedRefresh = true;

                if (GUI.Button(removeRect, "X"))
                {
                    _previewMeshes.RemoveAt(i);
                    _meshNamesNeedRefresh = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add Preview Mesh", ConvaiEditorStyles.MiniButton, GUILayout.Width(130)))
            {
                _previewMeshes.Add(null);
                _meshNamesNeedRefresh = true;
            }

            EditorGUILayout.EndHorizontal();
        }

        private static float GetDynamicLabelWidth(string label)
        {
            float textWidth = ConvaiEditorTextMetrics.Width(EditorStyles.label, label) + 48f;
            return Mathf.Clamp(textWidth, PreviewMeshLabelMinWidth, PreviewMeshLabelMaxWidth);
        }

        #region Bulk Operations Section

        private void DrawBulkOperationsSection()
        {
            if (!DrawSection(SectionBulkOperationsId, BulkOperationsTitle, Glyphs.Command, defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Enable All", ConvaiEditorStyles.MiniButton)) SetAllEnabled(true);

                if (GUILayout.Button("Disable All", ConvaiEditorStyles.MiniButton)) SetAllEnabled(false);

                if (GUILayout.Button("Reset Multipliers", ConvaiEditorStyles.MiniButton)) ResetAllMultipliers();

                if (GUILayout.Button("Reset Offsets", ConvaiEditorStyles.MiniButton)) ResetAllOffsets();

                if (GUILayout.Button("Reset Curves", ConvaiEditorStyles.MiniButton)) ResetAllCurveExponents();

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Enable Eyes Only", ConvaiEditorStyles.MiniButton))
                    EnableCategory(new[] { "Eye", "Blink", "Look", "Squint", "Wide" });

                if (GUILayout.Button("Enable Mouth Only", ConvaiEditorStyles.MiniButton))
                    EnableCategory(new[] { "Mouth", "Jaw", "Lip", "Smile", "Frown" });

                if (GUILayout.Button("Enable Brows Only", ConvaiEditorStyles.MiniButton))
                    EnableCategory(new[] { "Brow" });

                EditorGUILayout.EndHorizontal();
            });
        }

        #endregion

        private readonly struct MappingStats
        {
            public MappingStats(
                int totalCount,
                int enabledCount,
                int mappedEnabledCount,
                int unmappedEnabledCount,
                float coveragePercent)
            {
                TotalCount = totalCount;
                EnabledCount = enabledCount;
                MappedEnabledCount = mappedEnabledCount;
                UnmappedEnabledCount = Mathf.Max(0, unmappedEnabledCount);
                CoveragePercent = coveragePercent;
            }

            public int TotalCount { get; }
            public int EnabledCount { get; }
            public int MappedEnabledCount { get; }
            public int UnmappedEnabledCount { get; }
            public float CoveragePercent { get; }
        }

        #region Constants & Colors

        private static Color ConvaiGreen => Theme.Accent;
        private static Color ConvaiGreenLight => Theme.AccentBright;
        private static Color ConvaiWarning => Theme.Warning;
        private static Color ConvaiError => Theme.Error;

        private const float PreviewMeshLabelMinWidth = 220f;
        private const float PreviewMeshLabelMaxWidth = 360f;
        private const float SourceBlendshapeColumnWidth = 230f;
        private const float MappingTableLeadingPadding = 4f;
        private const float MappingTableToggleColumnWidth = 20f;
        private const float MappingTableArrowColumnWidth = 20f;
        private const float MappingTableNumericColumnWidth = 45f;
        private const float MappingTableExpandButtonWidth = 24f;
        private const float MappingTableDeleteButtonWidth = 22f;
        private const float MappingTableHeaderHeight = 24f;
        private const float MappingTableRowHeight = 22f;
        private const int SourceBlendshapeTruncateLength = 32;
        private const string SectionConfigurationId = "Configuration";
        private const string SectionToolsId = "Tools";
        private const string SectionMappingsId = "Mappings";
        private const string SectionBulkOperationsId = "BulkOperations";
        private const float MappingsScrollMinHeight = 460f;
        private const float MappingsScrollMaxHeight = 560f;

        #endregion

        #region Private Fields

        private static readonly GUIContent ConfigurationTitle = new("Configuration");
        private static readonly GUIContent ToolsTitle = new("Tools");
        private static readonly GUIContent BulkOperationsTitle = new("Bulk Operations");
        private static readonly GUIContent MappingsTitle = new("Mappings");

        private static readonly GUIContent TotalLabel = new("Total");
        private static readonly GUIContent EnabledLabel = new("Enabled");
        private static readonly GUIContent MappedLabel = new("Mapped");
        private static readonly GUIContent CoverageLabel = new("Coverage");

        private static readonly GUIContent AutoDetectButton = new("Auto-Detect From Mesh");
        private static readonly GUIContent DescriptionLabel = new("Description");
        private static readonly GUIContent GlobalMultiplierLabel = new("Multiplier", "Applied to all values");
        private static readonly GUIContent GlobalOffsetLabel = new("Offset", "Added to all values");

        private static readonly GUIContent AllowUnmappedLabel =
            new("Allow Unmapped", "Pass through Source Blendshapes not in this list");

        private readonly GUIContent _profileChipContent = new(string.Empty);

        private ConvaiLipSyncMapAsset _mapping;

        private Vector2 _scrollPosition;
        private string _searchFilter = "";
        private bool _showOnlyUnmapped;
        private bool _showOnlyEnabled = true;
        private List<SkinnedMeshRenderer> _previewMeshes = new();

        private SerializedProperty _targetProfileProp;
        private SerializedProperty _descriptionProp;
        private SerializedProperty _mappingsProp;
        private SerializedProperty _globalMultiplierProp;
        private SerializedProperty _globalOffsetProp;
        private SerializedProperty _allowUnmappedPassthroughProp;

        private List<string> _meshBlendshapeNames;
        private bool _meshNamesNeedRefresh = true;

        /// <summary>Array index of the mapping entry whose advanced options are expanded. -1 when none.</summary>
        private int _expandedMappingIndex = -1;

        #endregion

        #region Header

        protected override string Title => "Lip Sync Mapping";

        protected override string EditorStateHostId => "MapAssetEditor";

        protected override GUIContent StatusChip
        {
            get
            {
                if (target == null || _targetProfileProp == null) return null;

                LipSyncProfileId selectedProfile = new(_targetProfileProp.stringValue);
                _profileChipContent.text = GetProfileDisplayName(selectedProfile);
                return _profileChipContent;
            }
        }

        protected override Color StatusChipTint => Theme.Accent;

        #endregion

        #region Unity Editor Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();

            _mapping = (ConvaiLipSyncMapAsset)target;
            CacheSerializedProperties();
        }

        private void CacheSerializedProperties()
        {
            _targetProfileProp = serializedObject.FindProperty("_targetProfileId");
            _descriptionProp = serializedObject.FindProperty("_description");
            _mappingsProp = serializedObject.FindProperty("_mappings");
            _globalMultiplierProp = serializedObject.FindProperty("_globalMultiplier");
            _globalOffsetProp = serializedObject.FindProperty("_globalOffset");
            _allowUnmappedPassthroughProp = serializedObject.FindProperty("_allowUnmappedPassthrough");
        }

        protected override void DrawBody()
        {
            // The SDK ships default maps inside the package. Selecting one here and pressing
            // Auto-detect used to rewrite it: lost on the next update, and refused outright in a
            // normally installed project. This editor sits off ConvaiEmbodimentProfileEditorBase,
            // so it carries the guard itself.
            using var readOnly = Convai.Editor.Ownership.ConvaiOwnershipNotice.BeginAssetEdit(target);

            DrawConfigurationSection();
            DrawToolsSection();
            DrawBulkOperationsSection();
            DrawMappingsSection();
        }

        #endregion

        #region Header

        protected override void DrawHeaderExtras()
        {
            MappingStats stats = BuildMappingStats();

            EditorGUILayout.BeginHorizontal();
            StatTile(TotalLabel, stats.TotalCount.ToString(), ConvaiGreenLight);
            StatTile(EnabledLabel, stats.EnabledCount.ToString(), ConvaiGreenLight);
            StatTile(MappedLabel, $"{stats.MappedEnabledCount}/{stats.EnabledCount}", ConvaiGreenLight);
            StatTile(CoverageLabel, $"{stats.CoveragePercent:0.#}%", ConvaiGreenLight);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        #endregion

        #region Mappings Section

        private void DrawMappingsSection()
        {
            if (!DrawSection(SectionMappingsId, MappingsTitle, Glyphs.Routing)) return;

            DrawSectionBody(() =>
            {
                using (ConvaiEditorFrame.Panel())
                {
                    int savedIndent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(-4f);

                    _searchFilter = EditorGUILayout.TextField(
                        _searchFilter, ConvaiEditorStyles.SearchFieldBox, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("x", GUILayout.Width(20)))

                    {
                        _searchFilter = "";
                        GUI.FocusControl(null);
                    }

                    GUILayout.Space(10);

                    _showOnlyUnmapped = GUILayout.Toggle(_showOnlyUnmapped, "Unmapped", EditorStyles.miniButton,
                        GUILayout.Width(70));
                    _showOnlyEnabled = GUILayout.Toggle(_showOnlyEnabled, "Enabled", EditorStyles.miniButton,
                        GUILayout.Width(60));

                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(4);

                    DrawAlignedMappingHeader();

                    _scrollPosition = EditorGUILayout.BeginScrollView(
                        _scrollPosition,
                        GUILayout.MinHeight(MappingsScrollMinHeight),
                        GUILayout.MaxHeight(MappingsScrollMaxHeight));

                    int visibleCount = 0;
                    for (int i = 0; i < _mappingsProp.arraySize; i++)
                    {
                        SerializedProperty entry = _mappingsProp.GetArrayElementAtIndex(i);

                        if (ShouldShowEntry(entry))
                        {
                            DrawAlignedMappingEntry(entry, i, visibleCount % 2 == 1);
                            visibleCount++;
                        }
                    }

                    if (visibleCount == 0)
                    {
                        GUILayout.Space(20);
                        EditorGUILayout.LabelField(
                            "No mappings match the current filter",
                            ConvaiEditorStyles.CenteredMini(Theme.TextMuted));
                        GUILayout.Space(20);
                    }
                    else
                    {
                        // Add bottom padding so the final row remains fully visible.
                        GUILayout.Space(6);
                    }

                    EditorGUILayout.EndScrollView();

                    GUILayout.Space(4);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    Color previousAddBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = ConvaiGreen;
                    if (GUILayout.Button("+ Add Entry", GUILayout.Width(100), GUILayout.Height(22))) AddNewEntry();
                    GUI.backgroundColor = previousAddBgColor;

                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(2);
                    EditorGUI.indentLevel = savedIndent;
                }
            });
        }

        /// <summary>Draws per-entry advanced options: clamp, override value, ignore global modifiers.</summary>
        private void DrawMappingEntryAdvanced(SerializedProperty entry, bool altRow)
        {
            SerializedProperty useOverrideValueProp = entry.FindPropertyRelative("useOverrideValue");
            SerializedProperty overrideValueProp = entry.FindPropertyRelative("overrideValue");
            SerializedProperty ignoreGlobalModifiersProp = entry.FindPropertyRelative("ignoreGlobalModifiers");
            SerializedProperty curveExponentProp = entry.FindPropertyRelative("curveExponent");
            SerializedProperty clampMinValueProp = entry.FindPropertyRelative("clampMinValue");
            SerializedProperty clampMaxValueProp = entry.FindPropertyRelative("clampMaxValue");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(28);

            using (ConvaiEditorFrame.Panel())
            {
                ConvaiEditorControls.GroupCaption("Per-blendshape overrides");

                EditorGUI.indentLevel++;

                if (curveExponentProp != null)
                {
                    EditorGUILayout.PropertyField(curveExponentProp,
                        new GUIContent("Response Curve",
                            "Exponent applied to the source value before gain. 1 = linear. " +
                            "Below 1 lifts the mid-range for clearer, more intentional articulation; " +
                            "above 1 suppresses low-level noise."));
                }

                if (clampMinValueProp != null)
                {
                    EditorGUILayout.PropertyField(clampMinValueProp,
                        new GUIContent("Clamp Min", "Minimum output value for this blendshape."));
                }

                if (clampMaxValueProp != null)
                {
                    EditorGUILayout.PropertyField(clampMaxValueProp,
                        new GUIContent("Clamp Max", "Maximum output value for this blendshape."));
                }

                if (ignoreGlobalModifiersProp != null)
                {
                    EditorGUILayout.PropertyField(ignoreGlobalModifiersProp,
                        new GUIContent("Ignore Global Modifiers",
                            "Use only this entry's multiplier/offset, not the asset's global multiplier/offset."));
                }

                if (useOverrideValueProp != null)
                {
                    EditorGUILayout.PropertyField(useOverrideValueProp,
                        new GUIContent("Use Override Value",
                            "When enabled, output a fixed value instead of the animated value."));
                }

                if (overrideValueProp != null && useOverrideValueProp != null && useOverrideValueProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(overrideValueProp, new GUIContent("Override Value"));
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
        }

        private void DrawAlignedMappingHeader()
        {
            using (ConvaiEditorFrame.TableHeader(MappingTableHeaderHeight))
            {
                // The recessed strip and hairline come from the scope; the column layout itself stays
                // rect-based so the header stays pixel-aligned with the editable fields in each row.
                Rect headerRect = ConvaiEditorFrame.ReserveScopeRect(MappingTableHeaderHeight);

                Rect alignedHeaderRect = headerRect;
                alignedHeaderRect.width = Mathf.Max(0f, alignedHeaderRect.width - GetMappingScrollbarWidth());

                GUIStyle headerLeftStyle = ConvaiEditorStyles.TableHeaderCell;
                GUIStyle headerCenteredStyle = ConvaiEditorStyles.TableHeaderCellCentered;
                float textInset = GetInputContentLeftInset();

                GetMappingColumnRects(
                    alignedHeaderRect,
                    out Rect toggleRect,
                    out Rect sourceRect,
                    out Rect arrowRect,
                    out Rect targetRect,
                    out Rect multiplierRect,
                    out Rect offsetRect,
                    out Rect expandRect,
                    out Rect deleteRect);

                GUI.Label(toggleRect, ConvaiEditorGlyphs.Status.Ok, headerCenteredStyle);
                GUI.Label(InsetRect(sourceRect, textInset), "Source Blendshape", headerLeftStyle);
                GUI.Label(arrowRect, ConvaiEditorGlyphs.Motion, headerCenteredStyle);
                GUI.Label(InsetRect(targetRect, textInset), "Target Name(s)", headerLeftStyle);
                GUI.Label(multiplierRect, "Mult", headerCenteredStyle);
                GUI.Label(offsetRect, "Offs", headerCenteredStyle);
                GUI.Label(expandRect, string.Empty, headerCenteredStyle);
                GUI.Label(deleteRect, string.Empty, headerCenteredStyle);
            }
        }

        private void DrawAlignedMappingEntry(SerializedProperty entry, int index, bool altRow)
        {
            SerializedProperty sourceBlendshapeProp = entry.FindPropertyRelative("sourceBlendshape");
            SerializedProperty targetNamesProp = entry.FindPropertyRelative("targetNames");
            SerializedProperty multiplierProp = entry.FindPropertyRelative("multiplier");
            SerializedProperty offsetProp = entry.FindPropertyRelative("offset");
            SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");

            string sourceBlendshape = sourceBlendshapeProp?.stringValue ?? string.Empty;
            bool isEnabled = enabledProp?.boolValue ?? true;
            bool hasTarget = targetNamesProp != null && targetNamesProp.arraySize > 0;

            Rect rowRect;
            bool isExpanded = _expandedMappingIndex == index;

            // The zebra fill comes from the scope; the column layout itself stays rect-based so each
            // field lines up with the header and with the other rows. The scope closes before the
            // expanded advanced panel below, which needs its own full-width layout row.
            using (new ConvaiEditorFrame.TableRowScope(altRow ? 1 : 0, MappingTableRowHeight))
            {
                rowRect = ConvaiEditorFrame.ReserveScopeRect(MappingTableRowHeight);

                Color statusColor = !isEnabled
                    ? Theme.TextMuted
                    : hasTarget
                        ? ConvaiGreen
                        : ConvaiWarning;
                Theme.StatusDot(new Rect(rowRect.x, rowRect.y, 12f, rowRect.height), statusColor);

                GetMappingColumnRects(
                    rowRect,
                    out Rect toggleRect,
                    out Rect sourceRect,
                    out Rect arrowRect,
                    out Rect targetRect,
                    out Rect multiplierRect,
                    out Rect offsetRect,
                    out Rect expandRect,
                    out Rect deleteRect);

                EditorGUI.BeginChangeCheck();
                bool newEnabled = EditorGUI.Toggle(toggleRect, isEnabled);
                if (EditorGUI.EndChangeCheck() && enabledProp != null) enabledProp.boolValue = newEnabled;

                string displayName = sourceBlendshape.Length > SourceBlendshapeTruncateLength
                    ? sourceBlendshape.Substring(0, SourceBlendshapeTruncateLength - 3) + "..."
                    : sourceBlendshape;
                GUI.Label(sourceRect, new GUIContent(displayName, sourceBlendshape), EditorStyles.textField);
                GUI.Label(arrowRect, ConvaiEditorGlyphs.Motion, ConvaiEditorStyles.TableHeaderCellCentered);

                if (HasPreviewMeshForDropdown())
                    DrawTargetDropdown(targetRect, targetNamesProp);
                else
                {
                    string currentTarget = targetNamesProp != null && targetNamesProp.arraySize > 0
                        ? targetNamesProp.GetArrayElementAtIndex(0).stringValue
                        : string.Empty;

                    EditorGUI.BeginChangeCheck();
                    string newTarget = EditorGUI.TextField(targetRect, currentTarget);
                    if (EditorGUI.EndChangeCheck() && targetNamesProp != null)
                    {
                        if (targetNamesProp.arraySize == 0) targetNamesProp.InsertArrayElementAtIndex(0);

                        targetNamesProp.GetArrayElementAtIndex(0).stringValue = newTarget;
                    }
                }

                if (multiplierProp != null)
                {
                    EditorGUI.BeginChangeCheck();
                    float newMult = EditorGUI.FloatField(multiplierRect, multiplierProp.floatValue);
                    if (EditorGUI.EndChangeCheck()) multiplierProp.floatValue = Mathf.Clamp(newMult, 0f, 5f);
                }

                if (offsetProp != null)
                {
                    EditorGUI.BeginChangeCheck();
                    float newOffset = EditorGUI.FloatField(offsetRect, offsetProp.floatValue);
                    if (EditorGUI.EndChangeCheck()) offsetProp.floatValue = Mathf.Clamp(newOffset, -1f, 1f);
                }

                if (GUI.Button(expandRect, isExpanded ? ConvaiEditorGlyphs.Affordance.DisclosureOpen : ConvaiEditorGlyphs.Affordance.DisclosureClosed, ConvaiEditorStyles.MiniButton))
                    _expandedMappingIndex = isExpanded ? -1 : index;

                Color previousColor = GUI.color;
                GUI.color = ConvaiError;
                if (GUI.Button(deleteRect, ConvaiEditorGlyphs.Affordance.Remove, ConvaiEditorStyles.MiniButton))
                {
                    if (_expandedMappingIndex == index)
                        _expandedMappingIndex = -1;
                    else if (_expandedMappingIndex > index) _expandedMappingIndex--;

                    _mappingsProp.DeleteArrayElementAtIndex(index);
                }

                GUI.color = previousColor;
            }

            if (_expandedMappingIndex == index) DrawMappingEntryAdvanced(entry, altRow);
        }

        private void DrawTargetDropdown(Rect rect, SerializedProperty targetNamesProp)
        {
            EnsureMeshNamesCache();

            var options = new List<string> { "(None)" };
            options.AddRange(_meshBlendshapeNames ?? new List<string>());

            string currentValue = targetNamesProp != null && targetNamesProp.arraySize > 0
                ? targetNamesProp.GetArrayElementAtIndex(0).stringValue
                : string.Empty;

            int currentIndex = string.IsNullOrEmpty(currentValue) ? 0 : options.IndexOf(currentValue);
            if (currentIndex < 0) currentIndex = 0;

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(rect, currentIndex, options.ToArray());
            if (EditorGUI.EndChangeCheck() && targetNamesProp != null)
            {
                string newValue = newIndex == 0 ? string.Empty : options[newIndex];
                if (string.IsNullOrEmpty(newValue))
                    targetNamesProp.ClearArray();
                else
                {
                    if (targetNamesProp.arraySize == 0) targetNamesProp.InsertArrayElementAtIndex(0);

                    targetNamesProp.GetArrayElementAtIndex(0).stringValue = newValue;
                }
            }
        }

        private static void GetMappingColumnRects(
            Rect rowRect,
            out Rect toggleRect,
            out Rect sourceRect,
            out Rect arrowRect,
            out Rect targetRect,
            out Rect multiplierRect,
            out Rect offsetRect,
            out Rect expandRect,
            out Rect deleteRect)
        {
            float x = rowRect.x + MappingTableLeadingPadding;
            float y = rowRect.y;
            float height = rowRect.height;

            toggleRect = new Rect(x, y, MappingTableToggleColumnWidth, height);
            x += MappingTableToggleColumnWidth;

            sourceRect = new Rect(x, y, SourceBlendshapeColumnWidth, height);
            x += SourceBlendshapeColumnWidth;

            arrowRect = new Rect(x, y, MappingTableArrowColumnWidth, height);
            x += MappingTableArrowColumnWidth;

            float trailingWidth =
                (MappingTableNumericColumnWidth * 2f) +
                MappingTableExpandButtonWidth +
                MappingTableDeleteButtonWidth;
            float targetWidth = Mathf.Max(0f, rowRect.xMax - trailingWidth - x);
            targetRect = new Rect(x, y, targetWidth, height);
            x += targetWidth;

            multiplierRect = new Rect(x, y, MappingTableNumericColumnWidth, height);
            x += MappingTableNumericColumnWidth;

            offsetRect = new Rect(x, y, MappingTableNumericColumnWidth, height);
            x += MappingTableNumericColumnWidth;

            expandRect = new Rect(x, y + 2f, MappingTableExpandButtonWidth, Mathf.Max(0f, height - 4f));
            x += MappingTableExpandButtonWidth;

            deleteRect = new Rect(x, y + 2f, MappingTableDeleteButtonWidth, Mathf.Max(0f, height - 4f));
        }

        private static Rect InsetRect(Rect rect, float leftInset)
        {
            return new Rect(
                rect.x + leftInset,
                rect.y,
                Mathf.Max(0f, rect.width - leftInset),
                rect.height);
        }

        private static float GetInputContentLeftInset()
        {
            RectOffset padding = EditorStyles.textField?.padding;
            return Mathf.Max(4f, padding?.left ?? 0);
        }

        private static float GetMappingScrollbarWidth()
        {
            GUIStyle scrollbarStyle = GUI.skin?.verticalScrollbar;
            if (scrollbarStyle == null) return 13f;

            float width = scrollbarStyle.fixedWidth;
            if (width <= 0f) width = 13f;

            return width;
        }

        private bool ShouldShowEntry(SerializedProperty entry)
        {
            SerializedProperty sourceBlendshapeProp = entry.FindPropertyRelative("sourceBlendshape");
            SerializedProperty targetNamesProp = entry.FindPropertyRelative("targetNames");
            SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");

            string sourceBlendshape = sourceBlendshapeProp?.stringValue ?? "";
            bool isEnabled = enabledProp?.boolValue ?? true;
            bool hasTarget = targetNamesProp != null && targetNamesProp.arraySize > 0 &&
                             !string.IsNullOrEmpty(targetNamesProp.GetArrayElementAtIndex(0).stringValue);

            if (_showOnlyEnabled && !isEnabled) return false;

            if (_showOnlyUnmapped && hasTarget) return false;

            if (!string.IsNullOrEmpty(_searchFilter))
            {
                string searchLower = _searchFilter.ToLowerInvariant();
                bool matchesSource = sourceBlendshape.ToLowerInvariant().Contains(searchLower);

                bool matchesTarget = false;
                if (targetNamesProp != null)
                {
                    for (int i = 0; i < targetNamesProp.arraySize; i++)
                    {
                        string targetName = targetNamesProp.GetArrayElementAtIndex(i).stringValue;
                        if (!string.IsNullOrEmpty(targetName) && targetName.ToLowerInvariant().Contains(searchLower))
                        {
                            matchesTarget = true;
                            break;
                        }
                    }
                }

                if (!matchesSource && !matchesTarget) return false;
            }

            return true;
        }

        #endregion

        #region Helper Methods

        private bool HasPreviewMeshForDropdown()
        {
            if (_previewMeshes == null || _previewMeshes.Count == 0) return false;

            for (int i = 0; i < _previewMeshes.Count; i++)
            {
                SkinnedMeshRenderer mesh = _previewMeshes[i];
                if (mesh != null && mesh.sharedMesh != null) return true;
            }

            return false;
        }

        private void EnsureMeshNamesCache()
        {
            if (!_meshNamesNeedRefresh && _meshBlendshapeNames != null) return;

            _meshBlendshapeNames = new List<string>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddMeshBlendshapes(SkinnedMeshRenderer meshRenderer)
            {
                if (meshRenderer == null || meshRenderer.sharedMesh == null) return;

                Mesh mesh = meshRenderer.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (unique.Add(name)) _meshBlendshapeNames.Add(name);
                }
            }

            if (_previewMeshes != null)
            {
                for (int i = 0; i < _previewMeshes.Count; i++)
                    AddMeshBlendshapes(_previewMeshes[i]);
            }

            _meshNamesNeedRefresh = false;
        }

        private int GetPreviewMeshBlendshapeCount()
        {
            int count = 0;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_previewMeshes != null)
            {
                for (int meshIndex = 0; meshIndex < _previewMeshes.Count; meshIndex++)
                {
                    SkinnedMeshRenderer meshRenderer = _previewMeshes[meshIndex];
                    if (meshRenderer == null || meshRenderer.sharedMesh == null) continue;

                    Mesh m = meshRenderer.sharedMesh;
                    for (int i = 0; i < m.blendShapeCount; i++)
                    {
                        if (unique.Add(m.GetBlendShapeName(i)))
                            count++;
                    }
                }
            }

            return HasPreviewMeshForDropdown()
                ? count
                : -1;
        }

        private void ShowAutoDetectMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Exact Match"), false, () => RunAutoDetect(BlendshapeMatchMode.Exact));
            menu.AddItem(new GUIContent("Contains Match (Recommended)"), false,
                () => RunAutoDetect(BlendshapeMatchMode.Contains));
            menu.AddItem(new GUIContent("Fuzzy Match"), false, () => RunAutoDetect(BlendshapeMatchMode.Fuzzy));
            menu.ShowAsContext();
        }

        private void RunAutoDetect(BlendshapeMatchMode mode)
        {
            Undo.RecordObject(_mapping, "Auto-Detect Lip Sync Mapping");
            SkinnedMeshRenderer[] previewMeshes = GetPreviewMeshes();
            if (previewMeshes.Length == 0) return;

            _mapping.AutoDetectFromMeshes(previewMeshes, mode);
            EditorUtility.SetDirty(_mapping);
            serializedObject.Update();

            ConvaiLogger.Info($"[Convai LipSync] Auto-detect complete using {mode} matching.", LogCategory.Editor);
        }

        private SkinnedMeshRenderer[] GetPreviewMeshes()
        {
            var meshes = new List<SkinnedMeshRenderer>();
            if (_previewMeshes == null || _previewMeshes.Count == 0) return meshes.ToArray();

            var unique = new HashSet<long>();
            for (int i = 0; i < _previewMeshes.Count; i++)
            {
                SkinnedMeshRenderer mesh = _previewMeshes[i];
                if (mesh == null || mesh.sharedMesh == null) continue;

                if (!unique.Add(ConvaiObjectId.Of(mesh))) continue;

                meshes.Add(mesh);
            }

            return meshes.ToArray();
        }

        private void ImportMappingFromFile()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Import Lip Sync Mapping",
                UnityEngine.Application.dataPath,
                new[] { "Mapping files", "json,txt,map", "All files", "*" });

            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                string text = File.ReadAllText(path);
                TryImportMappingText(text, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Import Failed",
                    $"Could not read file:\n{path}\n\n{ex.Message}",
                    "OK");
            }
        }

        private void ImportMappingFromClipboard()
        {
            string clipboard = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(clipboard))
            {
                EditorUtility.DisplayDialog(
                    "Clipboard Empty",
                    "Clipboard does not contain mapping text.",
                    "OK");
                return;
            }

            TryImportMappingText(clipboard, "clipboard");
        }

        private void TryImportMappingText(string rawText, string sourceLabel)
        {
            if (!ConvaiLipSyncMapImportParser.TryParse(rawText,
                    out ConvaiLipSyncMapImportParser.MappingImportData imported, out string error))
            {
                EditorUtility.DisplayDialog(
                    "Import Failed",
                    error ?? "Unsupported mapping format.",
                    "OK");
                return;
            }

            ApplyImportedMappings(imported, $"Import Lip Sync Mapping ({sourceLabel})");

            string summary = $"Imported {imported.Entries.Count} mapping entries from {sourceLabel}.";
            if (imported.Warnings.Count > 0) summary += $"\nSkipped {imported.Warnings.Count} malformed entries.";

            EditorUtility.DisplayDialog("Import Complete", summary, "OK");

            if (imported.Warnings.Count > 0)
            {
                ConvaiLogger.Warning(
                    $"[Convai LipSync] Import warnings:\n- {string.Join("\n- ", imported.Warnings)}",
                    LogCategory.Editor);
            }
        }

        private void ApplyImportedMappings(ConvaiLipSyncMapImportParser.MappingImportData imported, string undoLabel)
        {
            if (imported == null) return;

            Undo.RecordObject(_mapping, undoLabel);
            serializedObject.Update();

            if (!string.IsNullOrWhiteSpace(imported.TargetProfileId) && _targetProfileProp != null)
                _targetProfileProp.stringValue = LipSyncProfileId.Normalize(imported.TargetProfileId);

            if (imported.HasDescription && _descriptionProp != null)
                _descriptionProp.stringValue = imported.Description ?? string.Empty;

            if (imported.GlobalMultiplier.HasValue && _globalMultiplierProp != null)
                _globalMultiplierProp.floatValue = Mathf.Clamp(imported.GlobalMultiplier.Value, 0f, 3f);

            if (imported.GlobalOffset.HasValue && _globalOffsetProp != null)
                _globalOffsetProp.floatValue = Mathf.Clamp(imported.GlobalOffset.Value, -1f, 1f);

            if (imported.AllowUnmappedPassthrough.HasValue && _allowUnmappedPassthroughProp != null)
                _allowUnmappedPassthroughProp.boolValue = imported.AllowUnmappedPassthrough.Value;

            _mappingsProp.ClearArray();
            for (int i = 0; i < imported.Entries.Count; i++)
            {
                ConvaiLipSyncMapImportParser.ImportedEntry sourceEntry = imported.Entries[i];

                int entryIndex = _mappingsProp.arraySize;
                _mappingsProp.InsertArrayElementAtIndex(entryIndex);
                SerializedProperty entry = _mappingsProp.GetArrayElementAtIndex(entryIndex);

                SerializedProperty sourceBlendshapeProp = entry.FindPropertyRelative("sourceBlendshape");
                if (sourceBlendshapeProp != null)
                    sourceBlendshapeProp.stringValue = sourceEntry.SourceBlendshape ?? string.Empty;

                SerializedProperty targetNamesProp = entry.FindPropertyRelative("targetNames");
                if (targetNamesProp != null)
                {
                    List<string> targets = sourceEntry.TargetNames == null
                        ? new List<string>()
                        : sourceEntry.TargetNames
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Select(name => name.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    SetStringArray(targetNamesProp, targets);
                }

                SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");
                if (enabledProp != null) enabledProp.boolValue = sourceEntry.Enabled;

                SerializedProperty multiplierProp = entry.FindPropertyRelative("multiplier");
                if (multiplierProp != null) multiplierProp.floatValue = Mathf.Clamp(sourceEntry.Multiplier, 0f, 5f);

                SerializedProperty offsetProp = entry.FindPropertyRelative("offset");
                if (offsetProp != null) offsetProp.floatValue = Mathf.Clamp(sourceEntry.Offset, -1f, 1f);

                SerializedProperty curveExponentProp = entry.FindPropertyRelative("curveExponent");
                if (curveExponentProp != null)
                {
                    curveExponentProp.floatValue = Mathf.Clamp(sourceEntry.CurveExponent,
                        ConvaiLipSyncMapAsset.MinCurveExponent, ConvaiLipSyncMapAsset.MaxCurveExponent);
                }

                SerializedProperty useOverrideValueProp = entry.FindPropertyRelative("useOverrideValue");
                if (useOverrideValueProp != null) useOverrideValueProp.boolValue = sourceEntry.UseOverrideValue;

                SerializedProperty overrideValueProp = entry.FindPropertyRelative("overrideValue");
                if (overrideValueProp != null) overrideValueProp.floatValue = Mathf.Clamp01(sourceEntry.OverrideValue);

                SerializedProperty ignoreGlobalModifiersProp = entry.FindPropertyRelative("ignoreGlobalModifiers");
                if (ignoreGlobalModifiersProp != null)
                    ignoreGlobalModifiersProp.boolValue = sourceEntry.IgnoreGlobalModifiers;

                float clampMin = Mathf.Clamp01(sourceEntry.ClampMinValue);
                float clampMax = Mathf.Clamp01(sourceEntry.ClampMaxValue);
                if (clampMax < clampMin) clampMax = clampMin;

                SerializedProperty clampMinValueProp = entry.FindPropertyRelative("clampMinValue");
                if (clampMinValueProp != null) clampMinValueProp.floatValue = clampMin;

                SerializedProperty clampMaxValueProp = entry.FindPropertyRelative("clampMaxValue");
                if (clampMaxValueProp != null) clampMaxValueProp.floatValue = clampMax;
            }

            _expandedMappingIndex = -1;
            _meshNamesNeedRefresh = true;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(_mapping);
            serializedObject.Update();
        }

        private static void SetStringArray(SerializedProperty arrayProp, List<string> values)
        {
            if (arrayProp == null) return;

            arrayProp.ClearArray();
            if (values == null || values.Count == 0) return;

            for (int i = 0; i < values.Count; i++)
            {
                arrayProp.InsertArrayElementAtIndex(i);
                arrayProp.GetArrayElementAtIndex(i).stringValue = values[i];
            }
        }

        private void CopyMappingAsJsonToClipboard()
        {
            string json = BuildMappingJson(true);
            GUIUtility.systemCopyBuffer = json;

            ConvaiLogger.Info(
                $"[Convai LipSync] Mapping JSON copied to clipboard ({_mappingsProp.arraySize} entries).",
                LogCategory.Editor);
            EditorUtility.DisplayDialog("JSON Copied", "Current mapping was copied to clipboard as JSON.", "OK");
        }

        private string BuildMappingJson(bool prettyPrint)
        {
            serializedObject.Update();

            var payload = new MappingExportPayload
            {
                version = ConvaiLipSyncMapImportParser.CurrentVersion,
                targetProfileId =
                    _targetProfileProp != null ? _targetProfileProp.stringValue : LipSyncProfileId.ARKitValue,
                description = _descriptionProp != null ? _descriptionProp.stringValue : string.Empty,
                globalMultiplier = _globalMultiplierProp != null ? _globalMultiplierProp.floatValue : 1f,
                globalOffset = _globalOffsetProp != null ? _globalOffsetProp.floatValue : 0f,
                allowUnmappedPassthrough =
                    _allowUnmappedPassthroughProp != null && _allowUnmappedPassthroughProp.boolValue,
                mappings = new List<MappingExportEntry>()
            };

            for (int i = 0; i < _mappingsProp.arraySize; i++)
            {
                SerializedProperty entry = _mappingsProp.GetArrayElementAtIndex(i);
                if (entry == null) continue;

                var exportEntry = new MappingExportEntry
                {
                    sourceBlendshape = entry.FindPropertyRelative("sourceBlendshape")?.stringValue ?? string.Empty,
                    targetNames = GetStringArray(entry.FindPropertyRelative("targetNames")),
                    multiplier = entry.FindPropertyRelative("multiplier")?.floatValue ?? 1f,
                    offset = entry.FindPropertyRelative("offset")?.floatValue ?? 0f,
                    curveExponent = entry.FindPropertyRelative("curveExponent")?.floatValue ?? 1f,
                    enabled = entry.FindPropertyRelative("enabled")?.boolValue ?? true,
                    useOverrideValue = entry.FindPropertyRelative("useOverrideValue")?.boolValue ?? false,
                    overrideValue = entry.FindPropertyRelative("overrideValue")?.floatValue ?? 0f,
                    ignoreGlobalModifiers = entry.FindPropertyRelative("ignoreGlobalModifiers")?.boolValue ?? false,
                    clampMinValue = entry.FindPropertyRelative("clampMinValue")?.floatValue ?? 0f,
                    clampMaxValue = entry.FindPropertyRelative("clampMaxValue")?.floatValue ?? 1f
                };

                payload.mappings.Add(exportEntry);
            }

            return JsonUtility.ToJson(payload, prettyPrint);
        }

        private static string[] GetStringArray(SerializedProperty arrayProp)
        {
            if (arrayProp == null || !arrayProp.isArray || arrayProp.arraySize == 0) return Array.Empty<string>();

            string[] values = new string[arrayProp.arraySize];
            for (int i = 0; i < arrayProp.arraySize; i++) values[i] = arrayProp.GetArrayElementAtIndex(i).stringValue;

            return values;
        }

        [Serializable]
        private sealed class MappingExportPayload
        {
            public int version = ConvaiLipSyncMapImportParser.CurrentVersion;
            public string targetProfileId;
            public string description;
            public float globalMultiplier = 1f;
            public float globalOffset;
            public bool allowUnmappedPassthrough = true;
            public List<MappingExportEntry> mappings = new();
        }

        [Serializable]
        private sealed class MappingExportEntry
        {
            public string sourceBlendshape;
            public string[] targetNames;
            public float multiplier = 1f;
            public float offset;
            public float curveExponent = 1f;
            public bool enabled = true;
            public bool useOverrideValue;
            public float overrideValue;
            public bool ignoreGlobalModifiers;
            public float clampMinValue;
            public float clampMaxValue = 1f;
        }

        private void DrawTargetProfileSelector()
        {
            IReadOnlyList<ConvaiLipSyncProfile> profiles = LipSyncProfileCatalog.GetProfiles();
            string currentId = LipSyncProfileId.Normalize(_targetProfileProp.stringValue);

            if (profiles == null || profiles.Count == 0)
            {
                EditorGUILayout.PropertyField(_targetProfileProp, new GUIContent("Target Profile ID"));
                return;
            }

            string[] profileOptions = new string[profiles.Count];
            int selectedIndex = -1;
            for (int i = 0; i < profiles.Count; i++)
            {
                ConvaiLipSyncProfile profile = profiles[i];
                profileOptions[i] = profile.DisplayName;
                if (string.Equals(profile.ProfileId.Value, currentId, StringComparison.Ordinal)) selectedIndex = i;
            }

            int popupIndex = selectedIndex >= 0 ? selectedIndex : 0;
            EditorGUI.BeginChangeCheck();
            int newSelection = EditorGUILayout.Popup("Target Profile", popupIndex, profileOptions);
            if (EditorGUI.EndChangeCheck())
            {
                string nextProfileId = profiles[Mathf.Clamp(newSelection, 0, profiles.Count - 1)].ProfileId.Value;
                if (EditorUtility.DisplayDialog(
                        "Profile Changed",
                        "Do you want to reinitialize the mappings for the new profile? This will clear existing mappings.",
                        "Yes, Reinitialize", "No, Keep Current"))
                {
                    _targetProfileProp.stringValue = nextProfileId;
                    serializedObject.ApplyModifiedProperties();
                    _mapping.InitializeWithDefaults();
                    serializedObject.Update();
                }
                else
                    _targetProfileProp.stringValue = nextProfileId;
            }

            if (selectedIndex < 0)
            {
                WarningBox(
                    "Unregistered Profile",
                    $"Profile id '{currentId}' is not registered. Select a valid profile.");
            }
        }

        private void SetAllEnabled(bool enabled)
        {
            Undo.RecordObject(_mapping, enabled ? "Enable All Lip Sync" : "Disable All Lip Sync");

            for (int i = 0; i < _mappingsProp.arraySize; i++)
            {
                SerializedProperty enabledProp =
                    _mappingsProp.GetArrayElementAtIndex(i).FindPropertyRelative("enabled");
                if (enabledProp != null) enabledProp.boolValue = enabled;
            }

            EditorUtility.SetDirty(_mapping);
        }

        private void ResetAllMultipliers()
        {
            Undo.RecordObject(_mapping, "Reset Lip Sync Multipliers");

            for (int i = 0; i < _mappingsProp.arraySize; i++)
            {
                SerializedProperty multiplierProp =
                    _mappingsProp.GetArrayElementAtIndex(i).FindPropertyRelative("multiplier");
                if (multiplierProp != null) multiplierProp.floatValue = 1f;
            }

            EditorUtility.SetDirty(_mapping);
        }

        private void ResetAllCurveExponents()
        {
            Undo.RecordObject(_mapping, "Reset Lip Sync Response Curves");

            for (int i = 0; i < _mappingsProp.arraySize; i++)
            {
                SerializedProperty curveExponentProp =
                    _mappingsProp.GetArrayElementAtIndex(i).FindPropertyRelative("curveExponent");
                if (curveExponentProp != null) curveExponentProp.floatValue = 1f;
            }

            EditorUtility.SetDirty(_mapping);
        }

        private void ResetAllOffsets()
        {
            Undo.RecordObject(_mapping, "Reset Lip Sync Offsets");

            for (int i = 0; i < _mappingsProp.arraySize; i++)
            {
                SerializedProperty offsetProp = _mappingsProp.GetArrayElementAtIndex(i).FindPropertyRelative("offset");
                if (offsetProp != null) offsetProp.floatValue = 0f;
            }

            EditorUtility.SetDirty(_mapping);
        }

        private void EnableCategory(string[] keywords)
        {
            Undo.RecordObject(_mapping, "Enable Category");

            for (int i = 0; i < _mappingsProp.arraySize; i++)
            {
                SerializedProperty entry = _mappingsProp.GetArrayElementAtIndex(i);
                SerializedProperty sourceBlendshapeProp = entry.FindPropertyRelative("sourceBlendshape");
                SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");

                if (sourceBlendshapeProp == null || enabledProp == null) continue;

                string name = sourceBlendshapeProp.stringValue;
                bool matchesCategory = keywords.Any(kw => name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);

                enabledProp.boolValue = matchesCategory;
            }

            EditorUtility.SetDirty(_mapping);
        }

        private void SortMappings()
        {
            Undo.RecordObject(_mapping, "Sort Mappings");

            IReadOnlyList<ConvaiLipSyncMapAsset.BlendshapeMappingEntry> mappings = _mapping.Mappings;
            if (mappings == null) return;

            List<ConvaiLipSyncMapImportParser.ImportedEntry> sortedList = mappings
                .OrderBy(m => m.sourceBlendshape)
                .Select(m => new ConvaiLipSyncMapImportParser.ImportedEntry
                {
                    SourceBlendshape = m.sourceBlendshape,
                    TargetNames = m.targetNames != null ? new List<string>(m.targetNames) : new List<string>(),
                    Multiplier = m.multiplier,
                    Offset = m.offset,
                    CurveExponent = m.curveExponent,
                    Enabled = m.enabled,
                    UseOverrideValue = m.useOverrideValue,
                    OverrideValue = m.overrideValue,
                    IgnoreGlobalModifiers = m.ignoreGlobalModifiers,
                    ClampMinValue = m.clampMinValue,
                    ClampMaxValue = m.clampMaxValue
                })
                .ToList();

            serializedObject.Update();
            _mappingsProp.ClearArray();
            for (int i = 0; i < sortedList.Count; i++)
            {
                ConvaiLipSyncMapImportParser.ImportedEntry sourceEntry = sortedList[i];
                int entryIndex = _mappingsProp.arraySize;
                _mappingsProp.InsertArrayElementAtIndex(entryIndex);
                SerializedProperty entry = _mappingsProp.GetArrayElementAtIndex(entryIndex);

                entry.FindPropertyRelative("sourceBlendshape").stringValue =
                    sourceEntry.SourceBlendshape ?? string.Empty;
                SetStringArray(entry.FindPropertyRelative("targetNames"), sourceEntry.TargetNames);
                entry.FindPropertyRelative("multiplier").floatValue = Mathf.Clamp(sourceEntry.Multiplier, 0f, 5f);
                entry.FindPropertyRelative("offset").floatValue = Mathf.Clamp(sourceEntry.Offset, -1f, 1f);
                entry.FindPropertyRelative("curveExponent").floatValue = Mathf.Clamp(sourceEntry.CurveExponent,
                    ConvaiLipSyncMapAsset.MinCurveExponent, ConvaiLipSyncMapAsset.MaxCurveExponent);
                entry.FindPropertyRelative("enabled").boolValue = sourceEntry.Enabled;
                entry.FindPropertyRelative("useOverrideValue").boolValue = sourceEntry.UseOverrideValue;
                entry.FindPropertyRelative("overrideValue").floatValue = Mathf.Clamp01(sourceEntry.OverrideValue);
                entry.FindPropertyRelative("ignoreGlobalModifiers").boolValue = sourceEntry.IgnoreGlobalModifiers;

                float clampMin = Mathf.Clamp01(sourceEntry.ClampMinValue);
                float clampMax = Mathf.Clamp01(sourceEntry.ClampMaxValue);
                if (clampMax < clampMin) clampMax = clampMin;

                entry.FindPropertyRelative("clampMinValue").floatValue = clampMin;
                entry.FindPropertyRelative("clampMaxValue").floatValue = clampMax;
            }

            serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(_mapping);
            serializedObject.Update();

            ConvaiLogger.Info("[Convai LipSync] Mappings sorted alphabetically.", LogCategory.Editor);
        }

        private void AddNewEntry()
        {
            int newIndex = _mappingsProp.arraySize;
            _mappingsProp.InsertArrayElementAtIndex(newIndex);

            SerializedProperty newEntry = _mappingsProp.GetArrayElementAtIndex(newIndex);
            newEntry.FindPropertyRelative("sourceBlendshape").stringValue = "NewBlendshape";
            newEntry.FindPropertyRelative("targetNames").ClearArray();
            newEntry.FindPropertyRelative("multiplier").floatValue = 1f;
            newEntry.FindPropertyRelative("offset").floatValue = 0f;
            newEntry.FindPropertyRelative("enabled").boolValue = true;
            SerializedProperty useOverride = newEntry.FindPropertyRelative("useOverrideValue");
            if (useOverride != null) useOverride.boolValue = false;
            SerializedProperty overrideVal = newEntry.FindPropertyRelative("overrideValue");
            if (overrideVal != null) overrideVal.floatValue = 0f;
            SerializedProperty ignoreGlobal = newEntry.FindPropertyRelative("ignoreGlobalModifiers");
            if (ignoreGlobal != null) ignoreGlobal.boolValue = false;
            SerializedProperty clampMin = newEntry.FindPropertyRelative("clampMinValue");
            if (clampMin != null) clampMin.floatValue = 0f;
            SerializedProperty clampMax = newEntry.FindPropertyRelative("clampMaxValue");
            if (clampMax != null) clampMax.floatValue = 1f;

            _scrollPosition = new Vector2(0, float.MaxValue);
        }

        #endregion
    }
}
#endif
