using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Modules.Emotion.Components;
using Convai.Runtime.Actions;
using Convai.Runtime.Logging;
using Convai.Runtime.Utilities;
using UnityEngine;

namespace Convai.Modules.Emotion.Executors
{
    /// <summary>
    ///     Changes how the character feels, and keeps it there. The shift eases in over time rather
    ///     than snapping, and it colours everything afterwards — expression, posture, how the
    ///     character delivers its lines — until something changes it again.
    /// </summary>
    /// <remarks>
    ///     This is the lasting one. For a short beat that passes — a flinch, a flash of delight —
    ///     use <see cref="ConvaiReactActionExecutor" /> instead; using this for a momentary reaction
    ///     leaves the character stuck in that mood for the rest of the conversation.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Set Mood")]
    [ConvaiActionArchetype(
        "Set Mood",
        ActionName = "Set Mood",
        Description = "Changes how the character feels and keeps it there, easing into the new mood.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        Parameters = new[] { "mood,Choice", "intensity,Number" },
        ParameterDescriptions = new[]
        {
            "Name the ongoing mood to express using a label supported by the character's Emotion " +
            "Profile. Always provide a mood.",
            "Strength of the mood from 0 (subtle) to 1 (fully expressed)."
        },
        RequiredPeerHint = "ConvaiEmotionController")]
    public sealed class ConvaiSetMoodActionExecutor : ConvaiCharacterActionExecutor<ConvaiEmotionController>
    {
        [SerializeField, ConvaiEmotionLabel("None — do nothing without a mood")]
        [Tooltip("The mood to use when the character does not name one. Normally the 'mood' parameter " +
                 "drives this instead.")]
        private string _defaultMood = string.Empty;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("How strongly to feel it. The character can ask for a different strength per call " +
                 "with the 'intensity' parameter.")]
        private float _defaultIntensity = 0.6f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("How long the change takes, in seconds. A mood that arrives instantly reads as a " +
                 "glitch; give it a moment.")]
        private float _transitionSeconds = 1.5f;

        private readonly HashSet<string> _warnedMoods = new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiEmotionController emotion,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            string mood = GetOverride(invocation, "mood", _defaultMood);
            if (string.IsNullOrWhiteSpace(mood))
            {
                return Task.FromResult(ConvaiActionExecutionResult.Unhandled(
                    "No mood was given, and this behavior has no default one."));
            }

            if (!emotion.TryResolveEmotionLabel(mood, out string knownMood))
                return Task.FromResult(RejectUnknownMood(mood, emotion));

            float intensity = Mathf.Clamp01(GetOverride(invocation, "intensity", _defaultIntensity));
            float transitionSeconds = Mathf.Max(0f, GetOverride(invocation, "transition", _transitionSeconds));

            emotion.SetMood(knownMood, intensity, transitionSeconds);
            return Task.FromResult(ConvaiActionExecutionResult.Succeeded($"Now feeling {knownMood}."));
        }

        /// <summary>
        ///     Rejects a mood the character does not have, naming the ones it does.
        /// </summary>
        /// <remarks>
        ///     Failing loudly here is deliberate. The emotion system quietly treats an unknown mood as
        ///     neutral, so accepting it would produce a character that visibly does nothing while every
        ///     log says the action succeeded — the single hardest kind of problem to track down. The
        ///     warning is logged once per unrecognised word so a mistyped action definition surfaces
        ///     without filling the console; the message itself is built every call because the spoken
        ///     feedback relay may say it back to the player.
        /// </remarks>
        private ConvaiActionExecutionResult RejectUnknownMood(string mood, ConvaiEmotionController emotion)
        {
            string known = string.Join(", ", emotion.KnownEmotionLabels);
            string message = string.IsNullOrEmpty(known)
                ? $"This character has no mood called '{mood}' — its Emotion Taxonomy has no moods at all."
                : $"This character has no mood called '{mood}'. It knows: {known}.";

            if (_warnedMoods.Add(mood))
                ConvaiLogger.Warning($"[{nameof(ConvaiSetMoodActionExecutor)}] {message}", LogCategory.Character);

            return ConvaiActionExecutionResult.Failed(message, ConvaiActionFailureReason.Custom);
        }

        private void OnValidate()
        {
            _defaultIntensity = Mathf.Clamp01(_defaultIntensity);
            _transitionSeconds = Mathf.Max(0f, _transitionSeconds);
        }
    }
}
