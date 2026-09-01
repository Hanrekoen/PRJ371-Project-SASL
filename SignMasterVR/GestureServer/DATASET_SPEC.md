# Gesture dataset — what to capture and how

This is the spec for building the training dataset the gesture recognition model will learn from. Read this before capturing anything — the format matters more than the volume, because it has to match exactly what the app feeds the model when it's actually running.

## Use the webcam, not the headset

Capture with a laptop's webcam, using `GestureServer/webcam_capture.html` — not the Quest headset's own hand tracking. This isn't a style preference: the deployed app recognizes gestures via an external webcam on a laptop (the front of the hand is more visible from a webcam than from the headset's own downward-facing cameras), so the training data has to come from the same kind of camera and the same tracking library (MediaPipe Hands), or a model trained on it won't generalize to what the app actually sees at runtime.

## How to capture

1. Open `GestureServer/webcam_capture.html` in a normal browser (Chrome/Edge). If the camera prompt doesn't appear, run `python -m http.server 8000` from inside `GestureServer/` and open `http://localhost:8000/webcam_capture.html` instead.
2. Leave the label list as `A,B,C,...,Z` (or edit it if you're capturing something else) and click **Start Camera**, then allow camera access.
3. For each letter: hold the handshape steady, then either:
   - **Space** — captures one snapshot after the countdown.
   - **C** — records a short clip (the countdown, then it records for the clip length shown, capturing natural micro-movement instead of one frozen instant).
4. **N** / **P** (or the on-screen buttons) move to the next/previous label. The tool tracks how many takes you've done per label and auto-advances once you hit the target repeat count.
5. **Backspace** undoes the last capture if a take was bad (hand left frame, wrong shape, etc).
6. When you're done, click **Download JSON** and send that file back. It also autosaves to the browser as you go, so a closed tab won't lose your session.

## How much to capture

- All 26 letters, one hand only (the tool is set to track a single hand — that matches how the app will actually be used).
- Aim for **at least 20–30 takes per letter**, more if you can. The tool's own default (5) is fine for a quick smoke test, not for real training.
- Vary conditions between takes of the same letter: move your hand a bit closer/further from the camera, rotate your wrist slightly, shift position in frame, change the lighting if you can, sign at slightly different speeds for clips. A dataset where every "A" looks pixel-identical trains a model that only recognizes that one exact position.
- Mix snapshots and clips — clips capture the small natural variation a real hand has even when "holding still," which helps the model generalize.
- If more than one person on the team can contribute captures, that's genuinely valuable — a model trained on only one person's hand tends to overfit to that person's hand size and signing style.

## Check separability before grinding through the whole alphabet

After capturing even a handful of letters, click **Build report**. It shows, for every pair of letters captured so far, how far apart the webcam actually sees them versus how much each one wobbles on its own. Anything flagged "not separable" means the webcam/tracker genuinely cannot tell those two letters apart — more captures of them won't fix that, it's a signal problem, not a data-volume problem, and worth flagging back to the team early rather than after the full dataset is built.

## The exact format

Every capture is one JSON object in a top-level array. Multiple sessions/people can be combined later by just concatenating the arrays from each exported file into one — don't worry about merging them yourself unless asked.

```json
{
  "label": "A",
  "take": 3,
  "kind": "snapshot",
  "timestamp": "2026-09-01T20:14:03.512Z",
  "source": "webcam-browser-mediapipejs",
  "landmarkOrder": ["WRIST", "THUMB_CMC", "..." /* 21 names total */],
  "frameCount": 1,
  "frames": [
    {
      "t": 1234.5,
      "landmarks": [{ "x": 0.482, "y": 0.601, "z": -0.003 }, "... 21 points, raw MediaPipe output"],
      "rel": [{ "x": 0.0, "y": 0.0, "z": 0.0 }, "... 21 points, wrist-relative"],
      "features": [0.0, 0.0, 0.0, "... 63 numbers total (21 points x/y/z, flattened)"]
    }
  ]
}
```

The field that actually matters for training is **`features`** — a flat array of 63 numbers per frame (21 landmarks × x/y/z), computed as: subtract the wrist position from every point, then divide the x and y of every point by the wrist-to-middle-knuckle distance (z is left as-is). That's not an arbitrary choice — it's the exact same formula the app runs live on the laptop during a real lesson (`GestureServer/hand_tracker.py`), so a model trained on `features` here sees the same shape of input it'll get at runtime. Don't re-derive or re-normalize it a different way; use it as given.

`label` must match the letter exactly as Unity expects it (`"A"`–`"Z"`, uppercase, matching `GestureData.gestureId`) — that's what the model's predictions get compared against later.

## One limitation to know about

The capture tool only tracks one hand at a time, which is fine for fingerspelling (each letter is a single handshape) but won't work if the project later adds two-handed signs — that would need a different capture setup.
