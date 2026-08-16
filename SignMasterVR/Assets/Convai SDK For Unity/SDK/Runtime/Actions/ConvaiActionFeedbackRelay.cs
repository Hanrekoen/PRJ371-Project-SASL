using System;
using System.Collections.Generic;
using System.Text;
using Convai.Domain.EventSystem;
using Convai.Runtime;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     How the feedback relay turns an action-batch outcome into character-facing feedback.
    /// </summary>
    public enum ConvaiActionFeedbackMode
    {
        /// <summary>Nothing is sent to the character for this outcome class.</summary>
        Off = 0,

        /// <summary>
        ///     A compact world-fact sentence is staged silently (<c>run_llm = false</c>): the
        ///     LLM's world state stays correct without narrating.
        /// </summary>
        SilentContext = 1,

        /// <summary>
        ///     The same world-fact sentence is sent with <c>run_llm = true</c> so the character
        ///     voices it naturally, in persona and in the conversation's language.
        /// </summary>
        NarrateInCharacter = 2,

        /// <summary>An authored line template is spoken verbatim via <c>NarrativeDesign.InvokeSpeech</c>.</summary>
        ScriptedSpeech = 3
    }

    /// <summary>
    ///     Per-<see cref="ConvaiActionFailureReason" /> scripted line used by
    ///     <see cref="ConvaiActionFeedbackMode.ScriptedSpeech" />. Supports <c>{action}</c>,
    ///     <c>{target}</c>, and <c>{reason}</c> tokens.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionFeedbackScriptedLine
    {
        [SerializeField]
        [Tooltip("The failure reason this line is spoken for.")]
        private ConvaiActionFailureReason _reason;

        [SerializeField]
        [Tooltip("Tokens: {action}, {target}, {reason}.")]
        private string _line = string.Empty;

        /// <summary>The failure reason this line answers.</summary>
        public ConvaiActionFailureReason Reason => _reason;

        /// <summary>The authored line template.</summary>
        public string Line => _line;

        public ConvaiActionFeedbackScriptedLine()
        {
        }

        public ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason reason, string line)
        {
            _reason = reason;
            _line = line ?? string.Empty;
        }

        /// <summary>Default authored line set covering every structured failure reason.</summary>
        public static ConvaiActionFeedbackScriptedLine[] CreateDefaultFailureLines() => new[]
        {
            new ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason.TargetMissing, "I can't find {target}."),
            new ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason.TargetUnreachable, "I can't reach {target}."),
            new ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason.PathBlocked, "Something's blocking my way to {target}."),
            new ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason.PeerMissing, "I'm not able to {action} right now."),
            new ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason.InvalidState, "I can't {action} right now."),
            new ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason.Timeout, "I couldn't finish {action} in time."),
            new ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason.Interrupted, "I stopped {action} partway through."),
            new ConvaiActionFeedbackScriptedLine(ConvaiActionFailureReason.Custom, "I couldn't {action}.")
        };
    }

    /// <summary>
    ///     Subscribes to a <see cref="ConvaiActionDispatcher" /> on the same <see cref="GameObject" />
    ///     and reports batch outcomes back to the character over the existing dynamic-context /
    ///     narrative-trigger channels — the LLM otherwise assumes every emitted action succeeded.
    /// </summary>
    /// <remarks>
    ///     At most one feedback item is emitted per batch: the first hard failure if one occurred,
    ///     otherwise a success summary. Success and failure outcomes are configured independently
    ///     via <see cref="FailureFeedbackMode" />/<see cref="SuccessFeedbackMode" />.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Convai Action Feedback Relay")]
    [RequireComponent(typeof(ConvaiActionDispatcher))]
    [DisallowMultipleComponent]
    // ExecuteAlways keeps Awake/OnEnable active outside play mode, mirroring
    // ConvaiActionDispatcher: without it this relay never subscribes to the dispatcher's events in
    // EditMode (editor tooling, EditMode tests), since regular MonoBehaviours don't run their
    // lifecycle methods outside play mode.
    [ExecuteAlways]
    public sealed class ConvaiActionFeedbackRelay : MonoBehaviour
    {
        [Header("Modes")]
        [SerializeField]
        [Tooltip("How a batch-ending hard failure is reported to the character.")]
        private ConvaiActionFeedbackMode _failureFeedbackMode = ConvaiActionFeedbackMode.NarrateInCharacter;

        [SerializeField]
        [Tooltip("How an all-succeeded (or unhandled-only) batch is reported to the character.")]
        private ConvaiActionFeedbackMode _successFeedbackMode = ConvaiActionFeedbackMode.SilentContext;

        [SerializeField]
        [Tooltip("How a command that never ran is reported to the character — one the Convai " +
                 "Character asked for but that could not be matched to anything in the scene. Off " +
                 "by default: the character says nothing and the reason is written to the console " +
                 "for you instead. Turn this on when a character that silently ignores a request " +
                 "would be worse than one that admits it cannot do it.")]
        private ConvaiActionFeedbackMode _droppedCommandFeedbackMode = ConvaiActionFeedbackMode.Off;

        [Header("Guards")]
        [SerializeField, Min(0f)]
        [Tooltip("Minimum seconds between spoken narrations (NarrateInCharacter/ScriptedSpeech). " +
                 "SilentContext facts are exempt. A narration inside the cooldown window is " +
                 "downgraded to SilentContext instead of being dropped.")]
        private float _cooldownSeconds = 10f;

        [Header("Scripted Speech")]
        [SerializeField]
        [Tooltip("Per-failure-reason lines used when Failure Feedback Mode is ScriptedSpeech.")]
        private ConvaiActionFeedbackScriptedLine[] _scriptedFailureLines =
            ConvaiActionFeedbackScriptedLine.CreateDefaultFailureLines();

        [SerializeField]
        [Tooltip("Line spoken when Success Feedback Mode is ScriptedSpeech. Token: {action}.")]
        private string _scriptedSuccessLine = "There, all done.";

        /// <summary>
        ///     How long an answer may wait for the character to stop talking before it is delivered
        ///     silently instead.
        /// </summary>
        /// <remarks>
        ///     Not a setting, because there is no version of this a user would want to tune: an
        ///     answer held past a long utterance has been overtaken by the conversation, and a
        ///     character that finally volunteers it reads worse than one that quietly knew it. The
        ///     answer is never discarded — only its voice is.
        /// </remarks>
        private const float AnswerHoldSeconds = 20f;

        /// <summary>
        ///     Upper bound on answers held at once. One batch produces at most one, so reaching this
        ///     means something is queueing batches faster than the character can speak; the oldest
        ///     are delivered silently rather than accumulated.
        /// </summary>
        private const int MaxHeldAnswers = 8;

        private ConvaiActionDispatcher _dispatcher;
        private ConvaiCharacter _character;
        private readonly List<ConvaiActionStepReport> _batchReports = new();
        private readonly List<HeldAnswer> _heldAnswers = new();
        private bool _subscribedToSpeechEnd;
        private float _lastNarrationRealtime = float.NegativeInfinity;
        private bool _suppressNextAggregatedFact;
        private IEventHub _subscribedDropEventHub;
        private SubscriptionToken _dropDiagnosticToken;

        /// <summary>An answer composed while the character was mid-utterance, waiting for silence.</summary>
        private readonly struct HeldAnswer
        {
            public ConvaiActionFeedbackComposer.Outcome Outcome { get; }
            public ConvaiRespondMode SpokenRespondMode { get; }
            public float QueuedRealtime { get; }

            public HeldAnswer(
                ConvaiActionFeedbackComposer.Outcome outcome,
                ConvaiRespondMode spokenRespondMode,
                float queuedRealtime)
            {
                Outcome = outcome;
                SpokenRespondMode = spokenRespondMode;
                QueuedRealtime = queuedRealtime;
            }
        }

        /// <summary>How a batch-ending hard failure is reported to the character.</summary>
        public ConvaiActionFeedbackMode FailureFeedbackMode
        {
            get => _failureFeedbackMode;
            set => _failureFeedbackMode = value;
        }

        /// <summary>How an all-succeeded (or unhandled-only) batch is reported to the character.</summary>
        public ConvaiActionFeedbackMode SuccessFeedbackMode
        {
            get => _successFeedbackMode;
            set => _successFeedbackMode = value;
        }

        /// <summary>
        ///     How a command that was dropped before it could run is reported to the character.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <see cref="ConvaiActionFeedbackMode.Off" /> by default, and that default is the
        ///         considered one rather than the cautious one. A dropped command is usually a naming
        ///         or wiring problem the developer fixes in a minute, and a shipped character that
        ///         announces every one of them is worse than one that quietly does nothing: the
        ///         player hears a stream of apologies for a fault that is not theirs and cannot be
        ///         acted on. The explanation goes to the console, where the person who can fix it is.
        ///     </para>
        ///     <para>
        ///         Turn it on when silence is the worse failure — a companion that appears to ignore
        ///         a direct request reads as broken, while one that says it cannot do that reads as
        ///         honest. <see cref="ConvaiActionFeedbackMode.SilentContext" /> is the middle
        ///         setting and often the right one: the character's own model of the world learns
        ///         that the thing did not happen, without anybody being told about it out loud.
        ///     </para>
        /// </remarks>
        public ConvaiActionFeedbackMode DroppedCommandFeedbackMode
        {
            get => _droppedCommandFeedbackMode;
            set => _droppedCommandFeedbackMode = value;
        }

        /// <summary>Minimum seconds between spoken narrations.</summary>
        public float CooldownSeconds
        {
            get => _cooldownSeconds;
            set => _cooldownSeconds = Mathf.Max(0f, value);
        }

        /// <summary>
        ///     Raised whenever the relay composes a feedback fact, whether or not it was actually
        ///     voiced. <c>narrated</c> is <c>true</c> for NarrateInCharacter/ScriptedSpeech
        ///     delivery, <c>false</c> for SilentContext (including cooldown/speaking downgrades).
        /// </summary>
        public event Action<string, bool> OnFeedbackComposed;

        private void Awake()
        {
            _dispatcher = GetComponent<ConvaiActionDispatcher>();
            _character = GetComponent<ConvaiCharacter>();
        }

        private void OnEnable()
        {
            if (_dispatcher == null) _dispatcher = GetComponent<ConvaiActionDispatcher>();
            if (_character == null) _character = GetComponent<ConvaiCharacter>();
            if (_dispatcher == null)
            {
                enabled = false;
                return;
            }

            _dispatcher.OnStepCompleted.AddListener(HandleStepCompleted);
            _dispatcher.OnBatchCompleted.AddListener(HandleBatchEnded);
            _dispatcher.OnBatchAborted.AddListener(HandleBatchEnded);
            _dispatcher.OnCancelledByUserSpeech += HandleCancelledByUserSpeech;
            SubscribeToDropsIfNeeded();
        }

        private void OnDisable()
        {
            UnsubscribeFromDrops();

            // A held answer cannot outlive the relay that owes it. Dropping the subscription without
            // clearing the list would leave an answer that can never be delivered and a handler that
            // can never be removed.
            UnsubscribeFromSpeechEnd();
            _heldAnswers.Clear();

            if (_dispatcher == null) return;

            _dispatcher.OnStepCompleted.RemoveListener(HandleStepCompleted);
            _dispatcher.OnBatchCompleted.RemoveListener(HandleBatchEnded);
            _dispatcher.OnBatchAborted.RemoveListener(HandleBatchEnded);
            _dispatcher.OnCancelledByUserSpeech -= HandleCancelledByUserSpeech;
            _batchReports.Clear();
            _suppressNextAggregatedFact = false;
        }

        /// <summary>
        ///     Listens for commands dropped before they could run — but only when this relay is
        ///     configured to say something about them.
        /// </summary>
        /// <remarks>
        ///     Not subscribing at all when the mode is <see cref="ConvaiActionFeedbackMode.Off" /> is
        ///     what makes the default free: the shipped configuration adds no handler, does no work
        ///     per response, and cannot narrate by accident.
        /// </remarks>
        private void SubscribeToDropsIfNeeded()
        {
            if (_droppedCommandFeedbackMode == ConvaiActionFeedbackMode.Off) return;
            // Fully qualified: the SDK has its own Convai.Application namespace, so an unqualified
            // Application here binds to that and fails to compile.
            if (!UnityEngine.Application.isPlaying || _subscribedDropEventHub != null) return;

            IEventHub hub = GetComponentInParent<EmbodimentContext>(true)?.EventHub;
            if (hub == null) return;

            _dropDiagnosticToken = hub.Subscribe<ConvaiActionResponseFilterDiagnostic>(HandleCommandsDropped);
            _subscribedDropEventHub = hub;

            // The filter only gathers explanations while something wants them; without this the
            // relay would be handed empty reports and stay silent for reasons nobody could see.
            ConvaiActionDropReporting.AttachTool();
        }

        private void UnsubscribeFromDrops()
        {
            IEventHub hub = _subscribedDropEventHub;
            if (hub == null) return;

            hub.Unsubscribe(_dropDiagnosticToken);
            _dropDiagnosticToken = default;
            _subscribedDropEventHub = null;
            ConvaiActionDropReporting.DetachTool();
        }

        /// <summary>
        ///     Reports at most one dropped command per response, matching the one-item-per-batch rule
        ///     the rest of this relay follows: a Convai Character that lost three commands at once
        ///     has one thing to say about it, not three.
        /// </summary>
        private void HandleCommandsDropped(ConvaiActionResponseFilterDiagnostic diagnostic)
        {
            if (_droppedCommandFeedbackMode == ConvaiActionFeedbackMode.Off) return;
            if (diagnostic.Drops.Count == 0) return;
            if (_character == null ||
                !string.Equals(diagnostic.CharacterId, _character.CharacterId, StringComparison.Ordinal))
                return;

            Deliver(
                ConvaiActionFeedbackComposer.ComposeDrop(diagnostic.Drops[0]),
                _droppedCommandFeedbackMode);
        }

        private void HandleStepCompleted(ConvaiActionStepReport report)
        {
            if (report != null) _batchReports.Add(report);
        }

        private void HandleBatchEnded()
        {
            List<ConvaiActionStepReport> reports = new(_batchReports);
            _batchReports.Clear();

            if (_suppressNextAggregatedFact)
            {
                _suppressNextAggregatedFact = false;
                return;
            }

            ConvaiActionFeedbackComposer.Outcome outcome = ConvaiActionFeedbackComposer.Compose(reports);
            if (outcome.Kind == ConvaiActionFeedbackComposer.OutcomeKind.None) return;

            Deliver(outcome);
        }

        /// <summary>
        ///     A barge-in cancellation (<see cref="ConvaiActionDispatcher.CancelOnUserSpeech" />)
        ///     always reports as a SilentContext interrupted-fact, bypassing the normal
        ///     aggregation/cooldown pipeline, and suppresses the generic aggregated fact the
        ///     dispatcher's own Canceled step report would otherwise produce for the same batch.
        /// </summary>
        private void HandleCancelledByUserSpeech(string interruptedAction)
        {
            _batchReports.Clear();
            _suppressNextAggregatedFact = true;

            string action = string.IsNullOrWhiteSpace(interruptedAction) ? "what it was doing" : interruptedAction;
            string fact = $"You stopped {action} because the player spoke.";
            if (_character != null) _character.DynamicContext.AddEvent(fact, ConvaiRespondMode.Silent);
            OnFeedbackComposed?.Invoke(fact, false);
        }

        private void Deliver(ConvaiActionFeedbackComposer.Outcome outcome) =>
            Deliver(
                outcome,
                outcome.Kind == ConvaiActionFeedbackComposer.OutcomeKind.Failure
                    ? _failureFeedbackMode
                    : _successFeedbackMode);

        /// <summary>
        ///     Sends one composed outcome to the character under an explicit mode.
        /// </summary>
        /// <remarks>
        ///     The mode is a parameter rather than derived from the outcome because a dropped command
        ///     composes as a failure and yet is configured separately: it is a different event with a
        ///     different right answer, and reusing everything below this line — the speaking guard,
        ///     the cooldown, the authored lines — is exactly why it is worth passing in.
        /// </remarks>
        private void Deliver(ConvaiActionFeedbackComposer.Outcome outcome, ConvaiActionFeedbackMode mode)
        {
            if (_character == null) return;

            ConvaiRespondMode spokenRespondMode = ConvaiRespondMode.MustRespond;
            if (outcome.HasAnswer)
                mode = ResolveAnswerMode(outcome, mode, out spokenRespondMode);

            if (mode == ConvaiActionFeedbackMode.Off) return;

            if (outcome.ForceSilent) mode = ConvaiActionFeedbackMode.SilentContext;

            if (RequiresVoice(mode) && _character.IsSpeaking)
            {
                // An answer is not chatter: hold it until the character stops talking rather than
                // discarding its voice. Everything else still degrades, because a character
                // narrating over its own utterance is worse than one that stays quiet about a walk.
                if (outcome.HasAnswer)
                {
                    HoldUntilSpeechEnds(outcome, spokenRespondMode);
                    return;
                }

                mode = ConvaiActionFeedbackMode.SilentContext;
            }

            // The cooldown throttles chatter. An answer is exempt: a visitor who asks two questions
            // six seconds apart is owed two answers, and rate-limiting the second one away is
            // indistinguishable from a character that did not hear the question.
            if (RequiresVoice(mode) && !outcome.HasAnswer && !CooldownElapsed())
                mode = ConvaiActionFeedbackMode.SilentContext;

            Send(outcome, mode, spokenRespondMode);
        }

        /// <summary>
        ///     Applies the per-action answer delivery over the character-wide mode.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The authored setting is the more specific one and wins, exactly as
        ///         <see cref="ConvaiActionFailurePolicyOverride" /> wins over the dispatcher's policy.
        ///         That includes winning over <see cref="ConvaiActionFeedbackMode.Off" />: switching
        ///         off success reporting turns off <em>chatter about completed actions</em>, and an
        ///         override that cannot override the strictest value is not an override.
        ///     </para>
        ///     <para>
        ///         <see cref="ConvaiActionFeedbackMode.ScriptedSpeech" /> can never carry an answer —
        ///         an authored line has only an <c>{action}</c> token, so a character set to scripted
        ///         lines would say "There, all done." instead of what was found. It speaks in its own
        ///         words for answers, and the Actions Editor says so.
        ///     </para>
        /// </remarks>
        private ConvaiActionFeedbackMode ResolveAnswerMode(
            ConvaiActionFeedbackComposer.Outcome outcome,
            ConvaiActionFeedbackMode characterMode,
            out ConvaiRespondMode spokenRespondMode)
        {
            spokenRespondMode = ConvaiRespondMode.MustRespond;

            ConvaiActionFeedbackMode mode = outcome.AnswerDelivery switch
            {
                ConvaiActionAnswerDelivery.RememberOnly => ConvaiActionFeedbackMode.SilentContext,
                ConvaiActionAnswerDelivery.MentionIfRelevant => ConvaiActionFeedbackMode.NarrateInCharacter,
                ConvaiActionAnswerDelivery.TellThePlayer => ConvaiActionFeedbackMode.NarrateInCharacter,
                _ => characterMode
            };

            if (outcome.AnswerDelivery == ConvaiActionAnswerDelivery.MentionIfRelevant)
                spokenRespondMode = ConvaiRespondMode.Auto;

            if (mode == ConvaiActionFeedbackMode.ScriptedSpeech)
                mode = ConvaiActionFeedbackMode.NarrateInCharacter;

            return mode;
        }

        /// <summary>
        ///     Parks an answer that arrived mid-utterance and arranges for it to be delivered when the
        ///     character stops speaking.
        /// </summary>
        /// <remarks>
        ///     Subscribed to only while something is actually waiting — the same
        ///     subscribe-when-needed discipline the dropped-command diagnostics use, so a relay with
        ///     nothing held costs nothing and cannot fire by accident.
        /// </remarks>
        private void HoldUntilSpeechEnds(
            ConvaiActionFeedbackComposer.Outcome outcome,
            ConvaiRespondMode spokenRespondMode)
        {
            if (_heldAnswers.Count >= MaxHeldAnswers)
            {
                ConvaiActionFeedbackComposer.Outcome oldest = _heldAnswers[0].Outcome;
                _heldAnswers.RemoveAt(0);
                Send(oldest, ConvaiActionFeedbackMode.SilentContext, ConvaiRespondMode.Silent);
            }

            _heldAnswers.Add(new HeldAnswer(outcome, spokenRespondMode, Time.realtimeSinceStartup));
            SubscribeToSpeechEndIfNeeded();
        }

        private void SubscribeToSpeechEndIfNeeded()
        {
            if (_subscribedToSpeechEnd || _character == null) return;

            _character.OnSpeechStopped += HandleSpeechEnded;
            _character.OnTurnCompleted += HandleTurnCompleted;
            _subscribedToSpeechEnd = true;
        }

        private void UnsubscribeFromSpeechEnd()
        {
            if (!_subscribedToSpeechEnd || _character == null)
            {
                _subscribedToSpeechEnd = false;
                return;
            }

            _character.OnSpeechStopped -= HandleSpeechEnded;
            _character.OnTurnCompleted -= HandleTurnCompleted;
            _subscribedToSpeechEnd = false;
        }

        private void HandleTurnCompleted(bool _) => HandleSpeechEnded();

        /// <summary>
        ///     Delivers everything that was waiting on the character's voice, oldest first.
        /// </summary>
        /// <remarks>
        ///     An answer held longer than <see cref="AnswerHoldSeconds" /> is delivered silently: the
        ///     conversation has moved on, and a character that finally volunteers a stale reading is
        ///     worse than one that simply knew it.
        /// </remarks>
        private void HandleSpeechEnded()
        {
            if (_heldAnswers.Count == 0)
            {
                UnsubscribeFromSpeechEnd();
                return;
            }

            float now = Time.realtimeSinceStartup;

            // Copied out first: Send reaches game code through OnFeedbackComposed, which is free to
            // enqueue more actions and re-enter this list.
            var due = new List<HeldAnswer>(_heldAnswers);
            _heldAnswers.Clear();
            UnsubscribeFromSpeechEnd();

            for (int i = 0; i < due.Count; i++)
            {
                HeldAnswer held = due[i];
                bool stale = now - held.QueuedRealtime > AnswerHoldSeconds;

                Send(
                    held.Outcome,
                    stale ? ConvaiActionFeedbackMode.SilentContext : ConvaiActionFeedbackMode.NarrateInCharacter,
                    stale ? ConvaiRespondMode.Silent : held.SpokenRespondMode);
            }
        }

        /// <summary>The single place a composed outcome actually leaves this relay.</summary>
        private void Send(
            ConvaiActionFeedbackComposer.Outcome outcome,
            ConvaiActionFeedbackMode mode,
            ConvaiRespondMode spokenRespondMode)
        {
            bool narrated = RequiresVoice(mode);
            if (narrated) _lastNarrationRealtime = Time.realtimeSinceStartup;

            switch (mode)
            {
                case ConvaiActionFeedbackMode.SilentContext:
                    _character.DynamicContext.AddEvent(outcome.Fact, ConvaiRespondMode.Silent);
                    break;
                case ConvaiActionFeedbackMode.NarrateInCharacter:
                    _character.DynamicContext.AddEvent(outcome.Fact, spokenRespondMode);
                    break;
                case ConvaiActionFeedbackMode.ScriptedSpeech:
                    _character.NarrativeDesign.InvokeSpeech(ResolveScriptedLine(
                        outcome, outcome.Kind == ConvaiActionFeedbackComposer.OutcomeKind.Failure));
                    break;
            }

            OnFeedbackComposed?.Invoke(outcome.Fact, narrated);
        }

        private static bool RequiresVoice(ConvaiActionFeedbackMode mode) =>
            mode == ConvaiActionFeedbackMode.NarrateInCharacter || mode == ConvaiActionFeedbackMode.ScriptedSpeech;

        private bool CooldownElapsed() =>
            Time.realtimeSinceStartup - _lastNarrationRealtime >= _cooldownSeconds;

        private string ResolveScriptedLine(ConvaiActionFeedbackComposer.Outcome outcome, bool isFailure)
        {
            if (!isFailure)
                return ApplyTokens(_scriptedSuccessLine, outcome.ActionToken, outcome.TargetToken, string.Empty);

            string template = null;
            for (int i = 0; i < _scriptedFailureLines.Length; i++)
            {
                if (_scriptedFailureLines[i] != null && _scriptedFailureLines[i].Reason == outcome.FailureReason)
                {
                    template = _scriptedFailureLines[i].Line;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(template))
                template = "I couldn't {action}.";

            return ApplyTokens(template, outcome.ActionToken, outcome.TargetToken, outcome.FailureReason.ToString());
        }

        private static string ApplyTokens(string template, string action, string target, string reason)
        {
            var builder = new StringBuilder(template ?? string.Empty);
            builder.Replace("{action}", action ?? string.Empty);
            builder.Replace("{target}", target ?? string.Empty);
            builder.Replace("{reason}", reason ?? string.Empty);
            return builder.ToString();
        }
    }
}
