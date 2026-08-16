using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Resolves <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> to a live
    ///     <see cref="IConvaiActionExecutor" /> component on a character's hierarchy. Used by
    ///     <see cref="Convai.Runtime.Components.ConvaiActionConfigSource" /> to auto-bind definitions
    ///     authored inside a <see cref="ConvaiActionSet" /> asset, which cannot hold a scene
    ///     executor reference.
    /// </summary>
    internal static class ConvaiActionExecutorBinder
    {
        // Built lazily once per domain reload; loaded assemblies/types don't change afterward.
        private static Dictionary<string, Type> _shortNameLookup;
        private static Dictionary<string, Type> _fullNameLookup;

        // Log-once guard for unresolvable ExecutorTypeHint text. Keyed by the trimmed hint itself
        // (not by action or Action Set) so the same typo authored on many actions/sets, or bound
        // repeatedly across dispatcher/backend re-syncs and MCP tooling calls, only ever warns
        // once per domain reload instead of once per action, per frame, or per binding attempt.
        private static readonly HashSet<string> _warnedUnresolvedHints = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     Test-only hook that clears the unresolved-hint log-once guard so EditMode tests can
        ///     assert the warning fires again for a hint a previous test already warned about. Never
        ///     called from shipping code.
        /// </summary>
        internal static void ResetUnresolvedHintWarningsForTests() => _warnedUnresolvedHints.Clear();

        /// <summary>
        ///     Attempts to resolve <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> to an
        ///     <see cref="IConvaiActionExecutor" /> <see cref="MonoBehaviour" /> found under
        ///     <paramref name="root" /> (including inactive children), and assigns it to
        ///     <see cref="ConvaiActionDefinition.Executor" />. No-ops when
        ///     <paramref name="definition" /> already has an explicit <see cref="ConvaiActionDefinition.Executor" />,
        ///     has no hint, or the hint cannot be resolved to a type or a component instance. When the
        ///     hint text itself cannot be resolved to any known action behavior type, logs one
        ///     actionable warning per distinct hint (see <see cref="_warnedUnresolvedHints" />).
        /// </summary>
        /// <param name="definition">The definition being bound.</param>
        /// <param name="root">The character hierarchy to search for a matching component.</param>
        /// <param name="ownerLabel">
        ///     Best-effort name of the asset that authored <paramref name="definition" /> (typically a
        ///     <see cref="ConvaiActionSet" />'s asset name), used only to make an unresolved-hint
        ///     warning actionable. Optional.
        /// </param>
        /// <returns>True when a component was bound.</returns>
        internal static bool TryBind(ConvaiActionDefinition definition, GameObject root, string ownerLabel = null)
        {
            if (definition == null || root == null)
                return false;

            if (definition.Executor != null)
                return false;

            string hint = definition.ExecutorTypeHint;
            if (string.IsNullOrWhiteSpace(hint))
                return false;

            if (!TryResolveType(hint, out Type executorType))
            {
                WarnUnresolvedHint(hint, definition.ActionName, ownerLabel);
                return false;
            }

            var component = root.GetComponentInChildren(executorType, true) as MonoBehaviour;
            if (component == null)
                return false;

            definition.Executor = component;
            return true;
        }

        /// <summary>
        ///     Logs one actionable warning, the first time only, naming an
        ///     <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> that could not be resolved to any
        ///     known action behavior type on the character — the case a typo, a rename, or an
        ///     uninstalled module would otherwise leave silently unhandled.
        /// </summary>
        private static void WarnUnresolvedHint(string hint, string actionName, string ownerLabel)
        {
            string trimmedHint = hint.Trim();
            if (!_warnedUnresolvedHints.Add(trimmedHint))
                return;

            string actionPart = string.IsNullOrWhiteSpace(actionName)
                ? "An action"
                : $"The '{actionName}' action";
            string ownerPart = string.IsNullOrWhiteSpace(ownerLabel)
                ? string.Empty
                : $" in '{ownerLabel}'";

            ConvaiLogger.Warning(
                $"[ConvaiActionExecutorBinder] {actionPart}{ownerPart} names an action behavior " +
                $"('{trimmedHint}') that Convai could not find on this character. Check the action's " +
                "behavior name for a typo, or make sure the module that provides it is installed. " +
                "This action will not run until it is fixed.",
                LogCategory.Character);
        }

        /// <summary>
        ///     Resolves an executor type hint (short or full type name) to a loaded
        ///     <see cref="IConvaiActionExecutor" /> <see cref="MonoBehaviour" /> type: exact short
        ///     name, then exact full name, then case-insensitive short name, then case-insensitive
        ///     full name.
        /// </summary>
        internal static bool TryResolveType(string hint, out Type type)
        {
            type = null;
            if (string.IsNullOrWhiteSpace(hint))
                return false;

            EnsureLookupBuilt();

            string trimmed = hint.Trim();
            if (_shortNameLookup.TryGetValue(trimmed, out type))
                return true;

            if (_fullNameLookup.TryGetValue(trimmed, out type))
                return true;

            foreach (KeyValuePair<string, Type> pair in _shortNameLookup)
            {
                if (string.Equals(pair.Key, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    type = pair.Value;
                    return true;
                }
            }

            foreach (KeyValuePair<string, Type> pair in _fullNameLookup)
            {
                if (string.Equals(pair.Key, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    type = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static void EnsureLookupBuilt()
        {
            if (_shortNameLookup != null)
                return;

            _shortNameLookup = new Dictionary<string, Type>(StringComparer.Ordinal);
            _fullNameLookup = new Dictionary<string, Type>(StringComparer.Ordinal);

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

                    if (!_shortNameLookup.ContainsKey(candidate.Name))
                        _shortNameLookup[candidate.Name] = candidate;

                    string fullName = candidate.FullName;
                    if (!string.IsNullOrEmpty(fullName) && !_fullNameLookup.ContainsKey(fullName))
                        _fullNameLookup[fullName] = candidate;
                }
            }
        }
    }
}
