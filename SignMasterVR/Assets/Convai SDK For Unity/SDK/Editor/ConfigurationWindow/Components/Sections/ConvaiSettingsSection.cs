using System.Collections.Generic;
using Convai.Editor.Settings;
using Convai.Editor.Settings.Views;
using Convai.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     SDK Settings section of the Convai configuration window. Mounts the same
    ///     shared section views as Edit &gt; Project Settings &gt; Convai SDK.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiSettingsSection : ConvaiBaseSection
    {
        /// <summary>Unique identifier for this section in navigation.</summary>
        public const string SECTION_NAME = "settings";

        private readonly ConfigurationWindowContext _windowContext;
        private ConvaiSettingsViewContext _viewContext;
        private List<ConvaiSettingsSectionView> _views;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConvaiSettingsSection" /> class.
        /// </summary>
        public ConvaiSettingsSection() : this(null)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConvaiSettingsSection" /> class.
        /// </summary>
        /// <param name="context">Shared window context.</param>
        public ConvaiSettingsSection(ConfigurationWindowContext context)
        {
            _windowContext = context;
            AddToClassList("section-card");

            RegisterCallback<AttachToPanelEvent>(_ => Mount());
            RegisterCallback<DetachFromPanelEvent>(_ => Unmount());
        }

        protected override void OnSectionShown()
        {
            if (_views == null) return;

            _viewContext?.Settings.Update();
            foreach (ConvaiSettingsSectionView view in _views) view.Activate();
        }

        protected override void OnSectionHidden()
        {
            if (_views == null) return;

            foreach (ConvaiSettingsSectionView view in _views) view.Deactivate();
        }

        private void Mount()
        {
            if (_viewContext != null) return;

            Clear();
            Add(ConvaiVisualElementUtility.CreateLabel("section-header", "SDK Settings", "header"));

            ConvaiSettings.EnsureSettingsAssetExists();
            ConvaiSettings settings = ConvaiSettings.Instance;
            if (settings == null)
            {
                Add(new HelpBox(
                    "ConvaiSettings asset could not be loaded or created. Reimport the SDK package.",
                    HelpBoxMessageType.Error));
                return;
            }

            _viewContext = new ConvaiSettingsViewContext(
                new SerializedObject(settings), ConvaiSettingsHostKind.ConfigurationWindow);
            if (_windowContext != null)
                _viewContext.CredentialsChanged += _windowContext.NotifyApiKeyUpdated;

            ConvaiSettingsUi.PrepareRoot(this, _viewContext.Host);

            Add(ConvaiSettingsUi.CreateSaveTracker(_viewContext));
            _views = ConvaiSettingsUi.CreateSectionViews(_viewContext);

            foreach (ConvaiSettingsSectionView view in _views) Add(view);

            this.Bind(_viewContext.Settings);

            if (IsSectionVisible) OnSectionShown();
        }

        private void Unmount()
        {
            if (_viewContext == null) return;

            this.Unbind();
            OnSectionHidden();
            _views = null;
            _viewContext.Dispose();
            _viewContext = null;
        }
    }
}
