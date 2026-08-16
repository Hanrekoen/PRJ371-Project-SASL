using System;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     A locomotion clip slot: the clip plus the motion metadata the runtime needs to
    ///     keep it synchronized with the NavMeshAgent. Empty slots are legal — features that
    ///     depend on a missing slot degrade to simple blending and log the fallback once.
    /// </summary>
    [Serializable]
    public sealed class LocomotionClip
    {
        [SerializeField] private AnimationClip _clip;
        [SerializeField] private ClipMotionMetadata _metadata = new();

        public AnimationClip Clip => _clip;
        public ClipMotionMetadata Metadata => _metadata;

        public bool IsValid => _clip != null;

        /// <summary>Clip length in seconds, 0 when the slot is empty.</summary>
        public float Length => _clip != null ? _clip.length : 0f;

        public string ClipName => _clip != null ? _clip.name : "(none)";

        internal void Initialize(AnimationClip clip)
        {
            _clip = clip;
        }
    }
}
