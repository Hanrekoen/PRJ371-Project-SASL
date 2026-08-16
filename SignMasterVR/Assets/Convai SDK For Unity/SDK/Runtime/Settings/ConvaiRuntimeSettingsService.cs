using System;
using System.Collections.Generic;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Single source of truth for effective runtime settings.
    /// </summary>
    public sealed class ConvaiRuntimeSettingsService : IConvaiRuntimeSettingsService
    {
        private readonly ConvaiSettings _defaultsSettings;
        private readonly IMicrophoneDeviceService _microphoneDeviceService;
        private readonly IConvaiRuntimeSettingsStore _store;
        private readonly object _syncRoot = new();
        private ConvaiRuntimeSettingsSnapshot _current;
        private ConvaiRuntimeSettingsSnapshot _defaults;

        private ConvaiRuntimeSettingsOverrides _overrides;

        public ConvaiRuntimeSettingsService(
            ConvaiSettings defaultsSettings,
            IConvaiRuntimeSettingsStore store,
            IMicrophoneDeviceService microphoneDeviceService)
        {
            _defaultsSettings = defaultsSettings ?? ConvaiSettings.Instance;
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _microphoneDeviceService = microphoneDeviceService ??
                                       throw new ArgumentNullException(nameof(microphoneDeviceService));

            _defaults = BuildDefaultsSnapshot();
            _overrides = _store.LoadOverrides() ?? new ConvaiRuntimeSettingsOverrides();

            ConvaiRuntimeSettingsSnapshot merged = Merge(_defaults, _overrides);
            _current = Normalize(merged);
            _overrides = BuildOverrides(_current, _defaults);
            _store.SaveOverrides(_overrides);
        }

        public event Action<ConvaiRuntimeSettingsChanged> Changed;

        public ConvaiRuntimeSettingsSnapshot Current
        {
            get
            {
                lock (_syncRoot) return _current;
            }
        }

        public ConvaiRuntimeSettingsApplyResult Apply(ConvaiRuntimeSettingsPatch patch)
        {
            if (patch == null) return ConvaiRuntimeSettingsApplyResult.Invalid(Current, "Patch cannot be null.");

            if (patch.IsEmpty)
                return ConvaiRuntimeSettingsApplyResult.Ok(Current, ConvaiRuntimeSettingsChangeMask.None);

            ConvaiRuntimeSettingsChanged? changed = null;

            lock (_syncRoot)
            {
                ConvaiRuntimeSettingsSnapshot previous = _current;

                ConvaiRuntimeSettingsSnapshot candidate = _current.With(
                    patch.PlayerDisplayName ?? _current.PlayerDisplayName,
                    patch.TranscriptEnabled,
                    patch.NotificationsEnabled,
                    patch.PreferredMicrophoneDeviceId ?? _current.PreferredMicrophoneDeviceId);

                ConvaiRuntimeSettingsSnapshot normalized = Normalize(candidate);
                ConvaiRuntimeSettingsChangeMask mask = ComputeChangeMask(previous, normalized);

                if (mask == ConvaiRuntimeSettingsChangeMask.None)
                    return ConvaiRuntimeSettingsApplyResult.Ok(_current, ConvaiRuntimeSettingsChangeMask.None);

                _current = normalized;
                _overrides = BuildOverrides(_current, _defaults);
                _store.SaveOverrides(_overrides);

                changed = new ConvaiRuntimeSettingsChanged(previous, _current, mask);
            }

            if (changed.HasValue)
            {
                Changed?.Invoke(changed.Value);
                return ConvaiRuntimeSettingsApplyResult.Ok(changed.Value.Current, changed.Value.Mask);
            }

            return ConvaiRuntimeSettingsApplyResult.Ok(Current, ConvaiRuntimeSettingsChangeMask.None);
        }

        public ConvaiRuntimeSettingsApplyResult ResetToDefaults()
        {
            ConvaiRuntimeSettingsChanged? changed = null;

            lock (_syncRoot)
            {
                ConvaiRuntimeSettingsSnapshot previous = _current;

                _defaults = BuildDefaultsSnapshot();
                _current = Normalize(_defaults);
                _overrides = new ConvaiRuntimeSettingsOverrides();
                _store.ClearOverrides();

                ConvaiRuntimeSettingsChangeMask mask = ComputeChangeMask(previous, _current);
                if (mask != ConvaiRuntimeSettingsChangeMask.None)
                    changed = new ConvaiRuntimeSettingsChanged(previous, _current, mask);
            }

            if (changed.HasValue)
            {
                Changed?.Invoke(changed.Value);
                return ConvaiRuntimeSettingsApplyResult.Ok(changed.Value.Current, changed.Value.Mask);
            }

            return ConvaiRuntimeSettingsApplyResult.Ok(Current, ConvaiRuntimeSettingsChangeMask.None);
        }

        private ConvaiRuntimeSettingsSnapshot BuildDefaultsSnapshot()
        {
            string defaultName = _defaultsSettings != null ? _defaultsSettings.DefaultPlayerDisplayName : "Player";
            bool transcriptEnabled = _defaultsSettings == null || _defaultsSettings.TranscriptSystemEnabled;
            bool notificationsEnabled = _defaultsSettings != null && _defaultsSettings.NotificationSystemEnabled;
            string preferredMicId = _defaultsSettings?.DefaultMicrophoneDeviceId;

            string defaultMicId = string.Empty;
            IReadOnlyList<ConvaiMicrophoneDevice> devices = _microphoneDeviceService.GetAvailableDevices();
            if (devices.Count > 0) defaultMicId = _microphoneDeviceService.ResolvePreferredDeviceId(preferredMicId);

            return new ConvaiRuntimeSettingsSnapshot(
                defaultName,
                transcriptEnabled,
                notificationsEnabled,
                defaultMicId);
        }

        private static ConvaiRuntimeSettingsSnapshot Merge(
            ConvaiRuntimeSettingsSnapshot defaults,
            ConvaiRuntimeSettingsOverrides overrides)
        {
            if (overrides == null) return defaults;

            return defaults.With(
                overrides.PlayerDisplayName ?? defaults.PlayerDisplayName,
                overrides.TranscriptEnabled,
                overrides.NotificationsEnabled,
                overrides.PreferredMicrophoneDeviceId ?? defaults.PreferredMicrophoneDeviceId);
        }

        private ConvaiRuntimeSettingsSnapshot Normalize(ConvaiRuntimeSettingsSnapshot snapshot)
        {
            string fallbackName = _defaults.PlayerDisplayName;
            if (string.IsNullOrWhiteSpace(fallbackName)) fallbackName = "Player";

            string normalizedName = string.IsNullOrWhiteSpace(snapshot.PlayerDisplayName)
                ? fallbackName
                : snapshot.PlayerDisplayName.Trim();

            string normalizedMicId =
                _microphoneDeviceService.ResolvePreferredDeviceId(snapshot.PreferredMicrophoneDeviceId);

            return snapshot.With(
                normalizedName,
                preferredMicrophoneDeviceId: normalizedMicId);
        }

        private static ConvaiRuntimeSettingsOverrides BuildOverrides(
            ConvaiRuntimeSettingsSnapshot current,
            ConvaiRuntimeSettingsSnapshot defaults)
        {
            return new ConvaiRuntimeSettingsOverrides
            {
                PlayerDisplayName =
                    current.PlayerDisplayName == defaults.PlayerDisplayName ? null : current.PlayerDisplayName,
                TranscriptEnabled =
                    current.TranscriptEnabled == defaults.TranscriptEnabled ? null : current.TranscriptEnabled,
                NotificationsEnabled =
                    current.NotificationsEnabled == defaults.NotificationsEnabled ? null : current.NotificationsEnabled,
                PreferredMicrophoneDeviceId =
                    current.PreferredMicrophoneDeviceId == defaults.PreferredMicrophoneDeviceId
                        ? null
                        : current.PreferredMicrophoneDeviceId
            };
        }

        private static ConvaiRuntimeSettingsChangeMask ComputeChangeMask(
            ConvaiRuntimeSettingsSnapshot previous,
            ConvaiRuntimeSettingsSnapshot current)
        {
            var mask = ConvaiRuntimeSettingsChangeMask.None;

            if (!string.Equals(previous.PlayerDisplayName, current.PlayerDisplayName, StringComparison.Ordinal))
                mask |= ConvaiRuntimeSettingsChangeMask.PlayerDisplayName;

            if (previous.TranscriptEnabled != current.TranscriptEnabled)
                mask |= ConvaiRuntimeSettingsChangeMask.TranscriptEnabled;

            if (previous.NotificationsEnabled != current.NotificationsEnabled)
                mask |= ConvaiRuntimeSettingsChangeMask.NotificationsEnabled;

            if (!string.Equals(previous.PreferredMicrophoneDeviceId, current.PreferredMicrophoneDeviceId,
                    StringComparison.Ordinal)) mask |= ConvaiRuntimeSettingsChangeMask.PreferredMicrophoneDeviceId;

            return mask;
        }
    }
}
