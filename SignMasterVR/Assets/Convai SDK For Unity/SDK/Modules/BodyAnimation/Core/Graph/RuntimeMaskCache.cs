using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Graph
{
    /// <summary>
    ///     Ref-counted cache for the runtime <see cref="AvatarMask" /> instances derived by
    ///     <c>TalkLayer</c> and <c>ActionLayer</c>. All four masks (full-body, arms-only, and
    ///     the talk layer's upper-body-minus-spine mask) are deterministic functions of their
    ///     inputs, so per-character construction/destruction is wasted work — this hands out
    ///     one shared instance per distinct input across every character in the scene and only
    ///     destroys it once the last holder releases.
    /// </summary>
    /// <remarks>
    ///     The critical lifecycle case this exists to get right: during a set handoff, two
    ///     layer instances of the same kind are alive at once (the retiring one is torn down
    ///     after the replacement is built). Ref-counting makes the retiring layer's
    ///     <c>Teardown</c> a no-op on a mask the live layer still holds, instead of destroying
    ///     shared state out from under it — the exact class of bug the retiring-layer fix addressed for
    ///     mixers.
    /// </remarks>
    internal static class RuntimeMaskCache
    {
        private sealed class Entry
        {
            public AvatarMask Mask;
            public int RefCount;
        }

        // Full-body and arms masks depend on nothing, so each has exactly one entry.
        private static Entry _fullBodyEntry;
        private static Entry _armsEntry;

        // Talk-upper-body masks depend on the set's UpperBodyMask instance.
        private static readonly Dictionary<AvatarMask, Entry> _talkEntriesBySource = new();

        // Reverse lookup so Release(AvatarMask) works uniformly for all three kinds without
        // the caller having to remember which acquire method produced the instance.
        private static readonly Dictionary<AvatarMask, Entry> _entriesByProducedMask = new();

        /// <summary>Shared default-constructed mask (every humanoid part enabled).</summary>
        internal static AvatarMask AcquireFullBody()
        {
            if (IsAlive(_fullBodyEntry))
            {
                _fullBodyEntry.RefCount++;
                return _fullBodyEntry.Mask;
            }

            PurgeDeadEntry(_fullBodyEntry);
            AvatarMask mask = new() { name = "Convai_FullBody_Runtime" };
            _fullBodyEntry = new Entry { Mask = mask, RefCount = 1 };
            _entriesByProducedMask[mask] = _fullBodyEntry;
            return mask;
        }

        /// <summary>Shared mask with only LeftArm/RightArm humanoid parts enabled.</summary>
        internal static AvatarMask AcquireArms()
        {
            if (IsAlive(_armsEntry))
            {
                _armsEntry.RefCount++;
                return _armsEntry.Mask;
            }

            PurgeDeadEntry(_armsEntry);
            AvatarMask mask = new() { name = "Convai_MovingTalkArms_Runtime" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);

            _armsEntry = new Entry { Mask = mask, RefCount = 1 };
            _entriesByProducedMask[mask] = _armsEntry;
            return mask;
        }

        /// <summary>
        ///     Shared copy of <paramref name="source" /> with the Body humanoid part disabled
        ///     (the talk overlay must never take the torso over). Keyed on the source mask
        ///     instance; a null source returns null, matching the layer's existing behaviour
        ///     when the set has no Upper Body Mask.
        /// </summary>
        internal static AvatarMask AcquireTalkUpperBody(AvatarMask source)
        {
            if (source == null) return null;

            if (_talkEntriesBySource.TryGetValue(source, out Entry entry))
            {
                if (IsAlive(entry))
                {
                    entry.RefCount++;
                    return entry.Mask;
                }

                PurgeDeadEntry(entry);
            }

            AvatarMask mask = Object.Instantiate(source);
            mask.name = "Convai_TalkUpperBody_Runtime";
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);

            entry = new Entry { Mask = mask, RefCount = 1 };
            _talkEntriesBySource[source] = entry;
            _entriesByProducedMask[mask] = entry;
            return mask;
        }

        /// <summary>
        ///     Releases one reference to a mask previously returned by one of the Acquire
        ///     methods. Destroys and removes the entry at zero. A null mask, an unknown mask,
        ///     or a mask whose entry was already destroyed (e.g. by a stray domain reload) is a
        ///     tolerated no-op.
        /// </summary>
        internal static void Release(AvatarMask mask)
        {
            // Plain reference check, not Unity's fake-null: a caller releasing a reference
            // whose native object was already destroyed by something other than this cache
            // (a stray domain reload, an editor script) must still be able to find and clean
            // up its entry — the dictionary keys/looks up by the C# reference's stable
            // instance id, which survives native destruction.
            if (ReferenceEquals(mask, null)) return;
            if (!_entriesByProducedMask.TryGetValue(mask, out Entry entry)) return;

            entry.RefCount--;
            if (entry.RefCount > 0) return;

            _entriesByProducedMask.Remove(mask);
            if (_fullBodyEntry == entry) _fullBodyEntry = null;
            if (_armsEntry == entry) _armsEntry = null;
            RemoveTalkEntry(entry);

            // Unity-aware check: the entry's mask may already be destroyed (see above) —
            // destroying it again is unnecessary and, on some Unity versions, logs a warning.
            if (entry.Mask != null)
                DestroyRuntimeObject(entry.Mask);
            entry.Mask = null;
        }

        /// <summary>
        ///     Drops every trace of an entry whose mask was destroyed by something other than
        ///     <see cref="Release" /> (a stray domain reload, an editor script). Without this the
        ///     dead mask stays a key in <c>_entriesByProducedMask</c> forever: a destroyed
        ///     <see cref="Object" /> still hashes by its instance id, so the dictionary happily
        ///     keeps it and the map grows once per externally-destroyed mask. Rare, but it is a
        ///     leak in the very type whose job is not to leak.
        /// </summary>
        private static void PurgeDeadEntry(Entry entry)
        {
            if (entry == null) return;
            if (!ReferenceEquals(entry.Mask, null))
                _entriesByProducedMask.Remove(entry.Mask);
            RemoveTalkEntry(entry);
            entry.Mask = null;
        }

        private static void RemoveTalkEntry(Entry entry)
        {
            AvatarMask sourceKey = null;
            foreach (KeyValuePair<AvatarMask, Entry> pair in _talkEntriesBySource)
            {
                if (pair.Value != entry) continue;
                sourceKey = pair.Key;
                break;
            }

            if (!ReferenceEquals(sourceKey, null))
                _talkEntriesBySource.Remove(sourceKey);
        }

        // Unity's `== null` (not ReferenceEquals) treats a native-destroyed object as null,
        // which is exactly the check needed to tolerate an entry whose mask was destroyed by
        // something other than Release (a stray domain reload, an editor script, etc.).
        private static bool IsAlive(Entry entry) => entry != null && entry.Mask != null;

        /// <summary>Destroys a runtime-created object safely in both Play Mode and EditMode tests.</summary>
        private static void DestroyRuntimeObject(Object obj)
        {
            if (UnityEngine.Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        /// <summary>
        ///     Clears every cached entry on play-mode entry. The cache is static and would
        ///     otherwise survive a domain reload holding references to masks Unity already
        ///     destroyed (or, in a no-domain-reload editor setting, correctly-alive masks from
        ///     a previous play session that no character has re-acquired yet) — either way,
        ///     starting empty is the only safe state. Matches the precedent in
        ///     <c>LipSyncProfileCatalog.DomainReload</c>.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            _fullBodyEntry = null;
            _armsEntry = null;
            _talkEntriesBySource.Clear();
            _entriesByProducedMask.Clear();
        }
    }
}
