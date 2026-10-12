using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using SignMasterVR.Tutor;
using SignMasterVR.UI;
using SignMasterVR.ML;
using SignMasterVR.Core;

namespace SignMasterVR.Lessons
{
    /// <summary>
    /// PHASE 13/14 — The conductor. Owns "what happens next" and nothing else.
    /// Flow: Load Level -> Load Gesture -> Tell Tutor -> Tell UI -> Wait for
    /// learner -> Receive ML result -> Evaluate -> Give feedback -> Next gesture
    /// -> ... -> Level Complete -> unlock next level.
    /// Wired up automatically by Tools > SignMasterVR > 4 Wire Up Current Scene.
    /// </summary>
    public class LessonManager : MonoBehaviour
    {
        [Header("Refs")]
        public TutorController tutor;
        public UIManager ui;
        public LevelManager levelManager;

        [Tooltip("Any MonoBehaviour implementing IGestureRecognizer — FakeGestureRecognizer for now.")]
        public MonoBehaviour recognizerBehaviour;
        private IGestureRecognizer _recognizer;

        [Tooltip("Optional. Plays a recorded 2D hand-skeleton demo of the target sign alongside the tutor. Leave null if no ghost-hand clips have been exported yet — everything else works without it. Assigned automatically by Tools > SignMasterVR > 15 Add Ghost Hand Demo.")]
        public GhostHandPlayer ghostHand;

        [Header("Timing")]
        [Tooltip("Seconds the feedback panel stays up before moving on.")]
        public float feedbackHoldTime = 2f;

        [Header("Runtime state (read-only)")]
        public LevelData currentLevel;

        [Header("Navigation")]
        [Tooltip("Scene the Main Menu button loads. Must also be in File > Build Profiles > " +
                 "Scenes In Build, or the button does nothing in a build while working fine in " +
                 "the Editor.")]
        public string mainMenuSceneName = "MainMenu";
        private int _gestureIndex;
        private int _correctCount;
        private GestureData CurrentGesture => currentLevel.gestures[_gestureIndex];

        private void Awake()
        {
            _recognizer = recognizerBehaviour as IGestureRecognizer;
            if (_recognizer == null)
                Debug.LogError("[LessonManager] recognizerBehaviour does not implement IGestureRecognizer.");
        }

        private void OnEnable()
        {
            if (_recognizer != null) _recognizer.OnGestureDetected += HandleGestureResult;
        }

        private void OnDisable()
        {
            if (_recognizer != null) _recognizer.OnGestureDetected -= HandleGestureResult;
        }

        /// <summary>Call this to begin (or resume) a level, e.g. from LevelSelect or LessonBootstrap.</summary>
        public void StartLevel(LevelData level)
        {
            currentLevel = level;
            _gestureIndex = 0;
            _correctCount = 0;
            ui.ShowLevelHeader(level);
            tutor.PlayWelcome();
            LoadCurrentGesture();
        }

        private void LoadCurrentGesture()
        {
            if (currentLevel == null || currentLevel.gestures.Length == 0) return;

            var gesture = CurrentGesture;
            _recognizer.SetTargetGesture(gesture.gestureId);
            ui.ShowGesturePrompt(gesture, _gestureIndex, currentLevel.gestures.Length);
            tutor.Demonstrate(gesture.tutorAnimationTrigger);
            ghostHand?.Play(gesture.gestureId);
            // Spoken prompt, on presentation only -- deliberately NOT on a retry.
            // Failing four times should not mean hearing "Letter B" four times.
            AudioManager.Instance?.Play(gesture.promptAudioKey);
            // Learner now attempts the sign. This week: press C / TEST CORRECT
            // or X / TEST WRONG. Later: XR Hands -> ML model -> OnGestureDetected
            // fires automatically — same code path either way.
        }

        /// <summary>
        /// Move past the current sign without passing it. Wired to the Skip button by
        /// Tools > SignMasterVR > 18.
        ///
        /// This matters more for a sign-language tutor than for most lessons: a learner can be
        /// physically unable to form a particular handshape, and a recognizer that will not
        /// accept them has no way to tell the difference between "cannot" and "will not". Being
        /// trapped on letter G with no way forward is the worst possible experience, and it is
        /// also the most likely thing to happen in front of an examiner.
        ///
        /// A skip is NOT a pass: _correctCount is deliberately left alone, so the end-of-level
        /// score tells the truth, and ProgressManager is not told the sign was completed.
        /// </summary>
        public void SkipCurrentGesture()
        {
            if (currentLevel == null || currentLevel.gestures.Length == 0) return;

            // A feedback coroutine already in flight would advance the index again when it
            // finishes, so one press of Skip would jump two signs. Stopping it first is the
            // whole reason this is not simply "_gestureIndex++".
            StopAllCoroutines();
            ui.HideFeedback();

            Debug.Log($"[LessonManager] Skipped \"{CurrentGesture.gestureId}\" -- not counted as correct.");

            _gestureIndex++;
            if (_gestureIndex >= currentLevel.gestures.Length) CompleteLevel();
            else LoadCurrentGesture();
        }

        /// <summary>The level that follows this one, or null when this is the last.</summary>
        public LevelData NextLevel =>
            levelManager != null && currentLevel != null ? levelManager.GetNextLevel(currentLevel) : null;

        /// <summary>
        /// Leave the lesson and go back to the main menu. Wired to the Menu buttons by
        /// Tools > SignMasterVR > 18.
        ///
        /// Stops any running coroutine first: a feedback coroutine that survives the scene load
        /// would come back to touch a UIManager that no longer exists.
        /// </summary>
        public void GoToMainMenu()
        {
            StopAllCoroutines();
            SceneManager.LoadScene(mainMenuSceneName);
        }

        /// <summary>
        /// Move straight on to the next level from the level-complete panel.
        ///
        /// Before this existed, finishing a level was a dead end: CompleteLevel showed the
        /// panel, logged "Level 2 is now unlocked" to a console nobody in a headset can read,
        /// and left the learner with no way to reach it or to get out. Falls back to the main
        /// menu when there is no next level, so the last level is not a dead end either.
        /// </summary>
        public void StartNextLevel()
        {
            var next = NextLevel;
            if (next == null)
            {
                Debug.Log("[LessonManager] No level after this one -- returning to the main menu.");
                GoToMainMenu();
                return;
            }

            StopAllCoroutines();
            ui.HideLevelComplete();
            ui.HideFeedback();
            StartLevel(next);
        }

        private void HandleGestureResult(GestureResult result)
        {
            var gesture = CurrentGesture;

            // Prefer the target's own probability: it answers "did they sign the
            // target?" directly, and a wrong sign cannot pass it by construction.
            // Fall back to the old string-match check when the recognizer doesn't
            // supply one (FakeGestureRecognizer, --placeholder, or an older server).
            bool correct = result.TargetConfidence > 0f
                ? result.TargetConfidence >= gesture.requiredConfidence
                : (result.GestureId == gesture.gestureId && result.Confidence >= gesture.requiredConfidence);

            StartCoroutine(GiveFeedbackAndAdvance(correct, result, gesture));
        }

        private IEnumerator GiveFeedbackAndAdvance(bool correct, GestureResult result, GestureData gesture)
        {
            // The number that actually decided correctness above, so the % shown
            // to the learner matches the check that was applied to it.
            float displayConfidence = result.TargetConfidence > 0f ? result.TargetConfidence : result.Confidence;

            if (correct)
            {
                _correctCount++;
                tutor.PlayCorrect();
                ui.ShowFeedback(true, displayConfidence * 100f, gesture.successMessage);
                AudioManager.Instance?.Play(gesture.successAudioKey);
                ProgressManager.Instance?.MarkGestureComplete(currentLevel, gesture);
            }
            else
            {
                // GestureId is the model's overall best guess — targetConfidence alone
                // can't tell the learner what they actually signed, so surface it here.
                string hint = (result.GestureId != gesture.gestureId && result.GestureId != "NONE" && !string.IsNullOrEmpty(result.GestureId))
                    ? $"That looked like \"{result.GestureId}\"."
                    : "";
                tutor.PlayTryAgain();
                ui.ShowFeedback(false, displayConfidence * 100f, hint);
                AudioManager.Instance?.Play(gesture.tryAgainAudioKey);
                ghostHand?.Play(gesture.gestureId);
            }

            yield return new WaitForSeconds(feedbackHoldTime);
            ui.HideFeedback();

            if (correct)
            {
                _gestureIndex++;
                if (_gestureIndex >= currentLevel.gestures.Length) CompleteLevel();
                else LoadCurrentGesture();
            }
            else
            {
                LoadCurrentGesture(); // re-demonstrate the same gesture
            }
        }

        private void CompleteLevel()
        {
            float scorePercent = 100f * _correctCount / currentLevel.gestures.Length;
            tutor.PlayComplete();
            // Pass whether a next level exists so the panel can hide its NEXT LEVEL button on
            // the last one rather than offering a button that silently does nothing.
            ui.ShowLevelComplete(currentLevel, scorePercent, NextLevel != null);
            ProgressManager.Instance?.MarkLevelComplete(currentLevel, scorePercent);
            AudioManager.Instance?.Play("LevelComplete");

            var next = NextLevel;
            if (next != null) Debug.Log($"[LessonManager] {next.levelName} is now unlocked -- the NEXT LEVEL button on the panel starts it.");
        }
    }
}
