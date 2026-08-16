using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
#endif

namespace Convai.Editor
{
    /// <summary>
    ///     Editor-only settings for Convai SDK UI assets.
    ///     Stored in the package Resources folder as ConvaiEditorSettings.asset
    ///     and accessed via ConvaiEditorSettings.Instance in the Editor.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiEditorSettings", menuName = "Convai/Editor Settings")]
    public class ConvaiEditorSettings : ScriptableObject
    {
        private const string ResourcePath = "ConvaiEditorSettings";

        private static ConvaiEditorSettings _instance;

        [Header("Editor Window Settings")] [SerializeField]
        private StyleSheet _convaiConfigurationWindowStyleSheet;

        [SerializeField] private StyleSheet _unityStyleSheet;

        [SerializeField] private StyleSheet _convaiSettingsSectionsStyleSheet;

        [Header("Resources Images")] [SerializeField]
        private Texture2D _convaiIconTexture;

        [SerializeField] private Texture2D _convaiLogoTextureWhite;

        [SerializeField] private Texture2D _convaiThumbnailTexture;

        /// <summary>
        ///     Gets the singleton instance of ConvaiEditorSettings.
        ///     Creates a new instance if one doesn't exist (Editor only).
        /// </summary>
        public static ConvaiEditorSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<ConvaiEditorSettings>(ResourcePath);
#if UNITY_EDITOR
                    if (_instance == null) _instance = CreateInstance<ConvaiEditorSettings>();
#endif
                }

                return _instance;
            }
        }

        /// <summary>Convai configuration window style sheet.</summary>
        public StyleSheet ConvaiConfigurationWindowStyleSheet => _convaiConfigurationWindowStyleSheet;

        /// <summary>Unity style sheet.</summary>
        public StyleSheet UnityStyleSheet => _unityStyleSheet;

        /// <summary>Shared SDK Settings section style sheet.</summary>
        public StyleSheet ConvaiSettingsSectionsStyleSheet => _convaiSettingsSectionsStyleSheet;

        /// <summary>Convai icon texture.</summary>
        public Texture2D ConvaiIconTexture => _convaiIconTexture;

        /// <summary>Convai logo texture (white).</summary>
        public Texture2D ConvaiLogoTextureWhite => _convaiLogoTextureWhite;

        /// <summary>Convai thumbnail texture.</summary>
        public Texture2D ConvaiThumbnailTexture => _convaiThumbnailTexture;
    }
}
