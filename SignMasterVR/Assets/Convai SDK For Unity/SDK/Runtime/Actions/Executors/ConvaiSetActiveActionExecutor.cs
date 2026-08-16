using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>What <see cref="ConvaiSetActiveActionExecutor" /> does to the object it is pointed at.</summary>
    public enum ConvaiShowHideMode
    {
        /// <summary>Turns the object on.</summary>
        Show = 0,

        /// <summary>Turns the object off.</summary>
        Hide = 1,

        /// <summary>Turns it off if it is on, and on if it is off.</summary>
        Toggle = 2
    }

    /// <summary>
    ///     Turns the object the action points at on or off — the shortest path from "show them the
    ///     map" or "put the sign away" to something visible happening in the scene, with no code and
    ///     no extra components on the object itself.
    /// </summary>
    /// <remarks>
    ///     Asking for a state the object is already in succeeds and says so, rather than failing:
    ///     "show the map" when the map is already showing is a fulfilled request, not an error.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Show Or Hide Object")]
    [ConvaiActionArchetype(
        "Show Or Hide Object",
        ActionName = "Show Or Hide Object",
        Description = "Show, hide, or toggle the target object. Use this for scene objects whose " +
                      "visibility or availability should change as part of the interaction.",
        TargetRequirement = ConvaiActionTargetRequirement.Object,
        Parameters = new[] { "mode,Choice,,show|hide|toggle" },
        ParameterDescriptions = new[]
        {
            "Use 'show' to activate the object, 'hide' to deactivate it, or 'toggle' to switch its " +
            "current state. Always provide one of these values."
        },
        FeaturedOrder = 7)]
    public sealed class ConvaiSetActiveActionExecutor : ConvaiTargetedActionExecutor
    {
        [SerializeField]
        [Tooltip("What to do to the object. The character can ask for a different one per call with " +
                 "the 'mode' parameter ('show', 'hide', or 'toggle').")]
        private ConvaiShowHideMode _mode = ConvaiShowHideMode.Show;

        /// <inheritdoc />
        protected override Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            GameObject targetObject = ResolveTargetGameObject(invocation);
            if (targetObject == null)
                return Task.FromResult(ConvaiActionExecutionResult.Unhandled("This action has no object to act on."));

            ConvaiShowHideMode mode = ParseMode(GetOverride(invocation, "mode", string.Empty), _mode);

            bool wasVisible = targetObject.activeSelf;
            bool shouldBeVisible = mode switch
            {
                ConvaiShowHideMode.Show => true,
                ConvaiShowHideMode.Hide => false,
                ConvaiShowHideMode.Toggle => !wasVisible,
                _ => wasVisible
            };

            if (wasVisible == shouldBeVisible)
            {
                return Task.FromResult(ConvaiActionExecutionResult.Succeeded(
                    shouldBeVisible ? "Already showing." : "Already hidden."));
            }

            targetObject.SetActive(shouldBeVisible);
            return Task.FromResult(ConvaiActionExecutionResult.Succeeded());
        }

        /// <summary>
        ///     Reads the requested mode. The wording is deliberately generous — a language model can
        ///     just as easily send "activate" as "show" for the same intent, and refusing the synonym
        ///     would be a failure the author cannot see coming or fix. Anything genuinely
        ///     unrecognised keeps the authored default rather than guessing.
        /// </summary>
        private static ConvaiShowHideMode ParseMode(string requested, ConvaiShowHideMode authoredDefault)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return authoredDefault;

            return requested.Trim().ToLowerInvariant() switch
            {
                "show" or "activate" or "enable" or "on" => ConvaiShowHideMode.Show,
                "hide" or "deactivate" or "disable" or "off" => ConvaiShowHideMode.Hide,
                "toggle" or "switch" or "flip" => ConvaiShowHideMode.Toggle,
                _ => authoredDefault
            };
        }
    }
}
