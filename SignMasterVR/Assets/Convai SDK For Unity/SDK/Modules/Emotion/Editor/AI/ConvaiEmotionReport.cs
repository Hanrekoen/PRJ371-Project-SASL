using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Emotion;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor.AI
{
    /// <summary>How far a character is from actually showing what it feels.</summary>
    /// <remarks>
    ///     <see cref="Inert" /> is the state that matters. A character can pass every setup check
    ///     and still never change expression — emotion detection switched off, or a personality
    ///     that rests at nothing with every feel switch turned down. Without a word for that, an
    ///     assistant reports the character as configured and the user watches a face that never
    ///     moves.
    /// </remarks>
    internal enum EmotionReadiness
    {
        /// <summary>No Emotion component on this character.</summary>
        NotInstalled,

        /// <summary>Installed, but something stops it working at all — no character, or no face.</summary>
        Blocked,

        /// <summary>Set up and unblocked, but nothing will ever visibly change.</summary>
        Inert,

        /// <summary>Set up, and the face will move.</summary>
        Working
    }

    /// <summary>
    ///     One character's Emotion state, folded once and read by all four tools and the survey.
    /// </summary>
    /// <remarks>
    ///     Every field here is a projection of <see cref="EmotionSetupService" /> and
    ///     <see cref="EmotionTroubleshooter" />. This type performs no check of its own, so the MCP
    ///     tools, the component inspector and the Emotion editor window cannot describe the same
    ///     character differently.
    /// </remarks>
    internal readonly struct ConvaiEmotionReport
    {
        private ConvaiEmotionReport(
            ConvaiEmotionController controller,
            EmotionPreflight preflight,
            IReadOnlyList<EmotionFinding> findings,
            ConvaiEmotionProfile profile,
            EmotionRestingMood restingMood,
            EmotionDetectionMode detectionMode)
        {
            Controller = controller;
            Preflight = preflight;
            Findings = findings;
            Profile = profile;
            RestingMood = restingMood;
            DetectionMode = detectionMode;
        }

        internal ConvaiEmotionController Controller { get; }
        internal EmotionPreflight Preflight { get; }
        internal IReadOnlyList<EmotionFinding> Findings { get; }

        /// <summary>The personality asset assigned to this character, or <c>null</c>.</summary>
        internal ConvaiEmotionProfile Profile { get; }

        internal EmotionRestingMood RestingMood { get; }
        internal EmotionDetectionMode DetectionMode { get; }

        internal bool IsPresent => Controller != null;

        internal bool HasBlocker => IsPresent && Preflight.HasBlocker;

        /// <summary>
        ///     Whether this character will visibly react to anything.
        /// </summary>
        /// <remarks>
        ///     Detection Off is the whole answer on its own: with no feelings arriving, every other
        ///     setting is decoration. A character that does receive feelings will show them, since
        ///     an empty recipe list falls back to Convai's default expression library rather than
        ///     driving nothing.
        /// </remarks>
        internal bool IsWorking =>
            IsPresent && !HasBlocker && DetectionMode != EmotionDetectionMode.Off;

        internal EmotionReadiness State =>
            !IsPresent ? EmotionReadiness.NotInstalled
            : HasBlocker ? EmotionReadiness.Blocked
            : DetectionMode == EmotionDetectionMode.Off ? EmotionReadiness.Inert
            : EmotionReadiness.Working;

        /// <summary>One line a survey can show without expanding anything.</summary>
        internal string Summary => State switch
        {
            EmotionReadiness.NotInstalled =>
                "No Emotion component, so this character's face never reacts to what is said.",
            EmotionReadiness.Blocked => Blocker,
            EmotionReadiness.Inert =>
                "Set up, but emotion detection is Off, so this character never receives anything to feel.",
            _ => Profile != null
                ? $"Reacting with the '{Profile.name}' personality, detection {ModeName}."
                : $"Reacting on the Convai defaults, detection {ModeName}."
        };

        /// <summary>What stops it working, or empty when nothing does.</summary>
        internal string Blocker
        {
            get
            {
                if (!IsPresent) return "This character has no Emotion component.";
                IReadOnlyList<EmotionCheck> checks = Preflight.Checks;
                if (checks == null) return string.Empty;
                for (int i = 0; i < checks.Count; i++)
                    if (checks[i].State == EmotionCheckState.Blocked)
                        return checks[i].Detail;
                return string.Empty;
            }
        }

        /// <summary>The detection mode in the words the documentation and the Inspector use.</summary>
        internal string ModeName => EmotionDetectionModes.ShortNameFor(DetectionMode);

        internal static ConvaiEmotionReport For(GameObject characterRoot) =>
            For(characterRoot == null
                ? null
                : characterRoot.GetComponentInChildren<ConvaiEmotionController>(true));

        internal static ConvaiEmotionReport For(ConvaiEmotionController controller)
        {
            if (controller == null)
            {
                return new ConvaiEmotionReport(null, default, System.Array.Empty<EmotionFinding>(),
                    null, default, EmotionDetectionMode.Off);
            }

            EmotionPreflight preflight = EmotionSetupService.Inspect(controller);

            var findings = new List<EmotionFinding>(6);
            EmotionTroubleshooter.Evaluate(controller, in preflight, findings);

            return new ConvaiEmotionReport(
                controller,
                preflight,
                findings,
                EmotionSetupService.ResolveAssignedProfile(controller),
                EmotionSetupService.ResolveEffectiveRestingMood(controller),
                // The VALUE, never enumValueIndex: declaration order is Off, Llm, Nrclex, and
                // reading the declaration index here is the habit that shipped the two detection
                // providers swapped.
                (EmotionDetectionMode)new UnityEditor.SerializedObject(controller)
                    .FindProperty("detectionMode").intValue);
        }

        /// <summary>Stable issue code for a preflight row, e.g. <c>EMOTION_FACE</c>.</summary>
        internal static string IssueCode(string checkId) =>
            $"EMOTION_{(checkId ?? string.Empty).ToUpperInvariant()}";
    }
}
