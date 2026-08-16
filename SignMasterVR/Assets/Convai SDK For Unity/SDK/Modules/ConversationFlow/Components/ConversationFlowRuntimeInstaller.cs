using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Logging;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.ConversationFlow.Components
{
    /// <summary>
    ///     Announces this module as the supplier of a character's dialogue-state driver.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Gaze, body animation and body language all read the dialogue state, so a character
    ///         that has any of them but no flow controller would sit inert. Auto-provisioning is
    ///         therefore deliberate and supported — but it now <b>says what it did</b>: every
    ///         controller this creates logs once, naming the character, so a user can see why a
    ///         component they did not add is on their object.
    ///     </para>
    ///     <para>
    ///         Provisioning still only happens when a module explicitly asked for it (see
    ///         <c>EmbodimentContext.MarkConversationFlowDriverDemanded</c>). A character with none of
    ///         those modules never grows a controller.
    ///     </para>
    /// </remarks>
    internal static class ConversationFlowRuntimeInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterDefaultFactory()
        {
            EmbodimentContext.RegisterDefaultConversationFlowSourceFactory(
                EnsureSource,
                nameof(ConversationFlowRuntimeInstaller));
        }

        private static IConversationFlowSource EnsureSource(EmbodimentContext context)
        {
            if (context == null) return null;
            if (context.ConversationFlowSource != null)
                return context.ConversationFlowSource;

            ConvaiConversationFlowController existing =
                context.GetComponentInChildren<ConvaiConversationFlowController>(true);
            if (existing != null)
                return existing.isActiveAndEnabled ? existing : null;

            if (!context.IsConversationFlowDriverDemanded)
                return null;

            ConvaiConversationFlowController created =
                context.gameObject.AddComponent<ConvaiConversationFlowController>();
            created.hideFlags = HideFlags.None;

            ConvaiLogger.Info(
                $"[ConvaiConversationFlowController] Added to '{context.gameObject.name}' because an " +
                "embodiment module on this character needs the dialogue state. Add the component " +
                "yourself if you want to configure it.",
                LogCategory.Character);

            return context.ConversationFlowSource ?? created;
        }
    }
}
