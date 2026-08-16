using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Modules.Embodiment.Presets;
using Convai.Runtime.Embodiment;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.Embodiment.Components
{
    /// <summary>
    ///     Optional character-root helper that applies a ConvaiEmbodimentPreset to any
    ///     embodiment module implementing IEmbodimentProfileReceiver.
    /// </summary>
    [AddComponentMenu("Convai/Embodiment/Preset")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(EmbodimentExecutionOrders.Binding)]
    public sealed class ConvaiEmbodimentPresetBinding : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Preset applied to embodiment modules on this character at Awake.")]
        private ConvaiEmbodimentPreset preset;

        [SerializeField]
        [Tooltip("Optional library for runtime id-based preset swapping. Does not apply automatically.")]
        private ConvaiEmbodimentPresetLibrary library;

        [SerializeField]
        [Tooltip("When true, receivers without a matching preset entry keep their inspector-assigned profile.")]
        private bool preserveMissingSlots = true;

        public ConvaiEmbodimentPreset Preset => preset;
        public ConvaiEmbodimentPresetLibrary Library => library;
        public bool PreserveMissingSlots => preserveMissingSlots;

        private readonly List<EmbodimentProfileReceiverRegistration> _scratchReceivers = new();
        private EmbodimentContext _context;
        private bool _subscribed;

        private void Awake()
        {
            ResolveContext();
            if (preset != null) Apply(preset);
        }

        private void OnEnable()
        {
            ResolveContext();
            SubscribeToContext();
            if (preset != null) ApplyToRegisteredReceivers(preset);
        }

        private void OnDisable()
        {
            if (_subscribed && _context != null)
            {
                _context.ProfileReceiverRegistered -= HandleProfileReceiverRegistered;
                _subscribed = false;
            }
        }

        public void Apply(ConvaiEmbodimentPreset newPreset)
        {
            if (newPreset == null) return;
            preset = newPreset;

            _scratchReceivers.Clear();

            MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is not IEmbodimentProfileReceiver receiver) continue;
                _scratchReceivers.Add(new EmbodimentProfileReceiverRegistration(receiver, components[i]));
            }

            LogPresetDiagnostics(newPreset, _scratchReceivers);

            for (int i = 0; i < _scratchReceivers.Count; i++)
            {
                EmbodimentProfileReceiverRegistration registration = _scratchReceivers[i];
                if (!registration.IsValid) continue;
                ApplyToReceiver(newPreset, registration.Receiver, registration.Owner);
            }

            ResolveContext();
            SubscribeToContext();
            _context?.NotifyEmbodimentConfigurationChanged();
        }

        public bool ApplyPresetById(string id)
        {
            if (library == null) return false;
            ConvaiEmbodimentPreset resolved = library.Find(id);
            if (resolved == null) return false;
            Apply(resolved);
            return true;
        }

        private void ApplyToReceiver(
            ConvaiEmbodimentPreset sourcePreset,
            IEmbodimentProfileReceiver receiver,
            UnityEngine.Object receiverObject)
        {
            if (receiver == null) return;

            bool hasSlot = sourcePreset.TryGetProfile(receiver.ModuleId, out ScriptableObject profile);
            if (!hasSlot && preserveMissingSlots) return;

            if (!receiver.CanApplyProfile(profile))
            {
                LogBindingWarning(
                    $"[ConvaiEmbodimentPresetBinding] Profile '{profile?.name ?? "<null>"}' cannot be applied " +
                    $"to module '{receiver.ModuleId}' on '{receiverObject?.name ?? name}'.");
                return;
            }

            if (!receiver.ApplyProfile(profile))
            {
                LogBindingWarning(
                    $"[ConvaiEmbodimentPresetBinding] Module '{receiver.ModuleId}' rejected profile " +
                    $"'{profile?.name ?? "<null>"}' on '{receiverObject?.name ?? name}'.");
            }
        }

        private void ResolveContext()
        {
            if (_context != null) return;
            EmbodimentContext.TryResolve(this, out _context);
        }

        private void SubscribeToContext()
        {
            if (_context == null || _subscribed) return;
            _context.ProfileReceiverRegistered += HandleProfileReceiverRegistered;
            _subscribed = true;
        }

        private void ApplyToRegisteredReceivers(ConvaiEmbodimentPreset sourcePreset)
        {
            if (_context == null || sourcePreset == null) return;

            _scratchReceivers.Clear();
            _context.GetProfileReceivers(_scratchReceivers);
            LogPresetDiagnostics(sourcePreset, _scratchReceivers);
            for (int i = 0; i < _scratchReceivers.Count; i++)
            {
                EmbodimentProfileReceiverRegistration registration = _scratchReceivers[i];
                if (!registration.IsValid) continue;
                ApplyToReceiver(sourcePreset, registration.Receiver, registration.Owner);
            }
        }

        private void HandleProfileReceiverRegistered(EmbodimentProfileReceiverRegistration registration)
        {
            if (preset == null || !registration.IsValid) return;
            ApplyToReceiver(preset, registration.Receiver, registration.Owner);
            _context?.NotifyEmbodimentConfigurationChanged();
        }

        private void LogPresetDiagnostics(
            ConvaiEmbodimentPreset sourcePreset,
            IReadOnlyList<EmbodimentProfileReceiverRegistration> receivers)
        {
            if (sourcePreset == null) return;

            var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (receivers != null)
            {
                for (int i = 0; i < receivers.Count; i++)
                {
                    EmbodimentProfileReceiverRegistration registration = receivers[i];
                    if (!registration.IsValid || string.IsNullOrWhiteSpace(registration.Receiver.ModuleId))
                        continue;
                    moduleIds.Add(registration.Receiver.ModuleId);
                }
            }

            if (!sourcePreset.TryBuildDiagnosticReport(moduleIds, preserveMissingSlots, out string report))
                return;

            LogBindingWarning(
                $"[ConvaiEmbodimentPresetBinding] Preset '{sourcePreset.name}' diagnostics on '{name}': {report}");
        }

        /// <summary>
        ///     Reports a preset problem through the character's logger, falling back to the shared
        ///     setup reporter so the message survives a project with no log sink installed.
        /// </summary>
        /// <remarks>
        ///     Delegates the fallback to <see cref="EmbodimentDiagnostics" /> rather than spelling it
        ///     out again here, so the setup-warning fallback lives in exactly one place and the
        ///     logging guard test covers it there.
        /// </remarks>
        private void LogBindingWarning(string message)
        {
            ILogger logger = _context?.Logger;
            if (logger != null)
            {
                logger.Warning(message, LogCategory.Character);
                return;
            }

            EmbodimentDiagnostics.SetupWarning(message);
        }
    }
}
