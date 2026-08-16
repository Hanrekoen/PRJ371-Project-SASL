using Convai.Editor.PropertyDrawers;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     The serialized properties the room-control groups draw. They come from the scene component or from a
    ///     Room Manager Profile asset, so every field is optional: a null property means "this host does not own
    ///     that setting" and the row is skipped.
    /// </summary>
    internal sealed class ConvaiRoomControlProperties
    {
        public SerializedProperty VideoTrackName;
        public SerializedProperty ServerEndpoint;

        public SerializedProperty TurnTakingOptions;

        public SerializedProperty UserVadSettings;

        public SerializedProperty VisionContextMode;
        public SerializedProperty VisionInputSettings;
        public SerializedProperty VisionRespondModes;

        public SerializedProperty RoomRejoinTtlSeconds;
        public SerializedProperty ResumePolicy;
        public SerializedProperty MaxReconnectAttempts;
        public SerializedProperty SpawnAgentOnRejoin;
        public SerializedProperty StartWaitTimeoutMs;
        public SerializedProperty AutoMicStartDelaySeconds;

        /// <summary>
        ///     Drives whether the video track name is worth showing. Read-only here: the connection type
        ///     itself belongs to the host's top-level Connection section, so it is never edited twice.
        /// </summary>
        public SerializedProperty ConnectionTypeForDisplay;
    }

    /// <summary>
    ///     Draws the advanced room-control settings as flat groups inside a section body the host has
    ///     already opened. Shared by the Convai Room Manager component inspector and the Room Manager
    ///     Profile asset inspector, which otherwise drift into two layouts of the same settings.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every setting sits at one depth. The groups are labels rather than nested collapsibles,
    ///         and the composite settings render their fields inline rather than behind a second
    ///         foldout, so opening the section shows everything it holds. The previous layout mixed
    ///         group labels with Unity foldouts nested inside them, which put half of these settings a
    ///         level deeper than the other half and left them unfound.
    ///     </para>
    ///     <para>
    ///         The section that hosts this is collapsed by default, which is what keeps the advanced
    ///         settings out of a beginner's way — a reader who opens it has asked for all of it.
    ///     </para>
    /// </remarks>
    internal static class ConvaiRoomControlInspectorView
    {
        private static readonly GUIContent ConnectionGroup = new(
            "Connection",
            "How this room reaches the backend. The connection type itself is at the top of the inspector.");

        private static readonly GUIContent VoiceActivityGroup = new(
            "Voice Activity Detection",
            "Backend voice activity detection, sent as vad_params on connect. Separate from turn taking.");

        private static readonly GUIContent ReconnectGroup = new(
            "Reconnect",
            "What the room does when a session drops or the player rejoins.");

        private static readonly GUIContent VisionGroup = new(
            "Dynamic Vision Context",
            ConvaiInspectorContent.VisionContextMode.tooltip);

        /// <summary>
        ///     The group heading already names the feature, so the row inside it is just "Mode" — the
        ///     shape the Turn Taking group uses.
        /// </summary>
        private static readonly GUIContent VisionModeRow = new(
            "Mode",
            ConvaiInspectorContent.VisionContextMode.tooltip);

        /// <summary>Space between one group and the next.</summary>
        private const float GroupSpacing = 6f;

        /// <param name="properties">Properties to draw; null entries are skipped.</param>
        /// <param name="readOnly">True while the values are owned by a Room Manager Profile asset.</param>
        internal static void Draw(ConvaiRoomControlProperties properties, bool readOnly)
        {
            if (properties == null)
                return;

            DrawConnection(properties, readOnly);
            DrawTurnTaking(properties, readOnly);
            DrawVoiceActivity(properties, readOnly);
            DrawVision(properties, readOnly);
            DrawReconnect(properties, readOnly);
        }

        private static void DrawConnection(ConvaiRoomControlProperties properties, bool readOnly)
        {
            bool showsVideoTrackName =
                properties.VideoTrackName != null &&
                properties.ConnectionTypeForDisplay != null &&
                (ConvaiConnectionType)properties.ConnectionTypeForDisplay.enumValueIndex == ConvaiConnectionType.Video;

            if (!showsVideoTrackName && properties.ServerEndpoint == null)
                return;

            Theme.GroupCaption(ConnectionGroup);
            using (new EditorGUI.DisabledScope(readOnly))
            {
                // Connection Type and Starts Connected are everyday setup, so the host draws them at the
                // top of the inspector; what is left here is the tuning a project rarely touches.
                if (showsVideoTrackName)
                    EditorGUILayout.PropertyField(properties.VideoTrackName, ConvaiInspectorContent.VideoTrackName);

                if (properties.ServerEndpoint != null)
                    ConvaiInspectorFieldUtility.DrawServerEndpointField(
                        properties.ServerEndpoint, ConvaiInspectorContent.Server);
            }

            GUILayout.Space(GroupSpacing);
        }

        /// <remarks>
        ///     The push-to-talk key binding is deliberately absent: it belongs to the everyday
        ///     Conversation section, beside the choice of how the player talks, and a setting offered in
        ///     two places is a setting a reader has to guess about.
        /// </remarks>
        private static void DrawTurnTaking(ConvaiRoomControlProperties properties, bool readOnly)
        {
            if (properties.TurnTakingOptions == null)
                return;

            Theme.GroupCaption(ConvaiInspectorContent.TurnTaking);
            using (new EditorGUI.DisabledScope(readOnly))
            {
                TurnTakingOptionsDrawer.DrawContentLayout(properties.TurnTakingOptions);
            }

            GUILayout.Space(GroupSpacing);
        }

        private static void DrawVoiceActivity(ConvaiRoomControlProperties properties, bool readOnly)
        {
            if (properties.UserVadSettings == null)
                return;

            Theme.GroupCaption(VoiceActivityGroup);
            ConvaiUserVadSettingsInspectorUtility.DrawUserVadSettings(
                properties.UserVadSettings, readOnly, false);
            GUILayout.Space(GroupSpacing);
        }

        private static void DrawVision(ConvaiRoomControlProperties properties, bool readOnly)
        {
            if (properties.VisionContextMode == null &&
                properties.VisionInputSettings == null &&
                properties.VisionRespondModes == null)
                return;

            Theme.GroupCaption(VisionGroup);
            using (new EditorGUI.DisabledScope(readOnly))
            {
                if (properties.VisionContextMode != null)
                    EditorGUILayout.PropertyField(properties.VisionContextMode, VisionModeRow);

                if (properties.VisionInputSettings != null)
                {
                    GUILayout.Label(ConvaiInspectorContent.VisionInputSettings, Theme.MicroLabel);
                    DrawChildrenFlat(properties.VisionInputSettings);
                }

                if (properties.VisionRespondModes != null)
                {
                    GUILayout.Label(ConvaiInspectorContent.VisionRespondModes, Theme.MicroLabel);
                    DrawChildrenFlat(properties.VisionRespondModes);
                }
            }

            GUILayout.Space(GroupSpacing);
        }

        private static void DrawReconnect(ConvaiRoomControlProperties properties, bool readOnly)
        {
            Theme.GroupCaption(ReconnectGroup);
            using (new EditorGUI.DisabledScope(readOnly))
            {
                DrawOptionalProperty(properties.RoomRejoinTtlSeconds, ConvaiInspectorContent.RoomRejoinTtlSeconds);
                DrawOptionalProperty(properties.ResumePolicy, ConvaiInspectorContent.ResumePolicy);
                DrawOptionalProperty(properties.MaxReconnectAttempts, ConvaiInspectorContent.MaxReconnectAttempts);
                DrawOptionalProperty(properties.SpawnAgentOnRejoin, ConvaiInspectorContent.SpawnAgentOnRejoin);
                DrawOptionalProperty(properties.StartWaitTimeoutMs, ConvaiInspectorContent.StartWaitTimeoutMs);
                DrawOptionalProperty(
                    properties.AutoMicStartDelaySeconds, ConvaiInspectorContent.AutoMicStartDelaySeconds);
            }
        }

        private static void DrawOptionalProperty(SerializedProperty property, GUIContent label)
        {
            if (property != null)
                EditorGUILayout.PropertyField(property, label);
        }

        /// <summary>
        ///     Draws a nested settings object's own fields inline, without the foldout row that would
        ///     otherwise hide them one level deeper than everything around them.
        /// </summary>
        private static void DrawChildrenFlat(SerializedProperty parent)
        {
            SerializedProperty iterator = parent.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                EditorGUILayout.PropertyField(iterator, true);
            }
        }
    }
}
