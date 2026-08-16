using System;
using System.Collections.Generic;
using System.Text;
using Convai.Runtime.Actions;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Answers "what kind of action is this?" in plain English — the behavior family shown when the
    ///     Actions Editor groups by behavior, and the starting point the cold-start category suggestion
    ///     proposes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two sources, in order. A behavior may declare its own family through
    ///         <see cref="ConvaiActionArchetypeAttribute.Family" /> — the library, including a third
    ///         party's, names its own shelves. When it does not, the family is the Convai module the
    ///         behavior lives in, which is a fact read off the assembly rather than a taxonomy invented
    ///         in editor code.
    ///     </para>
    ///     <para>
    ///         Results are cached per type: this is called once per row per grouped rebuild, and type
    ///         attribute lookups are the kind of reflection that is cheap once and expensive every frame.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionBehaviorFamily
    {
        /// <summary>Family for a behavior that ships in the SDK core rather than in a Convai module.</summary>
        internal const string GeneralFamily = "General";

        /// <summary>Family for a behavior that belongs to neither — a project's or an asset's own.</summary>
        internal const string ProjectFamily = "This Project";

        private const string CoreAssembly = "Convai.Runtime";
        private const string ModuleAssemblyPrefix = "Convai.Modules.";

        private static readonly Dictionary<Type, string> FamilyByType = new();

        /// <summary>
        ///     The family of the behavior this definition runs through, or <see cref="string.Empty" />
        ///     when it has no behavior yet — an action still waiting to be wired up is not a family, it
        ///     is an unfinished action, and the list says so with its own bucket.
        /// </summary>
        internal static string Resolve(ConvaiActionDefinition definition) =>
            ConvaiActionArchetypeCatalog.TryResolveExecutorType(definition, out Type executorType)
                ? Resolve(executorType)
                : string.Empty;

        /// <summary>The family of one behavior type. Cached; safe to call per row per draw pass.</summary>
        internal static string Resolve(Type executorType)
        {
            if (executorType == null)
                return string.Empty;

            if (FamilyByType.TryGetValue(executorType, out string cached))
                return cached;

            string family = Compute(executorType);
            FamilyByType[executorType] = family;
            return family;
        }

        /// <summary>
        ///     Drops the cache. Paired with <see cref="ConvaiActionArchetypeCatalog.Refresh" />, so a
        ///     recompile that changes a behavior's declared family is reflected without restarting.
        /// </summary>
        internal static void Refresh() => FamilyByType.Clear();

        private static string Compute(Type executorType)
        {
            ConvaiActionArchetypeCatalogEntry entry = ConvaiActionArchetypeCatalog.FindByExecutorType(executorType);
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Family))
                return entry.Family.Trim();

            string assembly = executorType.Assembly.GetName().Name ?? string.Empty;
            if (string.Equals(assembly, CoreAssembly, StringComparison.Ordinal))
                return GeneralFamily;

            if (!assembly.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal))
                return ProjectFamily;

            string module = assembly.Substring(ModuleAssemblyPrefix.Length);
            int nestedSeparator = module.IndexOf('.');
            if (nestedSeparator > 0)
                module = module.Substring(0, nestedSeparator);

            return SplitWords(module);
        }

        /// <summary>
        ///     Turns an assembly's module segment into the product's own words: <c>BodyAnimation</c>
        ///     reads as "Body Animation", the same way it is written everywhere a user can see it.
        /// </summary>
        private static string SplitWords(string value)
        {
            if (string.IsNullOrEmpty(value))
                return ProjectFamily;

            var builder = new StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (i > 0 && char.IsUpper(character) && !char.IsUpper(value[i - 1]))
                    builder.Append(' ');

                builder.Append(character);
            }

            return builder.ToString();
        }
    }
}
