using System;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Runtime.Components;

namespace Convai.Modules.BodyAnimation.Core.Lifecycle
{
    /// <summary>
    ///     Owns the character's spoken-line feed used by referential gestures and
    ///     co-speech timing — both the direct <see cref="ConvaiCharacter.OnTranscriptReceived" />
    ///     subscription and the <see cref="CharacterTranscriptReceived" /> event-hub fallback, the
    ///     pending-transcript lock (written from an arbitrary thread, drained on the tick) and the
    ///     single pending slot.
    /// </summary>
    /// <remarks>
    ///     Independent of the runtime build/rebuild lifecycle by design (mirroring
    ///     <c>GazeReferentialGlances</c>): <see cref="Attach" /> once in <c>OnEnable</c>,
    ///     <see cref="Detach" /> once in <c>OnDisable</c> — a line that arrives before the runtime
    ///     is built is simply queued and dispatched to nothing by the owner (the referential
    ///     director/co-speech planner aren't built yet), never dropped or thrown on.
    /// </remarks>
    internal sealed class SpokenLineRelay
    {
        private readonly object _lock = new();
        private string _pendingFinalTranscript;

        private ConvaiCharacter _character;
        private bool _characterSubscribed;
        private IEventHub _eventHub;
        private SubscriptionToken _hubToken;

        /// <summary>
        ///     The character resolved by <see cref="SetCharacter" /> — used by the controller's
        ///     random seed resolution (<c>CharacterId</c> when present), independent of whether the
        ///     event subscriptions below ever fire.
        /// </summary>
        internal ConvaiCharacter Character => _character;

        /// <summary>
        ///     Records the character without subscribing yet. Split from <see cref="Attach" /> so
        ///     the controller can resolve <see cref="Character" /> (for its build-time seed) before
        ///     <c>BuildRuntime</c> runs, while the actual event subscription still happens after —
        ///     mirroring the previous ordering exactly.
        /// </summary>
        internal void SetCharacter(ConvaiCharacter character) => _character = character;

        /// <summary>
        ///     Subscribes both feeds (the character set via <see cref="SetCharacter" /> and, once
        ///     available, the event hub). Safe to call more than once: subscribing twice to the
        ///     same character or hub is a no-op.
        /// </summary>
        internal void Attach(IEventHub eventHub)
        {
            if (_character != null && !_characterSubscribed)
            {
                _character.OnTranscriptReceived += HandleTranscriptReceived;
                _characterSubscribed = true;
            }

            if (eventHub != null && _eventHub == null)
            {
                _hubToken = eventHub.Subscribe<CharacterTranscriptReceived>(HandleCharacterTranscriptReceived);
                _eventHub = eventHub;
            }
        }

        /// <summary>Unsubscribes both feeds and drops any pending (not-yet-consumed) line.</summary>
        internal void Detach()
        {
            if (_eventHub != null)
            {
                _eventHub.Unsubscribe(_hubToken);
                _hubToken = default;
                _eventHub = null;
            }

            lock (_lock) _pendingFinalTranscript = null;

            if (_character != null && _characterSubscribed)
            {
                _character.OnTranscriptReceived -= HandleTranscriptReceived;
                _characterSubscribed = false;
            }
        }

        /// <summary>
        ///     Drains the pending slot (if any). Called once per tick; the caller fans the result
        ///     out to whichever consumers are currently built (referential director, co-speech
        ///     planner) — those references change identity across a set-swap handoff, so the fan-
        ///     out itself stays with the tick rather than this always-alive relay.
        /// </summary>
        internal bool TryConsumePending(out string text)
        {
            lock (_lock)
            {
                text = _pendingFinalTranscript;
                _pendingFinalTranscript = null;
            }

            return !string.IsNullOrWhiteSpace(text);
        }

        private void HandleTranscriptReceived(string text, bool isFinal)
        {
            if (!isFinal) return;
            QueueFinalTranscript(text);
        }

        private void HandleCharacterTranscriptReceived(CharacterTranscriptReceived evt)
        {
            if (!evt.IsFinal || _character == null) return;
            if (!string.Equals(evt.CharacterId, _character.CharacterId, StringComparison.OrdinalIgnoreCase)) return;
            QueueFinalTranscript(evt.Text);
        }

        private void QueueFinalTranscript(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_lock) _pendingFinalTranscript = text;
        }
    }
}
