using System;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Declares an <see cref="IConvaiActionExecutor" /> class as a catalog-ready action
    ///     archetype: a beginner-friendly display name plus enough default authoring data (default
    ///     action name/description, target requirement, compact parameter specs, and a required-peer
    ///     hint) for editor tooling to pre-fill a <see cref="ConvaiActionDefinition" /> and bind this
    ///     executor with a single click. Purely descriptive — parsing <see cref="Parameters" /> into
    ///     real <see cref="ConvaiActionParameterDefinition" /> rows happens in editor tooling; this
    ///     attribute only carries the authoring intent alongside the executor's compiled behavior.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Parameters" /> grammar: each entry is a compact, comma-separated spec
    ///         matching one <see cref="ConvaiActionParameterDefinition" />:
    ///     </para>
    ///     <list type="bullet">
    ///         <item><description><c>"name,Type"</c> — e.g. <c>"item,Reference"</c></description></item>
    ///         <item><description><c>"name,Type,connector"</c> — e.g. <c>"container,Reference,on"</c></description></item>
    ///         <item>
    ///             <description>
    ///                 <c>"name,Choice,connector,a|b|c"</c> — choice values pipe-separated; use an
    ///                 empty connector segment (<c>"name,Choice,,a|b|c"</c>) when the parameter is
    ///                 first and takes no connector.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para><c>Type</c> matches a <see cref="ConvaiActionParameterType" /> member name.</para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ConvaiActionArchetypeAttribute : Attribute
    {
        /// <summary>Beginner-friendly display name shown in catalog/authoring UI (for example "Move To Target").</summary>
        public string DisplayName { get; }

        /// <summary>Default canonical action name pre-filled for a new definition; falls back to <see cref="DisplayName" /> when empty.</summary>
        public string ActionName { get; set; }

        /// <summary>Default description pre-filled for a new definition.</summary>
        public string Description { get; set; }

        /// <summary>
        ///     Optional compact description shown when this archetype is featured as a starter in
        ///     editor tooling. Falls back to <see cref="Description" /> when empty.
        /// </summary>
        public string FeaturedDescription { get; set; }

        /// <summary>Default target requirement pre-filled for a new definition.</summary>
        public ConvaiActionTargetRequirement TargetRequirement { get; set; }

        /// <summary>
        ///     Default parameter specs pre-filled for a new definition, using the compact grammar
        ///     documented on this type. Empty/null means the archetype authors no default parameters.
        /// </summary>
        public string[] Parameters { get; set; }

        /// <summary>
        ///     Optional descriptions aligned by index with <see cref="Parameters" />. These are sent
        ///     to Convai and should explain when and how to supply each value, not implementation details.
        /// </summary>
        public string[] ParameterDescriptions { get; set; }

        /// <summary>
        ///     Optional human-readable hint naming a required peer component/system (for example
        ///     "NavMeshAgent") that catalog tooling should check for and offer to add.
        /// </summary>
        public string RequiredPeerHint { get; set; }

        /// <summary>
        ///     Optional simple name of a component (for example "ConvaiControllableLight") that must be
        ///     present on the <em>resolved target</em> — not on the character — for this action to do
        ///     anything; editor tooling checks for it and offers to add it. Contrast with
        ///     <see cref="RequiredPeerHint" />, which names a component required on the character itself:
        ///     <see cref="RequiredPeerHint" /> is about "can the character perform this action at all",
        ///     while <see cref="RequiredTargetComponent" /> is about "does the object this action points
        ///     at actually support being acted on". An executor can declare either, both, or neither.
        /// </summary>
        public string RequiredTargetComponent { get; set; }

        /// <summary>
        ///     Where this archetype ranks among the ready-made starters offered to a character that
        ///     has no actions yet. <c>0</c> (the default) means "not a starter"; <c>1</c> is offered
        ///     first, <c>2</c> second, and so on.
        /// </summary>
        /// <remarks>
        ///     This exists so the shipped library decides what a beginner is shown first, in the same
        ///     place the behavior itself is declared. Editor tooling reads the ranking and never
        ///     names a behavior directly — which is what keeps the library reshapeable without
        ///     editor code following it around.
        /// </remarks>
        public int FeaturedOrder { get; set; }

        /// <summary>Recommended per-step timeout for a definition created from this archetype.</summary>
        public float TimeoutSeconds { get; set; }

        /// <summary>Recommended batch failure behavior for a definition created from this archetype.</summary>
        public ConvaiActionFailurePolicyOverride FailurePolicyOverride { get; set; }

        /// <summary>Recommended answer delivery for a definition created from this archetype.</summary>
        public ConvaiActionAnswerDelivery AnswerDelivery { get; set; }

        /// <summary>
        ///     Optional plain-English family this behavior belongs to (for example "Movement" or
        ///     "Attention"), shown when the Actions Editor groups the list by behavior and used as a
        ///     proposed category name for a character whose actions are not filed yet.
        /// </summary>
        /// <remarks>
        ///     Declared on the behavior for the same reason <see cref="FeaturedOrder" /> is: the library
        ///     — including a third party's — decides how its own behaviors are named and shelved, and
        ///     editor code never carries that list around. When this is left empty the family falls back
        ///     to the Convai module the behavior lives in, which is a fact rather than an opinion.
        /// </remarks>
        public string Family { get; set; }

        /// <summary>Declares an action archetype with the given catalog display name.</summary>
        public ConvaiActionArchetypeAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
