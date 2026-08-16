---
name: convai-unity-sdk
description: Guides safe setup, validation, debugging, and extension of Convai Unity SDK scenes. Activate for Convai rooms, players, characters, actions, lip sync, gaze and eye contact, body animation, body language, facial expression and mood, transcripts, runtime events, dynamic context, narrative, or SDK failures.
required_packages:
  com.unity.ai.assistant: ">=2.13.0-pre.2, <3.0.0"
required_editor_version: ">=6000.0.0"
tools:
  - Convai.GetGuidance
  - Convai.GetProjectStatus
  - Convai.InspectScene
  - Convai.ValidateSetup
  - Convai.BootstrapScene
  - Convai.ConfigureRoom
  - Convai.ConfigurePlayer
  - Convai.ConfigureCharacter
  - Convai.SetupConversationScene
  - Convai.DiagnoseConversation
  - Convai.ConfigureActions
  - Convai.DiagnoseActions
  - Convai.SimulateAction
  - Convai.ConfigureLipSync
  - Convai.DiagnoseLipSync
  - Convai.ConfigureGaze
  - Convai.DiagnoseGaze
  - Convai.MarkGazeTarget
  - Convai.ConfigureTranscripts
  - Convai.DiagnoseTranscripts
  - Convai.TraceRuntimeEvents
  - Convai.ConfigureNarrative
  - Convai.DiagnoseNarrative
  - Convai.ConfigureBodyAnimation
  - Convai.DiagnoseBodyAnimation
  - Convai.InspectBodyAnimationContent
  - Convai.TuneBodyAnimationPersonality
  - Convai.ConfigureBodyLanguage
  - Convai.DiagnoseBodyLanguage
  - Convai.InspectBodyLanguagePersonalities
  - Convai.ConfigureEmotion
  - Convai.DiagnoseEmotion
  - Convai.InspectEmotionPersonalities
  - Convai.TuneEmotionPersonality
  - Convai.DiagnoseEmbodiment
  - Convai.ConfigureEmbodiment
  - Convai.InspectEmbodimentPresets
---

# Convai Unity SDK

## Operating rules

1. Inspect first: `GetProjectStatus`, `InspectScene`, `ValidateSetup`, then feature diagnosis.
2. Use returned instance IDs. Never choose among same-named objects.
3. Do safe reversible work before asking. Ask only for irreducible values or ambiguous targets.
4. Use Unity tools for generic GameObjects, scripts, scenes, assets, console reads, screenshots, and Play Mode. Use Convai tools for SDK configuration and diagnosis.
5. Preview mutators, apply only requested changes, diagnose again. Keep scenes dirty and unsaved.
6. Never request, print, set, or transmit API keys. Never expose transcripts unless explicitly requested.
7. Never enter/exit Play Mode, save a scene, bake a NavMesh, or claim visual/runtime quality without explicit action and evidence.
8. Custom action executors implement `IConvaiActionExecutor`. Create scripts through Unity script tools, attach them, then pass returned component IDs to `ConfigureActions`.
9. Every action executor must complete on success, failure, timeout, and cancellation. Never leave dispatcher work pending.

## Route

- First setup: [quickstart](references/quickstart.md)
- Custom actions: [custom actions](references/custom-actions.md)
- Play Mode failures/traces: [runtime debugging](references/runtime-debugging.md)
- Dynamic context/scene objects: [dynamic context](references/dynamic-context-and-scene-objects.md)
- Narrative: [narrative](references/narrative.md)
- Any character-expression question, before you know which feature it is: call `Convai.DiagnoseEmbodiment`
- Eye contact and "why isn't it looking at me?": call `Convai.GetGuidance` with topic `Gaze`
- Exact inputs, outputs, and boundaries: [tool contract](resources/foundation-tools.md)

Lead with outcome. Report changed instance IDs, readiness, unsaved state, blockers, and unverified manual checks.
