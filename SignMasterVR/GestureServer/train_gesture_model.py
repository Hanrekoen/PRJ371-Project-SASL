"""
Train the SASL sign validator from webcam capture clips.

    python train_gesture_model.py                       # every file in data/
    python train_gesture_model.py --captures data/hello_bye.json data/letters.json

What it does, in order:
  1. Loads clips and reports a CONFOUND AUDIT -- with 40 clips it is very easy
     to score 100% on something that isn't the sign at all, so the script
     actively tries to cheat and tells you how well cheating works.
  2. Augments each clip (random temporal crops, rotation, noise, speed) inside
     the training fold only. The crops matter: live, the model sees an
     arbitrary 2.9s window of a learner mid-attempt, never a neatly trimmed
     clip, so it has to be trained on ragged windows too.
  3. Selects a model by clip-grouped cross-validation -- leave-one-clip-out
     on a small set, 10 folds once there are more than 60 clips. Augmented
     copies never cross the fold boundary, so the score is honest.
  4. Fits the confidence gate and saves everything to a .joblib bundle.

ADDING SIGNS OR MORE DATA: export from webcam_capture.html, drop the file in
data/, re-run this, then run test_pipeline.py. No code changes -- the label set
and the number of classes both come from the `label` fields in the data, and
every capture file in data/ is merged. Restart server.py to pick up the new
model.
"""
from __future__ import annotations

import argparse
import glob
import hashlib
import json
import os
import sys
import time
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


_T0 = time.time()


def stage(msg: str):
    """Progress line with elapsed time. Training a few hundred clips takes
    minutes, and the model is only written at the very end -- without these
    it looks hung, and a Ctrl-C means no model gets saved at all."""
    print(f"[{time.time() - _T0:6.0f}s] {msg}", flush=True)


def clip_fingerprint(label, frames) -> str:
    """Identity of a capture: its label plus the actual landmark stream.

    Two exports of the same take collide here regardless of which file they
    came from or what take number the tool gave them.
    """
    h = hashlib.sha1(str(label).encode())
    for f in frames:
        if f.xyz is None:
            h.update(b"-")
            continue
        h.update(np.round(f.xyz[:, :2], 5).tobytes())
    return h.hexdigest()


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
    leaks = []
    for name, col in [("hand used (chirality)", 3), ("mean wrist x in frame", 1),
                      ("mean wrist y in frame", 2)]:
        by = {l: [r[col] for r in rows if r[0] == l] for l in labels}
        vals = np.array([r[col] for r in rows])
        ytrue = np.array([labels.index(r[0]) for r in rows])
        # The crudest possible cheat: one threshold on one number. With more
        # than two signs a single split can't separate them all, so the score
        # is capped -- read it as "how much of the label this one number gives
        # away", not as an accuracy to compare against the model's.
        best = 0.0
        for thr in np.unique(vals):
            for flip in (0, 1):
                pred = ((vals >= thr).astype(int) ^ flip)
                best = max(best, float((pred == ytrue).mean()))
        baseline = max(np.bincount(ytrue)) / len(ytrue)   # always-guess-commonest
        summary = ", ".join(f"{l}: {np.mean(v):+.3f}" for l, v in by.items())
        leaking = best > 0.9 and best > baseline + 0.15
        if leaking:
            leaks.append(name)
        print(f"  {name:<24} {summary[:44]:<46} best single-split {best:.0%} "
              f"(chance {baseline:.0%}){'  <-- LEAK' if leaking else ''}")

    if leaks:
        print(f"\n  {' and '.join(leaks)} nearly gives the label away on its own.")
        print("  That means the captures for different signs differ in something that")
        print("  ISN'T the sign -- which hand was used, or where the signer stood.")
        print("  sasl_features.canonicalize() mirrors every hand to one chirality and")
        print("  uses horizontal position only relative to the window's own mean, so")
        print("  the model can't use those two. But the honest fix is in the capture:")
        print("  sign each label with BOTH hands and from different positions in frame.")
    else:
        print("\n  No single-number shortcut separates these signs. Good -- the accuracy")
        print("  below is about the gesture.")


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


MAX_LOCO_CLIPS = 60   # above this, leave-one-clip-out costs more than it's worth


def build_folds(clips, n_aug, rng_seed=0, max_loco=MAX_LOCO_CLIPS):
    """Pre-compute cross-validation folds once, so every candidate model is
    scored on identical data (and feature extraction isn't repeated per model).

    A whole clip is always held out together with all of its augmented copies --
    testing on a jittered copy of a training example would inflate the score.

    Up to `max_loco` clips this is leave-one-clip-out, which wastes nothing on a
    small set. Above that it switches to 10-fold grouped CV: LOCO costs one model
    fit per clip per candidate, so a full A-Z dataset (26 signs x 20 takes) would
    mean ~6,800 fits and hours of waiting for a score that 10 folds estimates
    just as well.
    """
    stage(f"Extracting features from {len(clips)} clips x {n_aug + 1} variants "
          f"({len(clips) * (n_aug + 1)} windows) ...")
    per_clip = []
    for i, clip in enumerate(clips):
        rng = np.random.default_rng(rng_seed * 1000 + i)
        variants = [clip[2]] + [augment_clip(clip[2], rng) for _ in range(n_aug)]
        feats = [f for f, _info in (extract_features(v) for v in variants)
                 if f is not None]
        per_clip.append((clip[0], feats))
        if (i + 1) % 50 == 0:
            stage(f"  ... {i + 1}/{len(clips)} clips")

    usable = [i for i, (_l, f) in enumerate(per_clip) if f]
    labels = np.array([per_clip[i][0] for i in usable])

    if len(usable) <= max_loco:
        scheme = "leave-one-clip-out"
        test_groups = [[i] for i in usable]
    else:
        from sklearn.model_selection import StratifiedGroupKFold
        n_splits = max(2, min(10, min(Counter(labels).values())))
        scheme = f"{n_splits}-fold grouped CV"
        idx = np.arange(len(usable))
        splitter = StratifiedGroupKFold(n_splits=n_splits, shuffle=True, random_state=0)
        test_groups = [[usable[k] for k in te]
                       for _tr, te in splitter.split(idx, labels, groups=idx)]

    folds = []
    for held in test_groups:
        held_set = set(held)
        Xtr, ytr = [], []
        for j, (label, feats) in enumerate(per_clip):
            if j in held_set:
                continue
            Xtr += feats
            ytr += [label] * len(feats)
        # Only the unaugmented clip is ever tested on.
        Xte = np.asarray([per_clip[j][1][0] for j in held])
        yte = np.asarray([per_clip[j][0] for j in held])
        folds.append((np.asarray(Xtr), np.asarray(ytr), Xte, yte))
    return folds, scheme


def cross_validate(model, folds):
    y_true, y_pred, y_conf = [], [], []
    for Xtr, ytr, Xte, yte in folds:
        m = _clone(model).fit(Xtr, ytr)
        proba = m.predict_proba(Xte)
        y_true += list(yte)
        y_pred += list(m.classes_[np.argmax(proba, axis=1)])
        y_conf += list(proba.max(axis=1))
    return np.array(y_true), np.array(y_pred), np.array(y_conf)


def _clone(model):
    from sklearn.base import clone
    return clone(model)


# --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--captures", nargs="+", default=["data/*.json"],
                    help="capture JSON files or glob patterns; defaults to every "
                         "file in data/, so adding a sign is: drop the export in "
                         "data/ and re-run")
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
    ap.add_argument("--min-clips", type=int, default=5,
                    help="signs with fewer usable clips than this are dropped")
    ap.add_argument("--tolerance", type=float, default=0.015,
                    help="accept a simpler model if it is within this much of the "
                         "best score; 0 always takes the top scorer")
    ap.add_argument("--quick", action="store_true",
                    help="score only the fast candidates -- roughly 3x quicker, "
                         "usually within a point of the full sweep")
    ap.add_argument("--skip-audit", action="store_true")
    args = ap.parse_args()

    # Expand globs here rather than relying on the shell: cmd.exe and PowerShell
    # don't expand them, so "--captures data/*.json" arrives as that literal
    # string on Windows -- which is where this actually runs.
    paths = []
    for pattern in args.captures:
        matches = sorted(glob.glob(pattern))
        if matches:
            paths += matches
        elif os.path.exists(pattern):
            paths.append(pattern)
        else:
            print(f"  no file matches {pattern!r}")
    paths = list(dict.fromkeys(paths))          # de-duplicate, keep order
    if not paths:
        sys.exit(f"No capture files found for {args.captures}. Put the exported "
                 f"JSON in data/ and re-run.")

    clips = []
    per_file = {}
    seen = {}
    dupes = Counter()
    too_short = Counter()
    for path in paths:
        loaded = load_captures(path)
        kept = []
        for clip in loaded:
            label, _take, frames = clip

            # The capture tool autosaves cumulatively, so a later export can
            # contain every take from an earlier one. Loading both would put an
            # identical twin of a held-out clip into the training fold and
            # inflate the score -- silently, and by a lot.
            key = clip_fingerprint(label, frames)
            if key in seen:
                dupes[(label, os.path.basename(seen[key]), os.path.basename(path))] += 1
                continue
            seen[key] = path

            # Snapshots are a single frozen frame. This model reads motion over
            # a window, so one frame is nothing it can use.
            if sum(1 for f in frames if f.xyz is not None) < 4:
                too_short[label] += 1
                continue

            kept.append(clip)

        per_file[path] = Counter(c[0] for c in kept)
        skipped = len(loaded) - len(kept)
        note = f"  ({skipped} skipped)" if skipped else ""
        print(f"Loaded {len(kept):3d} clips from {path}{note}")
        clips += kept

    if dupes:
        print("\n  DUPLICATES -- identical takes found in more than one file:")
        for (label, a, b), n in dupes.most_common():
            print(f"    {n:3d} x {label!r} in both {a} and {b} -- kept the first")
        print("    (the capture tool exports cumulatively; this is normal, and\n"
              "     loading both copies would have inflated the accuracy below)")

    if too_short:
        total = sum(too_short.values())
        print(f"\n  {total} captures skipped as too short (single-frame snapshots):")
        print("    " + ", ".join(f"{l} x{n}" for l, n in too_short.most_common(8))
              + (" ..." if len(too_short) > 8 else ""))
        print("    This model reads ~3s of motion, so snapshots carry no signal.\n"
              "    Re-capture those labels with C (clip) rather than Space.")

    if not clips:
        sys.exit("Capture files contained no usable clips.")

    # A class with almost no examples can't be learned and breaks fold splitting.
    counts_raw = Counter(c[0] for c in clips)
    dropped = {l: n for l, n in counts_raw.items() if n < args.min_clips}
    if dropped:
        print(f"\n  DROPPED {len(dropped)} signs with fewer than {args.min_clips} "
              f"usable clips -- not enough to learn or to cross-validate:")
        print("    " + ", ".join(f"{l} ({n})" for l, n in sorted(dropped.items())))
        print("    Capture more takes of these, then re-run. Lower the bar with "
              "--min-clips if you really want them in.")
        clips = [c for c in clips if c[0] not in dropped]
        for f in per_file:
            for l in dropped:
                per_file[f].pop(l, None)
    if len({c[0] for c in clips}) < 2:
        sys.exit("Need at least 2 signs with enough clips to train.")

    counts = Counter(c[0] for c in clips)
    print(f"\n{len(clips)} clips, {len(counts)} signs:")
    for label, n in sorted(counts.items()):
        sources = sum(1 for c in per_file.values() if label in c)
        flag = ""
        if n < 8:
            flag = "  <-- too few, expect a fragile model"
        elif n < 15:
            flag = "  <-- thin, aim for 15+"
        # A sign that only ever appears in one capture file is a sign only one
        # person (or one sitting) ever made. The model can learn that session's
        # lighting, distance and camera angle instead of the sign itself.
        elif len(paths) > 1 and sources == 1:
            flag = "  <-- only in one capture file"
        print(f"  {label:<12} {n:3d}{flag}")

    if len(paths) == 1 and len(counts) > 1:
        print("\n  All clips come from one capture file. Cross-signer accuracy is the\n"
              "  number that matters for a tutor, and it can't be measured from a\n"
              "  single session -- see ML_MODEL.md.")

    if not args.skip_audit:
        confound_audit(clips)

    print("\n" + "=" * 70)
    print("MODEL SELECTION -- clip-grouped cross-validation")
    print("=" * 70)

    X0, y0, _ = featurize(clips, n_aug=0)
    print(f"Feature vector: {X0.shape[1]} dims, {X0.shape[0]} clips")

    folds, scheme = build_folds(clips, args.n_aug)
    print(f"Cross-validation: {scheme}, {len(folds)} folds "
          f"({len(folds[0][0])} training windows each)")

    candidates = candidate_models(X0.shape[1], len(clips))
    if args.quick:
        candidates = [c for c in candidates if c[0].startswith("logreg")]
    stage(f"Scoring {len(candidates)} candidate models over {len(folds)} folds "
          f"-- this is the slow part, expect a few minutes")

    results = []
    for name, model in candidates:
        yt, yp, yc = cross_validate(model, folds)
        acc = float((yt == yp).mean())
        results.append((acc, float(np.mean(yc)), name, model, yt, yp))
        print(f"[{time.time() - _T0:6.0f}s]   {name:<28} accuracy {acc:6.1%}   "
              f"mean confidence {np.mean(yc):.2f}", flush=True)

    # Prefer the simplest model that is within a hair of the best. A random
    # forest with calibration pickles to ~60MB and is slower per frame; a
    # logistic regression on PCA components is a few hundred KB. On a few
    # hundred clips a 1-point gap is one or two clips -- inside the noise, and
    # not worth carrying that around in the repo and loading at every startup.
    COST = {"logreg": 0, "svm": 1, "random": 2}
    results.sort(key=lambda r: (-r[0], -r[1]))
    top_acc = results[0][0]
    close = [r for r in results if top_acc - r[0] <= args.tolerance]
    close.sort(key=lambda r: (COST.get(r[2].split("(")[0].split("-")[0], 9), -r[0]))
    best_acc, _, best_name, best_model, yt, yp = close[0]
    if best_name != results[0][2]:
        print(f"\n  {results[0][2]} scored {results[0][0]:.1%}, but {best_name} is "
              f"within {args.tolerance:.1%} at {best_acc:.1%} and is far smaller and\n"
              f"  faster -- taking the simpler one. Override with --tolerance 0.")
    print(f"\nBest: {best_name}  ({best_acc:.1%}, {scheme})")
    print("\n" + classification_report(yt, yp, digits=3))
    labels_sorted = sorted(set(yt))
    print("Confusion matrix (rows = true, cols = predicted):")
    w = max(10, max(len(str(l)) for l in labels_sorted) + 2)
    print(" " * (w + 2) + "".join(f"{l:>{w}}" for l in labels_sorted))
    for row, l in zip(confusion_matrix(yt, yp, labels=labels_sorted), labels_sorted):
        print(f"  {l:<{w}}" + "".join(f"{v:>{w}}" for v in row))

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
        # The floor has to scale with the number of signs. Calibrated
        # probabilities spread across however many classes there are, so a
        # confident 2-class answer sits near 0.95 while an equally confident
        # 16-class answer sits near 0.65. A fixed 0.5 floor is a sensible bar
        # for two signs and an impossible one for sixteen -- it silently
        # rejected 19% of genuine clips before this was relative to chance.
        n_classes = len(np.unique(ya))
        floor = float(min(0.5, max(0.20, 3.0 / n_classes)))
        passes_novelty = genuine_d <= gate.threshold_
        share = float(passes_novelty.mean())
        if share <= args.target_acceptance:
            min_conf = floor
        else:
            q = 100.0 * (1.0 - args.target_acceptance / share)
            min_conf = float(np.clip(np.percentile(cal_conf[passes_novelty], q),
                                     floor, 0.95))
    accepted = ((genuine_d <= gate.threshold_) & (cal_conf >= min_conf)).mean()
    print(f"Confidence bar: {min_conf:.2f} (floor {floor:.2f} for {n_classes} signs) "
          f"-> both gates accept {accepted:.0%} of real clips in a single window")

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
            "cv_scheme": scheme,
            "cv_accuracy": best_acc,
            "cv_accuracy_range": [min(r[0] for r in results),
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

    import joblib
    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    joblib.dump(model, args.out)
    stage(f"Saved -> {os.path.abspath(args.out)}")
    print(json.dumps(model.metadata, indent=2))
    print("\n" + "=" * 70)
    print(f"DONE. {len(model.labels)} signs in the new model:")
    print("  " + ", ".join(model.labels))
    print("\nNext: python test_pipeline.py    then restart live_demo.py / server.py")
    print("(live_demo and server load this file at startup -- they will keep")
    print(" using the old model until you restart them.)")
    print("=" * 70)


if __name__ == "__main__":
    main()
