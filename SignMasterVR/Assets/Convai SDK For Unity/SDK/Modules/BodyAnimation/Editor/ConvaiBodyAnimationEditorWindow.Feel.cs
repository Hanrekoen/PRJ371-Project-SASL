using Convai.Editor.Ownership;
using Convai.Editor.UI;
using Convai.Editor.Inspectors;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Feel mode: the archetype cards and personality sliders the inspector shows, plus the
    ///     complete ~100-field behavior config in its ten named sections
    ///     (<see cref="BodyAnimationConfigSections" />). The inspector shows three sliders; this
    ///     shows what those sliders actually move, and everything they do not (
    ///     drawing on the §A3 sectioning table).
    /// </summary>
    internal sealed partial class ConvaiBodyAnimationEditorWindow
    {
        private const string FeelSectionHost = "ConvaiBodyAnimationEditorWindow.Feel";

        private static readonly GUIContent FeelPersonalityHeaderContent =
            new(BodyAnimationEditorStrings.FeelPersonalityHeader);

        private void DrawFeelMode()
        {
            if (_controller == null)
            {
                DrawCenteredMessage(BodyAnimationEditorStrings.FeelModeNoController);
                return;
            }

            ConvaiBodyAnimationConfig config = BodyAnimationPersonality.ResolveConfig(_controller);
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Min(760f, position.width - LeftPaneWidth - 40f))))
            {
                GUILayout.Space(6f);

                if (config == null)
                {
                    ConvaiEditorFrame.InfoBox(
                        BodyAnimationEditorStrings.FeelPersonalityHeader,
                        BodyAnimationEditorStrings.FeelModeNoConfig);
                    return;
                }

                using (ConvaiEditorFrame.Card())
                {
                    ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Profile, FeelPersonalityHeaderContent);
                    BodyAnimationPersonality.DrawPersonalitySection(
                        config, BodyAnimationSetupService.ResolveAssignedSet(_controller), _controller);
                }

                GUILayout.Label(BodyAnimationEditorStrings.FeelFullConfigHeader, ConvaiEditorStyles.SelectedTitle);
                GUILayout.Label(BodyAnimationEditorStrings.FeelFullConfigIntro, ConvaiEditorStyles.MutedWrapped);
                GUILayout.Space(6f);

                // Computed the same pure way Edit Mode and a built runtime both read it —
                // whether or not this character's runtime has been built yet.
                BodyAnimationFeatureAvailability availability =
                    BodyAnimationFeatureAvailability.Compute(_controller.AnimationSet, config);
                DrawFullConfigSurface(config, _controller, in availability);
            }
        }

        /// <summary>
        ///     Every serialized field, grouped by <see cref="BodyAnimationConfigSections.Sections" />.
        ///     A gated section (today: Advanced Co-Speech) draws its gate field live and disables
        ///     the rest while the gate is off. A feature toggle that is on but has no matching
        ///     content in the set gets an inert badge.
        /// </summary>
        private static void DrawFullConfigSurface(
            ConvaiBodyAnimationConfig config,
            ConvaiBodyAnimationController owner,
            in BodyAnimationFeatureAvailability availability)
        {
            // The window edits the same config the inspector does, so it has to make the same
            // promise: a character still on the SDK's shipped settings gets its own copy the moment
            // anything here changes. A surface that forgot would write to the package instead.
            using var edit = ConvaiOwnedEdit.Begin(config, owner, BodyAnimationPersonality.Copier);
            using var readOnly = new EditorGUI.DisabledScope(!edit.CanEdit);

            SerializedObject serialized = edit.Serialized;

            BodyAnimationConfigSection[] sections = BodyAnimationConfigSections.Sections;
            for (int s = 0; s < sections.Length; s++)
            {
                BodyAnimationConfigSection section = sections[s];
                bool expanded = ConvaiEditorSectionState.Get(FeelSectionHost, section.Id, section.ExpandedByDefault);
                var spec = new ConvaiEditorSectionSpec(
                    FeelSectionHost, section.Id, section.Title, ConvaiEditorGlyphs.Profile);
                bool newExpanded = ConvaiEditorSections.DrawHeader(in spec, expanded);
                if (newExpanded != expanded) ConvaiEditorSectionState.Set(FeelSectionHost, section.Id, newExpanded);

                if (!newExpanded) continue;

                ConvaiEditorSections.BeginBody();
                GUILayout.Label(section.Summary, ConvaiEditorTheme.SectionSummary);
                GUILayout.Space(4f);
                DrawSectionFields(serialized, section, in availability);
                ConvaiEditorSections.EndBody();
            }
        }

        private static void DrawSectionFields(
            SerializedObject serialized, BodyAnimationConfigSection section, in BodyAnimationFeatureAvailability availability)
        {
            string gateField = BodyAnimationConfigSections.GateFieldFor(section.Id);
            bool gateOpen = true;

            if (gateField != null)
            {
                SerializedProperty gateProp = serialized.FindProperty(gateField);
                if (gateProp != null)
                {
                    EditorGUILayout.PropertyField(gateProp);
                    gateOpen = gateProp.boolValue;
                }
            }

            using (new EditorGUI.DisabledScope(gateField != null && !gateOpen))
            {
                string[] fields = section.Fields;
                for (int i = 0; i < fields.Length; i++)
                {
                    string fieldName = fields[i];
                    if (fieldName == gateField) continue; // already drawn above, live even while the gate is off

                    SerializedProperty property = serialized.FindProperty(fieldName);
                    if (property == null) continue;

                    EditorGUILayout.PropertyField(property, BodyAnimationConfigLabels.For(property), true);
                    DrawFeatureStatusNote(fieldName, in availability);
                }
            }
        }

        /// <summary>
        ///     States the effect a content-gated setting will actually have on THIS set, right
        ///     under the setting itself. Three distinct answers, deliberately worded differently
        ///     because they are not the same problem:
        ///     <list type="bullet">
        ///         <item>on with nothing to play — the dead-switch case, needs action;</item>
        ///         <item>content authored but the switch is off — the invisible authoring mistake;</item>
        ///         <item>no content but a defined fallback exists — informational, not a fault.</item>
        ///     </list>
        ///     Silent for every field not in the map and for a healthy, effective feature.
        /// </summary>
        private static void DrawFeatureStatusNote(string fieldName, in BodyAnimationFeatureAvailability availability)
        {
            switch (fieldName)
            {
                // Authored-clip only, no substitute: on without content really is dead.
                case "_enableBeatGestures":
                    DrawStateNote(availability.BeatGestures, null);
                    return;
                case "_enableAmbientActivities":
                    DrawStateNote(availability.AmbientActivities, null);
                    return;

                // These two always do something without content — say what, instead of
                // calling a working fallback "inert".
                case "_enableReferentialGestures":
                    DrawStateNote(availability.ReferentialGestures, BodyAnimationEditorStrings.FeelReferentialFallbackNote);
                    return;
                case "_movingTalkMode":
                    DrawStateNote(availability.MovingTalkAdditive, BodyAnimationEditorStrings.FeelMovingTalkFallbackNote);
                    return;
            }
        }

        /// <param name="fallbackNote">
        ///     What happens with the feature on and no content. <c>null</c> means nothing happens,
        ///     which is reported as the dead-switch case instead.
        /// </param>
        private static void DrawStateNote(BodyAnimationFeatureState state, string fallbackNote)
        {
            if (state.IsContentWithoutEnable)
            {
                EditorGUILayout.LabelField(
                    BodyAnimationEditorStrings.FeelFeatureDormantBadge, ConvaiEditorTheme.CaptionWrapped);
                return;
            }

            if (!state.IsEnabledWithoutContent) return;

            EditorGUILayout.LabelField(
                fallbackNote ?? BodyAnimationEditorStrings.FeelFeatureInertBadge,
                ConvaiEditorTheme.CaptionWrapped);
        }
    }
}
