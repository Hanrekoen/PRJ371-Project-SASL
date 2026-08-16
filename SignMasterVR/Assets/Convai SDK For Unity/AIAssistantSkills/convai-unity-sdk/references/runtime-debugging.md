# Runtime debugging

1. Diagnose configuration in Edit Mode before starting runtime.
2. Enter Play Mode only on explicit request.
3. Start `TraceRuntimeEvents` with narrow manager/character/event filters. Transcript capture stays off unless requested.
4. Reproduce once. Read the trace and feature diagnosis; use Unity console tools for stack traces.
5. Stop tracing when done. Trace storage is editor-only, bounded, and cleared on Play Mode exit/domain reload.

Rank evidence: session/pipeline errors, character readiness, turn/action lifecycle, speech/mic state, dynamic-context acknowledgements, then configuration warnings. Never infer success from absence of console errors.
