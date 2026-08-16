using System;
using System.Collections.Generic;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Bounded response-keyed frame counter. Removing a completed response also removes its order node.
    /// </summary>
    internal sealed class BlendshapeFrameStatsTracker
    {
        private readonly int _capacity;
        private readonly Dictionary<string, Entry> _entries;
        private readonly LinkedList<string> _order = new();

        internal BlendshapeFrameStatsTracker(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _entries = new Dictionary<string, Entry>(capacity, StringComparer.Ordinal);
        }

        internal int Count => _entries.Count;
        internal int OrderCount => _order.Count;

        internal void Add(in LipSyncResponseOwner owner, int frameCount)
        {
            if (!owner.HasIdentity || frameCount <= 0) return;

            string key = owner.CanonicalKey;
            if (key.Length == 0) return;

            if (TryFind(owner, out string existingKey, out Entry existing))
            {
                existing.FrameCount += frameCount;
                _entries[existingKey] = existing;
                return;
            }

            while (_entries.Count >= _capacity)
                RemoveOldest();

            LinkedListNode<string> node = _order.AddLast(key);
            _entries.Add(key, new Entry(owner, frameCount, node));
        }

        internal bool TryTake(in LipSyncResponseOwner owner, out int frameCount)
        {
            frameCount = 0;
            if (!TryFind(owner, out string key, out Entry entry)) return false;

            frameCount = entry.FrameCount;
            _entries.Remove(key);
            _order.Remove(entry.OrderNode);
            return true;
        }

        internal void Clear()
        {
            _entries.Clear();
            _order.Clear();
        }

        private void RemoveOldest()
        {
            LinkedListNode<string> first = _order.First;
            if (first == null) return;
            _order.RemoveFirst();
            _entries.Remove(first.Value);
        }

        private bool TryFind(in LipSyncResponseOwner owner, out string key, out Entry entry)
        {
            key = owner.CanonicalKey;
            if (key.Length == 0)
            {
                entry = default;
                return false;
            }

            if (_entries.TryGetValue(key, out entry)) return true;

            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (!pair.Value.Owner.Matches(owner)) continue;
                key = pair.Key;
                entry = pair.Value;
                return true;
            }

            entry = default;
            return false;
        }

        private struct Entry
        {
            internal Entry(
                in LipSyncResponseOwner owner,
                int frameCount,
                LinkedListNode<string> orderNode)
            {
                Owner = owner;
                FrameCount = frameCount;
                OrderNode = orderNode;
            }

            internal readonly LipSyncResponseOwner Owner;
            internal int FrameCount;
            internal readonly LinkedListNode<string> OrderNode;
        }
    }
}
