using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SignMasterVR.ML
{
    /// <summary>
    /// PHASE 15 — "Fake the ML system first."
    /// TestCorrect()/TestWrong() are wired to on-screen debug buttons by the
    /// Tools > SignMasterVR > 4 Wire Up Current Scene menu item, and to the
    /// keyboard (C / X) for even faster iteration. Delete or disable this
    /// component once the real recognizer exists — nothing else changes.
    /// </summary>
    public class FakeGestureRecognizer : MonoBehaviour, IGestureRecognizer
    {
        public event Action<GestureResult> OnGestureDetected;

        [Range(0f, 1f)] public float correctConfidence = 0.95f;
        [Range(0f, 1f)] public float wrongConfidence = 0.4f;
        public bool enableKeyboardShortcuts = true;

        private string _currentTarget;

        public void SetTargetGesture(string gestureId) => _currentTarget = gestureId;

        public void TestCorrect() => OnGestureDetected?.Invoke(new GestureResult(_currentTarget, correctConfidence));

        public void TestWrong() => OnGestureDetected?.Invoke(new GestureResult("WRONG_" + _currentTarget, wrongConfidence));

        private void Update()
        {
            if (!enableKeyboardShortcuts) return;
#if ENABLE_INPUT_SYSTEM
            // This project's Player Settings (Edit > Project Settings > Player >
            // Active Input Handling) use the new Input System exclusively, so the
            // legacy UnityEngine.Input class throws InvalidOperationException on
            // every call — that's what was flooding the Console. ENABLE_INPUT_SYSTEM
            // is a scripting define Unity sets automatically whenever the Input
            // System package is active, so this picks the right API without
            // needing any Project Settings change.
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.cKey.wasPressedThisFrame) TestCorrect();
            if (kb.xKey.wasPressedThisFrame) TestWrong();
#else
            if (Input.GetKeyDown(KeyCode.C)) TestCorrect();
            if (Input.GetKeyDown(KeyCode.X)) TestWrong();
#endif
        }
    }
}
