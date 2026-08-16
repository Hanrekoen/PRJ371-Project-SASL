using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]

// The embodiment inspectors render the same sections, labels and setup findings this assembly
// owns, so they share one model rather than each carrying its own copy of the vocabulary.
[assembly: InternalsVisibleTo("Convai.Editor.Embodiment")]

// The MCP tools are a projection of this assembly's setup service and troubleshooter — they wrap
// its checks rather than checking a character themselves, so an assistant and the inspector cannot
// describe the same character differently.
[assembly: InternalsVisibleTo("Convai.Modules.Emotion.Editor.AI")]

// And the contract test that asserts the MCP tools report exactly what this service reports — the
// test that keeps them one check engine rather than two.
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]
