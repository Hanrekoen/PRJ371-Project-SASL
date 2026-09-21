# Where this stands and what's next

Last updated 18 September 2026. Branch `ML-Validation`, **uncommitted working
tree**. Claude Code must not run git — see CLAUDE.md.

## State

| | |
|---|---|
| Signs | **42** — 26 letters + 16 phrases, served by **two models** |
| Clips | 570, **all from one signer** |
| Letters model | 26 signs, **95.8%** (6-fold grouped CV), bar 0.34 |
| Phrases model | 16 signs, **98.4%** (10-fold grouped CV), bar 0.42 |
| Combined 42-sign model | 97.2% — kept as a fallback, **not** the recommended runtime |
| Windowed replay, split | 65 correct, **1 wrong**, 18 missed of 84 |
| Windowed replay, single | 64 correct, **7 wrong**, 13 missed of 84 |
| Headset end-to-end | **still never run** |

## The split is now the recommended runtime

```bash
python server.py --model models/sasl_letters.joblib --model models/sasl_phrases.joblib
```

`--model` can be repeated. `RoutedGestureClassifier` picks whichever model knows
the current target — routing on what a model knows, not on the level number, so
there is no convention to keep in sync and it survives signs moving between
levels.

Why it wins: every threshold in the pipeline is calibrated against the number of
classes, and mixing static letters with dynamic phrases wrecks both ends.

```
one 42-sign model : confidence bar 0.27, liveness floor 0.47
letters model     : confidence bar 0.46, liveness floor 0.36
phrases model     : confidence bar 0.51, liveness floor 1.56
```

The liveness floor is derived from the quietest genuine clip. Held letters barely
move, so mixing them in drags the floor down and the gate stops rejecting a
resting hand — for the phrases too, which never needed that. Split, each model
keeps the thresholds its own signs justify. The letters model now rejects **both**
synthetic negatives the combined model let through.

## Done in this pass

- **Two-model split, measured and wired in.** Wrong fires 7 → 1.
- **`targetConfidence` end to end.** Python side plus the three C# edits —
  `GestureResult.cs`, `NetworkGestureRecognizer.cs`, `LessonManager.cs`. Also a
  "that looked like X" hint on failure, which is what `GestureId` is still for.
  **The C# has not been compiled.**
- **All 42 GestureData assets exist**, `requiredConfidence` measured per model
  (letters 0.34, phrases 0.42 — the value that passes 95% of each model's own
  genuine clips), plus `Level_2_Phrases`. The 26 letter assets were updated from
  0.75, which passed almost nothing.
- **Bug fixed: `GestureValidator.reset()` didn't clear the refractory clock.**
  Invisible live because time only moves forward; it silently suppressed nearly
  every detection in offline replay (6% vs 76%). Any past offline measurement
  that reused one classifier across clips was wrong.

## Ranked next steps

1. **Open Unity, let it import, check one asset in the Inspector**, then compile.
   The C# is unverified — its most likely failure is a small syntax slip.
2. **Run the lesson end to end.** Start the server with both models, enter Play
   mode, watch the server console: it prints every target received and every
   result sent.
3. **Letters detect less often than phrases** — 36/52 in replay, 0 wrong. Some of
   that is a replay artifact (letter clips are 1.9s against a 1.9s window, so every
   window spans a loop seam), but confirm against a real camera with
   `python live_demo.py --model models/sasl_letters.joblib` before tuning anything.
4. **Get 2-3 other people to record 8 takes each.** Still one signer. Still the
   number that matters.
5. **Record 20-30 real "not a sign" clips.** The phrases model still lets a
   drifting hand through.
6. **Re-capture the weak letters** — R, Z, H, U had the lowest recall.
7. **Compile and test `GhostHandPlayer.cs`.** Never opened in Unity.

## Known-good numbers to beat

- Letters model: 95.8% clip-level, 6-fold grouped CV
- Phrases model: 98.4% clip-level, 10-fold grouped CV
- Split windowed replay: 65 correct / 1 wrong / 18 missed of 84
- `python test_pipeline.py --model models/sasl_phrases.joblib` → 12/12, 1 warning
- `python test_pipeline.py --model models/sasl_letters.joblib` → 10/12 (the two
  failures are detection rate, not wrong answers)
