using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]

// The MCP tools are a projection of this assembly's setup services — they wrap the rig service, the
// module catalog and the preset troubleshooter rather than checking a character themselves, so an
// assistant and the Convai Embodiment window cannot describe the same character differently.
[assembly: InternalsVisibleTo("Convai.Editor.Embodiment.AI")]

// And the contract tests that assert the tools report exactly what those services report — the
// tests that keep them one check engine rather than two.
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]
