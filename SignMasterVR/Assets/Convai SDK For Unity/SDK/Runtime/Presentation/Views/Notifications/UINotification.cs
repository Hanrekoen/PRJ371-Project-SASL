using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Notifications
{
    /// <summary>
    ///     Represents a UI notification element that can be activated or deactivated.
    /// </summary>
    public class UINotification : MonoBehaviour
    {
        /// <summary>
        ///     The RectTransform of the notification UI element.
        /// </summary>
        public RectTransform notificationRectTransform;

        /// <summary>
        ///     The image component for displaying the notification icon.
        /// </summary>
        [SerializeField] private Image notificationIcon;

        /// <summary>
        ///     The TextMeshProUGUI component for displaying the notification title.
        /// </summary>
        [SerializeField] private TextMeshProUGUI notificationTitleText;

        /// <summary>
        ///     The TextMeshProUGUI component for displaying the notification text.
        /// </summary>
        [SerializeField] private TextMeshProUGUI notificationMessageText;

        /// <summary>
        ///     Deactivates the notification UI element on awake.
        /// </summary>
        private void Awake() => SetActive(false);

        /// <summary>
        ///     Initializes the UI notification with the provided Notification data.
        /// </summary>
        /// <param name="soNotification">The notification data to initialize the UI notification with.</param>
        public void Initialize(SONotification soNotification)
        {
            if (soNotification == null)
                throw new ArgumentNullException(nameof(soNotification), "SONotification is null.");

            notificationIcon.sprite = soNotification.icon;
            notificationTitleText.text = soNotification.notificationTitle;
            notificationMessageText.text = soNotification.notificationMessage;

            SetActive(true);
        }

        public void SetActive(bool value) => gameObject.SetActive(value);
    }
}
