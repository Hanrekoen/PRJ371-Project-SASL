using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.Narrative;
using Convai.RestAPI.Internal.Models;
using Convai.Runtime.Behaviors;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiNarrativeDesignTrigger" />: the assigned character,
    ///     the backend-fetched trigger to fire, activation settings, auto-recovery, diagnostics,
    ///     events, collider validation, and a Play-mode runtime readout with a scene-view proximity
    ///     gizmo.
    /// </summary>
    [CustomEditor(typeof(ConvaiNarrativeDesignTrigger))]
    internal sealed class ConvaiNarrativeDesignTriggerEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Narrative Trigger";
        private const string SubtitleText = "Convai Narrative Design Trigger";

        private const string PurposeText =
            "Fires a backend-defined narrative trigger when its activation condition is met.";

        private const string SectionActivationId = "Activation";
        private const string SectionAutoRecoveryId = "AutoRecovery";
        private const string SectionDiagnosticsId = "Diagnostics";
        private const string SectionEventsId = "Events";

        private static readonly GUIContent CharacterSection = new("Character");
        private static readonly GUIContent TriggerSection = new("Trigger");
        private static readonly GUIContent ActivationSection = new("Activation Settings");
        private static readonly GUIContent AutoRecoverySection = new("Auto-Recovery Settings");
        private static readonly GUIContent DiagnosticsSection = new("Diagnostics");
        private static readonly GUIContent EventsSection = new("Events");
        private static readonly GUIContent ActivationRuntimeSection = new("Activation Runtime");
        private static readonly GUIContent DiagnosticsRuntimeSection = new("Runtime Status");

        private static readonly GUIContent AutoFindLabel = new(
            "Auto Find", "Automatically search for a ConvaiCharacter if none is assigned");

        private static readonly GUIContent FetchButtonIdle = new("Fetch");
        private static readonly GUIContent FetchButtonBusy = new("Fetching...");
        private static readonly GUIContent ModeLabel = new("Mode");
        private static readonly GUIContent PlayerTagLabel = new("Player Tag");
        private static readonly GUIContent PlayerLayerLabel = new("Player Layer");
        private static readonly GUIContent DelayLabel = new("Delay (seconds)");
        private static readonly GUIContent RadiusLabel = new("Radius");
        private static readonly GUIContent TriggerOnceLabel = new("Trigger Once");
        private static readonly GUIContent InvokeButton = new("Invoke");
        private static readonly GUIContent ResetButton = new("Reset");

        private static readonly GUIContent AutoFindPlayerLabel = new(
            "Auto Find Player", "Automatically find the player for proximity detection");

        private static readonly GUIContent QueueUntilReadyLabel = new(
            "Queue Until Ready", "Queue trigger until character is in conversation");

        private static readonly GUIContent MaxWaitTimeLabel = new(
            "Max Wait Time", "Maximum seconds to wait (0 = infinite)");

        private static readonly GUIContent ResetOnSceneLoadLabel = new(
            "Reset On Scene Load", "Automatically reset trigger when scene reloads");

        private static readonly GUIContent EnableDiagnosticsLabel = new(
            "Enable Diagnostics", "Log detailed diagnostic info to console");

        private static readonly GUIContent ValidateOnStartLabel = new(
            "Validate On Start", "Run validation checks when the game starts");

        private static readonly GUIContent PrintDiagnosticsButton = new("Print Diagnostics to Console");

        private static readonly GUIContent OnTriggerActivatedLabel = new("On Trigger Activated");
        private static readonly GUIContent OnPlayerEnterZoneLabel = new("On Player Enter Zone");
        private static readonly GUIContent OnPlayerExitZoneLabel = new("On Player Exit Zone");

        private static readonly GUIContent OnTriggerFailedLabel = new(
            "On Trigger Failed", "Called with error message when trigger fails");

        private static readonly GUIContent OnTriggerQueuedLabel = new(
            "On Trigger Queued", "Called when trigger is queued waiting for character");

        private SerializedProperty _activationModeProp;
        private SerializedProperty _autoFindCharacterProp;

        private SerializedProperty _autoFindPlayerProp;
        private SerializedProperty _availableTriggersProp;

        private SerializedProperty _characterComponentProp;

        private SerializedProperty _enableDiagnosticsProp;
        private string _fetchError;
        private bool _isFetching;

        private MonoBehaviour _lastCharacterComponent;
        private SerializedProperty _maxWaitTimeProp;
        private SerializedProperty _onPlayerEnterZoneProp;
        private SerializedProperty _onPlayerExitZoneProp;

        private SerializedProperty _onTriggerActivatedProp;
        private SerializedProperty _onTriggerFailedProp;
        private SerializedProperty _onTriggerQueuedProp;
        private SerializedProperty _playerLayerProp;
        private SerializedProperty _playerTagProp;
        private SerializedProperty _proximityRadiusProp;
        private SerializedProperty _queueUntilReadyProp;
        private SerializedProperty _resetOnSceneLoadProp;
        private SerializedProperty _selectedTriggerIndexProp;
        private SerializedProperty _timeDelayProp;
        private ConvaiNarrativeDesignTrigger _trigger;

        private bool _triggerDetailsFoldout;
        private SerializedProperty _triggerIdProp;
        private SerializedProperty _triggerMessageProp;
        private SerializedProperty _triggerNameProp;
        private SerializedProperty _triggerOnceProp;
        private SerializedProperty _validateOnStartProp;
        private string _lastCharacterId;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => PurposeText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _trigger = (ConvaiNarrativeDesignTrigger)target;

            _characterComponentProp = serializedObject.FindProperty("_characterComponent");
            _autoFindCharacterProp = serializedObject.FindProperty("_autoFindCharacter");
            _selectedTriggerIndexProp = serializedObject.FindProperty("_selectedTriggerIndex");
            _triggerIdProp = serializedObject.FindProperty("_triggerId");
            _triggerNameProp = serializedObject.FindProperty("_triggerName");
            _triggerMessageProp = serializedObject.FindProperty("_triggerMessage");
            _activationModeProp = serializedObject.FindProperty("_activationMode");
            _proximityRadiusProp = serializedObject.FindProperty("_proximityRadius");
            _timeDelayProp = serializedObject.FindProperty("_timeDelay");
            _triggerOnceProp = serializedObject.FindProperty("_triggerOnce");
            _playerLayerProp = serializedObject.FindProperty("_playerLayer");
            _playerTagProp = serializedObject.FindProperty("_playerTag");
            _availableTriggersProp = serializedObject.FindProperty("_availableTriggers");

            _autoFindPlayerProp = serializedObject.FindProperty("_autoFindPlayer");
            _queueUntilReadyProp = serializedObject.FindProperty("_queueUntilReady");
            _maxWaitTimeProp = serializedObject.FindProperty("_maxWaitTime");
            _resetOnSceneLoadProp = serializedObject.FindProperty("_resetOnSceneLoad");

            _enableDiagnosticsProp = serializedObject.FindProperty("_enableDiagnostics");
            _validateOnStartProp = serializedObject.FindProperty("_validateOnStart");

            _onTriggerActivatedProp = serializedObject.FindProperty("_onTriggerActivated");
            _onPlayerEnterZoneProp = serializedObject.FindProperty("_onPlayerEnterZone");
            _onPlayerExitZoneProp = serializedObject.FindProperty("_onPlayerExitZone");
            _onTriggerFailedProp = serializedObject.FindProperty("_onTriggerFailed");
            _onTriggerQueuedProp = serializedObject.FindProperty("_onTriggerQueued");

            _lastCharacterComponent = _characterComponentProp.objectReferenceValue as MonoBehaviour;
            _lastCharacterId = _trigger.GetCharacterId();
        }

        private void OnSceneGUI()
        {
            if (_trigger == null)
                return;

            TriggerActivationMode mode = _trigger.ActivationMode;

            if (mode == TriggerActivationMode.Proximity)
            {
                float radius = _trigger.ProximityRadius;
                // Brand green: the same colour that means "Convai reach" everywhere else.
                Handles.color = Theme.Fade(Theme.Accent, 0.5f);
                Handles.DrawWireDisc(_trigger.transform.position, Vector3.up, radius);

                Handles.color = Theme.Fade(Theme.Accent, 0.1f);
                Handles.DrawSolidDisc(_trigger.transform.position, Vector3.up, radius);

                EditorGUI.BeginChangeCheck();
                float newRadius = Handles.RadiusHandle(Quaternion.identity, _trigger.transform.position, radius);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_trigger, "Change Proximity Radius");
                    _proximityRadiusProp.floatValue = newRadius;
                    serializedObject.ApplyModifiedProperties();
                }

                Handles.Label(_trigger.transform.position + (Vector3.up * 0.5f),
                    $"Proximity: {radius:F1}m",
                    ConvaiEditorStyles.SectionTitle);
            }
        }

        protected override void DrawBody()
        {
            DrawCharacterCard();
            DrawTriggerSelectionCard();
            DrawActivationCard();
            DrawAutoRecoveryCard();
            DrawDiagnosticsCard();
            DrawEventsCard();
            DrawValidationWarnings();

            CheckCharacterChange();
        }

        protected override void DrawLiveSection()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Live, ActivationRuntimeSection);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Has Triggered", _trigger.HasTriggered);
                EditorGUILayout.Toggle("Player In Zone", _trigger.PlayerInZone);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(InvokeButton))
                _trigger.InvokeTrigger();
            if (GUILayout.Button(ResetButton))
                _trigger.ResetTrigger();
            EditorGUILayout.EndHorizontal();

            Theme.EndCard();

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Live, DiagnosticsRuntimeSection);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Current Status", _trigger.CurrentStatus);
                EditorGUILayout.Toggle("Character Ready", _trigger.IsCharacterReady);

                string lastError = _trigger.LastErrorMessage;
                if (!string.IsNullOrEmpty(lastError))
                    EditorGUILayout.TextField("Last Error", lastError);
            }

            GUILayout.Space(2);
            if (GUILayout.Button(PrintDiagnosticsButton))
                _trigger.PrintDiagnostics();

            Theme.EndCard();

            Repaint();
        }

        private void DrawCharacterCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Identity, CharacterSection);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_characterComponentProp, GUIContent.none);
            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();

            EditorGUILayout.PropertyField(_autoFindCharacterProp, AutoFindLabel);

            var charComponent = _characterComponentProp.objectReferenceValue as MonoBehaviour;
            if (charComponent != null && !(charComponent is IConvaiCharacterAgent))
                WarningBox(
                    "Not a Convai character",
                    "The assigned component is not a Convai character, so this trigger has nothing to "
                    + "talk to. Assign the Convai Character component instead, or turn on Auto Find.");
            else if (charComponent == null && !_autoFindCharacterProp.boolValue)
                WarningBox("No character assigned", "Enable 'Auto Find' or assign a character.");

            Theme.EndCard();
        }

        private void DrawTriggerSelectionCard()
        {
            Theme.BeginCard();

            EditorGUILayout.BeginHorizontal();
            Theme.SectionHeader(Glyphs.Content, TriggerSection);

            GUILayout.FlexibleSpace();

            GUI.enabled = !_isFetching && !string.IsNullOrEmpty(_trigger.GetCharacterId());
            if (GUILayout.Button(_isFetching ? FetchButtonBusy : FetchButtonIdle, GUILayout.Width(60)))
                FetchTriggers();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_fetchError))
                ErrorBox("Fetch failed", _fetchError);

            int triggerCount = _availableTriggersProp?.arraySize ?? 0;

            if (triggerCount > 0)
            {
                List<TriggerData> triggers = _trigger.AvailableTriggers;
                string[] triggerOptions = new string[triggerCount + 1];
                triggerOptions[0] = "-- Select Trigger --";

                for (int i = 0; i < triggerCount; i++)
                {
                    TriggerData t = triggers[i];
                    triggerOptions[i + 1] = !string.IsNullOrEmpty(t.TriggerName) ? t.TriggerName : t.TriggerId;
                }

                int currentIndex = _selectedTriggerIndexProp.intValue + 1;
                if (currentIndex < 0)
                    currentIndex = 0;

                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUILayout.Popup(currentIndex, triggerOptions);
                if (EditorGUI.EndChangeCheck())
                {
                    int selectedIndex = newIndex - 1;
                    _selectedTriggerIndexProp.intValue = selectedIndex;

                    if (selectedIndex >= 0 && selectedIndex < triggerCount)
                    {
                        TriggerData selected = triggers[selectedIndex];
                        _triggerIdProp.stringValue = selected.TriggerId;
                        _triggerNameProp.stringValue = selected.TriggerName;
                        _triggerMessageProp.stringValue = string.Empty;
                    }
                    else
                    {
                        _triggerIdProp.stringValue = "";
                        _triggerNameProp.stringValue = "";
                        _triggerMessageProp.stringValue = "";
                    }
                }

                if (_selectedTriggerIndexProp.intValue >= 0 && _selectedTriggerIndexProp.intValue < triggerCount)
                {
                    EditorGUI.indentLevel++;
                    _triggerDetailsFoldout = EditorGUILayout.Foldout(_triggerDetailsFoldout, "Details", true);
                    if (_triggerDetailsFoldout)
                    {
                        TriggerData selectedTrigger = triggers[_selectedTriggerIndexProp.intValue];
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.TextField("ID", selectedTrigger.TriggerId);
                        EditorGUILayout.TextField("Message", selectedTrigger.TriggerMessage ?? "(none)");
                        EditorGUILayout.TextField("Destination", selectedTrigger.DestinationSection ?? "(none)");
                        EditorGUI.EndDisabledGroup();
                    }

                    EditorGUI.indentLevel--;
                }
            }
            else
                InfoBox("No triggers loaded", "Click 'Fetch' to load triggers from backend.");

            Theme.EndCard();
        }

        private void DrawActivationCard()
        {
            if (!DrawSection(SectionActivationId, ActivationSection, Glyphs.Command)) return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_activationModeProp, ModeLabel);

                var mode = (TriggerActivationMode)_activationModeProp.enumValueIndex;

                EditorGUILayout.Space(2);

                switch (mode)
                {
                    case TriggerActivationMode.Collision:
                        EditorGUILayout.PropertyField(_playerTagProp, PlayerTagLabel);
                        EditorGUILayout.PropertyField(_playerLayerProp, PlayerLayerLabel);
                        break;

                    case TriggerActivationMode.TimeBased:
                        EditorGUILayout.PropertyField(_playerTagProp, PlayerTagLabel);
                        EditorGUILayout.PropertyField(_playerLayerProp, PlayerLayerLabel);
                        EditorGUILayout.PropertyField(_timeDelayProp, DelayLabel);
                        break;

                    case TriggerActivationMode.Proximity:
                        EditorGUILayout.PropertyField(_proximityRadiusProp, RadiusLabel);
                        EditorGUILayout.PropertyField(_playerTagProp, PlayerTagLabel);
                        EditorGUILayout.PropertyField(_playerLayerProp, PlayerLayerLabel);
                        break;

                    case TriggerActivationMode.Manual:
                        InfoBox("Manual activation", "Call InvokeTrigger() from code.");
                        break;
                }

                EditorGUILayout.Space(2);

                EditorGUILayout.PropertyField(_triggerOnceProp, TriggerOnceLabel);
            });
        }

        private void DrawAutoRecoveryCard()
        {
            if (!DrawSection(SectionAutoRecoveryId, AutoRecoverySection, Glyphs.Routing, defaultExpanded: false)) return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_autoFindPlayerProp, AutoFindPlayerLabel);
                EditorGUILayout.PropertyField(_queueUntilReadyProp, QueueUntilReadyLabel);

                if (_queueUntilReadyProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_maxWaitTimeProp, MaxWaitTimeLabel);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(_resetOnSceneLoadProp, ResetOnSceneLoadLabel);
            });
        }

        private void DrawDiagnosticsCard()
        {
            if (!DrawSection(SectionDiagnosticsId, DiagnosticsSection, Glyphs.Validation, defaultExpanded: false, accent: Theme.StatusWarn)) return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_enableDiagnosticsProp, EnableDiagnosticsLabel);
                EditorGUILayout.PropertyField(_validateOnStartProp, ValidateOnStartLabel);
            });
        }

        private void DrawEventsCard()
        {
            if (!DrawSection(SectionEventsId, EventsSection, Glyphs.Events, defaultExpanded: false)) return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_onTriggerActivatedProp, OnTriggerActivatedLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_onPlayerEnterZoneProp, OnPlayerEnterZoneLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_onPlayerExitZoneProp, OnPlayerExitZoneLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_onTriggerFailedProp, OnTriggerFailedLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_onTriggerQueuedProp, OnTriggerQueuedLabel);
            });
        }

        private void DrawValidationWarnings()
        {
            var mode = (TriggerActivationMode)_activationModeProp.enumValueIndex;

            if (mode == TriggerActivationMode.Collision || mode == TriggerActivationMode.TimeBased)
            {
                var collider = _trigger.GetComponent<Collider>();
                if (collider == null)
                {
                    GUILayout.Space(4);
                    WarningBox("Missing collider", "Requires a Collider component.",
                        "Add Box Collider", () => Undo.AddComponent<BoxCollider>(_trigger.gameObject));
                }
                else if (!collider.isTrigger)
                {
                    GUILayout.Space(4);
                    WarningBox("Collider is not a trigger", "Enable 'Is Trigger' on Collider.",
                        "Enable Is Trigger", () =>
                        {
                            Undo.RecordObject(collider, "Enable Is Trigger");
                            collider.isTrigger = true;
                        });
                }
            }
        }

        private void FetchTriggers() => _ = FetchTriggersAsync();

        private async Task FetchTriggersAsync()
        {
            if (_isFetching)
                return;

            string characterId = _trigger.GetCharacterId();
            if (string.IsNullOrEmpty(characterId))
            {
                _fetchError = "No character assigned.";
                return;
            }

            _isFetching = true;
            _fetchError = null;
            Repaint();

            try
            {
                FetchResult<List<TriggerData>> result = await NarrativeDesignFetcher.FetchTriggersAsync(characterId);

                if (result.Success)
                {
                    Undo.RecordObject(_trigger, "Fetch Triggers");
                    _trigger.SetAvailableTriggers(result.Data);
                    EditorUtility.SetDirty(_trigger);
                    _fetchError = null;
                }
                else
                    _fetchError = result.Error;
            }
            catch (Exception ex)
            {
                _fetchError = ex.Message;
            }
            finally
            {
                _isFetching = false;
                Repaint();
            }
        }

        private void CheckCharacterChange()
        {
            var currentCharacter = _characterComponentProp.objectReferenceValue as MonoBehaviour;
            string currentCharacterId = _trigger.GetCharacterId();
            bool characterReferenceChanged = currentCharacter != _lastCharacterComponent;
            bool characterIdChanged = !string.Equals(currentCharacterId, _lastCharacterId, StringComparison.Ordinal);

            if (!characterReferenceChanged && !characterIdChanged)
                return;

            _lastCharacterComponent = currentCharacter;
            _lastCharacterId = currentCharacterId;

            Undo.RecordObject(_trigger, "Clear Narrative Trigger Selection");
            _trigger.SetAvailableTriggers(new List<TriggerData>());
            EditorUtility.SetDirty(_trigger);

            if (currentCharacter != null && !string.IsNullOrEmpty(currentCharacterId))
            {
                if (currentCharacter is IConvaiCharacterAgent)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (_trigger != null && !_isFetching)
                            FetchTriggers();
                    };
                }
            }
        }
    }
}
