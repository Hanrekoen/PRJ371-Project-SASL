using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Convai.Shared.Actions
{
    /// <summary>
    ///     Connect-time action affordances for the current session.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionConfig
    {
        [field: SerializeField]
        [JsonProperty("actions", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Actions { get; set; } = new();

        [field: SerializeField]
        [JsonProperty("characters", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConvaiActionCharacterDefinition> Characters { get; set; } = new();

        [field: SerializeField]
        [JsonProperty("objects", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConvaiActionObjectDefinition> Objects { get; set; } = new();

        [field: SerializeField]
        [JsonProperty("current_attention_object", NullValueHandling = NullValueHandling.Ignore)]
        public string CurrentAttentionObject { get; set; }

        public ConvaiActionConfig Clone() =>
            new()
            {
                Actions = Actions == null ? new List<string>() : new List<string>(Actions),
                Characters = CloneCharacters(Characters),
                Objects = CloneObjects(Objects),
                CurrentAttentionObject = CurrentAttentionObject
            };

        private static List<ConvaiActionCharacterDefinition> CloneCharacters(
            IReadOnlyList<ConvaiActionCharacterDefinition> characters)
        {
            var clone = new List<ConvaiActionCharacterDefinition>();
            if (characters == null)
                return clone;

            foreach (ConvaiActionCharacterDefinition character in characters)
                clone.Add(character?.Clone() ?? new ConvaiActionCharacterDefinition());

            return clone;
        }

        private static List<ConvaiActionObjectDefinition> CloneObjects(
            IReadOnlyList<ConvaiActionObjectDefinition> objects)
        {
            var clone = new List<ConvaiActionObjectDefinition>();
            if (objects == null)
                return clone;

            foreach (ConvaiActionObjectDefinition actionObject in objects)
                clone.Add(actionObject?.Clone() ?? new ConvaiActionObjectDefinition());

            return clone;
        }
    }

    /// <summary>
    ///     Runtime patch for the active session's action affordances. A null list is omitted and
    ///     preserves the current value; an empty list explicitly clears that request-level list.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionConfigPatch
    {
        /// <summary>Replacement action list. Null preserves; empty clears.</summary>
        [JsonProperty("actions", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Actions { get; set; }

        /// <summary>Replacement character-target list. Null preserves; empty clears.</summary>
        [JsonProperty("characters", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConvaiActionCharacterDefinition> Characters { get; set; }

        /// <summary>Replacement object-target list. Null preserves; empty clears.</summary>
        [JsonProperty("objects", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConvaiActionObjectDefinition> Objects { get; set; }

        /// <summary>
        ///     Optional attention update resolved after list replacement. Null preserves; empty clears.
        /// </summary>
        [JsonProperty("current_attention_object", NullValueHandling = NullValueHandling.Ignore)]
        public string CurrentAttentionObject { get; set; }

        /// <summary>Creates a deep copy while preserving omitted-versus-empty list semantics.</summary>
        public ConvaiActionConfigPatch Clone() =>
            new()
            {
                Actions = Actions == null ? null : new List<string>(Actions),
                Characters = CloneCharacters(Characters),
                Objects = CloneObjects(Objects),
                CurrentAttentionObject = CurrentAttentionObject
            };

        private static List<ConvaiActionCharacterDefinition> CloneCharacters(
            IReadOnlyList<ConvaiActionCharacterDefinition> characters)
        {
            if (characters == null)
                return null;

            var clone = new List<ConvaiActionCharacterDefinition>(characters.Count);
            for (int i = 0; i < characters.Count; i++)
                clone.Add(characters[i]?.Clone());

            return clone;
        }

        private static List<ConvaiActionObjectDefinition> CloneObjects(
            IReadOnlyList<ConvaiActionObjectDefinition> objects)
        {
            if (objects == null)
                return null;

            var clone = new List<ConvaiActionObjectDefinition>(objects.Count);
            for (int i = 0; i < objects.Count; i++)
                clone.Add(objects[i]?.Clone());

            return clone;
        }
    }

    /// <summary>
    ///     Explicit action target object available to the backend for grounding.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionObjectDefinition
    {
        [field: SerializeField]
        [field: Tooltip("What the Convai Character calls this object. Players can use this name when asking for it.")]
        [JsonProperty("name")]
        public string Name { get; set; }

        [field: SerializeField]
        [field: Tooltip("A short description sent to the Convai Character so it understands what this object is.")]
        [JsonProperty("description")]
        public string Description { get; set; }

        [field: SerializeField]
        [field: Tooltip("The object in your scene this entry stands for. Without it the Convai Character can " +
                        "talk about this entry but cannot walk to it, look at it, or use it.")]
        [JsonIgnore]
        public GameObject GameObjectReference { get; set; }

        /// <summary>
        ///     Declares that this entry deliberately has no object in the scene: the Convai Character
        ///     knows the name and can talk about it, and nothing can be performed on it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The difference between "I meant this" and "I forgot to link it". An entry with no
        ///         <see cref="GameObjectReference" /> and this left <c>false</c> is reported as an
        ///         error, because a targeted action can never resolve it; the same entry with this set
        ///         is reported as nothing at all.
        ///     </para>
        ///     <para>
        ///         Local-only and never sent to Convai — the backend receives the same name and
        ///         description either way.
        ///     </para>
        /// </remarks>
        [field: SerializeField]
        [field: Tooltip("Tick when nothing in the scene answers to this entry. The Convai Character will know " +
                        "the name and be able to talk about it, and will not try to act on it.")]
        [JsonIgnore]
        public bool TextOnly { get; set; }

        /// <summary>
        ///     Local-only alternate names the resolution ladder matches exactly (step 2) before
        ///     falling through to normalized/contains matching. Never sent to the backend.
        /// </summary>
        [field: SerializeField]
        [field: Tooltip("Extra wording that should match this entry when a request arrives. The name above " +
                        "already matches on its own, and so does close wording — add one only for a word the " +
                        "name would miss, like 'lamp' for a lantern. Never sent to Convai.")]
        [JsonIgnore]
        public List<string> Aliases { get; set; } = new();

        /// <summary>
        ///     Local-only explicit interaction point. When null, resolution falls back to
        ///     <see cref="GameObjectReference" />'s transform. Never sent to the backend.
        /// </summary>
        [field: SerializeField]
        [field: Tooltip("Where this Convai Character ends up when it acts on this object. Leave empty to use " +
                        "the object itself; point it at a small empty Transform to be exact — in front of a " +
                        "door rather than inside it. Never sent to Convai.")]
        [JsonIgnore]
        public Transform InteractionPoint { get; set; }

        /// <summary>
        ///     Local-only availability flag consulted by the resolution ladder (unavailable entries
        ///     are skipped at every step). Never sent to the backend.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Set while the merged config is being built, not by user code:
        ///         <c>ConvaiCharacterActions.SetTargetAvailable</c> writes to a separate per-character
        ///         overlay that is applied over whatever this says, so a despawned object can be
        ///         withdrawn without touching any authored data. That is also why nothing here needs
        ///         to survive a scene save — the overlay is deliberately session-scoped, and this
        ///         field is rebuilt from its sources every time the config is merged.
        ///     </para>
        /// </remarks>
        /// <remarks>
        ///     <para>
        ///         <b>Stored inverted, and that is load-bearing.</b> Unity rebuilds a
        ///         <see cref="SerializableAttribute" /> instance field by field without running a
        ///         constructor, so a property initializer never executes for an entry loaded from a
        ///         scene or a prefab. Written as <c>= true</c> this therefore came back <c>false</c>
        ///         on every authored target the moment it was loaded — and because the resolution
        ///         ladder skips unavailable entries at every rung, every registered place, prop and
        ///         person silently stopped resolving. The symptom is brutal to read: targetless
        ///         actions keep working perfectly while every targeted one is dropped before it
        ///         reaches a handler, so the character looks like it is ignoring half its abilities.
        ///     </para>
        ///     <para>
        ///         Inverting the stored value makes the zero-default mean available, which is the one
        ///         encoding that survives Unity's constructor-less deserialization.
        ///     </para>
        /// </remarks>
        [JsonIgnore]
        public bool Available
        {
            get => !_unavailable;
            set => _unavailable = !value;
        }

        /// <summary>Backing store for <see cref="Available" />; false (the default) means available.</summary>
        private bool _unavailable;

        public ConvaiActionObjectDefinition Clone() =>
            new()
            {
                Name = Name,
                Description = Description,
                GameObjectReference = GameObjectReference,
                TextOnly = TextOnly,
                Aliases = Aliases == null ? new List<string>() : new List<string>(Aliases),
                InteractionPoint = InteractionPoint,
                Available = Available
            };
    }

    /// <summary>
    ///     Explicit action target character available to the backend for grounding.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionCharacterDefinition
    {
        [field: SerializeField]
        [field: Tooltip("What the Convai Character calls this character.")]
        [JsonProperty("name")]
        public string Name { get; set; }

        [field: SerializeField]
        [field: Tooltip("A short background sent to the Convai Character so it understands who this character is.")]
        [JsonProperty("bio")]
        public string Bio { get; set; }

        [field: SerializeField]
        [field: Tooltip("The character in your scene this entry stands for. Without it the Convai Character can " +
                        "talk about this entry but cannot walk to it, look at it, or act on it.")]
        [JsonIgnore]
        public GameObject GameObjectReference { get; set; }

        /// <summary>
        ///     Declares that this entry deliberately has no object in the scene: the Convai Character
        ///     knows the name and can talk about it, and nothing can be performed on it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The difference between "I meant this" and "I forgot to link it". An entry with no
        ///         <see cref="GameObjectReference" /> and this left <c>false</c> is reported as an
        ///         error, because a targeted action can never resolve it; the same entry with this set
        ///         is reported as nothing at all.
        ///     </para>
        ///     <para>
        ///         Local-only and never sent to Convai — the backend receives the same name and
        ///         description either way.
        ///     </para>
        /// </remarks>
        [field: SerializeField]
        [field: Tooltip("Tick when nothing in the scene answers to this entry. The Convai Character will know " +
                        "the name and be able to talk about it, and will not try to act on it.")]
        [JsonIgnore]
        public bool TextOnly { get; set; }

        /// <summary>
        ///     Local-only alternate names the resolution ladder matches exactly (step 2) before
        ///     falling through to normalized/contains matching. Never sent to the backend.
        /// </summary>
        [field: SerializeField]
        [field: Tooltip("Extra wording that should match this entry when a request arrives. The name above " +
                        "already matches on its own, and so does close wording — add one only for a word the " +
                        "name would miss, like 'the shopkeeper' for Mira. Never sent to Convai.")]
        [JsonIgnore]
        public List<string> Aliases { get; set; } = new();

        /// <summary>
        ///     Local-only explicit interaction point. When null, resolution falls back to
        ///     <see cref="GameObjectReference" />'s transform. Never sent to the backend.
        /// </summary>
        [field: SerializeField]
        [field: Tooltip("Where this Convai Character ends up when it acts on this character. Leave empty to " +
                        "use the character itself; point it at a small empty Transform to be exact — beside " +
                        "them rather than inside them. Never sent to Convai.")]
        [JsonIgnore]
        public Transform InteractionPoint { get; set; }

        /// <summary>
        ///     Local-only availability flag consulted by the resolution ladder (unavailable entries
        ///     are skipped at every step). Never sent to the backend.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Set while the merged config is being built, not by user code:
        ///         <c>ConvaiCharacterActions.SetTargetAvailable</c> writes to a separate per-character
        ///         overlay that is applied over whatever this says, so a despawned object can be
        ///         withdrawn without touching any authored data. That is also why nothing here needs
        ///         to survive a scene save — the overlay is deliberately session-scoped, and this
        ///         field is rebuilt from its sources every time the config is merged.
        ///     </para>
        /// </remarks>
        /// <remarks>
        ///     <para>
        ///         <b>Stored inverted, and that is load-bearing.</b> Unity rebuilds a
        ///         <see cref="SerializableAttribute" /> instance field by field without running a
        ///         constructor, so a property initializer never executes for an entry loaded from a
        ///         scene or a prefab. Written as <c>= true</c> this therefore came back <c>false</c>
        ///         on every authored target the moment it was loaded — and because the resolution
        ///         ladder skips unavailable entries at every rung, every registered place, prop and
        ///         person silently stopped resolving. The symptom is brutal to read: targetless
        ///         actions keep working perfectly while every targeted one is dropped before it
        ///         reaches a handler, so the character looks like it is ignoring half its abilities.
        ///     </para>
        ///     <para>
        ///         Inverting the stored value makes the zero-default mean available, which is the one
        ///         encoding that survives Unity's constructor-less deserialization.
        ///     </para>
        /// </remarks>
        [JsonIgnore]
        public bool Available
        {
            get => !_unavailable;
            set => _unavailable = !value;
        }

        /// <summary>Backing store for <see cref="Available" />; false (the default) means available.</summary>
        private bool _unavailable;

        public ConvaiActionCharacterDefinition Clone() =>
            new()
            {
                Name = Name,
                Bio = Bio,
                GameObjectReference = GameObjectReference,
                TextOnly = TextOnly,
                Aliases = Aliases == null ? new List<string>() : new List<string>(Aliases),
                InteractionPoint = InteractionPoint,
                Available = Available
            };
    }
}
