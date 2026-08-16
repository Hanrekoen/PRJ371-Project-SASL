#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Editor.Inspectors;
using Convai.Editor.UI;
using Convai.Modules.LipSync.Profiles;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.LipSync.Editor
{
    /// <summary>
    ///     Validation window for verifying lip sync mapping between source blendshapes and target meshes.
    ///     Displays mapping health, missing targets, and optional runtime value previews.
    /// </summary>
    internal sealed class ConvaiLipSyncMapDebugWindow : EditorWindow
    {
        #region Filter Section

        private void DrawFilterSection()
        {
            if (_validationEntries.Count == 0) return;

            EditorGUILayout.BeginHorizontal();
            _searchFilter = EditorGUILayout.TextField(_searchFilter, ConvaiEditorStyles.SearchFieldBox,
                GUILayout.MinWidth(180), GUILayout.MaxWidth(280));
            if (GUILayout.Button(new GUIContent(ConvaiEditorGlyphs.Affordance.Remove, "Clear search"), GUILayout.Width(22)) &&
                !string.IsNullOrEmpty(_searchFilter))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }

            GUILayout.Space(12);
            _showOnlyProblems = GUILayout.Toggle(
                _showOnlyProblems,
                new GUIContent("Show Only Problems",
                    "Hide valid entries; show only No Mapping, Target Missing, or Disabled."),
                EditorStyles.miniButton,
                GUILayout.Width(130));
            GUILayout.Space(8);
            EditorGUI.BeginDisabledGroup(!UnityEngine.Application.isPlaying);
            bool newShowLive = GUILayout.Toggle(
                _showLiveValues,
                new GUIContent("Live Values",
                    "Show real-time blendshape values when in Play Mode (requires Lip Sync Component)."),
                EditorStyles.miniButton,
                GUILayout.Width(90));
            if (newShowLive != _showLiveValues) _showLiveValues = newShowLive;
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        #endregion

        #region Export

        private void ExportValidationReport()
        {
            string path = EditorUtility.SaveFilePanel(
                "Save Validation Report",
                "",
                $"LipSyncValidation_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                "txt");

            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== LIP SYNC MAPPING VALIDATION REPORT ===");
            sb.AppendLine($"Generated: {DateTime.Now}");
            sb.AppendLine($"Profile: {_selectedProfile}");
            if (_targetMeshes == null || _targetMeshes.Count == 0)
                sb.AppendLine("Target Meshes: None");
            else
            {
                for (int i = 0; i < _targetMeshes.Count; i++)
                {
                    SkinnedMeshRenderer mesh = _targetMeshes[i];
                    sb.AppendLine($"Target Mesh {i + 1}: {(mesh != null ? mesh.name : "None")}");
                }
            }

            sb.AppendLine($"Mapping: {(_mapping != null ? _mapping.name : "None")}");
            sb.AppendLine();

            int valid = _validationEntries.Count(e => e.Status == ValidationStatus.Valid);
            int problems = _validationEntries.Count(e =>
                e.Status == ValidationStatus.TargetBlendshapeMissing || e.Status == ValidationStatus.NoMapping);
            sb.AppendLine($"SUMMARY: {valid} valid, {problems} problems out of {_validationEntries.Count} total");
            sb.AppendLine();

            sb.AppendLine("=== PROBLEMS ===");
            foreach (MappingValidationEntry entry in _validationEntries.Where(e =>
                         e.Status == ValidationStatus.TargetBlendshapeMissing ||
                         e.Status == ValidationStatus.NoMapping))
            {
                sb.AppendLine($"[{entry.Index:D3}] {entry.SourceBlendshape}");
                sb.AppendLine($"      Status: {entry.Status}");
                sb.AppendLine($"      Message: {entry.StatusMessage}");
                if (entry.MappedTargetNames.Count > 0)
                    sb.AppendLine($"      Targets: {string.Join(", ", entry.MappedTargetNames)}");
                sb.AppendLine();
            }

            sb.AppendLine("=== ALL ENTRIES ===");
            sb.AppendLine("Index\tSource Blendshape\tTarget(s)\tStatus");
            foreach (MappingValidationEntry entry in _validationEntries)
            {
                string targets = entry.MappedTargetNames.Count > 0
                    ? string.Join("; ", entry.MappedTargetNames)
                    : "(none)";
                sb.AppendLine($"{entry.Index}\t{entry.SourceBlendshape}\t{targets}\t{entry.Status}");
            }

            sb.AppendLine();
            sb.AppendLine("=== MESH BLENDSHAPES ===");
            for (int i = 0; i < _meshBlendshapeNames.Count; i++) sb.AppendLine($"[{i:D3}] {_meshBlendshapeNames[i]}");

            File.WriteAllText(path, sb.ToString());
            EditorUtility.RevealInFinder(path);
            ConvaiLogger.Info("[Convai LipSync] Validation report exported to: " + path, LogCategory.Editor);
        }

        #endregion

        #region Constants & Colors

        private static Color ConvaiGreen => ConvaiEditorTheme.Accent;
        private static Color ConvaiGreenLight => ConvaiEditorTheme.AccentBright;
        private static Color ConvaiWarning => ConvaiEditorTheme.Warning;
        private static Color ConvaiError => ConvaiEditorTheme.Error;
        private static Color ConvaiInfo => ConvaiEditorTheme.Info;
        private static Color SectionBg => ConvaiEditorTheme.CardBg;
        private const int SectionIconFontSize = ConvaiEditorTheme.SectionIconFontSize;
        private const string EditorStateHostId = "MapDebugWindow";
        private const string SectionConfigurationId = "Configuration";
        private const string SectionValidationResultsId = "ValidationResults";
        private const float StatCellWidth = 52f;
        private const float StatCellGap = 20f;
        private const float StatBarHeight = 40f;

        #endregion

        #region Private Fields

        private ConvaiLipSyncComponent _component;
        private List<SkinnedMeshRenderer> _targetMeshes = new();
        private ConvaiLipSyncMapAsset _mapping;
        private LipSyncProfileId _selectedProfile = LipSyncProfileId.MetaHuman;

        private Vector2 _scrollPosition;
        private string _searchFilter = "";
        private bool _showOnlyProblems;
        private bool _showLiveValues;

        private readonly List<string> _meshBlendshapeNames = new();
        private readonly HashSet<string> _meshNameSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<MappingValidationEntry> _validationEntries = new();

        private bool _showConfiguration = true;
        private bool _showValidationResults = true;

        #endregion

        #region Data Structures

        private class MappingValidationEntry
        {
            public int Index;
            public bool IsEnabled = true;
            public List<string> MappedTargetNames = new();
            public float Multiplier = 1f;
            public float Offset;
            public string SourceBlendshape;
            public ValidationStatus Status;
            public string StatusMessage;
        }

        private enum ValidationStatus
        {
            Valid,
            NoMapping,
            TargetBlendshapeMissing,
            Disabled,
            MultipleTargets
        }

        #endregion

        #region Window Setup

        /// <summary>Window title used by the mapping validator UI.</summary>
        private const string WindowTitle = "Lip Sync Validator";

        public static void ShowWindow()
        {
            var window = GetWindow<ConvaiLipSyncMapDebugWindow>();
            window.titleContent = new GUIContent(
                WindowTitle,
                "Validate lip sync blendshape mappings between Convai profile and character meshes.");
            window.minSize = new Vector2(960, 620);
            Rect rect = window.position;
            if (rect.width < 980f || rect.height < 640f) window.position = new Rect(rect.x, rect.y, 980f, 640f);
        }

        /// <summary>
        ///     Opens the validator window with a specific Lip Sync component pre-selected.
        /// </summary>
        public static void ShowForComponent(ConvaiLipSyncComponent component)
        {
            var window = GetWindow<ConvaiLipSyncMapDebugWindow>();
            window.titleContent = new GUIContent(
                WindowTitle,
                "Validate lip sync blendshape mappings between Convai profile and character meshes.");
            window.minSize = new Vector2(960, 620);
            Rect rect = window.position;
            if (rect.width < 980f || rect.height < 640f) window.position = new Rect(rect.x, rect.y, 980f, 640f);
            window._component = component;
            window.SyncFromComponent();
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            _showConfiguration = ConvaiEditorSectionState.Get(EditorStateHostId, SectionConfigurationId, true);
            _showValidationResults =
                ConvaiEditorSectionState.Get(EditorStateHostId, SectionValidationResultsId, true);
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            ConvaiEditorSectionState.Set(EditorStateHostId, SectionConfigurationId, _showConfiguration);
            ConvaiEditorSectionState.Set(EditorStateHostId, SectionValidationResultsId, _showValidationResults);
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            TryAutoBindComponent();

            if (UnityEngine.Application.isPlaying && _showLiveValues && _component != null) Repaint();
        }

        private void OnGUI()
        {
            ConvaiEditorTheme.EnsureStyles();
            DrawHero();
            EditorGUILayout.Space(6);
            DrawSetupSection();
            DrawFilterSection();
            DrawValidationResults();
        }

        #endregion

        #region Header

        private static readonly GUIContent HeroTitle = new("Lip Sync Mapping Validator");

        private static readonly GUIContent HeroSubtitle = new(
            "Checks a profile's blendshapes against the character's meshes and mapping asset");

        /// <summary>
        ///     Draws the shared Convai window band.
        /// </summary>
        /// <remarks>
        ///     This window used to build its own: a 50px strip, a 22px emblem drawn by hand, and a
        ///     caption indented by the emblem's width. Every one of those numbers disagreed with the
        ///     band the other Convai windows share, so this window read as a different product. The
        ///     shared band owns all of it now.
        /// </remarks>
        private void DrawHero() =>
            ConvaiEditorTheme.WindowHero(position.width, HeroTitle, HeroSubtitle);

        private void DrawStatCell(string label, string value, Color valueColor)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(StatCellWidth), GUILayout.Height(StatBarHeight));
            GUILayout.FlexibleSpace();
            GUILayout.Label(label, ConvaiEditorStyles.TileLabel);
            GUILayout.Space(2);
            GUILayout.Label(value, ConvaiEditorStyles.TileNumberTinted(valueColor));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void DrawStatBarRightLabel(string text, Color color)
        {
            EditorGUILayout.BeginVertical(GUILayout.Height(StatBarHeight), GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, ConvaiEditorStyles.MicroLabelRightTinted(color));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Section Helpers

        private bool DrawSectionHeader(string sectionId, string title, bool isExpanded, string icon,
            Color? customColor = null, int? iconFontSize = null)
        {
            ConvaiEditorSectionSpec headerSpec = new(
                EditorStateHostId,
                sectionId,
                title,
                icon,
                customColor ?? ConvaiGreen,
                iconFontSize ?? SectionIconFontSize);
            return ConvaiEditorSections.DrawHeader(in headerSpec, isExpanded);
        }

        private void DrawSectionBackground(Action drawContent, Color? bgColor = null)
        {
            ConvaiEditorSections.BeginBody(bgColor ?? SectionBg);
            drawContent?.Invoke();
            ConvaiEditorSections.EndBody();
        }

        #endregion

        #region Setup Section

        private void DrawSetupSection()
        {
            _showConfiguration = DrawSectionHeader(SectionConfigurationId, "CONFIGURATION", _showConfiguration,
                ConvaiEditorGlyphs.Profile, ConvaiGreen, SectionIconFontSize);
            if (!_showConfiguration) return;

            DrawSectionBackground(() =>
            {
                if (_component != null)
                {
                    LipSyncProfileId syncedProfile = GetComponentSelectedProfile();
                    if (_selectedProfile != syncedProfile)
                    {
                        _selectedProfile = syncedProfile;
                        RefreshMeshBlendshapes();
                        RefreshValidation();
                    }
                }

                EditorGUI.BeginChangeCheck();
                EditorGUI.BeginDisabledGroup(_component != null);
                _selectedProfile = DrawProfilePopup(
                    new GUIContent("Profile",
                        "Blendshape set (e.g. ARKit, CC4) used for validation. Synced from Lip Sync Component when assigned."),
                    _selectedProfile);
                EditorGUI.EndDisabledGroup();
                if (EditorGUI.EndChangeCheck())
                {
                    RefreshMeshBlendshapes();
                    RefreshValidation();
                }

                if (_component != null)
                {
                    EditorGUILayout.LabelField("Profile synced from selected Lip Sync Component.",
                        ConvaiEditorStyles.MicroLabel);
                }

                EditorGUILayout.Space(4);

                EditorGUI.BeginChangeCheck();
                _component = (ConvaiLipSyncComponent)EditorGUILayout.ObjectField(
                    new GUIContent("Lip Sync Component",
                        "Assign to auto-fill target meshes and mapping from the component."),
                    _component, typeof(ConvaiLipSyncComponent), true);
                if (EditorGUI.EndChangeCheck() && _component != null) SyncFromComponent();

                EditorGUILayout.Space(4);

                EditorGUI.BeginChangeCheck();
                _mapping = (ConvaiLipSyncMapAsset)EditorGUILayout.ObjectField(
                    new GUIContent("Mapping Asset",
                        "Lip Sync Map Asset that defines source-to-target blendshape mappings."),
                    _mapping, typeof(ConvaiLipSyncMapAsset), false);
                if (EditorGUI.EndChangeCheck()) RefreshValidation();

                ConvaiEditorControls.GroupCaption("TARGET MESHES");
                EditorGUILayout.LabelField(
                    "SkinnedMeshRenderers that contain the target blendshapes. Add at least one to run validation.",
                    ConvaiEditorStyles.CaptionWrapped);
                GUILayout.Space(4);
                DrawTargetMeshListEditor();

                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                Color previousBgColor = GUI.backgroundColor;
                GUI.backgroundColor = ConvaiGreen;
                if (GUILayout.Button(
                        new GUIContent("Refresh Validation",
                            "Re-sync from Lip Sync Component (if assigned), re-scan meshes and mapping, then re-validate all entries."),
                        GUILayout.Height(24))) PerformFullRefresh();
                GUI.backgroundColor = previousBgColor;

                EditorGUI.BeginDisabledGroup(!HasAnyMesh());
                if (GUILayout.Button(new GUIContent("Export Report", "Save validation results to a text file."),
                        ConvaiEditorStyles.MiniButton, GUILayout.Height(24))) ExportValidationReport();
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
            });
        }

        private void DrawTargetMeshListEditor()
        {
            if (_targetMeshes == null) _targetMeshes = new List<SkinnedMeshRenderer>();

            bool changed = false;
            for (int i = 0; i < _targetMeshes.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                _targetMeshes[i] = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                    $"Target Mesh {i + 1}", _targetMeshes[i], typeof(SkinnedMeshRenderer), true);
                changed |= EditorGUI.EndChangeCheck();

                if (GUILayout.Button(new GUIContent("X", "Remove this mesh from the list"), GUILayout.Width(24)))
                {
                    _targetMeshes.RemoveAt(i);
                    changed = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Add Mesh Slot", "Add another target mesh slot"), ConvaiEditorStyles.MiniButton,
                    GUILayout.Width(120)))
            {
                _targetMeshes.Add(null);
                changed = true;
            }

            EditorGUILayout.EndHorizontal();

            if (changed)
            {
                RefreshMeshBlendshapes();
                RefreshValidation();
            }
        }

        /// <summary>
        ///     Re-syncs target meshes, mapping asset, and profile from the assigned Lip Sync Component, then refreshes blendshape
        ///     cache and validation.
        /// </summary>
        private void SyncFromComponent()
        {
            if (_component == null) return;

            _targetMeshes = _component.TargetMeshes != null
                ? _component.TargetMeshes.Where(m => m != null).Distinct().ToList()
                : new List<SkinnedMeshRenderer>();
            _mapping = _component.Mapping != null ? _component.Mapping : _component.EffectiveMapping;
            _selectedProfile = GetComponentSelectedProfile();

            RefreshMeshBlendshapes();
            RefreshValidation();
        }

        /// <summary>
        ///     Performs a full refresh: if a Lip Sync Component is assigned, re-syncs meshes/mapping/profile from it; then
        ///     re-scans mesh blendshapes and re-runs validation.
        ///     Ensures all displayed data is up to date when the user clicks Refresh Validation.
        /// </summary>
        private void PerformFullRefresh()
        {
            if (_component != null)
                SyncFromComponent();
            else
            {
                RefreshMeshBlendshapes();
                RefreshValidation();
            }
        }

        private bool HasAnyMesh() => _targetMeshes != null && _targetMeshes.Any(mesh => mesh != null);

        private static LipSyncProfileId DrawProfilePopup(GUIContent label, LipSyncProfileId selectedProfileId)
        {
            IReadOnlyList<ConvaiLipSyncProfile> profiles = LipSyncProfileCatalog.GetProfiles();
            if (profiles == null || profiles.Count == 0)
            {
                EditorGUILayout.LabelField(label, selectedProfileId.ToString());
                return selectedProfileId;
            }

            string normalized = LipSyncProfileId.Normalize(selectedProfileId.Value);
            string[] options = new string[profiles.Count];
            int selectedIndex = -1;
            for (int i = 0; i < profiles.Count; i++)
            {
                ConvaiLipSyncProfile profile = profiles[i];
                options[i] = $"{profile.DisplayName} ({profile.ProfileId})";
                if (string.Equals(profile.ProfileId.Value, normalized, StringComparison.Ordinal)) selectedIndex = i;
            }

            int popupIndex = selectedIndex >= 0 ? selectedIndex : 0;
            int newIndex = EditorGUILayout.Popup(label, popupIndex, options);
            return profiles[Mathf.Clamp(newIndex, 0, profiles.Count - 1)].ProfileId;
        }

        #endregion

        #region Validation Results

        private void DrawValidationResults()
        {
            const float tableHorizontalPadding = 8f;

            if (!HasAnyMesh())
            {
                ConvaiEditorFrame.InfoBox(
                    "No Meshes Assigned",
                    "Assign at least one target mesh in Configuration to see validation results.");
                return;
            }

            if (_validationEntries.Count == 0)
            {
                ConvaiEditorFrame.InfoBox(
                    "Nothing Analysed Yet",
                    "Press Refresh Validation in Configuration to analyse the mappings.");
                return;
            }

            _showValidationResults = DrawSectionHeader(SectionValidationResultsId, "Validation Results",
                _showValidationResults, ConvaiEditorGlyphs.Validation, ConvaiGreen, SectionIconFontSize);
            if (!_showValidationResults) return;

            DrawSectionBackground(() =>
            {
                using (ConvaiEditorFrame.TableHeader(22f))
                {
                    GUILayout.Space(tableHorizontalPadding);
                    ConvaiEditorTheme.TableColumn(IndexColumnLabel, 50);
                    ConvaiEditorTheme.TableColumn(SourceBlendshapeColumnLabel, 250);
                    ConvaiEditorTheme.TableColumn(ArrowColumnLabel, 20);
                    ConvaiEditorTheme.TableColumn(TargetBlendshapeColumnLabel, 250);
                    ConvaiEditorTheme.TableColumn(MultColumnLabel, 40);
                    if (_showLiveValues) ConvaiEditorTheme.TableColumn(LiveValueColumnLabel, 80);
                    ConvaiEditorTheme.TableColumn(StatusColumnLabel, 100);
                    GUILayout.Space(tableHorizontalPadding);
                }

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

                BlendshapeSnapshot liveSnapshot = default;
                if (_showLiveValues && UnityEngine.Application.isPlaying && _component != null)
                    liveSnapshot = _component.GetBlendshapeSnapshot();

                int rowIndex = 0;
                for (int i = 0; i < _validationEntries.Count; i++)
                {
                    MappingValidationEntry entry = _validationEntries[i];

                    if (_showOnlyProblems && entry.Status == ValidationStatus.Valid) continue;

                    if (!string.IsNullOrEmpty(_searchFilter))
                    {
                        bool matchesSearch =
                            entry.SourceBlendshape.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!matchesSearch && entry.MappedTargetNames.Count > 0)
                        {
                            matchesSearch = entry.MappedTargetNames.Any(n =>
                                n.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                        }

                        if (!matchesSearch) continue;
                    }

                    DrawValidationRow(entry, liveSnapshot, tableHorizontalPadding, rowIndex);
                    rowIndex++;
                }

                EditorGUILayout.EndScrollView();
                DrawValidationSummary();
            });
        }

        private static readonly GUIContent IndexColumnLabel = new("Index");
        private static readonly GUIContent SourceBlendshapeColumnLabel = new("Source Blendshape");
        private static readonly GUIContent ArrowColumnLabel = new(ConvaiEditorGlyphs.Motion);
        private static readonly GUIContent TargetBlendshapeColumnLabel = new("Target Blendshape Name(s)");
        private static readonly GUIContent MultColumnLabel = new("Mult");
        private static readonly GUIContent LiveValueColumnLabel = new("Live Value");
        private static readonly GUIContent StatusColumnLabel = new("Status");

        private void DrawValidationRow(MappingValidationEntry entry, BlendshapeSnapshot liveSnapshot,
            float horizontalPadding, int rowIndex)
        {
            using var rowScope = new ConvaiEditorFrame.TableRowScope(rowIndex, 20f);
            GUILayout.Space(horizontalPadding);

            EditorGUILayout.LabelField(entry.Index.ToString(), GUILayout.Width(50));
            EditorGUILayout.LabelField(entry.SourceBlendshape, GUILayout.Width(250));

            Color previousRowColor = GUI.color;
            GUI.color = entry.Status == ValidationStatus.Valid ? ConvaiGreen :
                entry.Status == ValidationStatus.TargetBlendshapeMissing ? ConvaiError : ConvaiWarning;
            EditorGUILayout.LabelField(ConvaiEditorGlyphs.Motion, GUILayout.Width(20));
            GUI.color = previousRowColor;

            string targetDisplay = entry.MappedTargetNames.Count > 0
                ? string.Join(", ", entry.MappedTargetNames)
                : "(none)";

            if (entry.Status == ValidationStatus.TargetBlendshapeMissing)
                GUI.color = ConvaiError;
            else if (entry.Status == ValidationStatus.NoMapping) GUI.color = ConvaiWarning;
            EditorGUILayout.LabelField(targetDisplay, GUILayout.Width(250));
            GUI.color = previousRowColor;

            string multStr = Math.Abs(entry.Multiplier - 1f) > 0.001f ? $"×{entry.Multiplier:F1}" : "1.0";
            EditorGUILayout.LabelField(multStr, GUILayout.Width(40));

            if (_showLiveValues)
            {
                if (liveSnapshot.IsValid && liveSnapshot.TryGetValue(entry.SourceBlendshape, out float liveValue))
                {
                    Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                        GUILayout.Width(80), GUILayout.Height(14));
                    EditorGUI.DrawRect(barRect, ConvaiEditorTheme.InnerBg);
                    if (liveValue > 0.001f)
                    {
                        var fillRect = new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(liveValue),
                            barRect.height);
                        Color barColor = Color.Lerp(ConvaiGreen, ConvaiWarning, liveValue);
                        EditorGUI.DrawRect(fillRect, barColor);
                        GUI.Label(barRect, $"{liveValue:F2}", ConvaiEditorStyles.MicroLabel);
                    }
                }
                else
                    EditorGUILayout.LabelField("-", GUILayout.Width(80));
            }

            DrawStatusBadge(entry.Status, entry.StatusMessage);
            GUILayout.Space(horizontalPadding);
        }

        private void DrawStatusBadge(ValidationStatus status, string message)
        {
            string text;
            Color color;

            switch (status)
            {
                case ValidationStatus.Valid:
                    text = $"{ConvaiEditorGlyphs.Status.Ok} OK";
                    color = ConvaiGreen;
                    break;
                case ValidationStatus.NoMapping:
                    text = $"{ConvaiEditorGlyphs.Status.Warn} No Map";
                    color = ConvaiWarning;
                    break;
                case ValidationStatus.TargetBlendshapeMissing:
                    text = $"{ConvaiEditorGlyphs.Status.Fail} Target Missing";
                    color = ConvaiError;
                    break;
                case ValidationStatus.Disabled:
                    text = $"{ConvaiEditorGlyphs.Status.Neutral} Disabled";
                    color = ConvaiEditorTheme.StatusIdle;
                    break;
                case ValidationStatus.MultipleTargets:
                    text = $"{ConvaiEditorGlyphs.Routing} Multi";
                    color = ConvaiInfo;
                    break;
                default:
                    text = $"{ConvaiEditorGlyphs.Status.Info} Unknown";
                    color = Color.white;
                    break;
            }

            Color previousBadgeColor = GUI.color;
            GUI.color = color;
            var content = new GUIContent(text, message);
            EditorGUILayout.LabelField(content, GUILayout.Width(100));
            GUI.color = previousBadgeColor;
        }

        private void DrawValidationSummary()
        {
            int total = _validationEntries.Count;
            int valid = _validationEntries.Count(e => e.Status == ValidationStatus.Valid);
            int noMapping = _validationEntries.Count(e => e.Status == ValidationStatus.NoMapping);
            int missingTarget = _validationEntries.Count(e => e.Status == ValidationStatus.TargetBlendshapeMissing);
            int disabled = _validationEntries.Count(e => e.Status == ValidationStatus.Disabled);
            int issues = noMapping + missingTarget;

            EditorGUILayout.Space(8);
            ConvaiEditorFrame.BeginPanel();
            EditorGUILayout.BeginHorizontal(GUILayout.Height(StatBarHeight));

            GUILayout.Space(10);
            DrawStatCell("Total", total.ToString(), Color.white);
            GUILayout.Space(StatCellGap);
            DrawStatCell("Valid", valid.ToString(), ConvaiGreen);
            GUILayout.Space(StatCellGap);
            DrawStatCell("Issues", issues.ToString(), issues > 0 ? ConvaiError : ConvaiGreen);
            GUILayout.Space(StatCellGap);
            DrawStatCell("Disabled", disabled.ToString(),
                disabled > 0 ? ConvaiEditorTheme.TextMuted : ConvaiEditorTheme.TextSecondary);
            GUILayout.FlexibleSpace();

            int meshCount = GetTotalMeshBlendshapeCount();
            if (meshCount >= 0) DrawStatBarRightLabel($"{meshCount} blendshapes", ConvaiGreenLight);
            GUILayout.Space(10);

            EditorGUILayout.EndHorizontal();
            ConvaiEditorFrame.EndPanel(0f);
        }

        private int GetTotalMeshBlendshapeCount()
        {
            int count = 0;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_targetMeshes != null)
            {
                for (int meshIndex = 0; meshIndex < _targetMeshes.Count; meshIndex++)
                {
                    SkinnedMeshRenderer mesh = _targetMeshes[meshIndex];
                    if (mesh == null || mesh.sharedMesh == null) continue;

                    for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                    {
                        if (unique.Add(mesh.sharedMesh.GetBlendShapeName(i)))
                            count++;
                    }
                }
            }

            return HasAnyMesh() ? count : -1;
        }

        #endregion

        #region Validation Logic

        private void RefreshMeshBlendshapes()
        {
            _meshBlendshapeNames.Clear();
            _meshNameSet.Clear();

            void AddMesh(SkinnedMeshRenderer mesh)
            {
                if (mesh == null || mesh.sharedMesh == null) return;
                Mesh m = mesh.sharedMesh;
                for (int i = 0; i < m.blendShapeCount; i++)
                {
                    string name = m.GetBlendShapeName(i);
                    if (_meshNameSet.Add(name)) _meshBlendshapeNames.Add(name);
                    string withoutPrefix = name.Contains(".") ? name.Substring(name.LastIndexOf('.') + 1) : null;
                    if (withoutPrefix != null && !_meshNameSet.Contains(withoutPrefix)) _meshNameSet.Add(withoutPrefix);
                }
            }

            if (_targetMeshes != null)
            {
                for (int i = 0; i < _targetMeshes.Count; i++)
                    AddMesh(_targetMeshes[i]);
            }
        }

        private void TryAutoBindComponent()
        {
            if (_component != null) return;

            var found = FindAnyObjectByType<ConvaiLipSyncComponent>();
            if (found == null) return;

            _component = found;
            SyncFromComponent();
        }

        private LipSyncProfileId GetComponentSelectedProfile()
        {
            if (_component == null) return _selectedProfile;
            return _component.LockedProfile;
        }

        private void RefreshValidation()
        {
            _validationEntries.Clear();

            IReadOnlyList<string> sourceBlendshapes = ResolveSourceBlendshapesForValidation();

            for (int i = 0; i < sourceBlendshapes.Count; i++)
            {
                string sourceBlendshape = sourceBlendshapes[i];
                var entry = new MappingValidationEntry { Index = i, SourceBlendshape = sourceBlendshape };

                if (_mapping != null)
                {
                    IReadOnlyList<string> targetNames = _mapping.GetTargetNames(sourceBlendshape);
                    bool isEnabled = _mapping.IsEnabled(sourceBlendshape);

                    entry.MappedTargetNames = targetNames?.ToList() ?? new List<string>();
                    entry.IsEnabled = isEnabled;

                    if (_mapping.TryGetEntry(sourceBlendshape,
                            out ConvaiLipSyncMapAsset.BlendshapeMappingSnapshot mapEntry))
                    {
                        entry.Multiplier = mapEntry.Multiplier;
                        entry.Offset = mapEntry.Offset;
                    }

                    if (!isEnabled)
                    {
                        entry.Status = ValidationStatus.Disabled;
                        entry.StatusMessage = "Mapping is disabled";
                    }
                    else if (entry.MappedTargetNames.Count == 0)
                    {
                        entry.Status = ValidationStatus.NoMapping;
                        entry.StatusMessage = "No target names defined in mapping";
                    }
                    else
                    {
                        bool allFound = true;
                        var missingNames = new List<string>();

                        foreach (string targetName in entry.MappedTargetNames)
                        {
                            if (!_meshNameSet.Contains(targetName))
                            {
                                allFound = false;
                                missingNames.Add(targetName);
                            }
                        }

                        if (allFound)
                        {
                            entry.Status = entry.MappedTargetNames.Count > 1
                                ? ValidationStatus.MultipleTargets
                                : ValidationStatus.Valid;
                            entry.StatusMessage = entry.MappedTargetNames.Count > 1
                                ? $"Maps to {entry.MappedTargetNames.Count} targets"
                                : "Mapping is valid";
                        }
                        else
                        {
                            entry.Status = ValidationStatus.TargetBlendshapeMissing;
                            entry.StatusMessage = $"Missing target blendshape(s): {string.Join(", ", missingNames)}";
                        }
                    }
                }
                else
                {
                    if (_meshNameSet.Contains(sourceBlendshape))
                    {
                        entry.Status = ValidationStatus.Valid;
                        entry.MappedTargetNames.Add(sourceBlendshape);
                        entry.StatusMessage = "Direct name match (no mapping)";
                    }
                    else
                    {
                        entry.Status = ValidationStatus.NoMapping;
                        entry.StatusMessage = "No mapping and no direct blendshape match on target meshes";
                    }
                }

                _validationEntries.Add(entry);
            }
        }

        private IReadOnlyList<string> ResolveSourceBlendshapesForValidation()
        {
            if (_mapping != null)
            {
                IReadOnlyList<string> mappedNames = _mapping.GetSourceBlendshapeNames();
                if (mappedNames != null && mappedNames.Count > 0) return mappedNames;
            }

            return LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(_selectedProfile);
        }

        #endregion
    }
}
#endif
