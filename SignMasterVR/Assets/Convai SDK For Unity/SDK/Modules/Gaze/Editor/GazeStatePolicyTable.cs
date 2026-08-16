using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.UI;
using Convai.Modules.Gaze.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>
    ///     A readable editor for the per-conversation-state gaze table — the module's last raw-data
    ///     surface.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Eight rows of eight fields, drawn by Unity as a reorderable array of structs, is the
    ///         one place in the module where a user still had to understand the data model to change
    ///         behaviour: which state each element applied to, that an unlisted state falls back to
    ///         Idle, that the array's order is meaningless, and what "Fixation Liveliness" was.
    ///     </para>
    ///     <para>
    ///         This draws the states instead of the array. Every conversation state is a row whether
    ///         or not the profile authored one, so a missing row is visible as a gap rather than as
    ///         silence, and adding it is a button rather than an array insert plus an enum pick.
    ///         Rows that differ from the profile's personality are marked, so "what did I change?"
    ///         is answerable at a glance — which is what the archetype pills could never tell you.
    ///     </para>
    ///     <para>
    ///         Idle is deliberately explained rather than hidden: it is the fallback for every state
    ///         the table does not list, and its engagement of 0 is a design decision (the character
    ///         is not engaged with anyone) rather than a value someone forgot to set.
    ///     </para>
    /// </remarks>
    internal static class GazeStatePolicyTable
    {
        /// <summary>
        ///     The states in conversational order, so the table reads as a conversation rather than
        ///     as however the array happened to be authored.
        /// </summary>
        private static readonly DialogueState[] Order =
        {
            DialogueState.Idle,
            DialogueState.Attending,
            DialogueState.Listening,
            DialogueState.Thinking,
            DialogueState.Speaking,
            DialogueState.Reacting,
            DialogueState.Interrupted,
            DialogueState.Settling
        };

        private static readonly Dictionary<DialogueState, string> Explanations = new()
        {
            [DialogueState.Idle] = "Nobody is talking to it. Also the fallback for any state not listed here.",
            [DialogueState.Attending] = "Someone just started addressing it.",
            [DialogueState.Listening] = "The player is speaking.",
            [DialogueState.Thinking] = "It is working out what to say.",
            [DialogueState.Speaking] = "It is talking.",
            [DialogueState.Reacting] = "Something just happened that it is responding to.",
            [DialogueState.Interrupted] = "It was cut off mid-sentence.",
            [DialogueState.Settling] = "The conversation is winding down."
        };

        private static readonly GUIContent EngagementLabel = new(
            "How committed", "0 lets it drift to its idle life; 1 is a full commit to whoever it is looking at.");

        private static readonly GUIContent AllowPlayerLabel = new(
            "Looks at the player", "When off, the player is not a candidate at all in this state.");

        private static readonly GUIContent HeadLabel = new(
            "How much the head joins in", "0 is eyes only; 1 is a full head commit.");

        private static readonly GUIContent BodyTurnLabel = new(
            "May turn its body", "Whether a target far off to the side can trigger a full-body turn here.");

        private static readonly GUIContent AversionModeLabel = new(
            "Looks away", "None holds unbroken contact. Natural takes brief social breaks. " +
                          "Cognitive is the up-and-aside 'let me think' beat.");

        private static readonly GUIContent AversionStrengthLabel = new(
            "How often it looks away", "Scales how frequently and how far the look-away beats go.");

        private static readonly GUIContent LivelinessLabel = new(
            "Eye liveliness", "Scales the small movements — eye flicks and face scanning — in this state.");

        /// <summary>
        ///     Draws the whole table against <paramref name="serializedProfile" />. The caller owns
        ///     Update/ApplyModifiedProperties, so the whole table is one undo step.
        /// </summary>
        internal static void Draw(ConvaiGazeProfile profile, SerializedObject serializedProfile)
        {
            if (profile == null || serializedProfile == null) return;

            SerializedProperty policies = GazeProfileSerializedPaths.Find(serializedProfile, "statePolicies");
            if (policies == null || !policies.isArray)
            {
                ConvaiEditorFrame.WarningBox("No State Table", "This profile has no state table.");
                return;
            }

            GazeProfileArchetypes.GazeArchetype active = GazePersonality.ActiveArchetype(profile);

            for (int i = 0; i < Order.Length; i++)
                DrawStateRow(policies, Order[i], active);

            DrawUnknownStateWarning(policies);
        }

        // ------------------------------------------------------------------ one state

        private static void DrawStateRow(
            SerializedProperty policies, DialogueState state, GazeProfileArchetypes.GazeArchetype active)
        {
            int index = IndexOf(policies, state);
            bool authored = index >= 0;
            bool customised = authored && active != null && !MatchesArchetype(policies, index, state, active);

            using (ConvaiEditorFrame.Panel())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(FriendlyName(state), ConvaiEditorStyles.SectionTitle, GUILayout.Width(110f));
                    GUILayout.Label(Explanations[state], ConvaiEditorTheme.CaptionWrapped);

                    if (customised)
                    {
                        var chip = new GUIContent("changed",
                            $"This row no longer matches the {active.Name} personality.");
                        Rect chipRect = GUILayoutUtility.GetRect(
                            ConvaiEditorTheme.PillWidth(chip), 18f, GUILayout.ExpandWidth(false));
                        ConvaiEditorTheme.Pill(chipRect, chip, ConvaiEditorTheme.Info);
                    }
                }

                if (!authored)
                {
                    DrawMissingRow(policies, state);
                    return;
                }

                SerializedProperty element = policies.GetArrayElementAtIndex(index);
                EditorGUI.indentLevel++;

                Field(element, "Engagement", EngagementLabel);
                Field(element, "AllowPlayerTarget", AllowPlayerLabel);
                Field(element, "HeadContribution", HeadLabel);
                Field(element, "AllowBodyTurn", BodyTurnLabel);
                Field(element, "AversionMode", AversionModeLabel);

                // Aversion strength does nothing while the mode is None, so it is hidden rather
                // than shown as a live control that changes nothing.
                SerializedProperty mode = element.FindPropertyRelative("AversionMode");
                if (mode != null && mode.enumValueIndex != (int)GazeAversionMode.None)
                    Field(element, "AversionStrength", AversionStrengthLabel);

                Field(element, "FixationLiveliness", LivelinessLabel);

                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        ///     A state the table does not author. Shown as a row rather than omitted, because the
        ///     silent fallback to Idle is the single most surprising thing about this data.
        /// </summary>
        private static void DrawMissingRow(SerializedProperty policies, DialogueState state)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(16f);
                GUILayout.Label("Not set — behaves like Idle here.", ConvaiEditorTheme.CaptionWrapped);
                if (GUILayout.Button("Give it its own behaviour", EditorStyles.miniButton, GUILayout.Width(180f)))
                    AddRow(policies, state);
            }
        }

        /// <summary>
        ///     Appends a row for <paramref name="state" />, seeded from the Idle row so the new row
        ///     starts where the character's behaviour already was rather than at struct defaults —
        ///     a fresh element would read as engagement 0 and instantly change the character.
        /// </summary>
        private static void AddRow(SerializedProperty policies, DialogueState state)
        {
            int seed = IndexOf(policies, DialogueState.Idle);
            int index = policies.arraySize;
            policies.InsertArrayElementAtIndex(index);

            SerializedProperty added = policies.GetArrayElementAtIndex(index);
            if (seed >= 0)
            {
                SerializedProperty source = policies.GetArrayElementAtIndex(seed);
                Copy(source, added, "Engagement");
                Copy(source, added, "AllowPlayerTarget");
                Copy(source, added, "HeadContribution");
                Copy(source, added, "AllowBodyTurn");
                Copy(source, added, "AversionMode");
                Copy(source, added, "AversionStrength");
                Copy(source, added, "FixationLiveliness");
            }

            SerializedProperty stateProperty = added.FindPropertyRelative("State");
            if (stateProperty != null) stateProperty.enumValueIndex = (int)state;
        }

        /// <summary>
        ///     Reports rows for states this editor does not know about — a table authored against a
        ///     newer SDK, or hand-edited YAML. They still work; they are just not drawn above, and
        ///     silently hiding them would be worse than saying so.
        /// </summary>
        private static void DrawUnknownStateWarning(SerializedProperty policies)
        {
            var unknown = new List<string>();
            for (int i = 0; i < policies.arraySize; i++)
            {
                SerializedProperty stateProperty =
                    policies.GetArrayElementAtIndex(i).FindPropertyRelative("State");
                if (stateProperty == null) continue;

                bool known = false;
                for (int k = 0; k < Order.Length; k++)
                    if (stateProperty.enumValueIndex == (int)Order[k]) { known = true; break; }

                if (!known) unknown.Add(stateProperty.enumDisplayNames[stateProperty.enumValueIndex]);
            }

            if (unknown.Count == 0) return;

            ConvaiEditorFrame.InfoBox(
                "Extra Rows Not Listed Here",
                "This profile also authors rows for states this editor does not list: " +
                string.Join(", ", unknown) + ". They still apply at runtime.");
        }

        // ------------------------------------------------------------------ helpers

        private static void Field(SerializedProperty element, string relative, GUIContent label)
        {
            SerializedProperty property = element.FindPropertyRelative(relative);
            if (property != null) EditorGUILayout.PropertyField(property, label);
        }

        private static void Copy(SerializedProperty from, SerializedProperty to, string relative)
        {
            SerializedProperty source = from.FindPropertyRelative(relative);
            SerializedProperty destination = to.FindPropertyRelative(relative);
            if (source == null || destination == null) return;

            switch (source.propertyType)
            {
                case SerializedPropertyType.Float:
                    destination.floatValue = source.floatValue;
                    break;
                case SerializedPropertyType.Boolean:
                    destination.boolValue = source.boolValue;
                    break;
                case SerializedPropertyType.Enum:
                    destination.enumValueIndex = source.enumValueIndex;
                    break;
            }
        }

        internal static int IndexOf(SerializedProperty policies, DialogueState state)
        {
            if (policies == null || !policies.isArray) return -1;

            for (int i = 0; i < policies.arraySize; i++)
            {
                SerializedProperty stateProperty =
                    policies.GetArrayElementAtIndex(i).FindPropertyRelative("State");
                if (stateProperty != null && stateProperty.enumValueIndex == (int)state) return i;
            }

            return -1;
        }

        /// <summary>Whether this row still matches the archetype the profile is on.</summary>
        private static bool MatchesArchetype(
            SerializedProperty policies, int index, DialogueState state,
            GazeProfileArchetypes.GazeArchetype archetype)
        {
            GazeProfileArchetypes.StateRow authored = default;
            bool found = false;
            for (int i = 0; i < archetype.States.Length; i++)
            {
                if (archetype.States[i].State != state) continue;
                authored = archetype.States[i];
                found = true;
                break;
            }

            if (!found) return true;

            SerializedProperty element = policies.GetArrayElementAtIndex(index);
            return Approximately(element, "Engagement", authored.Engagement) &&
                   Boolean(element, "AllowPlayerTarget") == authored.AllowPlayerTarget &&
                   Approximately(element, "HeadContribution", authored.HeadContribution) &&
                   Boolean(element, "AllowBodyTurn") == authored.AllowBodyTurn &&
                   Enum(element, "AversionMode") == (int)authored.AversionMode &&
                   Approximately(element, "AversionStrength", authored.AversionStrength) &&
                   Approximately(element, "FixationLiveliness", authored.FixationLiveliness);
        }

        private static bool Approximately(SerializedProperty element, string relative, float expected)
        {
            SerializedProperty property = element.FindPropertyRelative(relative);
            return property != null && Mathf.Abs(property.floatValue - expected) < 0.005f;
        }

        private static bool Boolean(SerializedProperty element, string relative)
        {
            SerializedProperty property = element.FindPropertyRelative(relative);
            return property != null && property.boolValue;
        }

        private static int Enum(SerializedProperty element, string relative)
        {
            SerializedProperty property = element.FindPropertyRelative(relative);
            return property != null ? property.enumValueIndex : -1;
        }

        /// <summary>
        ///     The state's name as a person would say it. <c>Attending</c> and <c>Settling</c> in
        ///     particular are internal vocabulary for "just addressed" and "winding down".
        /// </summary>
        internal static string FriendlyName(DialogueState state) => state switch
        {
            DialogueState.Idle => "Idle",
            DialogueState.Attending => "Addressed",
            DialogueState.Listening => "Listening",
            DialogueState.Thinking => "Thinking",
            DialogueState.Speaking => "Speaking",
            DialogueState.Reacting => "Reacting",
            DialogueState.Interrupted => "Interrupted",
            DialogueState.Settling => "Winding down",
            _ => state.ToString()
        };
    }
}
