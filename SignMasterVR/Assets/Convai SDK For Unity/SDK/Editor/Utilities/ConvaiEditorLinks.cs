namespace Convai.Editor.Utilities
{
    /// <summary>
    ///     Canonical editor URLs used by Convai editor tooling.
    /// </summary>
    public static class ConvaiEditorLinks
    {
        /// <summary>Convai dashboard home.</summary>
        public const string DashboardHomeUrl = "https://convai.com/pipeline/dashboard";

        /// <summary>Convai character dashboard base URL.</summary>
        public const string CharacterDashboardBaseUrl = "https://convai.com/pipeline/dashboard/character";

        /// <summary>Convai docs home.</summary>
        public const string DocsHomeUrl = "https://docs.convai.com";

        /// <summary>
        ///     The Unity SDK documentation hub — the landing page for every module topic (actions,
        ///     emotion, gaze, dialogue animation, narrative design, vision).
        /// </summary>
        /// <remarks>
        ///     This is what the "?" button in every Convai editor header opens, and it is deliberately
        ///     the hub rather than a per-module deep link: the published module pages currently live at
        ///     GitBook page-id URLs (<c>/pages/8e2d3990…</c>) which are not stable identifiers, so
        ///     hardcoding them into a shipped SDK would strand users on a 404 the first time the docs
        ///     are restructured. When stable per-topic slugs are published, add them here and override
        ///     <c>HelpUrl</c> on the editors that have one — the mechanism is already in place.
        /// </remarks>
        public const string DocsUnitySdkUrl =
            "https://docs.convai.com/api-docs/plugins-and-integrations/convai-unity-sdk";

        /// <summary>First-run setup walkthrough for the Unity SDK.</summary>
        public const string DocsUnityGettingStartedUrl =
            "https://docs.convai.com/api-docs/plugins-and-integrations/convai-unity-sdk/getting-started";

        /// <summary>Convai Unity SDK documentation URL.</summary>
        public const string DocsUnityQuickstartUrl =
            "https://docs.convai.com/api-docs/plugins-and-integrations/convai-unity-sdk";

        /// <summary>Convai changelog URL.</summary>
        public const string ChangelogUrl =
            "https://docs.convai.com/api-docs/plugins-and-integrations/unity-plugin/changelogs";

        /// <summary>Convai YouTube channel URL.</summary>
        public const string YouTubeUrl = "https://www.youtube.com/@convai";

        /// <summary>Convai developer forum URL.</summary>
        public const string DeveloperForumUrl = "https://forum.convai.com";

        /// <summary>Support email address.</summary>
        public const string SupportEmail = "support@convai.com";
    }
}
