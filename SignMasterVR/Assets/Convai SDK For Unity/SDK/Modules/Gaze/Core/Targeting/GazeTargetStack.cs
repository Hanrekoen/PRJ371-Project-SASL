using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Targeting
{
    /// <summary>
    ///     Priority stack of scripted <c>GazeAt</c> requests. Scripted targets sit above all
    ///     provider candidates; within the stack, the highest priority wins and recency
    ///     breaks ties. Entries may carry a hold deadline after which they expire on their
    ///     own.
    /// </summary>
    internal sealed class GazeTargetStack
    {
        internal sealed class Entry
        {
            public int Id;
            public int Priority;
            public Transform Target;
            public Vector3 Point;
            public bool HasTransform;
            public float EngagementOverride;   // < 0 → use state policy engagement

            /// <summary>
            ///     How much of the shift the head should take, 0–1; < 0 → use the state policy's.
            /// </summary>
            /// <remarks>
            ///     The third member of the family this entry already had — how strongly
            ///     (<see cref="EngagementOverride" />), may the body turn
            ///     (<see cref="AllowBodyTurn" />), and now how much of it is the head's. Its
            ///     absence is why an idle glance was performed almost entirely with the eyes:
            ///     the glance's own softness multiplied the Idle policy's head contribution,
            ///     authored for ambient drifting, and the product was a number nobody chose.
            /// </remarks>
            public float HeadContributionOverride;

            public bool AllowBodyTurn;
            public float Deadline;             // PositiveInfinity → until released
            public string Name;
            public long Sequence;              // recency tiebreaker

            /// <summary>
            ///     Aim offset in the target's local space, so a request can follow a transform
            ///     and still aim where its author meant.
            /// </summary>
            /// <remarks>
            ///     A provider's candidate carries an aim point that is not the transform's origin
            ///     — the player anchor's eye line, most of all, which sits a metre and a half
            ///     above a player rig's root. Re-pushing such a candidate as a glance used to keep
            ///     the transform and throw the point away, so the character followed the player
            ///     correctly and looked at their feet. Zero (the default, and what an ordinary
            ///     <c>GazeAt(transform)</c> supplies) resolves to the transform's position exactly
            ///     as before.
            /// </remarks>
            public Vector3 LocalAimOffset;

            public Vector3 ResolvePoint()
            {
                if (!HasTransform || Target == null) return Point;

                return LocalAimOffset == Vector3.zero
                    ? Target.position
                    : Target.TransformPoint(LocalAimOffset);
            }

            /// <summary>Entry died because its transform was destroyed mid-hold.</summary>
            public bool TargetDestroyed => HasTransform && Target == null;
        }

        private readonly List<Entry> _entries = new(4);
        private int _nextId = 1;
        private long _nextSequence;

        /// <summary>Number of live entries (including not-yet-expired holds).</summary>
        public int Count => _entries.Count;

        public int Push(
            Transform target,
            Vector3 point,
            bool hasTransform,
            int priority,
            float engagementOverride,
            bool allowBodyTurn,
            float deadline,
            string name,
            float headContributionOverride = -1f,
            Vector3 localAimOffset = default)
        {
            var entry = new Entry
            {
                Id = _nextId++,
                Priority = priority,
                Target = target,
                Point = point,
                HasTransform = hasTransform,
                EngagementOverride = engagementOverride,
                HeadContributionOverride = headContributionOverride,
                LocalAimOffset = localAimOffset,
                AllowBodyTurn = allowBodyTurn,
                Deadline = deadline,
                Name = string.IsNullOrEmpty(name) ? (target != null ? target.name : "point") : name,
                Sequence = _nextSequence++
            };
            _entries.Add(entry);
            return entry.Id;
        }

        /// <summary>Removes the entry with <paramref name="id" />. Safe to call twice.</summary>
        public bool Remove(int id)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id != id) continue;
                _entries.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>Whether the entry with <paramref name="id" /> is still live.</summary>
        public bool Contains(int id)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Id == id)
                    return true;
            return false;
        }

        public void Clear() => _entries.Clear();

        /// <summary>
        ///     Removes every entry whose priority is below <paramref name="priorityFloor" />,
        ///     appending the removed ids to <paramref name="removedIds" /> (when provided) and
        ///     returning the removal count. Used by the eye-contact lock to absorb glance-tier
        ///     requests without touching explicit ones.
        /// </summary>
        public int RemoveBelowPriority(int priorityFloor, List<int> removedIds)
        {
            int removed = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (entry.Priority >= priorityFloor) continue;

                removedIds?.Add(entry.Id);
                _entries.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        /// <summary>
        ///     Prunes expired/destroyed entries and returns the winning scripted entry, or
        ///     <c>null</c> when the stack is empty.
        /// </summary>
        public Entry ResolveActive(float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (now >= entry.Deadline || entry.TargetDestroyed)
                    _entries.RemoveAt(i);
            }

            Entry best = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (best == null ||
                    entry.Priority > best.Priority ||
                    (entry.Priority == best.Priority && entry.Sequence > best.Sequence))
                {
                    best = entry;
                }
            }

            return best;
        }
    }
}
