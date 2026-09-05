"""
Verification suite. Run after any change to features or training:

    python test_pipeline.py

These are not unit tests of arithmetic -- they check the properties the tutor
actually depends on:

  1. The model ignores which hand is used and where in frame it is. Given the
     confound in the capture set (all Hello = left hand, all Bye = right hand)
     this is the test that proves the model learned the gesture.
  2. The confidence gate rejects non-signs instead of forcing them into a class.
  3. Real signs still pass that gate.
  4. Signs survive live-style sliding windows, not just trimmed clips.
  5. The adapter server.py constructs behaves on the same clips, reports "NONE"
     when nothing is recognised, and refuses the old per-frame call signature.
"""
from __future__ import annotations

import sys

import joblib
import numpy as np

from gesture_classifier import GestureValidator
from sasl_features import HandFrame, extract_features, load_captures

MODEL = "models/sasl_gesture_model.joblib"
CAPTURES = "data/captures_hello_bye.json"

PASS, FAIL = "PASS", "FAIL"
results = []


def check(name, ok, detail=""):
    results.append((name, ok))
    print(f"  [{PASS if ok else FAIL}] {name}" + (f" -- {detail}" if detail else ""))


def mirror(frames):
    out = []
    for f in frames:
        if f.xyz is None:
            out.append(HandFrame(f.t, None))
            continue
        p = f.xyz.copy()
        p[:, 0] = 1.0 - p[:, 0]     # mirror the whole image, as a flipped
        out.append(HandFrame(f.t, p))  # webcam or the other hand would look
    return out


def translate(frames, dx, dy):
    out = []
    for f in frames:
        if f.xyz is None:
            out.append(HandFrame(f.t, None))
            continue
        p = f.xyz.copy()
        p[:, 0] += dx
        p[:, 1] += dy
        out.append(HandFrame(f.t, p))
    return out


class FakeResult:
    """Stands in for a mediapipe HandLandmarkerResult."""

    def __init__(self, xyz):
        if xyz is None:
            self.hand_landmarks = []
        else:
            self.hand_landmarks = [[type("L", (), {"x": float(p[0]), "y": float(p[1]),
                                                   "z": float(p[2])})() for p in xyz]]


def main():
    model = joblib.load(MODEL)
    clips = load_captures(CAPTURES)
    rng = np.random.default_rng(11)

    print("\n1. INVARIANCE -- does the model use the gesture, not the shortcut?")
    flips = same = 0
    feat_deltas = []
    for label, take, frames in clips:
        f1, i1 = extract_features(frames)
        f2, i2 = extract_features(mirror(frames))
        p1 = model.predict(f1, i1)
        p2 = model.predict(f2, i2)
        feat_deltas.append(float(np.linalg.norm(f1 - f2) / (np.linalg.norm(f1) + 1e-9)))
        if p1["label"] == p2["label"]:
            same += 1
        else:
            flips += 1
    check("mirroring the video (i.e. swapping the signing hand) does not change "
          "the prediction", flips == 0,
          f"{same}/{len(clips)} unchanged, mean relative feature change "
          f"{np.mean(feat_deltas):.1%}")

    moved = 0
    for label, take, frames in clips:
        dx = 0.25 if float(frames[0].xyz[0, 0]) < 0.5 else -0.25
        f1, i1 = extract_features(frames)
        f2, i2 = extract_features(translate(frames, dx, 0.0))
        if model.predict(f1, i1)["label"] == model.predict(f2, i2)["label"]:
            moved += 1
    check("moving the signer to the other side of frame does not change the "
          "prediction", moved == len(clips), f"{moved}/{len(clips)} unchanged")

    # Vertical position is NOT stripped -- signing height is part of a sign --
    # so this checks tolerance, not invariance. A large vertical shift is a
    # different camera setup and is expected to degrade.
    tilted = 0
    for label, take, frames in clips:
        f1, i1 = extract_features(frames)
        f2, i2 = extract_features(translate(frames, 0.0, -0.05))
        if model.predict(f1, i1)["label"] == model.predict(f2, i2)["label"]:
            tilted += 1
    check("tolerates a small vertical framing change", tilted >= 0.9 * len(clips),
          f"{tilted}/{len(clips)} unchanged (height is a deliberate feature)")

    print("\n2. CONFIDENCE GATE -- are non-signs rejected?")

    def make_frames(fn, n=45, dur=2.9):
        return [HandFrame(i * dur / n, fn(i / n)) for i in range(n)]

    base = clips[0][2][0].xyz.copy()

    # (a) a hand held perfectly still
    still = make_frames(lambda u: base + rng.normal(0, 0.001, base.shape).astype(np.float32))
    # (b) random landmark soup -- not a hand shape at all
    noise = make_frames(lambda u: rng.uniform(0.2, 0.8, base.shape).astype(np.float32))
    # (c) a real hand shape drifting slowly across frame (reaching for a mug)
    drift = make_frames(lambda u: (base + np.array([0.35 * u, 0.1 * u, 0], np.float32)
                                   + rng.normal(0, 0.002, base.shape).astype(np.float32)))
    # (d) a fast erratic scribble -- motion, but not a taught sign
    scribble = make_frames(lambda u: (base + np.array(
        [0.12 * np.sin(u * 41), 0.12 * np.cos(u * 37), 0], np.float32)
        + rng.normal(0, 0.02, base.shape).astype(np.float32)))
    # (e) hand missing from most of the window
    absent = [HandFrame(i * 2.9 / 45, base if i % 7 == 0 else None) for i in range(45)]

    for name, frames in [("a still hand", still), ("random noise", noise),
                         ("a hand drifting across frame", drift),
                         ("an erratic scribble", scribble),
                         ("a mostly-empty window", absent)]:
        f, i = extract_features(frames)
        if f is None:
            check(f"rejects {name}", True, i.get("reason"))
            continue
        out = model.predict(f, i)
        check(f"rejects {name}", not out["accepted"],
              out["reason"] or f"ACCEPTED as {out['label']} ({out['confidence']:.0%})")

    print("\n3. REAL SIGNS still pass the gate")
    accepted = [model.predict(*extract_features(fr)) for _l, _t, fr in clips]
    n_ok = sum(1 for a in accepted if a["accepted"])
    check("genuine clips are accepted", n_ok >= 0.9 * len(clips),
          f"{n_ok}/{len(clips)} accepted "
          f"(mean confidence {np.mean([a['confidence'] for a in accepted]):.2f})")

    correct = sum(1 for a, (l, _t, _f) in zip(accepted, clips) if a["label"] == l)
    check("labels are correct on accepted clips", correct >= 0.9 * len(clips),
          f"{correct}/{len(clips)} correct (in-sample -- see the leave-one-clip-out "
          f"figure from training for the honest number)")

    print("\n4. LIVE SLIDING WINDOW -- replaying clips frame by frame")

    fired_ok = fired_wrong = missed = 0
    for label, _t, frames in clips[:12] + clips[20:32]:
        v = GestureValidator(MODEL, votes_needed=2, evaluate_every=0.05)
        got = None
        # Replay at the clip's own frame timing, repeated until well past the
        # window length -- a learner signs continuously and clips here run
        # 1.9-3.2s, shorter than the 2.9s window on their own.
        dts = np.diff([f.t for f in frames])
        dt = float(np.median(dts)) if len(dts) else 0.05
        t = 0.0
        reps = int(np.ceil(3 * v.window_seconds / max(frames[-1].t, 0.1)))
        for _rep in range(max(2, reps)):
            for f in frames:
                t += dt
                ev = v.update(FakeResult(f.xyz), t)
                if ev and got is None:
                    got = ev.label
        if got is None:
            missed += 1
        elif got == label:
            fired_ok += 1
        else:
            fired_wrong += 1
    total = fired_ok + fired_wrong + missed
    check("sliding window fires the right sign", fired_wrong == 0 and fired_ok >= 0.8 * total,
          f"{fired_ok} correct, {fired_wrong} wrong, {missed} not detected (of {total})")

    print("\n5. SERVER ADAPTER -- the path server.py actually runs")

    from sasl_classifier import SASLGestureClassifier

    adapter = SASLGestureClassifier(MODEL, verbose=False)
    try:
        adapter.classify(None)
        check("old single-argument call fails loudly", False, "it returned instead")
    except TypeError:
        check("old single-argument call fails loudly", True,
              "a temporal model needs the raw result, and says so")

    ok = wrong = miss = 0
    for label, _t, frames in clips:
        a = SASLGestureClassifier(MODEL, verbose=False)
        dt = float(np.median(np.diff([f.t for f in frames])))
        t, got = 0.0, None
        for _rep in range(8):     # a learner signing continuously
            for f in frames:
                t += dt
                gid, _conf = a.classify(None, FakeResult(f.xyz), t=t)
                if gid != "NONE" and got is None:
                    got = gid
        if got is None:
            miss += 1
        elif got == label:
            ok += 1
        else:
            wrong += 1
    check("adapter reports the right gestureId", wrong == 0 and ok >= 0.85 * len(clips),
          f"{ok} correct, {wrong} wrong, {miss} not detected (of {len(clips)})")

    a = SASLGestureClassifier(MODEL, verbose=False)
    t, spoke = 0.0, False
    for _i in range(200):
        t += 0.05
        if a.classify(None, FakeResult(None), t=t)[0] != "NONE":
            spoke = True
    check("empty camera stays quiet", not spoke,
          "reports NONE, which server.py treats as say-nothing")

    print("\n" + "=" * 62)
    n_fail = sum(1 for _n, ok in results if not ok)
    print(f"{len(results) - n_fail}/{len(results)} checks passed")
    return 1 if n_fail else 0


if __name__ == "__main__":
    sys.exit(main())
