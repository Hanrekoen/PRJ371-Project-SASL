"""
Does splitting into a letters model and a phrases model beat one 42-sign model?

    python compare_split.py

Judged on the WINDOWED REPLAY, not clip accuracy. Clip accuracy asks "given a
neatly trimmed recording of a sign, what is it?" — which is not what a learner
experiences. The replay asks "given a rolling window over someone signing
continuously, does the right sign fire, and does a wrong one ever fire?" That
second question is the one a tutor lives or dies on, and the two numbers move
independently: adding the alphabet cost almost nothing on the first and a lot on
the second.

Both arms see identical clips, identical replay timing, and the per-model
thresholds each model calibrated for itself.
"""
from __future__ import annotations

import argparse
import collections

import joblib
import numpy as np

from sasl_classifier import SASLGestureClassifier
from test_pipeline import FakeResult, load_clips, sample_per_label


def replay(classifier, label, frames, reps=8):
    """Play a clip through the live path. Returns the first sign that fires."""
    classifier.reset()
    dts = np.diff([f.t for f in frames])
    dt = float(np.median(dts)) if len(dts) else 0.05
    t, fired = 0.0, None
    for _rep in range(reps):
        for f in frames:
            t += dt
            gid, _conf = classifier.classify(None, FakeResult.of(f), t=t)
            if gid != "NONE" and fired is None:
                fired = gid
    return fired


def score(pick_classifier, clips):
    ok = wrong = missed = 0
    per_kind = collections.defaultdict(lambda: [0, 0, 0])
    mistakes = []
    for label, _take, frames in clips:
        clf = pick_classifier(label)
        if clf is None:
            continue
        got = replay(clf, label, frames)
        kind = "letter" if (len(label) == 1 and label.isalpha()) else "phrase"
        if got is None:
            missed += 1
            per_kind[kind][2] += 1
        elif got == label:
            ok += 1
            per_kind[kind][0] += 1
        else:
            wrong += 1
            per_kind[kind][1] += 1
            mistakes.append((label, got))
    return ok, wrong, missed, per_kind, mistakes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--combined", default="models/_all42.joblib")
    ap.add_argument("--letters", default="models/_letters.joblib")
    ap.add_argument("--phrases", default="models/_phrases.joblib")
    ap.add_argument("--per-label", type=int, default=2)
    args = ap.parse_args()

    combined = SASLGestureClassifier(args.combined, verbose=False)
    letters = SASLGestureClassifier(args.letters, verbose=False)
    phrases = SASLGestureClassifier(args.phrases, verbose=False)

    letter_labels = set(letters.labels)
    phrase_labels = set(phrases.labels)
    known = set(combined.labels) | letter_labels | phrase_labels

    clips = sample_per_label([c for c in load_clips() if c[0] in known], args.per_label)
    print(f"{len(clips)} clips across {len(set(c[0] for c in clips))} signs\n")
    print(f"  one 42-sign model : confidence bar {combined.model.min_confidence:.2f}, "
          f"liveness floor {combined.model.min_motion_energy:.2f}")
    print(f"  letters model     : confidence bar {letters.model.min_confidence:.2f}, "
          f"liveness floor {letters.model.min_motion_energy:.2f}")
    print(f"  phrases model     : confidence bar {phrases.model.min_confidence:.2f}, "
          f"liveness floor {phrases.model.min_motion_energy:.2f}\n")

    arms = {
        "one 42-sign model": lambda l: combined,
        # The lesson always knows which level it is on, and `level` is already
        # carried on the wire in the target message -- unused today.
        "split by level": lambda l: (letters if l in letter_labels else
                                     phrases if l in phrase_labels else None),
    }

    results = {}
    for name, pick in arms.items():
        ok, wrong, missed, per_kind, mistakes = score(pick, clips)
        results[name] = (ok, wrong, missed, per_kind, mistakes)
        print(f"{name:<20} {ok:>3} correct  {wrong:>2} WRONG  {missed:>2} missed"
              f"   ({ok / len(clips):.0%} of attempts recognised)")
        for kind in ("letter", "phrase"):
            o, w, m = per_kind[kind]
            if o + w + m:
                print(f"    {kind + 's':<9} {o:>3} correct  {w:>2} wrong  {m:>2} missed")

    print()
    for name, (_o, _w, _m, _pk, mistakes) in results.items():
        if mistakes:
            print(f"{name} named the wrong sign on: "
                  + ", ".join(f"{a}->{b}" for a, b in mistakes))

    a = results["one 42-sign model"]
    b = results["split by level"]
    print("\nVERDICT")
    if b[1] < a[1] and b[0] >= a[0]:
        print("  Split wins: fewer wrong fires and no loss of detection.")
    elif b[1] < a[1]:
        print(f"  Split trades detection for safety: {a[1]}->{b[1]} wrong fires, "
              f"{a[0]}->{b[0]} correct. Worth it for a tutor -- a confident wrong "
              f"answer costs a learner more than a 'try again'.")
    elif b[1] == a[1] and b[0] > a[0]:
        print("  Split wins on detection at equal safety.")
    else:
        print("  Split does not beat the single model on this data. Leave it alone "
              "and say so.")


if __name__ == "__main__":
    main()
