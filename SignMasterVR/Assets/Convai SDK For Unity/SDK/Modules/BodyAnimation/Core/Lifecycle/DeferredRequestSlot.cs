using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Lifecycle
{
    /// <summary>
    ///     The payload, timing and description text for the controller's single deferred
    ///     first-call slot (<c>ConvaiBodyAnimationController.PlayAction</c>/<c>PointAt</c>/
    ///     <c>PlayActionAt</c> called before the runtime is built).
    /// </summary>
    /// <remarks>
    ///     The "identity" triplet — which kind is queued, its display name, and when it was
    ///     queued — stays on the controller itself: <c>BodyAnimationLifecycleTests</c> fakes "a
    ///     request is queued" without a scene by setting <c>_deferredKind</c>/<c>_deferredName</c>/
    ///     <c>_deferredQueuedAt</c> directly via reflection, so those three fields must keep
    ///     existing, by that name, on <c>ConvaiBodyAnimationController</c>. This class owns
    ///     everything else the identity triplet doesn't have to: the kind enum, the eight
    ///     request-specific payload fields, the pure expiry check, and the human-readable
    ///     description shared by the deferred-call log line and the expiry warning.
    /// </remarks>
    internal sealed class DeferredRequestSlot
    {
        internal enum Kind
        {
            None = 0,
            PlayAction = 1,
            PointAtPosition = 2,
            PointAtTarget = 3,
            PointAtTargetOptions = 4,
            PlayActionAt = 5
        }

        internal ActionPlayOptions ActionOptions;
        internal Vector3 Position;
        internal Transform Target;
        internal float HoldSeconds;
        internal PointingPlayOptions PointingOptions;
        internal Transform Anchor;
        internal ActionAnchorOptions AnchorOptions;
        internal ActionPlayOptions PlayOptions;

        /// <summary>
        ///     Clears only the payload a stale replay must never reuse — mirrors the original
        ///     <c>ClearDeferredRequest</c> exactly: <see cref="Target" />/<see cref="Anchor" />/
        ///     <see cref="AnchorOptions" /> are nulled, the remaining value-type payload is left
        ///     stale (harmless: it is only read when the kind that owns it is queued again, which
        ///     always overwrites it first).
        /// </summary>
        internal void Clear()
        {
            Target = null;
            Anchor = null;
            AnchorOptions = null;
        }

        /// <summary>Pure timeout check: true once <paramref name="queuedAt" /> is more than
        /// <paramref name="timeoutSeconds" /> behind <paramref name="now" />.</summary>
        internal static bool HasExpired(float queuedAt, float now, float timeoutSeconds) =>
            now - queuedAt > timeoutSeconds;

        /// <summary>Human-readable call description for the deferred-call log line and the expiry warning.</summary>
        internal static string Describe(Kind kind, string name) => kind switch
        {
            Kind.PlayAction => $"PlayAction('{name}')",
            Kind.PlayActionAt => $"PlayActionAt('{name}')",
            Kind.PointAtPosition or Kind.PointAtTarget or Kind.PointAtTargetOptions => "PointAt(...)",
            _ => "A body animation request"
        };
    }
}
