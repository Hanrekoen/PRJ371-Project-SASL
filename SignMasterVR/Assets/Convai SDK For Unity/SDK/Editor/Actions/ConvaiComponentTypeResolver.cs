using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Editor-only, domain-reload-cached short-name -&gt; <see cref="Type" /> lookup for any
    ///     loaded <see cref="Component" /> type. This is the single component-type lookup behind
    ///     every <c>RequiredPeerHint</c> / <c>RequiredTargetComponent</c> resolution in the Actions
    ///     editor tooling — the executor inspector, the Action Troubleshooter, and the Action Target
    ///     inspector all call <see cref="Resolve" /> instead of each keeping their own reflection
    ///     scan. Contrast <c>ConvaiActionExecutorBinder</c>, which resolves only
    ///     <c>IConvaiActionExecutor</c> types from a definition's <c>ExecutorTypeHint</c>.
    /// </summary>
    internal static class ConvaiComponentTypeResolver
    {
        private static Dictionary<string, Type> _lookup;

        /// <summary>
        ///     Resolves a component's simple type name (for example "ConvaiControllableLight") to a
        ///     loaded <see cref="Component" /> type: exact match first, then case-insensitive.
        ///     Returns null when no loaded component type has that name — the hint is then treated
        ///     as free text with nothing to validate.
        /// </summary>
        internal static Type Resolve(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
                return null;

            EnsureBuilt();

            if (_lookup.TryGetValue(shortName, out Type exact))
                return exact;

            foreach (KeyValuePair<string, Type> pair in _lookup)
            {
                if (string.Equals(pair.Key, shortName, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }

            return null;
        }

        /// <summary>
        ///     Product-facing display name for a component type: its nicified type name with the
        ///     redundant "Convai" brand prefix removed, so <c>ConvaiControllableLight</c> reads as
        ///     "Controllable Light". Inside a Convai window the prefix carries no information and
        ///     reads as a C# class name to a user. The runtime failure messages use exactly this
        ///     wording, so editor and runtime must stay in agreement — change both or neither.
        /// </summary>
        internal static string DisplayName(Type componentType)
        {
            if (componentType == null)
                return string.Empty;

            string name = componentType.Name;

            // Only strip when something meaningful remains: a type named exactly "Convai", or one
            // whose remainder does not start a new word, keeps its full name.
            const string brandPrefix = "Convai";
            if (name.Length > brandPrefix.Length &&
                name.StartsWith(brandPrefix, StringComparison.Ordinal) &&
                char.IsUpper(name[brandPrefix.Length]))
            {
                name = name.Substring(brandPrefix.Length);
            }

            return ObjectNames.NicifyVariableName(name);
        }

        private static void EnsureBuilt()
        {
            if (_lookup != null)
                return;

            _lookup = new Dictionary<string, Type>(StringComparer.Ordinal);

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
                    if (candidate == null || candidate.IsAbstract || candidate.IsGenericTypeDefinition)
                        continue;

                    if (!typeof(Component).IsAssignableFrom(candidate))
                        continue;

                    if (!_lookup.ContainsKey(candidate.Name))
                        _lookup[candidate.Name] = candidate;
                }
            }
        }
    }
}
