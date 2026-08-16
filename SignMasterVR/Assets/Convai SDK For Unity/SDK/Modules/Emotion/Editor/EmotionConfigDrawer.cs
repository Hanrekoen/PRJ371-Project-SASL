using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Convai.Editor.UI;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     Renders the <see cref="EmotionConfigSections" /> table onto a serialized profile.
    /// </summary>
    /// <remarks>
    ///     One implementation, two callers — the Emotion editor window's Feel mode and the profile
    ///     asset's own inspector. The section table, the plain-English labels, the gating and the
    ///     ordering therefore cannot drift between them, which is the whole reason the table exists.
    /// </remarks>
    internal static class EmotionConfigDrawer
    {
        /// <summary>
        ///     Draws every section of the table. This type owns <em>what</em> a section contains — the
        ///     order, the plain-English labels and the gating; <paramref name="drawSection" /> owns
        ///     <em>how</em> it is presented, including its frame and its fold state.
        /// </summary>
        /// <param name="drawSection">
        ///     Receives each section and a closure that draws that section's fields. A caller renders
        ///     the section in its own frame and invokes the closure where the body belongs — so an
        ///     inspector can wrap it in a Convai section card and a window can wrap it in its own.
        /// </param>
        /// <remarks>
        ///     Inverted from an earlier shape that took a <see cref="Dictionary{TKey,TValue}" /> of
        ///     fold state plus an <em>optional</em> header callback. Optional meant the Emotion
        ///     window's Feel mode passed none and fell through to a raw
        ///     <see cref="EditorGUILayout.Foldout" />, so the same section table rendered as plain
        ///     Unity foldouts in the window and as Convai-styled cards in the profile inspector. There is
        ///     no plain-Unity fallback any more: presenting a section is the caller's job, and every caller
        ///     is inside the design system.
        /// </remarks>
        internal static void DrawAllSections(
            SerializedObject serialized,
            Action<EmotionConfigSection, Action> drawSection)
        {
            if (serialized == null || drawSection == null) return;

            for (int i = 0; i < EmotionConfigSections.Sections.Length; i++)
            {
                EmotionConfigSection section = EmotionConfigSections.Sections[i];
                drawSection(section, () => DrawSectionFields(serialized, section));
            }
        }

        /// <summary>
        ///     The section mark for one config section. Lives here, beside the section table itself,
        ///     because both surfaces that render the table — the profile inspector and the editor
        ///     window's Feel mode — must give a section the same mark. Two copies of this mapping is
        ///     precisely how they would drift.
        /// </summary>
        internal static string GlyphFor(string sectionId) => sectionId switch
        {
            EmotionConfigSections.Personality => ConvaiEditorGlyphs.Profile,
            EmotionConfigSections.RestingMood => ConvaiEditorGlyphs.Visibility,
            EmotionConfigSections.Reactions => ConvaiEditorGlyphs.Reaction,
            EmotionConfigSections.SmallMovements => ConvaiEditorGlyphs.Motion,
            EmotionConfigSections.MixingEmotions => ConvaiEditorGlyphs.Routing,
            EmotionConfigSections.OtherCharacters => ConvaiEditorGlyphs.Range,
            EmotionConfigSections.PerEmotion => ConvaiEditorGlyphs.Reaction,
            EmotionConfigSections.Vocabulary => ConvaiEditorGlyphs.Identity,
            EmotionConfigSections.Expressions => ConvaiEditorGlyphs.Content,
            _ => ConvaiEditorGlyphs.Section
        };

        /// <summary>
        ///     Draws a section's fields, disabling each one whose declared gate is currently off.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Disabling a gated field says "this does nothing right now" far more clearly than a
        ///         sentence the user has to read — and it is the difference between a control that is
        ///         inert-with-a-reason and one that silently does nothing, which is the failure mode
        ///         this whole pass exists to remove.
        ///     </para>
        ///     <para>
        ///         Each field's gate is looked up by name from
        ///         <see cref="EmotionConfigSections.GateForField" /> rather than inferred from its
        ///         position. Position-based gating could only express "the toggle above me", which
        ///         quietly mis-stated the conversation-beat reactions: it greyed them out with the
        ///         unrelated micro-burst toggle, and left them looking live while the layer that
        ///         composes them was off.
        ///     </para>
        /// </remarks>
        internal static void DrawSectionFields(SerializedObject serialized, EmotionConfigSection section)
        {
            if (!string.IsNullOrEmpty(section.Summary))
                EditorGUILayout.LabelField(section.Summary, ConvaiEditorStyles.SectionSummary);

            for (int i = 0; i < section.Fields.Length; i++)
            {
                string fieldName = section.Fields[i];
                SerializedProperty property = serialized.FindProperty(fieldName);
                if (property == null) continue;

                string gateName = EmotionConfigSections.GateForField(fieldName);

                // A missing gate property must not silently disable the field it guards — an absent
                // gate means "no gate", never "gate is off".
                bool gateOpen = gateName == null ||
                                (serialized.FindProperty(gateName)?.boolValue ?? true);

                using (new EditorGUI.DisabledScope(!gateOpen))
                {
                    EditorGUILayout.PropertyField(property, EmotionConfigLabels.For(property), true);
                }
            }
        }
    }
}
