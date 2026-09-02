# SignMaster VR — what I built and what you need to do

I've written every script and copied them straight into your project at
`Assets/Scripts/` (Core, Lessons, Tutor, UI, ML, Editor). Nothing was committed
to git — these are just files sitting in your working copy, same as if you'd
pasted them in yourself. Review before you commit anything.

## What's fully automated now

Open Unity. Once it finishes compiling, a new menu appears: **Tools > SignMasterVR**.
Run these four in order (or just hit "RUN ALL"):

1. **1 Generate Alphabet Level (A-Z)** — creates 26 `GestureData` assets (Gesture_A ... Gesture_Z)
   under `Assets/Data/Gestures/`, and one `LevelData` asset — Level 1, "Letters", all 26 letters
   in order — under `Assets/Data/Levels/`.
2. **2 Build Placeholder Tutor Prefab** — builds `Assets/Prefabs/Tutor.prefab`: a simple
   capsule-body/cube-head placeholder with a working Animator (Idle + Active states) and a small
   sphere ("StateLight") that changes color depending on what the tutor is doing — grey idle,
   blue welcome, yellow demonstrating, green correct, red try-again, gold complete. No external
   model needed. Swap this prefab for a real rigged avatar later with zero script changes.
3. **3 Build Lesson UI Prefab** — builds `Assets/Prefabs/LessonCanvas.prefab`: a World Space
   canvas with the level title, current letter, progress counter, a reference-image slot,
   a feedback panel, a level-complete panel, and two debug buttons (TEST CORRECT / TEST WRONG).
4. **4 Wire Up Current Scene** — instantiates the Tutor and LessonCanvas into whichever scene
   is currently open, creates an `EventSystem` if one's missing, creates a `LessonSystem` object
   holding `LessonManager` / `LevelManager` / `ProgressManager` / `AudioManager` /
   `FakeGestureRecognizer`, wires every reference between them, hooks the two debug buttons up
   to the fake recognizer, and adds a `LessonBootstrap` that auto-starts Level 1 on Play.

**Open `Assets/Room/Scenes/ClassRoom.unity` first**, then run the four steps (or RUN ALL) so
the Tutor and canvas land in your actual classroom, not an empty scene. Press **Play**. You
should see the placeholder tutor, the letter "A" on the UI, and the state light idle-colored.
Press **C** (or click TEST CORRECT) to advance through the alphabet; press **X** (or TEST WRONG)
to see the try-again path. It should walk all the way through A → Z and then show LEVEL COMPLETE.

Re-running any step is safe — nothing gets duplicated.

## What I could not do for you (and exactly what to do instead)

**Remove Convai** — this needs the Package Manager GUI, which I can't drive remotely:
1. Window → Package Manager → find the Convai package(s) in the list → Remove.
2. Delete the `Assets/Convai` folder (and its samples under `Assets/Samples` if any are Convai's).
3. Let Unity recompile. If a scene had a Convai component on a GameObject, you'll see a
   "missing script" warning on that object — just remove that component, it's expected.

**Get a real avatar / real per-sign animations** — not something I can fetch or invent.
The placeholder tutor works today with zero avatar; when a rigged humanoid (Mixamo or your
Reallusion pipeline) is ready: import it, set Rig → Animation Type → Humanoid, drag it into
`Tutor.prefab` in place of the Body/Head primitives, and add real states/clips to
`TutorAnimator.controller` for each letter (name the trigger to match `GestureData.tutorAnimationTrigger`,
e.g. "Sign_A") — `TutorController.Demonstrate()` already checks for a matching trigger name
and will use it automatically the moment it exists.

**Real reference images per letter** — each `Gesture_A.asset` ... `Gesture_Z.asset` has an
empty `referenceImage` slot. Drop in a photo/sprite of the correct handshape for each once you
have them (Deaf-community/SASL-educator-approved, per your methodology) and the UI panel will
start showing it automatically — no code change needed.

**Quest 3 testing** — this setup only targets the PC/Editor demo per your call. Building/deploying
to the headset is a separate pass once the Editor loop is solid.

**Recorded tutor audio** — `AudioManager` is wired and ready (`successAudioKey`/`tryAgainAudioKey`
on each GestureData point at "Correct"/"TryAgain"); it just has no clips assigned yet, so it
silently plays nothing. Record short WAV lines, drop them into `AudioManager`'s `clips` list on
the `LessonSystem` object with matching keys, done.

## A heads-up on risk

I don't have Unity installed in my own environment, so none of this was compile-tested by
actually running it — I wrote it carefully against documented Unity/Editor APIs, but if the
Console shows an error after it compiles or after you run a menu step, paste it back to me
and I'll fix it immediately. Nothing here touches git; you're still in full control of what
gets committed.
