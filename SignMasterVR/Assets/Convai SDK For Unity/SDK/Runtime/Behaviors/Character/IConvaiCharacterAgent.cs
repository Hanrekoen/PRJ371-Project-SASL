using System.Collections.Generic;
using Convai.Domain.Emotion;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.NarrativeDesign;
using UnityEngine;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Unity-facing abstraction exposed to character behaviours for interacting with a character instance.
    /// </summary>
    public interface IConvaiCharacterAgent
    {
        /// <summary>
        ///     Unique identifier for the Character.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     Display name for the Character.
        /// </summary>
        public string CharacterName { get; }

        /// <summary>
        ///     Gets the name tag color for transcript display.
        /// </summary>
        public Color NameTagColor { get; }

        /// <summary>
        ///     Gets whether session resume is enabled for this character.
        /// </summary>
        public bool EnableSessionResume { get; }

        /// <summary>
        ///     Gets the initial dynamic info text to include in room connection requests.
        /// </summary>
        public string InitialDynamicInfoText { get; }

        /// <summary>
        ///     Gets whether initial dynamic info should be kept in server context.
        /// </summary>
        public bool InitialDynamicInfoKeepInContext { get; }

        /// <summary>
        ///     Character-owned dynamic context surface for tracked runtime state and events.
        /// </summary>
        public IConvaiDynamicContext DynamicContext { get; }

        /// <summary>
        ///     Effective emotion detection mode used to build the room-connect emotion_config.
        /// </summary>
        public EmotionDetectionMode EmotionDetectionMode { get; }

        /// <summary>Invokes a saved Narrative Design trigger through the character.</summary>
        public void SendTrigger(string triggerName);

        /// <summary>Sends inline narrative event context through the character.</summary>
        public void SendNarrativeEvent(string eventMessage);

        /// <summary>Sends exact scripted narrative speech through the character.</summary>
        public void SendNarrativeSpeech(string speechText);

        /// <summary>
        ///     Updates template keys for narrative design placeholder resolution.
        ///     Template keys like {PlayerName} in objectives will be replaced with the corresponding value.
        /// </summary>
        /// <param name="templateKeys">Dictionary of key-value pairs to update.</param>
        public void UpdateTemplateKeys(Dictionary<string, string> templateKeys);
    }
}
