using System.Runtime.CompilerServices;

// The contract tests assert on the readiness and per-behaviour states these tools report. Those
// states stay internal on purpose — the response's string values are the public contract, not the
// enums that produce them — so the tests read them through here rather than by pinning the enums
// into the package's public API surface.
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]
