using System;
using System.Collections.Generic;
using System.Linq;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Implementation of IVisibleCharacterService.
    ///     Manages the list of character IDs currently visible to the player.
    /// </summary>
    /// <remarks>
    ///     This service is thread-safe for basic operations and fires events
    ///     when visibility changes occur. Used by transcript filters and UIs.
    /// </remarks>
    internal class VisibleCharacterService : IVisibleCharacterService, IDisposable
    {
        private readonly object _lock = new();
        private readonly List<string> _visibleCharacterIds = new();

        /// <summary>
        ///     Disposes resources and clears event subscribers.
        /// </summary>
        public void Dispose()
        {
            lock (_lock) _visibleCharacterIds.Clear();
            VisibleCharacterChanged = null;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> VisibleCharacterIds
        {
            get
            {
                lock (_lock) return _visibleCharacterIds.ToList().AsReadOnly();
            }
        }

        /// <inheritdoc />
        public int Count
        {
            get
            {
                lock (_lock) return _visibleCharacterIds.Count;
            }
        }

        /// <inheritdoc />
        public event Action<string, bool> VisibleCharacterChanged;

        /// <inheritdoc />
        public void AddCharacter(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
                return;

            bool added = false;
            lock (_lock)
            {
                if (!_visibleCharacterIds.Contains(characterId))
                {
                    _visibleCharacterIds.Add(characterId);
                    added = true;
                }
            }

            if (added) VisibleCharacterChanged?.Invoke(characterId, true);
        }

        /// <inheritdoc />
        public void RemoveCharacter(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
                return;

            bool removed = false;
            lock (_lock) removed = _visibleCharacterIds.Remove(characterId);

            if (removed) VisibleCharacterChanged?.Invoke(characterId, false);
        }

        /// <inheritdoc />
        public void RemoveAll()
        {
            List<string> toRemove;
            lock (_lock)
            {
                toRemove = _visibleCharacterIds.ToList();
                _visibleCharacterIds.Clear();
            }

            foreach (string id in toRemove) VisibleCharacterChanged?.Invoke(id, false);
        }

        /// <inheritdoc />
        public void RemoveAt(int index)
        {
            string id = string.Empty;
            lock (_lock)
            {
                if (index >= 0 && index < _visibleCharacterIds.Count)
                {
                    id = _visibleCharacterIds[index];
                    _visibleCharacterIds.RemoveAt(index);
                }
            }

            if (!string.IsNullOrEmpty(id)) VisibleCharacterChanged?.Invoke(id, false);
        }

        /// <inheritdoc />
        public bool Contains(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
                return false;

            lock (_lock) return _visibleCharacterIds.Contains(characterId);
        }

        /// <inheritdoc />
        public string GetFirst()
        {
            lock (_lock) return _visibleCharacterIds.Count > 0 ? _visibleCharacterIds[0] : string.Empty;
        }
    }
}
