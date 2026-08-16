using System.Collections.Generic;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using Convai.Shared.Compatibility;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Which characters in the open scenes actually resolve a given content asset.
    /// </summary>
    /// <remarks>
    ///     One scan, one answer. The inspector's sharing notice, the Make Unique command and the
    ///     Convai MCP tools all ask this question, and a config that reads as "shared by 4" in one
    ///     place and "shared by 3" in another would make the whole copy-before-tuning contract
    ///     untrustworthy.
    /// </remarks>
    internal static class BodyAnimationUsage
    {
        /// <summary>Every controller in the loaded scenes, inactive ones included.</summary>
        private static ConvaiBodyAnimationController[] AllControllers() =>
            ConvaiObjectFind.All<ConvaiBodyAnimationController>(FindObjectsInactive.Include);

        /// <summary>How many characters resolve <paramref name="config" />.</summary>
        internal static int CountUsing(ConvaiBodyAnimationConfig config)
        {
            if (config == null) return 0;

            ConvaiBodyAnimationController[] controllers = AllControllers();
            int count = 0;
            for (int i = 0; i < controllers.Length; i++)
                if (BodyAnimationSetupService.ResolveAssignedConfig(controllers[i]) == config)
                    count++;

            return count;
        }

        /// <summary>How many characters resolve <paramref name="set" />.</summary>
        internal static int CountUsing(ConvaiBodyAnimationSet set)
        {
            if (set == null) return 0;

            ConvaiBodyAnimationController[] controllers = AllControllers();
            int count = 0;
            for (int i = 0; i < controllers.Length; i++)
                if (BodyAnimationSetupService.ResolveAssignedSet(controllers[i]) == set)
                    count++;

            return count;
        }

        /// <summary>
        ///     The names of the other characters sharing <paramref name="config" /> — what a user
        ///     needs before deciding whether changing it is safe.
        /// </summary>
        internal static List<string> NamesOfOthersUsing(
            ConvaiBodyAnimationConfig config, ConvaiBodyAnimationController exclude)
        {
            var names = new List<string>(4);
            if (config == null) return names;

            ConvaiBodyAnimationController[] controllers = AllControllers();
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] == exclude) continue;
                if (BodyAnimationSetupService.ResolveAssignedConfig(controllers[i]) != config) continue;
                names.Add(controllers[i].gameObject.name);
            }

            return names;
        }
    }
}
