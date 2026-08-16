using System;
using Convai.Runtime.Components;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Applies player-display-name runtime settings to the resolved local player.
    /// </summary>
    internal sealed class RuntimeSettingsIdentityApplier : IDisposable
    {
        private const string DefaultPlayerDisplayName = "Player";

        private readonly Action<string> _displayNameApplied;
        private readonly Func<ConvaiPlayer> _playerAccessor;
        private readonly IConvaiRuntimeSettingsService _settings;

        public RuntimeSettingsIdentityApplier(
            IConvaiRuntimeSettingsService settings,
            Func<ConvaiPlayer> playerAccessor,
            Action<string> displayNameApplied = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _playerAccessor = playerAccessor ?? throw new ArgumentNullException(nameof(playerAccessor));
            _displayNameApplied = displayNameApplied;

            _settings.Changed += OnSettingsChanged;
            Apply(_settings.Current);
        }

        public void Dispose() => _settings.Changed -= OnSettingsChanged;

        internal void ApplyCurrent() => Apply(_settings.Current);

        private void OnSettingsChanged(ConvaiRuntimeSettingsChanged changed)
        {
            if ((changed.Mask & ConvaiRuntimeSettingsChangeMask.PlayerDisplayName) == 0)
                return;

            Apply(changed.Current);
        }

        private void Apply(ConvaiRuntimeSettingsSnapshot snapshot)
        {
            ConvaiPlayer player = _playerAccessor();
            if (player == null) return;

            string displayName = snapshot.PlayerDisplayName;
            player.SetRuntimeDisplayName(
                string.IsNullOrWhiteSpace(displayName) ||
                string.Equals(displayName, DefaultPlayerDisplayName, StringComparison.Ordinal)
                    ? null
                    : displayName);
            _displayNameApplied?.Invoke(player.PlayerName);
        }
    }
}
