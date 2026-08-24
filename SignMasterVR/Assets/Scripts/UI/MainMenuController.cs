using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using SignMasterVR.Lessons;
using SignMasterVR.Core;

namespace SignMasterVR.UI
{
    /// <summary>
    /// PHASE 19/20 — Title screen: Start Lesson, browse Levels, reset progress.
    /// Built automatically by Tools > SignMasterVR > 6 Build Main Menu Scene.
    /// Only Level 1 (Letters) exists today; StartLevelByIndex/levelRows are
    /// already list-based so adding Level 2+ later is just adding entries,
    /// not restructuring this script.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class MainMenuController : MonoBehaviour
    {
        [System.Serializable]
        public struct LevelRow
        {
            public LevelData level;
            public TMP_Text statusText;
        }

        [Tooltip("Every level in the game, in order. Index 0 is what \"Start Lesson\" jumps straight into.")]
        public LevelData[] levels;

        [Tooltip("Matches each level to the status label shown in the Level List panel.")]
        public LevelRow[] levelRows;

        public GameObject levelListPanel;
        public string classroomSceneName = "ClassRoom";

        private void OnEnable() => RefreshLevelStatus();

        /// <summary>Wired to the "START LESSON" button — jumps straight into the first level.</summary>
        public void StartFirstLevel() => StartLevelByIndex(0);

        public void StartLevelByIndex(int index)
        {
            if (levels == null || index < 0 || index >= levels.Length)
            {
                Debug.LogWarning("[MainMenuController] No level at that index.");
                return;
            }
            LessonLaunchContext.SelectedLevel = levels[index];
            SceneManager.LoadScene(classroomSceneName);
        }

        public void ShowLevelList()
        {
            if (levelListPanel != null) levelListPanel.SetActive(true);
            RefreshLevelStatus();
        }

        public void HideLevelList()
        {
            if (levelListPanel != null) levelListPanel.SetActive(false);
        }

        public void ResetProgress()
        {
            ProgressManager.Instance?.ResetAllProgress();
            RefreshLevelStatus();
            Debug.Log("[MainMenuController] Progress reset.");
        }

        private void RefreshLevelStatus()
        {
            if (levelRows == null || ProgressManager.Instance == null) return;
            foreach (var row in levelRows)
            {
                if (row.level == null || row.statusText == null) continue;
                bool complete = ProgressManager.Instance.IsLevelComplete(row.level);
                bool unlocked = ProgressManager.Instance.IsLevelUnlocked(row.level);
                row.statusText.text = complete ? "COMPLETE" : unlocked ? "UNLOCKED" : "LOCKED";
            }
        }
    }
}
