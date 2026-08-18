using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SignMasterVR.Lessons;

namespace SignMasterVR.UI
{
    /// <summary>
    /// PHASE 10/11/17 — Everything the learner reads/sees on the World Space canvas.
    /// LessonManager calls into this; this never decides lesson logic itself.
    /// Built automatically by Tools > SignMasterVR > 3 Build Lesson UI Prefab.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Lesson header")]
        public TMP_Text levelTitleText;
        public TMP_Text gestureNameText;
        public TMP_Text progressText;

        [Header("Correct gesture panel (Phase 11)")]
        public Image referenceImage;

        [Header("Feedback panel (Phase 17)")]
        public GameObject feedbackPanel;
        public TMP_Text feedbackText;
        public TMP_Text feedbackConfidenceText;

        [Header("Level complete panel (Phase 18)")]
        public GameObject levelCompletePanel;
        public TMP_Text levelCompleteText;

        private void Awake()
        {
            HideFeedback();
            if (levelCompletePanel != null) levelCompletePanel.SetActive(false);
        }

        public void ShowLevelHeader(LevelData level)
        {
            if (levelTitleText != null) levelTitleText.text = $"LEVEL {level.levelNumber}\n{level.levelName.ToUpper()}";
        }

        public void ShowGesturePrompt(GestureData gesture, int index, int total)
        {
            if (gestureNameText != null) gestureNameText.text = gesture.displayName;
            if (progressText != null) progressText.text = $"{index + 1} / {total}";
            if (referenceImage != null)
            {
                referenceImage.enabled = gesture.referenceImage != null;
                referenceImage.sprite = gesture.referenceImage;
            }
            HideFeedback();
        }

        public void ShowFeedback(bool correct, float confidencePercent, string message)
        {
            if (feedbackPanel != null) feedbackPanel.SetActive(true);
            if (feedbackText != null) feedbackText.text = correct ? $"✓ CORRECT\n{message}" : "✗ TRY AGAIN";
            if (feedbackConfidenceText != null) feedbackConfidenceText.text = correct ? $"{confidencePercent:0}% Accuracy" : "";
        }

        public void HideFeedback()
        {
            if (feedbackPanel != null) feedbackPanel.SetActive(false);
        }

        public void ShowLevelComplete(LevelData level, float scorePercent)
        {
            if (levelCompletePanel != null) levelCompletePanel.SetActive(true);
            if (levelCompleteText != null) levelCompleteText.text = $"LEVEL COMPLETE\n{level.levelName} completed!\nScore: {scorePercent:0}%";
        }

        public void HideLevelComplete()
        {
            if (levelCompletePanel != null) levelCompletePanel.SetActive(false);
        }
    }
}
