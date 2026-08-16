using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Transcript
{
    /// <summary>
    ///     Sample feedback handler for transcript messages.
    ///     Allows users to provide positive/negative feedback on AI responses.
    /// </summary>
    /// <remarks>
    ///     Reference implementation that projects can customize or replace as needed.
    /// </remarks>
    public class FeedbackHandler : MonoBehaviour
    {
        [SerializeField] private ChatMessageBubble messageUI;
        [SerializeField] private Button positiveButton;
        [SerializeField] private Button negativeButton;

        [SerializeField] private GameObject positiveButtonFill;
        [SerializeField] private GameObject negativeButtonFill;

        private void OnEnable()
        {
            positiveButton.onClick.AddListener(OnPositiveButtonClick);
            negativeButton.onClick.AddListener(OnNegativeButtonClick);
        }

        private void OnDisable()
        {
            positiveButton.onClick.RemoveListener(OnPositiveButtonClick);
            negativeButton.onClick.RemoveListener(OnNegativeButtonClick);
        }

        public void ResetState()
        {
            positiveButtonFill.SetActive(false);
            negativeButtonFill.SetActive(false);
        }

        private void OnNegativeButtonClick()
        {
            if (messageUI.SendFeedback(false)) ToggleFillImage(false);
        }

        private void OnPositiveButtonClick()
        {
            if (messageUI.SendFeedback(true)) ToggleFillImage(true);
        }

        private void ToggleFillImage(bool isPositiveFeedback)
        {
            positiveButtonFill.SetActive(isPositiveFeedback);
            negativeButtonFill.SetActive(!isPositiveFeedback);
        }
    }
}
