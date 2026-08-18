using UnityEngine;

namespace SignMasterVR.Lessons
{
    /// <summary>
    /// Auto-starts a level when the scene plays — convenient for solo Editor
    /// testing this week where there's no main menu yet. Added automatically by
    /// Tools > SignMasterVR > 4 Wire Up Current Scene. Once a MainMenu scene
    /// exists, disable this and call lessonManager.StartLevel(chosenLevel) from
    /// a "Start Lesson" button instead.
    /// </summary>
    public class LessonBootstrap : MonoBehaviour
    {
        public LessonManager lessonManager;
        public LevelData levelToStart;

        private void Start()
        {
            if (lessonManager != null && levelToStart != null)
                lessonManager.StartLevel(levelToStart);
            else
                Debug.LogWarning("[LessonBootstrap] Missing lessonManager or levelToStart reference.");
        }
    }
}
