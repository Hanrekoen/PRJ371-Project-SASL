namespace SignMasterVR.ML
{
    /// <summary>
    /// The single piece of data that crosses the boundary between the ML team's
    /// gesture recognition model and the Unity lesson logic. Keep this struct
    /// stable — it's the contract the real model will eventually fill in.
    /// </summary>
    public struct GestureResult
    {
        public string GestureId;         // best guess across every sign the model knows, correct or not
        public float Confidence;         // 0..1, probability of GestureId
        public float TargetConfidence;   // 0..1, probability of the sign that was ASKED for (0 if the recognizer doesn't supply one)

        // Kept for FakeGestureRecognizer and any older/placeholder recognizer that
        // has no concept of "the target's own probability" — TargetConfidence is 0,
        // and LessonManager falls back to the old GestureId-match check for those.
        public GestureResult(string gestureId, float confidence)
            : this(gestureId, confidence, 0f) { }

        public GestureResult(string gestureId, float confidence, float targetConfidence)
        {
            GestureId = gestureId;
            Confidence = confidence;
            TargetConfidence = targetConfidence;
        }
    }
}
