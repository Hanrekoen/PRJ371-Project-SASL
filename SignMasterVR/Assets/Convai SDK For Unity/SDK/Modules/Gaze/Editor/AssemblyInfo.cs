using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]

// The MCP tools serialise the very findings this assembly produces. They are a consumer of the
// setup service, never a second check engine — an assistant and the setup card must be incapable
// of disagreeing about a character, because there is one implementation behind both.
[assembly: InternalsVisibleTo("Convai.Modules.Gaze.Editor.AI")]

// The embodiment inspectors (ConvaiGazeControllerEditor and the profile inspector) render the same
// setup findings and capability model this assembly evaluates, so they share one model rather than
// each carrying its own severity ladder and presentation.
[assembly: InternalsVisibleTo("Convai.Editor.Embodiment")]
