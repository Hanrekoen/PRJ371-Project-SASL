using System;
using System.Collections.Generic;
using Convai.RestAPI.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.RestAPI
{

    [Serializable]
    public class CharacterDetails
    {
        public CharacterDetails(MemorySettings memorySettings, string characterName, string userID, string characterID, string listing, List<string> languageCodes, string voiceType, List<string> characterActions, List<string> characterEmotions, ModelDetailsData modelDetails, string languageCode, GuardrailMetaData guardrailMeta, CharacterTraitsData characterTraits, string timestamp, string organizationId, string startNarrativeSectionId, object pronunciations, List<string> boostedWords, List<string> allowedModerationFilters, string uncensoredAccessConsent, string nsfwModelSize, string temperature, string backstory)
        {
            MemorySettings = memorySettings;
            CharacterName = characterName;
            UserID = userID;
            CharacterID = characterID;
            Listing = listing;
            LanguageCodes = languageCodes;
            VoiceType = voiceType;
            CharacterActions = characterActions;
            CharacterEmotions = characterEmotions;
            ModelDetails = modelDetails;
            LanguageCode = languageCode;
            GuardrailMeta = guardrailMeta;
            CharacterTraits = characterTraits;
            Timestamp = timestamp;
            OrganizationId = organizationId;
            StartNarrativeSectionId = startNarrativeSectionId;
            Pronunciations = pronunciations;
            BoostedWords = boostedWords;
            AllowedModerationFilters = allowedModerationFilters;
            UncensoredAccessConsent = uncensoredAccessConsent;
            NsfwModelSize = nsfwModelSize;
            Temperature = temperature;
            Backstory = backstory;
        }

        public static CharacterDetails Default()
        {
            return new CharacterDetails(
                MemorySettings.Default(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                new List<string>(),
                string.Empty,
                new List<string>(),
                new List<string>(),
                new ModelDetailsData(string.Empty, string.Empty, string.Empty),
                string.Empty,
                new GuardrailMetaData(0, new List<string>()),
                new CharacterTraitsData(new List<string>(), string.Empty, new PersonalityTraits(0, 0, 0, 0, 0)),
                string.Empty,
                string.Empty,
                string.Empty,
                new List<string>(),
                new List<string>(),
                new List<string>(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        [JsonProperty("memory_settings")] public MemorySettings MemorySettings { get; private set; }
        [JsonProperty("character_name")] public string CharacterName { get; private set; }
        [JsonProperty("user_id")] public string UserID { get; private set; }
        [JsonProperty("character_id")] public string CharacterID { get; private set; }
        [JsonProperty("listing")] public string Listing { get; private set; }
        [JsonProperty("language_codes")] public List<string> LanguageCodes { get; private set; }
        [JsonProperty("voice_type")] public string VoiceType { get; private set; }
        [JsonProperty("character_actions")] public List<string> CharacterActions { get; private set; }
        [JsonProperty("character_emotions")] public List<string> CharacterEmotions { get; private set; }
        [JsonProperty("model_details")] public ModelDetailsData ModelDetails { get; private set; }
        [JsonProperty("language_code")] public string LanguageCode { get; private set; }
        [JsonProperty("guardrail_meta")] public GuardrailMetaData GuardrailMeta { get; private set; }
        [JsonProperty("character_traits")] public CharacterTraitsData CharacterTraits { get; private set; }
        [JsonProperty("timestamp")] public string Timestamp { get; private set; }
        [JsonProperty("verbosity")] public int Verbosity { get; private set; }
        [JsonProperty("organization_id")] public string OrganizationId { get; private set; }

        [JsonProperty("is_narrative_driven")] public bool IsNarrativeDriven { get; private set; }

        [JsonProperty("start_narrative_section_id")]
        public string StartNarrativeSectionId { get; private set; }

        [JsonProperty("moderation_enabled")] public bool ModerationEnabled { get; private set; }
        [JsonProperty("pronunciations")] public object Pronunciations { get; private set; }
        [JsonProperty("boosted_words")] public List<string> BoostedWords { get; private set; }

        [JsonProperty("allowed_moderation_filters")]
        public List<string> AllowedModerationFilters { get; private set; }

        [JsonProperty("uncensored_access_consent")]
        public string UncensoredAccessConsent { get; private set; }

        [JsonProperty("nsfw_model_size")] public string NsfwModelSize { get; private set; }
        [JsonProperty("temperature")] public string Temperature { get; private set; }
        [JsonProperty("backstory")] public string Backstory { get; private set; }

        [JsonProperty("edit_character_access")]
        public bool EditCharacterAccess { get; private set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; private set; } = new Dictionary<string, JToken>();

        [Serializable]
        public class ModelDetailsData
        {
            public ModelDetailsData(string modelType, string modelLink, string modelPlaceholder)
            {
                ModelType = modelType;
                ModelLink = modelLink;
                ModelPlaceholder = modelPlaceholder;
            }

            [JsonProperty("modelType")] public string ModelType { get; private set; }
            [JsonProperty("modelLink")] public string ModelLink { get; private set; }
            [JsonProperty("modelPlaceholder")] public string ModelPlaceholder { get; private set; }
        }

        [Serializable]
        public class GuardrailMetaData
        {
            public GuardrailMetaData(int limitResponseLevel, List<string> blockedWords)
            {
                LimitResponseLevel = limitResponseLevel;
                BlockedWords = blockedWords;
            }

            [JsonProperty("limitResponseLevel")] public int LimitResponseLevel { get; private set; }
            [JsonProperty("blockedWords")] public List<string> BlockedWords { get; private set; }
        }

        [Serializable]
        public class CharacterTraitsData
        {
            public CharacterTraitsData(List<string> catchPhrases, string speakingStyle, PersonalityTraits personalityTraits)
            {
                CatchPhrases = catchPhrases;
                SpeakingStyle = speakingStyle;
                PersonalityTraits = personalityTraits;
            }

            [JsonProperty("catch_phrases")] public List<string> CatchPhrases { get; private set; }
            [JsonProperty("speaking_style")] public string SpeakingStyle { get; private set; }
            [JsonProperty("personality_traits")] public PersonalityTraits PersonalityTraits { get; private set; }
        }

        [Serializable]
        public class PersonalityTraits
        {
            public PersonalityTraits(int openness, int sensitivity, int extraversion, int agreeableness, int meticulousness)
            {
                Openness = openness;
                Sensitivity = sensitivity;
                Extraversion = extraversion;
                Agreeableness = agreeableness;
                Meticulousness = meticulousness;
            }

            [JsonProperty("openness")] public int Openness { get; private set; }
            [JsonProperty("sensitivity")] public int Sensitivity { get; private set; }
            [JsonProperty("extraversion")] public int Extraversion { get; private set; }
            [JsonProperty("agreeableness")] public int Agreeableness { get; private set; }
            [JsonProperty("meticulousness")] public int Meticulousness { get; private set; }
        }
    }

}
