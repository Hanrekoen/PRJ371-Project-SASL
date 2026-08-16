using System;
using System.Collections.Generic;

namespace Convai.Runtime.Vision.Context
{
    /// <summary>
    ///     Parameters for an explicit dynamic-vision trigger sent with
    ///     <see cref="Convai.Runtime.Room.IConvaiRoomConnectionService.TriggerVision" />.
    ///     A trigger asks the backend to attach buffered vision frames to a turn and (depending on
    ///     <see cref="RespondMode" />) invoke the model. When no frame selection is set, the backend
    ///     attaches the latest fresh frames up to its configured frames-per-turn.
    /// </summary>
    public sealed class ConvaiVisionTriggerRequest
    {
        /// <summary>
        ///     Creates a trigger request. When <paramref name="updateId" /> is omitted a unique id is
        ///     generated. Reusing the same id makes the request idempotent: the backend replays the
        ///     original acknowledgement instead of triggering again, so retries are always safe.
        /// </summary>
        public ConvaiVisionTriggerRequest(string updateId = null)
        {
            UpdateId = string.IsNullOrWhiteSpace(updateId) ? Guid.NewGuid().ToString("N") : updateId.Trim();
        }

        /// <summary>Idempotency key echoed back in the acknowledgement's <c>update_id</c>.</summary>
        public string UpdateId { get; }

        /// <summary>
        ///     Optional prompt accompanying the frames (e.g. "What changed on the table?"). When empty
        ///     the backend uses its generic inspect-the-frames prompt.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        ///     How this trigger affects the character's speech. Null uses the connect-time default for
        ///     the trigger lane (see <see cref="ConvaiVisionRespondModeSettings" />).
        /// </summary>
        public ConvaiRespondMode? RespondMode { get; set; }

        /// <summary>
        ///     Start of the relative frame window, when set via <see cref="SetFrameWindow" />.
        ///     Negative values count back from the newest buffered frame (-1 = newest); non-negative
        ///     values are zero-based offsets from the oldest retained frame.
        /// </summary>
        public int? FrameWindowStart { get; private set; }

        /// <summary>End of the relative frame window, when set via <see cref="SetFrameWindow" />. Same addressing as <see cref="FrameWindowStart" />.</summary>
        public int? FrameWindowEnd { get; private set; }

        /// <summary>
        ///     Optional absolute frame selection by presentation timestamp (nanoseconds), as reported in
        ///     acknowledgements (<c>attached_frame_pts</c>) and vision-status responses. Pinned frames are
        ///     exempt from the staleness window; a timestamp that already left the buffer fails the
        ///     trigger with a structured error. Takes precedence over the frame window when both are set.
        /// </summary>
        public IReadOnlyList<long> FramePtsIds { get; set; }

        /// <summary>
        ///     Selects a relative window of buffered frames, e.g. <c>SetFrameWindow(-5, -1)</c> for the
        ///     five most recent. The backend clamps out-of-range indices to the oldest retained frame and
        ///     reports what was actually attached in the acknowledgement.
        /// </summary>
        public void SetFrameWindow(int startIndex, int endIndex)
        {
            FrameWindowStart = startIndex;
            FrameWindowEnd = endIndex;
        }

        /// <summary>Clears a previously set frame window, restoring the default latest-frames selection.</summary>
        public void ClearFrameWindow()
        {
            FrameWindowStart = null;
            FrameWindowEnd = null;
        }
    }
}
