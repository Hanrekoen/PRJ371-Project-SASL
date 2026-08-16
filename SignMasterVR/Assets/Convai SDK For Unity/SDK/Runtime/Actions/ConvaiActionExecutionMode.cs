using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Which side of the project is responsible for running the action commands a Convai
    ///     Character receives. Authored on
    ///     <see cref="Convai.Runtime.Components.ConvaiActionConfigSource" />; it changes nothing at
    ///     runtime, and exists so the SDK's setup checks know whether a missing
    ///     <see cref="ConvaiActionDispatcher" /> is a mistake or a deliberate choice.
    /// </summary>
    /// <remarks>
    ///     Receiving action commands and running them are separate concerns. A character declares
    ///     what it can do through <see cref="Convai.Runtime.Components.ConvaiActionConfigSource" />,
    ///     and the backend sends back ordered commands either way. What happens next is the
    ///     project's choice: the shipped <see cref="ConvaiActionDispatcher" /> can resolve targets
    ///     and run bound behaviors, or project code can subscribe to
    ///     <see cref="Convai.Runtime.Components.ConvaiCharacter.OnActionsReceived" /> (one character)
    ///     or <c>ConvaiManager.Events.OnCharacterActionReceived</c> (every character in the room)
    ///     and do the work itself.
    /// </remarks>
    public enum ConvaiActionExecutionMode
    {
        /// <summary>
        ///     The shipped <see cref="ConvaiActionDispatcher" /> on this character runs the commands:
        ///     it resolves each command's target, runs the bound action behavior, and reports the
        ///     outcome. The default, and what the setup checks expect — a character in this mode with
        ///     no dispatcher component is reported as a setup error.
        /// </summary>
        [InspectorName("Convai Action Runner")]
        ConvaiActionDispatcher = 0,

        /// <summary>
        ///     Project code runs the commands instead, by subscribing to
        ///     <see cref="Convai.Runtime.Components.ConvaiCharacter.OnActionsReceived" /> or
        ///     <c>ConvaiManager.Events.OnCharacterActionReceived</c>. The setup checks stop asking for
        ///     a <see cref="ConvaiActionDispatcher" /> on this character. Commands that reach a
        ///     character nothing is subscribed to are still reported once at runtime, in either mode.
        /// </summary>
        CustomCode = 1
    }
}
