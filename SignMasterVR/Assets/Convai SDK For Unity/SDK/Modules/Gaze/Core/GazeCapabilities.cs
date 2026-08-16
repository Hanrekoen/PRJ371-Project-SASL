using System;
using System.Collections.Generic;
using Convai.Modules.Gaze.Providers;
using UnityEngine;

namespace Convai.Modules.Gaze.Core
{
    /// <summary>
    ///     Stable identifier for one optional gaze capability. Used as a dictionary key by tooling
    ///     and written into diagnostics, so the names are API: renaming one is a breaking change.
    /// </summary>
    public enum GazeCapabilityId
    {
        /// <summary>Notices when the player is looking at the character.</summary>
        PlayerAttention = 0,

        /// <summary>Publishes what the character is looking at so the backend can talk about it.</summary>
        AttentionGrounding = 1,

        /// <summary>Glances at an object when the character says its name.</summary>
        ReferentialGlances = 2,

        /// <summary>Notices what the player is looking at and looks there too.</summary>
        JointAttention = 3,

        /// <summary>Looks at other Convai characters.</summary>
        CharacterGaze = 4,

        /// <summary>Dilates the pupils with the character's arousal.</summary>
        PupilResponse = 5
    }

    /// <summary>
    ///     One optional gaze capability: what it does in plain English, and which component
    ///     provides it.
    /// </summary>
    public readonly struct GazeCapabilityInfo
    {
        internal GazeCapabilityInfo(
            GazeCapabilityId id, string displayName, string description, Type providerType, bool isPresent)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            ProviderType = providerType;
            IsPresent = isPresent;
        }

        /// <summary>Stable identifier.</summary>
        public GazeCapabilityId Id { get; }

        /// <summary>
        ///     What a user should see. Deliberately never the component's class name — a person
        ///     setting up a character should not have to learn one.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>One sentence explaining what turning it on changes.</summary>
        public string Description { get; }

        /// <summary>The <see cref="MonoBehaviour" /> type that provides this capability.</summary>
        public Type ProviderType { get; }

        /// <summary>Whether an enabled provider exists on this character right now.</summary>
        public bool IsPresent { get; }
    }

    /// <summary>
    ///     The catalogue of optional gaze capabilities, and the read that says which of them a
    ///     given character actually has.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this exists.</b> Adding <c>ConvaiGazeController</c> to a character gives it
    ///         eyes, a head, idle life, blinking, body turns and conversational rhythm with no
    ///         further setup. Six further capabilities each live behind their own small component,
    ///         and until this model existed nothing on any surface said they were there — so they
    ///         shipped dark. The components themselves are correct and deliberately opt-in; what
    ///         was missing was a way for the inspector, the editor window, the docs and a
    ///         customer's own tooling to ask "what is this character missing?" and get an answer
    ///         that is not a class name.
    ///     </para>
    ///     <para>
    ///         <b>Nothing here creates anything.</b> The only component gaze auto-provisions is the
    ///         player anchor, without which the module does nothing at all. Silently adding six
    ///         more would reintroduce exactly the hidden global state that got the old
    ///         <c>GazeRuntimeBootstrap</c> deleted, and would make "why is my character raycasting?"
    ///         unanswerable. Adding a capability is always the user's explicit act — the editor
    ///         surfaces make it one click, not one mystery.
    ///     </para>
    /// </remarks>
    public static class GazeCapabilities
    {
        private readonly struct Definition
        {
            public Definition(GazeCapabilityId id, string displayName, string description, Type providerType)
            {
                Id = id;
                DisplayName = displayName;
                Description = description;
                ProviderType = providerType;
            }

            public readonly GazeCapabilityId Id;
            public readonly string DisplayName;
            public readonly string Description;
            public readonly Type ProviderType;
        }

        /// <summary>
        ///     Authored once, in the order the setup surfaces should present them: the two with the
        ///     broadest value first, the scene-shape-dependent ones last.
        /// </summary>
        private static readonly Definition[] Definitions =
        {
            new(GazeCapabilityId.PlayerAttention,
                "Notices when you look at it",
                "The character can tell whether you are looking at it, and reacts sooner when you are.",
                typeof(PlayerAttentionSensor)),

            new(GazeCapabilityId.AttentionGrounding,
                "Can talk about what it's looking at",
                "Tells the backend which object has the character's attention, so \"it\" and \"that\" " +
                "resolve to the right thing in conversation.",
                typeof(GazeDynamicContextBridge)),

            new(GazeCapabilityId.ReferentialGlances,
                "Looks at things it mentions",
                "When the character says the name of an object in the scene, it glances at that object.",
                typeof(GazeReferentialGlances)),

            new(GazeCapabilityId.JointAttention,
                "Notices what you're looking at",
                "When you look at something for a moment, the character follows your attention and " +
                "looks there too.",
                typeof(GazeJointAttention)),

            new(GazeCapabilityId.CharacterGaze,
                "Looks at other Convai characters",
                "For scenes with more than one character: they look at whoever is speaking and " +
                "exchange glances when idle.",
                typeof(CharacterGazeTargetProvider)),

            new(GazeCapabilityId.PupilResponse,
                "Pupils widen with excitement",
                "Close-up and VR polish. Needs eye materials that expose a pupil-scale property.",
                typeof(ConvaiEyePupilDriver))
        };

        /// <summary>Number of optional capabilities in the catalogue.</summary>
        public static int Count => Definitions.Length;

        /// <summary>The plain-English name of a capability, without needing a character.</summary>
        public static string DisplayNameOf(GazeCapabilityId id)
        {
            int index = IndexOf(id);
            return index >= 0 ? Definitions[index].DisplayName : id.ToString();
        }

        /// <summary>The one-sentence description of a capability, without needing a character.</summary>
        public static string DescriptionOf(GazeCapabilityId id)
        {
            int index = IndexOf(id);
            return index >= 0 ? Definitions[index].Description : string.Empty;
        }

        /// <summary>The component type that provides a capability.</summary>
        public static Type ProviderTypeOf(GazeCapabilityId id)
        {
            int index = IndexOf(id);
            return index >= 0 ? Definitions[index].ProviderType : null;
        }

        /// <summary>
        ///     Fills <paramref name="results" /> (cleared first) with every capability and whether
        ///     <paramref name="characterRoot" /> currently has it. Inactive components count as
        ///     absent — a disabled sensor does nothing, and reporting it as present would be a lie
        ///     the setup surfaces would then repeat.
        /// </summary>
        public static void Evaluate(Transform characterRoot, List<GazeCapabilityInfo> results)
        {
            if (results == null) return;
            results.Clear();

            for (int i = 0; i < Definitions.Length; i++)
            {
                Definition definition = Definitions[i];
                results.Add(new GazeCapabilityInfo(
                    definition.Id, definition.DisplayName, definition.Description, definition.ProviderType,
                    IsPresentUnder(characterRoot, definition.ProviderType)));
            }
        }

        /// <summary>Whether an enabled provider of <paramref name="providerType" /> exists under the root.</summary>
        public static bool IsPresentUnder(Transform characterRoot, Type providerType)
        {
            if (characterRoot == null || providerType == null) return false;

            var component = characterRoot.GetComponentInChildren(providerType, true) as MonoBehaviour;
            return component != null && component.isActiveAndEnabled;
        }

        /// <summary>
        ///     A compact "attention, grounding" style list of the active capabilities, for the
        ///     one-line runtime diagnostic. Returns "none" when the character has no extras, which
        ///     is the common and perfectly valid case.
        /// </summary>
        public static string DescribeActive(Transform characterRoot)
        {
            string result = null;
            for (int i = 0; i < Definitions.Length; i++)
            {
                if (!IsPresentUnder(characterRoot, Definitions[i].ProviderType)) continue;
                result = result == null
                    ? Definitions[i].Id.ToString()
                    : result + ", " + Definitions[i].Id;
            }

            return result ?? "none";
        }

        private static int IndexOf(GazeCapabilityId id)
        {
            for (int i = 0; i < Definitions.Length; i++)
                if (Definitions[i].Id == id) return i;
            return -1;
        }
    }
}
