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

`classifier.py`'s `PlaceholderClassifier` is a stand-in — the same role
`FakeGestureRecognizer` plays on the Unity side — so the *whole pipeline*
(webcam → laptop → network → headset → LessonManager) is testable tonight
without the trained ML model. It does **not** recognize real ASL letters,
only open-hand vs. closed-fist:

- **Open hand** toward the camera → reports the current target letter back
  as a high-confidence match (simulates "correct", like pressing `C` /
  clicking TEST CORRECT used to).
- **Closed fist** → reports `"WRONG"` (simulates "incorrect", like `X` /
  TEST WRONG).
- **No hand in view** → reports nothing.

To swap in the real trained model once the ML sub-team delivers one: write a
class implementing `GestureClassifier.classify(landmarks) -> (gesture_id,
confidence)` in `classifier.py` (or a new file), and change the
`classifier = PlaceholderClassifier(...)` line in `server.py`'s `main()` to
construct it instead. Nothing else in this program, and nothing on the Unity
side, needs to change — `NetworkGestureRecognizer` and `LessonManager` only
ever see the resulting `(gestureId, confidence)` pair.

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
- `server.py` — ties it together: the TCP server, the accept/reconnect
  loop, the camera loop, and the debug preview window. Run this one.

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
  `Guess:` never changes from `NONE`, check the console for errors.
