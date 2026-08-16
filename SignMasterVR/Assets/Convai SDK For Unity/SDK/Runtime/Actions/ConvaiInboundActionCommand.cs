using Newtonsoft.Json;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     The shape an action command actually arrives in: a name, and optionally a target.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why a separate type.</b> Commands used to be deserialized straight onto
    ///         <c>ConvaiActionCommand</c>, which is the type the whole pipeline carries and the type
    ///         customers hold. Every public property on it was therefore settable from the wire —
    ///         including <c>Enriched</c>, which decides whether the SDK parses the command at all,
    ///         and <c>Parameters</c>, which is what parsing produces. A payload that set them would
    ///         have walked past the reader with values nothing checked.
    ///     </para>
    ///     <para>
    ///         The obvious fix — annotating <c>ConvaiActionCommand</c> with <c>[JsonIgnore]</c> —
    ///         cannot be done and should not be wanted. That type lives in the engine-free domain
    ///         assembly, which declares no references at all; giving it a serializer dependency to
    ///         solve a protocol problem is a worse coupling than the one being removed. It would also
    ///         change what <c>JsonConvert.SerializeObject</c> produces for anyone using that type for
    ///         saves or tooling.
    ///     </para>
    ///     <para>
    ///         So the wire gets its own type, here, in the assembly that talks to the wire. It is
    ///         opt-in: only the two properties below are read, and a payload carrying anything else
    ///         has nowhere to put it.
    ///     </para>
    ///     <para>
    ///         Two fields is not a guess — it is what the backend emits. Its action-response model
    ///         serializes exactly <c>name</c> and <c>target</c>, and drops <c>target</c> when null.
    ///     </para>
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class ConvaiInboundActionCommand
    {
        /// <summary>Action the Convai Character chose. Required; a command without one is malformed.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>What it should act on, when the backend named one separately.</summary>
        [JsonProperty("target")]
        public string Target { get; set; }
    }
}
