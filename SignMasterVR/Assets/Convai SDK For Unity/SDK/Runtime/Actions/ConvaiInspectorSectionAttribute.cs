using System;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Groups a serialized field into a named section of the Convai inspector.
    ///     Any component rendered by the Convai inspector framework (every
    ///     <see cref="ConvaiActionExecutorBase" />-derived action behavior, shipped or custom) can
    ///     annotate its fields with this attribute; fields without it land in a default
    ///     "Settings" section, so the attribute is purely additive polish.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Sections are ordered by the smallest <see cref="Order" /> among their fields
    ///         (declaration order breaks ties), and fields inside a section are ordered by
    ///         <see cref="Order" /> then declaration order. A section whose fields are all marked
    ///         <see cref="Advanced" /> renders inside a collapsed "Advanced" foldout by default.
    ///     </para>
    ///     <para>
    ///         Purely descriptive — the attribute has no runtime behavior and adds no runtime
    ///         cost; it is read by editor tooling only.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    ///     [SerializeField]
    ///     [Tooltip("Seconds to hold the pose before releasing.")]
    ///     [ConvaiInspectorSection("Timing", 1)]
    ///     private float _holdSeconds = 2f;
    ///     </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ConvaiInspectorSectionAttribute : Attribute
    {
        /// <summary>Display name of the section this field belongs to (for example "Timing").</summary>
        public string Section { get; }

        /// <summary>
        ///     Sort key inside the section (lower renders first); the section itself sorts by the
        ///     smallest order among its fields. Fields with equal order keep declaration order.
        /// </summary>
        public int Order { get; }

        /// <summary>
        ///     When true, marks this field as advanced. A section whose fields are all advanced
        ///     renders inside a collapsed foldout so beginners see only the essentials first.
        /// </summary>
        public bool Advanced { get; set; }

        /// <summary>Assigns the field to <paramref name="section" /> with an optional in-section sort key.</summary>
        public ConvaiInspectorSectionAttribute(string section, int order = 0)
        {
            Section = section;
            Order = order;
        }
    }
}
