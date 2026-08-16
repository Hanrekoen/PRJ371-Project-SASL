using UnityEngine;

namespace Convai.Runtime.Configuration
{
    /// <summary>
    ///     ScriptableObject that stores key bindings used by Convai runtime components.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiKeyBindings", menuName = "Convai/Key Bindings")]
    public class ConvaiKeyBindings : ScriptableObject
    {
        [SerializeField] [Tooltip("Key that starts and stops talking (push-to-talk).")]
        private KeyCode talkKey = KeyCode.T;

        [SerializeField] [Tooltip("Key that opens the Convai settings UI.")]
        private KeyCode openSettingsKey = KeyCode.F10;

        /// <summary>Gets the key used to start/stop talking.</summary>
        public KeyCode TalkKey => talkKey;

        /// <summary>Gets the key used to open the settings UI.</summary>
        public KeyCode OpenSettingsKey => openSettingsKey;

        /// <summary>
        ///     Attempts to load the key bindings asset from the Resources folder.
        /// </summary>
        /// <param name="binding">Loaded key bindings, if found.</param>
        /// <returns>True when the bindings asset was found; otherwise false.</returns>
        public static bool GetBinding(out ConvaiKeyBindings binding)
        {
            binding = Resources.Load<ConvaiKeyBindings>(nameof(ConvaiKeyBindings));
            return binding != null;
        }
    }
}
