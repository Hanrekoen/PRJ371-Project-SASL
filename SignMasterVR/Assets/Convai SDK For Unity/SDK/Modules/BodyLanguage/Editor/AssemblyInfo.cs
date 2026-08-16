using System.Runtime.CompilerServices;

// Body Language's editor assembly owns the demeanor vocabulary this module presents. That
// vocabulary is shared with Emotion and Body Animation and has drifted apart once already, so the
// guard test that holds the three together has to be able to read it — the same arrangement
// Convai.Modules.BodyAnimation.Editor and Convai.Modules.Emotion.Editor already have.
[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]

// The MCP tools are a projection of this assembly's setup service — they wrap its preflight and its
// coordination report rather than checking a character themselves, so an assistant and the inspector
// cannot describe the same character differently.
[assembly: InternalsVisibleTo("Convai.Modules.BodyLanguage.Editor.AI")]

// And the contract test that asserts the MCP tools report exactly what this service reports — the
// test that keeps them one check engine rather than two.
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]
