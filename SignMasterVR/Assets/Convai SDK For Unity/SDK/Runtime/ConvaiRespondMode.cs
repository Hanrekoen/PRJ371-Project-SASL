namespace Convai.Runtime
{
    /// <summary>
    ///     How an input lane (dynamic-context updates, vision frames, triggers, scene metadata)
    ///     should affect the character's speech. Maps to the backend <c>run_llm</c> policy and is
    ///     the single respond-mode vocabulary across dynamic context and dynamic vision.
    /// </summary>
    public enum ConvaiRespondMode
    {
        /// <summary>Absorb into awareness; never speak (<c>run_llm = false</c>).</summary>
        Silent = 0,

        /// <summary>Let the model decide whether to speak; it may abstain (<c>run_llm = auto</c>).</summary>
        Auto = 1,

        /// <summary>Always produce speech (<c>run_llm = true</c>).</summary>
        MustRespond = 2
    }

    /// <summary>Conversions between <see cref="ConvaiRespondMode" /> and its backend wire strings.</summary>
    public static class ConvaiRespondModeExtensions
    {
        /// <summary>Maps a respond mode to the exact backend wire string.</summary>
        public static string ToWireString(this ConvaiRespondMode mode) =>
            mode switch
            {
                ConvaiRespondMode.Auto => "auto",
                ConvaiRespondMode.MustRespond => "must_respond",
                _ => "silent"
            };

        /// <summary>
        ///     Parses a backend wire string (<c>silent</c>, <c>auto</c>, <c>must_respond</c>; whitespace
        ///     and case tolerant) back into a respond mode. Returns false for anything else.
        /// </summary>
        public static bool TryParseWireString(string value, out ConvaiRespondMode mode)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "silent":
                    mode = ConvaiRespondMode.Silent;
                    return true;
                case "auto":
                    mode = ConvaiRespondMode.Auto;
                    return true;
                case "must_respond":
                    mode = ConvaiRespondMode.MustRespond;
                    return true;
                default:
                    mode = ConvaiRespondMode.Silent;
                    return false;
            }
        }
    }
}
