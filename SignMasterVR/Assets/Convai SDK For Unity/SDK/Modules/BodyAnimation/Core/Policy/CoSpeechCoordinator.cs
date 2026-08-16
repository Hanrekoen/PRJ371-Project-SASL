using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Readings;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     The co-speech planner's glance/brow cross-module dispatch — a fresh
    ///     gesture request from <see cref="CoSpeechPerformancePlanner" /> requests a glance (through
    ///     the Gaze module's <see cref="IGazeGlanceHandler" /> seam, when present) and, for the
    ///     emphatic/greeting/enumerate cue kinds, a brow cue (through <see cref="IBrowCueSink" />).
    ///     Owns the sequence latch that turns "the planner still reports a gesture" into "dispatch
    ///     exactly once per newly-resolved gesture".
    /// </summary>
    internal sealed class CoSpeechCoordinator
    {
        private int _lastSequence;

        /// <summary>
        ///     Dispatches the reading's gesture (if any and not already dispatched this sequence).
        ///     A no-op when nothing changed, or when neither handler is registered.
        /// </summary>
        internal void Dispatch(in CoSpeechPerformanceReading reading, IGazeGlanceHandler glanceHandler, IBrowCueSink browCueSink)
        {
            if (!reading.HasGesture || reading.GestureSequence == _lastSequence) return;
            _lastSequence = reading.GestureSequence;

            CoSpeechGestureRequest request = reading.Gesture;
            if (request.HasWorldTarget)
                glanceHandler?.RequestGlance(
                    request.WorldTarget,
                    request.PreparationSeconds + request.StrokeSeconds + request.HoldSeconds);

            if (request.Kind is GestureCueKind.Emphatic or GestureCueKind.Greeting or GestureCueKind.Enumerate)
                browCueSink?.RaiseBrowCue(
                    request.Kind == GestureCueKind.Emphatic ? BrowCueKind.Flash : BrowCueKind.SubtleRaise,
                    request.Intensity);
        }

        /// <summary>Re-arms the latch — call from teardown so the next build's first gesture always dispatches.</summary>
        internal void Reset() => _lastSequence = 0;
    }
}
