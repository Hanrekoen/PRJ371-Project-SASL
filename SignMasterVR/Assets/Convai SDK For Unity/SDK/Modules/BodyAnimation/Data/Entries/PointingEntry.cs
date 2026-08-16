using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     One directional pointing clip. Direction is expressed in character-local degrees:
    ///     yaw 0 = forward, +90 = right, −90 = left, ±180 = behind; pitch + = up, − = down.
    ///     The pointing layer picks the entry whose direction is angularly closest to the
    ///     requested target.
    /// </summary>
    [Serializable]
    public sealed class PointingEntry
    {
        [SerializeField]
        [Tooltip("Point gesture clip (raise → hold apex → lower).")]
        private AnimationClip _clip;

        [SerializeField]
        [Range(-180f, 180f)]
        [Tooltip("Character-local yaw the clip points at. 0 forward, +right, −left.")]
        private float _yawDegrees;

        [SerializeField]
        [Range(-90f, 90f)]
        [Tooltip("Character-local pitch the clip points at. + up, − down.")]
        private float _pitchDegrees;

        public AnimationClip Clip => _clip;
        public float YawDegrees => _yawDegrees;
        public float PitchDegrees => _pitchDegrees;

        public bool IsValid => _clip != null;

        /// <summary>Squared angular distance (deg²) between this entry and a requested direction.</summary>
        public float AngularCost(float yawDegrees, float pitchDegrees)
        {
            float yawDelta = Mathf.DeltaAngle(_yawDegrees, yawDegrees);
            float pitchDelta = _pitchDegrees - pitchDegrees;
            return yawDelta * yawDelta + pitchDelta * pitchDelta;
        }

        internal void Initialize(AnimationClip clip, float yawDegrees, float pitchDegrees)
        {
            _clip = clip;
            _yawDegrees = yawDegrees;
            _pitchDegrees = pitchDegrees;
        }
    }

    /// <summary>
    ///     Pointing content: the directional clip table plus hold behavior. The pointing layer
    ///     freezes the selected clip at <see cref="HoldNormalizedTime" /> while a hold is
    ///     requested, then resumes to play the lower-arm tail.
    /// </summary>
    [Serializable]
    public sealed class PointingSection
    {
        [SerializeField] private List<PointingEntry> _entries = new();

        [SerializeField]
        [Range(0.05f, 0.95f)]
        [Tooltip("Normalized clip time where the arm is at its apex; playback holds here " +
                 "while pointing is sustained.")]
        private float _holdNormalizedTime = 0.5f;

        public IReadOnlyList<PointingEntry> Entries => _entries;
        public float HoldNormalizedTime => _holdNormalizedTime;

        public bool HasAny
        {
            get
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].IsValid) return true;
                }
                return false;
            }
        }

        /// <summary>
        ///     Returns the valid entry angularly closest to the requested direction, or
        ///     <c>null</c> when the section is empty.
        /// </summary>
        public PointingEntry FindClosest(float yawDegrees, float pitchDegrees)
        {
            PointingEntry best = null;
            float bestCost = float.MaxValue;

            for (int i = 0; i < _entries.Count; i++)
            {
                PointingEntry entry = _entries[i];
                if (!entry.IsValid) continue;

                float cost = entry.AngularCost(yawDegrees, pitchDegrees);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = entry;
                }
            }

            return best;
        }

        internal void Add(PointingEntry entry)
        {
            if (entry != null) _entries.Add(entry);
        }

        internal void Clear() => _entries.Clear();
    }
}
