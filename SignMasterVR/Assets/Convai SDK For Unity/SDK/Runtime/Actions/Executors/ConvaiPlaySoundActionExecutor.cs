using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Plays a sound when the character performs the action — a doorbell, a machine starting up,
    ///     a chime that confirms something worked. It uses an ordinary Unity
    ///     <see cref="AudioSource" />, so nothing else has to be set up on the object making the noise.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Works with or without a target, and that is your choice when you author the
    ///         action.</b> Author it with no target and assign an Audio Source below, and it simply
    ///         plays — "play a chime". Author it with a target and leave the Audio Source empty, and
    ///         the sound comes from whatever the character was asked to act on — "play a sound from
    ///         the machine". Same behavior, two actions.
    ///     </para>
    ///     <para>
    ///         <b>It never plays through the character's own voice.</b> The Audio Source is either the
    ///         one assigned below or one on the object the action points at — the character's speech
    ///         Audio Source is deliberately never picked up automatically, because borrowing it would
    ///         cut the character off mid-sentence. With neither available the action declines and says
    ///         what to assign.
    ///     </para>
    ///     <para>
    ///         When <c>Wait for the sound to finish</c> is on and the action is cancelled — because a
    ///         newer action replaced it, or it timed out — the Audio Source is stopped as the action
    ///         unwinds, so a cancelled action leaves no sound hanging behind it.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Play Sound")]
    [ConvaiActionArchetype(
        "Play Sound",
        ActionName = "Play Sound",
        Description = "Plays a sound. Give the action a target and the sound comes from that object; " +
                      "author it with no target and assign an Audio Source instead.",
        TargetRequirement = ConvaiActionTargetRequirement.Either,
        Parameters = new[] { "volume,Number" },
        ParameterDescriptions = new[]
        {
            "Playback volume from 0 (silent) to 1 (full authored volume). Omit it to use the " +
            "executor's Inspector setting."
        })]
    public sealed class ConvaiPlaySoundActionExecutor : ConvaiTargetedActionExecutor
    {
        /// <summary>
        ///     Extra time allowed on top of the clip's own length before the wait gives up. Covers a
        ///     source playing slower than normal pitch and ordinary frame jitter, so a clip that did
        ///     finish is never reported as stuck.
        /// </summary>
        private const float WaitGraceSeconds = 0.5f;

        [SerializeField]
        [Tooltip("The Audio Source to play through. Leave empty to use one on the object the action " +
                 "points at. The character's own speech Audio Source is never used.")]
        private AudioSource _audioSource;

        [SerializeField]
        [Tooltip("The sound to play. Leave empty to play whatever clip the Audio Source already has.")]
        private AudioClip _clip;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("How loud to play it, relative to the Audio Source's own volume. The character can " +
                 "ask for a different level per call with the 'volume' parameter.")]
        private float _volume = 1f;

        [SerializeField]
        [Tooltip("Hold the action open until the sound finishes. Turn this on when the next action " +
                 "should not start until the sound is done.")]
        private bool _waitForSoundToFinish;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            AudioSource source = ResolveAudioSource(invocation);
            if (source == null)
            {
                return ConvaiActionExecutionResult.Unhandled(
                    "No Audio Source to play through. Assign one on this behavior, or add an Audio " +
                    "Source to the object this action points at.");
            }

            AudioClip clip = _clip != null ? _clip : source.clip;
            if (clip == null)
            {
                return ConvaiActionExecutionResult.Failed(
                    "No sound to play. Assign a clip on this behavior or on the Audio Source.",
                    ConvaiActionFailureReason.InvalidState);
            }

            float volume = Mathf.Clamp01(GetOverride(invocation, "volume", _volume));

            // PlayOneShot for both cases: it layers on top of the source instead of replacing its
            // authored clip, and it scales volume per call, so playing a sound never leaves the
            // Audio Source configured differently than the scene author left it.
            source.PlayOneShot(clip, volume);

            if (!_waitForSoundToFinish)
                return ConvaiActionExecutionResult.Succeeded();

            float pitch = Mathf.Abs(source.pitch);
            float clipSeconds = pitch > 0.01f ? clip.length / pitch : clip.length;

            // Stopping on cancellation is registered only around the wait: outside it the sound is
            // fire-and-forget by design, and stopping the source then would silence audio this action
            // is no longer responsible for.
            using (cancellationToken.Register(source.Stop))
            {
                await ConvaiActionAsyncUtility.WaitSecondsAsync(clipSeconds + WaitGraceSeconds, cancellationToken);
            }

            return ConvaiActionExecutionResult.Succeeded();
        }

        /// <summary>
        ///     Finds the Audio Source to use: the explicitly assigned one first, then one on the
        ///     object this action points at (including its children). Never searches the character —
        ///     see the note on this class.
        /// </summary>
        private AudioSource ResolveAudioSource(ConvaiActionInvocation invocation)
        {
            if (_audioSource != null)
                return _audioSource;

            GameObject targetObject = ResolveTargetGameObject(invocation);
            return targetObject == null ? null : targetObject.GetComponentInChildren<AudioSource>(true);
        }

        /// <summary>
        ///     Keeps the volume inside 0–1. The <c>Range</c> attribute only constrains the Inspector
        ///     slider, so a script or a stale asset can still write something outside it.
        /// </summary>
        private void OnValidate() => _volume = Mathf.Clamp01(_volume);
    }
}
