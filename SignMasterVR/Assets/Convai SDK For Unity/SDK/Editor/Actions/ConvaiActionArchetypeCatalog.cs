using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Runtime.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Editor.Actions
{
    /// <summary>Where a catalog action came from, used only to keep the authoring menu coherent.</summary>
    internal enum ConvaiActionArchetypeOrigin
    {
        BuiltIn = 0,
        Sample = 1,
        ProjectOrPackage = 2
    }

    /// <summary>One fully laid-out action item for the Add Action menu.</summary>
    internal sealed class ConvaiActionArchetypeMenuItem
    {
        internal ConvaiActionArchetypeMenuItem(
            ConvaiActionArchetypeCatalogEntry entry,
            string menuPath,
            string sectionHeader,
            bool startsSection)
        {
            Entry = entry;
            MenuPath = menuPath;
            SectionHeader = sectionHeader;
            StartsSection = startsSection;
        }

        internal ConvaiActionArchetypeCatalogEntry Entry { get; }
        internal string MenuPath { get; }
        internal string SectionHeader { get; }
        internal bool StartsSection { get; }
    }

    /// <summary>
    ///     One catalog-ready action archetype discovered from a shipped or third-party
    ///     <see cref="IConvaiActionExecutor" /> MonoBehaviour carrying <see cref="ConvaiActionArchetypeAttribute" />.
    /// </summary>
    internal sealed class ConvaiActionArchetypeCatalogEntry
    {
        internal ConvaiActionArchetypeCatalogEntry(Type executorType, ConvaiActionArchetypeAttribute attribute)
        {
            ExecutorType = executorType;
            DisplayName = string.IsNullOrWhiteSpace(attribute.DisplayName) ? executorType.Name : attribute.DisplayName;
            RequiredPeerHint = attribute.RequiredPeerHint;
            RequiredTargetComponent = attribute.RequiredTargetComponent;
            FeaturedOrder = attribute.FeaturedOrder;
            Family = attribute.Family;
            Origin = ResolveOrigin(executorType);
            _actionName = string.IsNullOrWhiteSpace(attribute.ActionName) ? DisplayName : attribute.ActionName;
            _description = attribute.Description ?? string.Empty;
            FeaturedDescription = string.IsNullOrWhiteSpace(attribute.FeaturedDescription)
                ? _description
                : attribute.FeaturedDescription.Trim();
            _targetRequirement = attribute.TargetRequirement;
            _parameterSpecs = attribute.Parameters ?? Array.Empty<string>();
            _parameterDescriptions = attribute.ParameterDescriptions ?? Array.Empty<string>();
            _timeoutSeconds = Mathf.Max(0f, attribute.TimeoutSeconds);
            _failurePolicyOverride = attribute.FailurePolicyOverride;
            _answerDelivery = attribute.AnswerDelivery;
        }

        /// <summary>The executor <see cref="MonoBehaviour" /> type this archetype describes.</summary>
        internal Type ExecutorType { get; }

        /// <summary>Beginner-friendly catalog display name.</summary>
        internal string DisplayName { get; }

        /// <summary>Optional required-peer-component hint (for example "NavMeshAgent"), or null/empty when none.</summary>
        internal string RequiredPeerHint { get; }

        /// <summary>
        ///     Optional simple name of a component required on the resolved target (contrast
        ///     <see cref="RequiredPeerHint" />, which is about the character), or null/empty when
        ///     none. See <see cref="ConvaiActionArchetypeAttribute.RequiredTargetComponent" />.
        /// </summary>
        internal string RequiredTargetComponent { get; }

        /// <summary>
        ///     Starter ranking declared by the behavior itself; <c>0</c> means it is not offered as a
        ///     starter. See <see cref="ConvaiActionArchetypeAttribute.FeaturedOrder" />.
        /// </summary>
        internal int FeaturedOrder { get; }

        /// <summary>
        ///     Plain-English family declared by the behavior, or null/empty when it leaves the family to
        ///     be inferred from the module it lives in. See
        ///     <see cref="ConvaiActionArchetypeAttribute.Family" />.
        /// </summary>
        internal string Family { get; }

        /// <summary>Whether this action ships with the SDK, a sample, or the consuming project.</summary>
        internal ConvaiActionArchetypeOrigin Origin { get; }

        /// <summary>Full description authored on the archetype and copied into new action definitions.</summary>
        internal string Description => _description;

        /// <summary>Compact starter-card copy, falling back to <see cref="Description" />.</summary>
        internal string FeaturedDescription { get; }

        private readonly string _actionName;
        private readonly string _description;
        private readonly ConvaiActionTargetRequirement _targetRequirement;
        private readonly string[] _parameterSpecs;
        private readonly string[] _parameterDescriptions;
        private readonly float _timeoutSeconds;
        private readonly ConvaiActionFailurePolicyOverride _failurePolicyOverride;
        private readonly ConvaiActionAnswerDelivery _answerDelivery;

        private static ConvaiActionArchetypeOrigin ResolveOrigin(Type executorType)
        {
            string assemblyName = executorType.Assembly.GetName().Name ?? string.Empty;
            if (string.Equals(assemblyName, "Convai.Runtime", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Convai.Modules.", StringComparison.Ordinal))
                return ConvaiActionArchetypeOrigin.BuiltIn;

            return assemblyName.StartsWith("Convai.Sample", StringComparison.Ordinal)
                ? ConvaiActionArchetypeOrigin.Sample
                : ConvaiActionArchetypeOrigin.ProjectOrPackage;
        }

        /// <summary>
        ///     Builds a fresh, pre-filled <see cref="ConvaiActionDefinition" /> for this archetype
        ///     (a new instance every call so callers can freely mutate the result).
        /// </summary>
        internal ConvaiActionDefinition BuildDefinition()
        {
            var definition = new ConvaiActionDefinition
            {
                ActionName = _actionName,
                Description = _description,
                TargetRequirement = _targetRequirement,
                ExecutorTypeHint = ExecutorType.Name,
                TimeoutSeconds = _timeoutSeconds,
                FailurePolicyOverride = _failurePolicyOverride,
                AnswerDelivery = _answerDelivery,
                Parameters = new List<ConvaiActionParameterDefinition>(_parameterSpecs.Length)
            };

            for (int i = 0; i < _parameterSpecs.Length; i++)
            {
                ConvaiActionParameterDefinition parameter = ParseParameterSpec(_parameterSpecs[i]);
                if (parameter != null)
                {
                    if (i < _parameterDescriptions.Length)
                        parameter.Description = _parameterDescriptions[i]?.Trim() ?? string.Empty;
                    definition.Parameters.Add(parameter);
                }
            }

            return definition;
        }

        /// <summary>
        ///     Parses one compact parameter spec (grammar documented on <see cref="ConvaiActionArchetypeAttribute" />)
        ///     into a <see cref="ConvaiActionParameterDefinition" />. Returns null for a spec that cannot
        ///     be parsed (missing name/type).
        /// </summary>
        private static ConvaiActionParameterDefinition ParseParameterSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return null;

            string[] parts = spec.Split(',');
            if (parts.Length < 2)
                return null;

            string name = parts[0].Trim();
            if (string.IsNullOrEmpty(name))
                return null;

            if (!Enum.TryParse(parts[1].Trim(), true, out ConvaiActionParameterType type))
                type = ConvaiActionParameterType.Auto;

            string connector = parts.Length >= 3 ? parts[2].Trim() : string.Empty;

            var parameter = new ConvaiActionParameterDefinition
            {
                Name = name,
                Type = type,
                Connector = connector,
                Choices = new List<string>()
            };

            if (type == ConvaiActionParameterType.Choice && parts.Length >= 4)
            {
                // Rejoin any remaining comma-split segments before splitting choices on '|',
                // in case a choice value itself contained a comma.
                string choiceSegment = string.Join(",", parts, 3, parts.Length - 3);
                string[] choiceValues = choiceSegment.Split('|');
                for (int i = 0; i < choiceValues.Length; i++)
                {
                    string choice = choiceValues[i].Trim();
                    if (!string.IsNullOrEmpty(choice))
                        parameter.Choices.Add(choice);
                }
            }

            return parameter;
        }
    }

    /// <summary>
    ///     Editor-only, domain-reload-cached scan of loaded assemblies for <see cref="IConvaiActionExecutor" />
    ///     MonoBehaviours carrying <see cref="ConvaiActionArchetypeAttribute" />. Backs the "Add Action" catalog
    ///     menu and the Action Troubleshooter's known-executor-type checks. Never scans per-OnGUI call.
    /// </summary>
    internal static class ConvaiActionArchetypeCatalog
    {
        private static List<ConvaiActionArchetypeCatalogEntry> _entries;

        /// <summary>Every discovered archetype entry, scanned once and cached for the remainder of this domain.</summary>
        internal static IReadOnlyList<ConvaiActionArchetypeCatalogEntry> Entries
        {
            get
            {
                EnsureScanned();
                return _entries;
            }
        }

        /// <summary>Forces a re-scan (editor tests / explicit refresh only; not used by production UI paths).</summary>
        internal static void Refresh()
        {
            _entries = null;

            // Families are derived from these entries, so a stale family cache would outlive the data
            // it was computed from.
            ConvaiActionBehaviorFamily.Refresh();
        }

        /// <summary>
        ///     The ready-made starters offered to a character with no actions yet, best first.
        ///     Ordered by <see cref="ConvaiActionArchetypeCatalogEntry.FeaturedOrder" />, then by
        ///     display name so the list is stable across domain reloads regardless of assembly scan
        ///     order. Empty when nothing declares itself a starter — callers must handle that rather
        ///     than assume a fixed count.
        /// </summary>
        internal static List<ConvaiActionArchetypeCatalogEntry> FeaturedEntries(int maximumCount)
        {
            EnsureScanned();

            var featured = new List<ConvaiActionArchetypeCatalogEntry>();
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].FeaturedOrder > 0)
                    featured.Add(_entries[i]);

            featured.Sort(static (left, right) => left.FeaturedOrder != right.FeaturedOrder
                ? left.FeaturedOrder.CompareTo(right.FeaturedOrder)
                : string.CompareOrdinal(left.DisplayName, right.DisplayName));

            if (maximumCount >= 0 && featured.Count > maximumCount)
                featured.RemoveRange(maximumCount, featured.Count - maximumCount);

            return featured;
        }

        /// <summary>
        ///     Builds the Add Action menu in product order: curated built-ins first, remaining shipped
        ///     actions alphabetically, then samples and project/package actions in their own submenus.
        ///     Local archetypes can therefore never push the SDK's beginner choices down at random.
        /// </summary>
        internal static List<ConvaiActionArchetypeMenuItem> BuildMenuItems()
        {
            EnsureScanned();

            var recommended = new List<ConvaiActionArchetypeCatalogEntry>();
            var readyMade = new List<ConvaiActionArchetypeCatalogEntry>();
            var samples = new List<ConvaiActionArchetypeCatalogEntry>();
            var project = new List<ConvaiActionArchetypeCatalogEntry>();

            for (int i = 0; i < _entries.Count; i++)
            {
                ConvaiActionArchetypeCatalogEntry entry = _entries[i];
                switch (entry.Origin)
                {
                    case ConvaiActionArchetypeOrigin.BuiltIn when entry.FeaturedOrder > 0:
                        recommended.Add(entry);
                        break;
                    case ConvaiActionArchetypeOrigin.BuiltIn:
                        readyMade.Add(entry);
                        break;
                    case ConvaiActionArchetypeOrigin.Sample:
                        samples.Add(entry);
                        break;
                    default:
                        project.Add(entry);
                        break;
                }
            }

            recommended.Sort(CompareFeatured);
            readyMade.Sort(CompareDisplayName);
            samples.Sort(CompareDisplayName);
            project.Sort(static (left, right) =>
            {
                int family = string.Compare(
                    ConvaiActionBehaviorFamily.Resolve(left.ExecutorType),
                    ConvaiActionBehaviorFamily.Resolve(right.ExecutorType),
                    StringComparison.OrdinalIgnoreCase);
                return family != 0 ? family : CompareDisplayName(left, right);
            });

            var result = new List<ConvaiActionArchetypeMenuItem>(_entries.Count);
            AppendTopLevelSection(
                result,
                recommended,
                ConvaiActionsEditorStrings.RecommendedActionsMenuSection);
            AppendTopLevelSection(
                result,
                readyMade,
                ConvaiActionsEditorStrings.ReadyMadeActionsMenuSection);
            AppendSubmenuSection(result, samples, ConvaiActionsEditorStrings.SampleActionsMenu, false);
            AppendSubmenuSection(result, project, ConvaiActionsEditorStrings.ProjectActionsMenu, true);
            return result;
        }

        private static void AppendTopLevelSection(
            List<ConvaiActionArchetypeMenuItem> destination,
            List<ConvaiActionArchetypeCatalogEntry> entries,
            string header)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ConvaiActionArchetypeCatalogEntry entry = entries[i];
                destination.Add(new ConvaiActionArchetypeMenuItem(
                    entry,
                    SanitizeMenuSegment(entry.DisplayName),
                    i == 0 ? header : string.Empty,
                    i == 0));
            }
        }

        private static void AppendSubmenuSection(
            List<ConvaiActionArchetypeMenuItem> destination,
            List<ConvaiActionArchetypeCatalogEntry> entries,
            string root,
            bool includeFamily)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ConvaiActionArchetypeCatalogEntry entry = entries[i];
                string family = includeFamily
                    ? $"{SanitizeMenuSegment(ConvaiActionBehaviorFamily.Resolve(entry.ExecutorType))}/"
                    : string.Empty;
                destination.Add(new ConvaiActionArchetypeMenuItem(
                    entry,
                    $"{root}/{family}{SanitizeMenuSegment(entry.DisplayName)}",
                    string.Empty,
                    i == 0));
            }
        }

        private static int CompareFeatured(
            ConvaiActionArchetypeCatalogEntry left,
            ConvaiActionArchetypeCatalogEntry right) =>
            left.FeaturedOrder != right.FeaturedOrder
                ? left.FeaturedOrder.CompareTo(right.FeaturedOrder)
                : CompareDisplayName(left, right);

        private static int CompareDisplayName(
            ConvaiActionArchetypeCatalogEntry left,
            ConvaiActionArchetypeCatalogEntry right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);

        private static string SanitizeMenuSegment(string value) =>
            string.IsNullOrWhiteSpace(value) ? ConvaiActionBehaviorFamily.ProjectFamily : value.Trim().Replace('/', '∕');

        /// <summary>Finds the catalog entry for an executor type, or null when it carries no archetype attribute.</summary>
        internal static ConvaiActionArchetypeCatalogEntry FindByExecutorType(Type executorType)
        {
            if (executorType == null)
                return null;

            EnsureScanned();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].ExecutorType == executorType)
                    return _entries[i];
            }

            return null;
        }

        /// <summary>
        ///     Resolves an authored definition's catalog entry directly: prefers its bound
        ///     <see cref="ConvaiActionDefinition.Executor" /> instance's type, falling back to its
        ///     <see cref="ConvaiActionDefinition.ExecutorTypeHint" />. Returns null when the
        ///     definition has no resolvable executor type or that type carries no archetype
        ///     attribute. The single seam the Action Troubleshooter and the Action Target inspector both use
        ///     so "which archetype does this action belong to" is answered in exactly one place.
        /// </summary>
        internal static ConvaiActionArchetypeCatalogEntry FindByDefinition(ConvaiActionDefinition definition) =>
            FindByExecutorType(TryResolveExecutorType(definition, out Type executorType) ? executorType : null);

        /// <summary>
        ///     Resolves the behavior type an authored definition runs through: its bound
        ///     <see cref="ConvaiActionDefinition.Executor" /> instance's type, or the type its
        ///     <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> names. False when the definition
        ///     has neither — the type is unknown, which is different from "known but not in the catalog".
        /// </summary>
        internal static bool TryResolveExecutorType(ConvaiActionDefinition definition, out Type executorType)
        {
            if (definition?.Executor != null)
            {
                executorType = definition.Executor.GetType();
                return true;
            }

            if (!string.IsNullOrWhiteSpace(definition?.ExecutorTypeHint) &&
                ConvaiActionExecutorBinder.TryResolveType(definition.ExecutorTypeHint, out Type hintType))
            {
                executorType = hintType;
                return true;
            }

            executorType = null;
            return false;
        }

        private static void EnsureScanned()
        {
            if (_entries != null)
                return;

            _entries = new List<ConvaiActionArchetypeCatalogEntry>();

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                if (types == null)
                    continue;

                for (int t = 0; t < types.Length; t++)
                {
                    Type candidate = types[t];
                    if (candidate == null || candidate.IsAbstract || candidate.IsInterface)
                        continue;

                    if (!typeof(MonoBehaviour).IsAssignableFrom(candidate))
                        continue;

                    if (!typeof(IConvaiActionExecutor).IsAssignableFrom(candidate))
                        continue;

                    var attribute = candidate.GetCustomAttribute<ConvaiActionArchetypeAttribute>(false);
                    if (attribute == null)
                        continue;

                    _entries.Add(new ConvaiActionArchetypeCatalogEntry(candidate, attribute));
                }
            }

            _entries.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
