#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Convai.Domain.Models.LipSync;
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.LipSync;
using Convai.Modules.LipSync.Profiles;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Convai.Editor.UI;
using Convai.Shared.Compatibility;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;

namespace Convai.Modules.LipSync.Editor
{
    [CustomEditor(typeof(ConvaiLipSyncComponent))]
    internal sealed class ConvaiLipSyncComponentEditor : ConvaiInspectorEditor
    {
        #region Editor Mode Info

        private void DrawEditorModeInfo()
        {
            if (!DrawSection(SectionLiveStatusId, "Live", ConvaiEditorGlyphs.Live, accent: ConvaiInfo)) return;

            DrawSectionBody(() => OfflinePlaceholder());
        }

        #endregion

        #region Constants & Colors

        private static Color ConvaiGreen => ConvaiEditorTheme.Accent;
        private static Color ConvaiGreenLight => ConvaiEditorTheme.AccentBright;
        private static Color ConvaiWarning => ConvaiEditorTheme.Warning;
        private static Color ConvaiError => ConvaiEditorTheme.Error;
        private static Color ConvaiInfo => ConvaiEditorTheme.Info;

        private static Color SectionBg => ConvaiEditorTheme.CardBg;

        private const string DefaultRegistryResourcePath = "LipSync/DefaultMaps/LipSyncDefaultMapRegistry";
        private const string SectionCoreSetupId = "CoreSetup";
        private const string SectionPlaybackBehaviorId = "PlaybackBehavior";
        private const string SectionStreamingLatencyId = "StreamingLatency";
        private const string SectionLiveStatusId = "LiveStatus";

        #endregion

        #region Private Fields

        private ConvaiLipSyncComponent _component;

        private ReorderableList _targetMeshesList;

        private SerializedProperty _lockedProfileProp;
        private SerializedProperty _timeOffsetProp;
        private SerializedProperty _fadeOutDurationProp;
        private SerializedProperty _smoothingFactorProp;
        private SerializedProperty _latencyModeProp;
        private SerializedProperty _maxBufferedSecondsProp;
        private SerializedProperty _minResumeHeadroomSecondsProp;
        private SerializedProperty _deliverChunksAheadProp;
        private SerializedProperty _targetMeshesProp;
        private SerializedProperty _mappingProp;

        private readonly GUIContent _statusChipContent = new(string.Empty);
        private Color _statusChipTint;

        private ConvaiLipSyncDefaultMapRegistry _defaultMapRegistry;
        private readonly List<SkinnedMeshRenderer> _tempMeshes = new();
        private readonly HashSet<long> _seenMeshIds = new();
        private readonly HashSet<string> _uniqueBlendshapes = new(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Header

        protected override string Title => "Convai Lip Sync";

        protected override string EditorStateHostId => "ComponentEditor";

        protected override GUIContent StatusChip
        {
            get
            {
                GetHeaderStatus(out string statusText, out Color statusColor);
                _statusChipTint = statusColor;
                _statusChipContent.text = statusText;
                return _statusChipContent;
            }
        }

        protected override Color StatusChipTint => _statusChipTint;

        private void GetHeaderStatus(out string statusText, out Color statusColor)
        {
            if (_component == null || !UnityEngine.Application.isPlaying)
            {
                statusColor = ConvaiEditorChips.Ready.Tint;
                statusText = ConvaiEditorChips.Ready.Content.text;
                return;
            }

            PlaybackState state = _component.EngineState;
            switch (state)
            {
                case PlaybackState.FadingOut:
                    statusColor = ConvaiWarning;
                    statusText = "Fading";
                    break;
                case PlaybackState.Playing:
                    statusColor = ConvaiGreen;
                    statusText = "Playing";
                    break;
                case PlaybackState.Starving:
                    statusColor = ConvaiWarning;
                    statusText = "Starving";
                    break;
                case PlaybackState.Buffering:
                    statusColor = ConvaiInfo;
                    statusText = "Buffering";
                    break;
                default:
                    statusColor = ConvaiEditorChips.Idle.Tint;
                    statusText = ConvaiEditorChips.Idle.Content.text;
                    break;
            }
        }

        #endregion

        #region Unity Editor Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();

            _component = (ConvaiLipSyncComponent)target;
            CacheSerializedProperties();
            _defaultMapRegistry = Resources.Load<ConvaiLipSyncDefaultMapRegistry>(DefaultRegistryResourcePath);
        }

        private void CacheSerializedProperties()
        {
            _lockedProfileProp = serializedObject.FindProperty("_lockedProfileId");
            _timeOffsetProp = serializedObject.FindProperty("_timeOffset");
            _fadeOutDurationProp = serializedObject.FindProperty("_fadeOutDuration");
            _smoothingFactorProp = serializedObject.FindProperty("_smoothingFactor");
            _latencyModeProp = serializedObject.FindProperty("_latencyMode");
            _maxBufferedSecondsProp = serializedObject.FindProperty("_maxBufferedSeconds");
            _minResumeHeadroomSecondsProp = serializedObject.FindProperty("_minResumeHeadroomSeconds");
            _deliverChunksAheadProp = serializedObject.FindProperty("_deliverChunksAhead");
            _targetMeshesProp = serializedObject.FindProperty("_targetMeshes");
            _mappingProp = serializedObject.FindProperty("_mapping");
        }

        private void InitializeReorderableList()
        {
            if (_targetMeshesList != null) return;

            _targetMeshesList = new ReorderableList(serializedObject, _targetMeshesProp, true, true, true, true);

            _targetMeshesList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect,
                    new GUIContent("Target Meshes", "Blendshape target renderers used by lip sync runtime."));
            };

            _targetMeshesList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                SerializedProperty element = _targetMeshesProp.GetArrayElementAtIndex(index);

                rect.y += 2;

                var objRect = new Rect(rect.x, rect.y, rect.width - 110f, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(objRect, element, GUIContent.none);

                var countRect = new Rect(rect.xMax - 110f, rect.y, 110f, EditorGUIUtility.singleLineHeight);
                var meshRenderer = element.objectReferenceValue as SkinnedMeshRenderer;

                if (meshRenderer != null && meshRenderer.sharedMesh != null)
                {
                    int bsCount = meshRenderer.sharedMesh.blendShapeCount;
                    string label = bsCount > 0 ? $"{bsCount} blendshapes" : "0 blendshapes";
                    Color color = bsCount == 0 ? ConvaiError : Theme.Fade(Theme.AccentBright, 0.8f);

                    EditorGUI.LabelField(countRect, label, Styles.MicroLabelRightTinted(color));
                }
                else if (meshRenderer != null)
                {
                    EditorGUI.LabelField(countRect, "No Mesh Data", Styles.MicroLabelRightTinted(ConvaiWarning));
                }
                else
                {
                    EditorGUI.LabelField(countRect, "Empty",
                        Styles.MicroLabelRightTinted(Theme.Fade(Theme.TextMuted, 0.5f)));
                }
            };
        }

        protected override void OnBeforeInspectorGUI()
        {
            PopulateAssignedMeshes();
            InitializeReorderableList();
        }

        protected override void DrawBody()
        {
            DrawValidationWarnings();

            DrawCoreSetupSection();
            DrawPlaybackBehaviorSection();
            DrawStreamingLatencySection();

            if (!UnityEngine.Application.isPlaying)
                DrawEditorModeInfo();
        }

        protected override void DrawLiveSection()
        {
            DrawLiveStatusSection();

            if (_component.IsPlaying) Repaint();
        }

        #endregion

        #region Validation Warnings

        private void DrawValidationWarnings()
        {
            if (_tempMeshes.Count == 0)
            {
                WarningBox(
                    "Target Mesh Required",
                    "Assign at least one SkinnedMeshRenderer with Blendshapes to enable lip sync.",
                    "Auto-Find",
                    AutoFindMeshesInHierarchy);
            }
            else if (GetTotalUniqueBlendshapeCount() == 0)
            {
                ErrorBox(
                    "No Blendshapes Found",
                    "Assigned target meshes do not contain blendshapes. Lip sync requires at least one blendshape.");
            }

            LipSyncProfileId profileId = GetInspectorProfile();
            if (!LipSyncProfileCatalog.TryGetProfile(profileId, out _))
            {
                ErrorBox(
                    "Unknown Lip Sync Profile",
                    $"Profile '{profileId}' is not registered in LipSync profile catalog.");
            }
            else if (_mappingProp.objectReferenceValue is ConvaiLipSyncMapAsset assignedMap &&
                     assignedMap.TargetProfileId != profileId)
            {
                WarningBox(
                    "Profile / Mapping Mismatch",
                    $"Selected profile is '{ToDisplayProfileName(profileId)}' but mapping targets '{ToDisplayProfileName(assignedMap.TargetProfileId)}'. " +
                    "Runtime will ignore this mapping and use the selected profile default map.");
            }
            else if (_mappingProp.objectReferenceValue == null && GetProfileDefaultMap(profileId) == null)
            {
                WarningBox(
                    "Default Mapping Missing",
                    $"No default mapping registered for profile '{ToDisplayProfileName(profileId)}'. Runtime will use safe-disabled map.");
            }

            DrawRigMappingAdvisory(profileId);
        }

        /// <summary>
        ///     Points out that the mapping in use targets blendshape names this character does not
        ///     have, when another mapping registered for the same transport does fit it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The transport a character receives and the blendshape names on its mesh are
        ///         independent: a backend sending ARKit to a Character Creator 4 rig needs a
        ///         different mapping from one sending ARKit to an ARKit-named rig. Nothing in the
        ///         scene states which vocabulary a mesh uses, so this measures it instead — how many
        ///         of a mapping's channels resolve to a blendshape that actually exists here.
        ///     </para>
        ///     <para>
        ///         Advisory on purpose. It names the better mapping and offers to assign it rather
        ///         than swapping it silently, because a measurement is not a certainty and the
        ///         author's explicit choice has to keep winning. It stays quiet unless the
        ///         difference is decisive, so a rig that partially matches both never nags.
        ///     </para>
        /// </remarks>
        private void DrawRigMappingAdvisory(LipSyncProfileId profileId)
        {
            if (_defaultMapRegistry == null) return;
            if (GetTotalUniqueBlendshapeCount() == 0) return;

            ConvaiLipSyncMapAsset effective =
                _mappingProp.objectReferenceValue as ConvaiLipSyncMapAsset ?? GetProfileDefaultMap(profileId);

            int effectiveHits = CountDrivableChannels(effective);

            ConvaiLipSyncMapAsset best = null;
            int bestHits = effectiveHits;

            IReadOnlyList<ConvaiLipSyncDefaultMapRegistry.ProfileDefaultMapEntry> entries =
                _defaultMapRegistry.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                ConvaiLipSyncDefaultMapRegistry.ProfileDefaultMapEntry entry = entries[i];
                ConvaiLipSyncMapAsset candidate = entry != null ? entry.DefaultMap : null;
                if (candidate == null || candidate == effective) continue;
                if (entry.ProfileId != profileId) continue;

                int hits = CountDrivableChannels(candidate);
                if (hits <= bestHits) continue;

                bestHits = hits;
                best = candidate;
            }

            if (best == null) return;

            // Only speak up when the alternative is decisively better: twice the reach, or the
            // mapping in use reaching nothing at all.
            if (effectiveHits > 0 && bestHits < effectiveHits * 2) return;

            string current = effective != null ? effective.name : "the safe-disabled fallback";
            WarningBox(
                "Mapping Does Not Fit This Character",
                $"{current} drives {effectiveHits} of this character's blendshapes; " +
                $"'{best.name}' drives {bestHits}. Convai ships that mapping for characters whose mesh " +
                "uses a different blendshape naming convention from the lip sync profile.",
                "Use It",
                () => AssignMapping(best));
        }

        /// <summary>
        ///     How many of a mapping's source channels resolve to at least one blendshape that
        ///     exists on the assigned meshes. Counts channels rather than target names so a mapping
        ///     with many aliases per channel is not flattered by them.
        /// </summary>
        private int CountDrivableChannels(ConvaiLipSyncMapAsset map)
        {
            if (map == null) return 0;

            int hits = 0;
            IReadOnlyList<ConvaiLipSyncMapAsset.BlendshapeMappingEntry> entries = map.Mappings;
            for (int i = 0; i < entries.Count; i++)
            {
                List<string> targets = entries[i] != null ? entries[i].targetNames : null;
                if (targets == null) continue;

                for (int j = 0; j < targets.Count; j++)
                {
                    if (!_uniqueBlendshapes.Contains(targets[j])) continue;
                    hits++;
                    break;
                }
            }

            return hits;
        }

        private void AssignMapping(ConvaiLipSyncMapAsset map)
        {
            _mappingProp.objectReferenceValue = map;
            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region Editor Blocks

        private void DrawCoreSetupSection()
        {
            if (!DrawSection(SectionCoreSetupId, "Core Setup", ConvaiEditorGlyphs.Profile)) return;

            DrawSectionBody(() =>
            {
                DrawProfileSelector();
                EditorGUILayout.PropertyField(_mappingProp,
                    new GUIContent("Mapping", "Lip Sync mapping for the selected profile."));

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Create New", Styles.MiniButton, GUILayout.Width(80))) CreateMappingAsset();
                EditorGUI.BeginDisabledGroup(_mappingProp.objectReferenceValue == null);
                if (GUILayout.Button("Edit", Styles.MiniButton, GUILayout.Width(50)))
                    Selection.activeObject = _mappingProp.objectReferenceValue;
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button(
                        new GUIContent("Validator", "Open Lip Sync Mapping Validator to check blendshape mappings."),
                        Styles.MiniButton, GUILayout.Width(60)))
                    ConvaiLipSyncMapDebugWindow.ShowForComponent(_component);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(6);

                _targetMeshesList.DoLayoutList();

                int count = _tempMeshes.Count > 0 ? GetTotalUniqueBlendshapeCount() : 0;

                EditorGUILayout.BeginHorizontal();
                Color previousMeshStatusColor = GUI.color;
                if (_tempMeshes.Count > 0)
                {
                    GUI.color = count > 0 ? ConvaiGreenLight : ConvaiError;
                    string icon = count > 0 ? ConvaiEditorGlyphs.Status.Ok : ConvaiEditorGlyphs.Status.Fail;
                    GUILayout.Label($"{icon} {_tempMeshes.Count} Meshes Found ({count} Blendshapes)",
                        ConvaiEditorStyles.MicroLabel);
                    GUI.color = previousMeshStatusColor;
                }
                else
                {
                    GUI.color = ConvaiWarning;
                    GUILayout.Label($"{ConvaiEditorGlyphs.Status.Warn} No meshes assigned", ConvaiEditorStyles.MicroLabel);
                    GUI.color = previousMeshStatusColor;
                }

                GUILayout.FlexibleSpace();
                Color previousAutoFindBgColor = GUI.backgroundColor;
                GUI.backgroundColor = ConvaiGreen;
                if (GUILayout.Button("Auto-Find", Styles.MiniButton, GUILayout.Width(80))) AutoFindMeshesInHierarchy();
                GUI.backgroundColor = previousAutoFindBgColor;
                EditorGUILayout.EndHorizontal();
            });
        }

        private void DrawPlaybackBehaviorSection()
        {
            if (!DrawSection(SectionPlaybackBehaviorId, "Playback & Behavior", ConvaiEditorGlyphs.Motion)) return;

            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_smoothingFactorProp,
                    new GUIContent("Lip Smoothing", "Reduces high-frequency jitter in lip movements."));
                EditorGUILayout.PropertyField(_fadeOutDurationProp,
                    new GUIContent("Fade Transition", "Duration of the blend back to the neutral pose in seconds."));
                if (_timeOffsetProp != null)
                {
                    EditorGUILayout.PropertyField(_timeOffsetProp,
                        new GUIContent("A/V Sync Offset", "Fine-tune the audio-visual synchronization in seconds."));
                }
            });
        }

        private void DrawStreamingLatencySection()
        {
            if (!DrawSection(SectionStreamingLatencyId, "Streaming & Latency", ConvaiEditorGlyphs.Routing,
                    defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_latencyModeProp,
                    new GUIContent("Latency Mode",
                        "Preset strategies governing network resilience vs. playback delay. 'Balanced' (0.12s headroom) is highly recommended for general use."));
                if (EditorGUI.EndChangeCheck()) ApplyLatencyPresetForMode(_latencyModeProp.intValue);
                EditorGUILayout.PropertyField(_deliverChunksAheadProp,
                    new GUIContent("Ahead Chunk Delivery (Preview)",
                        "Trades a small amount of extra delay for smoother playback on unreliable " +
                        "connections. Preview feature — leave it off unless Convai support asks you to enable it."));
                if (_latencyModeProp.intValue == (int)LipSyncLatencyMode.Custom)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_maxBufferedSecondsProp,
                        new GUIContent("Max Buffered (s)",
                            "Maximum amount (in seconds) of upcoming lip sync data to hold in memory. Higher values consume more memory but offer a deeper safety net against persistent lag."));
                    EditorGUILayout.PropertyField(_minResumeHeadroomSecondsProp,
                        new GUIContent("Resume Headroom (s)",
                            "The minimum data cushion (in seconds) that must be received after a network stall before playback resumes. Lower values feel real-time but stutter heavily on bad connections."));
                    EditorGUI.indentLevel--;
                }

                DrawDriftMonitorLink();
            });
        }

        /// <summary>
        ///     The way into the drift monitor. It sits here, under the settings that decide how far
        ///     the mouth can run from the voice, because that is the only place the measurement is
        ///     worth anything — it used to be a top-level Convai menu row, which offered a
        ///     measurement instrument to every user as if it were a setup step.
        /// </summary>
        private static void DrawDriftMonitorLink()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(DriftMonitorButtonContent, Styles.MiniButton, GUILayout.Width(150)))
                    ConvaiLipSyncDriftMonitorWindow.Open();
                GUILayout.FlexibleSpace();
            }
        }

        private static readonly GUIContent DriftMonitorButtonContent = new(
            "Open Drift Monitor",
            "Charts how far the mouth is running ahead of or behind the voice while you play, and "
            + "lets you trial an offset before you commit to it.");

        private void ApplyLatencyPresetForMode(int latencyModeValue)
        {
            if (_maxBufferedSecondsProp == null || _minResumeHeadroomSecondsProp == null) return;

            switch ((LipSyncLatencyMode)latencyModeValue)
            {
                case LipSyncLatencyMode.UltraLowLatency:
                    _maxBufferedSecondsProp.floatValue = 1f;
                    _minResumeHeadroomSecondsProp.floatValue = 0.05f;
                    break;
                case LipSyncLatencyMode.Balanced:
                    _maxBufferedSecondsProp.floatValue = 3f;
                    _minResumeHeadroomSecondsProp.floatValue = 0.12f;
                    break;
                case LipSyncLatencyMode.NetworkSafe:
                    _maxBufferedSecondsProp.floatValue = 6f;
                    _minResumeHeadroomSecondsProp.floatValue = 0.25f;
                    break;
                case LipSyncLatencyMode.Custom:
                    break;
            }
        }

        #endregion

        #region Live Status Section

        /// <summary>Draws a progress bar for played and buffered portions of the active stream window.</summary>
        private static void DrawStreamProgressBar(float elapsed, float remaining)
        {
            const float barHeight = 10f;
            const float legendHeight = 14f;

            float total = elapsed + Mathf.Max(0f, remaining);
            if (total <= 0f) return;

            float playedRatio = elapsed / total;
            float bufferedRatio = remaining / total;

            Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(barHeight));
            float w = barRect.width;

            float x = barRect.x;
            if (playedRatio > 0f)
            {
                float segW = w * playedRatio;
                EditorGUI.DrawRect(new Rect(x, barRect.y, segW, barRect.height), ConvaiGreen);
                x += segW;
            }

            if (bufferedRatio > 0f)
            {
                float segW = w * bufferedRatio;
                EditorGUI.DrawRect(new Rect(x, barRect.y, segW, barRect.height), ConvaiInfo);
            }

            GUILayout.Space(2);
            Rect legendRect = GUILayoutUtility.GetRect(1f, legendHeight);
            float legendX = legendRect.x;
            float swatchSize = 8f;
            float gap = 6f;

            void DrawLegendSwatch(Color c, string label)
            {
                EditorGUI.DrawRect(
                    new Rect(legendX, legendRect.y + ((legendRect.height - swatchSize) * 0.5f), swatchSize, swatchSize),
                    c);
                legendX += swatchSize + 4f;
                GUI.Label(new Rect(legendX, legendRect.y, 120f, legendRect.height), label, ConvaiEditorStyles.MicroLabel);
                legendX += 52f + gap;
            }

            DrawLegendSwatch(ConvaiGreen, "Played");
            DrawLegendSwatch(ConvaiInfo, "Buffered");
        }

        private void DrawLiveStatusCell(string label, string value, int cellWidth, Color valueColor,
            bool isBoldValue = false)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(cellWidth));
            GUILayout.Label(label.ToUpper(), Styles.LiveCellLabel);
            GUILayout.Label(value, Styles.LiveCellValueTinted(valueColor, isBoldValue));
            EditorGUILayout.EndVertical();
        }

        private void DrawLiveStatusSection()
        {
            if (!DrawSection(SectionLiveStatusId, "Live", ConvaiEditorGlyphs.Live, accent: ConvaiInfo)) return;

            // Accent-tinted while speaking, plain card surface otherwise — the same "this is live"
            // signal the Vision live sections use.
            Color bgColor = _component.IsPlaying
                ? ConvaiEditorTheme.Tint(ConvaiEditorTheme.Accent)
                : SectionBg;
            DrawSectionBody(() =>
            {
                EditorGUILayout.BeginHorizontal();

                PlaybackState state = _component.EngineState;
                string statusGlyph;
                string statusText;
                Color statusColor;

                switch (state)
                {
                    case PlaybackState.FadingOut:
                        statusGlyph = ConvaiEditorGlyphs.Motion;
                        statusText = "FADING OUT";
                        statusColor = ConvaiWarning;
                        break;
                    case PlaybackState.Playing:
                        statusGlyph = ConvaiEditorGlyphs.Run;
                        statusText = "PLAYING";
                        statusColor = ConvaiGreen;
                        break;
                    case PlaybackState.Starving:
                        statusGlyph = ConvaiEditorGlyphs.Status.Warn;
                        statusText = "STARVING";
                        statusColor = ConvaiWarning;
                        break;
                    case PlaybackState.Buffering:
                        statusGlyph = ConvaiEditorGlyphs.Range;
                        statusText = "BUFFERING";
                        statusColor = ConvaiInfo;
                        break;
                    default:
                        statusGlyph = ConvaiEditorGlyphs.Live;
                        statusText = "IDLE";
                        statusColor = ConvaiEditorTheme.StatusIdle;
                        break;
                }

                Color previousStatusColor = GUI.color;
                GUI.color = statusColor;
                GUILayout.Label(statusGlyph, Theme.SectionGlyph, GUILayout.Width(20));
                GUILayout.Label(statusText, Theme.SectionTitle);
                GUI.color = previousStatusColor;

                GUILayout.FlexibleSpace();

                GUILayout.Label($"[{_component.ActiveProfile}]",
                    ConvaiEditorTheme.MicroLabelRightTinted(ConvaiEditorTheme.StatusInfo));
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(12);

                float remaining = _component.GetTalkingTimeRemaining();
                float elapsed = _component.GetTalkingTimeElapsed();
                float totalStream = _component.GetTotalStreamDuration();
                float headroom = _component.GetHeadroom();
                float bufferWindow = _component.GetTotalBufferedDuration();

                float bufferWindowTotal = elapsed + Mathf.Max(0f, remaining);
                if (bufferWindowTotal > 0f)
                {
                    DrawStreamProgressBar(elapsed, remaining);
                    GUILayout.Space(6);
                }

                const int cellWidth = 90;
                Color defaultValColor = ConvaiEditorTheme.TextPrimary;
                EditorGUILayout.BeginHorizontal();
                DrawLiveStatusCell("Elapsed Time", $"{elapsed:F2} s", cellWidth, defaultValColor);
                DrawLiveStatusCell("Remaining", $"{Mathf.Max(0f, remaining):F2} s", cellWidth, defaultValColor);
                DrawLiveStatusCell("Received Data", $"{totalStream:F2} s", cellWidth, defaultValColor);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);
                EditorGUILayout.BeginHorizontal();
                Color headroomColor = headroom > 0.1f ? ConvaiGreenLight : headroom > 0f ? ConvaiWarning : ConvaiError;
                DrawLiveStatusCell("Headroom", $"{headroom * 1000f:F0} ms", cellWidth, headroomColor, headroom < 0.1f);
                DrawLiveStatusCell("Buffer Size", $"{bufferWindow:F2} s", cellWidth, ConvaiInfo);

                string talkingStr = _component.IsTalking ? "Yes" : "No";
                Color talkingColor = _component.IsTalking ? ConvaiGreenLight : defaultValColor;
                DrawLiveStatusCell("Is Talking", talkingStr, cellWidth, talkingColor, _component.IsTalking);
                EditorGUILayout.EndHorizontal();
            }, bgColor);
        }

        #endregion

        #region Helper Methods

        private void PopulateAssignedMeshes()
        {
            _tempMeshes.Clear();
            _seenMeshIds.Clear();

            if (_targetMeshesProp == null || !_targetMeshesProp.isArray) return;

            for (int i = 0; i < _targetMeshesProp.arraySize; i++)
            {
                var mesh = _targetMeshesProp.GetArrayElementAtIndex(i).objectReferenceValue as SkinnedMeshRenderer;
                if (mesh == null || !_seenMeshIds.Add(ConvaiObjectId.Of(mesh))) continue;
                _tempMeshes.Add(mesh);
            }
        }

        private int GetTotalUniqueBlendshapeCount()
        {
            _uniqueBlendshapes.Clear();
            for (int i = 0; i < _tempMeshes.Count; i++)
            {
                SkinnedMeshRenderer mesh = _tempMeshes[i];
                if (mesh == null || mesh.sharedMesh == null) continue;

                Mesh sharedMesh = mesh.sharedMesh;
                for (int j = 0; j < sharedMesh.blendShapeCount; j++)
                    _uniqueBlendshapes.Add(sharedMesh.GetBlendShapeName(j));
            }

            return _uniqueBlendshapes.Count;
        }

        private void AutoFindMeshesInHierarchy()
        {
            Undo.RecordObject(_component, "Auto-Find Lip Sync Meshes");
            AutoFindMeshes();
            EditorUtility.SetDirty(_component);
        }

        private void AutoFindMeshes()
        {
            Transform root = _component.transform;
            SkinnedMeshRenderer[] meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var discovered = new List<SkinnedMeshRenderer>();
            var seen = new HashSet<long>();
            foreach (SkinnedMeshRenderer mesh in meshes)
            {
                if (mesh == null || mesh.sharedMesh == null || mesh.sharedMesh.blendShapeCount == 0) continue;

                if (!seen.Add(ConvaiObjectId.Of(mesh))) continue;

                discovered.Add(mesh);
            }

            discovered.Sort((a, b) =>
            {
                int scoreA = GetMeshPriority(a != null ? a.name : string.Empty);
                int scoreB = GetMeshPriority(b != null ? b.name : string.Empty);
                if (scoreA != scoreB) return scoreA.CompareTo(scoreB);

                string nameA = a != null ? a.name : string.Empty;
                string nameB = b != null ? b.name : string.Empty;
                return string.CompareOrdinal(nameA, nameB);
            });

            _targetMeshesProp.ClearArray();
            for (int i = 0; i < discovered.Count; i++)
            {
                _targetMeshesProp.InsertArrayElementAtIndex(i);
                _targetMeshesProp.GetArrayElementAtIndex(i).objectReferenceValue = discovered[i];
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static int GetMeshPriority(string meshName)
        {
            string lowerName = meshName.ToLowerInvariant();
            if (lowerName.Contains("cc_base_body") || lowerName.Contains("skinhead") ||
                lowerName.Contains("head") || lowerName.Contains("face"))
                return 0;

            if (lowerName.Contains("teeth") || lowerName.Contains("tooth")) return 1;

            if (lowerName.Contains("tongue")) return 2;

            return 3;
        }

        private void CreateMappingAsset()
        {
            string defaultName = "ConvaiLipSyncMapAsset";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Lip Sync Mapping",
                defaultName,
                "asset",
                "Create a lip sync mapping asset.");
            if (string.IsNullOrWhiteSpace(path)) return;

            var asset = CreateInstance<ConvaiLipSyncMapAsset>();
            var so = new SerializedObject(asset);
            SerializedProperty targetProfileId = so.FindProperty("_targetProfileId");
            if (targetProfileId != null) targetProfileId.stringValue = GetInspectorProfile().Value;
            so.ApplyModifiedPropertiesWithoutUndo();
            asset.InitializeWithDefaults();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            _mappingProp.objectReferenceValue = asset;
            serializedObject.ApplyModifiedProperties();
            EditorGUIUtility.PingObject(asset);
        }

        private LipSyncProfileId GetInspectorProfile()
        {
            return _lockedProfileProp != null
                ? new LipSyncProfileId(_lockedProfileProp.stringValue)
                : _component.ActiveProfile;
        }

        private ConvaiLipSyncMapAsset GetProfileDefaultMap(LipSyncProfileId profile) =>
            _defaultMapRegistry != null ? _defaultMapRegistry.GetForProfile(profile) : null;

        private static string ToDisplayProfileName(LipSyncProfileId profile)
        {
            if (LipSyncProfileCatalog.TryGetProfile(profile, out ConvaiLipSyncProfile asset))
                return asset.DisplayName;

            return profile.IsValid ? profile.Value : "(none)";
        }

        private void DrawProfileSelector()
        {
            IReadOnlyList<ConvaiLipSyncProfile> profiles = LipSyncProfileCatalog.GetProfiles();
            string currentId = LipSyncProfileId.Normalize(_lockedProfileProp.stringValue);

            if (profiles == null || profiles.Count == 0)
            {
                EditorGUILayout.PropertyField(_lockedProfileProp, new GUIContent("Profile ID"));
                return;
            }

            string[] labels = new string[profiles.Count];
            int selectedIndex = -1;
            for (int i = 0; i < profiles.Count; i++)
            {
                ConvaiLipSyncProfile profile = profiles[i];
                labels[i] = profile.DisplayName;
                if (string.Equals(profile.ProfileId.Value, currentId, StringComparison.Ordinal)) selectedIndex = i;
            }

            int fallbackIndex = selectedIndex >= 0 ? selectedIndex : 0;
            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup(
                new GUIContent("Profile", "Locked rig profile for this component."),
                fallbackIndex,
                labels);
            if (EditorGUI.EndChangeCheck() && nextIndex >= 0 && nextIndex < profiles.Count)
                _lockedProfileProp.stringValue = profiles[nextIndex].ProfileId.Value;

            if (selectedIndex < 0)
            {
                WarningBox(
                    "Unknown Profile",
                    $"Profile id '{currentId}' is not registered. Pick a profile from the catalog above.");
            }
        }

        #endregion
    }
}
#endif
