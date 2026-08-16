using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using Convai.Runtime;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Embodiment;
using Convai.Runtime.SceneMetadata;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Mirrors the character's current gaze object into the backend dynamic context key
    ///     <c>current_attention_object</c> so "it"/"that" references resolve against what
    ///     the character is actually looking at.
    /// </summary>
    [AddComponentMenu("Convai/Gaze/Advanced/Dynamic Context Bridge")]
    [DisallowMultipleComponent]
    public sealed class GazeDynamicContextBridge : MonoBehaviour, IEmbodimentTickable
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Minimum gaze engagement required before publishing the object.")]
        private float _engagementThreshold = 0.5f;

        private int _lastPublishedGenerationId = int.MinValue;
        private string _lastPublishedObjectName;
        private EmbodimentContext _context;

        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        private void OnEnable()
        {
            if (!EmbodimentContext.TryResolveFor(this, out _context)) return;
            _context.EnsureTickScheduler()?.Register(this);
        }

        private void OnDisable()
        {
            _context?.TickScheduler?.Unregister(this);
            _lastPublishedGenerationId = int.MinValue;
            _lastPublishedObjectName = null;
        }

        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            if (!TryEnsureContext()) return;

            ConvaiCharacter character = ResolveCharacter();
            if (character == null) return;

            GazeReading reading = _context.GazeSource?.Current ?? GazeReading.None;
            bool publishable = reading.Target != null &&
                               reading.Engagement >= _engagementThreshold &&
                               reading.TargetKind is GazeTargetKind.WorldObject or GazeTargetKind.Scripted;
            if (!publishable)
            {
                PublishClearIfNeeded();
                return;
            }

            if (!ConvaiWorldObjectUtility.TryResolveObjectName(reading.Target, out string objectName))
            {
                PublishClearIfNeeded();
                return;
            }

            if (reading.GenerationId == _lastPublishedGenerationId &&
                string.Equals(_lastPublishedObjectName, objectName, System.StringComparison.Ordinal))
                return;

            character.DynamicContext.SetCurrentAttentionObject(
                objectName,
                ConvaiRespondMode.Silent);

            _lastPublishedGenerationId = reading.GenerationId;
            _lastPublishedObjectName = objectName;
        }

        private void PublishClearIfNeeded()
        {
            if (string.IsNullOrEmpty(_lastPublishedObjectName)) return;

            ConvaiCharacter character = ResolveCharacter();
            if (character == null) return;

            character.DynamicContext.ClearCurrentAttentionObject(ConvaiRespondMode.Silent);
            _lastPublishedGenerationId = int.MinValue;
            _lastPublishedObjectName = null;
        }

        private bool TryEnsureContext()
        {
            if (_context != null) return true;
            return EmbodimentContext.TryResolveFor(this, out _context);
        }

        private ConvaiCharacter ResolveCharacter() =>
            _context?.Character ?? GetComponentInParent<ConvaiCharacter>(true);
    }
}
