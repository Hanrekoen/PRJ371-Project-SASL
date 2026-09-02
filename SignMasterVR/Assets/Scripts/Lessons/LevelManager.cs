using UnityEngine;
using SignMasterVR.Core;

namespace SignMasterVR.Lessons
{
    /// <summary>
    /// PHASE 12/20 — Knows about every level that exists and whether it's unlocked.
    /// Assign every LevelData asset here, in order (auto-assigned to just Level 1
    /// by the scene-wiring tool this week).
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public LevelData[] allLevels;

        public bool IsUnlocked(LevelData level) => ProgressManager.Instance.IsLevelUnlocked(level);
        public bool IsComplete(LevelData level) => ProgressManager.Instance.IsLevelComplete(level);

        public LevelData GetNextLevel(LevelData current)
        {
            for (int i = 0; i < allLevels.Length; i++)
                if (allLevels[i] == current && i + 1 < allLevels.Length)
                    return allLevels[i + 1];
            return null;
        }
    }
}
