using UnityEngine;

namespace Convai.Runtime.Animation.ProceduralPose
{
    /// <summary>
    ///     Makes swing-only additive bone writes idempotent when no animation source re-poses
    ///     the skeleton between ticks. A solver/compositor layer computes its delta on top of
    ///     whatever pose the frame's Animator/PlayableGraph (or a static bind pose) already
    ///     holds; with nothing rewriting the bones every frame, last frame's delta would still
    ///     be sitting on the bone and the next write would integrate on top of it. The guard
    ///     remembers exactly what was written and, before the next write, unwinds any bone that
    ///     still holds that exact value (i.e. nothing re-posed it), restoring the underlying
    ///     pose the delta was computed against.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unified Runtime-owned port of the gaze module's <c>AnimatedPoseWriteGuard</c>
    ///         and the (now retired) BodyLanguage-private chain write guard — module isolation
    ///         forbids either module referencing the other's guard directly, so this single
    ///         type under <c>Convai.Runtime.Animation.ProceduralPose</c> is the shared recipe
    ///         <see cref="ProceduralPoseCompositor" /> (and any future guarded additive writer)
    ///         uses. Detection is an exact component comparison of the cached post-write local
    ///         rotation: a fresh animator write never bit-matches the previous composite, so
    ///         animated characters are left untouched and the guard only engages on genuinely
    ///         static skeletons.
    ///     </para>
    ///     <para>
    ///         One instance is shared by every writer that touches the guarded bone set and is
    ///         owned and restored exactly once per frame by that set's single owner, before any
    ///         writer's pose is read or written that frame. <see cref="Record" /> is per-bone,
    ///         not per-write: if a later writer in the same frame writes a bone another writer
    ///         already recorded, the entry's post-write value is updated to that writer's
    ///         result but the ORIGINAL pre-write value (the true underlying animated/static
    ///         pose, captured by the first writer) is kept. That is what makes multiple writers
    ///         composing onto the same bone in one frame safe: restoring always unwinds back to
    ///         the pose that existed before any writer touched the bone this frame, never to an
    ///         intermediate composite.
    ///     </para>
    /// </remarks>
    internal sealed class AnimatedAdditivePoseGuard
    {
        // Spine, Chest, UpperChest, LeftShoulder, RightShoulder, Neck, Head, Hips, and the six
        // leg-chain bones (the BodyPose slot's full guarded set) is exactly 14;
        // 18 keeps real headroom so a future guarded writer can never silently overflow into an
        // UN-guarded write (a dropped Record would integrate on static rigs), without
        // meaningfully growing the fixed-size per-frame scan.
        private const int Capacity = 18;

        // Position entries: Hips is the only bone the compositor writes
        // a local POSITION for today (the pelvis lateral weight-shift translation); capacity 2
        // is a small, deliberate headroom over that single writer.
        private const int PositionCapacity = 2;

        private readonly Transform[] _bones = new Transform[Capacity];
        private readonly Quaternion[] _preWrite = new Quaternion[Capacity];
        private readonly Quaternion[] _postWrite = new Quaternion[Capacity];
        private int _count;

        private readonly Transform[] _positionBones = new Transform[PositionCapacity];
        private readonly Vector3[] _preWritePosition = new Vector3[PositionCapacity];
        private readonly Vector3[] _postWritePosition = new Vector3[PositionCapacity];
        private int _positionCount;

        /// <summary>
        ///     Records that a writer just wrote <paramref name="bone" /> this frame.
        ///     <paramref name="preWriteLocalRotation" /> is the local rotation the bone held
        ///     before THIS write. If the bone was already recorded earlier this frame (by this
        ///     or another writer), only the cached post-write value is refreshed to the bone's
        ///     current (post-write) rotation — the first writer's pre-write value is kept, since
        ///     that is the true underlying pose every writer's delta this frame was layered onto.
        /// </summary>
        public void Record(Transform bone, Quaternion preWriteLocalRotation)
        {
            if (bone == null) return;

            for (int i = 0; i < _count; i++)
            {
                if (_bones[i] != bone) continue;
                _postWrite[i] = bone.localRotation;
                return;
            }

            if (_count >= Capacity) return;

            _bones[_count] = bone;
            _preWrite[_count] = preWriteLocalRotation;
            _postWrite[_count] = bone.localRotation;
            _count++;
        }

        /// <summary>
        ///     Records that a writer just wrote <paramref name="bone" />'s local POSITION this
        ///     frame — the pelvis lateral weight-shift translation. Same
        ///     first-pre-wins/update-post semantics as <see cref="Record" />, kept as a separate
        ///     small fixed-size table since only one bone (Hips) is ever position-written today.
        /// </summary>
        public void RecordPosition(Transform bone, Vector3 preWriteLocalPosition)
        {
            if (bone == null) return;

            for (int i = 0; i < _positionCount; i++)
            {
                if (_positionBones[i] != bone) continue;
                _postWritePosition[i] = bone.localPosition;
                return;
            }

            if (_positionCount >= PositionCapacity) return;

            _positionBones[_positionCount] = bone;
            _preWritePosition[_positionCount] = preWriteLocalPosition;
            _postWritePosition[_positionCount] = bone.localPosition;
            _positionCount++;
        }

        /// <summary>
        ///     Unwinds last frame's writes on every bone no animation source re-posed since,
        ///     then forgets them. Call exactly once per frame, owned by the single owner that
        ///     shares this guard across writers, before any writer's pose is read or written
        ///     (i.e. before the first write in the frame's protocol). Walks both the rotation and
        ///     position entries.
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

            for (int i = 0; i < _positionCount; i++)
            {
                Transform bone = _positionBones[i];
                _positionBones[i] = null;
                if (bone == null) continue;

                Vector3 current = bone.localPosition;
                Vector3 written = _postWritePosition[i];
                if (current.x == written.x && current.y == written.y && current.z == written.z)
                {
                    bone.localPosition = _preWritePosition[i];
                }
            }

            _positionCount = 0;
        }
    }
}
