using System;
using UnityEngine;

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
            if (Input.GetKeyDown(KeyCode.C)) TestCorrect();
            if (Input.GetKeyDown(KeyCode.X)) TestWrong();
        }
    }
}
