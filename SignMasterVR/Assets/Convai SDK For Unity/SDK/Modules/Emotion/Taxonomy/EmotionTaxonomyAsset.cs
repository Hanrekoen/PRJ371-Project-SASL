using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.Emotion.Taxonomy
{
    /// <summary>
    ///     Data-driven emotion vocabulary asset. Authors ship an instance alongside the
    ///     emotion profile so the pipeline can evolve independently of the runtime protocol.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Exactly one entry must be marked as neutral. Labels are compared
    ///         case-insensitively and canonicalized to lowercase at load time. Aliases allow
    ///         server labels like <c>"happy"</c> to resolve to <c>"joy"</c> without polluting
    ///         the taxonomy.
    ///     </para>
    ///     <para>
    ///         The asset is immutable at runtime once <see cref="EnsureBuilt" /> has executed;
    ///         changes in the inspector while in play-mode invalidate the cached tables so
    ///         the next access rebuilds them.
    ///     </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "EmotionTaxonomy",
        menuName = "Convai/Embodiment/Emotion Taxonomy",
        order = 131)]
    public sealed class EmotionTaxonomyAsset : ScriptableObject, IEmotionTaxonomy
    {
        [SerializeField, Tooltip("Every emotion this vocabulary defines. Exactly one must be marked as the neutral one.")]
        private List<EmotionTaxonomyEntry> entries = new();

        private readonly List<EmotionDescriptor> _descriptors = new();
        private readonly Dictionary<string, EmotionDescriptor> _byLabel =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EmotionDescriptor> _byAlias =
            new(StringComparer.OrdinalIgnoreCase);
        private EmotionDescriptor _neutral;
        private bool _built;

        /// <inheritdoc />
        public IReadOnlyList<EmotionDescriptor> Emotions
        {
            get
            {
                EnsureBuilt();
                return _descriptors;
            }
        }

        /// <inheritdoc />
        public EmotionDescriptor Neutral
        {
            get
            {
                EnsureBuilt();
                return _neutral;
            }
        }

        /// <inheritdoc />
        public bool TryResolve(string serverLabel, out EmotionDescriptor descriptor)
        {
            EnsureBuilt();
            descriptor = null;
            if (string.IsNullOrWhiteSpace(serverLabel)) return false;

            string trimmed = serverLabel.Trim();
            if (_byLabel.TryGetValue(trimmed, out descriptor)) return true;
            if (_byAlias.TryGetValue(trimmed, out descriptor)) return true;
            return false;
        }

        private void OnEnable() => _built = false;

        /// <summary>Rebuilds resolution tables from <see cref="entries" />. Idempotent.</summary>
        public void EnsureBuilt()
        {
            if (_built) return;

            _descriptors.Clear();
            _byLabel.Clear();
            _byAlias.Clear();
            _neutral = null;

            if (entries == null) { _built = true; return; }

            for (int i = 0; i < entries.Count; i++)
            {
                EmotionTaxonomyEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Label)) continue;

                EmotionDescriptor descriptor;
                try { descriptor = entry.ToDescriptor(); }
                catch (ArgumentException) { continue; }

                if (_byLabel.ContainsKey(descriptor.Label))
                    continue; // first-win on duplicates

                _descriptors.Add(descriptor);
                _byLabel[descriptor.Label] = descriptor;

                for (int a = 0; a < descriptor.Aliases.Count; a++)
                {
                    string alias = descriptor.Aliases[a];
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    alias = alias.Trim();
                    if (_byAlias.ContainsKey(alias)) continue;
                    _byAlias[alias] = descriptor;
                }

                if (descriptor.IsNeutral && _neutral == null)
                    _neutral = descriptor;
            }

            if (_neutral == null)
            {
                ConvaiLogger.Warning(
                    "[EmotionTaxonomyAsset] This emotion vocabulary marks no emotion as the neutral one, so a " +
                    "stand-in is being used. Tick 'Is Neutral' on exactly one emotion — it is what the " +
                    "face relaxes to between feelings.",
                    LogCategory.SDK);

                // Synthesize a neutral so the module still works when the taxonomy is malformed.
                _neutral = new EmotionDescriptor(
                    label: "neutral",
                    aliases: Array.Empty<string>(),
                    complements: Array.Empty<string>(),
                    defaultMouthInfluence: 0f,
                    isNeutral: true);
                _descriptors.Insert(0, _neutral);
                _byLabel[_neutral.Label] = _neutral;
            }

            _built = true;
        }

        private void OnValidate()
        {
            _built = false;

            if (entries == null || entries.Count == 0) return;

            int neutralCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                EmotionTaxonomyEntry entry = entries[i];
                if (entry != null && entry.IsNeutral) neutralCount++;
            }

            if (neutralCount == 0)
                ConvaiLogger.Warning(
                    "[EmotionTaxonomyAsset] This emotion vocabulary marks no emotion as the neutral one, so a stand-in will be used at runtime. Tick 'Is Neutral' on exactly one emotion — it is what the face relaxes to between feelings.",
                    LogCategory.SDK);
            else if (neutralCount > 1)
                ConvaiLogger.Warning(
                    $"[EmotionTaxonomyAsset] {neutralCount} emotions in this vocabulary are ticked 'Is Neutral' and only the first is used. Untick the others, so it is clear which one the face relaxes to.",
                    LogCategory.SDK);
        }

        /// <summary>
        ///     A newly created vocabulary asset starts as a copy of the built-in one.
        /// </summary>
        /// <remarks>
        ///     An empty vocabulary is a character that understands no emotions at all — every
        ///     dropdown empty, every incoming emotion unresolved, and one warning about a missing
        ///     neutral entry. Authoring a vocabulary is almost always "the built-in set, with our
        ///     own words", so that is what a new asset contains; renaming and adding entries is
        ///     then editing rather than starting from nothing.
        /// </remarks>
        private void Reset()
        {
            if (entries != null && entries.Count > 0) return;

            entries = BuiltInEntries();
            _built = false;
        }

        /// <summary>Creates the built-in emotion vocabulary used when no asset is wired up.</summary>
        public static EmotionTaxonomyAsset CreateDefault()
        {
            EmotionTaxonomyAsset instance = CreateInstance<EmotionTaxonomyAsset>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.entries = BuiltInEntries();
            instance._built = false;
            return instance;
        }

        /// <summary>The vocabulary every character falls back to, as fresh authoring entries.</summary>
        private static List<EmotionTaxonomyEntry> BuiltInEntries()
        {
            return new List<EmotionTaxonomyEntry>
            {
                // Complements are what "show more than one emotion at once" actually blends in.
                // Every non-neutral entry carries one, because a real face rarely shows a single
                // pure emotion: anger reads as contempt with a trace of disgust, fear as a startle
                // with a trace of surprise. Seven of these were empty, which left the blending
                // feature doing nothing for every emotion except joy and trust.
                new("neutral",  new[] { "calm", "idle" },                new string[] { }, 0f,    true),
                new("joy",      new[] { "happy", "happiness", "ecstasy", "serenity", "excited", "enthusiastic" },      new[] { "trust" }, 0.6f, false),
                new("trust",    new[] { "acceptance", "admiration", "confident", "reassured" },     new[] { "joy" },  0.3f, false),
                new("fear",     new[] { "afraid", "apprehension", "terror", "fearful", "worried", "anxious", "nervous" },     new[] { "surprise" }, 0.4f, false),
                new("surprise", new[] { "amazement", "distraction", "surprised" },           new[] { "fear" }, 0.5f, false),
                new("sadness",  new[] { "sad", "pensiveness", "grief" },                    new[] { "disgust" }, 0.3f, false),
                new("disgust",  new[] { "disgusted", "loathing", "boredom", "bored" },      new[] { "anger" }, 0.4f, false),
                new("anger",    new[] { "angry", "annoyance", "rage" },                     new[] { "disgust" }, 0.55f, false),
                new("anticipation", new[] { "interest", "vigilance", "curious", "curiosity", "eager", "hopeful" },                      new[] { "joy" }, 0.45f, false),
            };
        }
    }
}
