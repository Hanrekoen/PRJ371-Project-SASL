using Convai.Runtime.Components;
using Convai.Shared.Abstractions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views
{
    /// <summary>
    ///     Clickable area that opens the Convai runtime settings panel.
    ///     Used by Basic Sample transcript UIs (Chat / QA / Subtitle).
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class ConvaiSettingsClickArea : MonoBehaviour, IPointerClickHandler
    {
        private IConvaiSettingsPanelController _panelController;

        public void OnPointerClick(PointerEventData eventData)
        {
            TryResolveDependencies();
            _panelController?.Open();
        }

        public void Inject(IConvaiSettingsPanelController panelController = null) => _panelController = panelController;

        private void TryResolveDependencies()
        {
            if (_panelController != null) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager != null &&
                manager.TryGetSettingsPanelController(out IConvaiSettingsPanelController panelController))
                Inject(panelController);
        }
    }
}
