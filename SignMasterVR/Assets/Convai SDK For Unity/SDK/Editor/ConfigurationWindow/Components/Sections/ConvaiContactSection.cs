using Convai.Editor.ConfigurationWindow.Content;
using Convai.Editor.Utilities;
using UnityEngine.UIElements;
using UnityApplication = UnityEngine.Application;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     Contact section of the Convai configuration window.
    ///     Provides links to support resources and the developer forum.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiContactSection : ConvaiBaseSection
    {
        /// <summary>UXML section name.</summary>
        public const string SECTION_NAME = "contact-us";

        public ConvaiContactSection()
        {
            var content = ConvaiConfigurationContent.Instance;
            AddToClassList("section-card");
            Add(ConvaiVisualElementUtility.CreateLabel("header", content.ContactHeader, "header"));
            Add(ConvaiVisualElementUtility.CreateLabel("subheader", content.ContactSubheader, "subheader"));

            var linkRow = new VisualElement { name = "contact-link-row" };
            linkRow.AddToClassList("contact-link-row");
            linkRow.style.flexDirection = FlexDirection.Row;
            linkRow.style.flexWrap = Wrap.Wrap;
            linkRow.style.alignSelf = Align.Center;
            linkRow.style.marginTop = 12;

            var youtubeButton = new Button(() => UnityApplication.OpenURL(ConvaiEditorLinks.YouTubeUrl))
            {
                name = "youtube-btn", text = "YouTube"
            };
            youtubeButton.AddToClassList("button-small");
            youtubeButton.AddToClassList("contact-link-button");

            var docsButton = new Button(() => UnityApplication.OpenURL(ConvaiEditorLinks.DocsHomeUrl))
            {
                name = "docs-btn", text = "Documentation"
            };
            docsButton.AddToClassList("button-small");
            docsButton.AddToClassList("contact-link-button");

            var forumButton = new Button(() => UnityApplication.OpenURL(ConvaiEditorLinks.DeveloperForumUrl))
            {
                name = "forum-btn", text = "Developer Forum"
            };
            forumButton.AddToClassList("button-small");
            forumButton.AddToClassList("contact-link-button");

            linkRow.Add(youtubeButton);
            linkRow.Add(docsButton);
            linkRow.Add(forumButton);
            Add(linkRow);
        }
    }
}
