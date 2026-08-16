using UnityEngine;

namespace Convai.Runtime.SceneMetadata
{
    /// <summary>
    ///     Shared helpers for resolving world-object identity from scene transforms.
    /// </summary>
    public static class ConvaiWorldObjectUtility
    {
        /// <summary>
        ///     Attempts to resolve a Convai object name from a transform hierarchy.
        /// </summary>
        public static bool TryResolveObjectName(Transform target, out string objectName)
        {
            objectName = null;
            if (target == null) return false;

            ConvaiObjectMetadata metadata = target.GetComponentInParent<ConvaiObjectMetadata>();
            if (metadata == null || !metadata.IsValid)
                return false;

            objectName = metadata.ObjectName;
            return !string.IsNullOrWhiteSpace(objectName);
        }
    }
}
