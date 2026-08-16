using System;
using Convai.Shared.Abstractions;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Default runtime implementation for settings panel visibility state.
    /// </summary>
    public sealed class ConvaiSettingsPanelController : IConvaiSettingsPanelController
    {
        public event Action<bool> VisibilityChanged = delegate { };

        public bool IsOpen { get; private set; }

        public void Open()
        {
            if (IsOpen) return;

            IsOpen = true;
            VisibilityChanged(IsOpen);
        }

        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            VisibilityChanged(IsOpen);
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }
    }
}
