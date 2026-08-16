using System;
using System.Collections.Generic;
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Runtime.Components;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiPlayer" />: the local player's identity, a rolling
    ///     validation report on the scene's player setup, and the effective values in Play mode.
    /// </summary>
    [CustomEditor(typeof(ConvaiPlayer))]
    internal sealed class ConvaiPlayerEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Convai Player";

        private const string PurposeText =
            "Defines the local player's identity for transcripts and conversation ownership. " +
            "Microphone and conversation mode live on ConvaiManager.";

        private const string SectionIdentityId = "Identity";
        private const string SectionValidationId = "Validation";
        private const string SectionRuntimeId = "Runtime";

        private static readonly GUIContent IdentitySection = new("Identity");
        private static readonly GUIContent ValidationSection = new("Validation");
        private static readonly GUIContent RuntimeSection = new("Runtime");

        private static readonly GUIContent ProjectSettingsButton = new(
            "Project Settings", "Opens the Convai SDK project settings.");

        private static readonly GUIContent RefreshChecksButton = new(
            "Refresh Checks", "Re-runs the checks below against the current scene.");

        private PlayerValidationReport _cachedValidationReport;
        private GUIContent _headerChip;
        private SerializedProperty _nameTagColorProp;
        private ConvaiEditorRefreshTimer _validationTimer;
        private ConvaiPlayer _player;
        private SerializedProperty _playerIdProp;
        private SerializedProperty _playerNameProp;

        protected override string Title => TitleText;
        protected override string Purpose => PurposeText;
        protected override GUIContent StatusChip => _headerChip;

        protected override void OnEnable()
        {
            base.OnEnable();

            _player = (ConvaiPlayer)target;
            _playerNameProp = serializedObject.FindProperty("_playerName");
            _playerIdProp = serializedObject.FindProperty("_playerId");
            _nameTagColorProp = serializedObject.FindProperty("_nameTagColor");
            _headerChip = new GUIContent(HeaderStatusText());

            InvalidateValidationCache();
        }

        /// <summary>
        ///     Refreshes the header chip text in place, so the player's name in the header follows
        ///     edits without allocating a <see cref="GUIContent" /> per repaint.
        /// </summary>
        protected override void OnBeforeInspectorGUI()
        {
            if (_headerChip == null)
                return;

            string status = HeaderStatusText();
            if (!string.Equals(_headerChip.text, status, StringComparison.Ordinal))
                _headerChip.text = status;
        }

        protected override void DrawBody()
        {
            // The identity fields are the whole point of this inspector; without them there is nothing
            // bespoke left to draw, so fall through to the attribute-driven renderer rather than
            // showing an empty page.
            if (_playerNameProp == null || _playerIdProp == null || _nameTagColorProp == null)
            {
                DrawGeneratedSections();
                return;
            }

            DrawIdentitySection();
            DrawValidationSection();
        }

        protected override void DrawLiveSection()
        {
            if (!DrawSection(SectionRuntimeId, RuntimeSection, Glyphs.Live,
                    defaultExpanded: false, accent: Theme.StatusInfo)) return;
            DrawSectionBody(() =>
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Effective Player Name", _player.PlayerName ?? string.Empty);
                    EditorGUILayout.TextField("Effective Player ID", _player.PlayerId ?? string.Empty);
                    EditorGUILayout.ColorField("Name Tag Color", _player.NameTagColor);
                }
            });
        }

        private void DrawIdentitySection()
        {
            if (!DrawSection(SectionIdentityId, IdentitySection, Glyphs.Identity)) return;
            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_playerNameProp, ConvaiInspectorContent.PlayerName);
                EditorGUILayout.PropertyField(_playerIdProp, ConvaiInspectorContent.PlayerId);
                EditorGUILayout.PropertyField(_nameTagColorProp, ConvaiInspectorContent.PlayerNameTagColor);
            });
        }

        private void DrawValidationSection()
        {
            if (!DrawSection(SectionValidationId, ValidationSection, Glyphs.Validation,
                    accent: Theme.StatusWarn)) return;
            DrawSectionBody(() =>
            {
                PlayerValidationReport report = GetValidationReport();

                if (report.MessageType == MessageType.Warning)
                    WarningBox("Needs attention", report.Summary);
                else
                    InfoBox("Player setup", report.Summary);

                for (int i = 0; i < report.Messages.Length; i++)
                    GUILayout.Label($"• {report.Messages[i]}", Theme.MutedWrapped);

                GUILayout.Space(4f);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(ProjectSettingsButton, Styles.MiniButton, GUILayout.Width(120f)))
                    SettingsService.OpenProjectSettings("Project/Convai SDK");
                if (GUILayout.Button(RefreshChecksButton, Styles.MiniButton, GUILayout.Width(120f)))
                    InvalidateValidationCache(true);
                EditorGUILayout.EndHorizontal();
            });
        }

        private string HeaderStatusText()
        {
            string playerName = _player != null ? _player.PlayerName : string.Empty;
            return string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName;
        }

        private PlayerValidationReport GetValidationReport()
        {
            if (_validationTimer.ShouldRefresh(_cachedValidationReport != null))
                _cachedValidationReport = BuildValidationReport();

            return _cachedValidationReport;
        }

        private PlayerValidationReport BuildValidationReport()
        {
            string playerName = _playerNameProp.stringValue?.Trim() ?? string.Empty;
            string playerId = _playerIdProp.stringValue?.Trim() ?? string.Empty;
            var manager = FindAnyObjectByType<ConvaiManager>();
            ConvaiPlayer[] players = ConvaiObjectFind.All<ConvaiPlayer>(FindObjectsInactive.Exclude);

            var messages = new List<string>();
            var messageType = MessageType.Info;
            string summary = "Player setup looks healthy.";

            if (string.IsNullOrWhiteSpace(playerName))
            {
                messageType = MessageType.Warning;
                summary = "Player name is empty.";
                messages.Add("Set Player Name so transcripts and debugging use a clear local identity.");
            }

            if (manager == null)
            {
                messageType = MessageType.Warning;
                summary = "ConvaiManager was not found in the scene.";
                messages.Add("Add one ConvaiManager so the player can participate in room setup and conversations.");
            }

            if (players.Length > 1)
            {
                if (messageType != MessageType.Warning)
                {
                    messageType = MessageType.Warning;
                    summary = "Multiple ConvaiPlayer components were found.";
                }

                messages.Add("Multiple players are valid only if you intentionally manage ownership yourself.");
            }

            if (string.IsNullOrWhiteSpace(playerId))
                messages.Add("Player ID is empty, so the SDK will reuse Player Name for local transcript attribution.");

            if (messages.Count == 0)
                messages.Add("Player identity is configured and a ConvaiManager is present.");

            return new PlayerValidationReport(summary, messageType, messages.ToArray());
        }

        private void InvalidateValidationCache(bool forceImmediateRefresh = false)
        {
            _cachedValidationReport = null;
            _validationTimer.Invalidate(forceImmediateRefresh);
        }

        private sealed class PlayerValidationReport
        {
            public PlayerValidationReport(string summary, MessageType messageType, string[] messages)
            {
                Summary = summary;
                MessageType = messageType;
                Messages = messages ?? Array.Empty<string>();
            }

            public string Summary { get; }
            public MessageType MessageType { get; }
            public string[] Messages { get; }
        }
    }
}
