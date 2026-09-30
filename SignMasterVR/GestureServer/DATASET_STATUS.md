# Dataset — what's outstanding

Measured 21 September 2026 against the standards the project already set for
itself in `ML_MODEL.md`. "100%" below means those standards, not an invented bar.

## The bar, and where we are

| Requirement | Source | Target | Today |
|---|---|---|---|
| Signs covered | — | 42 | **42** ✓ |
| Unique clips per sign | `ML_MODEL.md` | ≥ 15 | letters **10**, phrases **11–20** |
| Signers | `ML_MODEL.md` | ≥ 3 | **1** |
| "Not a sign" clips | `ML_MODEL.md`, `NEXT_STEPS.md` | 20–30 | **0** |
| Capture frame rate | pipeline assumes 12–25 fps | ≥ 12 fps | **153 of 574 clips** |
| Ghost-hand demo clips | exporter needs ≥ 8 frames | 42 | **37** |

Raw take count is 665; after the de-duplication the trainer performs, **574 are
unique**. `Home`, `My pleasure`, `Thank you` and `Toilet` each look like 40 takes
and are actually 20. `Drive` looks like 22 and is 11 — the thinnest sign in the set.

## Four gaps

### 1. One signer — the only one that changes the headline number

Cross-signer accuracy is unmeasured, and it is the number that predicts whether
this works for anyone but Hanre. The learning curve is already flat past 8 takes
(3 → 87.5%, 5 → 97.5%, 8 → 98.4%, no gain to 20), so more recording by the same
person cannot move it. Two more people at 8 takes each is the whole fix.

### 2. Frame rate is inconsistent and undocumented-low

Capture rate decayed across the alphabet session from 17.1 fps on A (10:03) to
2.8 fps on Z (12:08) — the takes are all the correct ~1.9s length, so nothing
looked wrong while recording. Six phrases are affected too (`Can you sign?` 3.0,
`I am deaf` 3.4, `I Sign` 4.5, `Help` 6.8, `Bye` 7.2, `Home` 8.0).

Be careful about how much to read into this. The letters model still scores 95.8%
and the phrases model 98.4%, so this data is *not* worthless, and the resampling
step absorbs some of it. What is established:

- **Proven:** V, W, X, Y and Z produce no ghost-hand demo, because every take
  falls under the exporter's 8-frame floor.
- **Suspected, not proven:** R, Z and U are three of the four weakest letters for
  recall and all sit in the sparse zone. H is the fourth and sits mid-range, so
  frame rate is a plausible contributor rather than the whole story.

Treat it as quality debt to avoid repeating, not as a reason to distrust the
current models.

### 3. Letters are at 10 unique clips, against a documented target of 15

`train_gesture_model.py` already flags anything under 15. All 26 letters are under.

### 4. No negative clips at all

The reject gate is tuned against synthetic negatives, and two of five now get
through on the phrases model. Sweeping the gate's embedding from 8 to 48
dimensions never separated the drift case, so it is not a tuning problem — it
needs 20–30 real "not a sign" clips: fidgeting, adjusting glasses, scratching,
half-finished attempts, hands at rest.

## Do it as one pass, not four chores

Gaps 1, 2 and 3 close together. If a second and third person each record 8 takes
of all 42 signs at a healthy frame rate, that simultaneously adds the missing
signers, lifts every sign from 10–20 clips to 26–36, and injects high-frame-rate
data for every sign in the set. Running them as separate capture programmes wastes
most of the effort.

| Task | Who | Effort |
|---|---|---|
| 42 signs × 8 takes | signer 2 | ~60–75 min, **split into 3 sittings** |
| 42 signs × 8 takes | signer 3 | ~60–75 min, split the same way |
| 20–30 negative clips | anyone | ~10 min |
| Re-capture M–Z (optional, unblocks ghost demos sooner) | Hanre | ~25 min |
| Retrain + `make_unity_assets.py --write` + `compare_split.py` | — | ~10 min |

## Capture settings, so the frame-rate problem doesn't recur

- **Keep each sitting under ~25 minutes** and restart the browser between them.
  The decay is gradual and invisible in the takes themselves.
- **Watch the frame counter, not the clip length.** A healthy take at ~1.9s should
  land near 25–35 frames. Under 20 means the browser is already struggling.
- Close other tabs and anything using the webcam.
- Vary the signing hand, position in frame and distance *within* each label. The
  code removes the handedness/position shortcut, but only real variety proves it.
- **Save each person's session as its own file** in `data/`. Never merge by hand —
  separate files are what preserve which clip came from whom, which is exactly
  what a cross-signer evaluation needs.

## One thing to decide before re-capturing

Everything in `data/` is loaded and merged. If M–Z is re-captured into a new file,
each of those letters ends up with 10 sparse old takes plus the new good ones, and
the trainer has no way to tell them apart. Either accept the mix, or split
`webcam-sign-captures-Alphabet.json` first — keep A–C (the only letters captured
above 10 fps) and retire the rest. Deleting the whole file loses good A–C data.
