using System;
using System.Collections.Generic;

namespace Convai.Runtime.DynamicContext
{
    internal readonly struct ConvaiDynamicContextStateChangeResult
    {
        internal ConvaiDynamicContextStateChangeResult(bool hasChanged, bool isNew, string previousValue)
        {
            HasChanged = hasChanged;
            IsNew = isNew;
            PreviousValue = previousValue;
        }

        public bool HasChanged { get; }
        public bool IsNew { get; }
        public string PreviousValue { get; }
    }

    internal readonly struct ConvaiDynamicContextBatch
    {
        internal ConvaiDynamicContextBatch(
            string text,
            ConvaiContextUpdateMode mode,
            ConvaiRespondMode reaction,
            bool hasAttention,
            string attentionObject)
        {
            Text = text;
            Mode = mode;
            Reaction = reaction;
            HasAttention = hasAttention;
            AttentionObject = attentionObject;
        }

        public string Text { get; }
        public ConvaiContextUpdateMode Mode { get; }
        public ConvaiRespondMode Reaction { get; }
        public bool HasAttention { get; }
        public string AttentionObject { get; }
    }

    internal sealed class ConvaiDynamicContextTracker
    {
        private const int MaxNewValueWordsInChangedLine = 3;

        private readonly struct StagedStateChange
        {
            internal StagedStateChange(string previousValue, bool hadPreviousValue)
            {
                PreviousValue = previousValue;
                HadPreviousValue = hadPreviousValue;
            }

            public string PreviousValue { get; }
            public bool HadPreviousValue { get; }
        }

        private readonly List<string> _eventLines = new();
        private readonly List<string> _stateOrder = new();
        private readonly Dictionary<string, string> _stateValues = new(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingEventSet = new(StringComparer.Ordinal);
        private readonly List<string> _pendingStateOrder = new();
        private readonly Dictionary<string, StagedStateChange> _pendingStateChanges = new(StringComparer.Ordinal);
        private bool _hasPendingBatch;
        private bool _hasPendingReset;
        private bool _hasPendingTextChange;
        private bool _hasPendingAttention;
        private bool _pendingRemoveStatic;
        private ConvaiRespondMode _pendingReaction = ConvaiRespondMode.Silent;
        private string _pendingAttentionObject;

        public bool HasTrackedContent => _stateOrder.Count > 0 || _eventLines.Count > 0;
        public bool HasPendingBatch => _hasPendingBatch;
        public bool HasPendingReset => _hasPendingReset;
        public bool PendingRemoveStatic => _pendingRemoveStatic;

        public ConvaiDynamicContextStateChangeResult SetState(string name, string value)
        {
            if (_stateValues.TryGetValue(name, out string existingValue))
            {
                if (existingValue == value)
                    return new ConvaiDynamicContextStateChangeResult(false, false, existingValue);

                _stateValues[name] = value;
                return new ConvaiDynamicContextStateChangeResult(true, false, existingValue);
            }

            _stateValues[name] = value;
            _stateOrder.Add(name);
            return new ConvaiDynamicContextStateChangeResult(true, true, null);
        }

        public bool TryGetStateValue(string name, out string value) => _stateValues.TryGetValue(name, out value);

        public void AddEvent(string text) => _eventLines.Add(text);

        public bool RemoveState(string name)
        {
            if (!_stateValues.Remove(name)) return false;

            _stateOrder.Remove(name);
            return true;
        }

        public void Reset()
        {
            _stateValues.Clear();
            _stateOrder.Clear();
            _eventLines.Clear();
        }

        public string BuildCanonicalContext(ICollection<string> excludedStateNames = null)
        {
            if (!HasTrackedContent) return string.Empty;

            var lines = new List<string>(_stateOrder.Count + _eventLines.Count);
            foreach (string stateName in _stateOrder)
            {
                if (excludedStateNames != null && excludedStateNames.Contains(stateName)) continue;
                if (_stateValues.TryGetValue(stateName, out string stateValue))
                    lines.Add($"{stateName} is {stateValue}");
            }

            lines.AddRange(_eventLines);
            return string.Join("\n", lines);
        }

        public bool StageState(
            string name,
            string value,
            ConvaiRespondMode reaction = ConvaiRespondMode.Silent)
        {
            ConvaiDynamicContextStateChangeResult result = SetState(name, value);
            if (!result.HasChanged) return false;

            StageStateChange(name, result);
            MarkPendingBatch(reaction, textChanged: true);
            return true;
        }

        public bool StageEvent(string text, ConvaiRespondMode reaction = ConvaiRespondMode.Auto)
        {
            if (!_pendingEventSet.Add(text)) return false;

            AddEvent(text);
            MarkPendingBatch(reaction, textChanged: true);
            return true;
        }

        public bool StageStateRemoval(string name)
        {
            if (!RemoveState(name)) return false;

            MarkPendingBatch(ConvaiRespondMode.Silent, textChanged: true);
            return true;
        }

        public void StageAttention(
            string currentAttentionObject,
            ConvaiRespondMode reaction = ConvaiRespondMode.Silent)
        {
            _hasPendingAttention = true;
            _pendingAttentionObject = currentAttentionObject;
            MarkPendingBatch(reaction, textChanged: false);
        }

        public void StageReset(bool removeStatic)
        {
            Reset();
            ClearPendingBatch();
            _hasPendingReset = true;
            _pendingRemoveStatic = removeStatic;
        }

        public void StageCanonicalResync()
        {
            if (_hasPendingReset || (!_hasPendingBatch && !HasTrackedContent)) return;

            _hasPendingBatch = true;
            _hasPendingTextChange = true;
        }

        public ConvaiDynamicContextBatch BuildPendingBatch()
        {
            string text = _hasPendingTextChange ? BuildPendingText() : null;
            ConvaiContextUpdateMode mode = _hasPendingTextChange
                ? ConvaiContextUpdateMode.Replace
                : ConvaiContextUpdateMode.Append;

            return new ConvaiDynamicContextBatch(
                text,
                mode,
                _pendingReaction,
                _hasPendingAttention,
                _pendingAttentionObject);
        }

        public void ClearPendingBatch()
        {
            _hasPendingBatch = false;
            _hasPendingTextChange = false;
            _hasPendingAttention = false;
            _pendingAttentionObject = null;
            _pendingReaction = ConvaiRespondMode.Silent;
            _pendingEventSet.Clear();
            _pendingStateOrder.Clear();
            _pendingStateChanges.Clear();
        }

        public void ClearPendingReset()
        {
            _hasPendingReset = false;
            _pendingRemoveStatic = false;
        }

        private void MarkPendingBatch(ConvaiRespondMode reaction, bool textChanged)
        {
            _hasPendingBatch = true;
            _hasPendingReset = false;
            _hasPendingTextChange |= textChanged;
            _pendingReaction = AggregateReaction(_pendingReaction, reaction);
        }

        private void StageStateChange(string name, ConvaiDynamicContextStateChangeResult result)
        {
            if (_pendingStateChanges.ContainsKey(name)) return;

            _pendingStateOrder.Add(name);
            _pendingStateChanges[name] = new StagedStateChange(
                result.PreviousValue,
                !result.IsNew);
        }

        private string BuildPendingText()
        {
            bool shouldIncludeDeltaTail = _pendingReaction != ConvaiRespondMode.Silent;
            List<string> deltaLines = shouldIncludeDeltaTail ? BuildDeltaLines() : null;
            HashSet<string> firstAppearanceKeys = shouldIncludeDeltaTail ? BuildFirstAppearanceKeySet() : null;
            string canonical = BuildCanonicalContext(firstAppearanceKeys);

            if (deltaLines == null || deltaLines.Count == 0) return canonical;
            string deltaText = string.Join("\n", deltaLines);
            return string.IsNullOrEmpty(canonical) ? deltaText : $"{canonical}\n{deltaText}";
        }

        private List<string> BuildDeltaLines()
        {
            var deltaLines = new List<string>(_pendingStateOrder.Count);
            foreach (string stateName in _pendingStateOrder)
            {
                if (!TryGetStateValue(stateName, out string currentValue)) continue;
                if (!_pendingStateChanges.TryGetValue(stateName, out StagedStateChange change)) continue;

                deltaLines.Add(change.HadPreviousValue
                    ? BuildChangedDeltaLine(stateName, change.PreviousValue, currentValue)
                    : $"{stateName} is {currentValue}");
            }

            return deltaLines;
        }

        private HashSet<string> BuildFirstAppearanceKeySet()
        {
            var firstAppearanceKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stateName in _pendingStateOrder)
                if (_pendingStateChanges.TryGetValue(stateName, out StagedStateChange change) &&
                    !change.HadPreviousValue)
                    firstAppearanceKeys.Add(stateName);

            return firstAppearanceKeys;
        }

        private static string BuildChangedDeltaLine(
            string stateName,
            string previousValue,
            string currentValue)
        {
            // Long values already appear in the canonical line; the tail narrates the transition.
            return CountWhitespaceSeparatedWords(currentValue) > MaxNewValueWordsInChangedLine
                ? $"{stateName} changed from {previousValue}"
                : $"{stateName} changed from {previousValue} to {currentValue}";
        }

        private static int CountWhitespaceSeparatedWords(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            int wordCount = 0;
            bool inWord = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    inWord = false;
                    continue;
                }

                if (!inWord)
                {
                    inWord = true;
                    if (++wordCount > MaxNewValueWordsInChangedLine) break;
                }
            }

            return wordCount;
        }

        // Escalation relies on the enum's declaration order: Silent < Auto < MustRespond.
        private static ConvaiRespondMode AggregateReaction(
            ConvaiRespondMode current,
            ConvaiRespondMode incoming) =>
            incoming > current ? incoming : current;
    }
}
