using System;

namespace SignMasterVR.ML
{
    /// <summary>
    /// The contract LessonManager talks to. Two implementations exist:
    ///   - FakeGestureRecognizer: dev-time stand-in with "Test Correct" / "Test Wrong" buttons.
    ///   - (later) RealGestureRecognizer: wraps the ML team's model, fed by XR Hands landmark data.
    /// Swapping between them is a one-field change in the Inspector (LessonManager.recognizerBehaviour) —
    /// nothing in LessonManager needs to know which one is active.
    /// </summary>
    public interface IGestureRecognizer
    {
        event Action<GestureResult> OnGestureDetected;
        void SetTargetGesture(string gestureId);
    }
}
