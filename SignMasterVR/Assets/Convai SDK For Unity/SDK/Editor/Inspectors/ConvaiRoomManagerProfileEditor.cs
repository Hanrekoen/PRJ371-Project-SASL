using Convai.Editor.Inspectors.Framework;
using Convai.Runtime;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiRoomManagerProfile" />: the room defaults a project
    ///     reuses across scenes (connection, dynamic vision context, turn taking) and the reconnect
    ///     policy.
    /// </summary>
    /// <remarks>
    ///     Laid out to match the Convai Room Manager component inspector section for section, and drawing
    ///     the same shared groups, so the asset and the scene component cannot describe the same settings
    ///     two different ways.
    /// </remarks>
    [CustomEditor(typeof(ConvaiRoomManagerProfile))]
    internal sealed class ConvaiRoomManagerProfileEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Convai Room Manager Profile";

        private const string PurposeText =
            "Reusable room defaults. Assign this asset to a Convai Room Manager so several scenes " +
            "share one connection, vision and reconnect setup.";

        /// <remarks>
        ///     Kept as "RoomDefaults" so a project that already collapsed or expanded this section keeps
        ///     its choice across the rename. The id is storage, not copy.
        /// </remarks>
        private const string SectionRoomControlId = "RoomDefaults";

        private const string SectionConnectionId = "Connection";

        private static readonly GUIContent ConnectionSection = new("Connection");
        private static readonly GUIContent RoomControlSection = new("Advanced Room Control");
        private static readonly GUIContent ReusableAssetChip = new("Reusable Asset");

        private SerializedProperty _autoMicStartDelaySecondsProp;
        private SerializedProperty _connectionTypeProp;
        private SerializedProperty _connectOnStartProp;
        private SerializedProperty _maxReconnectAttemptsProp;
        private SerializedProperty _resumePolicyProp;
        private SerializedProperty _roomRejoinTtlSecondsProp;
        private SerializedProperty _serverEndpointProp;
        private SerializedProperty _spawnAgentOnRejoinProp;
        private SerializedProperty _startWaitTimeoutMsProp;
        private SerializedProperty _turnTakingOptionsProp;
        private SerializedProperty _userVadSettingsProp;
        private SerializedProperty _videoTrackNameProp;
        private SerializedProperty _visionContextModeProp;
        private SerializedProperty _visionInputSettingsProp;
        private SerializedProperty _visionRespondModesProp;

        protected override string Title => TitleText;
        protected override string Purpose => PurposeText;
        protected override GUIContent StatusChip => ReusableAssetChip;
        protected override Color StatusChipTint => Theme.StatusInfo;

        protected override void OnEnable()
        {
            base.OnEnable();

            _connectionTypeProp = serializedObject.FindProperty("_connectionType");
            _videoTrackNameProp = serializedObject.FindProperty("_videoTrackName");
            _serverEndpointProp = serializedObject.FindProperty("_serverEndpoint");
            _connectOnStartProp = serializedObject.FindProperty("_connectOnStart");
            _turnTakingOptionsProp = serializedObject.FindProperty("_turnTakingOptions");
            _userVadSettingsProp = serializedObject.FindProperty("_userVadSettings");
            _visionContextModeProp = serializedObject.FindProperty("_visionContextMode");
            _visionInputSettingsProp = serializedObject.FindProperty("_visionInputSettings");
            _visionRespondModesProp = serializedObject.FindProperty("_visionRespondModes");
            _roomRejoinTtlSecondsProp = serializedObject.FindProperty("_roomRejoinTtlSeconds");
            _resumePolicyProp = serializedObject.FindProperty("_resumePolicy");
            _maxReconnectAttemptsProp = serializedObject.FindProperty("_maxReconnectAttempts");
            _spawnAgentOnRejoinProp = serializedObject.FindProperty("_spawnAgentOnRejoin");
            _startWaitTimeoutMsProp = serializedObject.FindProperty("_startWaitTimeoutMs");
            _autoMicStartDelaySecondsProp = serializedObject.FindProperty("_autoMicStartDelaySeconds");
        }

        protected override void DrawBody()
        {
            DrawConnectionSection();
            DrawRoomControlSection();
        }

        /// <summary>
        ///     The two decisions a room makes about connecting, at the top, exactly where the component
        ///     inspector puts them.
        /// </summary>
        private void DrawConnectionSection() =>
            DrawSection(SectionConnectionId, ConnectionSection, Glyphs.Routing, () =>
            {
                if (_connectionTypeProp != null)
                    EditorGUILayout.PropertyField(_connectionTypeProp, ConvaiInspectorContent.ConnectionType);

                if (_connectOnStartProp != null)
                    EditorGUILayout.PropertyField(_connectOnStartProp, ConvaiInspectorContent.ConnectOnStart);
            });

        private void DrawRoomControlSection() =>
            DrawSection(SectionRoomControlId, RoomControlSection, Glyphs.Section, () =>
                ConvaiRoomControlInspectorView.Draw(
                    new ConvaiRoomControlProperties
                    {
                        ConnectionTypeForDisplay = _connectionTypeProp,
                        VideoTrackName = _videoTrackNameProp,
                        ServerEndpoint = _serverEndpointProp,
                        TurnTakingOptions = _turnTakingOptionsProp,
                        UserVadSettings = _userVadSettingsProp,
                        VisionContextMode = _visionContextModeProp,
                        VisionInputSettings = _visionInputSettingsProp,
                        VisionRespondModes = _visionRespondModesProp,
                        RoomRejoinTtlSeconds = _roomRejoinTtlSecondsProp,
                        ResumePolicy = _resumePolicyProp,
                        MaxReconnectAttempts = _maxReconnectAttemptsProp,
                        SpawnAgentOnRejoin = _spawnAgentOnRejoinProp,
                        StartWaitTimeoutMs = _startWaitTimeoutMsProp,
                        AutoMicStartDelaySeconds = _autoMicStartDelaySecondsProp
                    },
                    false),
                false);
    }
}
