"""
Turn recorded captures into ghost-hand animation clips the tutor can play back.

    python export_ghost_hand.py                      # every sign in data/
    python export_ghost_hand.py --signs "Thank you" "Please"
    python export_ghost_hand.py --out ../Assets/StreamingAssets/GhostHands

Why a ghost hand and not an avatar: the capture files contain 21 hand landmarks
in image space (plus body pose, in files from webcam_capture_full.html). That is
exactly what a 2D skeleton overlay needs and nowhere near enough to drive a
rigged arm convincingly -- so this replays what was actually recorded rather
than inventing the parts that weren't.

WHICH TAKE GETS EXPORTED
A learner watches the demo, so it should be the cleanest rendition of the sign,
not an average of twenty. For each sign this picks the take closest to the
"centre" of that sign's takes -- the one whose features sit nearest the mean --
which is the most typical rendition rather than an outlier. Override per sign
with --take.

OUTPUT
One JSON per sign, resampled to a steady frame rate, smoothed, and normalized
into a unit box so the tutor panel can draw it without knowing anything about
the camera it was filmed on. Files from the full-tracking tool are normalized
against the signer's shoulders instead, which is stabler still.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
from collections import defaultdict

import numpy as np

from sasl_features import (LANDMARK_NAMES, WRIST, assign_roles_to_frames,
                           extract_features, load_captures)

HAND_CONNECTIONS = [
    (0, 1), (1, 2), (2, 3), (3, 4),
    (0, 5), (5, 6), (6, 7), (7, 8),
    (5, 9), (9, 10), (10, 11), (11, 12),
    (9, 13), (13, 14), (14, 15), (15, 16),
    (13, 17), (17, 18), (18, 19), (19, 20),
    (0, 17),
]


def _resample_clip(frames, fps):
    """Uniform time grid. Captures run 12-25fps unevenly; playback needs steady."""
    times = np.array([f.t for f in frames], dtype=np.float64)
    dur = float(times[-1] - times[0])
    if dur <= 0:
        return None, 0.0
    n = max(2, int(round(dur * fps)))
    grid = np.linspace(times[0], times[-1], n)

    out = []
    for g in grid:
        j = int(np.clip(np.searchsorted(times, g) - 1, 0, len(times) - 2))
        t0, t1 = times[j], times[j + 1]
        a = 0.0 if t1 <= t0 else (g - t0) / (t1 - t0)
        row = []
        for attr in ("xyz", "xyz2"):
            p0 = getattr(frames[j], attr, None)
            p1 = getattr(frames[j + 1], attr, None)
            if p0 is None and p1 is None:
                row.append(None)
            elif p0 is None:
                row.append(p1.astype(np.float64))
            elif p1 is None:
                row.append(p0.astype(np.float64))
            else:
                row.append((1 - a) * p0.astype(np.float64) + a * p1.astype(np.float64))
        out.append(row)
    return out, dur


def _smooth(seq, window=5):
    """Moving average over time, per landmark. Webcam landmarks jitter by a few
    pixels every frame; on a slow ghost replay that reads as a shiver."""
    if window < 3 or len(seq) < window:
        return seq
    half = window // 2
    out = []
    for i in range(len(seq)):
        lo, hi = max(0, i - half), min(len(seq), i + half + 1)
        row = []
        for hand_i in range(2):
            vals = [seq[k][hand_i] for k in range(lo, hi) if seq[k][hand_i] is not None]
            row.append(np.mean(vals, axis=0) if vals else None)
        out.append(row)
    return out


def _normalize(seq, pose_ref):
    """Put the clip in a unit box the panel can draw directly.

    With body tracking, the origin is the shoulder midpoint and the unit is
    shoulder width -- so "at the chin" lands in the same place no matter how far
    the signer stood from the camera. Without it, fall back to the bounding box
    of the hands over the whole clip, which is stable within a clip but can't
    tell you where the hand sat relative to the body.
    """
    if pose_ref is not None:
        ox, oy, scale = pose_ref
        mode = "body"
    else:
        pts = np.concatenate([h[:, :2] for row in seq for h in row if h is not None])
        lo, hi = pts.min(axis=0), pts.max(axis=0)
        ox, oy = (lo + hi) / 2.0
        scale = float(max(hi - lo)) or 1.0
        mode = "bbox"

    out = []
    for row in seq:
        frame = []
        for h in row:
            if h is None:
                frame.append(None)
                continue
            q = h.copy()
            q[:, 0] = (q[:, 0] - ox) / scale
            q[:, 1] = (q[:, 1] - oy) / scale
            q[:, 2] = q[:, 2] / scale
            frame.append(np.round(q, 4))
        out.append(frame)
    return out, mode


def _pose_reference(clip_frames_raw):
    """Shoulder midpoint and shoulder width, averaged over the clip, if the
    capture carries body tracking (files from webcam_capture_full.html)."""
    xs, ys, ws = [], [], []
    for f in clip_frames_raw:
        br = f.get("bodyRef")
        if br and br.get("scale", 0) > 1e-4 and br.get("visibility", 1) >= 0.5:
            xs.append(br["originX"]); ys.append(br["originY"]); ws.append(br["scale"])
    if len(ws) < 3:
        return None
    return float(np.mean(xs)), float(np.mean(ys)), float(np.mean(ws))


def pick_representative(takes):
    """The most typical take: the one whose feature vector is nearest the mean.

    A learner is watching this to learn the sign, so an unusual rendition is
    worse than a plain one. Nearest-to-mean beats "first take" (often the
    shakiest) and beats "longest" (often the one where they hesitated).
    """
    scored = []
    for frames_raw, frames in takes:
        f, info = extract_features(frames)
        if f is not None:
            scored.append((f, frames_raw, frames, info))
    if not scored:
        return None
    X = np.stack([s[0] for s in scored])
    mu = X.mean(axis=0)
    sd = X.std(axis=0) + 1e-6
    d = np.linalg.norm((X - mu) / sd, axis=1)
    return scored[int(np.argmin(d))]


def export_sign(label, takes, fps, out_dir, smooth_window):
    best = pick_representative(takes)
    if best is None:
        return None
    _feat, frames_raw, frames, _info = best

    frames = assign_roles_to_frames(frames)
    seq, dur = _resample_clip(frames, fps)
    if seq is None:
        return None
    seq = _smooth(seq, smooth_window)
    seq, mode = _normalize(seq, _pose_reference(frames_raw))

    # A hand that drifts into shot for a few frames is noise, not part of the
    # sign -- shown as a ghost it just flickers. Keep the support hand only when
    # it is there for a real share of the clip.
    present = sum(1 for row in seq if row[1] is not None)
    if present < 0.3 * len(seq):
        seq = [[row[0], None] for row in seq]

    out_frames = []
    for row in seq:
        out_frames.append({
            "dominant": None if row[0] is None else row[0].tolist(),
            "support": None if row[1] is None else row[1].tolist(),
        })

    doc = {
        "sign": label,
        "fps": fps,
        "duration": round(dur, 3),
        "frameCount": len(out_frames),
        "normalization": mode,
        "landmarkOrder": LANDMARK_NAMES,
        "connections": [list(c) for c in HAND_CONNECTIONS],
        "twoHanded": any(f["support"] is not None for f in out_frames),
        "note": ("Coordinates are normalized: origin at the shoulder midpoint, "
                 "1.0 = shoulder width" if mode == "body" else
                 "Coordinates are normalized to the clip's own hand bounding box; "
                 "recapture with webcam_capture_full.html for body-relative placement"),
        "frames": out_frames,
    }
    os.makedirs(out_dir, exist_ok=True)
    safe = "".join(ch if ch.isalnum() else "_" for ch in label).strip("_")
    path = os.path.join(out_dir, safe + ".json")
    with open(path, "w") as fh:
        json.dump(doc, fh, separators=(",", ":"))
    return path, doc


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--captures", nargs="+", default=["data/*.json"])
    ap.add_argument("--out", default="ghost_hands")
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--smooth", type=int, default=5,
                    help="moving-average window in frames; 0 disables")
    ap.add_argument("--signs", nargs="*", default=None, help="only these labels")
    ap.add_argument("--min-frames", type=int, default=8)
    args = ap.parse_args()

    paths = []
    for pat in args.captures:
        paths += sorted(glob.glob(pat)) or ([pat] if os.path.exists(pat) else [])
    if not paths:
        raise SystemExit(f"No capture files matched {args.captures}")

    by_label = defaultdict(list)
    for path in paths:
        with open(path) as fh:
            raw_clips = json.load(fh)
        parsed = load_captures(path)
        for raw, (label, _take, frames) in zip(raw_clips, parsed):
            if args.signs and label not in args.signs:
                continue
            if sum(1 for f in frames if f.xyz is not None) < args.min_frames:
                continue
            by_label[label].append((raw.get("frames", []), frames))

    if not by_label:
        raise SystemExit("Nothing to export.")

    print(f"{'sign':<20}{'takes':>6}{'frames':>8}{'dur':>7}  normalization")
    made = 0
    for label in sorted(by_label):
        res = export_sign(label, by_label[label], args.fps, args.out, args.smooth)
        if res is None:
            print(f"{label:<20}{len(by_label[label]):>6}   skipped (no usable take)")
            continue
        _path, doc = res
        flag = "  2-handed" if doc["twoHanded"] else ""
        print(f"{label:<20}{len(by_label[label]):>6}{doc['frameCount']:>8}"
              f"{doc['duration']:>6.1f}s  {doc['normalization']}{flag}")
        made += 1

    print(f"\nWrote {made} clip(s) to {os.path.abspath(args.out)}")
    print("Check one in ghost_preview.html before wiring it into Unity, then copy "
          "the folder to\nAssets/StreamingAssets/GhostHands/ and use GhostHandPlayer.cs.")


if __name__ == "__main__":
    main()
