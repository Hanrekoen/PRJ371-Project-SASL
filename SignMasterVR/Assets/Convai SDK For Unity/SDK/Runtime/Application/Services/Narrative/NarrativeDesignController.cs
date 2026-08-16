using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Domain.Narrative;

namespace Convai.Application.Services.Narrative
{
    /// <summary>
    ///     Controller for Convai Narrative Design integration.
    ///     Engine-agnostic: manages narrative section transitions and template keys using pure C# events.
    /// </summary>
    public class NarrativeDesignController
    {
        private readonly List<NarrativeSection> _sections = new();
        private readonly List<NarrativeTemplateKey> _templateKeys = new();

        /// <summary>List of narrative sections.</summary>
        public IReadOnlyList<NarrativeSection> Sections => _sections;

        /// <summary>Template keys for dynamic placeholder resolution.</summary>
        public IReadOnlyList<NarrativeTemplateKey> TemplateKeys => _templateKeys;

        /// <summary>Gets the current section ID.</summary>
        public string CurrentSectionID { get; private set; } = string.Empty;

        /// <summary>Gets the current section data including behavior tree information.</summary>
        public NarrativeSectionData CurrentSectionData { get; private set; }

        /// <summary>Optional logging delegate for info messages.</summary>
        public Action<string> LogInfo { get; set; }

        /// <summary>Optional logging delegate for warning messages.</summary>
        public Action<string> LogWarning { get; set; }

        /// <summary>Event invoked when template keys need to be sent to the server.</summary>
        public event Action<Dictionary<string, string>> OnTemplateKeysUpdateRequested;

        /// <summary>Event invoked when full section data is received (including behavior tree data).</summary>
        public event Action<NarrativeSectionData> OnSectionDataReceived;

        /// <summary>Event invoked when the current section changes. Provides previous and new section IDs.</summary>
        public event Action<string, string> OnSectionChanged;

        /// <summary>
        ///     Called when a narrative section ID is received from the server.
        /// </summary>
        /// <param name="sectionID">The section ID received.</param>
        public void OnNarrativeDesignSectionReceived(string sectionID) =>
            OnNarrativeDesignSectionDataReceived(new NarrativeSectionData(sectionID));

        /// <summary>
        ///     Called when full narrative section data is received from the server.
        /// </summary>
        /// <param name="sectionData">The section data including BT code and constants.</param>
        public void OnNarrativeDesignSectionDataReceived(NarrativeSectionData sectionData)
        {
            if (sectionData == null || string.IsNullOrEmpty(sectionData.SectionId)) return;

            LogInfo?.Invoke($"Section received: {sectionData.SectionId}, " +
                            $"BT Code: {(string.IsNullOrEmpty(sectionData.BehaviorTreeCode) ? "None" : "Present")}, " +
                            $"BT Constants: {(string.IsNullOrEmpty(sectionData.BehaviorTreeConstants) ? "None" : "Present")}");

            if (CurrentSectionID == sectionData.SectionId)
            {
                CurrentSectionData = sectionData;
                OnSectionDataReceived?.Invoke(sectionData);
                return;
            }

            string previousSectionId = CurrentSectionID;

            if (!string.IsNullOrEmpty(CurrentSectionID))
            {
                NarrativeSection prevSection = FindSection(CurrentSectionID);
                prevSection?.InvokeEnd();
            }

            CurrentSectionID = sectionData.SectionId;
            CurrentSectionData = sectionData;

            NarrativeSection newSection = FindSection(CurrentSectionID);
            newSection?.InvokeStart();

            OnSectionChanged?.Invoke(previousSectionId, CurrentSectionID);
            OnSectionDataReceived?.Invoke(sectionData);
        }

        /// <summary>
        ///     Converts the template keys list to a dictionary.
        /// </summary>
        /// <returns>Dictionary of template keys and values.</returns>
        public Dictionary<string, string> GetTemplateKeys()
        {
            return _templateKeys?
                       .Where(k => !string.IsNullOrEmpty(k.Key))
                       .ToDictionary(key => key.Key, key => key.Value ?? string.Empty)
                   ?? new Dictionary<string, string>();
        }

        /// <summary>
        ///     Updates a template key value. If the key doesn't exist, it will be added.
        /// </summary>
        /// <param name="key">The template key name.</param>
        /// <param name="value">The value to set.</param>
        public void UpdateTemplateKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                LogWarning?.Invoke("Cannot update template key with empty key name.");
                return;
            }

            NarrativeTemplateKey existingKey = _templateKeys.Find(k => k.Key == key);
            if (existingKey != null)
                existingKey.Value = value;
            else
                _templateKeys.Add(new NarrativeTemplateKey(key, value));

            LogInfo?.Invoke($"Template key updated: {key} = {value}");
        }

        /// <summary>
        ///     Updates multiple template keys at once.
        /// </summary>
        /// <param name="keys">Dictionary of key-value pairs to update.</param>
        public void UpdateTemplateKeys(Dictionary<string, string> keys)
        {
            if (keys == null) return;

            foreach (KeyValuePair<string, string> kvp in keys) UpdateTemplateKey(kvp.Key, kvp.Value);
        }

        /// <summary>
        ///     Sends the current template keys to the server.
        ///     Requires the controller to be connected to a character that can send the update.
        /// </summary>
        public void SendTemplateKeysUpdate()
        {
            Dictionary<string, string> keysToSend = GetTemplateKeys();
            if (keysToSend.Count == 0)
            {
                LogWarning?.Invoke("No template keys to send.");
                return;
            }

            OnTemplateKeysUpdateRequested?.Invoke(keysToSend);
            LogInfo?.Invoke($"Sending {keysToSend.Count} template keys to server.");
        }

        /// <summary>
        ///     Updates a template key and immediately sends all keys to the server.
        /// </summary>
        /// <param name="key">The template key name.</param>
        /// <param name="value">The value to set.</param>
        public void UpdateAndSendTemplateKey(string key, string value)
        {
            UpdateTemplateKey(key, value);
            SendTemplateKeysUpdate();
        }

        /// <summary>
        ///     Resets the controller state.
        /// </summary>
        public void Reset()
        {
            CurrentSectionID = string.Empty;
            CurrentSectionData = null;
        }

        #region Section Sync

        /// <summary>
        ///     Synchronizes the local sections list with backend data.
        ///     This method:
        ///     - Updates existing section names
        ///     - Marks sections deleted on backend as orphaned
        ///     - Adds new sections from backend
        /// </summary>
        /// <param name="backendSections">List of sections from the backend.</param>
        /// <returns>Sync result with statistics about what changed.</returns>
        public SectionSyncResult SyncWithBackendData(List<SectionSyncData> backendSections)
        {
            if (backendSections == null) return new SectionSyncResult { Error = "Backend sections list is null." };

            int updated = 0;
            int added = 0;
            int orphaned = 0;
            int reactivated = 0;

            var backendIds = new HashSet<string>();
            foreach (SectionSyncData bs in backendSections)
            {
                if (!string.IsNullOrEmpty(bs.SectionId))
                    backendIds.Add(bs.SectionId);
            }

            foreach (NarrativeSection localSection in _sections)
            {
                if (string.IsNullOrEmpty(localSection.SectionID)) continue;

                SectionSyncData backendMatch = null;
                foreach (SectionSyncData bs in backendSections)
                {
                    if (bs.SectionId == localSection.SectionID)
                    {
                        backendMatch = bs;
                        break;
                    }
                }

                if (backendMatch != null)
                {
                    if (localSection.SectionName != backendMatch.SectionName)
                    {
                        localSection.UpdateName(backendMatch.SectionName);
                        updated++;
                    }

                    if (localSection.IsOrphaned)
                    {
                        localSection.SetOrphaned(false);
                        reactivated++;
                    }
                }
                else
                {
                    if (!localSection.IsOrphaned)
                    {
                        localSection.SetOrphaned(true);
                        orphaned++;
                    }
                }
            }

            foreach (SectionSyncData backendSection in backendSections)
            {
                if (string.IsNullOrEmpty(backendSection.SectionId)) continue;

                NarrativeSection existing = FindSection(backendSection.SectionId);
                if (existing == null)
                {
                    _sections.Add(new NarrativeSection(backendSection.SectionId, backendSection.SectionName));
                    added++;
                }
            }

            return new SectionSyncResult
            {
                Success = true,
                SectionsUpdated = updated,
                SectionsAdded = added,
                SectionsOrphaned = orphaned,
                SectionsReactivated = reactivated
            };
        }

        #endregion

        #region Section Management

        /// <summary>
        ///     Adds a new section to the controller.
        /// </summary>
        /// <param name="sectionId">The section ID.</param>
        /// <param name="sectionName">The section name.</param>
        /// <returns>The created section.</returns>
        public NarrativeSection AddSection(string sectionId, string sectionName)
        {
            NarrativeSection existing = FindSection(sectionId);
            if (existing != null) return existing;

            var section = new NarrativeSection(sectionId, sectionName);
            _sections.Add(section);
            return section;
        }

        /// <summary>
        ///     Finds a section by ID.
        /// </summary>
        /// <param name="sectionId">The section ID to find.</param>
        /// <returns>The section if found, null otherwise.</returns>
        public NarrativeSection FindSection(string sectionId)
        {
            if (string.IsNullOrEmpty(sectionId)) return null;
            return _sections.Find(s => s.SectionID == sectionId);
        }

        /// <summary>
        ///     Gets count of active (non-orphaned) sections.
        /// </summary>
        public int ActiveSectionCount
        {
            get
            {
                int count = 0;
                foreach (NarrativeSection s in _sections)
                {
                    if (!s.IsOrphaned)
                        count++;
                }

                return count;
            }
        }

        /// <summary>
        ///     Gets count of orphaned sections.
        /// </summary>
        public int OrphanedSectionCount
        {
            get
            {
                int count = 0;
                foreach (NarrativeSection s in _sections)
                {
                    if (s.IsOrphaned)
                        count++;
                }

                return count;
            }
        }

        /// <summary>
        ///     Clears all sections. Used when switching to a different character.
        /// </summary>
        public void ClearSections()
        {
            _sections.Clear();
            CurrentSectionID = string.Empty;
            CurrentSectionData = null;
        }

        #endregion

        #region Template Key Management

        /// <summary>
        ///     Adds a template key.
        /// </summary>
        /// <param name="key">The key name.</param>
        /// <param name="value">The value.</param>
        public void AddTemplateKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;

            NarrativeTemplateKey existing = _templateKeys.Find(k => k.Key == key);
            if (existing != null)
                existing.Value = value;
            else
                _templateKeys.Add(new NarrativeTemplateKey(key, value));
        }

        /// <summary>
        ///     Removes a template key.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        public void RemoveTemplateKey(string key) => _templateKeys.RemoveAll(k => k.Key == key);

        /// <summary>
        ///     Clears all template keys.
        /// </summary>
        public void ClearTemplateKeys() => _templateKeys.Clear();

        #endregion
    }
}
