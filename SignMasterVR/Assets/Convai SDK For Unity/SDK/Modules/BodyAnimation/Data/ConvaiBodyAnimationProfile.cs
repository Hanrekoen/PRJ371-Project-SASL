using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     Bundles a body animation set and config for preset-based character setup, routed to
    ///     <c>ConvaiBodyAnimationController</c> via the embodiment profile system
    ///     (<c>ModuleIds.BodyAnimation</c>).
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConvaiBodyAnimationProfile",
        menuName = "Convai/Embodiment/Body Animation Profile",
        order = 140)]
    public sealed class ConvaiBodyAnimationProfile : ScriptableObject
    {
        [SerializeField] private ConvaiBodyAnimationSet _animationSet;
        [SerializeField] private ConvaiBodyAnimationConfig _config;

        [SerializeField]
        [Tooltip("When true and no IConversationFlowSource is registered, the controller " +
                 "requests the hidden conversation-flow driver at runtime so Speaking states " +
                 "drive the talk layer. Disable when you provide your own flow source.")]
        private bool _autoCreateConversationFlow = true;

        public ConvaiBodyAnimationSet AnimationSet => _animationSet;
        public ConvaiBodyAnimationConfig Config => _config;
        public bool AutoCreateConversationFlow => _autoCreateConversationFlow;

        /// <summary>Runtime default when no profile asset is assigned (OwnedProfile parity).</summary>
        public static ConvaiBodyAnimationProfile CreateDefault()
        {
            ConvaiBodyAnimationProfile instance = CreateInstance<ConvaiBodyAnimationProfile>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        /// <summary>Editor/wizard writer. Not part of the public runtime API.</summary>
        internal void Initialize(
            ConvaiBodyAnimationSet animationSet,
            ConvaiBodyAnimationConfig config,
            bool autoCreateConversationFlow = true)
        {
            _animationSet = animationSet;
            _config = config;
            _autoCreateConversationFlow = autoCreateConversationFlow;
        }
    }
}
