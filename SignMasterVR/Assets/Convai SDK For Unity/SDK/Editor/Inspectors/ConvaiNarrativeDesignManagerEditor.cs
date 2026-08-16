using System.Collections.Generic;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.Narrative;
using Convai.Runtime.Behaviors;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiNarrativeDesignManager" />: the assigned character,
    ///     backend sync status, the synced narrative sections (with orphan indicators), template
    ///     keys, and section-change events.
    /// </summary>
    [CustomEditor(typeof(ConvaiNarrativeDesignManager))]
    internal sealed class ConvaiNarrativeDesignManagerEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Narrative Design";
        private const string SubtitleText = "Convai Narrative Design Manager";

        private const string PurposeText =
            "Syncs narrative sections and template keys from the backend for this character, and " +
            "routes section-change events into the scene.";

        private const string SectionSectionsId = "Sections";
        private const string SectionTemplateKeysId = "TemplateKeys";
        private const string SectionEventsId = "Events";

        private static readonly GUIContent CharacterSection = new("Character");
        private static readonly GUIContent SyncSection = new("Sync Status");
        private static readonly GUIContent TemplateKeysSection = new("Template Keys");
        private static readonly GUIContent EventsSection = new("Events");

        private static readonly GUIContent SyncButtonIdle = new("Sync with Backend");
        private static readonly GUIContent SyncButtonBusy = new("Syncing...");
        private static readonly GUIContent SendToServerButton = new("Send to Server");
        private static readonly GUIContent KeysLabel = new("Keys");

        /// <summary>
        ///     Explains the list once, above it, rather than repeating an example on every row.
        /// </summary>
        private static readonly string TemplateKeysNote =
            "Names your narrative design uses to fill in dynamic values, one per row — for example "
            + "player_name or current_quest. They must match the template variables set up for this "
            + "character on the Convai dashboard.";
        private static readonly GUIContent OnSectionStartLabel = new("On Section Start");
        private static readonly GUIContent OnSectionEndLabel = new("On Section End");
        private static readonly GUIContent OnAnySectionChangedLabel = new("On Any Section Changed");
        private static readonly GUIContent OnSectionDataReceivedLabel = new("On Section Data Received");
        private static readonly GUIContent OnSectionsSyncedLabel = new("On Sections Synced");

        private readonly Dictionary<string, bool> _sectionFoldouts = new();
        private readonly GUIContent _sectionsTitle = new(string.Empty);

        private SerializedProperty _characterComponentProp;
        private bool _hasPendingCharacterChange;
        private SerializedProperty _isFetchingProp;

        private MonoBehaviour _lastCharacterComponent;
        private SerializedProperty _lastFetchErrorProp;
        private SerializedProperty _lastSyncTimeProp;
        private string _lastTrackedCharacterId;
        private ConvaiNarrativeDesignManager _manager;
        private SerializedProperty _onAnySectionChangedProp;
        private SerializedProperty _onSectionDataReceivedProp;
        private SerializedProperty _onSectionsSyncedProp;
        private MonoBehaviour _pendingNewCharacter;
        private string _pendingNewCharacterId;
        private SerializedProperty _sectionConfigsProp;
        private SerializedProperty _templateKeysProp;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => PurposeText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _manager = (ConvaiNarrativeDesignManager)target;

            _characterComponentProp = serializedObject.FindProperty("_characterComponent");
            _sectionConfigsProp = serializedObject.FindProperty("_sectionConfigs");
            _templateKeysProp = serializedObject.FindProperty("_templateKeys");
            _isFetchingProp = serializedObject.FindProperty("_isFetching");
            _lastSyncTimeProp = serializedObject.FindProperty("_lastSyncTime");
            _lastFetchErrorProp = serializedObject.FindProperty("_lastFetchError");
            _onAnySectionChangedProp = serializedObject.FindProperty("_onAnySectionChanged");
            _onSectionDataReceivedProp = serializedObject.FindProperty("_onSectionDataReceived");
            _onSectionsSyncedProp = serializedObject.FindProperty("_onSectionsSynced");

            _lastCharacterComponent = _characterComponentProp.objectReferenceValue as MonoBehaviour;
            _lastTrackedCharacterId = GetCharacterIdFromComponent(_lastCharacterComponent);
        }

        protected override void OnBeforeInspectorGUI()
        {
            if (_manager == null)
                return;

            int count = _manager.ActiveSectionCount + _manager.OrphanedSectionCount;
            _sectionsTitle.text = count > 0 ? $"Narrative Sections ({count})" : "Narrative Sections";
        }

        protected override void DrawBody()
        {
            DrawCharacterCard();
            DrawSyncStatusCard();
            DrawSectionsCard();
            DrawTemplateKeysCard();
            DrawEventsCard();

            CheckCharacterChange();
        }

        private void DrawCharacterCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Identity, CharacterSection);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_characterComponentProp, GUIContent.none);
            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();

            var charComponent = _characterComponentProp.objectReferenceValue as MonoBehaviour;
            if (charComponent != null && !(charComponent is IConvaiCharacterAgent))
                WarningBox("Invalid character reference", "Selected component does not implement IConvaiCharacterAgent.");

            Theme.EndCard();
        }

        private void DrawSyncStatusCard()
        {
            if (_manager == null)
                return;

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Routing, SyncSection);

            EditorGUILayout.BeginHorizontal();

            bool isFetching = _isFetchingProp?.boolValue ?? false;
            GUI.enabled = !isFetching && !string.IsNullOrEmpty(_manager.GetCharacterId());

            if (GUILayout.Button(isFetching ? SyncButtonBusy : SyncButtonIdle, GUILayout.Width(130)))
                _manager.FetchAndSyncFromBackend();

            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            int activeCount = _manager.ActiveSectionCount;
            int orphanedCount = _manager.OrphanedSectionCount;

            string statusText = orphanedCount > 0
                ? $"{activeCount} sections, {orphanedCount} orphaned"
                : $"{activeCount} sections";

            EditorGUILayout.LabelField(statusText, Styles.MicroLabelRight, GUILayout.Width(120));

            EditorGUILayout.EndHorizontal();

            string lastSync = _lastSyncTimeProp.stringValue;
            if (!string.IsNullOrEmpty(lastSync))
                EditorGUILayout.LabelField($"Last sync: {lastSync}", ConvaiEditorStyles.MicroLabel);

            string lastError = _lastFetchErrorProp.stringValue;
            if (!string.IsNullOrEmpty(lastError))
                ErrorBox("Sync failed", lastError);

            Theme.EndCard();
        }

        private void DrawSectionsCard()
        {
            if (_manager == null)
                return;

            if (!DrawSection(SectionSectionsId, _sectionsTitle, Glyphs.Content)) return;
            DrawSectionBody(() =>
            {
                if (_sectionConfigsProp == null || _sectionConfigsProp.arraySize == 0)
                    InfoBox("No sections yet", "Click 'Sync with Backend' to fetch.");
                else
                {
                    GUILayout.Space(4);

                    for (int i = 0; i < _sectionConfigsProp.arraySize; i++)
                    {
                        SerializedProperty sectionProp = _sectionConfigsProp.GetArrayElementAtIndex(i);
                        if (sectionProp != null)
                            DrawNarrativeSectionEntry(sectionProp, i);
                    }
                }
            });
        }

        private void DrawNarrativeSectionEntry(SerializedProperty sectionProp, int index)
        {
            SerializedProperty sectionIdProp = sectionProp.FindPropertyRelative("_sectionId");
            SerializedProperty sectionNameProp = sectionProp.FindPropertyRelative("_sectionName");
            SerializedProperty isOrphanedProp = sectionProp.FindPropertyRelative("_isOrphaned");
            SerializedProperty onStartProp = sectionProp.FindPropertyRelative("_onSectionStart");
            SerializedProperty onEndProp = sectionProp.FindPropertyRelative("_onSectionEnd");

            string sectionId = sectionIdProp?.stringValue ?? "";
            string sectionName = sectionNameProp?.stringValue ?? "";
            bool isOrphaned = isOrphanedProp?.boolValue ?? false;

            string foldoutKey = string.IsNullOrEmpty(sectionId) ? $"section_{index}" : sectionId;
            if (!_sectionFoldouts.ContainsKey(foldoutKey))
                _sectionFoldouts[foldoutKey] = false;

            string displayName = !string.IsNullOrEmpty(sectionName) ? sectionName : sectionId;

            if (isOrphaned)
                displayName = $"{Glyphs.Status.Warn} {displayName} (orphaned)";

            _sectionFoldouts[foldoutKey] = EditorGUILayout.Foldout(_sectionFoldouts[foldoutKey], displayName, true);

            if (_sectionFoldouts[foldoutKey])
            {
                EditorGUI.indentLevel++;

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("Section ID", sectionId);
                EditorGUI.EndDisabledGroup();

                if (isOrphaned)
                    WarningBox("Orphaned section", "Deleted on backend. Events preserved.");

                EditorGUILayout.Space(2);

                EditorGUILayout.PropertyField(onStartProp, OnSectionStartLabel);
                EditorGUILayout.PropertyField(onEndProp, OnSectionEndLabel);

                EditorGUILayout.Space(4);

                EditorGUI.indentLevel--;
            }
        }

        private void DrawTemplateKeysCard()
        {
            if (!DrawSection(SectionTemplateKeysId, TemplateKeysSection, Glyphs.Content, defaultExpanded: false)) return;
            DrawSectionBody(() =>
            {
                if (_templateKeysProp != null)
                    EditorGUILayout.PropertyField(_templateKeysProp, KeysLabel, true);

                GUILayout.Space(4f);
                GUILayout.Label(TemplateKeysNote, Theme.MutedWrapped);

                EditorGUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(SendToServerButton, GUILayout.Width(100)))
                {
                    if (UnityEngine.Application.isPlaying)
                        _manager.SendTemplateKeysUpdate();
                    else
                    {
                        EditorUtility.DisplayDialog("Not in Play Mode",
                            "Sending template keys needs a live connection to the character. "
                            + "Enter Play Mode, then press Send to Server again.", "OK");
                    }
                }

                EditorGUILayout.EndHorizontal();
            });
        }

        private void DrawEventsCard()
        {
            if (!DrawSection(SectionEventsId, EventsSection, Glyphs.Events, defaultExpanded: false)) return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_onAnySectionChangedProp, OnAnySectionChangedLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_onSectionDataReceivedProp, OnSectionDataReceivedLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_onSectionsSyncedProp, OnSectionsSyncedLabel);
            });
        }

        private void CheckCharacterChange()
        {
            if (_characterComponentProp == null || _manager == null)
                return;

            var currentCharacter = _characterComponentProp.objectReferenceValue as MonoBehaviour;
            string currentCharacterId = GetCharacterIdFromComponent(currentCharacter);

            bool characterIdChanged = !string.IsNullOrEmpty(_lastTrackedCharacterId) &&
                                      !string.IsNullOrEmpty(currentCharacterId) &&
                                      _lastTrackedCharacterId != currentCharacterId;

            bool hasExistingSections = _manager.ActiveSectionCount > 0 || _manager.OrphanedSectionCount > 0;

            if (characterIdChanged && hasExistingSections)
            {
                if (!_hasPendingCharacterChange)
                {
                    _hasPendingCharacterChange = true;
                    _pendingNewCharacter = currentCharacter;
                    _pendingNewCharacterId = currentCharacterId;

                    EditorApplication.delayCall += ShowCharacterChangeDialog;
                }
            }
            else if (currentCharacter != _lastCharacterComponent && currentCharacter != null)
            {
                _lastCharacterComponent = currentCharacter;
                _lastTrackedCharacterId = currentCharacterId;

                if (currentCharacter is IConvaiCharacterAgent)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (_manager != null && !_manager.IsFetching)
                        {
                            _manager.FetchAndSyncFromBackend();
                            Repaint();
                        }
                    };
                }
            }
        }

        private void ShowCharacterChangeDialog()
        {
            _hasPendingCharacterChange = false;

            if (_manager == null)
                return;

            bool confirmed = EditorUtility.DisplayDialog(
                "Character Change Detected",
                "You are switching to a different character. This will permanently clear all existing narrative section configurations, including any Unity Events (OnSectionStart/OnSectionEnd) you have configured.\n\n" +
                "This is a one-way operation and cannot be undone.\n\n" +
                "Do you want to proceed?",
                "Yes, Clear and Switch",
                "Cancel"
            );

            if (confirmed)
            {
                _manager.ClearAllSectionConfigs();

                EditorUtility.SetDirty(_manager);

                _lastCharacterComponent = _pendingNewCharacter;
                _lastTrackedCharacterId = _pendingNewCharacterId;

                EditorApplication.delayCall += () =>
                {
                    if (_manager != null && !_manager.IsFetching)
                    {
                        _manager.FetchAndSyncFromBackend();
                        Repaint();
                    }
                };
            }
            else
            {
                serializedObject.Update();
                _characterComponentProp.objectReferenceValue = _lastCharacterComponent;
                serializedObject.ApplyModifiedProperties();
                Repaint();
            }

            _pendingNewCharacter = null;
            _pendingNewCharacterId = null;
        }

        private string GetCharacterIdFromComponent(MonoBehaviour component)
        {
            if (component is IConvaiCharacterAgent agent)
                return agent.CharacterId;
            return null;
        }
    }
}
