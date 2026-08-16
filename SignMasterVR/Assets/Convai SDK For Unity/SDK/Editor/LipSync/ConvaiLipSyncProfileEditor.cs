#if UNITY_EDITOR
using System;
using Convai.Domain.Models.LipSync;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.LipSync.Profiles;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.LipSync.Editor
{
    [CustomEditor(typeof(ConvaiLipSyncProfile))]
    internal sealed class ConvaiLipSyncProfileEditor : ConvaiInspectorEditor
    {
        private const float CompactLabelWidth = 170f;

        private const string IdentityCaption =
            "Stable key used by profile lookup, component lock, and map targeting.";

        private const string DisplayCaption =
            "Shown in dropdowns and tools only. No runtime transport impact.";

        private const string TransportCaption =
            "Validated against incoming payload format. Mismatch drops packets.";

        private static readonly GUIContent IdentitySection = new("Runtime Identity");
        private static readonly GUIContent DisplaySection = new("Editor Label");
        private static readonly GUIContent TransportSection = new("Backend Transport Token");

        private static readonly GUIContent ProfileIdLabel = new(
            "Profile ID", "Canonical runtime identifier. Example: arkit, metahuman.");

        private static readonly GUIContent DisplayNameLabel = new(
            "Display Name", "Human-readable name for inspector and editor lists.");

        private static readonly GUIContent OverrideToggleLabel = new(
            "Override default token", "Enable only when backend token differs from Profile ID.");

        private static readonly GUIContent TransportTokenLabel = new(
            "Transport Token", "Format token sent to backend and expected in lip sync payloads.");

        private static readonly GUIContent UseProfileIdTokenButton = new("Use Profile ID Token");

        private SerializedProperty _displayNameProp;
        private SerializedProperty _profileIdProp;
        private SerializedProperty _transportFormatProp;
        private bool _transportStateInitialized;

        private bool _useCustomTransportToken;

        private ConvaiLipSyncProfile Asset => (ConvaiLipSyncProfile)target;

        protected override string Title => "Lip Sync Profile";

        protected override string Subtitle =>
            _profileIdProp != null ? $"{Asset.DisplayName} (id: {Asset.ProfileId})" : null;

        protected override void OnEnable()
        {
            base.OnEnable();

            _profileIdProp = serializedObject.FindProperty("_profileId");
            _displayNameProp = serializedObject.FindProperty("_displayName");
            _transportFormatProp = serializedObject.FindProperty("_transportFormat");

            InitializeTransportStateFromSerialized();
        }

        protected override void DrawBody()
        {
            if (_profileIdProp == null || _displayNameProp == null || _transportFormatProp == null)
            {
                // Only reachable when the asset on disk no longer matches this version of the profile
                // type — the fields this editor is built around are simply not there. Unity's raw
                // field list is deliberate here rather than a fallback we forgot to style: it is the
                // only view that can still show whatever the asset does contain, which is what someone
                // recovering an old asset needs. The message above says why it looks different.
                ErrorBox(
                    "This profile does not match the current SDK",
                    "Some of the fields this profile should have are missing, so it cannot be shown " +
                    "the usual way. Everything the asset still contains is listed below — copy what " +
                    "you need into a new Lip Sync Profile, then delete this one.");
                DrawDefaultInspector();
                return;
            }

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = CompactLabelWidth;
            try
            {
                DrawIdentityCard();
                DrawDisplayCard();
                DrawTransportCard();
                DrawValidationMessages();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        private void DrawIdentityCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Identity, IdentitySection);
            GUILayout.Label(IdentityCaption, Theme.SectionSummary);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_profileIdProp, ProfileIdLabel);
            if (EditorGUI.EndChangeCheck())
            {
                _profileIdProp.stringValue = Normalize(_profileIdProp.stringValue);
                if (!_useCustomTransportToken) _transportFormatProp.stringValue = _profileIdProp.stringValue;
            }

            Theme.EndCard();
        }

        private void DrawDisplayCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Identity, DisplaySection);
            GUILayout.Label(DisplayCaption, Theme.SectionSummary);

            EditorGUILayout.PropertyField(_displayNameProp, DisplayNameLabel);

            Theme.EndCard();
        }

        private void DrawTransportCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Routing, TransportSection);
            GUILayout.Label(TransportCaption, Theme.SectionSummary);

            if (!_transportStateInitialized) InitializeTransportStateFromSerialized();

            string normalizedProfileId = Normalize(_profileIdProp.stringValue);
            string normalizedTransport = Normalize(_transportFormatProp.stringValue);
            if (!_useCustomTransportToken &&
                !string.Equals(normalizedTransport, normalizedProfileId, StringComparison.Ordinal))
                _transportFormatProp.stringValue = normalizedProfileId;

            EditorGUI.BeginChangeCheck();
            bool nextUseCustom = EditorGUILayout.ToggleLeft(OverrideToggleLabel, _useCustomTransportToken);
            if (EditorGUI.EndChangeCheck())
            {
                _useCustomTransportToken = nextUseCustom;
                if (!_useCustomTransportToken)
                {
                    GUI.FocusControl(string.Empty);
                    _transportFormatProp.stringValue = normalizedProfileId;
                }
                else if (string.IsNullOrWhiteSpace(_transportFormatProp.stringValue))
                    _transportFormatProp.stringValue = normalizedProfileId;
            }

            if (_useCustomTransportToken)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_transportFormatProp, TransportTokenLabel);
                if (EditorGUI.EndChangeCheck())
                    _transportFormatProp.stringValue = Normalize(_transportFormatProp.stringValue);
            }
            else
            {
                _transportFormatProp.stringValue = normalizedProfileId;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Transport Token", normalizedProfileId);
            }

            if (!_useCustomTransportToken)
                GUILayout.Label($"Using Profile ID token: {ToLabelValue(normalizedProfileId)}", Theme.SectionSummary);
            else if (GUILayout.Button(UseProfileIdTokenButton))
            {
                GUI.FocusControl(string.Empty);
                _useCustomTransportToken = false;
                _transportFormatProp.stringValue = normalizedProfileId;
            }

            Theme.EndCard();
        }

        private void DrawValidationMessages()
        {
            string profileId = Normalize(_profileIdProp.stringValue);
            string transport = Normalize(_transportFormatProp.stringValue);

            if (string.IsNullOrWhiteSpace(profileId))
                ErrorBox("Missing Profile ID", "Profile ID cannot be empty.");

            if (string.IsNullOrWhiteSpace(transport))
                ErrorBox("Missing Transport Token", "Transport Token cannot be empty.");

            if (_useCustomTransportToken &&
                string.Equals(profileId, transport, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(profileId))
                InfoBox("Override Redundant", "Custom override is enabled but token equals Profile ID.");
        }

        private void InitializeTransportStateFromSerialized()
        {
            if (_profileIdProp == null || _transportFormatProp == null) return;

            string profileId = Normalize(_profileIdProp.stringValue);
            string transport = Normalize(_transportFormatProp.stringValue);
            _useCustomTransportToken =
                !string.IsNullOrWhiteSpace(transport) &&
                !string.Equals(profileId, transport, StringComparison.Ordinal);
            _transportStateInitialized = true;
        }

        private static string Normalize(string raw) => LipSyncProfileId.Normalize(raw);

        private static string ToLabelValue(string raw) => string.IsNullOrWhiteSpace(raw) ? "(empty)" : raw;
    }
}
#endif
