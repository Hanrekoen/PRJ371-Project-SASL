namespace Convai.Domain.Narrative
{
    /// <summary>
    ///     Key-value pair for dynamic template substitution in narrative content.
    ///     Template keys allow dynamic placeholder resolution in objectives (e.g., {PlayerName}, {TimeOfDay}).
    /// </summary>
    public class NarrativeTemplateKey
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="NarrativeTemplateKey" /> class.
        /// </summary>
        public NarrativeTemplateKey()
        {
            Key = string.Empty;
            Value = string.Empty;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="NarrativeTemplateKey" /> class.
        /// </summary>
        /// <param name="key">Template key name.</param>
        /// <param name="value">Value to substitute.</param>
        public NarrativeTemplateKey(string key, string value)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }

        /// <summary>Template key name (e.g., "PlayerName", "TimeOfDay").</summary>
        public string Key { get; set; }

        /// <summary>Value to substitute for this key.</summary>
        public string Value { get; set; }
    }
}
