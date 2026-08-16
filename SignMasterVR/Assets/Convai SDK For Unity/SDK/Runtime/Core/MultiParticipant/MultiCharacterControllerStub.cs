using System;
using System.Collections.Generic;
using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Stub implementation of <see cref="IMultiCharacterController" /> for single-character mode.
    /// </summary>
    /// <remarks>
    ///     In single-character mode, this controller manages a single active character
    ///     which is always the focused character. Multi-character operations degrade gracefully.
    /// </remarks>
    internal sealed class MultiCharacterControllerStub : IMultiCharacterController
    {
        private readonly List<IConvaiCharacterAgent> _activeCharacters = new();

        /// <inheritdoc />
        public IConvaiCharacterAgent FocusedCharacter { get; private set; }

        /// <inheritdoc />
        public IReadOnlyList<IConvaiCharacterAgent> ActiveCharacters => _activeCharacters;

        /// <inheritdoc />
        public int ActiveCount => _activeCharacters.Count;

        /// <inheritdoc />
        public event Action<IConvaiCharacterAgent> FocusChanged;

        /// <inheritdoc />
        public event Action<IConvaiCharacterAgent> CharacterActivated;

        /// <inheritdoc />
        public event Action<IConvaiCharacterAgent> CharacterDeactivated;

        /// <inheritdoc />
        public void SetFocus(IConvaiCharacterAgent character)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));

            if (!_activeCharacters.Contains(character))
                throw new InvalidOperationException("Cannot focus an inactive character.");

            if (FocusedCharacter == character)
                return;

            FocusedCharacter = character;
            FocusChanged?.Invoke(character);
        }

        /// <inheritdoc />
        public void ClearFocus()
        {
            if (FocusedCharacter == null)
                return;

            FocusedCharacter = null;
            FocusChanged?.Invoke(null);
        }

        /// <inheritdoc />
        public void FocusNext()
        {
            if (_activeCharacters.Count == 0)
                return;

            int currentIndex = FocusedCharacter != null
                ? _activeCharacters.IndexOf(FocusedCharacter)
                : -1;

            int nextIndex = (currentIndex + 1) % _activeCharacters.Count;
            SetFocus(_activeCharacters[nextIndex]);
        }

        /// <inheritdoc />
        public void FocusPrevious()
        {
            if (_activeCharacters.Count == 0)
                return;

            int currentIndex = FocusedCharacter != null
                ? _activeCharacters.IndexOf(FocusedCharacter)
                : 0;

            int prevIndex = (currentIndex - 1 + _activeCharacters.Count) % _activeCharacters.Count;
            SetFocus(_activeCharacters[prevIndex]);
        }

        /// <inheritdoc />
        public void Activate(IConvaiCharacterAgent character)
        {
            if (character == null || _activeCharacters.Contains(character))
                return;

            _activeCharacters.Add(character);
            CharacterActivated?.Invoke(character);

            // Auto-focus first character
            if (FocusedCharacter == null)
                SetFocus(character);
        }

        /// <inheritdoc />
        public void Deactivate(IConvaiCharacterAgent character)
        {
            if (character == null || !_activeCharacters.Remove(character))
                return;

            CharacterDeactivated?.Invoke(character);

            // Clear focus if deactivating focused character
            if (FocusedCharacter == character)
            {
                FocusedCharacter = _activeCharacters.Count > 0 ? _activeCharacters[0] : null;
                FocusChanged?.Invoke(FocusedCharacter);
            }
        }

        /// <inheritdoc />
        public void ActivateAll(IEnumerable<IConvaiCharacterAgent> characters)
        {
            foreach (IConvaiCharacterAgent character in characters)
                Activate(character);
        }

        /// <inheritdoc />
        public void DeactivateAll()
        {
            while (_activeCharacters.Count > 0)
                Deactivate(_activeCharacters[0]);
        }

        /// <inheritdoc />
        public void BroadcastText(string text)
        {
            // Stub: In real implementation, send to all active characters
            foreach (IConvaiCharacterAgent character in _activeCharacters)
            {
                // character.SendText(text); // Would call actual method
            }
        }

        /// <inheritdoc />
        public void SendTextToFocused(string text)
        {
            // Stub: In real implementation, send to focused character only
            // _focusedCharacter?.SendText(text);
        }

        /// <inheritdoc />
        public void InterruptAll()
        {
            // Stub: In real implementation, interrupt all active characters
            foreach (IConvaiCharacterAgent character in _activeCharacters)
            {
                // character.Interrupt(); // Would call actual method
            }
        }
    }
}
