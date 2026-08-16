# Narrative

`ConfigureNarrative` edits Unity-side manager, section mappings, template keys, and trigger components only. It never fetches or mutates backend Narrative Design.

Preserve unrelated mappings and all UnityEvent persistent listeners. Upsert sections by section ID, template keys by key, and triggers by explicit host/identity. Diagnose duplicate IDs, orphaned sections, invalid tags/layers, cached sync errors, and runtime queued/fired state.

Narrative content and trigger messages stay hidden by default. Ask for explicit content disclosure only when needed for the requested debugging task.
