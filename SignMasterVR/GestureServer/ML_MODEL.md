# Gesture recognition model

The real classifier behind `server.py`, replacing `PlaceholderClassifier`.
Trained on 311 clips across **16 signs**: Bye, Can you sign?, Drive, Hello, Help,
Home, How are you, I Sign, I am, I am deaf, My pleasure, Nice to meet you,
Please, Sorry, Thank you, Toilet.

```
sasl_features.py        canonicalization + feature extraction (shared by training and runtime)
sasl_model.py           the saved artefact: classifier + 3-stage confidence gate
sasl_classifier.py      GestureClassifier adapter -- what server.py constructs
gesture_classifier.py   real-time sliding-window validator
train_gesture_model.py  training, confound audit, model selection, threshold calibration
test_pipeline.py        verification suite (13 checks + warnings)
live_demo.py            standalone webcam demo, no headset or network needed
data/                   capture JSON
models/sasl_gesture_model.joblib
```

## Run it

```bash
pip install -r requirements.txt          # now also needs scikit-learn + joblib
python test_pipeline.py                  # 13/13 should pass
python live_demo.py                      # sign at the webcam, no headset needed
python server.py --port 8765             # the real thing
```

`train_gesture_model.py` only needs re-running when you add signs or change
features — the trained model is committed. See *Adding signs, or more data for
existing ones* below.

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
4 of 311 clips — and all four are two-handed signs, where mirroring changes which
hand the tracker follows rather than which sign it sees (see *Known limits*).
Moving the signer to the other side of frame is likewise near-total.

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

Clip-grouped cross-validation, augmented copies never crossing the fold boundary
(leave-one-clip-out up to 60 clips, 10-fold above that):

| | |
|---|---|
| Signs | 16 |
| Clips | 311 (20 per sign; Drive has 11) |
| Best model | logistic regression on 24 PCA components |
| Accuracy | **97.4%**, 10-fold grouped CV |
| Top scorer | random forest at 98.1% -- not used, see below |
| Single-window acceptance of genuine clips | 90% |
| Live sliding-window replay | 29/32 correct, 0 wrong |
| Replay through the server adapter | 29/32 correct, 0 wrong |
| Training time | ~3.5 min full sweep, ~70s with `--quick` |

The random forest scored 0.7 points higher but pickles to **60 MB** against 400 KB
and is slower per frame. On 311 clips that gap is two clips -- inside the noise --
so the trainer takes the simplest model within `--tolerance` (default 1.5%) of the
best. Pass `--tolerance 0` to always take the top scorer.

Only two signs get confused: Bye with Hello (both waves), and I am with How are
you. Everything else is clean.

Cross-signer accuracy is still unmeasured -- every clip is one person. That
remains the number that matters for a tutor.

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
| Liveness | hand absent, or holding still | motion energy ≥ 1.51 (quietest real clip: 1.55) |
| Novelty | anything unlike the training distribution (Mahalanobis distance in a 16-d PCA space) | 4.32 = 97th percentile over real clips |
| Confidence | attempts falling between signs | 0.43 calibrated probability |

The confidence bar scales with the number of signs. Calibrated probabilities
spread across however many classes exist, so a confident 2-sign answer sits near
0.95 and an equally confident 16-sign answer near 0.65. The floor is
`min(0.5, max(0.20, 3/n_signs))` -- a fixed 0.5 was right for two signs and
silently rejected 19% of genuine clips at sixteen.

Every threshold is derived from the data at training time, not hand-picked. The
confidence bar is set so both gates together accept 90% of genuine clips — an
explicit trade between "try again" on a real attempt and a confident wrong one.
Probabilities are sigmoid-calibrated on out-of-fold predictions; without that
they saturate at 0.9999 and the bar would be decorative.

Verified rejections: random landmarks, a hand drifting across frame with a static
shape, a still hand, and a window where the hand is visible 16% of the time.

`test_pipeline.py` now **warns** rather than fails on one synthetic negative, a
fast erratic scribble. Against two signs it was comfortably rejected; against
sixteen mostly-dynamic phrase signs its novelty distance sits *below* the median
genuine clip, because "fast sinusoidal hand movement" genuinely is one of these
signs. The proxy stopped being valid, not the gate. Recording real non-signs is
the fix.

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

## Adding signs, or more data for existing ones

Four steps, no code changes — the label set and the number of classes both come
from the `label` fields in the capture JSON.

1. Record with `webcam_capture.html` as usual. Put the sign names in the label
   list (they become the model's labels verbatim, so use the exact `gestureId`
   Unity expects) and export.
2. Drop the exported file into `data/`. **Keep it as its own file** — don't merge
   it into an existing one. Every `.json` in `data/` is loaded and merged
   automatically, and separate files preserve which session and which person each
   clip came from. That provenance is what lets the script warn when a sign only
   ever appears in one capture file, and it's what a future per-signer evaluation
   needs. Merging by hand throws it away and risks corrupting a working file for
   no benefit.
3. `python train_gesture_model.py` — reads everything in `data/` and overwrites
   `models/sasl_gesture_model.joblib`. Add `--quick` to skip the slower
   candidates (~3x faster, usually within a point). Progress lines show elapsed
   time; **the model is written only at the very end**, so a Ctrl-C part-way
   leaves the old model in place and the demo keeps showing the old signs.
4. `python test_pipeline.py`, then restart `server.py` to pick up the new model.

To train on a subset instead, name files or patterns:
`python train_gesture_model.py --captures data/hello_bye.json data/letters_*.json`
(the script expands the patterns itself, because `cmd.exe` and PowerShell don't).

Aim for **≥15 clips per sign across at least 3 signers**, with both hands and
varied position and distance. The script prints a per-sign table and flags
anything under 15, plus any sign that came from only one capture file.

Two things it handles for you, both of which bit this dataset:

- **Duplicate takes.** The capture tool exports cumulatively, so a later export
  can contain every take from an earlier one. `Thank_You_My_Pleasure.json` is
  entirely contained in `I_Sign,I_am_Deaf,Can_You_Sign.json` — 40 clips. Loading
  both would put an identical twin of a held-out clip into the training fold and
  inflate the score. Clips are de-duplicated by their actual landmark stream and
  the overlap is reported.
- **Snapshots.** Single-frame captures (Space rather than C) carry no motion, and
  this model reads ~3s of it. 89 of the 105 A–Z captures are snapshots, which
  left H, J, P and Z with 4 usable clips each and every other letter with none —
  so all 26 letters were dropped. **Re-capture the alphabet with C (clip).**
  Signs with fewer than `--min-clips` (default 5) usable clips are dropped with
  a message rather than silently poisoning the fold split.

Training time scales with the number of clips. Up to 60 clips it uses
leave-one-clip-out, which wastes nothing on a small set (~90 seconds for 40
clips). Above that it switches to 10-fold grouped CV — leave-one-clip-out costs
one model fit per clip per candidate, so a full A–Z set would otherwise mean
~6,800 fits and hours of waiting for a number 10 folds estimates just as well.
Expect a few minutes for a few hundred clips.

Re-read the confound audit each time. It runs against whatever is in `data/`, so
a shortcut introduced by a new capture session — one signer always closer to the
camera, one sign only ever recorded left-handed — gets flagged before you trust
the accuracy underneath it.

## Known limits

- **Two signs, one signer.** Cross-signer generalization is untested and is the
  first thing to measure once a second person contributes captures.
- **Two-handed signs are only half-seen.** The pipeline follows one hand, but
  four of the 16 signs are genuinely two-handed: **I Sign** (99% of frames show
  two hands), **Home** (86%), **Can you sign?** (75%), **Nice to meet you**
  (56%). They still classify correctly, because one hand of each is distinctive
  among these 16 -- but it is half the evidence, and which hand gets followed is
  not stable: mirroring the video flips the prediction on 4 of 311 clips, all of
  them two-handed. Adding a second-hand feature block (its handshape, and its
  position relative to the first) is the single biggest accuracy and robustness
  win available, and it grows more important with every two-handed sign added.
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
