using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Convai.Runtime.Components;
using Convai.Runtime.Facades;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Editor.AI
{
    public enum ConvaiRuntimeTraceOperation { Start, Read, Clear, Stop }

    public sealed class ConvaiRuntimeTraceRequest
    {
        public ConvaiRuntimeTraceOperation Operation { get; set; } = ConvaiRuntimeTraceOperation.Read;
        public long ManagerInstanceId { get; set; }
        public long CharacterInstanceId { get; set; }
        public string[] EventFilters { get; set; } = Array.Empty<string>();
        public int Limit { get; set; } = 100;
        public bool CaptureTranscripts { get; set; }
    }

    [InitializeOnLoad]
    internal static class ConvaiRuntimeEventTrace
    {
        internal const int Capacity = 256;
        private static readonly string[] EventNames =
        {
            "OnSessionStateChanged", "OnConnected", "OnDisconnected", "OnSessionError",
            "OnCharacterSpeechStateChanged", "OnCharacterEmotionChanged", "OnCharacterReady",
            "OnCharacterTurnCompleted", "OnPlayerSpeakingStateChanged", "OnMicMuteChanged",
            "OnNarrativeSectionChanged", "OnUsageLimitReached", "OnUserIdleWarningReceived",
            "OnCharacterActionReceived", "OnModerationResponseReceived", "OnLlmNoResponseReceived",
            "OnInteractionCreated", "OnVadSttStateChanged", "OnPipelineError",
            "OnRoomOwnershipRebindStateChanged", "OnDynamicContextUpdateResultReceived"
        };

        private static readonly string[] TranscriptEventNames =
        {
            "OnCharacterTranscriptReceived", "OnPlayerTranscriptReceived", "OnFinalUserTranscriptionReceived"
        };

        internal static IReadOnlyList<string> SubscribedEventNames { get; } =
            EventNames.Concat(TranscriptEventNames).ToArray();

        private static readonly List<TraceEntry> Entries = new(Capacity);
        private static readonly List<Subscription> Subscriptions = new();
        private static ConvaiEvents _events;
        private static long _managerInstanceId;
        private static long _characterInstanceId;
        private static HashSet<string> _filters = new(StringComparer.OrdinalIgnoreCase);
        private static bool _captureTranscripts;

        static ConvaiRuntimeEventTrace()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Reset;
        }

        internal static object Execute(ConvaiRuntimeTraceRequest request)
        {
            request ??= new ConvaiRuntimeTraceRequest();
            return request.Operation switch
            {
                ConvaiRuntimeTraceOperation.Start => Start(request),
                ConvaiRuntimeTraceOperation.Clear => Clear(),
                ConvaiRuntimeTraceOperation.Stop => Stop(),
                _ => Read(request.Limit)
            };
        }

        private static object Start(ConvaiRuntimeTraceRequest request)
        {
            if (!EditorApplication.isPlaying)
                return ConvaiMcpResponses.Envelope(false, "Runtime tracing requires Play Mode; Play Mode was not changed.", new { code = "PLAY_MODE_REQUIRED", tracing = false });

            if (!ConvaiMcpResolvers.TryManager(request.ManagerInstanceId, true,
                    out ConvaiManager manager, out string error))
                return ConvaiMcpResponses.Envelope(false, error, new { code = ConvaiMcpResolvers.ManagerErrorCode, tracing = false });
            ConvaiCharacter character = null;
            if (request.CharacterInstanceId != 0 &&
                !ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true, out character, out error))
                return ConvaiMcpResponses.Envelope(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode, tracing = false });

            ResetSubscriptions();
            try { _events = manager.Events; }
            catch (InvalidOperationException exception)
            {
                return ConvaiMcpResponses.Envelope(false, exception.Message, new { code = "EVENTS_UNAVAILABLE", tracing = false });
            }

            _managerInstanceId = ConvaiMcpEntityRef.ToToolId(manager);
            _characterInstanceId = ConvaiMcpEntityRef.ToToolId(character);
            _filters = new HashSet<string>(request.EventFilters ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _captureTranscripts = request.CaptureTranscripts;
            try
            {
                Subscribe(EventNames);
                if (_captureTranscripts) Subscribe(TranscriptEventNames);
            }
            catch (Exception exception)
            {
                ResetSubscriptions();
                return ConvaiMcpResponses.Envelope(false, $"Runtime event subscription failed: {exception.Message}", new { code = "SUBSCRIPTION_FAILED", tracing = false });
            }
            Add("TraceStarted", "Lifecycle", null);
            return ConvaiMcpResponses.Success("Started Convai runtime event trace.", Snapshot(request.Limit));
        }

        private static object Read(int limit) =>
            ConvaiMcpResponses.Success("Read Convai runtime event trace.", Snapshot(limit));

        private static object Clear()
        {
            Entries.Clear();
            return ConvaiMcpResponses.Success("Cleared Convai runtime event trace.", Snapshot(0));
        }

        private static object Stop()
        {
            bool wasTracing = _events != null;
            ResetSubscriptions();
            return ConvaiMcpResponses.Success(wasTracing ? "Stopped Convai runtime event trace." : "Convai runtime event trace was already stopped.", Snapshot(100));
        }

        private static void Subscribe(IEnumerable<string> eventNames)
        {
            foreach (string eventName in eventNames)
            {
                EventInfo eventInfo = typeof(ConvaiEvents).GetEvent(eventName);
                if (eventInfo == null)
                {
                    Debug.LogWarning(
                        $"[ConvaiRuntimeEventTrace] ConvaiEvents event '{eventName}' was not found; runtime trace subscription skipped.");
                    continue;
                }
                MethodInfo invoke = eventInfo.EventHandlerType?.GetMethod("Invoke");
                ParameterInfo[] parameters = invoke?.GetParameters() ?? Array.Empty<ParameterInfo>();
                Delegate callback;
                if (parameters.Length == 0)
                {
                    Action signal = () => Add(eventName, Categorize(eventName), null);
                    callback = signal;
                }
                else if (parameters.Length == 1)
                {
                    Type adapterType = typeof(EventAdapter<>).MakeGenericType(parameters[0].ParameterType);
                    object adapter = Activator.CreateInstance(adapterType, eventName);
                    MethodInfo handler = adapterType.GetMethod(nameof(EventAdapter<object>.Handle));
                    callback = Delegate.CreateDelegate(eventInfo.EventHandlerType, adapter, handler);
                }
                else continue;
                eventInfo.AddEventHandler(_events, callback);
                Subscriptions.Add(new Subscription(eventInfo, callback));
            }
        }

        private static void Add(string eventType, string category, object payload)
        {
            if (_filters.Count > 0 && !_filters.Contains(eventType) && !_filters.Contains(category)) return;
            string characterId = ReadString(payload, "CharacterId");
            if (_characterInstanceId != 0)
            {
                ConvaiMcpEntityRef.TryResolve(_characterInstanceId, out ConvaiCharacter character);
                if (character != null && !string.IsNullOrEmpty(characterId) &&
                    !string.Equals(character.CharacterId, characterId, StringComparison.Ordinal)) return;
            }

            string text = _captureTranscripts && eventType.Contains("Transcript", StringComparison.OrdinalIgnoreCase)
                ? ReadString(payload, "Text")
                : string.Empty;
            if (Entries.Count == Capacity) Entries.RemoveAt(0);
            Entries.Add(new TraceEntry
            {
                Sequence = Entries.Count == 0 ? 1 : Entries[^1].Sequence + 1,
                TimestampUtc = DateTime.UtcNow.ToString("O"),
                EventType = eventType,
                Category = category,
                CharacterId = characterId,
                TranscriptText = text
            });
        }

        private static object Snapshot(int requestedLimit)
        {
            int limit = Mathf.Clamp(requestedLimit <= 0 ? 100 : requestedLimit, 1, Capacity);
            int skip = Math.Max(0, Entries.Count - limit);
            return new
            {
                tracing = _events != null,
                capacity = Capacity,
                count = Entries.Count,
                managerInstanceId = _managerInstanceId,
                characterInstanceId = _characterInstanceId,
                captureTranscripts = _captureTranscripts,
                eventFilters = _filters.ToArray(),
                entries = Entries.Skip(skip).Select(entry => new
                {
                    sequence = entry.Sequence,
                    timestampUtc = entry.TimestampUtc,
                    eventType = entry.EventType,
                    category = entry.Category,
                    characterId = entry.CharacterId,
                    transcriptText = _captureTranscripts ? entry.TranscriptText : string.Empty
                }).ToArray()
            };
        }

        private static string ReadString(object value, string propertyName)
        {
            if (value == null) return string.Empty;
            object propertyValue = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(value);
            return propertyValue?.ToString() ?? string.Empty;
        }

        private static string Categorize(string eventType)
        {
            if (eventType.Contains("Error", StringComparison.OrdinalIgnoreCase)) return "Error";
            if (eventType.Contains("Action", StringComparison.OrdinalIgnoreCase)) return "Action";
            if (eventType.Contains("Speech", StringComparison.OrdinalIgnoreCase) || eventType.Contains("Mic", StringComparison.OrdinalIgnoreCase)) return "Speech";
            if (eventType.Contains("Turn", StringComparison.OrdinalIgnoreCase)) return "Turn";
            if (eventType.Contains("Narrative", StringComparison.OrdinalIgnoreCase)) return "Narrative";
            if (eventType.Contains("DynamicContext", StringComparison.OrdinalIgnoreCase)) return "DynamicContext";
            if (eventType.Contains("Moderation", StringComparison.OrdinalIgnoreCase)) return "Moderation";
            return "Session";
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode || change == PlayModeStateChange.ExitingPlayMode) Reset();
        }

        private static void Reset()
        {
            ResetSubscriptions();
            Entries.Clear();
        }

        private static void ResetSubscriptions()
        {
            if (_events != null)
                foreach (Subscription subscription in Subscriptions)
                    subscription.Event.RemoveEventHandler(_events, subscription.Callback);
            Subscriptions.Clear();
            _events = null;
            _managerInstanceId = 0;
            _characterInstanceId = 0;
            _filters.Clear();
            _captureTranscripts = false;
        }

        private sealed class TraceEntry
        {
            public int Sequence;
            public string TimestampUtc;
            public string EventType;
            public string Category;
            public string CharacterId;
            public string TranscriptText;
        }

        private sealed class EventAdapter<T>
        {
            private readonly string _eventName;

            public EventAdapter(string eventName) => _eventName = eventName;

            public void Handle(T value) => Add(_eventName, Categorize(_eventName), value);
        }

        private readonly struct Subscription
        {
            public Subscription(EventInfo @event, Delegate callback) { Event = @event; Callback = callback; }
            public EventInfo Event { get; }
            public Delegate Callback { get; }
        }
    }
}

#pragma warning restore CS0618
