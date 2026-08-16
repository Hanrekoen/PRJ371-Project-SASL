using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;

namespace Convai.Editor.Inspectors.Framework
{
    /// <summary>
    ///     Per-field inspector-section metadata fed to <see cref="ConvaiInspectorSectionLayout.Build" /> —
    ///     one entry per top-level serialized field, in declaration order, carrying what its
    ///     <see cref="ConvaiInspectorSectionAttribute" /> declared (or defaults when unattributed).
    /// </summary>
    internal readonly struct ConvaiInspectorFieldMetadata
    {
        /// <summary>Serialized field name (the top-level property path).</summary>
        internal string FieldName { get; }

        /// <summary>Declared section name; null/empty routes the field to the default "Settings" section.</summary>
        internal string Section { get; }

        /// <summary>In-section sort key; lower renders first, ties keep declaration order.</summary>
        internal int Order { get; }

        /// <summary>Whether the field was declared advanced.</summary>
        internal bool Advanced { get; }

        internal ConvaiInspectorFieldMetadata(string fieldName, string section, int order, bool advanced)
        {
            FieldName = fieldName;
            Section = section;
            Order = order;
            Advanced = advanced;
        }
    }

    /// <summary>One resolved section: display name, advanced flag, and its ordered field names.</summary>
    internal sealed class ConvaiInspectorSectionModel
    {
        /// <summary>Section display name.</summary>
        internal string Name { get; }

        /// <summary>
        ///     True when every field in the section is marked advanced — the section then renders
        ///     inside a collapsed foldout. A mixed section stays visible so a field an author left
        ///     unmarked is never hidden.
        /// </summary>
        internal bool Advanced { get; }

        /// <summary>Field names in final render order.</summary>
        internal IReadOnlyList<string> FieldNames => _fieldNames;

        private readonly List<string> _fieldNames;

        internal ConvaiInspectorSectionModel(string name, bool advanced, List<string> fieldNames)
        {
            Name = name;
            Advanced = advanced;
            _fieldNames = fieldNames;
        }
    }

    /// <summary>
    ///     Pure section-model builder for the Convai inspector framework (no GUI, unit-testable):
    ///     groups field metadata into ordered sections. Rules: unattributed fields land in the
    ///     default <see cref="DefaultSectionName" /> section; fields inside a section order by
    ///     (order, declaration index); sections order by (all-advanced last, smallest field order,
    ///     first declaration index); a section is advanced only when every one of its fields is.
    /// </summary>
    internal static class ConvaiInspectorSectionLayout
    {
        /// <summary>Section that collects fields without a <see cref="ConvaiInspectorSectionAttribute" />.</summary>
        internal const string DefaultSectionName = "Settings";

        private sealed class SectionAccumulator
        {
            internal string Name;
            internal int MinOrder = int.MaxValue;
            internal int FirstDeclarationIndex = int.MaxValue;
            internal bool AllAdvanced = true;
            internal readonly List<(int Order, int DeclarationIndex, string FieldName)> Fields = new();
        }

        /// <summary>Builds the ordered section models for one component's field metadata.</summary>
        internal static List<ConvaiInspectorSectionModel> Build(IReadOnlyList<ConvaiInspectorFieldMetadata> fields)
        {
            var byName = new Dictionary<string, SectionAccumulator>(StringComparer.Ordinal);
            var accumulators = new List<SectionAccumulator>();

            for (int i = 0; fields != null && i < fields.Count; i++)
            {
                ConvaiInspectorFieldMetadata field = fields[i];
                if (string.IsNullOrWhiteSpace(field.FieldName))
                    continue;

                string sectionName = string.IsNullOrWhiteSpace(field.Section)
                    ? DefaultSectionName
                    : field.Section.Trim();

                if (!byName.TryGetValue(sectionName, out SectionAccumulator accumulator))
                {
                    accumulator = new SectionAccumulator { Name = sectionName };
                    byName[sectionName] = accumulator;
                    accumulators.Add(accumulator);
                }

                accumulator.MinOrder = Math.Min(accumulator.MinOrder, field.Order);
                accumulator.FirstDeclarationIndex = Math.Min(accumulator.FirstDeclarationIndex, i);
                accumulator.AllAdvanced &= field.Advanced;
                accumulator.Fields.Add((field.Order, i, field.FieldName));
            }

            accumulators.Sort((a, b) =>
            {
                int byAdvanced = a.AllAdvanced.CompareTo(b.AllAdvanced);
                if (byAdvanced != 0)
                    return byAdvanced;

                int byOrder = a.MinOrder.CompareTo(b.MinOrder);
                return byOrder != 0 ? byOrder : a.FirstDeclarationIndex.CompareTo(b.FirstDeclarationIndex);
            });

            var models = new List<ConvaiInspectorSectionModel>(accumulators.Count);
            for (int s = 0; s < accumulators.Count; s++)
            {
                SectionAccumulator accumulator = accumulators[s];
                accumulator.Fields.Sort((a, b) =>
                {
                    int byOrder = a.Order.CompareTo(b.Order);
                    return byOrder != 0 ? byOrder : a.DeclarationIndex.CompareTo(b.DeclarationIndex);
                });

                var fieldNames = new List<string>(accumulator.Fields.Count);
                for (int f = 0; f < accumulator.Fields.Count; f++)
                    fieldNames.Add(accumulator.Fields[f].FieldName);

                models.Add(new ConvaiInspectorSectionModel(accumulator.Name, accumulator.AllAdvanced, fieldNames));
            }

            return models;
        }
    }
}
