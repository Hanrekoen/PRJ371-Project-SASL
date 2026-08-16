# Quickstart

1. Call `Convai.GetProjectStatus`, `Convai.InspectScene`, and `Convai.ValidateSetup`.
2. Preview `Convai.SetupConversationScene` with recommended Audio, HandsFree, connect-on-start, and placeholders.
3. Apply all safe work. Missing Character ID does not block manager, room, player, placeholder character, audio, or ownership setup.
4. Ask only for Character ID when absent or explicit target choice when diagnosis reports ambiguity.
5. Reinspect and call `Convai.DiagnoseConversation`.
6. Enter Play Mode only when requested. Use `Convai.TraceRuntimeEvents Start`, then diagnose connection.

Never modify Main Camera to create a player. Never ask whether to save before setup.
