using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     A named, ordered collection of <see cref="ConvaiActionTarget" /> members that the AI
    ///     can address as a single unit — "the paintings", "the row of exhibits" — instead of one
    ///     target at a time. Drag-drop, no code required (mirrors <see cref="ConvaiActionTarget" />'s
    ///     poll-based registration precedent). While enabled, the group's name resolves through
    ///     the same target resolution ladder as an authored object (see
    ///     <see cref="ConvaiResolvedActionTarget" />): the resolved entry's
    ///     <see cref="ConvaiResolvedActionTarget.GameObjectReference" /> is this component's
    ///     GameObject, so a consumer resolves the target by name and then reads
    ///     <see cref="Members" />/<see cref="IsOrdered" /> off the same GameObject.
    /// </summary>
    /// <remarks>
    ///     Enumeration/late-enable follows the exact same poll-at-read-time pattern as
    ///     <see cref="ConvaiActionTarget" />: this component keeps its own static
    ///     <see cref="ActiveGroups" /> list rather than pushing itself into every character's
    ///     registry, so a group enabling after characters exist (or vice versa) is trivially
    ///     correct with no bidirectional bookkeeping.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Convai Action Target Group")]
    [DisallowMultipleComponent]
    public sealed class ConvaiActionTargetGroup : MonoBehaviour
    {
        private static readonly List<ConvaiActionTargetGroup> Registry = new(8);

        /// <summary>Enabled groups in the scene (polled by <c>ConvaiCharacter</c>'s merged config builder).</summary>
        internal static IReadOnlyList<ConvaiActionTargetGroup> ActiveGroups => Registry;

        [SerializeField]
        [Tooltip("Group name the backend/resolution ladder matches. Defaults to this GameObject's name when blank.")]
        private string _groupName;

        [SerializeField]
        [Tooltip("Group description sent to the backend for grounding (e.g. \"the row of four paintings\").")]
        private string _description;

        [SerializeField]
        [Tooltip("Members in authored order. Ordered executors (gaze tour, sequential sweep) walk this list front-to-back.")]
        private List<ConvaiActionTarget> _members = new();

        [SerializeField]
        [Tooltip("Whether member order is meaningful (tour/sequence) or the group is addressed as an unordered set (sweep/centroid).")]
        private bool _isOrdered = true;

        [SerializeField]
        [Tooltip("Register automatically on enable / unregister on disable.")]
        private bool _registerOnEnable = true;

        private readonly List<ConvaiActionTarget> _cleanMembersCache = new();
        private bool _membersCacheDirty = true;
        private bool _registered;
        private bool _emptyNameWarned;

        /// <summary>Group name, defaulting to the GameObject's name when left blank.</summary>
        public string GroupName
        {
            get => string.IsNullOrWhiteSpace(_groupName) ? gameObject.name : _groupName;
            set => _groupName = value;
        }

        /// <summary>Group description sent to the backend for grounding.</summary>
        public string Description
        {
            get => _description;
            set => _description = value;
        }

        /// <summary>Whether member order is meaningful (tour/sequence) versus an unordered set (sweep/centroid).</summary>
        public bool IsOrdered
        {
            get => _isOrdered;
            set => _isOrdered = value;
        }

        /// <summary>Whether this group registers automatically on enable / unregisters on disable.</summary>
        public bool RegisterOnEnable
        {
            get => _registerOnEnable;
            set => _registerOnEnable = value;
        }

        /// <summary>
        ///     Authored members with null entries (destroyed targets) filtered out. Cached and
        ///     refreshed only when the authored list changes or a stale (destroyed) entry is
        ///     detected in the cache, so repeated reads within a frame do not re-scan or allocate.
        /// </summary>
        public IReadOnlyList<ConvaiActionTarget> Members
        {
            get
            {
                if (_membersCacheDirty || CacheHasStaleEntries())
                    RefreshMembersCache();

                return _cleanMembersCache;
            }
        }

        private void OnValidate() => _membersCacheDirty = true;

        private bool CacheHasStaleEntries()
        {
            for (int i = 0; i < _cleanMembersCache.Count; i++)
            {
                if (_cleanMembersCache[i] == null) return true;
            }

            return false;
        }

        private void RefreshMembersCache()
        {
            _cleanMembersCache.Clear();
            if (_members != null)
            {
                for (int i = 0; i < _members.Count; i++)
                {
                    ConvaiActionTarget member = _members[i];
                    if (member != null) _cleanMembersCache.Add(member);
                }
            }

            _membersCacheDirty = false;
        }

        private void OnEnable()
        {
            if (_registerOnEnable) HandleEnable();
        }

        private void OnDisable() => HandleDisable();

        /// <summary>
        ///     Full enable path (registration into <see cref="ActiveGroups" />). Internal seam so
        ///     EditMode tests can drive the lifecycle explicitly — Unity does not invoke
        ///     <c>OnEnable</c> for plain MonoBehaviours outside play mode.
        /// </summary>
        internal void HandleEnable()
        {
            if (_registered) return;

            if (string.IsNullOrWhiteSpace(GroupName))
            {
                if (!_emptyNameWarned)
                {
                    _emptyNameWarned = true;
                    ConvaiLogger.Warning(
                        $"[ConvaiActionTargetGroup] '{gameObject.name}' has no group name and no GameObject name to fall back to; ignoring.",
                        LogCategory.Character);
                }

                return;
            }

            Registry.Add(this);
            _registered = true;
        }

        /// <summary>Disable counterpart of <see cref="HandleEnable" /> (test seam).</summary>
        internal void HandleDisable()
        {
            if (!_registered) return;
            Registry.Remove(this);
            _registered = false;
        }
    }
}
