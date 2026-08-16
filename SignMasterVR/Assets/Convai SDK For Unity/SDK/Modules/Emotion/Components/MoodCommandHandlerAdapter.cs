using Convai.Domain.Embodiment.Interfaces;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Emotion.Components
{
    /// <summary>
    ///     Implements <see cref="IMoodCommandHandler" /> by delegating to
    ///     <see cref="ConvaiEmotionController" /> on the same character, and registers itself on
    ///     the character's <see cref="EmbodimentContext" /> — the seam Runtime action executors
    ///     and composites (set-mood, greet, present-object's mood lift, react) use to request
    ///     mood/emotion changes without referencing this module's assembly directly.
    /// </summary>
    /// <remarks>
    ///     Auto-added alongside <see cref="ConvaiEmotionController" /> via
    ///     <c>[RequireComponent]</c> so users get mood-driven actions for free. Requests forward
    ///     directly: <see cref="IMoodCommandHandler.RequestMood" /> to
    ///     <see cref="ConvaiEmotionController.SetMood" />, <see cref="IMoodCommandHandler.RequestEmotionBeat" />
    ///     to <see cref="ConvaiEmotionController.SetEmotionOverride" />.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    internal sealed class MoodCommandHandlerAdapter : MonoBehaviour, IMoodCommandHandler
    {
        private EmbodimentContext _context;
        private CharacterServiceRegistry.ServiceToken _token;
        private ConvaiEmotionController _emotionController;

        /// <summary>
        ///     Resolved lazily: <c>[RequireComponent]</c> adds this adapter BEFORE the controller
        ///     it depends on, so a <c>GetComponent</c> in <c>OnEnable</c> would permanently miss it.
        ///     Request-frequency lookups (not per-frame), so the fallback <c>GetComponent</c> is fine.
        /// </summary>
        private ConvaiEmotionController EmotionController =>
            _emotionController != null ? _emotionController : _emotionController = GetComponent<ConvaiEmotionController>();

        private void OnEnable()
        {
            if (!EmbodimentContext.TryResolveFor(this, out EmbodimentContext ctx))
            {
                enabled = false;
                return;
            }

            _context = ctx;
            _token = _context.Provide<IMoodCommandHandler>(this);
        }

        private void OnDisable()
        {
            _token.Release();
            _token = default;
            _context = null;
        }

        bool IMoodCommandHandler.RequestMood(string label, float intensity, float transitionSeconds)
        {
            ConvaiEmotionController controller = EmotionController;
            if (controller == null) return false;
            controller.SetMood(label, intensity, transitionSeconds);
            return true;
        }

        bool IMoodCommandHandler.RequestEmotionBeat(string label, float intensity)
        {
            ConvaiEmotionController controller = EmotionController;
            if (controller == null) return false;
            controller.SetEmotionOverride(label, intensity);
            return true;
        }
    }
}
