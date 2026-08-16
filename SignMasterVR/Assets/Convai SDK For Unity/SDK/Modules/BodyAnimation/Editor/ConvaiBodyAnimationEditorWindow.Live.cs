using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Live mode: a deeper Play-Mode-only monitor than the inspector's compact strip — layer
    ///     weight bars, a foot-slide meter, dialogue/locomotion state, and the recent transition
    ///     log. Driven by the existing non-allocating <see cref="ConvaiBodyAnimationController.CaptureSnapshot(BodyAnimationSnapshot)" />
    ///     overload, reusing one owned <see cref="BodyAnimationSnapshot" /> instance so repeated
    ///     repaints never allocate.
    /// </summary>
    internal sealed partial class ConvaiBodyAnimationEditorWindow
    {
        private const int LiveTransitionTailLength = 24;
        private const float FootSlideWarnThreshold = 0.35f;

        private static readonly GUIContent LiveStateHeaderContent = new("Runtime State");

        private static readonly GUIContent LiveLayerWeightsHeaderContent =
            new(BodyAnimationEditorStrings.LiveLayerWeightsHeader);

        private static readonly GUIContent LiveTransitionLogHeaderContent =
            new(BodyAnimationEditorStrings.LiveTransitionLogHeader);

        private static readonly GUIContent NoLayersBuiltContent = new("No layers are built yet.");

        private readonly BodyAnimationSnapshot _snapshot = new();

        private void DrawLiveMode()
        {
            if (_controller == null)
            {
                DrawCenteredMessage(BodyAnimationEditorStrings.LiveModeNoController);
                return;
            }

            if (!UnityEngine.Application.isPlaying)
            {
                DrawCenteredMessage(BodyAnimationEditorStrings.LiveNotPlayingMessage);
                return;
            }

            if (!_controller.IsRuntimeBuilt)
            {
                DrawCenteredMessage(BodyAnimationEditorStrings.LiveNotBuiltMessage);
                return;
            }

            _controller.CaptureSnapshot(_snapshot);

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Min(760f, position.width - LeftPaneWidth - 40f))))
            {
                GUILayout.Space(6f);
                DrawStateRow();
                GUILayout.Space(10f);
                DrawLayerWeights();
                GUILayout.Space(10f);
                DrawTransitionLog();
            }
        }

        private void DrawStateRow()
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Live, LiveStateHeaderContent);

                using (new EditorGUILayout.HorizontalScope())
                {
                    ConvaiEditorControls.LiveCell(
                        BodyAnimationEditorStrings.LiveDialogueStateLabel,
                        _snapshot.DialogueState.ToString(), ConvaiEditorTheme.AccentBright, 130f, true);

                    string locomotion = string.IsNullOrEmpty(_snapshot.LocomotionState) ? "—" : _snapshot.LocomotionState;
                    ConvaiEditorControls.LiveCell(
                        BodyAnimationEditorStrings.LiveLocomotionStateLabel,
                        locomotion, ConvaiEditorTheme.StatusInfo, 130f, true);

                    float slide = Mathf.Abs(_snapshot.AgentSpeed - _snapshot.AnimationSpeed);
                    ConvaiEditorControls.LiveCell(
                        BodyAnimationEditorStrings.LiveFootSlideLabel, $"{slide:0.00} m/s",
                        slide > FootSlideWarnThreshold ? ConvaiEditorTheme.StatusWarn : ConvaiEditorTheme.TextPrimary,
                        130f, true);

                    ConvaiEditorControls.LiveCell(
                        BodyAnimationEditorStrings.LiveDesiredSpeedLabel,
                        $"{_snapshot.DesiredSpeed:0.00} m/s", ConvaiEditorTheme.TextPrimary, 130f);

                    ConvaiEditorControls.LiveCell(
                        BodyAnimationEditorStrings.LiveRemainingDistanceLabel,
                        $"{_snapshot.RemainingDistance:0.00} m", ConvaiEditorTheme.TextPrimary, 130f);
                }
            }
        }

        private void DrawLayerWeights()
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Motion, LiveLayerWeightsHeaderContent);

                for (int i = 0; i < _snapshot.Layers.Count; i++)
                {
                    BodyAnimationLayerSnapshot layer = _snapshot.Layers[i];
                    Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));

                    var labelRect = new Rect(row.x, row.y, 150f, row.height);
                    GUI.Label(labelRect, $"{layer.Name}  ·  {layer.Clip}", ConvaiEditorStyles.MicroLabel);

                    var barTrack = new Rect(labelRect.xMax + 6f, row.y + 4f, row.width - labelRect.width - 60f, 12f);
                    ConvaiEditorTheme.FillRounded(barTrack, ConvaiEditorTheme.InnerBg, 3f);
                    float weight = Mathf.Clamp01(layer.FinalWeight);
                    var barFill = new Rect(barTrack.x, barTrack.y, barTrack.width * weight, barTrack.height);
                    ConvaiEditorTheme.FillRounded(
                        barFill,
                        layer.Additive ? ConvaiEditorTheme.StatusInfo : ConvaiEditorTheme.Accent, 3f);

                    var valueRect = new Rect(barTrack.xMax + 6f, row.y, 48f, row.height);
                    GUI.Label(valueRect, $"{weight:P0}", ConvaiEditorStyles.MicroLabelRight);
                }

                if (_snapshot.Layers.Count == 0)
                    GUILayout.Label(NoLayersBuiltContent, ConvaiEditorStyles.CaptionWrapped);
            }
        }

        private void DrawTransitionLog()
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Routing, LiveTransitionLogHeaderContent);

                if (_snapshot.RecentTrace.Count == 0)
                {
                    GUILayout.Label(BodyAnimationEditorStrings.LiveTransitionLogEmpty, ConvaiEditorStyles.CaptionWrapped);
                    return;
                }

                int first = Mathf.Max(0, _snapshot.RecentTrace.Count - LiveTransitionTailLength);
                using (ConvaiEditorFrame.Panel())
                {
                    for (int i = _snapshot.RecentTrace.Count - 1; i >= first; i--)
                    {
                        AnimTraceEntry entry = _snapshot.RecentTrace[i];
                        GUILayout.Label($"[{entry.Time:0.00}s] {entry.Message}", ConvaiEditorStyles.CaptionWrapped);
                    }
                }
            }
        }
    }
}
