using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.Emotion;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>How badly a finding affects the character.</summary>
    internal enum EmotionSeverity
    {
        /// <summary>Worth knowing, nothing is wrong.</summary>
        Info = 0,

        /// <summary>Something will not behave as the settings claim.</summary>
        Warning = 1,

        /// <summary>The character will not express anything at all.</summary>
        Error = 2
    }

    /// <summary>A fix the surface can offer as a button, or <see cref="EmotionFixId.None" />.</summary>
    internal enum EmotionFixId
    {
        None = 0,

        /// <summary>Switch detection from Off to the fast provider.</summary>
        TurnOnDetection,

        /// <summary>Raise the resting mood strength above zero so the chosen mood actually shows.</summary>
        ApplyRestingMoodStrength,

        /// <summary>Turn the small-movement layer on so a resting face is not a frozen mask.</summary>
        EnableSmallMovements,

        /// <summary>Turn off picking up other characters' moods, since there is nobody to react to.</summary>
        DisableOtherCharacters,

        /// <summary>Clear a resting-mood override this character's vocabulary cannot resolve.</summary>
        ClearInitialMood,

        /// <summary>Raise this character's own resting-mood strength above zero.</summary>
        RaiseInitialMoodStrength
    }

    /// <summary>One thing that is wrong, said in plain language, with a fix where one exists.</summary>
    internal readonly struct EmotionFinding
    {
        public EmotionFinding(EmotionSeverity severity, string title, string message, EmotionFixId fix)
        {
            Severity = severity;
            Title = title;
            Message = message;
            Fix = fix;
        }

        public EmotionSeverity Severity { get; }
        public string Title { get; }
        public string Message { get; }
        public EmotionFixId Fix { get; }
    }

    /// <summary>
    ///     Evaluates a configured character for the ways emotions can be quietly wrong — settings
    ///     that contradict each other, or that describe something the scene cannot deliver.
    /// </summary>
    /// <remarks>
    ///     Every finding here is a failure this module could previously produce in silence: a
    ///     resting mood with zero strength, a character whose detection is off while its personality
    ///     is fully authored, an unrecognised face rig, or "picks up other characters' moods" in a
    ///     scene with one character.
    /// </remarks>
    internal static class EmotionTroubleshooter
    {
        internal static void Evaluate(
            ConvaiEmotionController controller, in EmotionPreflight preflight, List<EmotionFinding> results)
        {
            if (results == null) return;
            results.Clear();
            if (controller == null) return;

            var serialized = new SerializedObject(controller);
            ConvaiEmotionProfile profile = EmotionSetupService.ResolveAssignedProfile(controller);

            // Detection off is reported whether or not a personality is assigned. It used to be
            // reported only alongside one, on the reasoning that configuring a temperament for a
            // character that will never feel anything is the obvious mistake — but a character with
            // the component added and detection switched off never reacts either, and staying quiet
            // about that left the inspector saying nothing while the assistant's diagnosis called
            // the same character inert. One condition, so both surfaces say the same thing.
            //
            // The serialized VALUE, never enumValueIndex — Off happens to be 0 in both, but reading
            // the declaration index here is the habit that shipped the two providers swapped.
            var detectionMode = (EmotionDetectionMode)serialized.FindProperty("detectionMode").intValue;

            if (detectionMode == EmotionDetectionMode.Off)
            {
                results.Add(new EmotionFinding(EmotionSeverity.Warning,
                    "Emotions are switched off",
                    (profile != null
                        ? "This character has a personality assigned but emotion detection is Off, so it "
                        : "Emotion detection is Off on this character, so it ") +
                    "will never react to anything that is said. Turning it on selects " +
                    $"{EmotionDetectionModes.ShortNameFor(EmotionDetectionModes.Default)}, which " +
                    "reacts while the reply is being spoken; Advanced has the other option and what " +
                    "each is better at.",
                    EmotionFixId.TurnOnDetection));
            }

            if (profile != null)
            {
                // A resting mood at zero strength is a control that looks set and does nothing.
                if (!string.IsNullOrWhiteSpace(profile.BaselineEmotionLabel) && profile.BaselineIntensity <= 0f)
                {
                    results.Add(new EmotionFinding(EmotionSeverity.Warning,
                        "Resting mood has no strength",
                        $"The personality rests at '{profile.BaselineEmotionLabel}', but its strength is 0, " +
                        "so the face settles to plain neutral instead.",
                        EmotionFixId.ApplyRestingMoodStrength));
                }

                // A resting mood with no life layer is the classic frozen-mask look.
                if (!string.IsNullOrWhiteSpace(profile.BaselineEmotionLabel) &&
                    profile.BaselineIntensity > 0f && !profile.MicroExpressionsEnabled)
                {
                    results.Add(new EmotionFinding(EmotionSeverity.Warning,
                        "Resting face will look frozen",
                        "This character holds a resting mood but never moves while it does, which reads " +
                        "as a mask rather than a person.",
                        EmotionFixId.EnableSmallMovements));
                }

                // Reacting to other characters in a scene that has none.
                if (profile.ContagionEnabled && !EmotionPersonality.HasOtherCharacters(controller))
                {
                    results.Add(new EmotionFinding(EmotionSeverity.Info,
                        "Nobody to react to",
                        "This character is set to pick up other characters' moods, but the open scenes " +
                        "hold no other Convai character, so nothing will happen.",
                        EmotionFixId.DisableOtherCharacters));
                }
            }

            EvaluateRestingMoodOverride(controller, serialized, results);
            EvaluateGatedReactions(profile, results);
            EvaluateActionReactions(controller, serialized, results);

            // An unrecognised rig is not an error — recipes simply resolve to fewer channels — but
            // it is the reason a face looks under-expressive, so it must be said out loud.
            if (preflight.Convention == RigConvention.Unknown && preflight.ResolvedShapeCount > 0)
            {
                results.Add(new EmotionFinding(EmotionSeverity.Warning,
                    "Face rig not recognised",
                    "This character's blendshape names do not match ARKit, Reallusion CC3/CC4 or " +
                    "MetaHuman, so most expressions have nothing to drive. Assign a Custom Rig " +
                    "Convention Map to map them yourself.",
                    EmotionFixId.None));
            }

            if (preflight.ResolvedShapeCount == 0)
            {
                results.Add(new EmotionFinding(EmotionSeverity.Error,
                    "No face to move",
                    "No skinned mesh with blendshapes was found under this character, so emotion state " +
                    "will update while the face stays still.",
                    EmotionFixId.None));
            }
        }

        /// <summary>
        ///     This character's own resting-mood override, when it says one thing and does another.
        /// </summary>
        /// <remarks>
        ///     Both failures here were previously silent. A label the vocabulary does not define is
        ///     ignored by the runtime and the character quietly rests at the personality's mood
        ///     instead; a label with zero strength is a control that looks set and does nothing —
        ///     the same failure already reported for the personality's own resting mood, which this
        ///     character-level pair did not check.
        /// </remarks>
        private static void EvaluateRestingMoodOverride(
            ConvaiEmotionController controller, SerializedObject serialized, List<EmotionFinding> results)
        {
            string label = serialized.FindProperty("initialMoodLabel")?.stringValue ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label)) return;

            EmotionRestingMood resting = EmotionSetupService.ResolveEffectiveRestingMood(controller);

            if (!resting.LabelResolves)
            {
                results.Add(new EmotionFinding(EmotionSeverity.Warning,
                    "Resting mood is not a real emotion",
                    $"This character is set to rest at '{label}', but its vocabulary has no emotion " +
                    "with that name, so the setting is ignored and it rests at the personality's " +
                    "mood instead. Pick one from the dropdown.",
                    EmotionFixId.ClearInitialMood));
                return;
            }

            if (resting.Source == EmotionRestingMoodSource.InitialMoodOverride && resting.Intensity <= 0f)
            {
                results.Add(new EmotionFinding(EmotionSeverity.Warning,
                    "This character's resting mood has no strength",
                    $"This character is set to rest at '{resting.Label}', but its strength is 0, so " +
                    "the face settles to plain neutral instead.",
                    EmotionFixId.RaiseInitialMoodStrength));
            }
        }

        /// <summary>
        ///     Conversation-beat reactions authored above zero while the layer that composes them is
        ///     off.
        /// </summary>
        /// <remarks>
        ///     These four strengths are only consulted when small movements are on. A character
        ///     shipped as Warm or Energetic carries all four already raised, so turning small
        ///     movements off silently disables four settings that still read as configured — the
        ///     reason a value must never be reported without the gate above it.
        /// </remarks>
        private static void EvaluateGatedReactions(
            ConvaiEmotionProfile profile, List<EmotionFinding> results)
        {
            if (profile == null || profile.MicroExpressionsEnabled) return;

            var authored = new List<string>(4);
            if (profile.ListeningReactionStrength > 0f) authored.Add("listening");
            if (profile.ThinkingReactionStrength > 0f) authored.Add("thinking");
            if (profile.ReactingAccentStrength > 0f) authored.Add("reacting");
            if (profile.InterruptedFlinchStrength > 0f) authored.Add("being interrupted");
            if (authored.Count == 0) return;

            results.Add(new EmotionFinding(EmotionSeverity.Warning,
                "Conversation reactions will never show",
                $"This character has reactions set up for {Join(authored)}, but small movements are " +
                "off and that layer is what plays them, so none of them will ever be seen.",
                EmotionFixId.EnableSmallMovements));
        }

        /// <summary>
        ///     Post-action mood reactions authored on a character that has no actions to react to.
        /// </summary>
        private static void EvaluateActionReactions(
            ConvaiEmotionController controller, SerializedObject serialized, List<EmotionFinding> results)
        {
            bool authored =
                !string.IsNullOrWhiteSpace(serialized.FindProperty("actionSuccessMoodLabel")?.stringValue) ||
                !string.IsNullOrWhiteSpace(serialized.FindProperty("actionFailureMoodLabel")?.stringValue);
            if (!authored || HasActionDispatcher(controller)) return;

            results.Add(new EmotionFinding(EmotionSeverity.Info,
                "Nothing to react to yet",
                "This character has moods set for when an action succeeds or fails, but it has no " +
                "Actions component, so nothing will ever trigger them. Add one, or leave these as " +
                "they are — they cost nothing.",
                EmotionFixId.None));
        }

        /// <summary>"listening, thinking and reacting" — an oxford-comma-free list a person reads.</summary>
        private static string Join(List<string> items)
        {
            if (items.Count == 1) return items[0];
            if (items.Count == 2) return $"{items[0]} and {items[1]}";
            return string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[^1];
        }

        internal static EmotionSeverity WorstSeverity(List<EmotionFinding> findings)
        {
            EmotionSeverity worst = EmotionSeverity.Info;
            if (findings == null) return worst;
            for (int i = 0; i < findings.Count; i++)
                if (findings[i].Severity > worst) worst = findings[i].Severity;
            return worst;
        }

        /// <summary>The button label for a fix, or <c>null</c> when the user must act themselves.</summary>
        internal static string DescribeFix(EmotionFixId fix) => fix switch
        {
            EmotionFixId.TurnOnDetection => "Turn Emotions On",
            EmotionFixId.ApplyRestingMoodStrength => "Give It Some Strength",
            EmotionFixId.EnableSmallMovements => "Keep The Face Alive",
            EmotionFixId.DisableOtherCharacters => "Turn That Off",
            EmotionFixId.ClearInitialMood => "Use The Personality's Mood",
            EmotionFixId.RaiseInitialMoodStrength => "Give It Some Strength",
            _ => null
        };

        internal static void ApplyFix(ConvaiEmotionController controller, EmotionFixId fix)
        {
            if (controller == null) return;

            switch (fix)
            {
                case EmotionFixId.TurnOnDetection:
                {
                    var serialized = new SerializedObject(controller);
                    // Written as a VALUE. The previous line here set enumValueIndex = 1 under a
                    // comment claiming that was the word-matching provider; declaration order is
                    // Off, Llm, Nrclex, so it selected the other one.
                    serialized.FindProperty("detectionMode").intValue = (int)EmotionDetectionModes.Default;
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(controller);
                    break;
                }

                case EmotionFixId.ApplyRestingMoodStrength:
                    WriteProfileField(controller, p => p.FindProperty("baselineIntensity").floatValue =
                        EmotionPersonalityTable.DefaultRestingMoodIntensity);
                    break;

                case EmotionFixId.EnableSmallMovements:
                    WriteProfileField(controller, p => p.FindProperty("microExpressionsEnabled").boolValue = true);
                    break;

                case EmotionFixId.DisableOtherCharacters:
                    WriteProfileField(controller, p => p.FindProperty("contagionEnabled").boolValue = false);
                    break;

                // Both of these write the CHARACTER, not the personality: a resting-mood override
                // belongs to one character, and repairing it on the shared asset would move every
                // character that asset tunes.
                case EmotionFixId.ClearInitialMood:
                    WriteControllerField(controller,
                        s => s.FindProperty("initialMoodLabel").stringValue = string.Empty);
                    break;

                case EmotionFixId.RaiseInitialMoodStrength:
                    WriteControllerField(controller,
                        s => s.FindProperty("initialMoodIntensity").floatValue =
                            EmotionPersonalityTable.DefaultRestingMoodIntensity);
                    break;
            }
        }

        private static void WriteControllerField(
            ConvaiEmotionController controller, System.Action<SerializedObject> write)
        {
            var serialized = new SerializedObject(controller);
            serialized.Update();
            write(serialized);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }

        /// <remarks>
        ///     Routed through <see cref="EmotionPersonality.EnsureWritable" /> for the same reason the
        ///     personality controls are: a one-click fix offered on a character still reading the
        ///     SDK's shipped personality would otherwise write to the package, where it is lost on the
        ///     next update and refused outright in an installed project - and the card would report
        ///     success either way, which is the worst possible outcome for a troubleshooter.
        /// </remarks>
        private static void WriteProfileField(
            ConvaiEmotionController controller, System.Action<SerializedObject> write)
        {
            ConvaiEmotionProfile assigned = EmotionSetupService.ResolveAssignedProfile(controller);
            if (assigned == null) return;

            ConvaiEmotionProfile profile = EmotionPersonality.EnsureWritable(assigned, controller);
            if (profile == null) return;

            var serialized = new SerializedObject(profile);
            serialized.Update();
            write(serialized);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
        }

        /// <summary>
        ///     Whether this character has an Action Runner, so the post-action mood reaction has
        ///     outcomes to react to.
        /// </summary>
        /// <remarks>
        ///     Resolved by type name rather than by reference: the Actions surface lives in
        ///     <c>Convai.Runtime</c> and this module must not take a dependency on it just to grey
        ///     out one block of an inspector.
        /// </remarks>
        internal static bool HasActionDispatcher(ConvaiEmotionController controller)
        {
            if (controller == null) return false;

            foreach (MonoBehaviour behaviour in controller.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Name == "ConvaiActionDispatcher") return true;
            }

            foreach (MonoBehaviour behaviour in controller.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Name == "ConvaiActionDispatcher") return true;
            }

            return false;
        }
    }
}
