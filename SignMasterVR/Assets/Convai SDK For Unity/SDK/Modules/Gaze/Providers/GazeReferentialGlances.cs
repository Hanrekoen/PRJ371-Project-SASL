using System;
using System.Collections.Generic;
using Convai.Modules.Gaze.Components;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using Convai.Runtime.SceneMetadata;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Referential (deictic) glances: when the character's own spoken line mentions a
    ///     registered world object ("take a look at the <i>painting</i>"), the character
    ///     glances at that object around the moment of the mention and returns — closing the
    ///     loop with the <c>current_attention_object</c> grounding. Opt-in, owned by Gaze, and
    ///     built entirely on the public <see cref="ConvaiGazeController.GlanceAt(Transform, float)" />
    ///     API, so it needs no changes to the gaze core.
    /// </summary>
    /// <remarks>
    ///     The character's outgoing utterance arrives whole (no word-level timing), so v1 uses
    ///     arrival-time triggering: on the final transcript the object name is matched, a short
    ///     randomized delay is scheduled ("thinks of it, then looks"), and the glance fires.
    ///     A per-object cooldown prevents re-glancing at the same object on every sentence, and
    ///     only one glance is ever pending (the newest mention replaces an unfired one).
    /// </remarks>
    [AddComponentMenu("Convai/Gaze/Advanced/Referential Glances")]
    [DisallowMultipleComponent]
    public sealed class GazeReferentialGlances : MonoBehaviour
    {
        [SerializeField, Range(0.3f, 4f)]
        [Tooltip("Duration (seconds) of the glance at a mentioned object.")]
        private float glanceDuration = 1.6f;

        [SerializeField, Min(0f)]
        [Tooltip("Per-object cooldown (seconds): the same object is not re-glanced within this window.")]
        private float cooldownSeconds = 10f;

        [SerializeField, Range(1, 8)]
        [Tooltip("Longest object name (in words) that is matched — keeps matching cheap and sane.")]
        private int maxMentionWords = 4;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Minimum delay (seconds) between the mention and the glance ('thinks of it, then looks').")]
        private float minDelaySeconds = 0.3f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Maximum delay (seconds) between the mention and the glance.")]
        private float maxDelaySeconds = 0.8f;

        private ConvaiCharacter _character;
        private ConvaiGazeController _controller;
        private bool _subscribed;
        private DeterministicEmbodimentRandom _random;

        private readonly Dictionary<string, float> _cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _nameScratch = new(16);

        private string _pendingName;
        private float _pendingFireTime;

        /// <summary>The object name of the glance currently queued (null when none). Test seam.</summary>
        internal string PendingName => _pendingName;

        private void Awake() => _random = DeterministicEmbodimentRandom.Create(this);

        private void OnEnable()
        {
            _character = GetComponentInParent<ConvaiCharacter>(true);
            ResolveController();

            if (_character != null && !_subscribed)
            {
                _character.OnTranscriptReceived += HandleTranscript;
                _subscribed = true;
            }
        }

        private void OnValidate() => maxDelaySeconds = Mathf.Max(maxDelaySeconds, minDelaySeconds);

        /// <summary>
        ///     The character's gaze controller, re-resolved lazily so the component works no
        ///     matter which order it and the controller were added in.
        /// </summary>
        private ConvaiGazeController ResolveController()
        {
            if (_controller == null) _controller = GetComponentInParent<ConvaiGazeController>(true);
            return _controller;
        }

        private void OnDisable()
        {
            if (_character != null && _subscribed)
            {
                _character.OnTranscriptReceived -= HandleTranscript;
                _subscribed = false;
            }

            _pendingName = null;
        }

        private void HandleTranscript(string text, bool isFinal)
        {
            // Only the final utterance carries the whole sentence; interim results would
            // match (and re-schedule) the same object several times as the text streams in.
            if (isFinal) NotifyUtterance(text);
        }

        /// <summary>
        ///     Feeds a spoken line to the matcher as if the character had just said it — the
        ///     same path the final backend transcript takes. Useful for text-only integrations
        ///     and for offline testing where no backend transcript is available.
        /// </summary>
        public void NotifyUtterance(string utterance)
        {
            if (ResolveController() == null || string.IsNullOrEmpty(utterance)) return;

            _nameScratch.Clear();
            ConvaiObjectMetadata[] all = ConvaiMetadataRegistry.GetValidMetadata();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) _nameScratch.Add(all[i].ObjectName);

            ScheduleGlanceForMention(utterance, _nameScratch, Time.time, ref _random);
        }

        private void Update()
        {
            if (_pendingName == null || ResolveController() == null) return;
            if (Time.time < _pendingFireTime) return;

            string name = _pendingName;
            _pendingName = null;

            ConvaiObjectMetadata target = ResolveMetadata(name);
            if (target == null) return;

            _controller.GlanceAt(target.transform, glanceDuration);
            MarkGlanced(name, Time.time);
        }

        /// <summary>
        ///     Matches <paramref name="utterance" /> against the object names and, on a fresh
        ///     (not-on-cooldown) hit, queues a glance after a randomized delay. The newest
        ///     mention replaces any still-pending glance. Internal so tests can drive it
        ///     without a backend transcript or the metadata registry.
        /// </summary>
        internal void ScheduleGlanceForMention(
            string utterance, IReadOnlyList<string> objectNames, float now, ref DeterministicEmbodimentRandom random)
        {
            if (!TryDecideGlance(utterance, objectNames, now, out string matched)) return;

            _pendingName = matched;
            _pendingFireTime = now + NextDelay(minDelaySeconds, maxDelaySeconds, ref random);
        }

        /// <summary>
        ///     Decides whether <paramref name="utterance" /> should trigger a glance:
        ///     a name matches and it is not within its cooldown window. Pure — no scheduling.
        /// </summary>
        internal bool TryDecideGlance(
            string utterance, IReadOnlyList<string> objectNames, float now, out string matchedName)
        {
            matchedName = null;
            if (!TryMatchMention(utterance, objectNames, maxMentionWords, out string matched)) return false;
            if (_cooldownUntil.TryGetValue(matched, out float until) && now < until) return false;

            matchedName = matched;
            return true;
        }

        /// <summary>Starts the per-object cooldown for <paramref name="name" />.</summary>
        internal void MarkGlanced(string name, float now)
        {
            if (!string.IsNullOrEmpty(name)) _cooldownUntil[name] = now + Mathf.Max(0f, cooldownSeconds);
        }

        private static ConvaiObjectMetadata ResolveMetadata(string name)
        {
            ConvaiObjectMetadata[] all = ConvaiMetadataRegistry.GetValidMetadata();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && string.Equals(all[i].ObjectName, name, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            return null;
        }

        // ── Pure matching / timing (unit-tested) ─────────────────────────────

        /// <summary>Randomized mention→glance delay from the seeded stream.</summary>
        internal static float NextDelay(float min, float max, ref DeterministicEmbodimentRandom random) =>
            random.Range(Mathf.Min(min, max), Mathf.Max(min, max));

        /// <summary>
        ///     Finds the longest registered object name that appears as a whole-word,
        ///     contiguous run in <paramref name="utterance" /> (case-insensitive,
        ///     punctuation-agnostic). "Greedy" = the longest matching name wins, so
        ///     "magic painting" beats a bare "painting".
        /// </summary>
        internal static bool TryMatchMention(
            string utterance, IReadOnlyList<string> objectNames, int maxMentionWords, out string matchedName)
        {
            matchedName = null;
            if (string.IsNullOrWhiteSpace(utterance) || objectNames == null || objectNames.Count == 0)
                return false;

            List<string> words = Tokenize(utterance);
            if (words.Count == 0) return false;

            int limit = Mathf.Max(1, maxMentionWords);
            int bestWordCount = 0;
            var nameWords = new List<string>(8);

            for (int i = 0; i < objectNames.Count; i++)
            {
                string objName = objectNames[i];
                if (string.IsNullOrWhiteSpace(objName)) continue;

                nameWords.Clear();
                TokenizeInto(objName, nameWords);
                if (nameWords.Count == 0 || nameWords.Count > limit) continue;

                if (nameWords.Count > bestWordCount && ContainsSequence(words, nameWords))
                {
                    bestWordCount = nameWords.Count;
                    matchedName = objName;
                }
            }

            return matchedName != null;
        }

        private static List<string> Tokenize(string text)
        {
            var result = new List<string>(16);
            TokenizeInto(text, result);
            return result;
        }

        private static void TokenizeInto(string text, List<string> into)
        {
            if (string.IsNullOrEmpty(text)) return;

            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                bool alphanumeric = char.IsLetterOrDigit(text[i]);
                if (alphanumeric && start < 0)
                {
                    start = i;
                }
                else if (!alphanumeric && start >= 0)
                {
                    into.Add(text.Substring(start, i - start).ToLowerInvariant());
                    start = -1;
                }
            }

            if (start >= 0) into.Add(text.Substring(start).ToLowerInvariant());
        }

        private static bool ContainsSequence(List<string> haystack, List<string> needle)
        {
            if (needle.Count == 0 || needle.Count > haystack.Count) return false;

            for (int i = 0; i <= haystack.Count - needle.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Count; j++)
                {
                    if (!string.Equals(haystack[i + j], needle[j], StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return true;
            }

            return false;
        }
    }
}
