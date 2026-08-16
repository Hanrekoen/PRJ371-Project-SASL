namespace Convai.Modules.Gaze.Core.Policy
{
    /// <summary>Requires post-expression, two-axis contact to remain stable across rendered frames.</summary>
    internal sealed class GazeSettlementEvaluator
    {
        internal const float ContactToleranceDegrees = 2f;
        internal const int RequiredStableFrames = 3;

        private int _entryId;
        private int _stableFrames;

        public bool Tick(int entryId, bool committed, float contactErrorDegrees)
        {
            if (entryId <= 0 || !committed || float.IsNaN(contactErrorDegrees) ||
                contactErrorDegrees > ContactToleranceDegrees)
            {
                Reset();
                return false;
            }

            if (_entryId != entryId)
            {
                _entryId = entryId;
                _stableFrames = 0;
            }

            _stableFrames++;
            return _stableFrames >= RequiredStableFrames;
        }

        public void Reset()
        {
            _entryId = 0;
            _stableFrames = 0;
        }
    }
}
