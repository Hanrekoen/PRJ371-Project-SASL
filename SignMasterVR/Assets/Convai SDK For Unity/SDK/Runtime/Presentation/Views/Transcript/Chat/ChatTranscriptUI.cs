using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Facades;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Presentation.Services.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Transcript
{
    /// <summary>
    ///     Reference chat transcript UI built on ConvaiManager.Transcripts.
    /// </summary>
    public class ChatTranscriptUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField]
        private ScrollRect scrollRect;

        [SerializeField] private RectTransform chatContainer;
        [SerializeField] private GameObject characterMessagePrefab;
        [SerializeField] private GameObject playerMessagePrefab;
        [SerializeField] private TMP_InputField chatInputField;

        [Header("Fade Settings")]
        [SerializeField]
        private CanvasFader canvasFader;

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeDuration = 0.5f;

        private readonly Dictionary<string, GameObject> _messageRowsByTurnId = new();
        private readonly HashSet<string> _locallyHiddenTurnIds = new();
        private ConvaiTranscripts _boundTranscripts;
        private IDisposable _transcriptSubscription;
        private IAgentRegistry _agentRegistry;
        private bool _isActive = true;
        private bool _isInjected;
        private IPlayerInputService _playerInput;

        private void Awake()
        {
            if (canvasFader == null)
                canvasFader = GetComponentInChildren<CanvasFader>();
            if (canvasGroup == null)
                canvasGroup = GetComponentInChildren<CanvasGroup>();

            if (chatContainer == null)
                ConvaiLogger.Warning("chatContainer is not assigned - messages will not display",
                    LogCategory.UI);

            if (scrollRect == null)
                ConvaiLogger.Warning("scrollRect is not assigned - auto-scroll will not work",
                    LogCategory.UI);
        }

        private void Start()
        {
            TryResolveDependencies();
            TrySubscribeTranscripts(false);
            StartFadeIn();
        }

        private void OnEnable()
        {
            TryResolveDependencies();
            TrySubscribeTranscripts(true);
            if (chatInputField != null) chatInputField.onSubmit.AddListener(OnChatInputSubmit);
        }

        private void OnDisable()
        {
            if (chatInputField != null) chatInputField.onSubmit.RemoveListener(OnChatInputSubmit);
            UnsubscribeTranscripts();
        }

        private void OnDestroy()
        {
            UnsubscribeTranscripts();
        }

        private void Update()
        {
            TrySubscribeTranscripts(false);

            if (!IsActive || chatInputField == null) return;

            if (!chatInputField.isFocused && IsEnterKeyPressed())
                chatInputField.ActivateInputField();
        }

        public bool IsActive => _isActive && gameObject.activeInHierarchy;

        public void Inject(IAgentRegistry agentRegistry, IPlayerInputService playerInput)
        {
            _agentRegistry = agentRegistry;
            _playerInput = playerInput;
            _isInjected = true;

            if (_playerInput == null)
                ConvaiLogger.Warning("IPlayerInputService not available - text input will not work",
                    LogCategory.UI);
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            gameObject.SetActive(active);

            if (active) StartFadeIn();
        }

        private void StartFadeIn()
        {
            if (canvasFader != null && canvasGroup != null)
                canvasFader.StartFadeIn(canvasGroup, fadeDuration);
        }

        public void ClearAll()
        {
            ClearRenderedRows(true);
        }

        private void ClearRenderedRows(bool rememberAsLocallyHidden)
        {
            if (rememberAsLocallyHidden)
            {
                foreach (string turnId in _messageRowsByTurnId.Keys)
                    _locallyHiddenTurnIds.Add(turnId);
            }

            if (chatContainer != null)
            {
                foreach (Transform child in chatContainer.transform)
                {
                    if (child.gameObject != characterMessagePrefab &&
                        child.gameObject != playerMessagePrefab)
                        Destroy(child.gameObject);
                }
            }

            _messageRowsByTurnId.Clear();
        }

        private void TryResolveDependencies()
        {
            if (_isInjected) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            manager.TryGetAgentRegistry(out IAgentRegistry agentRegistry);
            manager.TryGetPlayerInputService(out IPlayerInputService playerInput);
            Inject(agentRegistry, playerInput);
        }

        private void TrySubscribeTranscripts(bool logFailure)
        {
            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null || !manager.TryGetTranscripts(out ConvaiTranscripts transcripts))
            {
                UnsubscribeTranscripts();
                if (logFailure)
                    ConvaiLogger.Warning("No active ConvaiManager found.", LogCategory.UI);
                return;
            }

            if (ReferenceEquals(_boundTranscripts, transcripts)) return;

            UnsubscribeTranscripts();
            _boundTranscripts = transcripts;
            _boundTranscripts.PresentationEnabledChanged += HandlePresentationEnabledChanged;
            ApplyPresentationEnabled(_boundTranscripts.IsPresentationEnabled);
        }

        private void UnsubscribeTranscripts()
        {
            DisposeTranscriptSubscription();
            if (_boundTranscripts != null)
                _boundTranscripts.PresentationEnabledChanged -= HandlePresentationEnabledChanged;
            _boundTranscripts = null;
        }

        private void HandlePresentationEnabledChanged(bool enabled) => ApplyPresentationEnabled(enabled);

        private void ApplyPresentationEnabled(bool enabled)
        {
            if (!enabled)
            {
                DisposeTranscriptSubscription();
                ClearRenderedRows(false);
                return;
            }

            if (_boundTranscripts == null || _transcriptSubscription != null) return;

            _locallyHiddenTurnIds.IntersectWith(_boundTranscripts.CurrentTimeline.TurnsById.Keys);
            _transcriptSubscription = _boundTranscripts.Subscribe(
                DisplayChange,
                new TranscriptSubscriptionOptions { ReplayExisting = true });
        }

        private void DisposeTranscriptSubscription()
        {
            _transcriptSubscription?.Dispose();
            _transcriptSubscription = null;
        }

        private void DisplayChange(TranscriptChange change)
        {
            if (change == null) return;

            if (change.Kind == TranscriptChangeKind.Removed)
            {
                _locallyHiddenTurnIds.Remove(change.TurnId);
                if (_messageRowsByTurnId.TryGetValue(change.TurnId, out GameObject row))
                {
                    _messageRowsByTurnId.Remove(change.TurnId);
                    if (row != null) Destroy(row);
                }

                return;
            }

            DisplayTurn(change.Turn);
        }

        private void DisplayTurn(TranscriptTurn turn)
        {
            if (turn == null || !turn.HasText || chatContainer == null || _locallyHiddenTurnIds.Contains(turn.Id))
                return;

            bool hadRow = _messageRowsByTurnId.TryGetValue(turn.Id, out GameObject messageObj);
            if (!hadRow)
            {
                GameObject prefab = turn.Speaker?.Type == TranscriptSpeakerType.Character
                    ? characterMessagePrefab
                    : playerMessagePrefab;
                if (prefab == null) return;

                messageObj = Instantiate(prefab, chatContainer);
                messageObj.SetActive(true);
                _messageRowsByTurnId.Add(turn.Id, messageObj);

                if (messageObj.TryGetComponent(out ChatMessageBubble bubble))
                {
                    bubble.Identifier = turn.Speaker?.Type == TranscriptSpeakerType.Character
                        ? turn.Speaker.Id
                        : turn.Id;
                    bubble.SetAgentRegistry(_agentRegistry);
                }
            }

            UpdateMessageBubble(messageObj, turn);
            ScrollToBottom();
        }

        private void UpdateMessageBubble(GameObject messageObj, TranscriptTurn turn)
        {
            if (messageObj.TryGetComponent(out ChatMessageBubble bubble))
            {
                bubble.SetSender(turn.Speaker?.DisplayName ?? string.Empty);
                bubble.SetMessage(turn.DisplayText);
                bubble.IsCompleted = turn.IsCommitted;

                if (turn.Speaker?.Type == TranscriptSpeakerType.Character &&
                    _agentRegistry != null &&
                    _agentRegistry.TryGetCharacter(turn.Speaker.Id, out IConvaiCharacterAgent character))
                    bubble.SetSenderColor(character.NameTagColor);
                return;
            }

            TMP_Text textComponent = messageObj.GetComponentInChildren<TMP_Text>();
            if (textComponent != null)
                textComponent.text = $"{turn.Speaker?.DisplayName}: {turn.DisplayText}";
        }

        private void OnChatInputSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (!_isInjected)
            {
                ConvaiLogger.Warning("Cannot send message - dependencies not injected",
                    LogCategory.UI);
                return;
            }

            if (_playerInput == null || !_playerInput.HasPlayer)
            {
                ConvaiLogger.Info("No player found", LogCategory.UI);
                return;
            }

            chatInputField.SetTextWithoutNotify(string.Empty);
            _playerInput.Player.SendTextMessage(text);
            chatInputField.ActivateInputField();
        }

        private void ScrollToBottom()
        {
            if (scrollRect == null) return;

            Canvas.ForceUpdateCanvases();

            if (chatContainer != null) LayoutRebuilder.ForceRebuildLayoutImmediate(chatContainer);

            scrollRect.verticalNormalizedPosition = 0;
        }

        private static bool IsEnterKeyPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#elif ENABLE_INPUT_SYSTEM
            return IsInputSystemKeyPressedThisFrame("Enter", "NumpadEnter");
#else
            try
            {
                return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            }
            catch
            {
                return false;
            }
#endif
        }

        private static bool _inputSystemReflectionChecked;
        private static Type _keyboardType;
        private static Type _keyType;
        private static System.Reflection.PropertyInfo _currentKeyboardProp;
        private static System.Reflection.PropertyInfo _indexerProp;
        private static System.Reflection.PropertyInfo _wasPressedProperty;
        private static readonly Dictionary<string, object> _inputSystemKeyCache = new();

        private static bool IsInputSystemKeyPressedThisFrame(params string[] keyNames)
        {
            EnsureInputSystemReflection();

            if (_keyboardType == null || _keyType == null || _currentKeyboardProp == null || _indexerProp == null)
                return false;

            object keyboard = _currentKeyboardProp.GetValue(null);
            if (keyboard == null)
                return false;

            for (int i = 0; i < keyNames.Length; i++)
                if (IsInputSystemKeyPressedThisFrame(keyboard, keyNames[i]))
                    return true;

            return false;
        }

        private static void EnsureInputSystemReflection()
        {
            if (_inputSystemReflectionChecked)
                return;

            _inputSystemReflectionChecked = true;
            try
            {
                _keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                _keyType = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
                if (_keyboardType != null && _keyType != null)
                {
                    _currentKeyboardProp = _keyboardType.GetProperty(
                        "current",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    _indexerProp = _keyboardType.GetProperty("Item", new[] { _keyType });
                }
            }
            catch
            {
                // Input System may be absent from host project assemblies.
            }
        }

        private static bool IsInputSystemKeyPressedThisFrame(object keyboard, string keyName)
        {
            if (!TryResolveInputSystemKey(keyName, out object key))
                return false;

            object keyControl = _indexerProp.GetValue(keyboard, new[] { key });
            if (keyControl == null)
                return false;

            _wasPressedProperty ??= keyControl.GetType().GetProperty(
                "wasPressedThisFrame",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            return _wasPressedProperty?.GetValue(keyControl) is bool wasPressed && wasPressed;
        }

        private static bool TryResolveInputSystemKey(string keyName, out object key)
        {
            if (_inputSystemKeyCache.TryGetValue(keyName, out key))
                return true;

            try
            {
                key = Enum.Parse(_keyType, keyName, false);
                _inputSystemKeyCache[keyName] = key;
                return true;
            }
            catch
            {
                key = null;
                return false;
            }
        }
    }
}
