# Custom actions

Diagnose before repair. Upsert definitions without deleting unrelated actions or targets.

Before writing a new executor, check whether a shipped one already covers it. Twenty-one built-in
Action Behaviors ship in the package. Flow & Utility: `ConvaiUnityEventActionExecutor`,
`ConvaiWaitActionExecutor`, `ConvaiSequenceActionExecutor`, `ConvaiSetActiveActionExecutor`,
`ConvaiAnimatorStateActionExecutor`, `ConvaiPlaySoundActionExecutor`. Observation:
`ConvaiCountTargetGroupActionExecutor`, `ConvaiMeasureDistanceActionExecutor`. Gaze:
`ConvaiLookAtActionExecutor`, `ConvaiWatchPlayerActionExecutor`, `ConvaiScanEnvironmentActionExecutor`.
Emotion: `ConvaiSetMoodActionExecutor`, `ConvaiReactActionExecutor`. Body Language:
`ConvaiHeadResponseActionExecutor`. Body Animation: `ConvaiPlayGestureActionExecutor`,
`ConvaiPointAtActionExecutor`, `ConvaiWalkToActionExecutor`, `ConvaiLeadPlayerActionExecutor`,
`ConvaiFollowPlayerActionExecutor`, `ConvaiTurnToFaceActionExecutor`,
`ConvaiReturnToStartActionExecutor`. In the Unity Editor these are reachable from the
Actions Editor **+ Add Action ▾** catalog (every executor class carrying
`[ConvaiActionArchetype]`), which pre-fills the definition and binds the executor component in one
click — prefer that path over `ConfigureActions` when the user is working in-editor.

For built-in movement/body actions, configure the feature first and use returned executor IDs in `Convai.ConfigureActions`. For custom behavior:

1. Create an executor script with Unity script tools:
   - implement `IConvaiActionExecutor` directly for full manual control, or
   - derive from `ConvaiActionExecutor<TParameters>` for a typed parameter DTO, or
   - derive from `ConvaiTargetedActionExecutor` when the executor acts on a resolved target
     through a hierarchy-resolved peer component (controller/locomotion/rig) — it supplies target
     validation, peer resolution/caching, once-logged missing-peer diagnostics, and
     invocation-parameter-override helpers for free.
   - Tag the class with `[ConvaiActionArchetype(displayName, ActionName = ..., Description = ...,
     TargetRequirement = ..., Parameters = new[] { "name,Type" }, RequiredPeerHint = "...")]` so it
     also appears in the in-editor "Add Action ▾" catalog automatically — no registration step.
2. Return `Unhandled` for unsupported commands, `Failed` for handled failures (prefer
   `Failed(message, ConvaiActionFailureReason)` over the untyped overload so game code and
   `ConvaiActionFeedbackRelay` can react to *why* it failed), and `Succeeded` only after work
   finishes. A harmless no-op (e.g. dropping nothing) is `Succeeded("explanation")`, not `Failed`.
3. Observe cancellation and guarantee completion for every path, including exceptions and timeouts.
4. Attach component with Unity tools; capture its instance ID.
5. Preview/apply `ConfigureActions` with `executorInstanceId`. Reusable verb libraries can also be
   authored once as a `ConvaiActionSet` asset (`Convai/Action Set`) and assigned to multiple
   characters' `ConvaiActionConfigSource` instead of repeating per-character definitions.
   Each definition also carries an `enabled` flag ("Offer This Action" in the editor): a disabled
   action is excluded from the `action_config` the character receives, so it is never offered.
   `ConfigureActions` reads and writes it per definition (omit to leave the authored value
   unchanged); `DiagnoseActions` reports every effective action's enabled state plus the assigned
   action sets and their entries (read-only). At runtime,
   `character.Actions.SetActionAvailable(name, bool)` overrides it per session.
6. Register spawned/discovered objects as action targets without editing `action_config` by hand:
   attach `Convai/Actions/Convai Action Target` to the object (no code), or call
   `character.Actions.RegisterObject(name, description, gameObject)` /
   `RegisterCharacter(...)` from spawner code. `character.Actions.SetTargetAvailable(name, bool)`
   toggles availability without destroying anything. Both paths batch into the same mid-session
   `context-update` sync the backend already accepts.
7. Add `ConvaiActionFeedbackRelay` next to the dispatcher so failures/successes are reported back
   to the character instead of the LLM silently assuming every action succeeded.
8. In Edit Mode, validate using `SimulateAction`. Enter Play Mode explicitly for real dispatch.

UnityEvent placeholders are incomplete until persistent events are wired.
