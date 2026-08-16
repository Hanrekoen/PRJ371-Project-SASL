using UnityEngine;

namespace Convai.Editor.ConfigurationWindow.Content
{
    /// <summary>
    ///     Localizable/editor-managed copy for the configuration window dashboard.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiConfigurationContent", menuName = "Convai/Configuration Window Content")]
    public sealed class ConvaiConfigurationContent : ScriptableObject
    {
        private const string ResourcePath = "ConvaiConfigurationContent";

        private static ConvaiConfigurationContent _instance;

        [Header("Welcome")] [SerializeField] private string _welcomeHeader = "Convai WebRTC SDK (Beta)";

        [SerializeField] private string _welcomeSubheader = "Operational Dashboard";

        [SerializeField] private string _betaNotice =
            "You are using the first major WebRTC-based beta release. Use this dashboard to verify setup health and unblock integration quickly.";

        [SerializeField] private string _quickStartInstructions =
            "1. Set API key in Edit > Project Settings > Convai SDK.\n" +
            "2. Run GameObject > Convai > Setup Required Components.\n" +
            "3. Validate scene setup and fix any blockers shown below.";

        [Header("Contact")] [SerializeField] private string _contactHeader = "Need Help?";

        [SerializeField] private string _contactSubheader = "Support and Community Resources";

        /// <summary>Gets the singleton content asset instance.</summary>
        public static ConvaiConfigurationContent Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<ConvaiConfigurationContent>(ResourcePath);
                    if (_instance == null) _instance = CreateInstance<ConvaiConfigurationContent>();
                }

                return _instance;
            }
        }

        /// <summary>Welcome card header text.</summary>
        public string WelcomeHeader => _welcomeHeader;

        /// <summary>Welcome card subheader text.</summary>
        public string WelcomeSubheader => _welcomeSubheader;

        /// <summary>Beta notice content.</summary>
        public string BetaNotice => _betaNotice;

        /// <summary>Quick start instructions text.</summary>
        public string QuickStartInstructions => _quickStartInstructions;

        /// <summary>Contact header text.</summary>
        public string ContactHeader => _contactHeader;

        /// <summary>Contact subheader text.</summary>
        public string ContactSubheader => _contactSubheader;
    }
}
