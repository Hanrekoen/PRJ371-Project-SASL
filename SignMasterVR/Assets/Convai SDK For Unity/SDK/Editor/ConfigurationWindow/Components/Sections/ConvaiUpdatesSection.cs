#if CONVAI_ENABLE_UPDATES_SECTION
using Convai.Application;
using Convai.Editor.ConfigurationWindow.Content;
using Convai.Editor.Utilities;
using UnityEngine.UIElements;
using UnityApplication = UnityEngine.Application;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    /// Updates section of the Convai configuration window.
    /// Displays current SDK version and local release notes.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiUpdatesSection : ConvaiBaseSection
    {
        /// <summary>Unique identifier for this section in navigation.</summary>
        public const string SECTION_NAME = "updates";

        public ConvaiUpdatesSection()
        {
            AddToClassList("section-card");
            Add(ConvaiVisualElementUtility.CreateLabel("header", "Updates", "header"));
            Add(ConvaiVisualElementUtility.CreateLabel("subheader", "Current SDK Version", "subheader"));
            Add(ConvaiVisualElementUtility.CreateLabel("current-sdk-version", ConvaiSDK.Version.ToString(), "label"));

            ConvaiReleaseNotesAsset releaseNotes = ConvaiReleaseNotesAsset.Instance;
            Add(ConvaiVisualElementUtility.CreateLabel("subheader", "Release Notes", "subheader"));
            foreach (ConvaiReleaseNotesAsset.ReleaseNoteEntry entry in releaseNotes.Entries)
            {
                VisualElement card = new VisualElement { name = "release-note-card" };
                card.AddToClassList("card");

                card.Add(ConvaiVisualElementUtility.CreateLabel("sdk-version", entry.Version, "label"));
                card.Add(ConvaiVisualElementUtility.CreateLabel("release-date", "Released: " + entry.ReleaseDate, "helper-text"));
                card.Add(ConvaiVisualElementUtility.CreateLabel("summary", entry.Summary, "helper-text"));

                foreach (string highlight in entry.Highlights)
                {
                    card.Add(ConvaiVisualElementUtility.CreateLabel("highlight", "•  " + highlight, "changelog-item"));
                }

                Add(card);
            }

            Button viewFullChangelogButton = new Button { name = "view-full-changelog", text = "View Full Changelog" };
            viewFullChangelogButton.AddToClassList("button-small");
            viewFullChangelogButton.style.alignSelf = Align.Center;
            viewFullChangelogButton.clicked += () => UnityApplication.OpenURL(ConvaiEditorLinks.ChangelogUrl);
            Add(viewFullChangelogButton);
        }
    }
}
#endif
