using System.Collections.Generic;
using System.IO;
using System.Linq;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     Shows reusable config assets and provides lightweight asset creation shortcuts.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiProfilesSection : ConvaiBaseSection
    {
        public const string SECTION_NAME = "config-assets";

        private readonly Label _characterProfilesLabel;
        private readonly Label _roomConfigsLabel;
        private readonly Label _sceneUsageLabel;

        public ConvaiProfilesSection() : this(null)
        {
        }

        public ConvaiProfilesSection(ConfigurationWindowContext context)
        {
            AddToClassList("section-card");
            Add(ConvaiVisualElementUtility.CreateLabel("profiles-header", "Config Assets", "header"));
            Add(ConvaiVisualElementUtility.CreateLabel(
                "profiles-description",
                "Project-wide defaults live in Edit > Project Settings > Convai SDK. Room Manager Profile and Character Profile assets are optional reusable assets for sharing setup across scenes and prefabs.",
                "helper-text"));

            Add(CreateOwnershipCard());
            Add(CreateCreateAssetsCard());

            _roomConfigsLabel = ConvaiVisualElementUtility.CreateLabel(
                "room-configs-summary",
                string.Empty,
                "helper-text");
            _characterProfilesLabel = ConvaiVisualElementUtility.CreateLabel(
                "character-profiles-summary",
                string.Empty,
                "helper-text");
            _sceneUsageLabel = ConvaiVisualElementUtility.CreateLabel(
                "profile-usage-summary",
                string.Empty,
                "helper-text");

            VisualElement inventoryCard = CreateCard("Current Inventory");
            inventoryCard.Add(_roomConfigsLabel);
            inventoryCard.Add(ConvaiVisualElementUtility.CreateSpacer(8));
            inventoryCard.Add(_characterProfilesLabel);
            inventoryCard.Add(ConvaiVisualElementUtility.CreateSpacer(8));
            inventoryCard.Add(_sceneUsageLabel);
            Add(inventoryCard);

            RefreshSummaries();
        }

        protected override void OnSectionShown() => RefreshSummaries();

        private VisualElement CreateOwnershipCard()
        {
            VisualElement card = CreateCard("How These Assets Fit In");
            card.Add(CreateBullet("Project Settings",
                "Edit > Project Settings > Convai SDK stores project-wide credentials and default SDK behavior."));
            card.Add(CreateBullet("Room Manager Profile",
                "Use a Room Manager Profile when you want reusable room defaults like connection mode, connect-on-start, turn-taking, or reconnect behavior."));
            card.Add(CreateBullet("Character Profile",
                "Use a Character Profile when you want reusable character defaults like character identity, audio behavior, or session resume."));
            return card;
        }

        private VisualElement CreateCreateAssetsCard()
        {
            VisualElement card = CreateCard("Create Assets");

            var buttonsRow = new VisualElement();
            buttonsRow.style.flexDirection = FlexDirection.Row;
            buttonsRow.style.flexWrap = Wrap.Wrap;
            buttonsRow.style.marginTop = 4;

            Button createProfileButton =
                new(() => CreateAsset<ConvaiRoomManagerProfile>("Assets/Convai/Profiles",
                    "ConvaiRoomManagerProfile.asset")) { text = "Create Room Manager Profile" };
            ConvaiVisualElementUtility.AddStyles(createProfileButton, "button", "btn-medium");
            createProfileButton.style.marginRight = 8;
            createProfileButton.style.marginBottom = 8;

            Button createAgentButton =
                new(() => CreateAsset<ConvaiCharacterProfile>("Assets/Convai/Agents", "ConvaiCharacterProfile.asset"))
                {
                    text = "Create Character Profile"
                };
            ConvaiVisualElementUtility.AddStyles(createAgentButton, "button", "btn-medium");
            createAgentButton.style.marginBottom = 8;

            buttonsRow.Add(createProfileButton);
            buttonsRow.Add(createAgentButton);
            card.Add(buttonsRow);

            card.Add(ConvaiVisualElementUtility.CreateLabel(
                "profiles-create-help",
                "These assets are optional. Use them only when you want reusable defaults across multiple scenes or prefabs.",
                "helper-text"));

            return card;
        }

        private static VisualElement CreateCard(string title)
        {
            VisualElement card = new();
            card.AddToClassList("card");
            card.style.marginTop = 12;
            card.Add(ConvaiVisualElementUtility.CreateLabel(title.ToLowerInvariant().Replace(' ', '-') + "-title",
                title, "subheader"));
            return card;
        }

        private static VisualElement CreateBullet(string title, string description)
        {
            var container = new VisualElement();
            container.style.marginTop = 6;

            Label header = ConvaiVisualElementUtility.CreateLabel(title.ToLowerInvariant().Replace(' ', '-') + "-label",
                title, "label");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(header);

            Label body = ConvaiVisualElementUtility.CreateLabel(title.ToLowerInvariant().Replace(' ', '-') + "-body",
                description, "helper-text");
            body.style.whiteSpace = WhiteSpace.Normal;
            container.Add(body);
            return container;
        }

        private void RefreshSummaries()
        {
            IReadOnlyList<ConvaiRoomManagerProfile> roomConfigs = FindAssets<ConvaiRoomManagerProfile>();
            IReadOnlyList<ConvaiCharacterProfile> characterProfiles = FindAssets<ConvaiCharacterProfile>();

            _roomConfigsLabel.text = roomConfigs.Count == 0
                ? "Room Manager Profile assets: none created yet."
                : "Room Manager Profile assets: " + string.Join(", ", roomConfigs.Select(GetDisplayName));

            _characterProfilesLabel.text = characterProfiles.Count == 0
                ? "Character Profile assets: none created yet."
                : "Character Profile assets: " + string.Join(", ", characterProfiles.Select(GetDisplayName));

            int roomManagersUsingRoomConfigs = Resources.FindObjectsOfTypeAll<ConvaiRoomManager>()
                .Count(manager =>
                    manager != null && !EditorUtility.IsPersistent(manager) && manager.RoomConfigAsset != null);
            int charactersUsingDefinitions = Resources.FindObjectsOfTypeAll<ConvaiCharacter>()
                .Count(character => character != null && !EditorUtility.IsPersistent(character) &&
                                    character.CharacterConfigAsset != null);

            _sceneUsageLabel.text =
                $"Scene usage: {roomManagersUsingRoomConfigs} room manager(s) use a Room Manager Profile asset, {charactersUsingDefinitions} character(s) use a Character Profile asset.";
        }

        private static IReadOnlyList<TAsset> FindAssets<TAsset>() where TAsset : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(TAsset).Name}");
            var assets = new List<TAsset>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<TAsset>(path);
                if (asset != null) assets.Add(asset);
            }

            return assets;
        }

        private static string GetDisplayName(Object asset)
        {
            if (asset == null) return string.Empty;

            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrWhiteSpace(path) ? asset.name : $"{asset.name} ({path})";
        }

        private void CreateAsset<TAsset>(string directory, string fileName) where TAsset : ScriptableObject
        {
            Directory.CreateDirectory(directory);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(directory, fileName));
            var asset = ScriptableObject.CreateInstance<TAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            RefreshSummaries();
        }
    }
}
