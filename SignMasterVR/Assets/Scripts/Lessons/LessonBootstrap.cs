using UnityEngine;

namespace SignMasterVR.Lessons
{
    /// <summary>
    /// Carries which level to start across the MainMenu -> Classroom scene load.
    /// MainMenuController sets this before calling SceneManager.LoadScene();
    /// LessonBootstrap reads and clears it on the other side.
    /// </summary>
    public static class LessonLaunchContext
    {
        public static LevelData SelectedLevel;
    }

    /// <summary>
    /// Starts a level when the Classroom scene plays. If MainMenuController sent a
    /// specific level via LessonLaunchContext, that's used; otherwise falls back to
    /// `levelToStart` (handy for solo Editor testing — press Play directly on the
    /// Classroom scene and it still starts Level 1 without going through the menu).
    /// Added automatically by Tools > SignMasterVR > 4 Wire Up Current Scene.
    /// </summary>
    public class LessonBootstrap : MonoBehaviour
    {
        public LessonManager lessonManager;
        public LevelData levelToStart;

        private void Start()
        {
            LevelData level = LessonLaunchContext.SelectedLevel != null ? LessonLaunchContext.SelectedLevel : levelToStart;
            LessonLaunchContext.SelectedLevel = null;

            if (lessonManager != null && level != null)
                lessonManager.StartLevel(level);
            else
                Debug.LogWarning("[LessonBootstrap] Missing lessonManager or level to start.");
        }
    }
}
