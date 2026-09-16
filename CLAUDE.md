# SignMasterVR — working notes for Claude Code

VR South African Sign Language tutor. A Quest headset runs the lesson; a laptop
with a webcam does the gesture recognition and reports results over TCP.

```
SignMasterVR/
  Assets/               Unity project
    Scripts/Lessons/    LessonManager, LevelData, GestureData, GhostHandPlayer
    Scripts/ML/         IGestureRecognizer, GestureResult, NetworkGestureRecognizer, FakeGestureRecognizer
    Data/Gestures/      GestureData assets — currently A-Z only
    Data/Levels/        LevelData assets
  GestureServer/        the Python side. Everything ML lives here.
```

Read `GestureServer/ML_MODEL.md` before touching the recogniser, and
`GestureServer/NEXT_STEPS.md` for what is currently unfinished.

## Commands (run from GestureServer/)

```bash
.venv\Scripts\activate
python train_gesture_model.py            # ~4 min; --quick for ~90s
python test_pipeline.py                  # 12/12 + 1 known warning
python live_demo.py                      # webcam only, no headset
python server.py --port 8765             # the real thing
python server.py --placeholder           # old open-hand/fist stand-in
python export_ghost_hand.py --out ../Assets/StreamingAssets/GhostHands
```

## Invariants — breaking these fails silently, which is the problem

**`sasl_features.py` is the single source of truth for features.** Training and
live inference both import it. Change anything in it and you must retrain, or the
model gets a differently-shaped input at runtime than it saw in training and
degrades without erroring.

**Never use MediaPipe's `handedness` label for anything that affects features.**
It flips depending on whether the video is mirrored — the browser capture tool
previews mirrored, `server.py` calls `cv2.flip`. Chirality is derived
geometrically from the palm triangle instead (`palm_chirality`), and the dominant
hand is chosen by how much it moves, not by which hand it is.

**Chirality is one decision per window, not per frame.** An edge-on hand makes the
palm triangle degenerate and the sign flip, which teleports the canonical wrist by
a hand-width and injects phantom velocity. This was a real bug affecting 96 of 327
clips. See `majority_chirality()`.

**`HandTracker` must be constructed with `max_hands=2`.** Four of the trained signs
are two-handed; `max_hands=1` silently shows the model half of each.

**The classifier needs the raw HandLandmarker result, not `rel`.**
`classify(landmarks, results)` — `rel` has the wrist subtracted out, so it cannot
represent where the hand travels, and these signs are mostly trajectory.

**The model file is written only at the very end of training.** A Ctrl-C part-way
leaves the previous model in place with no error, and the demo keeps showing the
old signs. If new signs don't appear, check the model file's timestamp first.

## Data conventions

- Capture with `webcam_capture_full.html` (hands + body + face) or the older
  `webcam_capture.html` (hands only). Both formats load.
- **Record clips (`C`), never snapshots (`Space`.)** A snapshot is one frame; the
  model reads ~2 seconds of motion and drops anything under 4 frames.
- Drop each export into `GestureServer/data/` as **its own file**. Everything in
  there is merged automatically; separate files preserve which session and which
  person each clip came from, which the trainer reports on.
- 5 takes per sign is the floor (the trainer drops anything less), 8 is the sweet
  spot — measured, the curve is flat past 8.
- Vary the signing hand and where you stand. The first dataset was fully
  confounded (all Hello = left hand, all Bye = right hand); the code removes that
  shortcut but only varied capture proves it is gone. The trainer runs a confound
  audit each run — read it.

## Unity side

`LessonManager` asks `_recognizer.SetTargetGesture(gesture.gestureId)`, gets a
`GestureResult` back, and does:

```csharp
bool correct = result.GestureId == gesture.gestureId
               && result.Confidence >= gesture.requiredConfidence;
```

So a sign can only be a lesson target if it has a `GestureData` asset in
`Assets/Data/Gestures/` **and** appears in a `LevelData`. The model currently
knows 16 phrase signs that have no assets — see NEXT_STEPS.md.

**`requiredConfidence` must scale with the number of signs.** The 0.75 default was
right for 2 classes. At 16, calibrated probabilities spread across 16 options and
only 41% of correct signs clear 0.75; 0.45 passes 99%. Don't copy 0.75 onto new
assets.

`GhostHandPlayer.cs` has never been compiled — it was written against the project's
conventions but not opened in Unity. Its hand-rolled JSON parser is the likely
first thing to need fixing.

## Style

- Python: stdlib + numpy/sklearn/opencv/mediapipe. No new dependencies without a
  reason; `requirements.txt` pins scikit-learn to 1.8.x because the committed
  `.joblib` is written by it.
- Comments explain *why*, especially where a simpler-looking approach is wrong —
  most of the non-obvious code here exists because the obvious version failed a
  measurement. Keep that habit; the measurements are in ML_MODEL.md.
- Verify changes by running `test_pipeline.py`, not by reasoning about them. If a
  check fails, find out whether the model regressed or the check stopped being
  fair — both have happened.
