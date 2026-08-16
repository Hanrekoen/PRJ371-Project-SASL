# Dynamic context and scene objects

Use Unity tools to create, name, position, tag, and script scene objects. Use Convai APIs for SDK-aware context and attention.

- Stable world state: send typed dynamic-context updates through the character facade.
- Current focus: update the attention object using explicit target identity.
- Gameplay events: call Convai from a small project script; do not encode behavior in object names.
- Actions targeting objects: register explicit targets through `ConfigureActions`, using returned GameObject IDs.

Diagnose acknowledgements with `TraceRuntimeEvents`; do not expose payload text when metadata proves delivery.
