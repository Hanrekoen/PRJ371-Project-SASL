# Gesture recognition model

The real classifier behind `server.py`, replacing `PlaceholderClassifier`.
Trained on 570 clips across **42 signs** — the 26 fingerspelled letters A–Z, and
16 phrases: Bye, Can you sign?, Drive, Hello, Help, Home, How are you, I Sign,
I am, I am deaf, My pleasure, Nice to meet you, Please, Sorry, Thank you, Toilet.

**Those 42 signs are served by two models, not one** — a letters model and a
phrases model, routed at runtime by which one knows the current target. That is
the single most important thing on this page; see *The two-model split* below for
the measurement that forced it.

```
sasl_features.py        canonicalization + feature extraction (shared by training and runtime)
webcam_capture_full.html  capture with hands + body pose + face (for the avatar and for arm position)
export_ghost_hand.py    capture JSON -> ghost-hand demo clips
ghost_preview.html      preview an exported demo clip without Unity
GhostHandPlayer.cs      Unity component that plays one (goes in Assets/Scripts/Lessons/)
sasl_model.py           the saved artefact: classifier + 3-stage confidence gate
sasl_classifier.py      SASLGestureClassifier + RoutedGestureClassifier (what server.py constructs)
gesture_classifier.py   real-time sliding-window validator
train_gesture_model.py  training, confound audit, model selection, threshold calibration
test_pipeline.py        verification suite (12 checks + warnings)
compare_split.py        one 42-sign model vs the letters/phrases split, on windowed replay
make_unity_assets.py    writes GestureData/LevelData assets with measured requiredConfidence
live_demo.py            standalone webcam demo, no headset or network needed
data/                   capture JSON
models/sasl_letters.joblib      26 letters   <- the runtime pair
models/sasl_phrases.joblib      16 phrases   <-
models/sasl_gesture_model.joblib  all 42 in one model, kept as a fallback
```

## Run it

```bash
pip install -r requirements.txt          # now also needs scikit-learn + joblib

python test_pipeline.py --model models/sasl_phrases.joblib   # 12/12, 1 warning
python test_pipeline.py --model models/sasl_letters.joblib   # 10/12, both detection-rate

python live_demo.py --model models/sasl_phrases.joblib       # sign at the webcam

# the real thing -- pass --model twice, one model per level
python server.py --port 8765 \
    --model models/sasl_letters.joblib \
    --model models/sasl_phrases.joblib
```

`train_gesture_model.py` only needs re-running when you add signs or change
features — the trained models are committed. See *Adding signs, or more data for
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
2 of 311 clips, and the mean feature vector moves by 1.1%. Moving the signer to
the other side of frame changes 1 of 311.

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

| | letters | phrases | all 42 in one |
|---|---|---|---|
| Signs | 26 | 16 | 42 |
| Clips | 259 | 311 | 570 |
| Accuracy | **95.8%** (6-fold) | **98.4%** (10-fold) | 97.2% (6-fold) |
| Confidence bar | 0.46 | 0.51 | 0.27 |
| Liveness floor | 0.36 | 1.56 | 0.47 |
| `test_pipeline.py` | 10/12 | 12/12, 1 warning | — |
| Synthetic negatives rejected | 2 of 2 | 0 of 2 | 0 of 2 |

Windowed replay, 84 clips, two takes per sign:

| | correct | **wrong** | missed |
|---|---|---|---|
| one 42-sign model | 64 | **7** | 13 |
| letters + phrases, routed | 65 | **1** | 18 |

Every clip is still **one signer**. Cross-signer accuracy remains unmeasured and
remains the number that matters.

## The two-model split

The single 42-sign model names the wrong sign seven times in 84 attempts —
`Thank you→Hello`, `Bye→Help`, `K→N`, `K→R`, `U→R`, `U→R`, `I am→Toilet`. Split
in two, only `I am→Toilet` survives, at no cost in detections.

The cause is that **every threshold in the pipeline is calibrated against the
class set**, and letters and phrases pull them in opposite directions:

- The liveness floor comes from the quietest genuine clip. A held letter barely
  moves, so mixing letters in drags the floor from 1.56 down to 0.47 — and the
  gate stops rejecting a resting hand *for the phrases too*, which never needed
  that concession.
- The confidence bar falls as classes are added, because calibrated probability
  spreads over more options. At 42 it lands at 0.27, low enough that a confusable
  pair clears it.

Split, each model keeps the thresholds its own signs justify. The letters model
rejects both synthetic negatives (drift 11.5 > 4.6, scribble 27.8 > 4.6) that the
combined model waves through.

Routing is on **what a model knows**, not on the level number:
`RoutedGestureClassifier` picks whichever model has the current target in its
label set. There is no convention to keep in sync, and it survives a sign moving
between levels. Re-run `compare_split.py` after any retrain — if the split ever
stops winning, say so and drop back to the single model.

## Two hands, chirality, and the window

Three fixes took the phrases model from 97.4% to 98.4%, in order of how much they
mattered:

**Both hands are now read.** Four signs are genuinely two-handed — I Sign (two
hands in 99% of frames), Home (86%), Can you sign? (75%), Nice to meet you
(56%). All four now score 97.5–100%. The feature vector carries a second block
(the support hand's shape, and its position relative to the dominant hand),
302 dims in total.

Which hand is "dominant" is decided by **how much each one moves over the
window**, never by MediaPipe's Left/Right label — that label flips with mirrored
video, so it would silently swap the two feature blocks between the capture tool
and the live server. Motion is a magnitude, so it survives mirroring.

**A chirality bug was corrupting the trajectory features.** Chirality was decided
per frame; when a hand turns edge-on the palm triangle degenerates and the sign
flips for a frame or two, teleporting the canonical wrist by about a hand-width.
That produced ~200 hand-widths/second of phantom speed. It affected **96 of 327
clips** — every take of "I am" and "Nice to meet you" among them. Chirality is
now one decision per window, by majority vote.

**The window was longer than the signs.** It was fixed at 2.9s, from the original
Hello/Bye clips. The median sign here runs 2.2s and 221 of 311 clips are shorter
than 2.9s, so every window carried a second of whatever came before or after.
The window is now taken from the data at training time (2.2s here) and stored in
the model. That alone recovered 3 detections out of 32 in replay.

Confusions left: Bye/Hello (both waves) and I am/How are you among the phrases;
R, Z, H and U have the lowest recall among the letters.

## How it works

**Features** (302 dims: 241 for the dominant hand, 61 for the support hand). Not raw coordinates — with 20 clips per sign, 63
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
| Liveness | hand absent, or holding still | motion energy ≥ 1.56 (quietest real clip: 1.55) |
| Novelty | anything unlike the training distribution (Mahalanobis distance in a 32-d PCA space) | 5.47 = 97th percentile over real clips |
| Confidence | attempts falling between signs | 0.54 calibrated probability |

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

`test_pipeline.py` **warns** rather than fails on two synthetic negatives now —
the erratic scribble and the drifting hand. Both were comfortably rejected at two
signs; at sixteen mostly-dynamic ones they sit inside the genuine distribution,
because "fast sinusoidal hand movement" and "a held handshape moving slowly" are
plausible descriptions of real signs in this set. The proxies stopped being
valid, not the gate — see *Known limits*.

Rejected attempts surface as `"NONE"`, which `server.py` already treats as "say
nothing" — so the headset is never told a wrong sign was made. The pass/fail
decision stays in Unity: `LessonManager` compares the reported guess against the
target and `GestureData.requiredConfidence`, exactly as with
`FakeGestureRecognizer`. `GestureResult`'s contract is unchanged.

## Wiring into server.py

- `GestureClassifier.classify()` gained an optional `result=None` second
  argument, and `server.py` now calls `classifier.classify(landmarks, results)`.
  The model reads the raw HandLandmarker result because `rel` has already
  discarded where the hand is. `PlaceholderClassifier` ignores the argument, so
  both classifiers still satisfy one interface — swap back by flipping which one
  `server.py` constructs.
- The classifier buffers one window internally (length stored in the model, ~2.2s
  for phrases) and reports a sign only after several overlapping windows agree,
  then holds it so the headset sees it despite the underlying event being
  momentary.
- `--model` is repeatable. One path builds a `SASLGestureClassifier`; two or more
  build a `RoutedGestureClassifier`, which forwards `set_target()` to every member
  and classifies with whichever one knows that target.
- When the target changes, `server.py` clears the rolling window and re-targets.
  Without that, the next judgement is made partly from frames captured while the
  *previous* sign was still on screen.

### `targetConfidence`

The wire `result` message now carries `targetConfidence` — the model's calibrated
probability for **the sign that was asked for** — alongside `gestureId` and
`confidence` (the argmax and its probability). `LessonManager` grades on
`targetConfidence` when it is present and falls back to the argmax comparison
when it isn't, so the old `FakeGestureRecognizer` still works. `gestureId`
survives as the "that looked like *X*" hint on a failed attempt.

Worth knowing: measured on this data, grading on `targetConfidence` rather than
the argmax is roughly a wash (69 passes vs 68). It was kept because it makes the
failure message useful and decouples pass/fail from a 42-way argmax, not because
it moved the accuracy number.

## Unity assets: all 42 signs now exist

`make_unity_assets.py` writes them. All 26 letters plus all 16 phrases have a
`GestureData` asset in `Assets/Data/Gestures/`, and `Level_2_Phrases` references
the 16.

**Never hand-pick `requiredConfidence`, and never copy the old 0.75.** That value
was right for two classes; at 26 or 42 it fails almost every genuine attempt, and
it silently fails them — the learner just never passes. `make_unity_assets.py`
derives it per model from the trained model itself: the bar that passes 95% of
that model's own genuine clips (`--target-pass`, default 0.95). Today that is
**0.34 for letters and 0.42 for phrases**. The 26 letter assets were sitting at
0.75 until this run.

A per-sign bar was tried and measured *worse* than a flat per-model floor (61
passes vs 69), so it stays behind a `--per-sign` flag and off by default.

Retraining changes these numbers. Re-run `make_unity_assets.py` (dry run first,
`--write` to apply) after every retrain, or the assets and the model disagree.

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

- **42 signs, one signer.** Cross-signer generalization is untested and is the
  first thing to measure once a second person contributes captures. The learning
  curve against takes-per-sign is flat past 8 (3 takes 87.5%, 5 takes 97.5%, 8
  takes 98.4%, no gain to 20), so **more signers, not more takes, is the
  bottleneck.**
- **Letters detect less often than phrases** — 36 of 52 in windowed replay, but 0
  wrong. Some of that is a replay artefact: letter clips run about 1.9s against a
  1.9s window, so every window in the loop spans a seam. Confirm against a real
  camera before tuning anything.
- **Never run offline replay without `GestureValidator.reset()` clearing the
  refractory clock.** It didn't, until this pass. Live it was invisible, because
  time only moves forward; in replay it suppressed nearly every detection (6% vs
  76%). Any offline measurement taken before that fix, in this file or elsewhere,
  is wrong.
- **Three hands or more is not handled** — two is the cap, which matches signing.
- **The confidence gate is now the weakest part.** Two of the five synthetic
  negatives get through at 16 signs: a fast erratic scribble, and a real
  handshape sliding slowly across frame. Both are measured, not guessed —
  sweeping the gate's embedding from 8 to 48 dimensions never separated the
  drift case from genuine clips, so it is not a tuning problem. With this many
  dynamic signs those synthetic proxies have simply stopped being fair tests.
  **Record 20–30 real "not a sign" clips** — fidgeting, adjusting glasses,
  half-finished attempts — label them and re-tune against those. This is the
  top remaining risk and the cheapest thing left to fix.
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
