using System.Collections.Generic;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Core.Diagnostics;
using Convai.Modules.Gaze.Core.Policy;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Targeting
{
    /// <summary>
    ///     Ownership of scripted gaze: the request stack, the handles handed back to callers, and
    ///     the settlement evaluator that decides when a caller's <c>Settled</c> task completes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Extracted from <see cref="ConvaiGazeController" />, which had grown to hold this
    ///         alongside twenty-four behaviour directors and the whole solve. These six pieces of
    ///         state only ever move together — a handle is meaningless without its stack entry, and
    ///         settlement is meaningless without the latched winner — so they belong to one object
    ///         rather than to six fields on a composition root.
    ///     </para>
    ///     <para>
    ///         <b>This changes no ordering.</b> Every method is called from exactly where its
    ///         inlined predecessor was called, so the tick sequence that determines how gaze feels
    ///         is untouched. The controller keeps the public <c>GazeAt</c>/<c>GlanceAt</c> surface
    ///         and the internal test seams, forwarding here.
    ///     </para>
    /// </remarks>
    internal sealed class GazeScriptedRequests
    {
        private readonly GazeTargetStack _stack = new();
        private readonly List<GazeHandle> _handles = new(2);
        private readonly List<int> _suppressedEntryIds = new(2);
        private readonly GazeSettlementEvaluator _settlement = new();

        private int _activeEntryId;
        private float _activeCommitment;

        /// <summary>The underlying stack — an internal seam the controller's tests drive directly.</summary>
        internal GazeTargetStack Stack => _stack;

        /// <summary>How many scripted requests are currently held.</summary>
        internal int Count => _stack.Count;

        /// <summary>The request that wins this tick, or <c>null</c>.</summary>
        internal GazeTargetStack.Entry ResolveActive(float time) => _stack.ResolveActive(time);

        /// <summary>
        ///     Pushes a request and returns the handle the caller awaits. The trace line is built
        ///     by the caller so this stays free of presentation concerns.
        /// </summary>
        internal GazeHandle Push(
            ConvaiGazeController owner,
            Transform target,
            Vector3 point,
            bool hasTransform,
            int priority,
            float engagementOverride,
            bool allowBodyTurn,
            float deadline,
            string name)
        {
            int entryId = _stack.Push(
                target, point, hasTransform, priority, engagementOverride, allowBodyTurn, deadline, name);

            var handle = new GazeHandle(owner, entryId, name);
            _handles.Add(handle);
            return handle;
        }

        /// <summary>
        ///     Pushes a request no caller awaits — the curiosity and character-glance directors,
        ///     which schedule their own beats and never hand a handle to anyone.
        /// </summary>
        internal void PushUnowned(
            Transform target,
            Vector3 point,
            bool hasTransform,
            int priority,
            float engagementOverride,
            bool allowBodyTurn,
            float deadline,
            string name,
            float headContributionOverride = -1f,
            Vector3 localAimOffset = default) =>
            _stack.Push(
                target, point, hasTransform, priority, engagementOverride, allowBodyTurn, deadline, name,
                headContributionOverride, localAimOffset);

        /// <summary>Releases one request. Returns whether its stack entry was still live.</summary>
        internal bool Release(GazeHandle handle)
        {
            if (handle == null) return false;

            bool removed = _stack.Remove(handle.EntryId);
            handle.MarkCompleted();
            _handles.Remove(handle);
            return removed;
        }

        /// <summary>Releases every request, completing each handle unsettled.</summary>
        internal void ReleaseAll()
        {
            for (int i = _handles.Count - 1; i >= 0; i--)
                _handles[i].MarkCompleted();
            _handles.Clear();
            _stack.Clear();
        }

        /// <summary>
        ///     Latches this tick's scripted winner and expires handles whose stack entry is gone
        ///     (hold elapsed, or the target transform died). Settlement is decided later, against
        ///     the solved contact error.
        /// </summary>
        internal void ProcessDecision(in GazeTargetDecision decision)
        {
            _activeEntryId = decision.IsScripted ? decision.ScriptedEntryId : 0;
            _activeCommitment = decision.IsScripted ? decision.Commitment : 0f;

            for (int i = _handles.Count - 1; i >= 0; i--)
            {
                GazeHandle handle = _handles[i];
                if (_stack.Contains(handle.EntryId)) continue;

                handle.MarkCompleted();
                _handles.RemoveAt(i);
            }
        }

        /// <summary>
        ///     Completes the active request's <c>Settled</c> task once the character is visibly on
        ///     target, so a caller can gate follow-up work on the look actually landing.
        /// </summary>
        internal void ProcessSettlement(float contactErrorDegrees)
        {
            if (!_settlement.Tick(_activeEntryId, _activeCommitment >= 0.95f, contactErrorDegrees))
                return;

            for (int i = 0; i < _handles.Count; i++)
            {
                GazeHandle handle = _handles[i];
                if (handle.EntryId != _activeEntryId) continue;
                handle.MarkSettled(true);
                return;
            }
        }

        /// <summary>
        ///     Drops every glance-tier request (priority below the explicit <c>GazeAt</c> default of
        ///     0) while an eye-contact lock is in force, completing their handles unsettled.
        ///     Explicit requests are untouched: a direct <c>GazeAt</c> is deliberate developer
        ///     intent and stays sovereign over the lock.
        /// </summary>
        /// <param name="absorbed">Receives the name of each absorbed request, for the trace.</param>
        /// <returns>Whether anything was absorbed.</returns>
        internal bool SuppressGlanceTier(List<string> absorbed)
        {
            absorbed?.Clear();
            if (_stack.Count == 0) return false;

            _suppressedEntryIds.Clear();
            if (_stack.RemoveBelowPriority(0, _suppressedEntryIds) == 0) return false;

            for (int i = _handles.Count - 1; i >= 0; i--)
            {
                GazeHandle handle = _handles[i];
                if (!_suppressedEntryIds.Contains(handle.EntryId)) continue;

                handle.MarkCompleted();
                _handles.RemoveAt(i);
                absorbed?.Add(handle.TargetName);
            }

            return true;
        }

        /// <summary>
        ///     Rejects everything while Exact focus refuses scripted overrides. Returns whether
        ///     there was anything to reject, so the caller can skip its trace line.
        /// </summary>
        internal bool RejectAllForExactFocus()
        {
            if (_stack.Count == 0 && _handles.Count == 0) return false;

            _stack.Clear();
            for (int i = _handles.Count - 1; i >= 0; i--)
            {
                _handles[i].MarkCompleted();
                _handles.RemoveAt(i);
            }

            _activeEntryId = 0;
            _activeCommitment = 0f;
            _settlement.Reset();
            return true;
        }

        /// <summary>Full reset for component disable.</summary>
        internal void Reset()
        {
            ReleaseAll();
            _settlement.Reset();
            _activeEntryId = 0;
            _activeCommitment = 0f;
            _suppressedEntryIds.Clear();
        }
    }
}
