using Convai.Domain.Embodiment.Interfaces;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Components
{
    /// <summary>
    ///     Implements <see cref="IGazeCommandHandler" /> by delegating to
    ///     <see cref="ConvaiGazeController" /> on the same character, and registers itself on the
    ///     character's <see cref="EmbodimentContext" /> — the seam Runtime action composites
    ///     (gaze tour, group attention, lead-the-way glance-back) use to direct attention without
    ///     referencing this module's assembly directly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The name is user-visible, so it reads as English.</b> This component is
    ///         <c>internal</c> and hidden from the Add Component menu, but <c>[RequireComponent]</c>
    ///         still serializes it onto the customer's character, where the Inspector renders the
    ///         type name as its header. It used to be called <c>GazeCommandHandlerAdapter</c>, which
    ///         put three words of internal vocabulary on a shipped character for no one's benefit.
    ///         Hiding it with <see cref="HideFlags.HideInInspector" /> is not the alternative — the
    ///         project already rejected that for embodiment infrastructure (see
    ///         <c>EmbodimentContext.RuntimeInfrastructureHideFlags</c>): a component the user cannot
    ///         see is a component they cannot debug. It explains itself in its own inspector instead.
    ///     </para>
    ///     <para>
    ///         Auto-added alongside <see cref="ConvaiGazeController" /> via
    ///         <c>[RequireComponent]</c> so users get scripted attention requests for free.
    ///         Separate from the controller's own <see cref="IGazeGlanceHandler" /> implementation
    ///         (a lower-commitment, no-return-value contract already used by Body Animation's
    ///         referential glances) — <see cref="IGazeCommandHandler" /> additionally tracks a
    ///         single caller-owned <see cref="GazeHandle" /> so <see cref="ReleaseGaze" /> can end
    ///         exactly the request this handler made, and reports acceptance via its
    ///         <c>bool</c> returns.
    ///     </para>
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    internal sealed class GazeAttentionRequests : MonoBehaviour, IGazeCommandHandler
    {
        private const float DefaultGlanceHoldSeconds = 1.2f;

        private EmbodimentContext _context;
        private CharacterServiceRegistry.ServiceToken _token;
        private ConvaiGazeController _gazeController;
        private GazeHandle _activeHandle;

        /// <summary>
        ///     Resolved lazily: <c>[RequireComponent]</c> adds this adapter BEFORE the controller
        ///     it depends on, so a <c>GetComponent</c> in <c>OnEnable</c> would permanently miss it.
        ///     Request-frequency lookups (not per-frame), so the fallback <c>GetComponent</c> is fine.
        /// </summary>
        private ConvaiGazeController GazeController =>
            _gazeController != null ? _gazeController : _gazeController = GetComponent<ConvaiGazeController>();

        private void OnEnable()
        {
            if (!EmbodimentContext.TryResolveFor(this, out EmbodimentContext ctx))
            {
                enabled = false;
                return;
            }

            _context = ctx;
            _token = _context.Provide<IGazeCommandHandler>(this);
        }

        private void OnDisable()
        {
            _token.Release();
            _token = default;
            _context = null;
            _activeHandle = null;
        }

        bool IGazeCommandHandler.RequestSustainedGaze(Vector3 worldPosition, float durationSeconds, int priority)
        {
            if (GazeController == null) return false;
            _activeHandle = _gazeController.GazeAt(worldPosition, SustainedOptions(durationSeconds, priority));
            return _activeHandle != null;
        }

        bool IGazeCommandHandler.RequestGlance(Vector3 worldPosition, float durationSeconds, int priority)
        {
            if (GazeController == null) return false;
            _activeHandle = _gazeController.GazeAt(worldPosition, GlanceOptions(durationSeconds, priority));
            return _activeHandle != null;
        }

        void IGazeCommandHandler.ReleaseGaze()
        {
            _activeHandle?.Release();
            _activeHandle = null;
        }

        private static GazeOptions SustainedOptions(float durationSeconds, int priority) => new()
        {
            Priority = priority,
            HoldSeconds = durationSeconds,
            Engagement = 1f,
            AllowBodyTurn = false
        };

        private static GazeOptions GlanceOptions(float durationSeconds, int priority) => new()
        {
            Priority = priority,
            HoldSeconds = durationSeconds > 0f ? durationSeconds : DefaultGlanceHoldSeconds,
            Engagement = 1f,
            AllowBodyTurn = false
        };
    }
}
