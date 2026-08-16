using System.Collections.Generic;
using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Content mode: the window's actual reason to exist. Authors the
    ///     resolved <see cref="ConvaiBodyAnimationSet" /> properly instead of falling through to
    ///     <c>DrawDefaultInspector</c> — pools with inline clip pickers and preview, locomotion as
    ///     a coverage grid instead of 26 bare fields, an actions/gestures list with the cue tag as
    ///     a first-class column, and pointing as a compass diagram instead of raw yaw/pitch numbers.
    /// </summary>
    /// <remarks>
    ///     Clip preview reuses the Troubleshooter window's proven <see cref="AnimationMode" />
    ///     sampler verbatim: it records/restores the pre-preview pose automatically, only ever
    ///     stops Animation Mode when this window started it, and stops on target change, Play Mode
    ///     entry, and window close (see <see cref="StopPreview" />, called from every one of those
    ///     paths in the other partial files).
    /// </remarks>
    internal sealed partial class ConvaiBodyAnimationEditorWindow
    {
        // ------------------------------------------------------------------ cached content

        private const string NoSetTitle = "No Animation Set Yet";

        private static readonly GUIContent CreateSetContent =
            new(BodyAnimationEditorStrings.CreateAnimationSetButton);

        private static readonly GUIContent MeasureClipsContent =
            new(BodyAnimationEditorStrings.MeasureClipsButton);

        private static readonly GUIContent StopPreviewContent =
            new(BodyAnimationEditorStrings.StopPreviewButton);

        private static readonly GUIContent AddEntryContent = new(BodyAnimationEditorStrings.AddEntryButton);

        private static readonly GUIContent AddActionContent = new(BodyAnimationEditorStrings.AddActionButton);

        private static readonly GUIContent AddDirectionContent = new(BodyAnimationEditorStrings.AddDirectionButton);

        private static readonly GUIContent LocomotionGridTitleContent =
            new(BodyAnimationEditorStrings.LocomotionGridTitle);

        /// <summary>Reused for section titles that carry a live count, and for coverage-cell labels.</summary>
        private static readonly GUIContent ScratchSectionTitle = new();

        private static readonly GUIContent ScratchCellLabel = new();

        // ------------------------------------------------------------------ preview state (AnimationMode)

        private Animator _previewAnimator;
        private bool _isPreviewing;
        private bool _ownsAnimationMode;
        private AnimationClip _previewClip;
        private string _previewLabel = string.Empty;
        private float _previewTime;
        private double _lastEditorTime;
        private PlayableGraph _layeredPreviewGraph;
        private AnimationClipPlayable _layeredBasePlayable;
        private AnimationClipPlayable _layeredOverlayPlayable;

        private string _expandedLocomotionCellKey = string.Empty;

        private void DrawContentMode()
        {
            if (_controller == null)
            {
                DrawCenteredMessage(BodyAnimationEditorStrings.ContentModeNoController);
                return;
            }

            ConvaiBodyAnimationSet set = BodyAnimationSetupService.ResolveAssignedSet(_controller);
            _previewAnimator = _controller.ResolveTargetAnimator();

            GUILayout.Space(6f);
            DrawContentToolbar(set);
            GUILayout.Space(8f);

            if (set == null)
            {
                ConvaiEditorFrame.InfoBox(NoSetTitle, BodyAnimationEditorStrings.ContentModeNoSet);
                return;
            }

            var serialized = new SerializedObject(set);
            serialized.Update();

            DrawMaskRow(serialized);
            GUILayout.Space(8f);

            DrawPool(serialized, BodyAnimationEditorStrings.PoolIdleTitle, "_idles", false,
                BodyAnimationEditorStrings.PoolIdleEmptyHint, set);
            DrawPool(serialized, BodyAnimationEditorStrings.PoolTalkTitle, "_talks", true,
                BodyAnimationEditorStrings.PoolTalkEmptyHint, set);
            DrawPool(serialized, BodyAnimationEditorStrings.PoolListenTitle, "_listens", true,
                BodyAnimationEditorStrings.PoolListenEmptyHint, set);
            DrawPool(serialized, BodyAnimationEditorStrings.PoolThinkTitle, "_thinks", true,
                BodyAnimationEditorStrings.PoolThinkEmptyHint, set);

            GUILayout.Space(10f);
            DrawLocomotionCoverage(serialized, set);

            GUILayout.Space(10f);
            DrawActionsList(serialized);

            GUILayout.Space(10f);
            DrawPointingSection(serialized, set);

            serialized.ApplyModifiedProperties();

            if (_isPreviewing)
            {
                GUILayout.Space(8f);
                using (ConvaiEditorFrame.Panel(ConvaiEditorTheme.Accent, 6f, 2f))
                {
                    Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                    ConvaiEditorTheme.StatusDot(
                        new Vector2(row.x + 6f, row.y + (row.height * 0.5f)), ConvaiEditorTheme.Accent, true);

                    var stop = new Rect(row.xMax - 60f, row.y, 60f, row.height);
                    GUI.Label(
                        new Rect(row.x + 18f, row.y, Mathf.Max(40f, stop.x - row.x - 24f), row.height),
                        $"Previewing: {_previewLabel}", ConvaiEditorStyles.MicroLabel);

                    if (ConvaiEditorControls.GhostButton(stop, StopPreviewContent))
                        StopPreview();
                }
            }
        }

        private void DrawContentToolbar(ConvaiBodyAnimationSet set)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect create = GUILayoutUtility.GetRect(150f, 24f, GUILayout.Width(150f));
                if (ConvaiEditorControls.GhostButton(create, CreateSetContent))
                    ConvaiBodyAnimationSetBuilderWindow.Open();

                GUILayout.Space(6f);

                using (new EditorGUI.DisabledScope(set == null))
                {
                    Rect measure = GUILayoutUtility.GetRect(130f, 24f, GUILayout.Width(130f));
                    if (ConvaiEditorControls.GhostButton(measure, MeasureClipsContent))
                        MeasureClips(set);
                }

                GUILayout.FlexibleSpace();
            }

            if (set != null)
            {
                GUILayout.Space(4f);
                GUILayout.Label(
                    BodyAnimationFixes.DescribeLocomotionCoverage(set), ConvaiEditorStyles.CaptionWrapped);
            }
        }

        /// <summary>
        ///     Measures clip metadata after the current IMGUI pass. The confirmation is modal, and a
        ///     modal raised from inside a layout scope discards the layout state the enclosing scope is
        ///     about to close — which leaves the window throwing on every later repaint.
        /// </summary>
        private static void MeasureClips(ConvaiBodyAnimationSet set)
        {
            EditorApplication.delayCall += () =>
            {
                if (set == null) return;

                BodyAnimationFixes.ApplyToSet(set, BodyAnimationFixId.AnalyzeClipMetadata);
                EditorUtility.DisplayDialog(
                    BodyAnimationEditorStrings.MeasureClipsDoneTitle,
                    BodyAnimationEditorStrings.MeasureClipsDoneBody, "OK");
            };
        }

        private static void DrawMaskRow(SerializedObject serialized)
        {
            SerializedProperty maskProp = serialized.FindProperty("_upperBodyMask");
            if (maskProp == null) return;
            EditorGUILayout.PropertyField(maskProp, new GUIContent("Upper Body Mask",
                "Mask used by upper-body talk, gestures, and pointing. Legs and root must stay disabled."));
        }

        // ------------------------------------------------------------------ pools (Idle/Talk/Listen/Think)

        private void DrawPool(
            SerializedObject serialized, string title, string arrayFieldName,
            bool hasAdditive, string emptyHint, ConvaiBodyAnimationSet set)
        {
            SerializedProperty arrayProp = serialized.FindProperty(arrayFieldName);
            if (arrayProp == null) return;

            ConvaiEditorFrame.BeginCard();
            ScratchSectionTitle.text = $"{title}  ({arrayProp.arraySize})";
            ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Content, ScratchSectionTitle);

            if (arrayProp.arraySize == 0)
                GUILayout.Label(emptyHint, ConvaiEditorStyles.CaptionWrapped);

            int removeIndex = -1;
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                SerializedProperty element = arrayProp.GetArrayElementAtIndex(i);
                SerializedProperty clipProp = element.FindPropertyRelative("_clip");
                SerializedProperty weightProp = element.FindPropertyRelative("_weight");
                SerializedProperty additiveProp = hasAdditive ? element.FindPropertyRelative("_additive") : null;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(clipProp, GUIContent.none, GUILayout.MinWidth(160f));
                    GUILayout.Label(BodyAnimationEditorStrings.WeightFieldLabel, GUILayout.Width(46f));
                    if (weightProp != null)
                        weightProp.floatValue = EditorGUILayout.FloatField(weightProp.floatValue, GUILayout.Width(40f));

                    if (additiveProp != null)
                        additiveProp.boolValue = EditorGUILayout.ToggleLeft(
                            BodyAnimationEditorStrings.AdditiveFieldLabel, additiveProp.boolValue, GUILayout.Width(70f));

                    DrawLoopStatus(clipProp.objectReferenceValue as AnimationClip);

                    var clip = clipProp.objectReferenceValue as AnimationClip;
                    using (new EditorGUI.DisabledScope(clip == null || _previewAnimator == null))
                    {
                        bool isThisPlaying = _isPreviewing && clip != null && _previewClip == clip;
                        if (GUILayout.Button(
                                isThisPlaying ? BodyAnimationEditorStrings.StopPreviewButton : BodyAnimationEditorStrings.PreviewButton,
                                ConvaiEditorStyles.MiniButton, GUILayout.Width(56f)))
                        {
                            if (isThisPlaying) StopPreview();
                            else StartPreview(clip, $"{title} [{i}]");
                        }
                    }

                    if (hasAdditive)
                    {
                        AnimationClip idleClip = FirstValidClip(set.Idles);
                        using (new EditorGUI.DisabledScope(clip == null || idleClip == null || _previewAnimator == null))
                        {
                            if (GUILayout.Button(
                                    BodyAnimationEditorStrings.LayeredPreviewButton,
                                    ConvaiEditorStyles.MiniButton, GUILayout.Width(110f)))
                                StartLayeredPreview(idleClip, clip, set.UpperBodyMask, additiveProp != null && additiveProp.boolValue, $"Idle + {title} [{i}]");
                        }
                    }

                    if (GUILayout.Button(
                            BodyAnimationEditorStrings.RemoveEntryButton,
                            ConvaiEditorStyles.MiniButton, GUILayout.Width(22f)))
                        removeIndex = i;
                }

                // Walk-and-talk tier. The additive twin decides what "Best available" actually
                // plays while the character is moving, and it used to be invisible here — an
                // entry looked complete while its moving tier silently fell back.
                SerializedProperty movingClipProp = hasAdditive ? element.FindPropertyRelative("_additiveClip") : null;
                if (movingClipProp != null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(14f);
                        GUILayout.Label(BodyAnimationEditorStrings.MovingTalkClipLabel, GUILayout.Width(110f));
                        EditorGUILayout.PropertyField(movingClipProp, GUIContent.none, GUILayout.MinWidth(160f));
                    }

                    GUILayout.Label(
                        DescribeMovingTalkTier(
                            movingClipProp.objectReferenceValue as AnimationClip,
                            additiveProp != null && additiveProp.boolValue),
                        ConvaiEditorStyles.CaptionWrapped);
                }
            }

            if (removeIndex >= 0) arrayProp.DeleteArrayElementAtIndex(removeIndex);

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect add = GUILayoutUtility.GetRect(70f, 20f, GUILayout.Width(70f));
                if (ConvaiEditorControls.GhostButton(add, AddEntryContent))
                    arrayProp.arraySize++;
                GUILayout.FlexibleSpace();
            }

            ConvaiEditorFrame.EndCard(8f);
        }

        /// <summary>
        ///     Mirrors <see cref="TalkEntry.ResolveMovingClip" />: which walk-and-talk tier this
        ///     entry resolves to under the "Best available" moving-talk mode.
        /// </summary>
        private static string DescribeMovingTalkTier(AnimationClip additiveTwin, bool mainClipIsAdditive)
        {
            if (additiveTwin != null) return BodyAnimationEditorStrings.MovingTalkTierAdditive;
            return mainClipIsAdditive
                ? BodyAnimationEditorStrings.MovingTalkTierSelfAdditive
                : BodyAnimationEditorStrings.MovingTalkTierFallback;
        }

        private static AnimationClip FirstValidClip(IReadOnlyList<IdleEntry> idles)
        {
            for (int i = 0; i < idles.Count; i++)
            {
                if (idles[i]?.Clip != null) return idles[i].Clip;
            }
            return null;
        }

        private static void DrawLoopStatus(AnimationClip clip)
        {
            string label = clip == null
                ? BodyAnimationEditorStrings.LoopNoneLabel
                : clip.isLooping ? BodyAnimationEditorStrings.LoopOkLabel : BodyAnimationEditorStrings.LoopBadLabel;
            Color color = clip == null
                ? ConvaiEditorTheme.TextMuted
                : clip.isLooping ? ConvaiEditorTheme.StatusReady : ConvaiEditorTheme.StatusWarn;

            GUILayout.Label(label, ConvaiEditorStyles.MicroLabelRightTinted(color), GUILayout.Width(84f));
        }

        // ------------------------------------------------------------------ locomotion coverage grid

        // The coverage table itself lives in BodyAnimationContentCoverage, so this window and the
        // Convai.InspectBodyAnimationContent tool report the same slots and the same gaps. This
        // file owns only how they are drawn.

        private void DrawLocomotionCoverage(SerializedObject serialized, ConvaiBodyAnimationSet set)
        {
            SerializedProperty locomotionProp = serialized.FindProperty("_locomotion");
            if (locomotionProp == null) return;

            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Motion, LocomotionGridTitleContent);
                GUILayout.Label(BodyAnimationEditorStrings.LocomotionGridIntro, ConvaiEditorStyles.MutedWrapped);
                GUILayout.Space(4f);

                DrawLocomotionRow(locomotionProp, BodyAnimationEditorStrings.LocomotionRowWalk,
                    BodyAnimationContentCoverage.WalkCells);
                GUILayout.Space(6f);
                DrawLocomotionRow(locomotionProp, BodyAnimationEditorStrings.LocomotionRowJog,
                    BodyAnimationContentCoverage.JogCells);
            }
        }

        private void DrawLocomotionRow(
            SerializedProperty locomotionProp, string rowLabel, LocomotionCoverageCell[] cells)
        {
            ConvaiEditorControls.GroupCaption(rowLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                for (int c = 0; c < cells.Length; c++)
                    DrawLocomotionCellChip(locomotionProp, rowLabel, cells[c]);
            }

            for (int c = 0; c < cells.Length; c++)
            {
                string key = $"{rowLabel}.{cells[c].ColumnLabel}";
                if (key != _expandedLocomotionCellKey) continue;
                DrawLocomotionCellDetail(locomotionProp, cells[c]);
            }
        }

        private void DrawLocomotionCellChip(
            SerializedProperty locomotionProp, string rowLabel, LocomotionCoverageCell cell)
        {
            int filled = BodyAnimationContentCoverage.CountFilled(locomotionProp, cell);
            int total = cell.Slots.Length;
            string key = $"{rowLabel}.{cell.ColumnLabel}";
            bool expanded = key == _expandedLocomotionCellKey;

            Color color = total == 0 ? ConvaiEditorTheme.TextMuted
                : filled == 0 ? ConvaiEditorTheme.TextMuted
                : filled == total ? ConvaiEditorTheme.StatusReady
                : ConvaiEditorTheme.StatusWarn;

            ScratchCellLabel.text = total == 0
                ? cell.ColumnLabel
                : $"{cell.ColumnLabel}\n{BodyAnimationEditorStrings.BuildLocomotionCellLabel(filled, total)}";
            GUIContent content = ScratchCellLabel;
            Rect rect = GUILayoutUtility.GetRect(120f, 38f, GUILayout.Width(120f), GUILayout.Height(38f));

            ConvaiEditorTheme.FillRounded(rect, expanded ? ConvaiEditorTheme.CardBgSelected : ConvaiEditorTheme.CardBg, 5f);
            ConvaiEditorTheme.StrokeRounded(rect, ConvaiEditorTheme.TintBorder(color), 5f);

            GUIStyle style = ConvaiEditorTheme.CenteredMini(color);
            if (total > 0 && GUI.Button(rect, content, style))
                _expandedLocomotionCellKey = expanded ? string.Empty : key;
            else if (total == 0)
                GUI.Label(rect, content, style);
        }

        private void DrawLocomotionCellDetail(SerializedProperty locomotionProp, LocomotionCoverageCell cell)
        {
            using (ConvaiEditorFrame.Panel())
            {
                int filled = BodyAnimationContentCoverage.CountFilled(locomotionProp, cell);
                if (filled == 0)
                    GUILayout.Label(cell.DisabledFeatureText, ConvaiEditorStyles.CaptionWrapped);

                for (int s = 0; s < cell.Slots.Length; s++)
                {
                    LocomotionSlotRef slot = cell.Slots[s];
                    SerializedProperty clipProp =
                        BodyAnimationContentCoverage.ClipPropertyFor(locomotionProp, slot.FieldName);
                    if (clipProp == null) continue;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(slot.Label, GUILayout.Width(110f));
                        EditorGUILayout.PropertyField(clipProp, GUIContent.none);
                        DrawLoopStatus(clipProp.objectReferenceValue as AnimationClip);

                        var clip = clipProp.objectReferenceValue as AnimationClip;
                        using (new EditorGUI.DisabledScope(clip == null || _previewAnimator == null))
                        {
                            bool isThisPlaying = _isPreviewing && clip != null && _previewClip == clip;
                            if (GUILayout.Button(
                                    isThisPlaying ? BodyAnimationEditorStrings.StopPreviewButton : BodyAnimationEditorStrings.PreviewButton,
                                    ConvaiEditorStyles.MiniButton, GUILayout.Width(56f)))
                            {
                                if (isThisPlaying) StopPreview();
                                else StartPreview(clip, slot.Label);
                            }
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ actions & gestures

        private void DrawActionsList(SerializedObject serialized)
        {
            SerializedProperty arrayProp = serialized.FindProperty("_actions");
            if (arrayProp == null) return;

            ConvaiEditorFrame.BeginCard();
            ScratchSectionTitle.text = $"{BodyAnimationEditorStrings.ActionsListTitle}  ({arrayProp.arraySize})";
            ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Command, ScratchSectionTitle);

            if (arrayProp.arraySize == 0)
                GUILayout.Label(BodyAnimationEditorStrings.ActionsEmptyHint, ConvaiEditorStyles.CaptionWrapped);
            else
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(BodyAnimationEditorStrings.ActionsColName, ConvaiEditorStyles.TableHeaderLabel, GUILayout.Width(140f));
                    GUILayout.Label(BodyAnimationEditorStrings.ActionsColClip, ConvaiEditorStyles.TableHeaderLabel, GUILayout.MinWidth(140f));
                    GUILayout.Label(BodyAnimationEditorStrings.ActionsColCue, ConvaiEditorStyles.TableHeaderLabel, GUILayout.Width(110f));
                    GUILayout.Space(24f);
                }

            int removeIndex = -1;
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                SerializedProperty element = arrayProp.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = element.FindPropertyRelative("_actionName");
                SerializedProperty clipProp = element.FindPropertyRelative("_clip");
                SerializedProperty cueProp = element.FindPropertyRelative("_cue");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (nameProp != null)
                        nameProp.stringValue = EditorGUILayout.TextField(nameProp.stringValue, GUILayout.Width(140f));
                    if (clipProp != null)
                        EditorGUILayout.PropertyField(clipProp, GUIContent.none, GUILayout.MinWidth(140f));
                    if (cueProp != null)
                        EditorGUILayout.PropertyField(cueProp, GUIContent.none, GUILayout.Width(110f));

                    var clip = clipProp?.objectReferenceValue as AnimationClip;
                    using (new EditorGUI.DisabledScope(clip == null || _previewAnimator == null))
                    {
                        bool isThisPlaying = _isPreviewing && clip != null && _previewClip == clip;
                        if (GUILayout.Button(isThisPlaying ? BodyAnimationEditorStrings.StopPreviewButton : BodyAnimationEditorStrings.PreviewButton, GUILayout.Width(56f)))
                        {
                            if (isThisPlaying) StopPreview();
                            else StartPreview(clip, nameProp != null ? nameProp.stringValue : $"Action[{i}]");
                        }
                    }

                    if (GUILayout.Button(
                            BodyAnimationEditorStrings.RemoveEntryButton,
                            ConvaiEditorStyles.MiniButton, GUILayout.Width(22f)))
                        removeIndex = i;
                }
            }

            if (removeIndex >= 0) arrayProp.DeleteArrayElementAtIndex(removeIndex);

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect add = GUILayoutUtility.GetRect(120f, 20f, GUILayout.Width(120f));
                if (ConvaiEditorControls.GhostButton(add, AddActionContent))
                    arrayProp.arraySize++;
                GUILayout.FlexibleSpace();
            }

            ConvaiEditorFrame.EndCard(8f);
        }

        // ------------------------------------------------------------------ pointing compass

        private void DrawPointingSection(SerializedObject serialized, ConvaiBodyAnimationSet set)
        {
            SerializedProperty pointingProp = serialized.FindProperty("_pointing");
            SerializedProperty entriesProp = pointingProp?.FindPropertyRelative("_entries");
            if (entriesProp == null) return;

            ConvaiEditorFrame.BeginCard();
            ScratchSectionTitle.text = $"{BodyAnimationEditorStrings.PointingTitle}  ({entriesProp.arraySize})";
            ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Placement, ScratchSectionTitle);

            if (entriesProp.arraySize == 0)
            {
                GUILayout.Label(BodyAnimationEditorStrings.PointingEmptyHint, ConvaiEditorStyles.CaptionWrapped);
            }
            else
            {
                DrawPointingCompass(set);
            }

            GUILayout.Space(6f);

            int removeIndex = -1;
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                SerializedProperty element = entriesProp.GetArrayElementAtIndex(i);
                SerializedProperty clipProp = element.FindPropertyRelative("_clip");
                SerializedProperty yawProp = element.FindPropertyRelative("_yawDegrees");
                SerializedProperty pitchProp = element.FindPropertyRelative("_pitchDegrees");

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(clipProp, GUIContent.none, GUILayout.MinWidth(140f));
                    GUILayout.Label("Yaw", GUILayout.Width(30f));
                    if (yawProp != null) yawProp.floatValue = EditorGUILayout.Slider(yawProp.floatValue, -180f, 180f, GUILayout.Width(120f));
                    GUILayout.Label("Pitch", GUILayout.Width(34f));
                    if (pitchProp != null) pitchProp.floatValue = EditorGUILayout.Slider(pitchProp.floatValue, -90f, 90f, GUILayout.Width(120f));

                    if (GUILayout.Button(
                            BodyAnimationEditorStrings.RemoveEntryButton,
                            ConvaiEditorStyles.MiniButton, GUILayout.Width(22f)))
                        removeIndex = i;
                }
            }

            if (removeIndex >= 0) entriesProp.DeleteArrayElementAtIndex(removeIndex);

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect add = GUILayoutUtility.GetRect(130f, 20f, GUILayout.Width(130f));
                if (ConvaiEditorControls.GhostButton(add, AddDirectionContent))
                    entriesProp.arraySize++;
                GUILayout.FlexibleSpace();
            }

            ConvaiEditorFrame.EndCard(8f);
        }

        /// <summary>
        ///     Radar-style compass: angle = yaw (0 = forward, top of the circle), radial position
        ///     encodes pitch (up entries sit toward the rim, down entries toward the centre) — a
        ///     spatial read of "which directions are covered" instead of a table of numbers.
        /// </summary>
        private static void DrawPointingCompass(ConvaiBodyAnimationSet set)
        {
            const float diameter = 150f;
            Rect area = GUILayoutUtility.GetRect(diameter, diameter, GUILayout.Width(diameter), GUILayout.Height(diameter));
            Vector2 center = area.center;
            const float radius = diameter * 0.5f - 12f;

            ConvaiEditorTheme.StrokeRounded(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f),
                ConvaiEditorTheme.CardBorder, radius);
            ConvaiEditorTheme.StrokeRounded(new Rect(center.x - radius * 0.5f, center.y - radius * 0.5f, radius, radius),
                ConvaiEditorTheme.CardBorder, radius * 0.5f);

            if (Event.current.type == EventType.Repaint)
            {
                GUIStyle compassStyle = ConvaiEditorTheme.CenteredMini(ConvaiEditorTheme.TextMuted);
                GUI.Label(new Rect(center.x - 24f, area.y - 2f, 48f, 14f), BodyAnimationEditorStrings.PointingCompassFront, compassStyle);
                GUI.Label(new Rect(center.x - 24f, area.yMax - 12f, 48f, 14f), BodyAnimationEditorStrings.PointingCompassBack, compassStyle);
                GUI.Label(new Rect(area.x - 4f, center.y - 7f, 40f, 14f), BodyAnimationEditorStrings.PointingCompassLeft, compassStyle);
                GUI.Label(new Rect(area.xMax - 36f, center.y - 7f, 40f, 14f), BodyAnimationEditorStrings.PointingCompassRight, compassStyle);
            }

            IReadOnlyList<PointingEntry> entries = set.Pointing.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                PointingEntry entry = entries[i];
                if (entry == null || !entry.IsValid) continue;

                float yawRad = entry.YawDegrees * Mathf.Deg2Rad;
                float pitchFactor = Mathf.Clamp01(1f - Mathf.Abs(entry.PitchDegrees) / 90f * 0.5f);
                float r = radius * pitchFactor;
                Vector2 pos = center + new Vector2(Mathf.Sin(yawRad) * r, -Mathf.Cos(yawRad) * r);

                Color dotColor = entry.PitchDegrees > 10f ? ConvaiEditorTheme.Info
                    : entry.PitchDegrees < -10f ? ConvaiEditorTheme.Warning
                    : ConvaiEditorTheme.AccentBright;
                ConvaiEditorTheme.FillCircle(pos, 4f, dotColor);
            }
        }

        // ------------------------------------------------------------------ AnimationMode preview (verbatim from the Troubleshooter window)

        private void StartPreview(AnimationClip clip, string label)
        {
            if (clip == null || _previewAnimator == null) return;

            StopPreview();

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
                _ownsAnimationMode = true;
            }

            _isPreviewing = true;
            _previewClip = clip;
            _previewLabel = label;
            _previewTime = 0f;
            _lastEditorTime = EditorApplication.timeSinceStartup;

            EditorApplication.update += TickPreview;
            SampleCurrent();
        }

        private void StartLayeredPreview(AnimationClip baseClip, AnimationClip overlayClip, AvatarMask mask, bool additive, string label)
        {
            if (baseClip == null || overlayClip == null || _previewAnimator == null) return;
            StopPreview();

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
                _ownsAnimationMode = true;
            }

            _layeredPreviewGraph = PlayableGraph.Create("ConvaiBodyAnimation.EditorPreview");
            _layeredPreviewGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var mixer = AnimationLayerMixerPlayable.Create(_layeredPreviewGraph, 2);
            _layeredBasePlayable = AnimationClipPlayable.Create(_layeredPreviewGraph, baseClip);
            _layeredOverlayPlayable = AnimationClipPlayable.Create(_layeredPreviewGraph, overlayClip);
            _layeredBasePlayable.SetSpeed(0d);
            _layeredOverlayPlayable.SetSpeed(0d);
            _layeredPreviewGraph.Connect(_layeredBasePlayable, 0, mixer, 0);
            _layeredPreviewGraph.Connect(_layeredOverlayPlayable, 0, mixer, 1);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, 1f);
            mixer.SetLayerAdditive(1u, additive);
            if (mask != null) mixer.SetLayerMaskFromAvatarMask(1u, mask);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_layeredPreviewGraph, "Body Animation Editor Preview", _previewAnimator);
            output.SetSourcePlayable(mixer);
            _layeredPreviewGraph.Play();

            _isPreviewing = true;
            _previewClip = overlayClip;
            _previewLabel = label;
            _previewTime = 0f;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += TickPreview;
            SampleCurrent();
        }

        private void TickPreview()
        {
            if (!_isPreviewing || _previewClip == null || _previewAnimator == null || UnityEngine.Application.isPlaying)
            {
                StopPreview();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float deltaTime = (float)(now - _lastEditorTime);
            _lastEditorTime = now;

            float length = Mathf.Max(0.0001f, _previewClip.length);
            _previewTime += deltaTime;
            if (_previewClip.isLooping) _previewTime %= length;
            else if (_previewTime > length) _previewTime = length;

            SampleCurrent();
            Repaint();
        }

        private void SampleCurrent()
        {
            if (_previewAnimator == null || _previewClip == null) return;

            AnimationMode.BeginSampling();
            if (_layeredPreviewGraph.IsValid())
            {
                _layeredBasePlayable.SetTime(_previewTime);
                _layeredOverlayPlayable.SetTime(_previewTime);
                _layeredPreviewGraph.Evaluate(0f);
            }
            else
            {
                AnimationMode.SampleAnimationClip(_previewAnimator.gameObject, _previewClip, _previewTime);
            }
            AnimationMode.EndSampling();
            SceneView.RepaintAll();
        }

        private void StopPreview()
        {
            bool wasPreviewing = _isPreviewing;

            EditorApplication.update -= TickPreview;
            _isPreviewing = false;
            _previewClip = null;
            _previewLabel = string.Empty;
            if (_layeredPreviewGraph.IsValid()) _layeredPreviewGraph.Destroy();

            // Only stop Animation Mode when this window was the one driving it — never step on an
            // unrelated tool's active Animation Mode session (e.g. the Animation window).
            if (wasPreviewing && _ownsAnimationMode && AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
            _ownsAnimationMode = false;

            SceneView.RepaintAll();
        }
    }
}
