using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using UnityEngine;

namespace Convai.Runtime.SceneMetadata
{
    /// <summary>
    ///     Shared poll clock for <see cref="ConvaiObjectMetadata" /> tracked properties.
    ///     Started when the first world object registers and stopped when the last unregisters.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    internal sealed class ConvaiWorldObjectPollDriver : MonoBehaviour
    {
        private const float DefaultPollIntervalSeconds = 0.25f;

        private static ConvaiWorldObjectPollDriver _instance;
        private float _nextPollTime = -1f;

        internal static void EnsureStarted()
        {
            if (_instance != null) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            _instance = manager.gameObject.GetComponent<ConvaiWorldObjectPollDriver>();
            if (_instance == null)
                _instance = manager.gameObject.AddComponent<ConvaiWorldObjectPollDriver>();
        }

        internal static void StopIfIdle()
        {
            if (_instance == null || ConvaiMetadataRegistry.Count > 0) return;

            Destroy(_instance);
            _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (ConvaiMetadataRegistry.Count == 0)
            {
                StopIfIdle();
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextPollTime) return;
            _nextPollTime = now + DefaultPollIntervalSeconds;

            IAgentRegistry registry = ResolveAgentRegistry();
            ConvaiObjectMetadata[] objects = ConvaiMetadataRegistry.GetAllMetadata();
            for (int i = 0; i < objects.Length; i++)
            {
                ConvaiObjectMetadata metadata = objects[i];
                if (metadata != null)
                    metadata.EvaluateTrackedProperties(registry);
            }
        }

        private static IAgentRegistry ResolveAgentRegistry()
        {
            ConvaiManager manager = ConvaiManager.ActiveManager;
            return manager != null && manager.TryGetAgentRegistry(out IAgentRegistry registry)
                ? registry
                : null;
        }
    }
}
