using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]

// The shared embodiment editor renders the same setup findings this assembly evaluates, so the two
// share one model rather than each carrying its own severity ladder and presentation. This module's
// own inspectors live here rather than there, so nothing depends on this grant today; it is kept
// for the cross-module surfaces that read these findings.
[assembly: InternalsVisibleTo("Convai.Editor.Embodiment")]

// The MCP tools serialise the very findings this assembly produces. They are a consumer of the
// setup service, never a second check engine — an assistant and the setup card must be incapable
// of disagreeing about a character, because there is one implementation behind both.
[assembly: InternalsVisibleTo("Convai.Modules.BodyAnimation.Editor.AI")]
