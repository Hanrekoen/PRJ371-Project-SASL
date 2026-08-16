using UnityEngine;

namespace Convai.Modules.Gaze.Core.Solvers
{
    /// <summary>
    ///     Makes the gaze chain's post-animator bone writes idempotent when no animation
    ///     source re-poses the skeleton. The solvers layer swing deltas ON TOP of the
    ///     frame's animated pose, which assumes something (Body Animation's PlayableGraph,
    ///     a plain Animator, Timeline…) rewrites the bones every frame; with all of them
    ///     absent or disabled, last frame's delta would still be on the bone and the next
    ///     write would integrate on top of it — a runaway head spin. The guard remembers
    ///     exactly what the solver wrote and, before the next solve, unwinds any bone that
    ///     still holds that exact value (i.e. nothing re-posed it), restoring the underlying
    ///     pose the delta was computed against.
    /// </summary>
    /// <remarks>
    ///     Detection is an exact component comparison of the cached post-write local
    ///     rotation: a fresh animator write never bit-matches the solver's composed value
    ///     (the delta would have to be identity, where restoring is a no-op anyway), so
    ///     animated characters are left untouched and the guard only engages on genuinely
    ///     static skeletons. Local rotations are used throughout, which keeps entries
    ///     independent of each other's restore order along the parent chain.
    /// </remarks>
    internal sealed class AnimatedPoseWriteGuard
    {
        private const int Capacity = 4; // chest, upper chest, neck, head

        private readonly Transform[] _bones = new Transform[Capacity];
        private readonly Quaternion[] _preWrite = new Quaternion[Capacity];
        private readonly Quaternion[] _postWrite = new Quaternion[Capacity];
        private int _count;

        /// <summary>
        ///     Records that the solver just wrote <paramref name="bone" /> this frame.
        ///     <paramref name="preWriteLocalRotation" /> is the local rotation the bone held
        ///     before the write (the animated pose the delta was layered onto).
        /// </summary>
        public void Record(Transform bone, Quaternion preWriteLocalRotation)
        {
            if (bone == null || _count >= Capacity) return;

            _bones[_count] = bone;
            _preWrite[_count] = preWriteLocalRotation;
            _postWrite[_count] = bone.localRotation;
            _count++;
        }

        /// <summary>
        ///     Unwinds last frame's writes on every bone no animation source re-posed since,
        ///     then forgets them. Call once per solve, before any pose is read or written.
        /// </summary>
        public void RestoreStaleWrites()
        {
            for (int i = 0; i < _count; i++)
            {
                Transform bone = _bones[i];
                _bones[i] = null;
                if (bone == null) continue;

                Quaternion current = bone.localRotation;
                Quaternion written = _postWrite[i];
                if (current.x == written.x && current.y == written.y &&
                    current.z == written.z && current.w == written.w)
                {
                    bone.localRotation = _preWrite[i];
                }
            }

            _count = 0;
        }
    }
}
