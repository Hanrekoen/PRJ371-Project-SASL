using System;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.LongTermMemory
{
    /// <summary>
    ///     Long Term Memory section of the Convai configuration window.
    ///     Manages end users associated with long-term memory activity.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiLongTermMemorySection : ConvaiBaseSection
    {
        /// <summary>Unique identifier for this section in navigation.</summary>
        public const string SECTION_NAME = "ltm";

        private readonly ConfigurationWindowContext _context;
        private LongTermMemoryLogic _logic;

        public ConvaiLongTermMemorySection() : this(null)
        {
        }

        public ConvaiLongTermMemorySection(ConfigurationWindowContext context)
        {
            _context = context;
            AddToClassList("section-card");
            CreateHeader();
            IDContainer = new ScrollView { style = { flexGrow = 1, marginBottom = 10 } };
            TableTitle = ConvaiVisualElementUtility.CreateLabel("title", "No End Users Found", "label");
            StatusLabel = ConvaiVisualElementUtility.CreateLabel("ltm-status",
                "Open this section to load end users.", "helper-text");
            RetryButton = new Button { name = "retry-button", text = "Retry" };
            SelectAllButton = new Button { name = "select-all-button", text = "Select All" };
            DeleteButton = new Button { name = "delete-button", text = "Delete" };

            RetryButton.AddToClassList("button-small");
            RetryButton.style.alignSelf = Align.FlexStart;
            RetryButton.style.display = DisplayStyle.None;

            SelectAllButton.AddToClassList("button-small");
            SelectAllButton.style.alignSelf = Align.FlexStart;
            SelectAllButton.SetEnabled(false);

            DeleteButton.AddToClassList("button-small");
            DeleteButton.style.alignSelf = Align.FlexStart;
            DeleteButton.SetEnabled(false);

            VisualElement actionRow = new() { name = "ltm-action-row" };
            actionRow.AddToClassList("ltm-action-row");

            actionRow.Add(RetryButton);
            actionRow.Add(SelectAllButton);
            actionRow.Add(DeleteButton);

            Add(TableTitle);
            Add(StatusLabel);
            Add(actionRow);
            Add(IDContainer);

            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _logic?.Dispose();
                _logic = null;
            });
        }

        /// <summary>Button to refresh the end-user list.</summary>
        public Button RefreshButton { get; private set; }

        /// <summary>Scrollable container for end-user items.</summary>
        public ScrollView IDContainer { get; }

        /// <summary>Label displaying the table title or empty state message.</summary>
        public Label TableTitle { get; }

        /// <summary>Label displaying current load/error state.</summary>
        public Label StatusLabel { get; }

        /// <summary>Retry button shown for recoverable load errors.</summary>
        public Button RetryButton { get; }

        /// <summary>Button to select all end-user items in the list.</summary>
        public Button SelectAllButton { get; }

        /// <summary>Button to delete selected end users.</summary>
        public Button DeleteButton { get; }

        protected override void OnSectionShown()
        {
            EnsureLogic();
            _logic?.SetSectionVisible(true);
        }

        protected override void OnSectionHidden() => _logic?.SetSectionVisible(false);

        private void EnsureLogic() => _logic ??= new LongTermMemoryLogic(this, _context);

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
                    alignItems = Align.Center
                }
            };
            VisualElement headerLabel = ConvaiVisualElementUtility.CreateLabel("header", "Long Term Memory", "header");
            RefreshButton = new Button { name = "refresh-btn", text = "Refresh" };

            RefreshButton.AddToClassList("button-small");
            ConvaiVisualElementUtility.ModifyMargin(headerLabel, 0, 0);
            ConvaiVisualElementUtility.ModifyMargin(RefreshButton, 0, 0);
            ConvaiVisualElementUtility.ModifyMargin(headerRow, 0, 16);

            headerRow.Add(headerLabel);
            headerRow.Add(RefreshButton);
            Add(headerRow);
        }
    }

    /// <summary>
    ///     UI element representing a single Long Term Memory end user item.
    ///     Displays user name, end user ID, and a toggle for selection.
    /// </summary>
    internal class EndUserItemUI : VisualElement
    {
        private readonly Action<bool, string> _onToggle;
        private readonly Toggle _toggleSelectionButton;

        /// <summary>
        ///     Creates a new end-user item UI element.
        /// </summary>
        /// <param name="displayName">Display name of the user.</param>
        /// <param name="endUserId">The end user ID (used for deletion).</param>
        /// <param name="shortId">Short version of the ID for display.</param>
        /// <param name="onToggle">Callback invoked when the selection toggle changes.</param>
        public EndUserItemUI(string displayName, string endUserId, string shortId, Action<bool, string> onToggle)
        {
            AddToClassList("card");
            DisplayName = displayName;
            EndUserId = endUserId;
            _onToggle = onToggle;
            _toggleSelectionButton = new Toggle { name = "selection-btn" };
            _toggleSelectionButton.RegisterValueChangedCallback(OnToggleValueChanged);
            VisualElement container = new() { name = "container", style = { marginLeft = 10 } };
            Label nameLabel = ConvaiVisualElementUtility.CreateLabel("name", DisplayName, "label");
            Label id = ConvaiVisualElementUtility.CreateLabel("id", $"ID: {shortId}", "helper-text");

            ConvaiVisualElementUtility.ModifyMargin(nameLabel, 0, 0);
            style.flexDirection = FlexDirection.Row;
            style.marginBottom = 5;

            container.Add(nameLabel);
            container.Add(id);
            Add(_toggleSelectionButton);
            Add(container);
        }

        private string DisplayName { get; }

        /// <summary>End user ID used for selection and deletion.</summary>
        public string EndUserId { get; }

        /// <summary>Sets the selection state of this item without invoking the callback.</summary>
        public void SetSelected(bool selected)
        {
            if (_toggleSelectionButton.value != selected) _toggleSelectionButton.SetValueWithoutNotify(selected);
        }

        private void OnToggleValueChanged(ChangeEvent<bool> evt) => _onToggle?.Invoke(evt.newValue, EndUserId);
    }
}
