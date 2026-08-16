using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Narrative;
using Convai.Domain.Logging;
using Convai.Domain.Narrative;
using Convai.Infrastructure.Services;
using Convai.Runtime.Configuration;
using Convai.Runtime.NarrativeDesign;
using Convai.Runtime.Utilities;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter
    {
        private sealed class CharacterNarrativeDesignFacade : IConvaiNarrativeDesign
        {
            private readonly ConvaiCharacter _owner;
            private readonly Dictionary<string, string> _templateKeys = new();
            private readonly Queue<ConvaiNarrativeTriggerInvocation> _pendingTriggers = new();
            private readonly object _lock = new();
            private bool _hasPendingTemplateKeySync;
            private NarrativeSectionData _currentSectionData;

            public CharacterNarrativeDesignFacade(ConvaiCharacter owner) =>
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));

            public IReadOnlyDictionary<string, string> TemplateKeys => _templateKeys;

            public string CurrentSectionId => _currentSectionData?.SectionId ?? string.Empty;

            public NarrativeSectionData CurrentSectionData => _currentSectionData;

            public event Action<string, string> OnSectionChanged;

            public event Action<NarrativeSectionData> OnSectionDataReceived;

            public event Action<ConvaiNarrativeTriggerInvocation> OnTriggerInvoked;

            public bool SetTemplateKey(string key, string value)
            {
                if (!TryValidateTemplateKey(key)) return false;

                lock (_lock)
                {
                    _templateKeys[key] = value ?? string.Empty;
                }

                if (!_owner.IsInConversation)
                {
                    MarkPendingTemplateKeySync();
                    return true;
                }

                if (TrySendTemplateKeysSnapshot()) return true;

                MarkPendingTemplateKeySync();
                return false;
            }

            public bool SetTemplateKeys(IReadOnlyDictionary<string, string> keys)
            {
                if (keys == null || keys.Count == 0)
                {
                    _owner.Logger?.Warning(
                        $"[{_owner._characterName}] Cannot update empty narrative template keys");
                    return false;
                }

                bool changed = false;
                lock (_lock)
                {
                    foreach (KeyValuePair<string, string> kvp in keys)
                    {
                        if (!TryValidateTemplateKey(kvp.Key)) continue;

                        _templateKeys[kvp.Key] = kvp.Value ?? string.Empty;
                        changed = true;
                    }
                }

                if (!changed) return false;

                if (!_owner.IsInConversation)
                {
                    MarkPendingTemplateKeySync();
                    return true;
                }

                if (TrySendTemplateKeysSnapshot()) return true;

                MarkPendingTemplateKeySync();
                return false;
            }

            public bool InvokeTrigger(string triggerName)
            {
                if (!TryCreateRequest(
                        () => ConvaiNarrativeTriggerRequest.SavedTrigger(triggerName),
                        "Narrative trigger name cannot be empty",
                        out ConvaiNarrativeTriggerRequest request))
                    return false;

                return EnqueueOrSend(request);
            }

            public bool InvokeEvent(string eventMessage)
            {
                if (!TryCreateRequest(
                        () => ConvaiNarrativeTriggerRequest.InlineEvent(eventMessage),
                        "Narrative event message cannot be empty",
                        out ConvaiNarrativeTriggerRequest request))
                    return false;

                return EnqueueOrSend(request);
            }

            public bool InvokeSpeech(string speechText)
            {
                if (!TryCreateRequest(
                        () => ConvaiNarrativeTriggerRequest.ScriptedSpeech(speechText),
                        "Narrative speech text cannot be empty",
                        out ConvaiNarrativeTriggerRequest request))
                    return false;

                return EnqueueOrSend(request);
            }

            private bool TryCreateRequest(
                Func<ConvaiNarrativeTriggerRequest> create,
                string warning,
                out ConvaiNarrativeTriggerRequest request)
            {
                try
                {
                    request = create();
                    return true;
                }
                catch (ArgumentException)
                {
                    _owner.Logger?.Warning(
                        $"[{_owner._characterName}] {warning}");
                    request = default;
                    return false;
                }
            }

            private bool EnqueueOrSend(ConvaiNarrativeTriggerRequest request)
            {
                var invocation = new ConvaiNarrativeTriggerInvocation(request, !_owner.IsInConversation);

                if (!_owner.IsInConversation)
                {
                    lock (_lock) _pendingTriggers.Enqueue(invocation);
                    RaiseTriggerInvoked(invocation);
                    return true;
                }

                if (TrySendTriggerInvocation(invocation))
                {
                    RaiseTriggerInvoked(invocation);
                    return true;
                }

                lock (_lock)
                    _pendingTriggers.Enqueue(new ConvaiNarrativeTriggerInvocation(
                        invocation.Request,
                        true));

                RaiseTriggerInvoked(new ConvaiNarrativeTriggerInvocation(
                    invocation.Request,
                    true));
                return false;
            }

            public Task<NarrativeFetchResult<List<NarrativeSectionInfo>>> FetchSectionsAsync(string apiKey = null)
            {
                INarrativeDesignDataService service = CreateDataService();
                return service.FetchSectionsAsync(_owner.CharacterId, apiKey);
            }

            public Task<NarrativeFetchResult<List<NarrativeTriggerInfo>>> FetchTriggersAsync(string apiKey = null)
            {
                INarrativeDesignDataService service = CreateDataService();
                return service.FetchTriggersAsync(_owner.CharacterId, apiKey);
            }

            internal void HandleNarrativeSectionChanged(NarrativeSectionChanged evt)
            {
                if (!_owner.MatchesCharacterIdentity(evt.CharacterId) && !_owner.MatchesCharacterIdentity(evt.ParticipantId))
                    return;

                var sectionData = new NarrativeSectionData(
                    evt.SectionId,
                    evt.BehaviorTreeCode,
                    evt.BehaviorTreeConstants);

                string previousSectionId = CurrentSectionId;
                _currentSectionData = sectionData;

                if (!string.Equals(previousSectionId, sectionData.SectionId, StringComparison.Ordinal))
                {
                    SafeEventInvoker.Invoke(
                        OnSectionChanged,
                        previousSectionId,
                        sectionData.SectionId,
                        _owner.Logger,
                        "ConvaiCharacter.NarrativeDesign.OnSectionChanged",
                        LogCategory.Character);
                }

                SafeEventInvoker.Invoke(
                    OnSectionDataReceived,
                    sectionData,
                    _owner.Logger,
                    "ConvaiCharacter.NarrativeDesign.OnSectionDataReceived",
                    LogCategory.Character);
            }

            internal void FlushPending()
            {
                if (!_owner.IsInConversation) return;

                if (_hasPendingTemplateKeySync && TrySendTemplateKeysSnapshot())
                    _hasPendingTemplateKeySync = false;

                while (true)
                {
                    ConvaiNarrativeTriggerInvocation invocation;
                    lock (_lock)
                    {
                        if (_pendingTriggers.Count == 0) return;
                        invocation = _pendingTriggers.Peek();
                    }

                    if (!TrySendTriggerInvocation(invocation)) return;

                    lock (_lock) _pendingTriggers.Dequeue();
                }
            }

            internal void MarkPendingReplayAfterDisconnect()
            {
                lock (_lock)
                {
                    if (_templateKeys.Count > 0)
                        _hasPendingTemplateKeySync = true;
                }
            }

            private void MarkPendingTemplateKeySync() => _hasPendingTemplateKeySync = true;

            private bool TrySendTemplateKeysSnapshot()
            {
                Dictionary<string, string> snapshot;
                lock (_lock)
                {
                    if (_templateKeys.Count == 0) return false;
                    snapshot = new Dictionary<string, string>(_templateKeys);
                }

                if (_owner.ConnectionService == null)
                {
                    _owner.Logger?.Warning(
                        $"[{_owner._characterName}] Narrative template key transport unavailable");
                    return false;
                }

                if (_owner.ConnectionService.UpdateTemplateKeys(snapshot)) return true;

                _owner.Logger?.Warning(
                    $"[{_owner._characterName}] Connection not ready for narrative template keys");
                return false;
            }

            private bool TrySendTriggerInvocation(ConvaiNarrativeTriggerInvocation invocation)
            {
                if (_owner.ConnectionService == null)
                {
                    _owner.Logger?.Warning(
                        $"[{_owner._characterName}] Narrative trigger transport unavailable");
                    return false;
                }

                if (_owner.ConnectionService.SendNarrativeTrigger(invocation.Request))
                    return true;

                _owner.Logger?.Warning(
                    $"[{_owner._characterName}] Connection not ready for narrative trigger {invocation.Request.WireFieldValue}");
                return false;
            }

            private bool TryValidateTemplateKey(string key)
            {
                if (!string.IsNullOrWhiteSpace(key)) return true;

                _owner.Logger?.Warning(
                    $"[{_owner._characterName}] Narrative template key name cannot be empty");
                return false;
            }

            private void RaiseTriggerInvoked(ConvaiNarrativeTriggerInvocation invocation) =>
                SafeEventInvoker.Invoke(
                    OnTriggerInvoked,
                    invocation,
                    _owner.Logger,
                    "ConvaiCharacter.NarrativeDesign.OnTriggerInvoked",
                    LogCategory.Character);

            private static INarrativeDesignDataService CreateDataService() =>
                new NarrativeDesignDataService(() => ConvaiSettings.Instance?.ApiKey);
        }

        private CharacterNarrativeDesignFacade _narrativeDesign;

        /// <summary>
        ///     Character-owned runtime surface for narrative design template keys, triggers, and section events.
        /// </summary>
        public IConvaiNarrativeDesign NarrativeDesign => _narrativeDesign ??= new CharacterNarrativeDesignFacade(this);

        private void HandleNarrativeSectionChanged(NarrativeSectionChanged evt) =>
            (_narrativeDesign ??= new CharacterNarrativeDesignFacade(this)).HandleNarrativeSectionChanged(evt);

        private void FlushPendingNarrativeDesign() => _narrativeDesign?.FlushPending();

        private void MarkPendingNarrativeReplayAfterDisconnect() => _narrativeDesign?.MarkPendingReplayAfterDisconnect();
    }
}
