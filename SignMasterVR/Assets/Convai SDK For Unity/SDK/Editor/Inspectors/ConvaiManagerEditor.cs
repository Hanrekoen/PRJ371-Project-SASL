using System.Collections.Generic;
using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai inspector for <see cref="ConvaiManager" /> — the component that starts the SDK for a
    ///     scene and owns the room, the player and the characters while it runs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every serialized field on this component is <c>[HideInInspector]</c>, and deliberately
    ///         so: the manager is meant to work with no configuration, and the values it does hold are
    ///         either project-wide (they belong in Convai Settings) or owned by another component
    ///         (they belong on the Room Manager). That left the default inspector showing nothing but
    ///         the script slot, which reads as "this component is broken" rather than "this component
    ///         needs nothing from you".
    ///     </para>
    ///     <para>
    ///         So this inspector does not present fields. It answers the two questions someone
    ///         selecting the manager actually has: is my scene wired up, and — once playing — what is
    ///         it doing. Everything else is a signpost to where the setting really lives.
    ///     </para>
    /// </remarks>
    [CustomEditor(typeof(ConvaiManager))]
    internal sealed class ConvaiManagerEditor : ConvaiInspectorEditor
    {
        private const string SetupSectionId = "SceneSetup";
        private const string LiveSectionId = "Live";

        private static readonly GUIContent RoomManagerLabel = new(
            "Room Manager", "The component that connects this scene to the Convai service.");

        private static readonly GUIContent SceneInstallerLabel = new(
            "Scene Installer", "Optional. Runs extra setup for this scene after the SDK starts.");

        private static readonly GUIContent CharactersLabel = new(
            "Characters", "Convai Characters this manager found in the scene.");

        private static readonly GUIContent PlayerLabel = new(
            "Player", "The Convai Player this manager is speaking on behalf of.");

        private static readonly GUIContent ConnectedLabel = new(
            "Connected", "Whether the room connection is open right now.");

        private static readonly GUIContent SpeakingToLabel = new(
            "Speaking To", "The character the player is currently in conversation with.");

        private ConvaiManager Manager => (ConvaiManager)target;

        protected override string Title => "Convai Manager";

        protected override string Subtitle => "Scene runtime host";

        protected override string Purpose =>
            "Starts Convai for this scene and owns the room, the player and the characters while it " +
            "runs. It needs no configuration — project defaults live in Convai Settings, and " +
            "connection settings live on the Convai Room Manager.";

        protected override string EditorStateHostId => "ConvaiManagerEditor";

        /// <summary>
        ///     Outside Play mode this reports whether the scene has what the manager needs; in Play
        ///     mode it reports whether the manager actually started.
        /// </summary>
        protected override GUIContent StatusChip =>
            EditorApplication.isPlaying
                ? Manager.IsInitialized ? Chips.Running(Manager.IsConnected).Content : Chips.Inactive.Content
                : HasRoomManager ? Chips.Ready.Content : Chips.NotSetUp.Content;

        protected override Color StatusChipTint =>
            EditorApplication.isPlaying
                ? Manager.IsInitialized ? Chips.Running(Manager.IsConnected).Tint : Chips.Inactive.Tint
                : HasRoomManager ? Chips.Ready.Tint : Chips.NotSetUp.Tint;

        /// <summary>
        ///     Whether a Room Manager exists anywhere in the loaded scenes.
        /// </summary>
        /// <remarks>
        ///     Scanned on demand rather than per repaint: this runs only when the inspector rebuilds
        ///     its chip, and a scene-wide search on every repaint of a docked inspector is the kind of
        ///     cost that shows up as editor stutter with no visible cause.
        /// </remarks>
        private bool HasRoomManager => _cachedRoomManager != null;

        private ConvaiRoomManager _cachedRoomManager;
        private ConvaiSceneInstaller _cachedSceneInstaller;

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshSceneReferences();
        }

        /// <summary>Refreshes the cached scene lookups once per pass rather than per repaint.</summary>
        protected override void OnBeforeInspectorGUI()
        {
            // Cheap enough once per inspector pass, and it keeps the chip honest when the user adds a
            // Room Manager while this inspector is open.
            if (Event.current.type == EventType.Layout)
                RefreshSceneReferences();
        }

        private void RefreshSceneReferences()
        {
            _cachedRoomManager = FindAnyObjectByType<ConvaiRoomManager>(FindObjectsInactive.Include);
            _cachedSceneInstaller = FindAnyObjectByType<ConvaiSceneInstaller>(FindObjectsInactive.Include);
        }

        protected override void DrawBody()
        {
            if (EditorApplication.isPlaying)
                return;

            if (!HasRoomManager)
            {
                WarningBox(
                    "No Room Manager in this scene",
                    "The manager starts the SDK, but a Convai Room Manager is what actually connects " +
                    "to the service. Without one, characters in this scene will never speak.");
            }

            DrawSection(SetupSectionId, "Scene Setup", Glyphs.Routing, () =>
            {
                Theme.KeyValueRow(RoomManagerLabel, NameOrMissing(_cachedRoomManager));
                Theme.KeyValueRow(SceneInstallerLabel, NameOrNone(_cachedSceneInstaller));
                GUILayout.Space(4f);
                GUILayout.Label(
                    "These are found automatically. Nothing here needs to be assigned by hand.",
                    Theme.MutedWrapped);
            });
        }

        protected override void DrawLiveSection()
        {
            DrawSection(LiveSectionId, "Live", Glyphs.Live, () =>
            {
                IReadOnlyList<ConvaiCharacter> characters = Manager.Characters;
                ConvaiCharacter speakingTo = Manager.ActiveConversationCharacter;

                Theme.KeyValueRow(
                    ConnectedLabel,
                    Manager.IsConnected ? "Yes" : "No",
                    Manager.IsConnected ? Theme.StatusReady : Theme.TextMuted);
                Theme.KeyValueRow(PlayerLabel, NameOrNone(Manager.Player));
                Theme.KeyValueRow(CharactersLabel, characters.Count.ToString());
                Theme.KeyValueRow(SpeakingToLabel, NameOrNone(speakingTo));
            }, accent: Theme.StatusInfo);
        }

        private static string NameOrMissing(Object value) => value != null ? value.name : "Missing";

        private static string NameOrNone(Object value) => value != null ? value.name : "None";
    }
}
