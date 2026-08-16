#if CONVAI_ENABLE_SERVER_ANIMATION
using System;
using Convai.RestAPI.Internal;
using Convai.Editor.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Convai.Runtime;
using Convai.Editor;
using Convai.Editor.ConfigurationWindow.Components;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.ServerAnimation
{
    /// <summary>
    /// Configuration window section for browsing and importing server-provided animations.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiServerAnimationSection : ConvaiBaseSection
    {
        /// <summary>UXML section name.</summary>
        public const string SECTION_NAME = "server-animation";
        private readonly ConfigurationWindowContext _context;
        private ServerAnimationLogic _logic;

        public ConvaiServerAnimationSection() : this(null)
        {
        }

        public ConvaiServerAnimationSection(ConfigurationWindowContext context)
        {
            _context = context;
            AddToClassList("section-card");
            CreateHeader();
            CreateAnimationContainer();
            CreateNavigationRow();
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _logic?.Dispose();
                _logic = null;
            });
        }

        public Button RefreshButton { get; private set; }

        public Button PreviousButton { get; private set; }

        public Button NextButton { get; private set; }

        public Button ImportButton { get; private set; }

        /// <summary>Gets the container that holds animation cards.</summary>
        public ScrollView AnimationContainer { get; private set; }

        protected override void OnSectionShown()
        {
            _logic ??= new ServerAnimationLogic(this, _context);
            _logic.SetSectionVisible(true);
        }

        protected override void OnSectionHidden()
        {
            _logic?.SetSectionVisible(false);
        }

        private void CreateNavigationRow()
        {
            VisualElement navigationRow = new() { name = "navigation-row" };
            navigationRow.AddToClassList("server-anim-nav-row");

            PreviousButton = new Button { name = "previous-btn", text = "Previous" };
            NextButton = new Button { name = "next-btn", text = "Next" };
            ImportButton = new Button { name = "import-btn", text = "Import" };

            PreviousButton.AddToClassList("button");
            NextButton.AddToClassList("button");
            ImportButton.AddToClassList("button");

            PreviousButton.style.minWidth = 100;
            NextButton.style.minWidth = 100;
            ImportButton.style.minWidth = 100;

            navigationRow.Add(PreviousButton);
            navigationRow.Add(NextButton);
            navigationRow.Add(ImportButton);

            Add(navigationRow);
        }

        private void CreateAnimationContainer()
        {
            AnimationContainer = new ScrollView { style = { minHeight = 200, flexGrow = 1, marginBottom = 10 } };

            AnimationContainer.contentContainer.style.flexDirection = FlexDirection.Row;
            AnimationContainer.contentContainer.style.flexWrap = Wrap.Wrap;
            Add(AnimationContainer);
        }

        private void CreateHeader()
        {
            VisualElement headerRow = new()
            {
                name = "header-row",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    marginBottom = 6
                }
            };

            RefreshButton = new Button { name = "refresh-btn", text = "Refresh" };
            RefreshButton.AddToClassList("button");
            RefreshButton.style.minWidth = 100;

            headerRow.Add(ConvaiVisualElementUtility.CreateLabel("header", "Server Animation", "header"));
            headerRow.Add(RefreshButton);
            Add(headerRow);
        }

    }

    /// <summary>
    /// Visual element representing a selectable animation item.
    /// </summary>
    public class ConvaiServerAnimationItem : VisualElement
    {
        private readonly StyleColor _selectedBorderColor = new(new Color(11f / 255, 96f / 255, 73f / 255));
        private readonly StyleColor _unselectedBorderColor = new(new Color(0, 0, 0, 0.25f));

        private bool _isSelected;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConvaiServerAnimationItem"/> class.
        /// </summary>
        /// <param name="onSelectedChanged">Callback invoked when the selection state changes.</param>
        /// <param name="animation">Animation data associated with this card.</param>
        public ConvaiServerAnimationItem(Action<bool, ServerAnimationItemResponse> onSelectedChanged, ServerAnimationItemResponse animation)
        {
            AddToClassList("server-animation-card");
            CreateThumbnail();
            CreateName();
            IsSelected = false;
            CanBeSelected = true;
            RegisterCallback<ClickEvent>(_ =>
            {
                if (!CanBeSelected)
                {
                    return;
                }

                IsSelected = !IsSelected;
                onSelectedChanged?.Invoke(IsSelected, animation);
            });
        }

        public Image Thumbnail { get; private set; }

        /// <summary>Gets the label used to display the animation name.</summary>
        public Label Name { get; private set; }

        public bool CanBeSelected { get; set; } = true;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                StyleColor borderColor = _isSelected ? _selectedBorderColor : _unselectedBorderColor;
                style.borderBottomColor = borderColor;
                style.borderTopColor = borderColor;
                style.borderLeftColor = borderColor;
                style.borderRightColor = borderColor;
            }
        }

        private void CreateName()
        {
            Name = ConvaiVisualElementUtility.CreateLabel("name", "Animation Name", "server-animation-card-label");
            Add(Name);
        }

        private void CreateThumbnail()
        {
            Thumbnail = new Image
            {
                style =
                {
                    backgroundImage =
                        new StyleBackground(
                            ConvaiEditorSettings.Instance.ConvaiThumbnailTexture)
                }
            };
            Add(Thumbnail);
        }
    }
}
#endif
