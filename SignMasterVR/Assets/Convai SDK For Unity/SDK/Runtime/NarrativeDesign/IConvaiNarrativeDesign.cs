using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.Narrative;

namespace Convai.Runtime.NarrativeDesign
{
    /// <summary>
    ///     Character-scoped runtime surface for Convai Narrative Design.
    /// </summary>
    public interface IConvaiNarrativeDesign
    {
        /// <summary>Template keys currently tracked for placeholder replacement.</summary>
        public IReadOnlyDictionary<string, string> TemplateKeys { get; }

        /// <summary>Current narrative section ID received from the server.</summary>
        public string CurrentSectionId { get; }

        /// <summary>Current narrative section data received from the server.</summary>
        public NarrativeSectionData CurrentSectionData { get; }

        /// <summary>Raised when the current section ID changes. Parameters: previous section ID, new section ID.</summary>
        public event Action<string, string> OnSectionChanged;

        /// <summary>Raised whenever section data is received for this character.</summary>
        public event Action<NarrativeSectionData> OnSectionDataReceived;

        /// <summary>Raised after a trigger/speech request is accepted locally.</summary>
        public event Action<ConvaiNarrativeTriggerInvocation> OnTriggerInvoked;

        /// <summary>Updates or adds one template key. Sends immediately when connected, otherwise replays on ready.</summary>
        public bool SetTemplateKey(string key, string value);

        /// <summary>Updates or adds multiple template keys. Sends immediately when connected, otherwise replays on ready.</summary>
        public bool SetTemplateKeys(IReadOnlyDictionary<string, string> keys);

        /// <summary>Invokes a saved Narrative Design trigger by name. Queues until character ready if called early.</summary>
        public bool InvokeTrigger(string triggerName);

        /// <summary>Sends inline event context and lets Convai respond naturally.</summary>
        public bool InvokeEvent(string eventMessage);

        /// <summary>Sends exact scripted speech. The SDK wraps the text for direct speech internally.</summary>
        public bool InvokeSpeech(string speechText);

        /// <summary>Fetches available narrative sections for the character from REST.</summary>
        public Task<NarrativeFetchResult<List<NarrativeSectionInfo>>> FetchSectionsAsync(string apiKey = null);

        /// <summary>Fetches available narrative triggers for the character from REST.</summary>
        public Task<NarrativeFetchResult<List<NarrativeTriggerInfo>>> FetchTriggersAsync(string apiKey = null);
    }
}
