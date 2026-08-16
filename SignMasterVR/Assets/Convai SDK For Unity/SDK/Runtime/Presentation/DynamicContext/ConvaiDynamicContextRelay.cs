using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Presentation.DynamicContext
{
    /// <summary>
    ///     Inspector-friendly relay for binding Unity gameplay events to one character's dynamic context.
    /// </summary>
    [AddComponentMenu("Convai/Dynamic Context/Convai Dynamic Context Relay")]
    [DisallowMultipleComponent]
    public sealed class ConvaiDynamicContextRelay : MonoBehaviour
    {
        [Tooltip("Optional explicit character reference. If omitted, the relay can use a ConvaiCharacter on the same GameObject.")]
        [SerializeField]
        [ConvaiInspectorSection("Target")]
        private ConvaiCharacter _character;

        [Tooltip("If enabled, the relay looks for a ConvaiCharacter on the same GameObject.")]
        [SerializeField]
        [ConvaiInspectorSection("Target")]
        private bool _autoResolveCharacter = true;

        [Tooltip("Whether calls made through this relay should make the character reply, or update its context silently.")]
        [SerializeField]
        [ConvaiInspectorSection("Defaults")]
        private ConvaiRespondMode _reactionMode = ConvaiRespondMode.Silent;

        [Tooltip("If enabled, changes made through this relay are sent to the character immediately instead of waiting for the next flush.")]
        [SerializeField]
        [ConvaiInspectorSection("Defaults")]
        private bool _flushImmediately;

        [Tooltip("Invoked whenever this relay successfully queues a change for the character.")]
        [SerializeField]
        [ConvaiInspectorSection("Events")]
        private UnityEvent _onQueued = new();

        [Tooltip("Invoked whenever this relay could not resolve a character to send a change to.")]
        [SerializeField]
        [ConvaiInspectorSection("Events")]
        private UnityEvent _onSkipped = new();

        public UnityEvent OnQueued => _onQueued;
        public UnityEvent OnSkipped => _onSkipped;

        public void SetState(string name, string value)
        {
            if (!TryResolveDynamicContext(out IConvaiDynamicContext dynamicContext)) return;
            dynamicContext.SetState(name, value, _reactionMode);
            Complete(dynamicContext);
        }

        public void AddEvent(string text)
        {
            if (!TryResolveDynamicContext(out IConvaiDynamicContext dynamicContext)) return;
            dynamicContext.AddEvent(text, _reactionMode);
            Complete(dynamicContext);
        }

        public void SetCurrentAttentionObject(string objectName)
        {
            if (!TryResolveDynamicContext(out IConvaiDynamicContext dynamicContext)) return;
            dynamicContext.SetCurrentAttentionObject(objectName, _reactionMode);
            Complete(dynamicContext);
        }

        public void ClearCurrentAttentionObject()
        {
            if (!TryResolveDynamicContext(out IConvaiDynamicContext dynamicContext)) return;
            dynamicContext.ClearCurrentAttentionObject(_reactionMode);
            Complete(dynamicContext);
        }

        public void ResetContext() => ResetContext(false);

        public void ResetContext(bool removeStatic)
        {
            if (!TryResolveDynamicContext(out IConvaiDynamicContext dynamicContext)) return;
            dynamicContext.Reset(removeStatic);
            Complete(dynamicContext);
        }

        public void Flush()
        {
            if (!TryResolveDynamicContext(out IConvaiDynamicContext dynamicContext)) return;
            dynamicContext.Flush();
            _onQueued?.Invoke();
        }

        private bool TryResolveDynamicContext(out IConvaiDynamicContext dynamicContext)
        {
            ConvaiCharacter resolvedCharacter = _character != null ? _character :
                _autoResolveCharacter ? GetComponent<ConvaiCharacter>() : null;
            dynamicContext = resolvedCharacter != null ? resolvedCharacter.DynamicContext : null;

            if (dynamicContext != null) return true;

            ConvaiLogger.Warning(
                "Assign a ConvaiCharacter or enable Auto Resolve Character.",
                LogCategory.Character);
            _onSkipped?.Invoke();
            return false;
        }

        private void Complete(IConvaiDynamicContext dynamicContext)
        {
            if (_flushImmediately)
                dynamicContext.Flush();

            _onQueued?.Invoke();
        }
    }
}
