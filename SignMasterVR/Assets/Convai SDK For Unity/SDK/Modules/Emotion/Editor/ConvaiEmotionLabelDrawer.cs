using System;
using System.Collections.Generic;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;
using Convai.Modules.Emotion.Taxonomy;
using Convai.Runtime.Utilities;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     Draws every <see cref="ConvaiEmotionLabelAttribute" /> field as the character's emotion
    ///     vocabulary, so an emotion is chosen from a list everywhere instead of typed by hand.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One drawer rather than a popup hand-written into each inspector: the emotion fields
    ///         are spread over the Emotion controller, its personality asset, per-emotion recipes,
    ///         material slots, the Set Mood and React behaviors, Body Animation's emotion affinities
    ///         and the Body Language and Gaze per-emotion modifiers. The curated inspectors had
    ///         already grown popups for three of them, which is exactly how the rest stayed text
    ///         boxes.
    ///     </para>
    ///     <para>
    ///         The attribute lives in <c>Convai.Runtime</c> and a property drawer applies project
    ///         wide, so the modules that own those fields need no reference to this one — the
    ///         no-cross-module-references rule holds.
    ///     </para>
    ///     <para>
    ///         The list is the vocabulary the field's own character uses — a custom
    ///         <see cref="EmotionTaxonomyAsset" /> when one is wired up, the built-in vocabulary
    ///         otherwise — so a project that authors its own emotions gets its own emotions offered
    ///         here, with no SDK change.
    ///     </para>
    /// </remarks>
    [CustomPropertyDrawer(typeof(ConvaiEmotionLabelAttribute))]
    internal sealed class ConvaiEmotionLabelDrawer : PropertyDrawer
    {
        /// <summary>Suffix marking a stored name this character's vocabulary does not define.</summary>
        private const string UnknownSuffix = "  (not in this character's vocabulary)";

        /// <summary>Shown when a field that must name an emotion has not been given one yet.</summary>
        private const string NothingChosenLabel = "Choose an emotion…";

        // Rebuilt per draw call rather than cached per drawer instance: one drawer instance serves
        // every row of a reorderable list, so a cache keyed on nothing would show row 0's vocabulary
        // on every row. The label list itself is cached by EmotionLabelCatalog, which is the part
        // that used to be expensive.
        private readonly List<string> _options = new();
        private readonly List<GUIContent> _content = new();

        // The popup wants an array; an inspector repaints every frame in Play Mode, so the array is
        // reused whenever the vocabulary length has not changed rather than rebuilt per repaint.
        private GUIContent[] _choices = Array.Empty<GUIContent>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var emotionField = (ConvaiEmotionLabelAttribute)attribute;
            string current = property.stringValue;

            BuildOptions(emotionField, ResolveTaxonomy(property), current);

            int currentIndex = IndexOf(_options, current);
            if (currentIndex < 0) currentIndex = 0;

            label = EditorGUI.BeginProperty(position, label, property);

            bool mixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUI.Popup(position, label, currentIndex, Choices());
            if (EditorGUI.EndChangeCheck() && nextIndex >= 0 && nextIndex < _options.Count)
                property.stringValue = _options[nextIndex];

            EditorGUI.showMixedValue = mixed;
            EditorGUI.EndProperty();
        }

        /// <summary>
        ///     The vocabulary, plus the stored value when the vocabulary no longer defines it.
        /// </summary>
        /// <remarks>
        ///     Keeping an unknown value selectable is what stops merely opening an Inspector from
        ///     rewriting a hand-authored or backend-supplied name to whatever is first in the list.
        /// </remarks>
        private void BuildOptions(
            ConvaiEmotionLabelAttribute emotionField,
            EmotionTaxonomyAsset taxonomy,
            string current)
        {
            _options.Clear();
            _content.Clear();

            // An empty value gets an entry either way — as the author's choice when the field
            // accepts one, and otherwise as an unmistakable "nothing is set here yet". Without it
            // an unset required field would display the first emotion in the list as though it
            // were the stored value.
            if (emotionField.AllowsEmpty)
            {
                _options.Add(string.Empty);
                _content.Add(new GUIContent(emotionField.EmptyOptionLabel));
            }
            else if (string.IsNullOrWhiteSpace(current))
            {
                _options.Add(string.Empty);
                _content.Add(new GUIContent(NothingChosenLabel));
            }

            string[] vocabulary = EmotionLabelCatalog.LabelsFor(taxonomy);
            for (int i = 0; i < vocabulary.Length; i++)
            {
                if (IndexOf(_options, vocabulary[i]) >= 0) continue;
                _options.Add(vocabulary[i]);
                _content.Add(new GUIContent(EmotionLabelCatalog.DisplayName(vocabulary[i])));
            }

            if (string.IsNullOrWhiteSpace(current) || IndexOf(_options, current) >= 0) return;

            _options.Add(current);
            _content.Add(new GUIContent(EmotionLabelCatalog.DisplayName(current) + UnknownSuffix));
        }

        /// <summary>
        ///     The emotion vocabulary the edited object's character actually uses, or <c>null</c>
        ///     for the built-in one.
        /// </summary>
        /// <remarks>
        ///     A behavior lives on the character or on a child of it, so the Emotion controller is
        ///     found by walking up rather than on the same GameObject. An asset that is not an
        ///     emotion personality — a Body Animation profile, for instance — has no character to
        ///     ask, and falls back to the built-in vocabulary.
        /// </remarks>
        private static EmotionTaxonomyAsset ResolveTaxonomy(SerializedProperty property)
        {
            UnityEngine.Object target = property.serializedObject.targetObject;

            switch (target)
            {
                case ConvaiEmotionProfile profile:
                    return profile.Taxonomy;

                case ConvaiEmotionController controller:
                    return EmotionSetupService.ResolveAssignedProfile(controller)?.Taxonomy;

                case Component component:
                {
                    ConvaiEmotionController peer =
                        component.GetComponentInParent<ConvaiEmotionController>(true);
                    return peer != null
                        ? EmotionSetupService.ResolveAssignedProfile(peer)?.Taxonomy
                        : null;
                }

                default:
                    return null;
            }
        }

        /// <summary>The built options as the array the popup takes, without reallocating per repaint.</summary>
        private GUIContent[] Choices()
        {
            if (_choices.Length != _content.Count) _choices = new GUIContent[_content.Count];
            for (int i = 0; i < _content.Count; i++) _choices[i] = _content[i];
            return _choices;
        }

        private static int IndexOf(IReadOnlyList<string> options, string value)
        {
            if (options == null || value == null) return -1;
            for (int i = 0; i < options.Count; i++)
                if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}
