using System.Collections.Generic;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Reusable, project-shareable collection of <see cref="ConvaiActionDefinition" /> templates.
    ///     Assign one or more sets on <see cref="Convai.Runtime.Components.ConvaiActionConfigSource" />
    ///     to author a common verb library once and reuse it across characters.
    /// </summary>
    /// <remarks>
    ///     Definitions authored inside an asset cannot hold a scene <see cref="MonoBehaviour" />
    ///     reference (assets must not carry scene object links), so every entry relies on
    ///     <see cref="ConvaiActionDefinition.ExecutorTypeHint" /> instead of
    ///     <see cref="ConvaiActionDefinition.Executor" />; the config source auto-binds the hint to a
    ///     hierarchy component of that type at connect time via
    ///     <see cref="ConvaiActionExecutorBinder" />. An explicit <see cref="ConvaiActionDefinition.Executor" />
    ///     assigned in an inline (non-asset) definition always wins over a hint.
    /// </remarks>
    [CreateAssetMenu(menuName = "Convai/Action Set", fileName = "ConvaiActionSet")]
    public sealed class ConvaiActionSet : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Reusable action definition templates. Executor references are not supported here " +
                 "(assets cannot hold scene objects) — use Executor Type Hint on each definition instead.")]
        private List<ConvaiActionDefinition> _definitions = new();

        /// <summary>Authored action definition templates in this set.</summary>
        public IReadOnlyList<ConvaiActionDefinition> Definitions => _definitions;

        /// <summary>Creates an empty, validly configured default instance (profile convention).</summary>
        public static ConvaiActionSet CreateDefault()
        {
            var instance = CreateInstance<ConvaiActionSet>();
            instance._definitions = new List<ConvaiActionDefinition>();
            return instance;
        }

        /// <summary>
        ///     Editor-tooling entry point that replaces the authored definitions wholesale, mirroring
        ///     <see cref="Convai.Runtime.Components.ConvaiActionConfigSource.ReplaceDefinitions" />.
        ///     Callers own <c>Undo</c> recording and dirty marking.
        /// </summary>
        internal void ReplaceDefinitions(List<ConvaiActionDefinition> definitions) =>
            _definitions = definitions ?? new List<ConvaiActionDefinition>();

        private void OnValidate()
        {
            if (_definitions == null)
            {
                _definitions = new List<ConvaiActionDefinition>();
                return;
            }

            for (int i = 0; i < _definitions.Count; i++)
            {
                ConvaiActionDefinition definition = _definitions[i];
                if (definition == null)
                    continue;

                definition.ActionName = ConvaiActionDefinition.NormalizeActionName(definition.ActionName);
                definition.ExecutorTypeHint = ConvaiActionParameterDefinition.Normalize(definition.ExecutorTypeHint);

                if (definition.TimeoutSeconds < 0f)
                    definition.TimeoutSeconds = 0f;

                if (definition.DelayAfterBotSpeechSeconds < 0f)
                    definition.DelayAfterBotSpeechSeconds = 0f;

                if (definition.Parameters == null)
                    continue;

                for (int p = 0; p < definition.Parameters.Count; p++)
                {
                    ConvaiActionParameterDefinition parameter = definition.Parameters[p];
                    if (parameter == null)
                        continue;

                    parameter.Name = ConvaiActionParameterDefinition.Normalize(parameter.Name);
                    parameter.Connector = ConvaiActionParameterDefinition.Normalize(parameter.Connector);
                }
            }
        }
    }
}
