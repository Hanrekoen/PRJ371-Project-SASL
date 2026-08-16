using System.Collections;
using System.Collections.Generic;
using Convai.Runtime.Presentation.Services.Utilities;
using UnityEngine;

namespace Convai.Runtime.Presentation.Views.Notifications
{
    /// <summary>
    ///     This class is responsible for controlling the UI notifications in the game.
    ///     It handles the creation, activation, deactivation, and animation of notifications.
    /// </summary>
    public class UINotificationController : MonoBehaviour
    {
        /// <summary>
        ///     Maximum number of notifications that can be displayed at the same time.
        /// </summary>
        private const int MAX_NUMBER_OF_NOTIFICATION_AT_SAME_TIME = 3;

        private const float FADE_IN_DURATION = 0.35f;
        private const float FADE_OUT_DURATION = 0.2f;

        /// <summary>
        ///     References to the UI notification prefab and other necessary components.
        /// </summary>
        [Header("References")] [SerializeField]
        private UINotification uiNotificationPrefab;

        /// <summary>
        ///     Spacing between Notifications
        /// </summary>
        [Header("Configurations")] [SerializeField]
        private int spacing = 100;

        /// <summary>
        ///     Position for Active Notification
        /// </summary>
        [Tooltip(
            "Starting position for the first notification; Y value adjusts sequentially for each subsequent notification.")]
        [SerializeField]
        private Vector2 activeNotificationPos;

        /// <summary>
        ///     Position for Deactivated Notification
        /// </summary>
        [SerializeField] private Vector2 deactivatedNotificationPos;

        [Header("UI Notification Animation Values")] [SerializeField]
        private float activeDuration = 4f;

        [SerializeField] private float slipDuration = 0.3f;
        [SerializeField] private float delay = 0.3f;
        [SerializeField] private AnimationCurve slipAnimationCurve;

        /// <summary>
        ///     List to keep track of the order in which pending notifications were requested.
        /// </summary>
        private readonly List<SONotification> _pendingNotificationsOrder = new();

        private Queue<UINotification> _activeUINotifications;
        private CanvasGroup _canvasGroup;
        private Queue<UINotification> _deactivatedUINotifications;
        private CanvasFader _fadeCanvas;

        /// <summary>
        ///     Flag indicating whether a UI notification movement animation is currently in progress.
        ///     Used to prevent overlapping animation coroutines for UI notifications.
        /// </summary>
        private bool _isNotificationAnimationInProgress;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _fadeCanvas = GetComponent<CanvasFader>();
            if (_fadeCanvas == null) _fadeCanvas = gameObject.AddComponent<CanvasFader>();

            // Fix zero scale issue
            if (transform.localScale == Vector3.zero) transform.localScale = Vector3.one;

            InitializeUINotifications();
        }

        /// <summary>
        ///     Handles a new notification request by adding it to the order list and attempting to initialize it.
        ///     If a notification animation is already in progress, waits for it to complete before processing the new request.
        /// </summary>
        /// <param name="soNotification">The requested SONotification to be processed.</param>
        public void Notify(SONotification soNotification)
        {
            _pendingNotificationsOrder.Add(soNotification);

            if (_isNotificationAnimationInProgress) return;

            if (!TryInitializeNewNotification(soNotification, out UINotification uiNotification)) return;

            StartNotificationUICoroutine(uiNotification);
        }

        /// <summary>
        ///     This function is used to initialize the UI notifications.
        ///     It initializes the queues for active and deactivated UI notifications and instantiates and enqueues deactivated UI
        ///     notifications.
        /// </summary>
        private void InitializeUINotifications()
        {
            _activeUINotifications = new Queue<UINotification>();
            _deactivatedUINotifications = new Queue<UINotification>();

            for (int i = 0; i < MAX_NUMBER_OF_NOTIFICATION_AT_SAME_TIME; i++)
            {
                UINotification uiNotification = Instantiate(uiNotificationPrefab, transform);

                uiNotification.notificationRectTransform.anchoredPosition = deactivatedNotificationPos;
                _deactivatedUINotifications.Enqueue(uiNotification);
            }
        }

        /// <summary>
        ///     Attempts to initialize a new UI notification using the provided SONotification.
        ///     Tries to get an available UI notification and initializes it with the given SONotification.
        /// </summary>
        /// <param name="soNotification">The SONotification to be used for initializing the UI notification.</param>
        /// <param name="uiNotification">The initialized UINotification if successful, otherwise null.</param>
        /// <returns>True if initialization is successful, false otherwise.</returns>
        private bool TryInitializeNewNotification(SONotification soNotification, out UINotification uiNotification)
        {
            uiNotification = GetAvailableUINotification();
            if (uiNotification == null) return false;

            uiNotification.Initialize(soNotification);
            return true;
        }

        /// <summary>
        ///     Initiates the coroutine for UI notification animations and adds the notification to the active queue.
        /// </summary>
        /// <param name="uiNotification">The UINotification to be animated and added to the active queue.</param>
        private void StartNotificationUICoroutine(UINotification uiNotification)
        {
            float extraDelayForNotificationEndTransition = 0.5f;

            float totalAnimationDuration = FADE_IN_DURATION + activeDuration + (2 * slipDuration) + delay +
                                           extraDelayForNotificationEndTransition;

            _fadeCanvas.StartFadeInFadeOutWithGap(_canvasGroup, FADE_IN_DURATION, FADE_OUT_DURATION,
                totalAnimationDuration);

            _activeUINotifications.Enqueue(uiNotification);

            StartCoroutine(StartNotificationUI(uiNotification));
        }

        /// <summary>
        ///     Coroutine for managing the lifecycle of a UI notification, including its activation, display duration, and
        ///     deactivation.
        /// </summary>
        /// <param name="uiNotification">The UINotification to be managed.</param>
        private IEnumerator StartNotificationUI(UINotification uiNotification)
        {
            int firstIndex = 0;
            _pendingNotificationsOrder.RemoveAt(firstIndex);

            yield return MoveUINotificationToActivePosition(uiNotification);

            yield return new WaitForSeconds(activeDuration);

            UpdateUINotificationPositions();

            yield return MoveUINotificationToHiddenPosition(uiNotification);

            DeactivateAndEnqueueUINotification(uiNotification);

            if (AreTherePendingNotifications()) TryInitializeAndStartNewNotification();

            UpdateUINotificationPositions();
        }

        /// <summary>
        ///     Moves the UI notification to its active position.
        /// </summary>
        private IEnumerator MoveUINotificationToActivePosition(UINotification uiNotification)
        {
            float targetY = activeNotificationPos.y - (spacing * (_activeUINotifications.Count - 1));
            Vector2 targetPos = new(activeNotificationPos.x, targetY);
            yield return StartCoroutine(MoveUINotificationToTargetPos(uiNotification, targetPos));
        }

        /// <summary>
        ///     Moves the UI notification to its hidden position.
        /// </summary>
        private IEnumerator MoveUINotificationToHiddenPosition(UINotification uiNotification)
        {
            Vector2 targetPos = deactivatedNotificationPos;
            yield return StartCoroutine(MoveUINotificationToTargetPos(uiNotification, targetPos));
        }

        /// <summary>
        ///     Deactivates the UI notification, updates positions, and enqueues it for later use.
        /// </summary>
        private void DeactivateAndEnqueueUINotification(UINotification uiNotification)
        {
            uiNotification.SetActive(false);
            _activeUINotifications.Dequeue();
            _deactivatedUINotifications.Enqueue(uiNotification);
            UpdateUINotificationPositions();
        }

        /// <summary>
        ///     Checks if there are pending notifications and initializes and starts a new one if available.
        /// </summary>
        private void TryInitializeAndStartNewNotification()
        {
            if (TryInitializeNewNotification(_pendingNotificationsOrder[0], out UINotification newUiNotification))
                StartNotificationUICoroutine(newUiNotification);
        }

        /// <summary>
        ///     Smoothly moves the UI notification to the target position over a specified duration.
        /// </summary>
        /// <param name="uiNotification">The UINotification to be moved.</param>
        /// <param name="targetPos">The target position to move the UINotification to.</param>
        private IEnumerator MoveUINotificationToTargetPos(UINotification uiNotification, Vector2 targetPos)
        {
            _isNotificationAnimationInProgress = true;

            float elapsedTime = 0f;
            Vector2 startPos = uiNotification.notificationRectTransform.anchoredPosition;

            while (elapsedTime <= slipDuration + delay)
            {
                elapsedTime += Time.deltaTime;
                float percent = Mathf.Clamp01(elapsedTime / slipDuration);
                float curvePercent = slipAnimationCurve.Evaluate(percent);
                uiNotification.notificationRectTransform.anchoredPosition =
                    Vector2.Lerp(startPos, targetPos, curvePercent);
                yield return null;
            }

            _isNotificationAnimationInProgress = false;
        }

        /// <summary>
        ///     Updates the positions of active UI notifications along the Y-axis.
        /// </summary>
        private void UpdateUINotificationPositions()
        {
            float targetX = activeNotificationPos.x;
            float targetY = activeNotificationPos.y;

            foreach (UINotification activeUINotification in _activeUINotifications)
            {
                Vector2 targetPos = new(targetX, targetY);
                StartCoroutine(MoveUINotificationToTargetPos(activeUINotification, targetPos));
                targetY -= spacing;
            }
        }

        /// <summary>
        ///     Gets an available UI notification from the deactivated queue.
        /// </summary>
        /// <returns>The available UI notification, or null if the deactivated queue is empty.</returns>
        private UINotification GetAvailableUINotification()
        {
            if (_deactivatedUINotifications.Count == 0) return null;

            return _deactivatedUINotifications.Dequeue();
        }

        /// <summary>
        ///     Checks if there are pending notifications in the order list.
        /// </summary>
        /// <returns>True if there are pending notifications, false otherwise.</returns>
        private bool AreTherePendingNotifications() =>
            _pendingNotificationsOrder.Count >= 1;

        /// <summary>
        ///     Clears all active and pending notifications.
        /// </summary>
        public void ClearAll()
        {
            StopAllCoroutines();

            _pendingNotificationsOrder.Clear();
            _isNotificationAnimationInProgress = false;

            // Deactivate all active notifications
            while (_activeUINotifications.Count > 0)
            {
                UINotification notification = _activeUINotifications.Dequeue();
                notification.SetActive(false);
                notification.notificationRectTransform.anchoredPosition = deactivatedNotificationPos;
                _deactivatedUINotifications.Enqueue(notification);
            }

            // Reset canvas group
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        }
    }
}
