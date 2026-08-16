using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Modules.Emotion.Taxonomy
{
    /// <summary>
    ///     Serializable entry used by <see cref="EmotionTaxonomyAsset" /> to author the
    ///     emotion vocabulary.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Mirrors <see cref="EmotionDescriptor" /> in shape but stays mutable so Unity
    ///         can serialize it in the inspector. The asset converts entries into immutable
    ///         descriptors at load time.
    ///     </para>
    /// </remarks>
    [Serializable]
    public sealed class EmotionTaxonomyEntry
    {
        [SerializeField, Tooltip("The name of this emotion, lowercase (for example 'joy' or 'anger'). Each one must appear only once in this vocabulary.")]
        private string label;

        [SerializeField, Tooltip("Other words that mean this emotion. A reply labelled with any of them is treated as this one, so a backend that says 'happiness' can still reach 'joy'.")]
        private List<string> aliases = new();

        [SerializeField, Tooltip("Emotions that read naturally alongside this one, and come along at reduced strength when the character shows it — joy with a trace of trust, for example. Only used when the personality has mixing turned on.")]
        private List<string> complements = new();

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How much this emotion shapes the mouth while the character is not speaking. Lip sync owns the mouth whenever it is.")]
        private float defaultMouthInfluence = 0.5f;

        [SerializeField, Tooltip("The emotion that means 'feeling nothing in particular'. Mark exactly one, since it is what the face relaxes to.")]
        private bool isNeutral;

        [SerializeField, Tooltip("Describe this emotion yourself instead of using Convai's own description of it. The values below tell other features — how a character moves and holds itself — what this emotion feels like.")]
        private bool useCustomDimensions;

        // These four keep research names because they are the vocabulary the behaviour features
        // read, and renaming a serialized field would orphan the value in every vocabulary asset
        // already authored. The tooltip is where a user meets them, so the tooltip says what each
        // one means in ordinary words.
        [SerializeField, Range(-1f, 1f)]
        [Tooltip("How pleasant this emotion is. +1 is delight, -1 is misery, 0 is neither.")]
        private float valence;

        [SerializeField, Range(-1f, 1f)]
        [Tooltip("How worked up this emotion is. +1 is keyed up and fast, -1 is subdued and slow.")]
        private float arousal;

        [SerializeField, Range(-1f, 1f)]
        [Tooltip("How much in control the character feels. +1 is in charge, -1 is at the mercy of events.")]
        private float agency;

        [SerializeField, Range(-1f, 1f)]
        [Tooltip("Whether this emotion moves the character toward what caused it or away from it. +1 leans in, -1 pulls back.")]
        private float approach;

        public string Label => label;
        public IReadOnlyList<string> Aliases => aliases;
        public IReadOnlyList<string> Complements => complements;
        public float DefaultMouthInfluence => defaultMouthInfluence;
        public bool IsNeutral => isNeutral;
        public bool UseCustomDimensions => useCustomDimensions;
        public EmotionDimensions Dimensions => useCustomDimensions
            ? new EmotionDimensions(valence, arousal, agency, approach)
            : EmotionDimensionDefaults.Resolve(label);

        public EmotionTaxonomyEntry() { }

        public EmotionTaxonomyEntry(
            string label,
            IEnumerable<string> aliases,
            IEnumerable<string> complements,
            float defaultMouthInfluence,
            bool isNeutral)
        {
            this.label = label;
            this.aliases = aliases != null ? new List<string>(aliases) : new List<string>();
            this.complements = complements != null ? new List<string>(complements) : new List<string>();
            this.defaultMouthInfluence = defaultMouthInfluence;
            this.isNeutral = isNeutral;
        }

        /// <summary>Converts this serializable entry into an immutable <see cref="EmotionDescriptor" />.</summary>
        internal EmotionDescriptor ToDescriptor()
        {
            return new EmotionDescriptor(
                label: label,
                aliases: aliases?.ToArray() ?? Array.Empty<string>(),
                complements: complements?.ToArray() ?? Array.Empty<string>(),
                defaultMouthInfluence: defaultMouthInfluence,
                isNeutral: isNeutral,
                dimensions: Dimensions);
        }
    }
}
