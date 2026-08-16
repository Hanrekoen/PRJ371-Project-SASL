using System.Reflection;
using Convai.Editor.Actions;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using UnityEngine;

namespace Convai.Editor.AI
{
    /// <summary>
    ///     The one place authoring tooling is allowed to know which Action Behavior stands in for
    ///     "this action exists but nothing is wired to it yet".
    /// </summary>
    /// <remarks>
    ///     Authoring an action always produces a bound behavior, even when the author has not chosen
    ///     one — otherwise the resulting Action Set would be invalid the moment it was written. That
    ///     placeholder is <see cref="ConvaiUnityEventActionExecutor" />, and detecting an unwired one
    ///     is how tooling reports "you still need to wire this up" instead of shipping an action that
    ///     silently does nothing. Both facts live here rather than being spelled out at each call
    ///     site, so the shipped library can be reshaped without editor code following it around.
    /// </remarks>
    internal static class ConvaiActionsAuthoringDefaults
    {
        /// <summary>
        ///     Adds the placeholder Action Behavior to <paramref name="source" />'s action behaviors
        ///     object as an undoable operation and returns it.
        /// </summary>
        /// <remarks>
        ///     Which object the behavior lands on is not this seam's decision — it belongs to
        ///     <see cref="ConvaiActionBehaviorHosting" />, so every authoring path agrees. This seam
        ///     only owns <em>which behavior</em> stands in for an unwired action.
        /// </remarks>
        internal static MonoBehaviour AddPlaceholderExecutor(ConvaiActionConfigSource source) =>
            ConvaiActionBehaviorHosting.AddBehavior(source, typeof(ConvaiUnityEventActionExecutor));

        /// <summary>
        ///     Whether <paramref name="executor" /> is a placeholder that nobody has wired scene logic
        ///     into yet — an action that would dispatch successfully and do nothing at all.
        /// </summary>
        internal static bool IsUnwiredPlaceholder(object executor)
        {
            if (executor is not ConvaiUnityEventActionExecutor placeholder)
                return false;

            object authoredCallbacks = typeof(ConvaiUnityEventActionExecutor)
                .GetField(ConvaiUnityEventActionExecutor.EventFieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(placeholder);

            return ConvaiSceneQueries.IsUnityEventUnwired(authoredCallbacks);
        }

        /// <summary>
        ///     Blocking message shown when an action's behavior is missing or is an unwired
        ///     placeholder, named the way the author sees it in the Inspector.
        /// </summary>
        internal static string UnwiredPlaceholderMessage(string actionName) =>
            $"Wire up the Raise Unity Event behavior for action '{actionName}'.";
    }
}
