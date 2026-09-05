# Gesture recognition model

The real classifier behind `server.py`, replacing `PlaceholderClassifier`.
Trained on the 40-clip Hello/Bye webcam capture set.

```
sasl_features.py        canonicalization + feature extraction (shared by training and runtime)
sasl_model.py           the saved artefact: classifier + 3-stage confidence gate
sasl_classifier.py      GestureClassifier adapter -- what server.py constructs
gesture_classifier.py   real-time sliding-window validator
train_gesture_model.py  training, confound audit, model selection, threshold calibration
test_pipeline.py        verification suite (11 checks)
live_demo.py            standalone webcam demo, no headset or network needed
data/                   capture JSON
models/sasl_gesture_model.joblib
```

## Run it

```bash
pip install -r requirements.txt          # now also needs scikit-learn + joblib
python test_pipeline.py                  # 11/11 should pass
python live_demo.py                      # sign at the webcam, no headset needed
python server.py --port 8765             # the real thing
```

`train_gesture_model.py` only needs re-running when you add signs or change
features — the trained model is committed.

## Read this first: the capture set has a confound

In the Hello/Bye data, **every Hello clip is a left hand on the left of frame and
every Bye clip is a right hand on the right of frame.** Handedness and horizontal
position each separate the two classes perfectly on their own. Training prints
this before it starts:

```
hand used (chirality)    Bye: +1.000, Hello: -1.000   best single-split accuracy 100%  <-- LEAK
mean wrist x in frame    Bye: +0.685, Hello: +0.266   best single-split accuracy 100%  <-- LEAK
```

A model fed raw landmarks would hit 100% by learning "left hand = Hello" and then
collapse the moment a learner signs both with the same hand. Three things in
`sasl_features.py` remove it:

1. **Chirality is derived geometrically** from the palm triangle, never read from
   MediaPipe's handedness label. That label flips with mirrored video —
   `webcam_capture.html` shows a mirrored preview, `server.py` calls
   `cv2.flip(frame, 1)` — so trusting it would silently invert features between
   capture and runtime.
2. **Left hands are mirrored** to one canonical chirality, so both hands give
   identical features.
3. **Horizontal position is used only relative to the window's own mean**, so
   which side of frame the signer stands on carries no signal.

`test_pipeline.py` proves it: mirroring the whole video changes the prediction on
0 of 40 clips, as does moving the signer to the other side of frame.

**When capturing the next signs, vary the signing hand and the position within
every label.** The code removes the shortcut; only real variety proves it's gone.
This is worth adding to `DATASET_SPEC.md`'s "vary conditions between takes" list.

## Where this deviates from DATASET_SPEC.md

The spec says to train on each frame's `features` array (the flattened
wrist-relative, scale-normalized 63 numbers) and not to re-derive it. This model
uses the raw `landmarks` instead and re-derives its own normalization. That is a
deliberate deviation, for two reasons:

- **`features` has the wrist subtracted out**, so it cannot represent where the
  hand travels. Hello and Bye are both waves — the trajectory *is* the sign. For
  static fingerspelling A–Z the spec's advice is sound; for dynamic signs it
  discards the discriminating information.
- **`features` still carries the chirality confound.** A left hand's `rel` is the
  mirror image of a right hand's, so a model trained on it learns the hand, not
  the sign, on this dataset.

The capture format itself is unchanged and nothing about `webcam_capture.html`
needs to change — this reads `landmarks` from the same JSON, which the tool
already records. Worth reflecting back into `DATASET_SPEC.md` so the next person
doesn't reconcile the two by hand.

## Results

Leave-one-clip-out cross-validation, augmented copies never crossing the fold
boundary:

| | |
|---|---|
| Best model | logistic regression on 13 PCA components |
| Accuracy | **97.5%** (39/40), one Bye read as Hello |
| Range across all 13 candidates | 90.0% – 97.5% |
| Single-window acceptance of genuine clips | 90% |
| Live sliding-window replay | 23/24 correct, 0 wrong, 1 not detected |

The 97.5% is the best of 13 candidates scored on the same 40 clips, so treat
**92–95% as the realistic expectation** on a new signer. The honest number needs
clips from someone who wasn't in the training set.

## How it works

**Features** (241 dims). Not raw coordinates — with 20 clips per sign, 63
correlated numbers per frame overfits immediately. Each frame becomes a 20-value
pose descriptor (finger extension, curl, spread, palm direction and 3D facing),
and each clip is resampled onto a uniform time grid *by timestamp*, because the
capture runs at a jittery 12–25 fps. On top sit trajectory and motion summaries:
path length, speed, direction reversals, dominant oscillation frequency, and how
much the handshape itself changes — which separates a whole-arm wave from finger
flutter at a still wrist.

**Augmentation.** Each clip spawns 8 variants with random temporal crops,
rotation, zoom, translation, speed and landmark jitter. The crops matter most:
live, the model sees an arbitrary 2.9s window of someone mid-attempt, never a
neatly trimmed clip. Augmentation applies inside training folds only.

**The confidence gate** is what makes this usable in a tutor. A classifier is a
forced choice — show it a shrug and it still answers "Hello", confidently,
because the probabilities must sum to one. Three checks stand in front of it:

| Check | Rejects | Threshold |
|---|---|---|
| Liveness | hand absent, or holding still | motion energy ≥ 1.68 (quietest real clip: 2.15) |
| Novelty | anything unlike the training distribution (Mahalanobis distance in a 16-d PCA space) | 6.72 = 97th percentile over real clips |
| Confidence | attempts falling between signs | 0.72 calibrated probability |

Every threshold is derived from the data at training time, not hand-picked. The
confidence bar is set so both gates together accept 90% of genuine clips — an
explicit trade between "try again" on a real attempt and a confident wrong one.
Probabilities are sigmoid-calibrated on out-of-fold predictions; without that
they saturate at 0.9999 and the bar would be decorative.

Verified rejections: random landmarks (distance 255), an erratic scribble (17.5),
a hand drifting across frame with a static shape (7.6), a still hand, and a
window where the hand is visible 16% of the time.

Rejected attempts surface as `"NONE"`, which `server.py` already treats as "say
nothing" — so the headset is never told a wrong sign was made. The pass/fail
decision stays in Unity: `LessonManager` compares the reported guess against the
target and `GestureData.requiredConfidence`, exactly as with
`FakeGestureRecognizer`. `GestureResult`'s contract is unchanged.

## Wiring into server.py

Two lines changed in the camera loop, and one in `classifier.py`:

- `GestureClassifier.classify()` gained an optional `result=None` second
  argument, and `server.py` now calls `classifier.classify(landmarks, results)`.
  The model reads the raw HandLandmarker result because `rel` has already
  discarded where the hand is. `PlaceholderClassifier` ignores the argument, so
  both classifiers still satisfy one interface — swap back by flipping which one
  `server.py` constructs.
- The classifier buffers ~3 seconds internally and reports a sign only after 3
  overlapping windows agree, then holds it for 1.5s so the headset sees it
  despite the underlying event being momentary.

## Unity has no GestureData for these signs

`Assets/Data/Gestures/` contains A–Z fingerspelling only. The model's labels are
`Hello` and `Bye`, so **`LessonManager` can never set them as a target yet.**
`SASLGestureClassifier` prints a warning about this at startup. Either:

- create `Gesture_Hello.asset` / `Gesture_Bye.asset` in Unity (matching
  `gestureId` exactly), or
- pass `gesture_id_map={"Hello": "A", ...}` to map onto existing ids for a
  smoke test, or
- capture A–Z clips per `DATASET_SPEC.md` and retrain — no code changes needed,
  the label set comes from the data.

## Adding signs

```bash
python train_gesture_model.py --captures data/*.json
python test_pipeline.py
```

Aim for **≥15 clips per sign across at least 3 signers**, with both hands and
varied position and distance. Below 8 clips the script warns you.

## Known limits

- **Two signs, one signer.** Cross-signer generalization is untested and is the
  first thing to measure once a second person contributes captures.
- **Single hand.** `hands[0]` per frame; two-handed signs need a second feature
  block. Matches `hand_tracker.py`'s single-hand runtime, so nothing is lost yet.
- **Signing height is camera-dependent.** Vertical position is deliberately kept
  as a feature (temple vs chest is part of a sign), costing some robustness to
  camera framing — a 5% vertical shift flips 3 of 40 clips. MediaPipe Pose or
  FaceLandmarker would give a body-relative reference and remove this; worth
  doing before the VR port, where the headset supplies head pose for free.
- **Rejection thresholds are tuned against synthetic negatives.** Record 20–30
  real "not a sign" clips — fidgeting, adjusting glasses, half-finished attempts
  — and re-check. The drift case clears the novelty bar by only 7.6 vs 6.72.
- **Aspect ratio.** The capture JSON doesn't record video dimensions, so x and y
  are treated as isotropic. If the capture and runtime cameras differ in aspect
  ratio, handshape features skew slightly. Recording `videoWidth`/`videoHeight`
  in `webcam_capture.html` would let `DEFAULT_ASPECT` fix this properly.
- **scikit-learn version.** The `.joblib` is written by scikit-learn 1.8;
  `requirements.txt` pins that range. If loading ever complains about a version
  mismatch, re-run `train_gesture_model.py` — it takes about 90 seconds.
- **Not the old 26-joint OpenXR spec.** As `hand_tracker.py` warns, this is
  MediaPipe's 21 landmarks. The model metadata records the input spec; anything
  trained against the headset's own hand tracking needs retraining.
