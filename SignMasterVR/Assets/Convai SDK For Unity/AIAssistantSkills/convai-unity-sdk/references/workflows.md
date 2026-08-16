# Convai agent workflows

## Foundation setup

1. Call guidance with `Setup`.
2. Read project status. Report credential presence only.
3. Inspect the scene, including inactive objects.
4. Validate `All`.
5. Call `Convai.SetupConversationScene` with `dryRun=true` and detected target IDs/known Character ID.
6. For an explicit setup request, apply the safe preview with `dryRun=false` without duplicate confirmation.
7. Follow returned `requiredInputs` only after the tool has applied all unblocked work. Never ask whether placeholders should be created.
8. Inspect, validate, and call `Convai.DiagnoseConversation`.
9. Keep the scene dirty; save or enter Play Mode only when explicitly requested.

## Guided first-time setup

An end-to-end setup request already authorizes safe, reversible scene authoring. Do the detected work first. Ask only questions whose answers cannot be detected or safely defaulted, one at a time.

Recommended decision order:

1. Inspect and validate, then preview/apply `Convai.SetupConversationScene`.
2. Let the orchestrator create missing `Convai Player` and `Convai Character` placeholders. Never ask whether placeholders should be created.
3. Use Audio room mode, HandsFree input, automatic connection, and inline configuration unless the user requested another mode. Do not ask the user to approve these recommended defaults.
4. Check credentials. If present, continue without mentioning the key. If absent, continue offline scene authoring, then direct the user to Project Settings; never accept the key in chat.
5. Ask for Character ID when absent, after all independent setup is complete.
6. Ask for explicit player or character targets only when multiple plausible authored targets are ambiguous.
7. Ask optional product choices such as transcripts, lip sync, actions, or vision only after the runnable conversation foundation is complete and only when the available tools can implement the answer.
8. Reinspect, validate, and diagnose. Keep the scene dirty; offer saving only in the completion report unless the user already asked to save.

Do not front-load questions about placeholders, recommended voice mode, automatic connection, or saving. Clearly separate automated foundation changes from unsupported optional-feature work.

## Diagnosis

1. Load topic guidance matching the symptom.
2. Read project status, inspect scene, validate relevant scope, then call `Convai.DiagnoseConversation`.
3. Read Unity Console errors and warnings when the symptom involves compilation or runtime behavior.
4. Separate configuration evidence from runtime evidence.
5. Rank likely root causes. For each, state evidence, affected object, whether auto-fix exists, and next check.
6. Never modify the project during a diagnosis-only request.

Common routing:

- Cannot connect or speak: `Runtime`, then project/scene validation.
- Missing character behavior: `Setup`, then exact character inspection.
- Actions: `Actions`, then `Documentation~/ACTIONS.md`.
- Dynamic context: `DynamicContext`, then `Documentation~/DYNAMIC-CONTEXT.md`.
- Vision: `Vision`, then `Documentation~/DYNAMIC-VISION-CONTEXT.md`.
- Narrative: `Narrative`, then module README.
- Gaze, emotion, body animation: `Embodiment`, then matching feature documentation.

## Completion report

State:

- What was detected.
- What changed and on which instance IDs.
- Validation result.
- Whether scene is dirty and unsaved.
- What was not tested in Play Mode.
- Remaining manual or unsupported setup.
