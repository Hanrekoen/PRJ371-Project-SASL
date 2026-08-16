namespace Convai.Runtime.Vision.Sources.Internal
{
    internal readonly struct VisionFrameHealthSample
    {
        public VisionFrameHealthSample(
            bool isHealthy,
            int consecutiveInvalidFrames,
            bool shouldWarn,
            bool shouldEscalateError)
        {
            IsHealthy = isHealthy;
            ConsecutiveInvalidFrames = consecutiveInvalidFrames;
            ShouldWarn = shouldWarn;
            ShouldEscalateError = shouldEscalateError;
        }

        public bool IsHealthy { get; }

        public int ConsecutiveInvalidFrames { get; }

        public bool ShouldWarn { get; }

        public bool ShouldEscalateError { get; }
    }

    internal sealed class VisionFrameHealthMonitor
    {
        private readonly int _escalationThreshold;
        private readonly int _warningThreshold;
        private bool _warnedForCurrentInvalidBurst;

        public VisionFrameHealthMonitor(
            int warningThreshold,
            int escalationThreshold)
        {
            _warningThreshold = warningThreshold;
            _escalationThreshold = escalationThreshold;
            Reset();
        }

        public bool IsHealthy { get; private set; }

        public int ConsecutiveInvalidFrames { get; private set; }

        public void Reset()
        {
            IsHealthy = false;
            ConsecutiveInvalidFrames = 0;
            _warnedForCurrentInvalidBurst = false;
        }

        public VisionFrameHealthSample RegisterSample(bool? isUsable)
        {
            if (!isUsable.HasValue)
            {
                return new VisionFrameHealthSample(
                    IsHealthy,
                    ConsecutiveInvalidFrames,
                    false,
                    false);
            }

            if (isUsable.Value)
            {
                IsHealthy = true;
                ConsecutiveInvalidFrames = 0;
                _warnedForCurrentInvalidBurst = false;
                return new VisionFrameHealthSample(true, 0, false, false);
            }

            IsHealthy = false;
            ConsecutiveInvalidFrames++;

            bool shouldWarn = !_warnedForCurrentInvalidBurst && ConsecutiveInvalidFrames >= _warningThreshold;
            if (shouldWarn)
                _warnedForCurrentInvalidBurst = true;

            bool shouldEscalateError = ConsecutiveInvalidFrames >= _escalationThreshold;

            return new VisionFrameHealthSample(
                false,
                ConsecutiveInvalidFrames,
                shouldWarn,
                shouldEscalateError);
        }
    }
}
