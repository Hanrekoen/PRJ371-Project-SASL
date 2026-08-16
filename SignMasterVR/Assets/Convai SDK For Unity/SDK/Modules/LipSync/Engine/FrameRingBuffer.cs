using System;
using System.Collections.Generic;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Ring buffer storing timestamped blendshape frames for interpolation sampling.
    ///     Provides 4-frame windows for Catmull-Rom interpolation.
    ///     Pure C# -- no UnityEngine dependency.
    /// </summary>
    internal sealed class FrameRingBuffer
    {
        private const int InitialCapacity = 128;
        private const int ShrinkCheckInterval = 64;
        private const float MaxIndexedResponseSeconds = 300f;
        private const int IndexedInterpolationGuardFrames = 2;
        private int _appendsSinceShrinkCheck;

        private float[][] _frames = new float[InitialCapacity][];
        private int _head;
        private float[] _times = new float[InitialCapacity];

        public bool HasContent => FrameCount > 0;
        public int FrameCount { get; private set; }

        public int ChannelCount { get; private set; }

        public IReadOnlyList<string> ChannelNames { get; private set; } = Array.Empty<string>();

        public float StartTime => FrameCount > 0 ? _times[_head] : 0f;

        public float EndTime
        {
            get
            {
                if (FrameCount <= 0) return 0f;
                int lastPhysical = PhysicalIndex(FrameCount - 1);
                return _times[lastPhysical];
            }
        }

        public float Duration => FrameCount > 0 ? Math.Max(0f, EndTime - StartTime) : 0f;

        public void Clear()
        {
            _head = 0;
            FrameCount = 0;
            _appendsSinceShrinkCheck = 0;
        }

        public void SetChannelLayout(IReadOnlyList<string> channelNames)
        {
            if (channelNames == null || channelNames.Count == 0)
            {
                ChannelNames = Array.Empty<string>();
                ChannelCount = 0;
                Clear();
                return;
            }

            if (!IsSameLayout(ChannelNames, channelNames)) Clear();

            ChannelNames = channelNames;
            ChannelCount = channelNames.Count;
        }

        /// <summary>
        ///     Appends frames with computed timestamps: startTime + index / frameRate.
        ///     Copies frame values into reusable internal arrays.
        /// </summary>
        public void AppendFrames(
            float[][] frames,
            float startTimeSeconds,
            float frameRate,
            float maxBufferedSeconds,
            bool retainFutureFrames = false)
        {
            if (frames == null || frames.Length == 0 || frameRate <= 0f) return;

            int maxFrames = retainFutureFrames
                ? Math.Max(1, (int)Math.Ceiling(MaxIndexedResponseSeconds * frameRate) +
                              IndexedInterpolationGuardFrames)
                : Math.Max(1, (int)Math.Ceiling(maxBufferedSeconds * frameRate));
            float interval = 1f / frameRate;

            EnsureCapacity(Math.Min(maxFrames, FrameCount + frames.Length));

            for (int i = 0; i < frames.Length; i++)
            {
                float[] frame = frames[i];
                if (frame == null) continue;

                float time = startTimeSeconds + (i * interval);
                PushFrame(frame, time, maxFrames);
            }
        }

        /// <summary>
        ///     Drops frames from the tail whose timestamp is after the cutoff.
        ///     Returns the number of frames removed. Used when the server cancels a turn but the
        ///     released tail up to the cutoff should keep playing.
        /// </summary>
        public int TruncateAfter(float cutoffTime)
        {
            int removed = 0;
            while (FrameCount > 0)
            {
                int lastPhysical = PhysicalIndex(FrameCount - 1);
                if (_times[lastPhysical] <= cutoffTime) break;

                FrameCount--;
                removed++;
            }

            return removed;
        }

        public int PruneBefore(float cutoffTime)
        {
            int removed = 0;

            while (FrameCount > 4)
            {
                int nextHead = PhysicalIndex(1);
                if (_times[nextHead] >= cutoffTime) break;

                _head = nextHead;
                FrameCount--;
                removed++;
            }

            return removed;
        }

        /// <summary>
        ///     Gets a 4-frame window for Catmull-Rom interpolation.
        ///     p0, p1 are the pre-segment and segment-start; p2 is segment-end; p3 is post-segment.
        ///     Missing neighbors at buffer edges are clamped to the nearest available frame.
        /// </summary>
        public bool TryGetFrameWindow(
            double elapsed,
            out float[] p0, out float[] p1, out float[] p2, out float[] p3,
            out float alpha)
        {
            p0 = p1 = p2 = p3 = null;
            alpha = 0f;

            if (FrameCount == 0) return false;

            float t = (float)Math.Max(0d, elapsed);
            int segStart = FindSegmentStart(t);
            int i1 = segStart;
            int i2 = Math.Min(segStart + 1, FrameCount - 1);
            int i0 = Math.Max(0, i1 - 1);
            int i3 = Math.Min(i2 + 1, FrameCount - 1);

            p0 = _frames[PhysicalIndex(i0)];
            p1 = _frames[PhysicalIndex(i1)];
            p2 = _frames[PhysicalIndex(i2)];
            p3 = _frames[PhysicalIndex(i3)];

            if (p1 == null || p2 == null) return false;

            // Clamp neighbors at buffer boundaries.
            p0 ??= p1;
            p3 ??= p2;

            float t1 = _times[PhysicalIndex(i1)];
            float t2 = _times[PhysicalIndex(i2)];
            float span = t2 - t1;
            alpha = span > LipSyncConstants.MinFrameSpanSeconds
                ? Math.Clamp((t - t1) / span, 0f, 1f)
                : i1 == i2
                    ? 0f
                    : 1f;

            return true;
        }

        /// <summary>Binary search for the logical index of the last frame with time &lt;= t.</summary>
        private int FindSegmentStart(float t)
        {
            if (FrameCount <= 1) return 0;

            float firstTime = _times[_head];
            if (t <= firstTime) return 0;

            int lastLogical = FrameCount - 1;
            float lastTime = _times[PhysicalIndex(lastLogical)];
            if (t >= lastTime) return lastLogical;

            int lo = 0;
            int hi = lastLogical;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                float midTime = _times[PhysicalIndex(mid)];
                if (midTime <= t)
                    lo = mid;
                else
                    hi = mid;
            }

            return lo;
        }

        private void PushFrame(float[] values, float time, int? maxFrames)
        {
            if (maxFrames.HasValue && FrameCount >= maxFrames.Value)
            {
                _head = (_head + 1) % _frames.Length;
                FrameCount--;
            }

            int index = PhysicalIndex(FrameCount);

            if (_frames[index] == null || _frames[index].Length != values.Length)
                _frames[index] = new float[values.Length];

            Array.Copy(values, _frames[index], values.Length);

            _times[index] = time;
            FrameCount++;

            _appendsSinceShrinkCheck++;
            if (_appendsSinceShrinkCheck >= ShrinkCheckInterval)
            {
                _appendsSinceShrinkCheck = 0;
                if (maxFrames.HasValue) TryShrink(maxFrames.Value);
            }
        }

        private int PhysicalIndex(int logicalIndex) => (_head + logicalIndex) % _frames.Length;

        private void EnsureCapacity(int required)
        {
            if (_frames.Length >= required) return;

            int newCap = _frames.Length;
            while (newCap < required) newCap *= 2;

            Resize(newCap);
        }

        private void TryShrink(int maxFrames)
        {
            if (_frames.Length <= InitialCapacity) return;

            int desired = Math.Max(InitialCapacity, NextPow2(Math.Max(maxFrames, FrameCount + 1)));
            if (desired >= _frames.Length || FrameCount > desired / 2) return;

            Resize(desired);
        }

        private void Resize(int newCapacity)
        {
            float[][] newFrames = new float[newCapacity][];
            float[] newTimes = new float[newCapacity];
            for (int i = 0; i < FrameCount; i++)
            {
                int src = PhysicalIndex(i);
                newFrames[i] = _frames[src];
                newTimes[i] = _times[src];
            }

            _frames = newFrames;
            _times = newTimes;
            _head = 0;
        }

        private static int NextPow2(int v)
        {
            if (v <= InitialCapacity) return InitialCapacity;

            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }

        private static bool IsSameLayout(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (ReferenceEquals(a, b)) return true;

            if (a == null || b == null || a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
