using UnityEngine;
using SignMasterVR.Lessons;

namespace SignMasterVR.Core
{
    /// <summary>
    /// PHASE 23 — Saves what the learner has completed. PlayerPrefs is enough for
    /// this prototype; swap the storage backend later without touching callers.
    /// </summary>
    public class ProgressManager : MonoBehaviour
    {
        public static ProgressManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private static string LevelKey(LevelData level) => $"SMVR_Level_{level.levelNumber}_Complete";
        private static string LevelScoreKey(LevelData level) => $"SMVR_Level_{level.levelNumber}_BestScore";
        private static string GestureKey(LevelData level, GestureData gesture) => $"SMVR_Level_{level.levelNumber}_Gesture_{gesture.gestureId}_Complete";

        public bool IsLevelComplete(LevelData level) => PlayerPrefs.GetInt(LevelKey(level), 0) == 1;

        public bool IsLevelUnlocked(LevelData level)
        {
            if (level.requiredPreviousLevel == null) return true;
            return IsLevelComplete(level.requiredPreviousLevel);
        }

        public void MarkGestureComplete(LevelData level, GestureData gesture) => PlayerPrefs.SetInt(GestureKey(level, gesture), 1);

        public bool IsGestureComplete(LevelData level, GestureData gesture) => PlayerPrefs.GetInt(GestureKey(level, gesture), 0) == 1;

        public void MarkLevelComplete(LevelData level, float scorePercent)
        {
            PlayerPrefs.SetInt(LevelKey(level), 1);
            float best = PlayerPrefs.GetFloat(LevelScoreKey(level), 0f);
            if (scorePercent > best) PlayerPrefs.SetFloat(LevelScoreKey(level), scorePercent);
            PlayerPrefs.Save();
        }

        public float GetBestScore(LevelData level) => PlayerPrefs.GetFloat(LevelScoreKey(level), 0f);

        public void ResetAllProgress() => PlayerPrefs.DeleteAll();
    }
}
