using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.Ownership;
using Convai.Editor.UI;
using Convai.Modules.Emotion.Profiles;
using Convai.Runtime.Animation;
using Convai.Runtime.Embodiment;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    internal sealed partial class ConvaiEmotionEditorWindow
    {
        private readonly Dictionary<string, bool> _sectionExpanded = new();

        // ------------------------------------------------------------------ shared scaffolding

        private void DrawModeIntro(string text)
        {
            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                GUILayout.Label(text, ConvaiEditorTheme.BodyWrapped);
                GUILayout.Space(14f);
            }
            GUILayout.Space(6f);
        }

        /// <summary>
        ///     Draws one section of the shared emotion config table in this window's Convai editor frame —
        ///     the same section card the profile inspector draws, so the two surfaces that render this
        ///     table look identical. The window owns its own fold state because it is a
        ///     <see cref="EditorWindow" />, not a Convai inspector with persisted section state.
        /// </summary>
        private void DrawConfigSection(EmotionConfigSection section, Action drawFields)
        {
            bool expanded = _sectionExpanded.TryGetValue(section.Id, out bool stored)
                ? stored
                : section.ExpandedByDefault;

            using (ConvaiEditorFrame.Card())
            {
                expanded = ConvaiEditorFrame.SectionHeaderRow(
                    EmotionConfigDrawer.GlyphFor(section.Id), section.Title, expanded, ConvaiEditorTheme.Accent);
                _sectionExpanded[section.Id] = expanded;

                if (expanded)
                    using (ConvaiEditorSections.Body())
                        drawFields();
            }
        }

        /// <summary>Draws the "pick a character first" state every mode shares.</summary>
        private bool RequireController()
        {
            if (_controller != null) return true;

            DrawModeIntro("Select a character on the left.");
            return false;
        }

        /// <summary>
        ///     Draws the "this character has no personality asset" state, and reports whether the
        ///     caller should stop. Modes that edit profile fields have nothing to show without one.
        /// </summary>
        private bool RequireProfile(out ConvaiEmotionProfile profile)
        {
            profile = EmotionSetupService.ResolveAssignedProfile(_controller);
            if (profile != null) return true;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (ConvaiEditorFrame.Panel())
                {
                    GUILayout.Label(EmotionEditorStrings.NoProfileTitle, ConvaiEditorStyles.SectionTitle);
                    GUILayout.Label(EmotionEditorStrings.NoProfileBody,
                        ConvaiEditorTheme.CaptionWrapped);
                    if (GUILayout.Button("Select This Character", GUILayout.Height(22f)))
                        Selection.activeGameObject = _controller.gameObject;
                }
                GUILayout.Space(14f);
            }
            return false;
        }

        // ------------------------------------------------------------------ setup

        private void DrawSetupMode()
        {
            if (!RequireController()) return;
            DrawModeIntro(EmotionEditorStrings.SetupIntro);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (new EditorGUILayout.VerticalScope())
                {
                    for (int i = 0; i < _preflight.Checks.Count; i++) DrawCheckRow(_preflight.Checks[i]);

                    GUILayout.Space(10f);

                    if (_findings.Count == 0)
                    {
                        GUILayout.Label("Nothing needs attention.", ConvaiEditorTheme.CaptionWrapped);
                    }
                    else
                    {
                        for (int i = 0; i < _findings.Count; i++) DrawFindingRow(_findings[i]);
                    }
                }
                GUILayout.Space(14f);
            }
        }

        private static void DrawCheckRow(EmotionCheck check)
        {
            (string glyph, Color color) = check.State switch
            {
                EmotionCheckState.Ok => (ConvaiEditorGlyphs.Status.Ok, ConvaiEditorTheme.AccentBright),
                EmotionCheckState.Fixable => (ConvaiEditorGlyphs.Status.Fixable, ConvaiEditorTheme.Info),
                EmotionCheckState.Blocked => (ConvaiEditorGlyphs.Status.Fail, ConvaiEditorTheme.Error),
                _ => (ConvaiEditorGlyphs.Status.Neutral, ConvaiEditorTheme.TextMuted)
            };

            using (new EditorGUILayout.HorizontalScope())
            {
                Color previous = GUI.color;
                GUI.color = color;
                GUILayout.Label(glyph, GUILayout.Width(16f));
                GUI.color = previous;

                GUILayout.Label(check.Label, ConvaiEditorStyles.RowLabel, GUILayout.Width(90f));
                GUILayout.Label(check.Detail, ConvaiEditorTheme.CaptionWrapped);
            }
        }

        private void DrawFindingRow(EmotionFinding finding)
        {
            using (ConvaiEditorFrame.Panel())
            {
                Color previous = GUI.color;
                GUI.color = finding.Severity switch
                {
                    EmotionSeverity.Error => ConvaiEditorTheme.Error,
                    EmotionSeverity.Warning => ConvaiEditorTheme.Warning,
                    _ => ConvaiEditorTheme.Info
                };
                GUILayout.Label(finding.Title, ConvaiEditorStyles.SectionTitle);
                GUI.color = previous;

                GUILayout.Label(finding.Message, ConvaiEditorTheme.CaptionWrapped);

                string fixLabel = EmotionTroubleshooter.DescribeFix(finding.Fix);
                if (fixLabel == null) return;

                if (GUILayout.Button(fixLabel, GUILayout.Height(20f)))
                    EmotionTroubleshooter.ApplyFix(_controller, finding.Fix);
            }
        }

        // ------------------------------------------------------------------ feel

        /// <summary>
        ///     Every setting, rendered from the same <see cref="EmotionConfigSections" /> table the
        ///     profile-asset inspector uses, so the two surfaces can never drift apart.
        /// </summary>
        private void DrawFeelMode()
        {
            if (!RequireController()) return;
            DrawModeIntro(EmotionEditorStrings.FeelIntro);
            if (!RequireProfile(out ConvaiEmotionProfile profile)) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (new EditorGUILayout.VerticalScope())
                {
                    // Same promise the inspector makes: a character still on a personality the
                    // SDK ships gets its own copy the moment anything here changes.
                    using var edit = ConvaiOwnedEdit.Begin(profile, _controller, EmotionPersonality.Copier);
                    using var readOnly = new EditorGUI.DisabledScope(!edit.CanEdit);

                    EmotionPersonality.DrawArchetypeRow(profile, _controller);
                    GUILayout.Space(8f);

                    EmotionConfigDrawer.DrawAllSections(edit.Serialized, DrawConfigSection);
                }
                GUILayout.Space(14f);
            }
        }

        // ------------------------------------------------------------------ expressions

        /// <summary>
        ///     The content surface: the authored recipes, and — the reason this mode exists — the
        ///     mapping actually resolved against this character's own mesh.
        /// </summary>
        /// <remarks>
        ///     Removing the manual blendshape slot list took away the one place a user could see
        ///     what drives their face. Showing the resolved mapping puts that back, without
        ///     reintroducing an authoring surface that has to be maintained per rig.
        /// </remarks>
        private void DrawExpressionsMode()
        {
            if (!RequireController()) return;
            DrawModeIntro(EmotionEditorStrings.ExpressionsIntro);
            if (!RequireProfile(out ConvaiEmotionProfile profile)) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawResolvedFaceMapping();

                    GUILayout.Space(10f);
                    GUILayout.Label("Authored expressions", ConvaiEditorStyles.SectionTitle);

                    using var edit = ConvaiOwnedEdit.Begin(profile, _controller, EmotionPersonality.Copier);
                    using var readOnly = new EditorGUI.DisabledScope(!edit.CanEdit);

                    SerializedObject serialized = edit.Serialized;
                    SerializedProperty recipes = serialized.FindProperty("expressionRecipes");
                    if (recipes.arraySize == 0)
                    {
                        GUILayout.Label(
                            "None authored, so Convai's built-in expressions are used. Add one only to " +
                            "art-direct a specific emotion for this character.",
                            ConvaiEditorTheme.CaptionWrapped);
                    }

                    EditorGUILayout.PropertyField(recipes, EmotionConfigLabels.For(recipes), true);

                    GUILayout.Space(8f);
                    SerializedProperty material = serialized.FindProperty("materialBinding");
                    EditorGUILayout.PropertyField(material, EmotionConfigLabels.For(material), true);
                }
                GUILayout.Space(14f);
            }
        }

        /// <summary>
        ///     What Convai resolved on this character's actual face, so the mapping is verifiable
        ///     rather than a black box.
        /// </summary>
        private void DrawResolvedFaceMapping()
        {
            GUILayout.Label("This character's face", ConvaiEditorStyles.SectionTitle);

            string conventionText = _preflight.Convention == RigConvention.Unknown
                ? "Not recognised"
                : $"{EmotionSetupService.DescribeConvention(_preflight.Convention)} " +
                  $"({Mathf.RoundToInt(_preflight.ConventionConfidence * 100f)}% match)";

            EditorGUILayout.LabelField("Naming convention", conventionText);
            EditorGUILayout.LabelField("Shapes available", _preflight.ResolvedShapeCount.ToString());

            if (_preflight.Convention == RigConvention.Unknown)
            {
                GUILayout.Label(
                    "Convai could not match these blendshape names to a convention it knows, so most " +
                    "expressions have nothing to drive. Assign a Custom Rig Convention Map to map them " +
                    "yourself.",
                    ConvaiEditorTheme.CaptionWrapped);
                return;
            }

            List<SkinnedMeshRenderer> meshes = EmotionSetupService.CollectFaceMeshes(_controller);
            if (meshes.Count == 0) return;

            ConvaiEditorControls.GroupCaption("Meshes");
            for (int i = 0; i < meshes.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(10f);
                    GUILayout.Label($"{meshes[i].name} · {meshes[i].sharedMesh.blendShapeCount} shapes",
                        ConvaiEditorTheme.CaptionWrapped);
                }
            }
        }

        // ------------------------------------------------------------------ live

        private void DrawLiveMode()
        {
            if (!RequireController()) return;

            if (!UnityEngine.Application.isPlaying)
            {
                DrawModeIntro(EmotionEditorStrings.LiveIntro);
                DrawTimelineLink();
                return;
            }

            DrawModeIntro("What this character is feeling right now.");
            DrawTimelineLink();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                using (new EditorGUILayout.VerticalScope())
                {
                    EmotionReading reading = _controller.Current;

                    EditorGUILayout.LabelField("Feeling", reading.IsNeutral ? "—" : reading.DominantLabel);
                    EditorGUILayout.LabelField("Strength", reading.DominantScore.ToString("0.00"));
                    EditorGUILayout.LabelField("Held for", $"{reading.DominantHoldSeconds:0.0}s");
                    EditorGUILayout.LabelField("Resting mood",
                        reading.MoodScore > 0.01f
                            ? $"{reading.MoodLabel} ({reading.MoodScore:0.00})"
                            : "neutral");
                    EditorGUILayout.LabelField("Mouth influence", reading.MouthInfluence.ToString("0.00"));

                    GUILayout.Space(8f);
                    GUILayout.Label("Everything it is feeling", ConvaiEditorStyles.SectionTitle);
                    DrawScoreBars(reading);
                }
                GUILayout.Space(14f);
            }
        }

        /// <summary>
        ///     The way into the emotion timeline, offered here because Live mode has already
        ///     answered the question the timeline cannot answer for itself: which character. It
        ///     used to be a top-level Convai menu row, which put a profiler in front of users who
        ///     had not set a character up yet.
        /// </summary>
        private void DrawTimelineLink()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14f);
                if (GUILayout.Button(TimelineLinkContent, GUILayout.Width(170f)))
                    ConvaiEmotionTimelineWindow.Open(_controller);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(6f);
        }

        private static readonly GUIContent TimelineLinkContent = new(
            "Open Emotion Timeline",
            "Charts this character's feelings, resting mood and transitions over time while you play.");

        /// <summary>One bar per emotion carrying a non-zero score, strongest first is not needed —
        /// vocabulary order is stable and therefore easier to read frame to frame.</summary>
        private static void DrawScoreBars(EmotionReading reading)
        {
            if (reading.AllScores == null || reading.AllScores.Count == 0)
            {
                GUILayout.Label("Nothing yet.", ConvaiEditorTheme.CaptionWrapped);
                return;
            }

            foreach (KeyValuePair<string, float> entry in reading.AllScores)
            {
                if (entry.Value <= 0.001f) continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(entry.Key, GUILayout.Width(110f));
                    Rect bar = GUILayoutUtility.GetRect(0f, 14f, GUILayout.ExpandWidth(true));
                    bar.height = 10f;
                    bar.y += 2f;
                    ConvaiEditorTheme.FillRounded(bar, ConvaiEditorTheme.CardBg, 3f);
                    var filled = new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(entry.Value), bar.height);
                    ConvaiEditorTheme.FillRounded(filled, ConvaiEditorTheme.Accent, 3f);
                    GUILayout.Label(entry.Value.ToString("0.00"), GUILayout.Width(44f));
                }
            }
        }
    }
}
