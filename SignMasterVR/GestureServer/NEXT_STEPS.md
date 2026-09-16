# Where this stands and what's next

Last updated 16 September 2026. Branch `ML-Validation`, **uncommitted working
tree** — commit before doing anything else.

## State

| | |
|---|---|
| Signs | 16 phrases: Bye, Can you sign?, Drive, Hello, Help, Home, How are you, I Sign, I am, I am deaf, My pleasure, Nice to meet you, Please, Sorry, Thank you, Toilet |
| Clips | 311, **all from one signer** |
| Accuracy | 98.4%, 10-fold clip-grouped CV |
| Model | logistic regression on 48 PCA components, 600 KB |
| Verification | `test_pipeline.py` — 12/12 pass, 1 standing warning |
| Live webcam | confirmed working via `live_demo.py` |
| Headset end-to-end | **never run** |

Full detail in `ML_MODEL.md`.

## Open decisions (discussed, not yet implemented)

**1. Confusable pairs need `targetConfidence`.** Live testing showed the model
sometimes picks the wrong one of a confusable pair — Bye/Hello and I am/How are
you are the only two, per the confusion matrix.

Knowing the lesson target does **not** currently help. `server.py` receives
`{"type":"target",...}` but `SASLGestureClassifier` ignores it and reports argmax
over all 16 signs; `LessonManager` then does a string comparison. So a learner who
signs the target correctly can still be marked wrong when the model's top two are
close.

The lesson is asking a binary question — "did they sign the target?" — and the
classifier already computes exactly that, `P(target)`, then discards it by
reporting the argmax.

Proposed: keep `gestureId` as the best guess (so "you signed Sorry instead of
Please" stays possible) and **add a `targetConfidence` field** to the result
message. `LessonManager` compares that against `requiredConfidence`. With target
"How are you" at P=0.45 and "I am" at P=0.42, reporting 0.45 against a 0.45 bar
passes honestly — 45% across 16 classes is 7x chance, and all three gates still
apply.

Rejected: biasing the classifier toward the target. That makes the tutor a rubber
stamp, and fails worst on exactly the pairs that are confusable.

Touches: `sasl_classifier.py`, `server.py`, `protocol.py` docstring,
`NetworkGestureRecognizer.cs` (`WireMessage`), `LessonManager.cs`.

**2. No GestureData assets for the 16 signs.** `Assets/Data/Gestures/` holds A-Z
only, so no level can contain these signs and no target message will ever name
them. Needs 16 `GestureData` assets at `requiredConfidence` **0.45** (not the 0.75
default — see CLAUDE.md) plus a `LevelData` sequencing them.

## Ranked next steps

1. **Commit the working tree.** Nothing is in git yet.
2. **Create the 16 GestureData assets + a Level.** This is what unblocks an
   end-to-end headset test. Nothing else can proceed past it.
3. **Implement `targetConfidence`** (decision 1 above).
4. **Get 2-3 other people to record 8 takes each of the 16 signs.** Every clip is
   currently one person, so cross-signer accuracy is unmeasured — and for a tutor
   that judges other people's signing, it is *the* number. The learning curve went
   flat at 8 takes, which is the evidence that signers, not takes, are the
   bottleneck.
5. **Record 20-30 real "not a sign" clips** — fidgeting, adjusting glasses,
   half-finished attempts. The confidence gate is the weakest component: two of
   five synthetic negatives get through at 16 signs, and sweeping the gate's
   embedding from 8 to 48 dimensions never separated the drifting-hand case, so it
   is not a tuning problem. Real negatives are the only fix.
6. **Re-capture A-Z as clips.** 89 of the 105 alphabet captures were single-frame
   snapshots, so all 26 letters were dropped from the model. Expect letters to be
   harder than phrases — they differ by finger configuration, and M/N/S/T are
   close from a webcam's angle. Use the capture tool's separability report after
   the first handful rather than grinding through all 26.
7. **Compile and test `GhostHandPlayer.cs`.** Never opened in Unity.

## Things that are easy to get wrong

Listed in `CLAUDE.md` under Invariants. The three that have actually bitten:
per-frame chirality, `max_hands=1`, and assuming training finished when it was
interrupted.
