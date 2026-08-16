using System;
using System.Reflection;
using Convai.Runtime.Animation;
using UnityEngine;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Reflection-based bootstrap that integrates <see cref="EmbodimentContext" />
    ///     with the optional LipSync module without taking a hard assembly reference. In typical
    ///     scenarios, <see cref="ConvaiLipSyncSpeechEnergyAdapter" /> self-registers during
    ///     its lifecycle (<c>OnEnable</c>); this bridge serves as a last-resort factory when
    ///     a LipSync component exists but the adapter has not yet been created.
    /// </summary>
    /// <remarks>
    ///     Reflection is used because <c>Convai.Runtime</c> must not reference a module assembly, and
    ///     <c>SDK/link.xml</c> already preserves the adapter type explicitly so IL2CPP cannot strip
    ///     it. This bridge stays because auto-provisioning is a supported convenience, and it follows
    ///     the same rule as every other provisioned component: say what you added and why.
    /// </remarks>
    internal static class EmbodimentLipSyncBridge
    {
        private const string LipSyncAssemblyName = "Convai.Modules.LipSync";
        private const string SpeechEnergyAdapterTypeName = "Convai.Modules.LipSync.ConvaiLipSyncSpeechEnergyAdapter";
        private const string LipSyncComponentTypeName = "Convai.Modules.LipSync.ConvaiLipSyncComponent";

        /// <summary>
        ///     Fallback factory that creates and registers a speech energy adapter when the LipSync
        ///     module is present but no adapter has self-registered yet. Returns <c>true</c> when
        ///     an existing adapter is found and registered or a new adapter is successfully created;
        ///     <c>false</c> when LipSync is not present, no LipSync component exists in the hierarchy,
        ///     or adapter creation fails.
        /// </summary>
        /// <remarks>
        ///     Most production scenarios rely on <see cref="ConvaiLipSyncSpeechEnergyAdapter" />
        ///     registering itself during <c>OnEnable</c>. This method only runs when
        ///     <see cref="EmbodimentContext.EnsureSpeechEnergyProvider" /> is called before the
        ///     adapter's lifecycle completes, such as in unit tests or unusual initialization order.
        /// </remarks>
        public static bool TryRegisterSpeechEnergyAdapter(EmbodimentContext context)
        {
            if (context == null) return false;

            Type adapterType = FindLoadedType(SpeechEnergyAdapterTypeName, LipSyncAssemblyName);
            if (adapterType == null || !typeof(MonoBehaviour).IsAssignableFrom(adapterType))
                return false;

            // Check for existing adapter (including inactive/disabled instances, e.g. one
            // created earlier but never enabled). Provider registration and tick-scheduler
            // registration must never split: only the adapter's own OnEnable performs both
            // atomically, so the bridge's job here is to make sure OnEnable runs — never to
            // register the provider directly, which would leave a "provides but never samples"
            // adapter if it were found in a disabled state.
            if (context.GetComponentInChildren(adapterType, true) is MonoBehaviour existingBehaviour
                && existingBehaviour is ISpeechEnergyProvider)
            {
                if (!existingBehaviour.enabled)
                    existingBehaviour.enabled = true; // triggers OnEnable -> RegisterWithContext (provider + tick)
                return true;
            }

            // No adapter exists. Check if LipSync component is present to justify creating one.
            Type lipSyncType = FindLoadedType(LipSyncComponentTypeName, LipSyncAssemblyName);
            if (lipSyncType == null || context.GetComponentInChildren(lipSyncType, true) == null)
                return false;

            // Create adapter component; AddComponent enables it by default, which fires its
            // OnEnable and performs both provider and tick-scheduler registration together.
            var adapter = context.gameObject.AddComponent(adapterType) as MonoBehaviour;
            if (adapter == null) return false;

            adapter.hideFlags = EmbodimentContext.RuntimeInfrastructureHideFlags();

            ConvaiLogger.Info(
                $"[{adapterType.Name}] Added to '{context.gameObject.name}' so embodiment modules can " +
                "read this character's speech energy from Lip Sync.",
                LogCategory.Character);

            return adapter is ISpeechEnergyProvider;
        }

        private static Type FindLoadedType(string fullName, string assemblyName)
        {
            Type direct = Type.GetType($"{fullName}, {assemblyName}");
            if (direct != null) return direct;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type candidate = assemblies[i].GetType(fullName, throwOnError: false);
                if (candidate != null) return candidate;
            }

            return null;
        }
    }
}
