using System;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Declares a component as one of the character's embodiment features, so the editor can
    ///     list it, name it, and route a profile to it without anyone maintaining a second list.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Put this on a <see cref="ConvaiCharacterModule{TProfile}" /> subclass. The profile
    ///         type is <b>not</b> an argument — it is read from the generic base, so it cannot drift
    ///         from the type the component actually accepts. The attribute carries only what the base
    ///         cannot express: the stable routing id and the label a user reads.
    ///     </para>
    ///     <example>
    ///         <code>
    /// [EmbodimentModule(ModuleIds.Gaze, "Gaze")]
    /// public sealed class ConvaiGazeController : ConvaiCharacterModule&lt;ConvaiGazeProfile&gt; { }
    ///         </code>
    ///     </example>
    ///     <para>
    ///         Module identity has exactly one source of truth: this attribute on the module class.
    ///         Anything that also needs "which modules exist" — the <c>ModuleIds</c> constants, editor
    ///         maps, architecture-test tables — reads it here rather than maintaining its own list,
    ///         because a second list can drift, and drift is visible to customers: a preset slot can
    ///         be flagged as an unrecognized module simply because a hand-written map is missing an
    ///         entry.
    ///     </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EmbodimentModuleAttribute : Attribute
    {
        /// <param name="moduleId">
        ///     The stable routing key, from <c>ModuleIds</c>. This is serialized inside preset assets,
        ///     so it must never change once shipped.
        /// </param>
        /// <param name="displayName">
        ///     What the user reads — "Gaze", not "convai.gaze". Plain English, no jargon.
        /// </param>
        public EmbodimentModuleAttribute(string moduleId, string displayName)
        {
            ModuleId = moduleId;
            DisplayName = displayName;
        }

        /// <summary>Stable routing key serialized into preset assets.</summary>
        public string ModuleId { get; }

        /// <summary>Plain-English label shown in menus, dropdowns and inspectors.</summary>
        public string DisplayName { get; }

        /// <summary>
        ///     One sentence on what the feature does, shown as help text next to the module in the
        ///     preset editor and the character map.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        ///     What the character loses without this feature, phrased to complete the sentence
        ///     "Without it, " — for example <c>"the character never looks at anything"</c>.
        /// </summary>
        /// <remarks>
        ///     Must be authored as its own sentence, never derived from <see cref="Description" />.
        ///     Deriving it algorithmically — for example lower-casing the description and prefixing
        ///     "Without it: no" — produces wrong text like "Without it: no where the character looks":
        ///     a description answers "what is this?" and cannot be bent into an answer to "what do I
        ///     lose?". Leave unset and the map simply shows nothing.
        /// </remarks>
        public string Absence { get; set; }

        /// <summary>
        ///     Sort position in user-facing lists. Lower first; ties fall back to display name.
        /// </summary>
        public int Order { get; set; }
    }
}
