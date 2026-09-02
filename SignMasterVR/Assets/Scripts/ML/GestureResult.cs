namespace SignMasterVR.ML
{
    /// <summary>
    /// The single piece of data that crosses the boundary between the ML team's
    /// gesture recognition model and the Unity lesson logic. Keep this struct
    /// stable — it's the contract the real model will eventually fill in.
    /// </summary>
    public struct GestureResult
    {
        public string GestureId;   // e.g. "HELLO", "A", "1" — must match GestureData.gestureId
        public float Confidence;   // 0..1

        public GestureResult(string gestureId, float confidence)
        {
            GestureId = gestureId;
            Confidence = confidence;
        }
    }
}
