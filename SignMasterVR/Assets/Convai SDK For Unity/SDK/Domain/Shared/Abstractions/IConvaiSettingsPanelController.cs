using System;

namespace Convai.Shared.Abstractions
{
    /// <summary>
    ///     Runtime controller for settings panel visibility state.
    /// </summary>
    public interface IConvaiSettingsPanelController
    {
        /// <summary>
        ///     Gets whether the settings panel is currently open.
        /// </summary>
        public bool IsOpen { get; }

        /// <summary>
        ///     Raised when panel visibility changes. Parameter is current visibility.
        /// </summary>
        public event Action<bool> VisibilityChanged;

        /// <summary>
        ///     Opens the settings panel.
        /// </summary>
        public void Open();

        /// <summary>
        ///     Closes the settings panel.
        /// </summary>
        public void Close();

        /// <summary>
        ///     Toggles the settings panel visibility.
        /// </summary>
        public void Toggle();
    }
}
