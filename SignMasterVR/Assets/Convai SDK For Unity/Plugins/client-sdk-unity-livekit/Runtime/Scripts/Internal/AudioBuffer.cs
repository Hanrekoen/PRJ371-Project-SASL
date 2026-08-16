using System;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    /// A ring buffer that converts float Unity audio data into 16-bit PCM frames.
    /// </summary>
    internal sealed class AudioBuffer : IDisposable
    {
        private readonly uint _bufferDurationMs;
        private RingBuffer _buffer;
        private short[] _conversionBuffer;
        private uint _channels;
        private uint _sampleRate;

        internal AudioBuffer(uint bufferDurationMs = 200)
        {
            _bufferDurationMs = bufferDurationMs;
        }

        internal void Write(float[] data, uint channels, uint sampleRate)
        {
            if (data == null || data.Length == 0)
                return;

            static short FloatToS16(float value)
            {
                value *= 32768f;
                value = Math.Min(value, 32767f);
                value = Math.Max(value, -32768f);
                return (short)(value + Math.Sign(value) * 0.5f);
            }

            if (_conversionBuffer == null || _conversionBuffer.Length < data.Length)
                _conversionBuffer = new short[data.Length];

            for (int i = 0; i < data.Length; i++)
                _conversionBuffer[i] = FloatToS16(data[i]);

            Capture(_conversionBuffer, data.Length, channels, sampleRate);
        }

        internal AudioFrame ReadDuration(uint durationMs)
        {
            if (_buffer == null)
                return null;

            uint samplesPerChannel = (uint)(_sampleRate * (durationMs / 1000f));
            int requiredLength = (int)(samplesPerChannel * _channels * sizeof(short));
            if (_buffer.AvailableRead() < requiredLength)
                return null;

            var frame = new AudioFrame(_sampleRate, _channels, samplesPerChannel);
            unsafe
            {
                var frameData = new Span<byte>(frame.Data.ToPointer(), frame.Length);
                _buffer.Read(frameData);
            }

            return frame;
        }

        public void Dispose()
        {
            _buffer?.Dispose();
            _buffer = null;
            _conversionBuffer = null;
        }

        internal void Clear()
        {
            _buffer?.Dispose();
            _buffer = null;
        }

        private void Capture(short[] data, int sampleCount, uint channels, uint sampleRate)
        {
            if (_buffer == null || channels != _channels || sampleRate != _sampleRate)
            {
                int size = (int)(channels * sampleRate * (_bufferDurationMs / 1000f));
                _buffer?.Dispose();
                _buffer = new RingBuffer(size * sizeof(short));
                _channels = channels;
                _sampleRate = sampleRate;
            }

            unsafe
            {
                fixed (short* dataPtr = data)
                {
                    var bytes = new ReadOnlySpan<byte>(dataPtr, sampleCount * sizeof(short));
                    _buffer.Write(bytes);
                }
            }
        }
    }
}
