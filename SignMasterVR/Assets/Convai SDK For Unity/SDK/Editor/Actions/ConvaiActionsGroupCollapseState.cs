using System;
using System.Collections.Generic;
using UnityEditor;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Which action groups the user has folded away, shared by every surface that draws them —
    ///     the Actions Editor window and the Convai Actions inspector.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One store, because a group folded in the window and the same group standing open in the
    ///         inspector would read as two different lists of the same character. Keyed by
    ///         <see cref="ConvaiActionGroup.Key" />, which is stable across renames and casing.
    ///     </para>
    ///     <para>
    ///         Persisted in <see cref="EditorPrefs" /> rather than session state on purpose: folding a
    ///         group away is a statement about how the user wants to work, and it should still be true
    ///         tomorrow morning.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionsGroupCollapseState
    {
        private const string PrefsKey = "Convai.ActionsEditor.CollapsedGroups";
        private const char Separator = '\n';

        private static HashSet<string> s_collapsed;

        /// <summary>Whether this group is currently folded away.</summary>
        internal static bool IsCollapsed(string key) =>
            !string.IsNullOrEmpty(key) && Load().Contains(key);

        /// <summary>Folds a group away, or opens it. Returns true when the state actually changed.</summary>
        internal static bool SetCollapsed(string key, bool collapsed)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            HashSet<string> collapsedKeys = Load();
            bool changed = collapsed ? collapsedKeys.Add(key) : collapsedKeys.Remove(key);
            if (changed)
                EditorPrefs.SetString(PrefsKey, string.Join(Separator, collapsedKeys));

            return changed;
        }

        /// <summary>Opens a group the user is about to be shown something inside of.</summary>
        internal static void Expand(string key) => SetCollapsed(key, false);

        /// <summary>Test seam: forgets the in-memory copy so the next read comes from preferences.</summary>
        internal static void Reload() => s_collapsed = null;

        private static HashSet<string> Load()
        {
            if (s_collapsed != null)
                return s_collapsed;

            s_collapsed = new HashSet<string>(StringComparer.Ordinal);
            string stored = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (stored.Length == 0)
                return s_collapsed;

            string[] keys = stored.Split(Separator);
            for (int i = 0; i < keys.Length; i++)
            {
                if (!string.IsNullOrEmpty(keys[i]))
                    s_collapsed.Add(keys[i]);
            }

            return s_collapsed;
        }
    }
}
