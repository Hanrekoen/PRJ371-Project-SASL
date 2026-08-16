using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Runtime.Components;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Recommended base class for every action behavior — a <see cref="MonoBehaviour" /> that
    ///     runs when a bound action asks a Convai Character to act. All shipped executors derive
    ///     from this root (directly or through <see cref="ConvaiTargetedActionExecutor" /> /
    ///     <see cref="ConvaiActionExecutor{TParameters}" />), and custom behaviors should too:
    ///     deriving from this class (instead of implementing <see cref="IConvaiActionExecutor" />
    ///     by hand) gives the component the Convai inspector automatically — sectioned
    ///     fields, tooltips, and the action binding status block — with no editor code.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Prefer <see cref="ConvaiTargetedActionExecutor" /> when the behavior acts on a
    ///         resolved target through a hierarchy peer (it adds target validation, peer
    ///         resolution/caching, and parameter override helpers), and
    ///         <see cref="ConvaiActionExecutor{TParameters}" /> when the behavior wants invocation
    ///         parameters bound onto a typed object. Derive from this root directly only for fully
    ///         manual control over the invocation.
    ///     </para>
    ///     <para>
    ///         Group inspector fields with <see cref="ConvaiInspectorSectionAttribute" /> and give
    ///         every serialized field a <see cref="TooltipAttribute" /> — the Convai inspector
    ///         renders both.
    ///     </para>
    /// </remarks>
    public abstract class ConvaiActionExecutorBase : MonoBehaviour, IConvaiActionExecutor
    {
        private Transform _characterTransform;

        /// <summary>
        ///     The transform of the Convai Character this behavior belongs to. Use this — never this
        ///     component's own <see cref="Component.transform" /> — whenever a behavior reads or writes
        ///     world-space position or rotation.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Action behaviors are allowed to live either on the Convai Character itself or on a
        ///         child object that holds the character's behaviors (see the Action Behaviors Object
        ///         setting on <see cref="Convai.Runtime.Components.ConvaiActionConfigSource" />). Both
        ///         layouts are fully supported, so a behavior must never assume its own transform is
        ///         the character's: on a child object that has been nudged off the origin, reading
        ///         <c>transform.position</c> silently yields the wrong place, and the character walks
        ///         to it.
        ///     </para>
        ///     <para>
        ///         Resolved on first use by searching upward for a
        ///         <see cref="Convai.Runtime.Components.ConvaiCharacter" /> (inactive objects included).
        ///         Only a real character hit is cached, so per-frame paths pay nothing once one is
        ///         found. When there is no character above this component the fallback is this
        ///         component's own transform, returned <i>without</i> being cached — which keeps a
        ///         behavior authored on a bare GameObject (as EditMode tests do) working unchanged,
        ///         and lets a behavior that is later parented under its character pick the character
        ///         up instead of being stuck on the wrong transform forever.
        ///     </para>
        /// </remarks>
        protected Transform CharacterTransform
        {
            get
            {
                if (_characterTransform != null)
                    return _characterTransform;

                var character = GetComponentInParent<ConvaiCharacter>(true);
                if (character == null)
                    return transform;

                _characterTransform = character.transform;
                return _characterTransform;
            }
        }

        /// <summary>
        ///     Where the person this character is talking to actually is, or <c>null</c> when the
        ///     scene has nobody to measure to.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Not the same as the Convai Player's own transform</b>, and that difference has
        ///         cost real behavior. First-person controllers — Unity's own Starter Assets among
        ///         them — put the <see cref="CharacterController" /> on a capsule <i>inside</i> the
        ///         rig and move that, leaving the object carrying
        ///         <see cref="Convai.Runtime.Components.ConvaiPlayer" /> parked at the spawn point
        ///         for the whole session. Reading it reports where the player <i>started</i>.
        ///     </para>
        ///     <para>
        ///         The symptom reads as a broken feature rather than a wiring mistake: a character
        ///         leading somebody somewhere stops to wait, the player walks up beside her, and she
        ///         stays put — because the point she is measuring is still back at the spawn.
        ///         Nothing fails, so nothing is logged. Measured in this SDK's own demo, where a
        ///         hand-written copy of this rule had left the step out.
        ///     </para>
        ///     <para>
        ///         Falls back to <see cref="Camera.main" /> when the scene has no Convai Player,
        ///         which is almost always a first-person setup where the camera <i>is</i> the player.
        ///     </para>
        ///     <para>
        ///         <b>Overridable</b>, because a project can know better: a scene with split screen,
        ///         several rigs, or a cutscene camera has an answer this cannot reach. Override it
        ///         and every behavior in your hierarchy agrees about who "the player" is — which is
        ///         the failure that would otherwise have a character walk up to one person while
        ///         looking at another.
        ///     </para>
        /// </remarks>
        protected virtual Transform ResolvePlayer() => ConvaiPlayerBody.Resolve();

        /// <summary>
        ///     Whether the action declares this parameter and the Convai Character sent no value for
        ///     it — the case where falling back to an inspector default is a guess rather than a
        ///     setting.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>A default on this component answers a different question.</b> It is there for
        ///         an action that declares no such parameter at all — a "Wave" action and a "Bow"
        ///         action, each wired to its own behavior with the gesture set here. For an action
        ///         that <em>does</em> declare the parameter, the value is part of what the character
        ///         was asked to say, and arriving without it is missing information.
        ///     </para>
        ///     <para>
        ///         Measured live, twice on one run, and both times the default was the opposite of
        ///         what was asked. "Stop following me" arrived as Follow Me with no <c>mode</c>, and
        ///         the character started following. "Thanks, goodbye" arrived as Play Gesture with no
        ///         <c>gesture</c>, and she waved hello. Neither reported anything wrong, because
        ///         from the behavior's side a default is a valid value.
        ///     </para>
        ///     <para>
        ///         Decline instead. A command that says why nothing happened is worth more than one
        ///         that quietly does the wrong thing — and the drop report already names the action,
        ///         the parameter and what was offered.
        ///     </para>
        /// </remarks>
        /// <param name="invocation">The command being performed.</param>
        /// <param name="parameterName">The parameter to ask about.</param>
        protected static bool DeclaredButNotSent(ConvaiActionInvocation invocation, string parameterName)
        {
            if (invocation?.Definition?.Parameters == null || string.IsNullOrWhiteSpace(parameterName))
                return false;

            string wanted = ConvaiActionParameterDefinition.Normalize(parameterName);
            IReadOnlyList<ConvaiActionParameterDefinition> declared = invocation.Definition.Parameters;
            bool isDeclared = false;
            for (int i = 0; i < declared.Count; i++)
            {
                if (string.Equals(
                        ConvaiActionParameterDefinition.Normalize(declared[i]?.Name),
                        wanted,
                        StringComparison.OrdinalIgnoreCase))
                {
                    isDeclared = true;
                    break;
                }
            }

            if (!isDeclared)
                return false;

            return !invocation.TryGetParameter(wanted, out ConvaiActionParameterValue value) ||
                   value == null ||
                   value.Presence == ConvaiActionParameterPresence.Missing ||
                   string.IsNullOrWhiteSpace(value.StringValue);
        }

        /// <summary>
        ///     Runs one resolved action invocation. Return
        ///     <see cref="ConvaiActionExecutionResult.Unhandled" /> when this component cannot
        ///     service the invocation (missing rig or peer) so the dispatcher can report it
        ///     distinctly; honor <paramref name="cancellationToken" /> for batch replacement and
        ///     timeouts (register the token against your handle's stop/cancel/release method and
        ///     let <see cref="System.OperationCanceledException" /> propagate).
        /// </summary>
        public abstract Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken);
    }
}
