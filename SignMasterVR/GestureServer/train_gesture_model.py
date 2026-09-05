"""
Train the SASL sign validator from webcam capture clips.

    python train_gesture_model.py --captures data/captures_hello_bye.json

What it does, in order:
  1. Loads clips and reports a CONFOUND AUDIT -- with 40 clips it is very easy
     to score 100% on something that isn't the sign at all, so the script
     actively tries to cheat and tells you how well cheating works.
  2. Augments each clip (random temporal crops, rotation, noise, speed) inside
     the training fold only. The crops matter: live, the model sees an
     arbitrary 2.9s window of a learner mid-attempt, never a neatly trimmed
     clip, so it has to be trained on ragged windows too.
  3. Selects a model by leave-one-clip-out cross-validation. Augmented copies
     never cross the fold boundary, so the score is honest.
  4. Fits the confidence gate and saves everything to a .joblib bundle.

Adding a new sign means recording clips with the same browser capture tool,
dropping them in the same JSON (or passing several files), and re-running this.
No code changes -- the label set comes from the data.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter

import numpy as np
from sklearn.decomposition import PCA
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import classification_report, confusion_matrix
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.svm import SVC

from sasl_features import (WINDOW_SECONDS, HandFrame, extract_features,  # noqa
                           load_captures)
from sasl_model import GestureModel, NoveltyGate

RNG = np.random.default_rng(20260905)


# --------------------------------------------------------------------------
# Augmentation (landmark level, applied to training folds only)
# --------------------------------------------------------------------------

def augment_clip(frames: list[HandFrame], rng) -> list[HandFrame]:
    """One randomly perturbed variant of a clip.

    Every transform here corresponds to something that genuinely varies between
    a training capture and a learner in front of a webcam: where they stand,
    how far away, how fast they sign, camera tilt, and -- most importantly --
    that a live window starts and ends wherever it happens to.
    """
    n = len(frames)

    # Ragged temporal crop: keep 70-100%, starting anywhere in the first 25%.
    keep = rng.uniform(0.70, 1.0)
    start = int(rng.uniform(0, 0.25) * n)
    end = min(n, start + max(6, int(keep * n)))
    seg = frames[start:end]
    if len(seg) < 6:
        seg = frames

    t0 = seg[0].t
    speed = rng.uniform(0.85, 1.18)          # signed faster / slower
    theta = np.deg2rad(rng.uniform(-10, 10))  # camera tilt / body lean
    c, s = np.cos(theta), np.sin(theta)
    zoom = rng.uniform(0.85, 1.18)            # distance from camera
    shift = rng.normal(0, 0.03, size=2)       # standing position in frame
    noise = rng.uniform(0.0015, 0.006)        # landmark jitter

    out = []
    for f in seg:
        t = (f.t - t0) / speed
        if f.xyz is None:
            out.append(HandFrame(t, None))
            continue
        p = f.xyz.copy()
        centre = p[0, :2].copy()
        rel = p[:, :2] - centre
        rot = np.stack([rel[:, 0] * c - rel[:, 1] * s,
                        rel[:, 0] * s + rel[:, 1] * c], axis=1)
        p[:, :2] = centre + shift + rot * zoom
        p[:, 2] *= zoom
        p += rng.normal(0, noise, size=p.shape).astype(np.float32)
        out.append(HandFrame(t, p.astype(np.float32)))
    return out


def featurize(clips, n_aug: int = 0, rng=RNG):
    """clips -> (X, y, groups). group id == source clip, so augmented copies
    stay together when folds are drawn."""
    X, y, groups, skipped = [], [], [], 0
    for gi, (label, _take, frames) in enumerate(clips):
        variants = [frames] + [augment_clip(frames, rng) for _ in range(n_aug)]
        for v in variants:
            feats, info = extract_features(v)
            if feats is None:
                skipped += 1
                continue
            X.append(feats)
            y.append(label)
            groups.append(gi)
    if skipped:
        print(f"  ({skipped} windows skipped: no usable hand)")
    return np.asarray(X), np.asarray(y), np.asarray(groups)


# --------------------------------------------------------------------------
# Confound audit
# --------------------------------------------------------------------------

def confound_audit(clips):
    """Try to predict the label from things that are NOT the sign.

    If a shortcut scores near-perfectly, any model trained on raw coordinates is
    almost certainly using it rather than learning the gesture.
    """
    print("\n" + "=" * 70)
    print("CONFOUND AUDIT -- can the label be guessed without the sign?")
    print("=" * 70)

    import sasl_features as sf

    rows = []
    for label, take, frames in clips:
        xs, ys, chir = [], [], []
        for f in frames:
            if f.xyz is None:
                continue
            xs.append(float(f.xyz[0, 0]))
            ys.append(float(f.xyz[0, 1]))
            chir.append(sf.palm_chirality(f.xyz))
        if xs:
            rows.append((label, np.mean(xs), np.mean(ys), np.sign(np.mean(chir))))

    labels = sorted({r[0] for r in rows})
    for name, col in [("hand used (chirality)", 3), ("mean wrist x in frame", 1)]:
        by = {l: [r[col] for r in rows if r[0] == l] for l in labels}
        # A single-threshold split is the crudest possible cheat.
        vals = np.array([r[col] for r in rows])
        ytrue = np.array([labels.index(r[0]) for r in rows])
        best = 0.0
        for thr in np.unique(vals):
            for flip in (0, 1):
                pred = ((vals >= thr).astype(int) ^ flip)
                best = max(best, float((pred == ytrue).mean()))
        summary = ", ".join(f"{l}: {np.mean(v):+.3f}" for l, v in by.items())
        verdict = "  <-- LEAK" if best > 0.9 else ""
        print(f"  {name:<24} {summary:<38} best single-split accuracy {best:.0%}{verdict}")

    print("\n  In this capture set every Hello is a LEFT hand on the left of frame")
    print("  and every Bye is a RIGHT hand on the right of frame, so both shortcuts")
    print("  separate the classes perfectly. sasl_features.canonicalize() mirrors")
    print("  every hand to one chirality and uses horizontal position only relative")
    print("  to the clip's own mean, which removes both. The accuracy below is")
    print("  therefore about the GESTURE, not about which hand was raised.")


# --------------------------------------------------------------------------
# Model selection
# --------------------------------------------------------------------------

def candidate_models(n_features: int, n_samples: int):
    max_pca = max(2, min(24, n_samples // 3, n_features))
    grid = []
    for n_comp in sorted({6, 12, max_pca}):
        if n_comp > max_pca:
            continue
        for C in [1.0, 10.0]:
            grid.append((f"logreg(pca={n_comp},C={C})",
                         Pipeline([("sc", StandardScaler()),
                                   ("pca", PCA(n_components=n_comp, random_state=0)),
                                   ("clf", LogisticRegression(C=C, max_iter=5000))])))
            grid.append((f"svm-rbf(pca={n_comp},C={C})",
                         Pipeline([("sc", StandardScaler()),
                                   ("pca", PCA(n_components=n_comp, random_state=0)),
                                   ("clf", SVC(C=C, gamma="scale", probability=True,
                                               random_state=0))])))
    grid.append(("random-forest",
                 Pipeline([("sc", StandardScaler()),
                           ("clf", RandomForestClassifier(n_estimators=400,
                                                          min_samples_leaf=2,
                                                          random_state=0))])))
    return grid


def build_loco_folds(clips, n_aug, rng_seed=0):
    """Pre-compute leave-one-clip-out folds once, so every candidate model is
    scored on identical data (and feature extraction isn't repeated per model).

    Augmentation is applied to the training side only, and every augmented
    variant of the held-out clip is excluded -- otherwise the model would be
    tested on a jittered copy of something it trained on.
    """
    per_clip_aug = []
    for i, clip in enumerate(clips):
        rng = np.random.default_rng(rng_seed * 1000 + i)
        variants = [clip[2]] + [augment_clip(clip[2], rng) for _ in range(n_aug)]
        feats = []
        for v in variants:
            f, _info = extract_features(v)
            if f is not None:
                feats.append(f)
        per_clip_aug.append((clip[0], feats))

    folds = []
    for i in range(len(clips)):
        Xtr, ytr = [], []
        for j, (label, feats) in enumerate(per_clip_aug):
            if j == i:
                continue
            Xtr += feats
            ytr += [label] * len(feats)
        held = per_clip_aug[i]
        if not held[1]:
            continue
        folds.append((np.asarray(Xtr), np.asarray(ytr),
                      held[1][0].reshape(1, -1), held[0]))
    return folds


def leave_one_clip_out(model, folds):
    y_true, y_pred, y_conf = [], [], []
    for Xtr, ytr, Xte, yte in folds:
        m = _clone(model).fit(Xtr, ytr)
        p = m.predict_proba(Xte)[0]
        y_true.append(yte)
        y_pred.append(m.classes_[int(np.argmax(p))])
        y_conf.append(float(np.max(p)))
    return np.array(y_true), np.array(y_pred), np.array(y_conf)


def _clone(model):
    from sklearn.base import clone
    return clone(model)


# --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--captures", nargs="+", default=["data/captures_hello_bye.json"],
                    help="one or more capture JSON files")
    ap.add_argument("--out", default="models/sasl_gesture_model.joblib")
    ap.add_argument("--n-aug", type=int, default=12,
                    help="augmented variants per clip (training folds only)")
    ap.add_argument("--min-confidence", type=float, default=None,
                    help="fixed confidence bar; default derives one from the data")
    ap.add_argument("--target-acceptance", type=float, default=0.90,
                    help="share of genuine clips a single window should accept")
    ap.add_argument("--novelty-percentile", type=float, default=97.0,
                    help="how far outside the real-clip distribution still counts "
                         "as a known sign; lower = stricter")
    ap.add_argument("--skip-audit", action="store_true")
    args = ap.parse_args()

    clips = []
    for path in args.captures:
        loaded = load_captures(path)
        print(f"Loaded {len(loaded)} clips from {path}")
        clips += loaded
    if not clips:
        sys.exit("No clips loaded.")

    counts = Counter(c[0] for c in clips)
    print("Clips per sign:", dict(counts))
    if min(counts.values()) < 8:
        print("  WARNING: fewer than 8 clips for some signs -- expect a fragile model.")

    if not args.skip_audit:
        confound_audit(clips)

    print("\n" + "=" * 70)
    print("MODEL SELECTION -- leave-one-clip-out cross-validation")
    print("=" * 70)

    X0, y0, _ = featurize(clips, n_aug=0)
    print(f"Feature vector: {X0.shape[1]} dims, {X0.shape[0]} clips")

    folds = build_loco_folds(clips, args.n_aug)
    print(f"Built {len(folds)} leave-one-clip-out folds "
          f"({len(folds[0][0])} training windows each)")

    results = []
    for name, model in candidate_models(X0.shape[1], len(clips)):
        yt, yp, yc = leave_one_clip_out(model, folds)
        acc = float((yt == yp).mean())
        results.append((acc, float(np.mean(yc)), name, model, yt, yp))
        print(f"  {name:<28} accuracy {acc:6.1%}   mean confidence {np.mean(yc):.2f}")

    results.sort(key=lambda r: (-r[0], -r[1]))
    best_acc, _, best_name, best_model, yt, yp = results[0]
    print(f"\nBest: {best_name}  ({best_acc:.1%} leave-one-clip-out)")
    print("\n" + classification_report(yt, yp, digits=3))
    labels_sorted = sorted(set(yt))
    print("Confusion matrix (rows = true, cols = predicted):")
    print("            " + "".join(f"{l:>10}" for l in labels_sorted))
    for row, l in zip(confusion_matrix(yt, yp, labels=labels_sorted), labels_sorted):
        print(f"  {l:<10}" + "".join(f"{v:>10}" for v in row))

    # --- final fit on everything -------------------------------------------
    print("\n" + "=" * 70)
    print("FINAL FIT + CONFIDENCE GATE")
    print("=" * 70)
    Xa, ya, ga = featurize(clips, n_aug=args.n_aug, rng=np.random.default_rng(7))
    print(f"Training on {len(Xa)} windows ({len(clips)} clips x {args.n_aug + 1} variants)")

    # Probability calibration. Raw scores on near-separable classes come out at
    # 0.9999, which would make the confidence threshold decorative -- everything
    # clears it. Sigmoid calibration on OUT-OF-FOLD predictions, with all
    # augmented copies of a clip kept in the same fold, gives probabilities that
    # actually mean something for a "not sure, try again" response.
    from sklearn.calibration import CalibratedClassifierCV
    from sklearn.model_selection import StratifiedGroupKFold
    splits = list(StratifiedGroupKFold(n_splits=5, shuffle=True, random_state=0)
                  .split(Xa, ya, groups=ga))
    pipeline = CalibratedClassifierCV(_clone(best_model), method="sigmoid",
                                      cv=splits).fit(Xa, ya)
    raw_conf = _clone(best_model).fit(Xa, ya).predict_proba(X0).max(axis=1)
    cal_conf = pipeline.predict_proba(X0).max(axis=1)
    print(f"Confidence on real clips: raw median {np.median(raw_conf):.3f} "
          f"-> calibrated median {np.median(cal_conf):.3f} "
          f"(min {cal_conf.min():.3f})")

    # --- confidence gate ----------------------------------------------------
    # The gate's covariance is estimated on the augmented set (360 windows give
    # a far better estimate than 40), but its THRESHOLD comes from the real
    # clips only. Augmented variants are deliberately distorted, so calibrating
    # the cutoff on them would set the bar wide enough to wave anything through.
    embedder = Pipeline([("sc", StandardScaler()),
                         ("pca", PCA(n_components=min(16, Xa.shape[0], Xa.shape[1]),
                                     random_state=0))]).fit(Xa)
    gate = NoveltyGate(threshold_percentile=args.novelty_percentile)
    gate.fit(embedder.transform(Xa), ya)
    genuine_d = np.array([gate.distance(z) for z in embedder.transform(X0)])
    gate.threshold_ = float(np.percentile(genuine_d, args.novelty_percentile))
    print(f"Novelty threshold: {gate.threshold_:.2f} "
          f"({args.novelty_percentile:.0f}th percentile over {len(X0)} real clips; "
          f"median genuine distance {np.median(genuine_d):.2f})")

    # Liveness floor, likewise measured on real clips: comfortably below the
    # quietest genuine attempt, so a resting or slowly drifting hand never
    # reaches the classifier.
    energies = np.array([extract_features(c[2])[1]["motion_energy"] for c in clips])
    min_energy = float(0.6 * np.percentile(energies, 5))
    print(f"Liveness floor: motion energy >= {min_energy:.2f} "
          f"(quietest real clip {energies.min():.2f})")

    # Confidence bar. Rather than a magic 0.75, it is set so that the two gates
    # together accept the target share of genuine clips -- an explicit trade
    # between "try again" on a real attempt and a confident wrong answer. A live
    # session is more forgiving than this number suggests: the validator scores
    # ~7 overlapping windows a second and needs only a few to clear the bar.
    if args.min_confidence is not None:
        min_conf = args.min_confidence
    else:
        passes_novelty = genuine_d <= gate.threshold_
        share = float(passes_novelty.mean())
        if share <= args.target_acceptance:
            min_conf = 0.5
        else:
            q = 100.0 * (1.0 - args.target_acceptance / share)
            min_conf = float(np.clip(np.percentile(cal_conf[passes_novelty], q),
                                     0.5, 0.95))
    accepted = ((genuine_d <= gate.threshold_) & (cal_conf >= min_conf)).mean()
    print(f"Confidence bar: {min_conf:.2f} -> both gates accept "
          f"{accepted:.0%} of real clips in a single window")

    from sasl_features import FEATURE_NAMES
    model = GestureModel(
        pipeline=pipeline,
        embedder=embedder,
        gate=gate,
        labels=[str(c) for c in pipeline.classes_],
        feature_names=FEATURE_NAMES,
        min_confidence=min_conf,
        min_motion_energy=min_energy,
        window_seconds=WINDOW_SECONDS,
        metadata={
            "selected_model": best_name,
            "loco_accuracy": best_acc,
            "loco_accuracy_range": [min(r[0] for r in results),
                                    max(r[0] for r in results)],
            "novelty_threshold": gate.threshold_,
            "min_motion_energy": min_energy,
            "min_confidence": min_conf,
            "single_window_acceptance": float(accepted),
            "n_clips": len(clips),
            "clips_per_label": dict(counts),
            "n_augment": args.n_aug,
            "sources": args.captures,
            "input_spec": "MediaPipe Hands 21 landmarks, absolute image-normalized "
                          "xyz, canonicalized in sasl_features.canonicalize()",
        },
    )

    import os
    import joblib
    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    joblib.dump(model, args.out)
    print(f"\nSaved -> {args.out}")
    print(json.dumps(model.metadata, indent=2))


if __name__ == "__main__":
    main()
