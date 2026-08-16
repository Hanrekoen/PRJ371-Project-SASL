using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Convai.Editor.Settings.Services
{
    /// <summary>
    ///     Reads and writes Convai feature scripting-define symbols on the active build
    ///     target group. Writes trigger a script recompilation.
    /// </summary>
    public static class ScriptingDefineToggleService
    {
        /// <summary>Build target groups considered when checking for define drift.</summary>
        private static readonly BuildTargetGroup[] DriftCheckGroups =
        {
            BuildTargetGroup.Standalone,
            BuildTargetGroup.Android,
            BuildTargetGroup.iOS,
            BuildTargetGroup.WebGL
        };

        /// <summary>The Convai feature defines exposed in the settings UI.</summary>
        public static readonly (string Symbol, string Label, string Description)[] FeatureDefines =
        {
            ("CONVAI_DEBUG_LOGGING", "Debug Logging",
                "Verbose bootstrap/runtime debug logging for SDK development and support."),
            ("CONVAI_ENABLE_SERVER_ANIMATION", "Server Animation",
                "Enables the Server Animation editor section and services."),
            ("CONVAI_ENABLE_UPDATES_SECTION", "Updates Section",
                "Shows the Updates/release-notes section in the Convai Editor window."),
            ("CONVAI_ANIMATION_RIGGING", "Animation Rigging",
                "Enables Animation Rigging integrations (requires the Animation Rigging package).")
        };

        /// <summary>The active build target group the toggles operate on.</summary>
        public static BuildTargetGroup ActiveGroup =>
            BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

        /// <summary>True when the symbol is defined for the active build target group.</summary>
        public static bool IsDefined(string symbol) => IsDefined(symbol, ActiveGroup);

        /// <summary>True when the symbol is defined for the given build target group.</summary>
        public static bool IsDefined(string symbol, BuildTargetGroup group) =>
            GetDefines(group).Contains(symbol, StringComparer.Ordinal);

        /// <summary>
        ///     Adds or removes the symbol for the active build target group.
        ///     Triggers a script recompilation when the state changes.
        /// </summary>
        public static void SetDefined(string symbol, bool defined) => SetDefined(symbol, defined, ActiveGroup);

        /// <summary>Adds or removes the symbol for the given build target group.</summary>
        public static void SetDefined(string symbol, bool defined, BuildTargetGroup group)
        {
            List<string> defines = GetDefines(group).ToList();
            bool changed = defined ? AddUnique(defines, symbol) : defines.Remove(symbol);
            if (!changed) return;

            PlayerSettings.SetScriptingDefineSymbols(
                NamedBuildTarget.FromBuildTargetGroup(group),
                defines.ToArray());
        }

        /// <summary>
        ///     Returns the build target groups (from the drift-check set) whose state for
        ///     the symbol differs from the active group.
        /// </summary>
        public static IReadOnlyList<BuildTargetGroup> GetDriftingGroups(string symbol)
        {
            BuildTargetGroup active = ActiveGroup;
            bool activeState = IsDefined(symbol, active);

            var drifting = new List<BuildTargetGroup>();
            foreach (BuildTargetGroup group in DriftCheckGroups)
            {
                if (group == active) continue;
                if (IsDefined(symbol, group) != activeState) drifting.Add(group);
            }

            return drifting;
        }

        /// <summary>Applies the active group's state for the symbol to all drift-check groups.</summary>
        public static void SyncToAllGroups(string symbol)
        {
            bool state = IsDefined(symbol);
            foreach (BuildTargetGroup group in DriftCheckGroups)
                SetDefined(symbol, state, group);
        }

        /// <summary>Removes all Convai feature defines from the active build target group in one write.</summary>
        public static void ClearFeatureDefines()
        {
            BuildTargetGroup group = ActiveGroup;
            List<string> defines = GetDefines(group).ToList();
            int removed = defines.RemoveAll(candidate =>
                FeatureDefines.Any(feature => string.Equals(feature.Symbol, candidate, StringComparison.Ordinal)));
            if (removed == 0) return;

            PlayerSettings.SetScriptingDefineSymbols(
                NamedBuildTarget.FromBuildTargetGroup(group),
                defines.ToArray());
        }

        private static string[] GetDefines(BuildTargetGroup group)
        {
            PlayerSettings.GetScriptingDefineSymbols(
                NamedBuildTarget.FromBuildTargetGroup(group), out string[] defines);
            return defines ?? Array.Empty<string>();
        }

        private static bool AddUnique(List<string> defines, string symbol)
        {
            if (defines.Contains(symbol, StringComparer.Ordinal)) return false;

            defines.Add(symbol);
            return true;
        }
    }
}
