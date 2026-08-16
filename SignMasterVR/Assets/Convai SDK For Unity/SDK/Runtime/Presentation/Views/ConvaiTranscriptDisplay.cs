using System.Text;
using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using TMPro;
using UnityEngine;

namespace Convai.Runtime.Presentation.Views
{
    /// <summary>
    ///     Optional companion component for character-local TTS transcript display.
    /// </summary>
    /// <remarks>
    ///     This component follows the composition pattern:
    ///     - Must be attached to the same GameObject as ConvaiCharacter
    ///     - Auto-discovers and subscribes to ConvaiCharacter.OnTranscriptReceived (fed by CharacterTtsTextChunk)
    ///     - Displays TTS text in a TMP_Text component
    ///     - Supports both partial and final transcript display modes
    ///     Note: This is a character-local convenience surface, not the canonical room transcript pipeline.
    ///     Use ConvaiManager.Transcripts.SubscribeCaptions for room-wide live captions and
    ///     ConvaiManager.Transcripts.Subscribe for chat/history turns.
    /// </remarks>
    [AddComponentMenu("Convai/Convai Transcript Display")]
    [RequireComponent(typeof(ConvaiCharacter))]
    public class ConvaiTranscriptDisplay : MonoBehaviour
    {
        #region Public Methods

        /// <summary>Clears the transcript display.</summary>
        public void Clear()
        {
            _buffer?.Clear();
            if (_transcriptText != null) _transcriptText.text = string.Empty;
        }

        #endregion

        #region Serialized Fields

        [Tooltip("The text component that displays the character's spoken transcript.")]
        [SerializeField]
        [ConvaiInspectorSection("UI Reference")]
        private TMP_Text _transcriptText;

        [Tooltip("Whether to show partial (still-being-spoken) transcripts as they arrive, not just finished lines.")]
        [SerializeField]
        [ConvaiInspectorSection("Display Settings")]
        private bool _showPartialTranscripts = true;

        [Tooltip("If enabled, new transcript lines are added below previous ones instead of replacing the text.")]
        [SerializeField]
        [ConvaiInspectorSection("Display Settings")]
        private bool _appendMode;

        [Tooltip("If enabled, the displayed text is cleared each time a new final transcript starts.")]
        [SerializeField]
        [ConvaiInspectorSection("Display Settings")]
        private bool _clearOnNewFinal = true;

        [SerializeField] [Tooltip("Max characters to keep in append mode (0 = unlimited)")]
        [ConvaiInspectorSection("Display Settings")]
        private int _maxCharacters = 1000;

        #endregion

        #region Private Fields

        private ConvaiCharacter _character;
        private StringBuilder _buffer;

        #endregion

        #region Public Properties

        /// <summary>The target TMP_Text for displaying transcripts.</summary>
        public TMP_Text TranscriptText
        {
            get => _transcriptText;
            set => _transcriptText = value;
        }

        /// <summary>Whether to show partial (interim) transcripts.</summary>
        public bool ShowPartialTranscripts
        {
            get => _showPartialTranscripts;
            set => _showPartialTranscripts = value;
        }

        /// <summary>Whether to append transcripts instead of replacing.</summary>
        public bool AppendMode
        {
            get => _appendMode;
            set => _appendMode = value;
        }

        /// <summary>The current transcript text content.</summary>
        public string CurrentText => _transcriptText != null ? _transcriptText.text : string.Empty;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _character = GetComponent<ConvaiCharacter>();
            _buffer = new StringBuilder();
        }

        private void OnEnable()
        {
            if (_character == null)
            {
                ConvaiLogger.Error(
                    $"ConvaiCharacter component not found on {gameObject.name}",
                    LogCategory.UI);
                enabled = false;
                return;
            }

            if (_transcriptText == null)
            {
                ConvaiLogger.Warning($"No TMP_Text assigned on {gameObject.name}. " +
                                     "Assign a TextMeshPro component in the Inspector.", LogCategory.UI);
            }

            _character.OnTranscriptReceived += OnTranscriptReceived;
        }

        private void OnDisable()
        {
            if (_character != null) _character.OnTranscriptReceived -= OnTranscriptReceived;
        }

        #endregion

        #region Private Helpers

        private void OnTranscriptReceived(string transcript, bool isFinal)
        {
            if (_transcriptText == null) return;

            if (!isFinal && !_showPartialTranscripts) return;

            if (_appendMode)
                HandleAppendMode(transcript, isFinal);
            else
                HandleReplaceMode(transcript, isFinal);
        }

        private void HandleReplaceMode(string transcript, bool isFinal)
        {
            if (isFinal && _clearOnNewFinal) _buffer.Clear();

            _transcriptText.text = transcript;
        }

        private void HandleAppendMode(string transcript, bool isFinal)
        {
            if (isFinal)
            {
                _buffer.AppendLine(transcript);
                TrimBufferIfNeeded();
                _transcriptText.text = _buffer.ToString();
            }
            else
                _transcriptText.text = _buffer + transcript;
        }

        private void TrimBufferIfNeeded()
        {
            if (_maxCharacters <= 0 || _buffer.Length <= _maxCharacters) return;

            int excess = _buffer.Length - _maxCharacters;
            _buffer.Remove(0, excess);
        }

        #endregion
    }
}
