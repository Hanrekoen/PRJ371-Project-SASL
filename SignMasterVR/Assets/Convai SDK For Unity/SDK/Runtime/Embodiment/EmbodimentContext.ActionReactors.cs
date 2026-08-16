using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Fan-out of action-performance notifications to every reactor registered on this character.
    /// </summary>
    /// <remarks>
    ///     Separate from the composition root because this is dispatch, not composition: Gaze,
    ///     Emotion and Body Language each contribute a reactor and each is notified independently,
    ///     with one reactor's exception logged rather than allowed to cut the others off.
    /// </remarks>
    public sealed partial class EmbodimentContext
    {

        // Reused so the fan-out allocates nothing per call; safe because these are only invoked from
        // the main thread and the buffer is refilled at the top of each notification.
        private readonly List<IActionPerformanceReactor> _reactorBuffer = new(4);

        /// <summary>Notifies every registered action-performance reactor that a batch has started.</summary>
        public void NotifyActionBatchStarted()
        {
            GetAll(_reactorBuffer);
            for (int i = 0; i < _reactorBuffer.Count; i++)
            {
                try
                {
                    _reactorBuffer[i].OnActionBatchStarted();
                }
                catch (Exception ex)
                {
                    LogEventSubscriberException(ex,
                        "[EmbodimentContext] A reactor threw while handling OnActionBatchStarted.");
                }
            }
        }

        /// <summary>Notifies every registered action-performance reactor that a target was acquired.</summary>
        public void NotifyActionTargetAcquired(string targetName, Vector3 worldPosition)
        {
            GetAll(_reactorBuffer);
            for (int i = 0; i < _reactorBuffer.Count; i++)
            {
                try
                {
                    _reactorBuffer[i].OnActionTargetAcquired(targetName, worldPosition);
                }
                catch (Exception ex)
                {
                    LogEventSubscriberException(ex,
                        "[EmbodimentContext] A reactor threw while handling OnActionTargetAcquired.");
                }
            }
        }

        /// <summary>Notifies every registered action-performance reactor of a step's outcome.</summary>
        public void NotifyActionOutcome(bool success)
        {
            GetAll(_reactorBuffer);
            for (int i = 0; i < _reactorBuffer.Count; i++)
            {
                try
                {
                    _reactorBuffer[i].OnActionOutcome(success);
                }
                catch (Exception ex)
                {
                    LogEventSubscriberException(ex,
                        "[EmbodimentContext] A reactor threw while handling OnActionOutcome.");
                }
            }
        }
    }
}
