using System;
using System.Collections.Generic;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     A zero-allocation, read-only snapshot of the current blendshape output values.
    ///     References the engine's internal arrays directly with no copies.
    /// </summary>
    /// <remarks>
    ///     This struct is only valid for the current frame. The underlying arrays are
    ///     mutated in-place by the engine on each tick. Do not store across frames.
    /// </remarks>
    public readonly struct BlendshapeSnapshot
    {
        private readonly float[] _values;
        private readonly IReadOnlyList<string> _names;

        /// <summary>Number of blendshape channels in this snapshot.</summary>
        public readonly int Count;

        internal BlendshapeSnapshot(float[] values, IReadOnlyList<string> names)
        {
            _values = values;
            _names = names;
            Count = values != null && names != null
                ? Math.Min(values.Length, names.Count)
                : 0;
        }

        /// <summary>Whether this snapshot contains valid data.</summary>
        public bool IsValid => Count > 0;

        /// <summary>Get the blendshape value at the given channel index.</summary>
        /// <param name="index">Channel index (0-based).</param>
        public float GetValue(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));

            return _values[index];
        }

        /// <summary>Get the channel name at the given index.</summary>
        /// <param name="index">Channel index (0-based).</param>
        public string GetName(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));

            return _names[index];
        }

        /// <summary>
        ///     Looks up a blendshape value by channel name. O(n) linear scan.
        ///     For bulk access, iterate by index instead.
        /// </summary>
        public bool TryGetValue(string channelName, out float value)
        {
            if (_names != null && _values != null)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (string.Equals(_names[i], channelName, StringComparison.Ordinal))
                    {
                        value = _values[i];
                        return true;
                    }
                }
            }

            value = 0f;
            return false;
        }

        /// <summary>
        ///     Copy all values to the provided buffer. Returns the number of values copied.
        /// </summary>
        public int CopyValuesTo(float[] destination)
        {
            if (destination == null || Count == 0) return 0;
            int count = Math.Min(Count, destination.Length);
            Array.Copy(_values, destination, count);
            return count;
        }

        /// <summary>Returns a non-allocating enumerator for use with foreach.</summary>
        public Enumerator GetEnumerator() => new(_values, _names, Count);

        /// <summary>
        ///     Non-allocating enumerator. Supports the foreach pattern without IEnumerable boxing.
        ///     Yields <see cref="KeyValuePair{TKey,TValue}" /> pairs of (channelName, value).
        /// </summary>
        public struct Enumerator
        {
            private readonly float[] _values;
            private readonly IReadOnlyList<string> _names;
            private readonly int _count;
            private int _index;

            internal Enumerator(float[] values, IReadOnlyList<string> names, int count)
            {
                _values = values;
                _names = names;
                _count = count;
                _index = -1;
            }

            public bool MoveNext() => ++_index < _count;

            public KeyValuePair<string, float> Current
            {
                get
                {
                    if ((uint)_index >= (uint)_count)
                        throw new InvalidOperationException("Enumerator is not positioned on a valid element.");

                    return new KeyValuePair<string, float>(_names[_index], _values[_index]);
                }
            }
        }
    }
}
