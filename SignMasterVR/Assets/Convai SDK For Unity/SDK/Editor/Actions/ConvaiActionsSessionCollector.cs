using System;
using System.Collections.Generic;
using Convai.Domain.EventSystem;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using UnityEditor;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Editor-side session collector for the Actions Editor window's Live view and its
    ///     usage-insights panel: subscribes to one dispatcher's
    ///     batch/step events and one feedback relay's composed event, and records them into the
    ///     shared <see cref="ConvaiActionsSessionLog" /> ring buffers. Zero cost when unused —
    ///     nothing is hooked until at least one consumer is registered <em>and</em> Play mode is
    ///     running, and everything unsubscribes on Play-mode exit (the log itself survives exit so
    ///     post-play panels can still read it; a new Play session clears it). Domain reload resets
    ///     all static state naturally. Change notifications are throttled to at most one per
    ///     <see cref="MinNotifyIntervalSeconds" /> so a chatty batch cannot repaint-storm windows.
    /// </summary>
    internal static class ConvaiActionsSessionCollector
    {
        private const double MinNotifyIntervalSeconds = 0.1d;

        private static readonly HashSet<object> Consumers = new();
        private static readonly ConvaiActionsSessionLog SessionLog = new(() => EditorApplication.timeSinceStartup);

        private static ConvaiActionDispatcher _dispatcher;
        private static ConvaiActionFeedbackRelay _relay;
        private static IEventHub _dropEventHub;
        private static SubscriptionToken _dropToken;
        private static string _watchedCharacterId = string.Empty;
        private static bool _playModeHooked;
        private static bool _throttleUpdateHooked;
        private static bool _notifyPending;
        private static double _lastNotifyTime;

        /// <summary>The session's recorded batches/steps/feedback (see <see cref="ConvaiActionsSessionLog" />).</summary>
        internal static ConvaiActionsSessionLog Log => SessionLog;

        /// <summary>Raised (throttled) whenever the log changed. Consumers Repaint and rebuild cached models.</summary>
        internal static event Action Changed;

        /// <summary>Registers a consumer (e.g. an open window). First consumer arms the play-mode hook.</summary>
        internal static void AddConsumer(object consumer)
        {
            if (consumer == null)
                return;

            if (Consumers.Add(consumer) && Consumers.Count == 1 && !_playModeHooked)
            {
                EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
                _playModeHooked = true;
            }
        }

        /// <summary>Unregisters a consumer. The last consumer leaving detaches everything.</summary>
        internal static void RemoveConsumer(object consumer)
        {
            if (consumer == null || !Consumers.Remove(consumer) || Consumers.Count > 0)
                return;

            Detach();
            if (_playModeHooked)
            {
                EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
                _playModeHooked = false;
            }
        }

        /// <summary>
        ///     Points the collector at a character's dispatcher/relay pair. No-op outside Play mode
        ///     or with no consumers; cheap no-op when the subject is unchanged (callers may invoke
        ///     this from OnGUI).
        /// </summary>
        internal static void SetSubject(ConvaiActionDispatcher dispatcher, ConvaiActionFeedbackRelay relay)
        {
            if (Consumers.Count == 0 || !EditorApplication.isPlaying)
                return;

            if (ReferenceEquals(_dispatcher, dispatcher) && ReferenceEquals(_relay, relay))
                return;

            Detach();
            _dispatcher = dispatcher;
            _relay = relay;

            if (_dispatcher != null)
            {
                _dispatcher.OnBatchStarted.AddListener(HandleBatchStarted);
                _dispatcher.OnStepStarted.AddListener(HandleStepStarted);
                _dispatcher.OnStepCompleted.AddListener(HandleStepCompleted);
                _dispatcher.OnBatchCompleted.AddListener(HandleBatchCompleted);
                _dispatcher.OnBatchAborted.AddListener(HandleBatchAborted);
            }

            if (_relay != null)
                _relay.OnFeedbackComposed += HandleFeedbackComposed;

            SubscribeToDrops();
        }

        /// <summary>
        ///     Listens for commands the response filter discarded before the dispatcher ever heard
        ///     about them.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Every other signal this collector records comes from the dispatcher, and that is
        ///         precisely why the Live view used to show nothing for the worst failure mode: a
        ///         command dropped by the filter never starts a batch, so there was no event to draw
        ///         and the window sat empty while the character silently did nothing. The gap was in
        ///         the data, not in the drawing.
        ///     </para>
        ///     <para>
        ///         Attaching also tells the runtime that explanations are wanted, so opening the
        ///         window is enough to get them regardless of console verbosity.
        ///     </para>
        /// </remarks>
        private static void SubscribeToDrops()
        {
            if (_dispatcher == null)
                return;

            // Same seam the feedback relay uses: the character's embodiment context carries the hub
            // the response filter publishes on. Reached from the dispatcher so this collector stays
            // scoped to the one character it is already following.
            IEventHub hub = _dispatcher.GetComponentInParent<EmbodimentContext>(true)?.EventHub;
            if (hub == null)
                return;

            _watchedCharacterId = _dispatcher.GetComponentInParent<ConvaiCharacter>(true)?.CharacterId ?? string.Empty;

            _dropToken = hub.Subscribe<ConvaiActionResponseFilterDiagnostic>(HandleCommandsDropped);
            _dropEventHub = hub;
            ConvaiActionDropReporting.AttachTool();
        }

        private static void UnsubscribeFromDrops()
        {
            if (_dropEventHub == null)
                return;

            _dropEventHub.Unsubscribe(_dropToken);
            _dropToken = default;
            _dropEventHub = null;
            _watchedCharacterId = string.Empty;
            ConvaiActionDropReporting.DetachTool();
        }

        private static void HandleCommandsDropped(ConvaiActionResponseFilterDiagnostic diagnostic)
        {
            IReadOnlyList<ConvaiActionDropReport> drops = diagnostic.Drops;
            if (drops.Count == 0)
                return;

            // The hub carries every character in the room; this view follows one.
            if (!string.IsNullOrEmpty(_watchedCharacterId) &&
                !string.Equals(diagnostic.CharacterId, _watchedCharacterId, StringComparison.Ordinal))
                return;

            string timeLabel = DateTime.Now.ToString("HH:mm:ss");
            for (int i = 0; i < drops.Count; i++)
            {
                ConvaiActionDropReport drop = drops[i];
                SessionLog.OnCommandDropped(
                    timeLabel, drop.ActionName, drop.RequestedTarget, drop.Explanation);
            }

            NotifyThrottled();
        }

        private static void Detach()
        {
            if (_dispatcher != null)
            {
                _dispatcher.OnBatchStarted.RemoveListener(HandleBatchStarted);
                _dispatcher.OnStepStarted.RemoveListener(HandleStepStarted);
                _dispatcher.OnStepCompleted.RemoveListener(HandleStepCompleted);
                _dispatcher.OnBatchCompleted.RemoveListener(HandleBatchCompleted);
                _dispatcher.OnBatchAborted.RemoveListener(HandleBatchAborted);
                _dispatcher = null;
            }

            if (_relay != null)
            {
                _relay.OnFeedbackComposed -= HandleFeedbackComposed;
                _relay = null;
            }

            UnsubscribeFromDrops();

            if (_throttleUpdateHooked)
            {
                EditorApplication.update -= HandleThrottleUpdate;
                _throttleUpdateHooked = false;
            }

            _notifyPending = false;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    SessionLog.Clear(); // Fresh session, fresh log.
                    NotifyNow();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    Detach(); // Log intentionally survives for post-play reading.
                    NotifyNow();
                    break;
            }
        }

        private static void HandleBatchStarted()
        {
            SessionLog.OnBatchStarted();
            NotifyThrottled();
        }

        private static void HandleStepStarted(ConvaiActionInvocation invocation)
        {
            string actionName = invocation?.Definition?.ActionName ?? invocation?.Command?.Name ?? string.Empty;
            string targetName = invocation?.ResolvedTarget?.Name ?? invocation?.Command?.Target ?? string.Empty;
            SessionLog.OnStepStarted(invocation?.BatchIndex ?? -1, invocation?.StepIndex ?? 0, actionName, targetName);
            NotifyThrottled();
        }

        private static void HandleStepCompleted(ConvaiActionStepReport report)
        {
            SessionLog.OnStepCompleted(
                report?.Result.Status ?? ConvaiActionExecutionStatus.Failed,
                report?.FailureReason ?? ConvaiActionFailureReason.None,
                report?.FailureMessage ?? string.Empty);
            NotifyThrottled();
        }

        private static void HandleBatchCompleted()
        {
            SessionLog.OnBatchFinished(aborted: false);
            NotifyThrottled();
        }

        private static void HandleBatchAborted()
        {
            SessionLog.OnBatchFinished(aborted: true);
            NotifyThrottled();
        }

        private static void HandleFeedbackComposed(string fact, bool narrated)
        {
            SessionLog.OnFeedback(DateTime.Now.ToString("HH:mm:ss"), fact, narrated);
            NotifyThrottled();
        }

        private static void NotifyThrottled()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastNotifyTime >= MinNotifyIntervalSeconds)
            {
                NotifyNow();
                return;
            }

            if (_notifyPending)
                return;

            _notifyPending = true;
            if (!_throttleUpdateHooked)
            {
                EditorApplication.update += HandleThrottleUpdate;
                _throttleUpdateHooked = true;
            }
        }

        private static void HandleThrottleUpdate()
        {
            if (!_notifyPending)
            {
                EditorApplication.update -= HandleThrottleUpdate;
                _throttleUpdateHooked = false;
                return;
            }

            if (EditorApplication.timeSinceStartup - _lastNotifyTime < MinNotifyIntervalSeconds)
                return;

            NotifyNow();
        }

        private static void NotifyNow()
        {
            _lastNotifyTime = EditorApplication.timeSinceStartup;
            _notifyPending = false;
            if (_throttleUpdateHooked)
            {
                EditorApplication.update -= HandleThrottleUpdate;
                _throttleUpdateHooked = false;
            }

            Changed?.Invoke();
        }
    }
}
