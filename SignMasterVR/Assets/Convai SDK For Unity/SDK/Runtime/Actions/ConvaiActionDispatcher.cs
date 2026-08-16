using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Actions
{
    /// <summary>How the dispatcher treats a new backend batch while another batch is still executing.</summary>
    public enum ConvaiActionBatchPolicy
    {
        /// <summary>Run the new batch after the current one finishes (default).</summary>
        Queue = 0,

        /// <summary>Cancel the current batch and pending queue, then run the new batch.</summary>
        ReplaceCurrent = 1,

        /// <summary>Ignore the new batch entirely while anything is executing or queued.</summary>
        DropIncoming = 2
    }

    /// <summary>What a non-succeeded step does to the rest of its batch.</summary>
    public enum ConvaiActionBatchFailurePolicy
    {
        /// <summary>Abort the remaining steps of the batch (default).</summary>
        StopBatch = 0,

        /// <summary>Report the step and continue with the next one.</summary>
        ContinueBatch = 1
    }

    /// <summary>Serializable UnityEvent carrying the step's <see cref="ConvaiActionInvocation" />.</summary>
    [Serializable]
    public sealed class ConvaiActionInvocationUnityEvent : UnityEvent<ConvaiActionInvocation>
    {
    }

    /// <summary>Serializable UnityEvent carrying the completed step's <see cref="ConvaiActionStepReport" />.</summary>
    [Serializable]
    public sealed class ConvaiActionStepReportUnityEvent : UnityEvent<ConvaiActionStepReport>
    {
    }

    [AddComponentMenu("Convai/Convai Action Runner")]
    [DisallowMultipleComponent]
    // ExecuteAlways keeps Awake/OnEnable active outside play mode so EditMode tests and editor
    // tooling can inject batches; cross-thread marshaling is play-mode-only (see HandleActionsReceived).
    [ExecuteAlways]
    [RequireComponent(typeof(ConvaiCharacter))]
    public sealed class ConvaiActionDispatcher : MonoBehaviour, IActionActivitySource
    {
        [Header("Dispatch")]
        [SerializeField]
        [Tooltip("How new backend action batches behave while another batch is still executing.")]
        private ConvaiActionBatchPolicy _batchPolicy = ConvaiActionBatchPolicy.Queue;

        [SerializeField]
        [Tooltip("Whether a step failure aborts the remaining batch or allows it to continue.")]
        private ConvaiActionBatchFailurePolicy _failurePolicy = ConvaiActionBatchFailurePolicy.StopBatch;

        [SerializeField]
        [Tooltip("Maximum seconds a first action waits for character speech before running anyway.")]
        private float _speechGateTimeoutSeconds = 2f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Longest any action may run before it is reported as timed out, in seconds. This is " +
                 "a safety net, not a tuning value: without it one action behavior that never " +
                 "finishes holds this character's whole action queue for the rest of the session, " +
                 "and nothing says so. An action that is meant to run longer sets its own Timeout " +
                 "Seconds, which always wins. Zero removes the net entirely.")]
        private float _defaultStepTimeoutSeconds = DefaultStepTimeoutSeconds;

        /// <summary>
        ///     How long an action may run before the dispatcher gives up on it, when neither the
        ///     action nor the character says otherwise.
        /// </summary>
        /// <remarks>
        ///     Set well above any behavior that finishes on its own — a walk across a level, a
        ///     directed sequence, a tour of a room — so it never cuts short work that was going to
        ///     complete. It exists for the case that does not complete: an await that no event ever
        ///     satisfies, or a loop whose exit condition cannot be reached. That failure is otherwise
        ///     silent and permanent, because every later action queues behind the stuck one.
        /// </remarks>
        public const float DefaultStepTimeoutSeconds = 60f;

        [Header("Barge-In")]
        [SerializeField]
        [Tooltip("When enabled, the user starting to speak cancels the in-flight batch and clears " +
                 "the queue (same effect as ReplaceCurrent, but triggered by the player instead of a " +
                 "new backend batch). Off by default so existing behavior is unchanged.")]
        private bool _cancelOnUserSpeech;

        [Header("Performance")]
        [SerializeField]
        [Tooltip("When enabled, notifies any IActionPerformanceReactor peers registered on this " +
                 "character's embodiment context (Gaze look-where-you-act, Body Language " +
                 "acknowledgment nod, Emotion outcome mood beat) about batch/step lifecycle. " +
                 "No-op when no embodiment context or reactor is present.")]
        private bool _enablePerformanceReactions = true;

        [Header("Events")]
        [SerializeField]
        [Tooltip("Raised when a batch begins executing, before its first step.")]
        private UnityEvent _onBatchStarted = new();

        [SerializeField]
        [Tooltip("Raised before each step executes (after the first step's speech gate).")]
        private ConvaiActionInvocationUnityEvent _onStepStarted = new();

        [SerializeField]
        [Tooltip("Raised when a step's scene behavior reports success.")]
        private ConvaiActionInvocationUnityEvent _onStepSucceeded = new();

        [SerializeField]
        [Tooltip("Raised when a step fails: no definition, no scene behavior, unmet target requirement, error, or timeout.")]
        private ConvaiActionInvocationUnityEvent _onStepFailed = new();

        [SerializeField]
        [Tooltip("Raised when the scene behavior declines the step (reports it as unhandled).")]
        private ConvaiActionInvocationUnityEvent _onStepUnhandled = new();

        [SerializeField]
        [Tooltip("Raised after every step, success or not, with the full step report.")]
        private ConvaiActionStepReportUnityEvent _onStepCompleted = new();

        [SerializeField]
        [Tooltip("Raised when a batch runs all its steps without aborting.")]
        private UnityEvent _onBatchCompleted = new();

        [SerializeField]
        [Tooltip("Raised when a failing step aborts the remainder of its batch.")]
        private UnityEvent _onBatchAborted = new();

        private readonly object _queueLock = new();
        private readonly Queue<IReadOnlyList<ConvaiActionCommand>> _pendingBatches = new();

        /// <summary>
        ///     Reused renderer buffer for <see cref="TryResolvePerformanceLookPoint" />, so
        ///     announcing a step's target allocates nothing.
        /// </summary>
        private readonly List<Renderer> _performanceBoundsScratch = new();
        private readonly HashSet<string> _loggedDisabledActionNames = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Distinct discarded-batch faults already explained; see <see cref="Explain" />.</summary>
        private readonly HashSet<string> _reportedBatchDrops = new(StringComparer.Ordinal);
        private CancellationTokenSource _processingCts;
        private ConvaiCharacter _character;
        private bool _isProcessing;
        private int _batchCounter;
        private int _mainThreadId;

        private EmbodimentContext _embodimentContext;
        private CharacterServiceRegistry.ServiceToken _activityToken;
        private IEventHub _subscribedUserSpeechEventHub;
        private SubscriptionToken _playerSpeakingToken;
        private string _currentActionDisplayName = string.Empty;

        /// <summary>Authored policy for batches arriving while another batch is executing.</summary>
        public ConvaiActionBatchPolicy BatchPolicy => _batchPolicy;

        /// <summary>Whether this dispatcher is running a batch right now.</summary>
        /// <remarks>
        ///     <para>
        ///         Exposed because without it "the character received the command and has not started
        ///         it" is unanswerable from outside, and that sentence covers two completely different
        ///         situations: work legitimately queued behind something still running, and work that
        ///         is never going to start. A debug overlay, an editor panel or game code that wants
        ///         to gate input on the character being free all need to tell those apart.
        ///     </para>
        ///     <para>
        ///         Read-only and safe to poll — a plain flag read, no allocation.
        ///     </para>
        /// </remarks>
        public bool IsBusy
        {
            get
            {
                lock (_queueLock)
                    return _isProcessing;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Queued work counts as well as running work: a character that has been handed a
        ///     batch and is a frame away from starting it is not idle, and letting the state
        ///     flicker to Idle in that gap would flicker every behaviour keyed off it.
        /// </remarks>
        bool IActionActivitySource.IsPerformingAction => IsBusy || PendingBatchCount > 0;

        /// <summary>How many received batches are waiting their turn behind the current one.</summary>
        public int PendingBatchCount
        {
            get
            {
                lock (_queueLock)
                    return _pendingBatches.Count;
            }
        }

        /// <summary>
        ///     The action running right now, or empty between steps. Display text, not an identifier.
        /// </summary>
        public string CurrentActionName => _currentActionDisplayName ?? string.Empty;

        /// <summary>Authored default for what a non-succeeded step does to the rest of its batch.</summary>
        public ConvaiActionBatchFailurePolicy FailurePolicy => _failurePolicy;

        /// <summary>Editor-only live telemetry: whether a batch is currently executing.</summary>
        internal bool IsProcessingLive => _isProcessing;

        /// <summary>Editor-only live telemetry: display name of the step currently executing (empty when idle).</summary>
        internal string CurrentActionDisplayNameLive => _currentActionDisplayName;

        /// <summary>Editor-only live telemetry: batches waiting behind the currently executing one.</summary>
        internal int PendingBatchCountLive
        {
            get
            {
                lock (_queueLock)
                {
                    return _pendingBatches.Count;
                }
            }
        }

        /// <summary>Editor-only live telemetry: batches started since this component was enabled.</summary>
        internal int StartedBatchCountLive => _batchCounter;

        /// <summary>
        ///     Whether the user starting to speak cancels the in-flight batch and clears the
        ///     queue. Toggling this at runtime subscribes/unsubscribes immediately (play mode only).
        /// </summary>
        public bool CancelOnUserSpeech
        {
            get => _cancelOnUserSpeech;
            set
            {
                if (_cancelOnUserSpeech == value) return;
                _cancelOnUserSpeech = value;
                if (_cancelOnUserSpeech)
                    SubscribeToUserSpeechIfNeeded();
                else
                    UnsubscribeFromUserSpeech();
            }
        }

        /// <summary>Whether registered <c>IActionPerformanceReactor</c> peers are notified of batch/step lifecycle.</summary>
        public bool EnablePerformanceReactions
        {
            get => _enablePerformanceReactions;
            set => _enablePerformanceReactions = value;
        }

        /// <summary>
        ///     Raised when <see cref="CancelOnUserSpeech" /> cancels an in-flight/queued batch
        ///     because the user started speaking. Carries the display name of the action that was
        ///     interrupted (empty when nothing had started executing yet).
        /// </summary>
        public event Action<string> OnCancelledByUserSpeech;

        /// <summary>Raised when a batch begins executing, before its first step.</summary>
        public UnityEvent OnBatchStarted => _onBatchStarted;

        /// <summary>Raised before each step executes (after the first step's speech gate).</summary>
        public ConvaiActionInvocationUnityEvent OnStepStarted => _onStepStarted;

        /// <summary>Raised when a step's executor reports success.</summary>
        public ConvaiActionInvocationUnityEvent OnStepSucceeded => _onStepSucceeded;

        /// <summary>Raised when a step fails: no definition, no executor, unmet target requirement, error, or timeout.</summary>
        public ConvaiActionInvocationUnityEvent OnStepFailed => _onStepFailed;

        /// <summary>Raised when the executor declines the step (<see cref="ConvaiActionExecutionStatus.Unhandled" />).</summary>
        public ConvaiActionInvocationUnityEvent OnStepUnhandled => _onStepUnhandled;

        /// <summary>Raised after every step, success or not, with the full <see cref="ConvaiActionStepReport" />.</summary>
        public ConvaiActionStepReportUnityEvent OnStepCompleted => _onStepCompleted;

        /// <summary>Raised when a batch runs all its steps without aborting.</summary>
        public UnityEvent OnBatchCompleted => _onBatchCompleted;

        /// <summary>Raised when a failing step aborts the remainder of its batch.</summary>
        public UnityEvent OnBatchAborted => _onBatchAborted;

        /// <summary>
        ///     Injects a batch exactly as if the backend had sent it — same policies, cloning,
        ///     enrichment, and events. This is the entry point for local/manual triggering
        ///     (used by the Actions Editor's Live mode).
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Commands handed in here go through the same reading as commands from the backend —
        ///         wire-text cleaning, parameter parsing, target resolution — and produce the same
        ///         explanations when something will not work. That reading used to be skipped
        ///         entirely on this path, so the SDK's own Test Run and Live tools exercised a
        ///         different pipeline from the one that runs in a real conversation.
        ///     </para>
        ///     <para>
        ///         What it does <em>not</em> do is refuse. The backend path refuses commands because
        ///         a stale or hallucinated one should never reach an Action Behavior; a command your
        ///         own code hands over is not that, and turning it away would change what existing
        ///         callers get. The step preconditions remain the gate, exactly as before — they
        ///         already fail the step with a message and raise <see cref="OnStepFailed" />.
        ///     </para>
        /// </remarks>
        public void EnqueueActions(IReadOnlyList<ConvaiActionCommand> actions) =>
            ReceiveBatch(actions, alreadyRead: false);

        private void Awake()
        {
            _character = GetComponent<ConvaiCharacter>();
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void OnEnable()
        {
            if (_character == null)
            {
                enabled = false;
                return;
            }

            _character.OnActionsReceived += HandleActionsReceived;
            SubscribeToUserSpeechIfNeeded();

            // Publish "this character is busy doing what it was told" to the embodiment stack.
            // Conversation Flow treats it as engagement, which is what stops a character
            // decaying to Idle in the middle of an errand it is running for the player.
            EmbodimentContext activityContext = ResolveEmbodimentContext();
            _activityToken = activityContext != null
                ? activityContext.Provide<IActionActivitySource>(this)
                : default;
        }

        private void OnDisable()
        {
            if (_character != null)
                _character.OnActionsReceived -= HandleActionsReceived;

            _activityToken.Release();
            _activityToken = default;

            UnsubscribeFromUserSpeech();
            CancelAllWork();
        }

        /// <summary>
        ///     Resolves (without creating) this character's <see cref="EmbodimentContext" />, the
        ///     shared composition root embodiment modules register peer seams on. Returns
        ///     <c>null</c> when no embodiment module has ever been added to this character — the
        ///     barge-in and performance-reaction features then simply stay inert.
        /// </summary>
        private EmbodimentContext ResolveEmbodimentContext()
        {
            if (_embodimentContext == null)
                _embodimentContext = GetComponentInParent<EmbodimentContext>(true);

            return _embodimentContext;
        }

        /// <summary>
        ///     Subscribes to the earliest reliable user-barge-in signal available on this
        ///     character: the domain <see cref="PlayerSpeakingStateChanged" /> event (server VAD),
        ///     the same signal Gaze/Body Language already use for their own reactive cues. Reached
        ///     through the character's <see cref="EmbodimentContext" /> event hub — no embodiment
        ///     context means no barge-in cancellation (logged degradation, not an error).
        /// </summary>
        private void SubscribeToUserSpeechIfNeeded()
        {
            if (!_cancelOnUserSpeech || !UnityEngine.Application.isPlaying) return;
            if (_subscribedUserSpeechEventHub != null) return;

            IEventHub hub = ResolveEmbodimentContext()?.EventHub;
            if (hub == null) return;

            _playerSpeakingToken = hub.Subscribe<PlayerSpeakingStateChanged>(HandlePlayerSpeakingStateChanged);
            _subscribedUserSpeechEventHub = hub;
        }

        private void UnsubscribeFromUserSpeech()
        {
            IEventHub hub = _subscribedUserSpeechEventHub;
            if (hub == null) return;

            hub.Unsubscribe(_playerSpeakingToken);
            _playerSpeakingToken = default;
            _subscribedUserSpeechEventHub = null;
        }

        private void HandlePlayerSpeakingStateChanged(PlayerSpeakingStateChanged e)
        {
            if (!e.IsSpeaking || !_cancelOnUserSpeech) return;

            string interruptedAction = _currentActionDisplayName;
            bool hadWork;
            lock (_queueLock)
                hadWork = _isProcessing || _pendingBatches.Count > 0;

            // Raised before the cancel below so listeners (e.g. ConvaiActionFeedbackRelay's
            // suppress-next-aggregated-fact flag) are armed before CancelAllWorkLocked() unwinds the
            // in-flight step's cancellation — that unwind can complete synchronously depending on the
            // active SynchronizationContext, so ordering here is load-bearing, not cosmetic.
            if (hadWork)
                OnCancelledByUserSpeech?.Invoke(interruptedAction ?? string.Empty);

            lock (_queueLock)
                CancelAllWorkLocked();
        }

        private void OnDestroy() => CancelAllWork();

        /// <summary>
        ///     The backend's own path. These commands were already read and admitted by the response
        ///     filter before the event was published, so reading them again here would run the whole
        ///     stage twice for every command in a real conversation.
        /// </summary>
        private void HandleActionsReceived(IReadOnlyList<ConvaiActionCommand> actions) =>
            ReceiveBatch(actions, alreadyRead: true);

        /// <summary>
        ///     One ingress, with the marshal before anything touches the scene.
        /// </summary>
        /// <remarks>
        ///     Order is load-bearing. Reading a command resolves names against this character's
        ///     action config and the <c>GameObject</c>s and <c>Transform</c>s behind it, none of
        ///     which may be touched off the main thread — and a caller is free to call
        ///     <see cref="EnqueueActions" /> from a worker. So the batch is snapshotted, marshalled,
        ///     and only then read.
        /// </remarks>
        private void ReceiveBatch(IReadOnlyList<ConvaiActionCommand> actions, bool alreadyRead)
        {
            if (actions == null || actions.Count == 0)
                return;

            // Snapshot: EnqueueActions is public API and the caller may keep mutating its list.
            IReadOnlyList<ConvaiActionCommand> batch = ConvaiActionCommand.CloneBatch(actions);

            // The thread question first, and alone. This used to read
            // `UnityEngine.Application.isPlaying && !IsOnDispatcherThread()`, which cannot work:
            // `Application.isPlaying` is itself a main-thread-only call, so a worker-thread caller
            // threw a UnityException deciding whether it needed the marshal it was about to be
            // given. The guarantee that nothing touches scene state before the marshal was broken
            // by the line that implements it.
            //
            // Play state does not belong in this decision either way. Off the main thread there is
            // no legal alternative to marshalling, whatever the editor happens to be doing; on it
            // there is nothing to marshal.
            if (!IsOnDispatcherThread())
            {
                UnityScheduler.Post(() => EnqueueBatchOnDispatcherThread(batch, alreadyRead));
                return;
            }

            EnqueueBatchOnDispatcherThread(batch, alreadyRead);
        }

        private void EnqueueBatchOnDispatcherThread(IReadOnlyList<ConvaiActionCommand> batch, bool alreadyRead)
        {
            // This may have been posted a frame or more ago, and the component need not have
            // survived the wait — a scene change, a despawned character, an object destroyed while a
            // batch was in flight. A destroyed MonoBehaviour keeps a live C# reference, so the
            // captured closure still runs and the first field access throws inside the scheduler's
            // pump, which then abandons the rest of that frame's queue: unrelated work, belonging to
            // objects that are perfectly alive, disappears with it.
            //
            // Returning is the honest answer rather than a silent drop. There is nothing left to
            // report to — the character, its config and its drop listeners went with the component —
            // and the batch was never admitted, so nothing downstream is waiting on it.
            if (this == null)
                return;

            if (!EnsureCharacter())
            {
                ReportDispatcherUnavailable(
                    "this Convai Action Runner is not attached to a Convai Character.");
                return;
            }

            if (!isActiveAndEnabled)
            {
                ReportDispatcherUnavailable(
                    "this Convai Action Runner is disabled, so it is not running anything.");
                return;
            }

            if (!alreadyRead)
                batch = ReadLocalBatch(batch);

            bool shouldStartProcessing = false;
            bool droppedByPolicy = false;

            lock (_queueLock)
            {
                switch (_batchPolicy)
                {
                    case ConvaiActionBatchPolicy.DropIncoming when _isProcessing || _pendingBatches.Count > 0:
                        droppedByPolicy = true;
                        break;
                    case ConvaiActionBatchPolicy.ReplaceCurrent:
                        CancelAllWorkLocked();
                        break;
                }

                if (!droppedByPolicy)
                {
                    _pendingBatches.Enqueue(batch);
                    if (!_isProcessing)
                    {
                        _isProcessing = true;
                        shouldStartProcessing = true;
                    }
                }
            }

            // Reported after the lock is released: explaining reads the current action name and
            // writes a log line, and neither belongs inside a lock the dispatcher thread holds.
            if (droppedByPolicy)
            {
                ReportQueueBusy();
                return;
            }

            if (shouldStartProcessing)
                _ = ProcessQueueAsync();
        }

        /// <summary>
        ///     Whether an explanation for a discarded batch would be read by anyone. Checked before
        ///     building one, so a shipped build pays nothing for the diagnostics it never shows.
        /// </summary>
        private static bool WantsDropExplanation => LoggingConfig.IsWarningEnabled(LogCategory.Actions);

        /// <summary>
        ///     Says that a batch was discarded because this dispatcher could not take work at all.
        /// </summary>
        /// <remarks>
        ///     This was one of three completely silent doors: no log, no event, no step in any tool.
        ///     Commands arriving at a dispatcher with no character, or at a disabled component,
        ///     simply ceased to exist — and because they never reached a handler, nothing downstream
        ///     could report what it never saw.
        /// </remarks>
        private void ReportDispatcherUnavailable(string detail)
        {
            if (!WantsDropExplanation)
                return;

            Explain(ConvaiActionDropReportFactory.DispatcherUnavailable(detail));
        }

        /// <summary>
        ///     Says that a batch was discarded because earlier work was still running and the batch
        ///     policy discards anything arriving meanwhile.
        /// </summary>
        /// <remarks>
        ///     The most misleading of the silent doors, because the character is visibly working:
        ///     the second request looks like it is being ignored on purpose rather than never having
        ///     been queued. The explanation names the policy, since the policy is the fix.
        /// </remarks>
        private void ReportQueueBusy()
        {
            if (!WantsDropExplanation)
                return;

            Explain(ConvaiActionDropReportFactory.QueueBusy(
                _batchPolicy.ToString(), _currentActionDisplayName));
        }

        /// <summary>
        ///     Reads a locally injected batch the way the backend path reads one, explains anything
        ///     that will not work, and returns the read commands to be queued.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The value here is the explanation. A command handed in by hand can name an action
        ///         this character does not offer, or a target nothing in the scene answers to, and
        ///         until now the only symptom was a step that failed with a terse message after the
        ///         batch had already started. The same report the backend path produces is far more
        ///         use: it names what was asked for, what was found instead, and what to change.
        ///     </para>
        ///     <para>
        ///         <b>The read commands are what gets queued</b>, and that is not an optimization. A
        ///         first version reported on a copy and let the dispatcher read the batch again at
        ///         dispatch — so every command on this path was read twice and said everything twice.
        ///         A duplicated warning is worse than no warning: it teaches the reader that the
        ///         warnings are noise. One read, one report.
        ///     </para>
        ///     <para>
        ///         When nobody is listening for explanations the reading still happens — it is what
        ///         parses the command — but nothing is composed or written.
        ///     </para>
        /// </remarks>
        private IReadOnlyList<ConvaiActionCommand> ReadLocalBatch(IReadOnlyList<ConvaiActionCommand> batch)
        {
            if (!EnsureCharacter())
                return batch;

            var drops = new ConvaiActionDropCollector();
            IReadOnlyList<ConvaiActionCommand> read = ConvaiActionResponseParser.ReadWithoutRefusing(
                batch,
                _character.GetRuntimeActionConfig(),
                _character.GetRuntimeActionDefinitionCatalog(),
                drops,
                _character.transform.position);

            IReadOnlyList<ConvaiActionDropReport> reports = drops.Reports;
            for (int i = 0; i < reports.Count; i++)
                Explain(reports[i]);

            return read.Count == batch.Count ? read : batch;
        }

        /// <summary>
        ///     Writes a drop explanation once per distinct fault, for the life of this component. A
        ///     busy dispatcher discards batches in bursts, and what is worth knowing is that it
        ///     happens at all — a line per occurrence would bury the console it was meant to inform.
        /// </summary>
        private void Explain(in ConvaiActionDropReport report)
        {
            if (!_reportedBatchDrops.Add(report.Signature + '|' + report.Explanation))
                return;

            ConvaiLogger.Warning($"[ConvaiActionDispatcher] {report.Explanation}", LogCategory.Actions);
        }

        private async Task ProcessQueueAsync()
        {
            // Each drain owns one CTS; a ReplaceCurrent cancel ends the drain, and EndDrain decides
            // whether a fresh drain (with a fresh CTS) should pick up batches enqueued meanwhile.
            bool continueDraining = true;
            while (continueDraining && TryBeginDrain(out CancellationTokenSource processingCts))
            {
                try
                {
                    while (TryDequeueBatch(out IReadOnlyList<ConvaiActionCommand> batch, out int batchIndex))
                    {
                        await ExecuteBatchAsync(batch, processingCts.Token, batchIndex);
                        if (processingCts.IsCancellationRequested)
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"Batch processing failed on '{name}': {ex}",
                        LogCategory.Character);
                }
                finally
                {
                    continueDraining = EndDrain(processingCts);
                }
            }
        }

        private bool TryBeginDrain(out CancellationTokenSource processingCts)
        {
            lock (_queueLock)
            {
                if (_pendingBatches.Count == 0)
                {
                    _isProcessing = false;
                    processingCts = null;
                    return false;
                }

                _processingCts = new CancellationTokenSource();
                processingCts = _processingCts;
                return true;
            }
        }

        private bool TryDequeueBatch(out IReadOnlyList<ConvaiActionCommand> batch, out int batchIndex)
        {
            lock (_queueLock)
            {
                if (_pendingBatches.Count == 0)
                {
                    batch = null;
                    batchIndex = 0;
                    return false;
                }

                batch = _pendingBatches.Dequeue();
                batchIndex = _batchCounter++;
                return true;
            }
        }

        private bool EndDrain(CancellationTokenSource processingCts)
        {
            bool shouldContinueProcessing;
            lock (_queueLock)
            {
                if (ReferenceEquals(_processingCts, processingCts))
                    _processingCts = null;

                shouldContinueProcessing = _pendingBatches.Count > 0 && isActiveAndEnabled;
                if (!shouldContinueProcessing)
                    _isProcessing = false;
            }

            processingCts.Dispose();
            return shouldContinueProcessing;
        }

        private async Task ExecuteBatchAsync(
            IReadOnlyList<ConvaiActionCommand> actions,
            CancellationToken batchCt,
            int batchIndex)
        {
            _onBatchStarted?.Invoke();
            _currentActionDisplayName = string.Empty;

            ConvaiActionConfig actionConfig = _character.GetRuntimeActionConfig();
            IReadOnlyList<ConvaiActionDefinition> definitions = _character.GetRuntimeActionDefinitionCatalog();
            Dictionary<string, ConvaiActionDefinition> lookup = ConvaiActionDefinition.BuildLookup(definitions);

            bool batchAborted = false;

            for (int stepIndex = 0; stepIndex < actions.Count; stepIndex++)
            {
                batchCt.ThrowIfCancellationRequested();

                ConvaiActionInvocation invocation = ResolveStep(
                    actions[stepIndex], actionConfig, definitions, lookup, batchIndex, stepIndex);

                if (stepIndex == 0)
                {
                    await WaitForSpeechGateAsync(invocation.Command, invocation.Definition, batchCt);
                    NotifyPerformanceBatchStarted();
                }

                _currentActionDisplayName = invocation.Definition?.ActionName ?? invocation.Command?.Name ?? string.Empty;
                NotifyPerformanceTargetAcquired(invocation.ResolvedTarget);

                _onStepStarted?.Invoke(invocation);

                ConvaiActionExecutionResult result = await RunStepAsync(invocation, batchCt);
                NotifyPerformanceOutcome(result.Status == ConvaiActionExecutionStatus.Succeeded);

                bool stepWillAbort = result.Status != ConvaiActionExecutionStatus.Succeeded &&
                                     ShouldAbortNonSuccess(invocation.Definition);
                string failureMessage = result.Status == ConvaiActionExecutionStatus.Succeeded
                    ? string.Empty
                    : BuildFailureMessage(result, stepWillAbort);
                _onStepCompleted?.Invoke(new ConvaiActionStepReport(invocation, result, stepWillAbort, failureMessage));

                if (stepWillAbort)
                {
                    batchAborted = true;
                    break;
                }
            }

            if (batchAborted)
                _onBatchAborted?.Invoke();
            else
                _onBatchCompleted?.Invoke();
        }

        /// <summary>
        ///     Enriches the command (once), matches its definition, and resolves its target
        ///     into the immutable invocation handed to executors and events.
        /// </summary>
        private ConvaiActionInvocation ResolveStep(
            ConvaiActionCommand command,
            ConvaiActionConfig actionConfig,
            IReadOnlyList<ConvaiActionDefinition> definitions,
            Dictionary<string, ConvaiActionDefinition> lookup,
            int batchIndex,
            int stepIndex)
        {
            // Enrich first, unconditionally, guarded only by whether it has been done. It used to be
            // guarded by "the definition lookup failed", which meant a command whose name happened to
            // match a definition was never read at all: no wire-text cleaning, no parameters parsed,
            // no inline target recovered. Commands arriving from the backend were pre-enriched so
            // nobody noticed, but anything handed to EnqueueActions took the other branch — the
            // dispatcher behaved differently depending on where the command came from.
            if (command is { Enriched: false })
                command = ConvaiActionResponseParser.Enrich(command, actionConfig, definitions);

            lookup.TryGetValue(command.Name ?? string.Empty, out ConvaiActionDefinition definition);

            Vector3? origin = _character != null ? _character.transform.position : (Vector3?)null;
            // One ladder, shared with the response filter that admitted this command, so the target
            // it was judged on is the target the executor is handed.
            ConvaiActionTargetResolution.TryResolve(
                command, definition, actionConfig, origin, out ConvaiResolvedActionTarget resolvedTarget);

            ReportTargetDrift(command, resolvedTarget);

            return new ConvaiActionInvocation(command, definition, resolvedTarget, _character, batchIndex, stepIndex);
        }

        /// <summary>
        ///     Says when the target a command was admitted on is not the target it is about to act
        ///     on.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A command is judged the moment it arrives and performed some time later — queued
        ///         behind another batch, or held by the speech gate — and the scene does not stand
        ///         still in between. Something can despawn, a target can be withdrawn, a second
        ///         object of the same name can move nearer. Resolving again at dispatch is therefore
        ///         the correct thing to do, not a duplication to eliminate: the freshest answer is
        ///         the right one.
        ///     </para>
        ///     <para>
        ///         What was missing is that the change itself was invisible. The character agreed to
        ///         walk to one thing and walked to another, and every component involved was behaving
        ///         correctly, so nothing had reason to say anything. This is the one line that makes
        ///         it visible. Once per distinct drift, like every other explanation here.
        ///     </para>
        /// </remarks>
        private void ReportTargetDrift(ConvaiActionCommand command, ConvaiResolvedActionTarget resolved)
        {
            string admitted = command?.AdmittedTargetName;
            if (string.IsNullOrEmpty(admitted) || !WantsDropExplanation)
                return;

            string now = resolved?.Name;
            if (string.Equals(admitted, now, StringComparison.OrdinalIgnoreCase))
                return;

            string became = string.IsNullOrEmpty(now) ? "nothing at all" : $"'{now}'";
            Explain(ConvaiActionDropReportFactory.TargetDrifted(command?.Name, admitted, became));
        }

        /// <summary>
        ///     Checks step preconditions (definition, executor, target requirement), executes the
        ///     step, and fires exactly one of the succeeded/unhandled/failed step events.
        /// </summary>
        private async Task<ConvaiActionExecutionResult> RunStepAsync(
            ConvaiActionInvocation invocation,
            CancellationToken batchCt)
        {
            ConvaiActionDefinition definition = invocation.Definition;

            if (definition == null)
            {
                _onStepFailed?.Invoke(invocation);
                return ConvaiActionExecutionResult.Failed(
                    $"No local action definition found for action '{invocation.Command?.Name ?? string.Empty}'.");
            }

            // A stale backend command for an unavailable action (authored-disabled, or disabled
            // through ConvaiCharacterActions.SetActionAvailable) is declined, not executed: the
            // action was deliberately withheld from the backend's action config, so this command
            // is either stale or hallucinated. Test Run's explicit "Run Anyway" bypasses this.
            if (invocation.Command?.BypassAvailability != true && !IsActionAvailableForDispatch(definition))
            {
                LogDisabledActionOnce(definition.ActionName);
                _onStepUnhandled?.Invoke(invocation);
                return ConvaiActionExecutionResult.Unhandled(
                    $"Action '{definition.ActionName}' is disabled on this character (action disabled).");
            }

            if (definition.Executor is not IConvaiActionExecutor executor)
            {
                _onStepFailed?.Invoke(invocation);
                return ConvaiActionExecutionResult.Failed(
                    $"Action '{definition.ActionName}' has no action behavior bound to it.");
            }

            if (!ValidateTargetRequirement(definition.TargetRequirement, invocation.ResolvedTarget))
            {
                _onStepFailed?.Invoke(invocation);
                return ConvaiActionExecutionResult.Failed(
                    BuildTargetRequirementFailureMessage(invocation.Command, definition, invocation.ResolvedTarget));
            }

            ConvaiActionExecutionResult result = await ExecuteStepAsync(
                executor, invocation, definition, ResolveStepTimeoutSeconds(definition), batchCt);

            switch (result.Status)
            {
                case ConvaiActionExecutionStatus.Succeeded:
                    _onStepSucceeded?.Invoke(invocation);
                    break;
                case ConvaiActionExecutionStatus.Unhandled:
                    _onStepUnhandled?.Invoke(invocation);
                    break;
                default:
                    _onStepFailed?.Invoke(invocation);
                    break;
            }

            return result;
        }

        /// <summary>
        ///     How long this step may run: the action's own limit when it sets one, the character's
        ///     safety net otherwise.
        /// </summary>
        /// <remarks>
        ///     The action always wins, including when it is deliberately longer than the net — that
        ///     is what authoring a timeout on an action <em>means</em>. The net only answers for the
        ///     actions that never thought about it, which is nearly all of them.
        /// </remarks>
        private float ResolveStepTimeoutSeconds(ConvaiActionDefinition definition) =>
            definition.TimeoutSeconds > 0f ? definition.TimeoutSeconds : Mathf.Max(0f, _defaultStepTimeoutSeconds);

        private static async Task<ConvaiActionExecutionResult> ExecuteStepAsync(
            IConvaiActionExecutor executor,
            ConvaiActionInvocation invocation,
            ConvaiActionDefinition definition,
            float timeoutSeconds,
            CancellationToken batchCt)
        {
            bool hasTimeout = timeoutSeconds > 0f;
            CancellationTokenSource stepCts = null;

            try
            {
                CancellationToken stepCt = batchCt;

                if (hasTimeout)
                {
                    stepCts = CancellationTokenSource.CreateLinkedTokenSource(batchCt);
                    stepCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    stepCt = stepCts.Token;
                }

                return await executor.ExecuteAsync(invocation, stepCt);
            }
            catch (OperationCanceledException) when (!batchCt.IsCancellationRequested)
            {
                return ConvaiActionExecutionResult.TimedOut(
                    BuildTimeoutMessage(definition, timeoutSeconds));
            }
            catch (OperationCanceledException)
            {
                return ConvaiActionExecutionResult.Canceled();
            }
            catch (Exception ex)
            {
                return ConvaiActionExecutionResult.Failed(ex.Message, ex);
            }
            finally
            {
                stepCts?.Dispose();
            }
        }

        /// <summary>
        ///     Says which action ran out of time, how long it was given, and where that limit came
        ///     from — the three things somebody needs in order to tell a behavior that is stuck from
        ///     one that is simply slower than the limit allows.
        /// </summary>
        private static string BuildTimeoutMessage(ConvaiActionDefinition definition, float timeoutSeconds)
        {
            string actionName = string.IsNullOrWhiteSpace(definition?.ActionName)
                ? "This action"
                : $"'{definition.ActionName}'";

            return definition != null && definition.TimeoutSeconds > 0f
                ? $"{actionName} did not finish within its own Timeout Seconds ({timeoutSeconds:0.##}s), " +
                  "so it was stopped and the rest of the batch was not run."
                : $"{actionName} did not finish within {timeoutSeconds:0.##} seconds, so it was stopped " +
                  "to keep the character's other actions running. Either its Action Behavior is stuck, " +
                  "or it genuinely needs longer — give it its own Timeout Seconds if so.";
        }

        /// <summary>Effective availability of one definition: runtime override wins, authored <see cref="ConvaiActionDefinition.Enabled" /> otherwise.</summary>
        private bool IsActionAvailableForDispatch(ConvaiActionDefinition definition) =>
            _character != null ? _character.Actions.IsDefinitionAvailable(definition) : definition?.Enabled == true;

        /// <summary>Logs the first declined command per disabled action name (Detail-level; never per-command spam).</summary>
        private void LogDisabledActionOnce(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName) || !_loggedDisabledActionNames.Add(actionName.Trim()))
                return;

            ConvaiLogger.Debug(
                $"[ConvaiActionDispatcher] Declining command for disabled action '{actionName}' on '{name}' " +
                "(action disabled — excluded from this character's action config). Reported as unhandled; " +
                "further commands for this action are declined silently.",
                LogCategory.Character);
        }

        private async Task WaitForSpeechGateAsync(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            CancellationToken batchCt)
        {
            // Editor test runs happen without a conversation, so there is no speech to wait for;
            // the internal bypass skips the gate entirely instead of stalling to the timeout.
            if (command?.BypassSpeechGate == true)
                return;

            bool shouldWait = command?.WaitForBotSpeech == true || definition?.WaitForBotSpeech == true;
            if (!shouldWait || _character == null)
                return;

            var gate = new TaskCompletionSource<bool>();
            void Release() => gate.TrySetResult(true);
            void ReleaseTurn(bool _) => gate.TrySetResult(true);

            _character.OnSpeechStarted += Release;
            _character.OnSpeechStopped += Release;
            _character.OnTurnCompleted += ReleaseTurn;
            try
            {
                float timeout = Mathf.Max(0f, _speechGateTimeoutSeconds);
                Task timeoutTask = timeout <= 0f
                    ? Task.CompletedTask
                    : Task.Delay(TimeSpan.FromSeconds(timeout), batchCt);

                await Task.WhenAny(gate.Task, timeoutTask);
                batchCt.ThrowIfCancellationRequested();

                float delay = command?.WaitForBotSpeech == true
                    ? Mathf.Max(0f, command.DelayAfterBotSpeechSeconds)
                    : Mathf.Max(0f, definition?.DelayAfterBotSpeechSeconds ?? 0f);
                if (delay > 0f)
                    await Task.Delay(TimeSpan.FromSeconds(delay), batchCt);
            }
            finally
            {
                _character.OnSpeechStarted -= Release;
                _character.OnSpeechStopped -= Release;
                _character.OnTurnCompleted -= ReleaseTurn;
            }
        }

        private static bool ValidateTargetRequirement(
            ConvaiActionTargetRequirement requirement,
            ConvaiResolvedActionTarget target)
        {
            return requirement switch
            {
                ConvaiActionTargetRequirement.None => true,
                ConvaiActionTargetRequirement.Object => target?.Kind == ConvaiActionTargetKind.Object,
                ConvaiActionTargetRequirement.Character => target?.Kind == ConvaiActionTargetKind.Character,
                ConvaiActionTargetRequirement.Either =>
                    target?.Kind == ConvaiActionTargetKind.Object ||
                    target?.Kind == ConvaiActionTargetKind.Character,
                _ => false
            };
        }

        private bool ShouldAbortNonSuccess(ConvaiActionDefinition definition)
        {
            ConvaiActionBatchFailurePolicy policy = ResolveFailurePolicy(definition);
            return policy == ConvaiActionBatchFailurePolicy.StopBatch;
        }

        private ConvaiActionBatchFailurePolicy ResolveFailurePolicy(ConvaiActionDefinition definition)
        {
            return definition?.FailurePolicyOverride switch
            {
                ConvaiActionFailurePolicyOverride.StopBatch => ConvaiActionBatchFailurePolicy.StopBatch,
                ConvaiActionFailurePolicyOverride.ContinueBatch => ConvaiActionBatchFailurePolicy.ContinueBatch,
                _ => _failurePolicy
            };
        }

        private static string BuildTargetRequirementFailureMessage(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiResolvedActionTarget resolvedTarget)
        {
            string actionName = definition?.ActionName ?? command?.Name ?? string.Empty;
            string targetName = command?.Target ?? string.Empty;
            string resolvedKind = resolvedTarget?.Kind.ToString() ?? "None";
            return $"Action '{actionName}' target '{targetName}' required {definition.TargetRequirement} but resolved {resolvedKind}.";
        }

        private static string BuildFailureMessage(
            ConvaiActionExecutionResult result,
            bool batchWillAbort)
        {
            string message = result.Message;
            if (string.IsNullOrWhiteSpace(message) && result.Exception != null)
                message = result.Exception.Message;

            if (string.IsNullOrWhiteSpace(message))
                message = result.Status.ToString();

            return AppendBatchAbortSuffix(message, batchWillAbort);
        }

        private static string AppendBatchAbortSuffix(string message, bool batchWillAbort) =>
            batchWillAbort ? $"{message} Remaining batch will abort." : $"{message} Remaining batch will continue.";

        private void CancelAllWork()
        {
            lock (_queueLock)
                CancelAllWorkLocked();
        }

        private void CancelAllWorkLocked()
        {
            _processingCts?.Cancel();
            _pendingBatches.Clear();
        }

        /// <summary>Notifies registered performance reactors that a batch has started (post speech-gate).</summary>
        private void NotifyPerformanceBatchStarted()
        {
            if (!_enablePerformanceReactions) return;
            ResolveEmbodimentContext()?.NotifyActionBatchStarted();
        }

        /// <summary>Notifies registered performance reactors that a step has a resolved world-space target.</summary>
        private void NotifyPerformanceTargetAcquired(ConvaiResolvedActionTarget target)
        {
            if (!_enablePerformanceReactions || target == null) return;

            if (!TryResolvePerformanceLookPoint(target, out Vector3 lookPoint)) return;

            ResolveEmbodimentContext()?.NotifyActionTargetAcquired(target.Name, lookPoint);
        }

        /// <summary>
        ///     Where a reactor should aim for this step's target — the drawn volume of the thing
        ///     being acted on, not the spot the character stands to act on it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This used to report <c>InteractionPoint</c>, whose own tooltip calls it the
        ///         point "to move to / aim at". Those are two different points and only one of them
        ///         can be right: a place to walk to is on the floor by definition, and when no
        ///         explicit one is authored it falls back to the target's own transform, whose
        ///         pivot sits on the floor for a great many props. Aimed at raw, a character
        ///         looking where it acts looks at its own feet.
        ///     </para>
        ///     <para>
        ///         The angle is what hid it: it is an arctangent of the distance, so at the far
        ///         end of a walk it is a few degrees and invisible, and at arrival — a metre out,
        ///         with an eye line a metre and a half up — it is nearly sixty degrees down. The
        ///         character walks somewhere perfectly well and then ducks its head at the floor
        ///         exactly as it stops.
        ///     </para>
        ///     <para>
        ///         Aiming at the renderer bounds instead is the same rule
        ///         <c>PlayerAttentionSensor</c> already applies when deciding whether the PLAYER is
        ///         looking at a prop, and for the same stated reason. It was only ever applied in
        ///         one direction. Note this is deliberately not an eye-line lift: a character
        ///         acting on something genuinely low SHOULD look down at it, and the bounds centre
        ///         of a floor button still is low. What it fixes is aiming at a pivot that has
        ///         nothing to do with where the object visibly is.
        ///     </para>
        /// </remarks>
        private bool TryResolvePerformanceLookPoint(
            ConvaiResolvedActionTarget target, out Vector3 lookPoint) =>
            TryResolveLookPoint(
                target.GameObjectReference, target.InteractionPoint,
                _performanceBoundsScratch, out lookPoint);

        /// <summary>
        ///     The look-point rule itself, as a pure function of the target's object and its
        ///     interaction point — so it can be checked without a dispatcher, a batch or a scene.
        /// </summary>
        /// <param name="targetObject">The thing being acted on; null when the target is a bare point.</param>
        /// <param name="interactionPoint">Where the character stands to act. Last resort only.</param>
        /// <param name="scratch">Caller-owned renderer buffer, so this allocates nothing.</param>
        /// <param name="lookPoint">The resolved aim.</param>
        internal static bool TryResolveLookPoint(
            GameObject targetObject,
            Transform interactionPoint,
            List<Renderer> scratch,
            out Vector3 lookPoint)
        {
            lookPoint = default;

            if (targetObject != null)
            {
                targetObject.transform.GetComponentsInChildren(scratch);
                if (scratch.Count > 0)
                {
                    Bounds bounds = scratch[0].bounds;
                    for (int i = 1; i < scratch.Count; i++) bounds.Encapsulate(scratch[i].bounds);

                    scratch.Clear();
                    lookPoint = bounds.center;
                    return true;
                }

                // Nothing drawn to aim at. The pivot is all there is, and it is still a better
                // answer than a stand-here marker that may be metres away from the object.
                lookPoint = targetObject.transform.position;
                return true;
            }

            // No object behind the target at all — a bare interaction point is everything we
            // have, and aiming at it beats reacting to nothing.
            if (interactionPoint == null) return false;

            lookPoint = interactionPoint.position;
            return true;
        }

        /// <summary>Notifies registered performance reactors of a step's outcome.</summary>
        private void NotifyPerformanceOutcome(bool success)
        {
            if (!_enablePerformanceReactions) return;
            ResolveEmbodimentContext()?.NotifyActionOutcome(success);
        }

        private bool EnsureCharacter()
        {
            if (_character == null)
                _character = GetComponent<ConvaiCharacter>();

            return _character != null;
        }

        /// <summary>
        ///     Whether the caller is already on the thread that may touch scene state.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Managed-only, deliberately: this is asked <em>before</em> the marshal, so it may
        ///         not call anything that requires the main thread. That is the whole defect this
        ///         sits in front of.
        ///     </para>
        ///     <para>
        ///         <c>_mainThreadId</c> is recorded in <c>Awake</c>, which edit-mode tooling never
        ///         runs — the Actions Editor's Live and Test Run drive this component directly on a
        ///         scene object that was never started. So an unset id falls back to the scheduler,
        ///         which records the main thread once for the whole SDK. With neither available the
        ///         dispatcher was never initialized, and the only thread that can be reaching it is
        ///         the one holding it.
        ///     </para>
        /// </remarks>
        private bool IsOnDispatcherThread()
        {
            int mainThreadId = _mainThreadId;
            if (mainThreadId != 0)
                return Thread.CurrentThread.ManagedThreadId == mainThreadId;

            return UnityScheduler.Instance?.IsMainThread() ?? true;
        }
    }
}
