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
    ///     A moment of feeling that passes — surprise at bad news, a flash of delight, a wince. The
    ///     character shows it, holds it briefly, and settles back to whatever it was feeling before.
    /// </summary>
    /// <remarks>
    ///     The restore is guaranteed: even if the action is cancelled or replaced mid-beat, the
    ///     character comes back to its own mood rather than freezing in the reaction. That is the
    ///     whole difference between this and <see cref="ConvaiSetMoodActionExecutor" /> — one is a
    ///     beat, the other is a change.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/React")]
    [ConvaiActionArchetype(
        "React",
        ActionName = "React",
        Description = "Shows a moment of feeling that passes, then settles back — a flinch, a flash " +
                      "of delight, a wince.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        Parameters = new[] { "reaction,Choice", "intensity,Number" },
        ParameterDescriptions = new[]
        {
            "Name the brief emotional reaction to express using a label supported by the character's " +
            "Emotion Profile. Always provide a reaction.",
            "Strength of the reaction from 0 (subtle) to 1 (fully expressed)."
        },
        RequiredPeerHint = "ConvaiEmotionController")]
    public sealed class ConvaiReactActionExecutor : ConvaiCharacterActionExecutor<ConvaiEmotionController>
    {
        [SerializeField, ConvaiEmotionLabel("None — do nothing without a reaction")]
        [Tooltip("The reaction to use when the character does not name one. Normally the 'reaction' " +
                 "parameter drives this instead.")]
        private string _defaultReaction = string.Empty;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("How strong the reaction is. Reactions read best a little stronger than a mood, " +
                 "because they are over quickly.")]
        private float _defaultIntensity = 0.85f;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("How long the reaction is held before the character settles back, in seconds.")]
        private float _holdSeconds = 1.5f;

        private readonly HashSet<string> _warnedReactions = new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiEmotionController emotion,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            string reaction = GetOverride(invocation, "reaction", _defaultReaction);
            if (string.IsNullOrWhiteSpace(reaction))
            {
                return ConvaiActionExecutionResult.Unhandled(
                    "No reaction was given, and this behavior has no default one.");
            }

            if (!emotion.TryResolveEmotionLabel(reaction, out string knownReaction))
                return RejectUnknownReaction(reaction, emotion);

            float intensity = Mathf.Clamp01(GetOverride(invocation, "intensity", _defaultIntensity));
            float holdSeconds = Mathf.Max(0.1f, GetOverride(invocation, "holdSeconds", _holdSeconds));

            emotion.SetEmotionOverride(knownReaction, intensity);
            try
            {
                await ConvaiActionAsyncUtility.WaitSecondsAsync(holdSeconds, cancellationToken);
                return ConvaiActionExecutionResult.Succeeded($"Reacted with {knownReaction}.");
            }
            finally
            {
                // In a finally block on purpose: a cancelled reaction must still hand the character
                // back its own mood, or an interruption would leave the face stuck mid-beat.
                emotion.ClearEmotionOverride();
            }
        }

        /// <summary>
        ///     Rejects a reaction the character does not have, naming the ones it does. See the note
        ///     on <see cref="ConvaiSetMoodActionExecutor" /> for why this fails rather than passing an
        ///     unknown word through to a silent no-op.
        /// </summary>
        private ConvaiActionExecutionResult RejectUnknownReaction(string reaction, ConvaiEmotionController emotion)
        {
            string known = string.Join(", ", emotion.KnownEmotionLabels);
            string message = string.IsNullOrEmpty(known)
                ? $"This character has no reaction called '{reaction}' — its Emotion Taxonomy has none at all."
                : $"This character has no reaction called '{reaction}'. It knows: {known}.";

            if (_warnedReactions.Add(reaction))
                ConvaiLogger.Warning($"[{nameof(ConvaiReactActionExecutor)}] {message}", LogCategory.Character);

            return ConvaiActionExecutionResult.Failed(message, ConvaiActionFailureReason.Custom);
        }

        private void OnValidate()
        {
            _defaultIntensity = Mathf.Clamp01(_defaultIntensity);
            _holdSeconds = Mathf.Max(0.1f, _holdSeconds);
        }
    }
}
