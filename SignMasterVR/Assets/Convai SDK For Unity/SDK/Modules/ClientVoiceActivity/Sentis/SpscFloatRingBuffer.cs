using System;
using System.Threading;

namespace Convai.Modules.ClientVoiceActivity.Sentis
{
    /// <summary>
    ///     Fixed-capacity, single-producer/single-consumer buffer. The microphone callback
    ///     only writes primitive values and never allocates or waits on a lock.
    /// </summary>
    internal sealed class SpscFloatRingBuffer
    {
        private readonly float[] _buffer;
        private readonly int _mask;
        private int _readIndex;
        private int _writeIndex;

        internal SpscFloatRingBuffer(int capacity)
        {
            if (capacity < 2 || (capacity & (capacity - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");

            _buffer = new float[capacity];
            _mask = capacity - 1;
        }

        internal int Count
        {
            get
            {
                int write = Volatile.Read(ref _writeIndex);
                int read = Volatile.Read(ref _readIndex);
                return (write - read) & _mask;
            }
        }

        internal bool TryWrite(float sample)
        {
            int write = _writeIndex;
            int next = (write + 1) & _mask;
            if (next == Volatile.Read(ref _readIndex))
                return false;

            _buffer[write] = sample;
            Volatile.Write(ref _writeIndex, next);
            return true;
        }

        internal bool TryRead(out float sample)
        {
            int read = _readIndex;
            if (read == Volatile.Read(ref _writeIndex))
            {
                sample = 0f;
                return false;
            }

            sample = _buffer[read];
            Volatile.Write(ref _readIndex, (read + 1) & _mask);
            return true;
        }

        internal void Clear() =>
            Volatile.Write(ref _readIndex, Volatile.Read(ref _writeIndex));
    }
}
