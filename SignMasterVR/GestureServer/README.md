# GestureServer

Runs on a laptop with an external webcam plugged in. Tracks the learner's
hand, classifies it against whatever sign the headset is currently asking
for, and reports back over the local network. This replaces recognizing
gestures on-device on the headset with recognizing them here instead — the
front of the hand is easier to see from an external camera than from the
headset's own downward/forward-facing hand tracking.

## Setup

```
pip install -r requirements.txt
python server.py --port 8765
```

A preview window opens showing the webcam feed with the tracked hand
skeleton drawn on it, plus the current target letter and the classifier's
live guess in the corner. Press `q` in that window to quit. Pass
`--no-preview` to run without it. If the wrong camera opens, try
`--camera 1`, `--camera 2`, etc.

First run: Windows Firewall will likely prompt to allow the program network
access — allow it, otherwise the headset can never connect.

## Connecting it to the headset

1. In Unity, with `ClassRoom.unity` open and Step 4 (Wire Up Current Scene)
   already run: **Tools > SignMasterVR > 13 Switch To Network Gesture
   Recognizer**.
2. Select **LessonSystem** in the Hierarchy → **Network Gesture Recognizer**
   component.
3. **Laptop Host**: this machine's IP address on the *same Wi-Fi network* as
   the headset. On Windows, run `ipconfig` in a terminal and use the
   **IPv4 Address** under your Wi-Fi adapter (not the Ethernet one, unless
   that's what you're actually on). It'll look like `192.168.1.xxx`.
4. **Laptop Port**: whatever `--port` you started `server.py` with
   (`8765` by default).
5. Save the scene (Ctrl+S).

The headset (Unity) is the one that connects out — the laptop just listens.
That means you can start `server.py` first and leave it running; it'll pick
up the headset whenever Play mode starts (in the Editor) or the built app
launches, and keeps listening if the connection drops and comes back
(headset restarted, Wi-Fi hiccup, Play mode stopped and started again) —
no need to restart `server.py` between test runs.

Testing without a headset at all: set **Laptop Host** to `127.0.0.1` and
just enter Play mode in the Editor on the same laptop `server.py` is running
on.

## What it actually recognizes right now

The trained model, `sasl_classifier.py`'s `SASLGestureClassifier` — see
**`ML_MODEL.md`** for how it works, what it scores, and its limits. It knows
two signs, **Hello** and **Bye**, at ~97.5% leave-one-clip-out accuracy.

Unlike the placeholder it reads a **~3 second window**, not one frame: both
signs are waves, and a single frame cannot tell a wave from a hand held still.
It reports a sign only after several overlapping windows agree, and reports
`"NONE"` — which `server.py` treats as "say nothing" — whenever an attempt
isn't recognisable, so the headset is never told a wrong sign was made. The
preview window shows the reason it's staying quiet ("hand is holding still",
"doesn't match any known sign").

**Unity has no `GestureData` for these signs yet.** `Assets/Data/Gestures/`
contains A–Z fingerspelling only, so `LessonManager` can never set Hello or
Bye as a target until someone creates `Gesture_Hello.asset` /
`Gesture_Bye.asset` — or until A–Z clips are captured and the model retrained
(no code changes; the label set comes from the data). The classifier prints a
warning about this at startup.

`classifier.py`'s `PlaceholderClassifier` is still there and still works —
run `python server.py --placeholder` for the old open-hand-vs-fist stand-in,
which is handy for testing the network path on a machine without
scikit-learn installed:

- **Open hand** toward the camera → reports the current target letter back
  as a high-confidence match (simulates "correct").
- **Closed fist** → reports `"WRONG"` (simulates "incorrect").
- **No hand in view** → reports nothing.

Nothing on the Unity side changed — `NetworkGestureRecognizer` and
`LessonManager` still only ever see a `(gestureId, confidence)` pair, and
still make the pass/fail decision themselves against
`GestureData.requiredConfidence`. The one change on this side:
`GestureClassifier.classify()` gained an optional second argument, the raw
HandLandmarker result, because a temporal model needs the absolute landmark
positions that `landmarks` has already had the wrist subtracted out of.

##  Input spec change for the ML team

The project's earlier hand-input spec (see the build tracker's `ml-spec`
item) was **26 joints/hand, wrist-relative, in metres** — that was written
for the headset's own OpenXR hand tracking. Now that recognition runs here
instead, the input is **MediaPipe Hands: 21 landmarks/hand** (see
`LANDMARK_NAMES` in `hand_tracker.py`), wrist-relative, and
**scale-normalized** (unitless, divided by a hand-size reference length) —
not a real-world metric distance. **Please relay this to the ML sub-team** —
anything trained against the old 26-joint spec needs retraining or remapping
against this one before it'll work here.

## Files

- `protocol.py` — the wire format (newline-delimited JSON over TCP) shared
  with `NetworkGestureRecognizer.cs`. Read this first if you're debugging
  the network side.
- `hand_tracker.py` — webcam capture + MediaPipe Hands, landmark
  normalization.
- `classifier.py` — the swappable classifier interface + the placeholder.
- `sasl_classifier.py` — the trained model behind that interface. This is
  what `server.py` constructs by default.
- `server.py` — ties it together: the TCP server, the accept/reconnect
  loop, the camera loop, and the debug preview window. Run this one.
- `ML_MODEL.md` — the model: how it works, what it scores, how to add signs,
  and a confound in the Hello/Bye capture set worth knowing about before you
  capture more data. **Read this before retraining.**
- `sasl_features.py`, `sasl_model.py`, `gesture_classifier.py` — the model's
  internals: feature extraction, the saved artefact + confidence gate, and
  the real-time sliding window.
- `train_gesture_model.py`, `test_pipeline.py` — retraining and its
  verification suite. `python test_pipeline.py` should print 14/14.
- `live_demo.py` — sign at the webcam with no headset or network involved.
  The quickest way to check the model works on this machine.

## Troubleshooting

- **"Could not open webcam index 0"** — another program (Zoom, Teams, the
  Camera app) may already have it open; close those, or try `--camera 1`.
- **Headset never connects** — double-check both devices are on the same
  Wi-Fi network (not one on Wi-Fi and one on Ethernet through a router that
  doesn't bridge them), that the IP in Unity matches this run's actual IP
  (`ipconfig` again if unsure — it can change), and that Windows Firewall
  allowed `server.py`/Python through on the first-run prompt.
- **Connects, but nothing happens when you show your hand** — check the
  preview window: if no skeleton is drawn, MediaPipe isn't seeing a hand
  (lighting, distance, hand out of frame). If a skeleton IS drawn but
  `Guess:` never changes from `NONE`, read the grey line under it — the
  classifier says why it's staying quiet. "filling window" for more than
  ~3 seconds means frames are arriving too slowly; "doesn't match any known
  sign" means it saw a hand but not one of the signs it knows.
- **`ModuleNotFoundError: sklearn` or a joblib version complaint** — run
  `pip install -r requirements.txt` again (the model added scikit-learn and
  joblib). If the version complaint persists, re-run
  `python train_gesture_model.py` to rebuild the model locally (~90s).
