# SignMasterVR

A VR South African Sign Language (SASL) tutor for Meta Quest, built in Unity. This is the Unity/VR sub-project of the **PRJ371** team project — it covers the in-headset lesson experience and its live connection to an external, laptop-run gesture recognition pipeline.

## What it does

The learner puts on a Quest headset and works through a lesson: a tutor character demonstrates a letter, the learner signs it back, and the app tells them whether they got it right before moving to the next one. Recognition itself doesn't happen on the headset — a laptop with a webcam watches the learner's hand, runs the tracking/classification model, and reports "correct" or "try again" back to the headset over the local network in real time.

That split exists because the front of the hand — the part that actually carries the handshape — is far more visible to an external webcam than to the headset's own downward/forward-facing cameras.

## How it's put together

```
Quest headset (Unity, this repo's Assets/)
      │  TCP, newline-delimited JSON, local Wi-Fi
      ▼
Laptop (GestureServer/, Python)
      │  webcam → MediaPipe Hands → classifier
      ▼
result relayed back to the headset → LessonManager decides next step
```

- **Unity side** (`Assets/Scripts/`) — the lesson flow, tutor character, UI, and the network client that talks to the laptop. See `Assets/Scripts/ML/NetworkGestureRecognizer.cs` for the connection itself.
- **Laptop side** (`GestureServer/`) — a Python TCP server that captures webcam frames, extracts a 21-point hand skeleton with MediaPipe, classifies it, and sends the result back. See `GestureServer/README.md` for setup, running instructions, and the wire protocol.
- **Dataset tooling** (`GestureServer/webcam_capture.html`, `GestureServer/DATASET_SPEC.md`) — a browser tool for capturing training data in the exact format the classifier will see at runtime, and the spec for whoever is building the dataset.

The recognizer is swappable: today it's a placeholder that only tells open-hand from closed-fist, so the whole pipeline (webcam → laptop → network → headset → lesson logic) is fully testable before the real trained model exists. Swapping in the real model later only touches `GestureServer/classifier.py` — nothing on the Unity side needs to change.

## Requirements

- Unity **6000.3.10f1** (Unity 6) or later — check `ProjectSettings/ProjectVersion.txt` if unsure which editor to install.
- A Meta Quest headset for on-device testing (the Editor/PC loop works without one).
- Python 3.10+ on the laptop that will run `GestureServer/` (see its README for exact packages).
- A laptop and headset on the **same Wi-Fi network** for the two sides to talk to each other.

## Getting started

1. Open this folder as a project in Unity Hub.
2. Open `Assets/Room/Scenes/ClassRoom.unity`.
3. Use the **Tools > SignMasterVR** menu (added by `Assets/Scripts/Editor/SignMasterVREditorSetup.cs`) to generate the lesson content and wire up the scene — run the numbered steps in order, or use "RUN ALL". This builds the alphabet gesture data, the placeholder tutor prefab, the lesson UI, and wires everything together in the currently open scene. Re-running any step is safe.
4. Press **Play**. With the default setup, `C` / the on-screen TEST CORRECT button and `X` / TEST WRONG simulate a correct or incorrect sign, so you can walk through the whole lesson without a webcam or headset connected.
5. To test the real network pipeline instead of the debug buttons: run **Tools > SignMasterVR > 13 Switch To Network Gesture Recognizer**, then follow the setup in `GestureServer/README.md` to start the laptop-side server and point Unity at it.

## Project structure

```
Assets/Scripts/
  Core/      AudioManager, ProgressManager
  Lessons/   LessonManager, LevelManager, GestureData, LevelData, LessonBootstrap
  Tutor/     TutorController (the demonstrating character)
  UI/        UIManager, MainMenuController
  ML/        IGestureRecognizer, GestureResult, FakeGestureRecognizer (debug),
             NetworkGestureRecognizer (talks to the laptop)
  Editor/    SignMasterVREditorSetup — the Tools > SignMasterVR automation menu

Assets/Data/
  Gestures/  One GestureData asset per letter (Gesture_A ... Gesture_Z)
  Levels/    LevelData assets (Level_1_Letters = the alphabet, in order)

Assets/Prefabs/   Tutor.prefab, LessonCanvas.prefab (built by the Editor tooling)
Assets/Room/      The classroom scene

GestureServer/
  server.py            TCP server + webcam loop — run this on the laptop
  hand_tracker.py       MediaPipe hand tracking + landmark normalization
  classifier.py         Swappable classifier interface (placeholder today)
  protocol.py           The wire format shared with NetworkGestureRecognizer.cs
  webcam_capture.html   Browser tool for capturing training data
  DATASET_SPEC.md/.txt  Spec for whoever is building the training dataset
  README.md             Full setup, protocol, and troubleshooting details
```

## Status

The Unity lesson loop (tutor, UI, lesson progression through all 26 letters) and the laptop↔headset network pipeline are both built and working end-to-end with the placeholder classifier. Still open: a trained gesture recognition model from the ML sub-team, a real captured dataset, a rigged tutor avatar in place of the placeholder capsule/cube, recorded audio lines, and a full on-headset (Quest) playtest — the setup above has been exercised in the Editor/PC loop, not yet on-device.

## Team

Part of the PRJ371 group project. This sub-project (Unity/VR + the laptop gesture pipeline) is owned end-to-end by one team member; the trained recognition model and the training dataset are being built by separate sub-teams — see `GestureServer/README.md` and `GestureServer/DATASET_SPEC.md` for the interface contracts they need to hit.
