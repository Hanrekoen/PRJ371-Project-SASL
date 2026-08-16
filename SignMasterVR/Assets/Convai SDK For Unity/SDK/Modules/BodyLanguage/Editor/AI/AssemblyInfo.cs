using System.Runtime.CompilerServices;

// The contract tests assert on the report and readiness states these tools produce. Those states
// stay internal on purpose — the response's string values are the public contract, not the types
// that produce them — so the tests read them through here rather than by pinning them into the
// package's public API surface.
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]
