using System.Runtime.CompilerServices;

// The Compatibility seams (ConvaiObjectFind, ConvaiObjectId) are SDK-internal utilities, not
// customer API — they are consumed across the package but must never widen the public surface.
//
// This list names every assembly that uses a seam in the finished package, including ones that are
// not built yet. A grant for an assembly that does not exist is ignored by the compiler and costs
// nothing; a missing grant breaks that assembly the day it arrives, in a file its own author has no
// reason to look at. Prefer the harmless extra line.

// Runtime and module code.
[assembly: InternalsVisibleTo("Convai.Runtime")]
[assembly: InternalsVisibleTo("Convai.Modules.ConversationFlow")]
[assembly: InternalsVisibleTo("Convai.Modules.Gaze")]
[assembly: InternalsVisibleTo("Convai.Modules.LipSync")]

// Editor tooling and MCP surfaces run the same scene queries as runtime code.
[assembly: InternalsVisibleTo("Convai.Editor")]
[assembly: InternalsVisibleTo("Convai.Editor.AI")]
[assembly: InternalsVisibleTo("Convai.Editor.Embodiment")]
[assembly: InternalsVisibleTo("Convai.Modules.Gaze.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.Emotion.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.BodyAnimation.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.Narrative.Editor.AI")]
[assembly: InternalsVisibleTo("Convai.Modules.Gaze.Editor.AI")]

// Shipped sample support code lives in the package and compiles against the same seams.
[assembly: InternalsVisibleTo("Convai.Sample")]

// Test assemblies, for the seams' own tests and for tests that query the scene.
[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]
[assembly: InternalsVisibleTo("Convai.Tests.PlayMode")]
