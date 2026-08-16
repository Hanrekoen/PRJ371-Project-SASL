# Convai foundation tool reference

Unity Assistant calls the dot-named tools below. External MCP clients receive the equivalent underscore-normalized names shown in parentheses.

All tools return `{ success, message, data }`. Treat `success=false` as a failed operation even when the tool call itself completed.

These thirty-seven tools are the complete shipped Convai tool set. Contract version: 4.

## `Convai.GetGuidance` (`Convai_GetGuidance`)

- Input: `topic` — `Overview`, `Setup`, `Actions`, `DynamicContext`, `Vision`, `Narrative`, `Embodiment`, `Events`, or `Runtime`.
- Use before feature-specific work.
- Output includes summary, prerequisites, workflow, relevant Convai and Unity tools, and documentation paths.

## `Convai.GetProjectStatus` (`Convai_GetProjectStatus`)

- No input.
- Returns SDK, Unity, Assistant and tool-contract versions; credential presence; non-secret server and feature settings; Editor state; package root.
- It never returns the API key. Never ask the user to paste one into chat.

## `Convai.InspectScene` (`Convai_InspectScene`)

- Input: `includeInactive` (default `true`).
- Returns open scenes plus managers, rooms, players, characters, counts, component IDs, GameObject IDs, names, active state, Character IDs, and player names.
- Each character also carries a `modules` list: which Convai capabilities it has, a `readiness` word (`NotInstalled`, `Blocked`, `Inert`, `Working`), a one-line summary and, when something is wrong, a `blocker` sentence. Use it to see a capability exists before choosing a feature tool; use `Convai.DiagnoseEmbodiment` when you need the detail.
- Pass returned instance IDs to later operations. Names are display context, not stable identifiers.

## `Convai.ValidateSetup` (`Convai_ValidateSetup`)

- Input: `scope` — `All` (default), `Project`, or `Scene`.
- Returns `errors`, `warnings`, and `nextSteps`.
- Validation is read-only. Run before and after mutations.

## `Convai.BootstrapScene` (`Convai_BootstrapScene`)

- Input: `dryRun` (Unity Assistant default `true`).
- Edit Mode only. With `dryRun=true`, returns `wouldAddManager` and `wouldAddRoomManager` without mutation.
- With `dryRun=false`, idempotently creates or reuses the manager object and adds required manager/room components through Unity Undo.
- Does not add players or characters, set credentials, save the scene, or enter Play Mode.
- Prefer `Convai.SetupConversationScene` for end-to-end setup. Use bootstrap only for manager/room-only work.

## `Convai.ConfigureRoom` (`Convai_ConfigureRoom`)

- Requires an explicit target GameObject instance ID; previews by default.
- Ensures `ConvaiManager` and `ConvaiRoomManager`, then writes inline Audio/Video, input, startup, endpoint, vision-policy, and PTT settings or assigns an existing room profile.
- Rejects incomplete dynamic-vision Video configuration instead of opening a modal prompt.

## `Convai.ConfigurePlayer` (`Convai_ConfigurePlayer`)

- Requires an explicit target GameObject instance ID; previews by default.
- Adds/configures `ConvaiPlayer` and binds an explicit or unambiguous same-scene manager.
- Never repurposes or modifies `Main Camera`.

## `Convai.ConfigureCharacter` (`Convai_ConfigureCharacter`)

- Requires an explicit target GameObject instance ID; previews by default.
- Adds/configures `ConvaiCharacter`, `AudioSource`, and `ConvaiAudioOutput`, then binds manager ownership and active target.
- Empty Character ID permits independent authoring but returns `complete=false` and `requiredInputs=["characterId"]`.

## `Convai.SetupConversationScene` (`Convai_SetupConversationScene`)

- Active-scene orchestrator; previews by default.
- Selection order is explicit instance ID, one unambiguous existing component, then a safe placeholder when none exists.
- Creates `[Convai Manager]`, standalone `Convai Player`, and visible Capsule `Convai Character` as needed; configures Audio, HandsFree, automatic connection, audio output, and explicit ownership.
- Applies all unblocked work, reports ambiguity instead of guessing, and never saves or enters Play Mode.

## `Convai.DiagnoseConversation` (`Convai_DiagnoseConversation`)

- Read-only in Edit and Play Mode.
- Returns `readyToRun`, configuration/runtime snapshots, and ranked issues with stable codes, evidence, auto-fixability, and suggested tool arguments.
- Use Unity Console tools when it suggests console evidence; the diagnostic never changes runtime state.

## Feature tools

- `Convai.ConfigureActions` safely upserts definitions and explicit targets; missing executors receive UnityEvent placeholders and remain incomplete until wired.
- `Convai.DiagnoseActions` returns validator evidence. `Convai.SimulateAction` validates in Edit Mode and executes only in Play Mode.
- `Convai.ConfigureLipSync` assigns existing meshes/maps and uniquely detected or explicit shipped profiles. `Convai.DiagnoseLipSync` reports configuration and runtime buffer evidence.
- `Convai.ConfigureTranscripts` supports relay, chat, and world-space chat modes without changing project settings. `Convai.DiagnoseTranscripts` returns metadata, not transcript text.
- `Convai.TraceRuntimeEvents` starts, reads, clears, or stops a 256-entry editor-only trace. Transcript capture defaults off.
- `Convai.ConfigureNarrative` upserts Unity-side section/key/trigger configuration. `Convai.DiagnoseNarrative` reports local configuration and sanitized runtime state; neither tool contacts the backend.

## Gaze tools

- `Convai.DiagnoseGaze` answers "why isn't this character looking at me?". Read-only in Edit and Play Mode. Returns the preflight checks, the resolved head/neck/eye bone names, the eye backend, the facing-direction angle, the personality in use, the optional extras, the scene's gaze targets, and — in Play Mode — what the character is looking at right now.
- Its `watches` block names which link decided the player: `PlayerAnchorOverride`, `PlayerAnchorProvider`, `MainCamera`, `GuessedCamera`, or `Unresolved`. This is the most common cause of wrong-looking gaze; read it before changing anything.
- `Convai.ConfigureGaze` adds the Gaze component and tunes eye-contact mode, focus fidelity, the player anchor, aim point, body-turn style, and the optional extras. Edit Mode only; previews by default.
- Omitted settings are left unchanged. Omitting `capabilities` leaves the extras alone on a character that already has gaze, and gives a new one the recommended pair; passing an array sets the exact set, so `[]` removes them all.
- Neither tool creates or edits an asset. A character with no Gaze Profile is working, not broken — the response names `Assets → Create → Convai → Embodiment → Gaze Profile` rather than authoring one. Everything on the profile is out of scope for these tools.
- `Convai.MarkGazeTarget` adds or removes `ConvaiGazeTarget` on named GameObjects. The player publishes at priority 10, so a target above that outranks the player during conversation and the tool warns when you ask for it.

## Body animation tools

- `Convai.DiagnoseBodyAnimation` answers "why isn't this character doing anything?". Read-only in Edit and Play Mode. Returns the setup checklist, the troubleshooter findings, the rig's motion-scale calibration, the resolved animation content, the personality config and how many characters share it, movement settings, a per-behaviour breakdown, and — in Play Mode — layer weights, foot slide and the transition log.
- This module is **content-gated**, so read two fields before concluding anything is broken. `readiness.state` is `NotInstalled`, `Blocked` (the rig), `NeedsContent` (no animation set) or `Working`. Each behaviour in `features` then carries its own `state`: `Working`; `NeedsContent` — set up, but this character has no clips for it; `ContentIdle` — the clips exist and the setting that plays them is off; `OffByChoice` — a shipped default, not a fault; `FallbackTier` — no switch gates it and a documented fallback is running, which is correct behaviour.
- `Convai.ConfigureBodyAnimation` adds the component, assigns the shipped animation content, optionally adds movement, and tunes Speed Profile, Auto Jog Distance, Min Jog Distance, Acceleration and turn rate. Edit Mode only; previews by default. Omitted settings are left unchanged. A character whose rig cannot host the module is told why and gets no component.
- Walk Speed and Jog Speed are deliberately not settable: the animation content's measured clip speeds override them whenever the controller runs, which is what keeps the feet from sliding. Diagnose reports the effective speeds and their source.
- Movement is genuinely optional. A character with no `ConvaiNavMeshLocomotion` idles, talks, gestures and points perfectly well; never report its absence as a fault.
- `Convai.InspectBodyAnimationContent` lists the Idle/Talk/Listen/Think pools, every action with the names and aliases `PlayAction` accepts, locomotion coverage across all 26 slots, and pointing directions. Call it before writing code that plays a gesture. Works from a character or straight from an Animation Set asset path.
- `Convai.TuneBodyAnimationPersonality` sets **How expressive**, **How calm**, **Keeps busy when alone** and the four archetypes. One config can be shared by many characters, so it copies the config for the named character first and returns `CONFIG_SHARED_CONSENT_REQUIRED` until you pass `makeConfigUnique`. A config only one character uses is written in place.
- No tool creates an Animation Set or Config, assigns or tags a clip, runs the Clip Motion Analyzer, generates an overlay mask, or touches an Animator Controller. Responses name the Body Animation Editor mode or menu path that does. The personality tool's copy duplicates an existing asset and never authors a new one.

## Body language tools

- `Convai.DiagnoseBodyLanguage` answers "why isn't this character moving?". Read-only in Edit and Play Mode. Returns the setup checklist, what the rig offers, which personality tunes the character and which behaviours it switches off, and — because this module shares the spine, shoulders and head with Body Animation and Gaze — a `coordination` block naming which other Convai modules are on the character and what each one changes.
- Read `coordination` before concluding anything is broken. Every one of those relationships degrades silently, so "the module is broken" and "another module is holding the pose" look identical from outside.
- `Convai.ConfigureBodyLanguage` adds the component and assigns a Body Language Profile the project already has. Edit Mode only; previews by default. A character whose rig cannot drive the module is told why and gets no component.
- `Convai.InspectBodyLanguagePersonalities` lists the profiles the project has, each one's expressiveness, the behaviours it switches off, and which characters already use it.
- No tool creates or edits a Body Language Profile. A character with no profile is working, not broken — it runs on the SDK defaults, and responses name the menu path that creates one.

## Emotion tools

- `Convai.DiagnoseEmotion` answers "why isn't this character's face doing anything?". Read-only in Edit and Play Mode. Returns the setup checklist, the troubleshooter findings, how the character detects feelings, which personality tunes it, what it rests at, a per-behaviour breakdown, and — in Play Mode — the live reading.
- `readiness.state` is `NotInstalled`, `Blocked` (no skinned mesh with blendshapes), `Inert` — set up, unblocked, and still never going to change expression because emotion detection is Off — or `Working`. `Inert` is the state that looks fine and is not.
- **Never report a setting from its stored value; read the `behaviour` block.** Three settings look on and do nothing: a personality that did not come from a character type stores `false` for switches the documentation calls on by default; the four conversation-beat reactions only play when **Never sits perfectly still** is on; and **Picks up other characters' moods** does nothing in a scene holding one character.
- Emotion detection has three settings and the user-facing words matter: **Responsive** updates while the reply is spoken and is the default, **Accurate** reads the whole reply once and works in any language, **Off** means the character never receives anything to feel. Never say NRCLex or LLM to a user.
- `restingMood.decidedBy` names which link of the chain won: `ProfileBaseline`, `InitialMoodOverride`, `ForcedNeutralOverride` — an override deliberately forcing a neutral rest, which suppresses the personality rather than falling through to it — or `None`. In Play Mode the live value is reported beside the authored one, because a runtime `SetMood` call and mood drift are indistinguishable from outside the runtime.
- `Convai.ConfigureEmotion` adds the component, assigns an existing personality, and sets emotion detection and this character's own resting mood. Edit Mode only; previews by default. It writes only fields on the character, so it can never restyle another character.
- `Convai.InspectEmotionPersonalities` lists the personalities the project has, each one's character type, what it rests at, whether it ships with the SDK, and which characters use it.
- `Convai.TuneEmotionPersonality` sets character type, resting mood, expression strength and speed, and the feel switches. These live on a personality that may be shared, so it returns `PERSONALITY_SHARED_CONSENT_REQUIRED` until you pass `makePersonalityUnique`, then gives the character its own copy and writes only that. A personality only one character uses and that does not ship with the SDK is written in place.
- No tool creates a personality from nothing, edits a shared or SDK-shipped personality in place, or authors an emotion vocabulary or expression recipe. Responses name the Emotion Editor or the setup route that does.

## Embodiment tools

Embodiment is the layer the five expressive features share: the rig they all depend on, and the preset that hands each one its settings. **Reach for these first** — before you know which feature a question is about.

- `Convai.DiagnoseEmbodiment` surveys one character end to end: the rig, every feature it has, which of them will actually do something, its preset, and — in Play Mode — what it is doing right now. Read-only. Each entry in `capabilities` names that feature's own `configureTool` and `diagnoseTool`, so one call tells you which tool to reach for next.
- `readiness` uses one vocabulary for the character and for every feature: `NotInstalled`, `Blocked`, `Inert`, `Working`. `NotInstalled` is usually a deliberate choice, not a fault — a character that should not gesture is correctly configured without Body Animation. `Blocked` means the rig cannot support it. `Inert` means set up, unblocked, and still nothing visible will happen; `blocker` says why in one sentence.
- Read `rig` before concluding a feature is broken. Every feature depends on it, and a face rig Convai could not recognize confidently is the documented usual cause of "expression does nothing" — the fix is the **Character Rig** component's convention or a **Custom Rig Convention Map**, not the feature.
- `Convai.ConfigureEmbodiment` works out the rig now instead of at runtime, adds the features you name, and assigns an existing preset — one collapsed undo step. Edit Mode only; previews by default. It adds components only; every knob belongs to the feature's own Configure tool.
- `Convai.InspectEmbodimentPresets` lists the presets the project has, each one's validity verdict and entries, plus every feature a preset can carry and the menu path that creates its settings asset.
- No tool creates or edits a preset or a settings asset. A feature with no settings asset is working, not unfinished — it runs on Convai's built-in defaults, and responses name the menu path that creates one.
- The same survey reaches `Convai.InspectScene` and `Convai.ValidateSetup`: every character's `modules` list carries the readiness word and blocker for each feature, and for the Embodiment layer itself.

## Error handling

- `PLAY_MODE_ACTIVE`: authoring tools require Edit Mode. Do not stop Play Mode unless requested; diagnosis remains available.
- Invalid or missing IDs: inspect again; never fall back to a name guess.
- Missing credentials: direct user to Project Settings, then call project status again.
- Compilation or domain reload: wait for Editor readiness, then repeat the last read-only inspection before continuing.
