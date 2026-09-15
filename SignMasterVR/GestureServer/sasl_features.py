"""
Feature extraction for SASL sign recognition from MediaPipe hand landmarks.

This module is the SINGLE SOURCE OF TRUTH for how a sequence of hand landmarks
becomes a feature vector. Training (train_gesture_model.py) and live inference
(gesture_classifier.py) both import from here, so the model can never be fed a
differently-shaped input at runtime than it saw during training. If you change
anything in here, you must retrain.

--------------------------------------------------------------------------
INPUT SPEC (matches hand_tracker.py, not the old 26-joint OpenXR plan)
--------------------------------------------------------------------------
21 landmarks/hand, MediaPipe Hands order (see LANDMARK_NAMES). We take the
ABSOLUTE image-normalized coordinates (x, y in 0..1, z relative depth) rather
than hand_tracker's pre-normalized `rel` output, because where the hand sits in
frame and how it travels are part of the sign. Normalization happens here.

Two capture sources feed this module and both are handled:
  * browser captures  -> frames_from_capture_clip()      (the training JSON)
  * live HandTracker  -> hand_frame_from_mp_result()     (mediapipe Tasks API)

--------------------------------------------------------------------------
WHY CANONICALIZATION MATTERS (read this before touching the pipeline)
--------------------------------------------------------------------------
In the Hello/Bye capture set, handedness is perfectly confounded with the label:
all 20 Hello clips are a left hand on the left of frame, all 20 Bye clips are a
right hand on the right of frame. A model given raw coordinates scores 100% by
learning "left side of frame = Hello" and then fails the moment a learner signs
both with the same hand -- which is exactly what a tutor's users will do.

So every hand is canonicalized before features are computed:
  1. Chirality is derived GEOMETRICALLY from the palm triangle, not read from
     MediaPipe's handedness label. The label flips depending on whether the
     video feed is mirrored (browser previews usually are, cv2 usually is not),
     so trusting it would silently invert features between capture and runtime.
  2. Left hands are mirrored in x, so a left-handed and a right-handed rendition
     of the same sign produce the same features.
  3. Horizontal position is used only RELATIVE to the clip's own mean, so the
     side of frame the signer stands on carries no signal. Vertical position is
     kept absolute, since signing height (e.g. at the temple vs at the chest) is
     genuinely part of a sign.
Handedness is never a feature.
"""
from __future__ import annotations

import json
from dataclasses import dataclass

import numpy as np

LANDMARK_NAMES = [
    "WRIST",
    "THUMB_CMC", "THUMB_MCP", "THUMB_IP", "THUMB_TIP",
    "INDEX_MCP", "INDEX_PIP", "INDEX_DIP", "INDEX_TIP",
    "MIDDLE_MCP", "MIDDLE_PIP", "MIDDLE_DIP", "MIDDLE_TIP",
    "RING_MCP", "RING_PIP", "RING_DIP", "RING_TIP",
    "PINKY_MCP", "PINKY_PIP", "PINKY_DIP", "PINKY_TIP",
]
IDX = {n: i for i, n in enumerate(LANDMARK_NAMES)}

WRIST = IDX["WRIST"]
FINGER_TIPS = [IDX["THUMB_TIP"], IDX["INDEX_TIP"], IDX["MIDDLE_TIP"],
               IDX["RING_TIP"], IDX["PINKY_TIP"]]
FINGER_MCPS = [IDX["THUMB_MCP"], IDX["INDEX_MCP"], IDX["MIDDLE_MCP"],
               IDX["RING_MCP"], IDX["PINKY_MCP"]]
FINGER_PIPS = [IDX["THUMB_IP"], IDX["INDEX_PIP"], IDX["MIDDLE_PIP"],
               IDX["RING_PIP"], IDX["PINKY_PIP"]]
INDEX_MCP, PINKY_MCP, MIDDLE_MCP = IDX["INDEX_MCP"], IDX["PINKY_MCP"], IDX["MIDDLE_MCP"]

# Number of frames each clip is resampled to on a uniform time grid. Clips in
# the capture set run 15-73 frames at a jittery 12-25fps, so resampling on
# TIMESTAMPS (not frame indices) is what makes them comparable.
N_RESAMPLE = 8

# Feature-extraction window, in seconds. The training clips are ~2.9s long on
# average, and the live sliding window uses this same figure so that velocity
# and oscillation features land in the distribution the model was trained on.
WINDOW_SECONDS = 2.9

# Set this if your capture and runtime cameras have different aspect ratios.
# x and y both arrive normalized to 0..1 against different pixel counts, so on a
# non-square frame one unit of x is not one unit of y. Multiplying x by (w/h)
# makes the geometry isotropic. The training JSON does not record frame
# dimensions, so we default to 1.0 for both sides -- consistent, if not strictly
# metric. Worth recording video dimensions in future captures.
DEFAULT_ASPECT = 1.0


@dataclass
class HandFrame:
    """One frame. `xyz` is the DOMINANT hand, (21, 3) absolute image-normalized.

    `xyz2` is the support hand when a second one is visible. Four of the signs
    in the dataset are genuinely two-handed -- I Sign (two hands in 99% of
    frames), Home (86%), Can you sign? (75%), Nice to meet you (56%) -- and
    following only one of them threw away half the evidence.

    Which hand is "dominant" is decided by motion over the whole clip, not by
    MediaPipe's Left/Right label. See assign_hand_roles().
    """
    t: float                        # seconds
    xyz: np.ndarray | None          # dominant hand, or None if no hand at all
    xyz2: np.ndarray | None = None  # support hand, when present


# --------------------------------------------------------------------------
# Canonicalization
# --------------------------------------------------------------------------

def palm_chirality(xyz: np.ndarray) -> float:
    """+1 for a canonical (right-in-unmirrored-image) hand, -1 for its mirror.

    Uses the signed area of the wrist->index_MCP->pinky_MCP triangle in image
    space. This is a property of the pixels, so it agrees between mirrored and
    unmirrored feeds in the same way MediaPipe's own handedness label does not.
    Verified against the capture set's handedness labels: agrees on 1204/1207
    hand observations.
    """
    v1 = xyz[INDEX_MCP, :2] - xyz[WRIST, :2]
    v2 = xyz[PINKY_MCP, :2] - xyz[WRIST, :2]
    cross = v1[0] * v2[1] - v1[1] * v2[0]
    return 1.0 if cross >= 0 else -1.0


def majority_chirality(hands) -> float:
    """One chirality for a whole window, by majority vote over its frames.

    Deciding this per frame looks reasonable and is a trap. When a hand turns
    edge-on the palm triangle degenerates and the sign flips for a frame or
    two; because canonicalize() negates x on a negative sign, each flip teleports
    the canonical wrist by about one hand-width. That shows up as ~200
    hand-widths/second of phantom speed and a scrambled trajectory. It affected
    96 of 327 clips in the capture set -- every single take of "I am" and
    "Nice to meet you" among them.

    A signer does not change hands mid-sign, so one decision per window is both
    more accurate and more stable.
    """
    votes = [palm_chirality(h) for h in hands if h is not None]
    if not votes:
        return 1.0
    return 1.0 if float(np.mean(votes)) >= 0 else -1.0


def canonicalize(xyz: np.ndarray, aspect: float = DEFAULT_ASPECT,
                 force_chir: float | None = None):
    """Split one hand into (shape, wrist_xy, scale, chirality).

    shape     : (21, 3) wrist-relative, scale-normalized, mirrored to canonical
                chirality. This is the hand's POSE, free of position and size.
    wrist_xy  : (2,) where the hand is in frame (x already mirrored to match).
    scale     : wrist-to-middle-MCP length in image units -- a proxy for how
                close the hand is to the camera.
    chirality : +1 / -1, returned for diagnostics only. NEVER used as a feature.
    """
    p = np.asarray(xyz, dtype=np.float64).copy()
    p[:, 0] *= aspect

    # force_chir keeps one decision for a whole window (see majority_chirality).
    chir = palm_chirality(p) if force_chir is None else float(force_chir)
    if chir < 0:
        # Mirror about the hand's own vertical axis so left and right hands
        # produce identical shape features.
        p[:, 0] = -p[:, 0]

    wrist = p[WRIST].copy()
    rel = p - wrist

    scale = float(np.linalg.norm(rel[MIDDLE_MCP, :2]))
    if scale < 1e-6:
        return None
    rel = rel / scale   # x, y AND z divided by the same scalar, so the pose
                        # stays geometrically consistent in all three axes

    return rel.astype(np.float32), wrist[:2].astype(np.float32), scale, chir


# --------------------------------------------------------------------------
# Two-hand tracking and role assignment
# --------------------------------------------------------------------------

def assign_roles_to_frames(frames):
    """Re-derive (dominant, support) for a list of HandFrames.

    Input order within a frame is treated as arbitrary -- MediaPipe's is.
    """
    per_frame = []
    for f in frames:
        hands = [h for h in (f.xyz, getattr(f, "xyz2", None)) if h is not None]
        per_frame.append(hands)
    if not any(len(h) > 1 for h in per_frame):
        return frames                      # one hand throughout: nothing to decide
    dom, sup = assign_hand_roles(per_frame, len(frames))
    return [HandFrame(f.t, dom[i], sup[i]) for i, f in enumerate(frames)]


def _link_tracks(per_frame_hands):
    """Follow up to two hands across frames.

    MediaPipe does not guarantee a stable order between frames, so hands are
    matched to tracks by proximity to where that track was last seen. Returns
    two lists (one per track) of (frame_index, xyz), either of which may be
    short or empty.
    """
    tracks = [[], []]
    last = [None, None]
    for fi, hands in enumerate(per_frame_hands):
        if not hands:
            continue
        free = list(range(len(hands)))
        # Existing tracks claim their nearest hand first.
        for ti in (0, 1):
            if last[ti] is None or not free:
                continue
            j = min(free, key=lambda k: float(np.linalg.norm(hands[k][WRIST, :2] - last[ti])))
            tracks[ti].append((fi, hands[j]))
            last[ti] = hands[j][WRIST, :2].copy()
            free.remove(j)
        # Anything left starts a new track, if one is free.
        for j in free:
            for ti in (0, 1):
                if last[ti] is None:
                    tracks[ti].append((fi, hands[j]))
                    last[ti] = hands[j][WRIST, :2].copy()
                    break
    return tracks


def _track_activity(track):
    """How much this hand did: wrist path length in hand-widths, plus how much
    its own shape changed. Both are magnitudes, so mirroring the video does not
    change them -- which is what makes the dominant/support split stable."""
    if len(track) < 2:
        return 0.0
    chir = majority_chirality([x for _f, x in track])
    pts, shapes = [], []
    for _fi, xyz in track:
        c = canonicalize(xyz, force_chir=chir)
        if c is None:
            continue
        rel, wrist_xy, scale, _chir = c
        pts.append(wrist_xy / max(scale, 1e-6))
        shapes.append(shape_descriptor(rel))
    if len(pts) < 2:
        return 0.0
    pts = np.stack(pts)
    shapes = np.stack(shapes)
    path = float(np.linalg.norm(np.diff(pts, axis=0), axis=1).sum())
    shape_change = float(np.linalg.norm(np.diff(shapes, axis=0), axis=1).sum())
    return path + shape_change


def assign_hand_roles(per_frame_hands, n_frames):
    """[[xyz, ...] per frame] -> (dominant[], support[]), each length n_frames.

    The dominant hand is the one that MOVES more across the clip. Anatomical
    handedness would be the obvious choice and is the wrong one: MediaPipe's
    Left/Right label flips with mirrored video, so it would silently swap the
    two feature blocks between the capture tool (mirrored preview) and the
    live server (cv2, not mirrored). Activity is a magnitude, so it survives
    mirroring, and for a two-handed sign it also picks out the hand actually
    carrying the movement rather than the one being held as a base.
    """
    tracks = _link_tracks(per_frame_hands)
    act = [_track_activity(t) for t in tracks]
    order = (0, 1) if act[0] >= act[1] else (1, 0)

    # A near-tie means both hands move alike (a symmetric two-handed sign).
    # Fall back to something equally mirror-proof: the higher hand in frame.
    if tracks[0] and tracks[1] and abs(act[0] - act[1]) < 0.05 * max(act[0], act[1], 1e-6):
        ys = [float(np.mean([x[WRIST, 1] for _f, x in t])) if t else np.inf for t in tracks]
        order = (0, 1) if ys[0] <= ys[1] else (1, 0)

    dom = [None] * n_frames
    sup = [None] * n_frames
    for fi, xyz in tracks[order[0]]:
        dom[fi] = xyz
    for fi, xyz in tracks[order[1]]:
        sup[fi] = xyz
    # A frame with only a support hand and no dominant one is better used than
    # dropped -- promote it so the window keeps its detection rate.
    for i in range(n_frames):
        if dom[i] is None and sup[i] is not None:
            dom[i], sup[i] = sup[i], None
    return dom, sup


# --------------------------------------------------------------------------
# Per-frame shape descriptor
# --------------------------------------------------------------------------

SHAPE_FEATURE_NAMES = (
    [f"ext_{n}" for n in ["thumb", "index", "middle", "ring", "pinky"]]
    + [f"curl_{n}" for n in ["thumb", "index", "middle", "ring", "pinky"]]
    + [f"spread_{a}_{b}" for a, b in
       [("thumb", "index"), ("index", "middle"), ("middle", "ring"), ("ring", "pinky")]]
    + ["palm_dir_x", "palm_dir_y"]
    + ["palm_normal_x", "palm_normal_y", "palm_normal_z"]
    + ["thumb_index_tip_dist"]
)
N_SHAPE = len(SHAPE_FEATURE_NAMES)  # 20


def shape_descriptor(rel: np.ndarray) -> np.ndarray:
    """A compact, interpretable pose descriptor from the canonicalized hand.

    Deliberately not the raw 63 coordinates: with 20 clips per sign, 63 highly
    correlated dimensions per frame overfits fast. These 20 quantities are what
    actually distinguishes handshapes -- which fingers are out, how curled they
    are, how spread, and which way the palm faces.
    """
    tips = rel[FINGER_TIPS]
    mcps = rel[FINGER_MCPS]
    pips = rel[FINGER_PIPS]

    # How far each fingertip reaches from the wrist (extended vs folded).
    extension = np.linalg.norm(tips, axis=1)
    # Tip-to-knuckle distance: short means the finger is curled over.
    curl = np.linalg.norm(tips - mcps, axis=1)
    # Gaps between neighbouring fingertips (spread vs closed hand).
    spread = np.linalg.norm(np.diff(tips[:, :2], axis=0), axis=1)

    # In-plane pointing direction of the palm (up / sideways / down).
    palm_dir = rel[MIDDLE_MCP, :2]
    n = np.linalg.norm(palm_dir)
    palm_dir = palm_dir / n if n > 1e-8 else np.zeros(2)

    # Palm facing, in 3D -- separates palm-out (Hello-type waves) from
    # palm-side gestures.
    normal = np.cross(rel[INDEX_MCP], rel[PINKY_MCP])
    n = np.linalg.norm(normal)
    normal = normal / n if n > 1e-8 else np.zeros(3)

    thumb_index = np.linalg.norm(tips[0] - tips[1])

    return np.concatenate([
        extension, curl, spread, palm_dir, normal, [thumb_index]
    ]).astype(np.float32)


# --------------------------------------------------------------------------
# Sequence resampling
# --------------------------------------------------------------------------

def _resample(times: np.ndarray, values: np.ndarray, n: int) -> np.ndarray:
    """Linear interpolation of a (T, D) sequence onto n uniform time points."""
    grid = np.linspace(times[0], times[-1], n)
    return np.stack([np.interp(grid, times, values[:, d])
                     for d in range(values.shape[1])], axis=1)


def _reversals(x: np.ndarray, min_amp: float) -> float:
    """Count direction changes in a 1D trajectory, ignoring jitter smaller than
    min_amp. This is what tells a wave apart from a single sweep."""
    if len(x) < 3:
        return 0.0
    v = np.diff(x)
    count, last_sign, run = 0, 0, 0.0
    for step in v:
        s = 1 if step > 0 else -1
        if s != last_sign and last_sign != 0:
            if abs(run) >= min_amp:
                count += 1
                run = step
            # else: too small to be a real reversal, keep accumulating
        else:
            run += step
        if abs(run) >= min_amp:
            last_sign = s
    return float(count)


def _dominant_freq(x: np.ndarray, duration: float):
    """(frequency in Hz, share of total power) of the strongest oscillation."""
    if len(x) < 4 or duration <= 0:
        return 0.0, 0.0
    x = x - x.mean()
    if np.allclose(x, 0):
        return 0.0, 0.0
    spec = np.abs(np.fft.rfft(x)) ** 2
    spec[0] = 0.0
    total = spec.sum()
    if total <= 0:
        return 0.0, 0.0
    k = int(np.argmax(spec))
    return float(k / duration), float(spec[k] / total)


# --------------------------------------------------------------------------
# Clip -> feature vector
# --------------------------------------------------------------------------

def _build_feature_names() -> list[str]:
    names = []
    for i in range(N_RESAMPLE):
        names += [f"t{i}_{n}" for n in SHAPE_FEATURE_NAMES]
    names += [f"mean_{n}" for n in SHAPE_FEATURE_NAMES]
    names += [f"std_{n}" for n in SHAPE_FEATURE_NAMES]
    for i in range(N_RESAMPLE):
        names += [f"t{i}_wrist_dx", f"t{i}_wrist_y", f"t{i}_scale"]
    names += [
        "path_length", "bbox_w", "bbox_h",
        "speed_mean", "speed_std", "speed_max",
        "reversals_x", "reversals_y",
        "freq_x", "freq_x_power", "freq_y", "freq_y_power",
        "shape_energy", "shape_energy_std",
        "scale_mean", "scale_std",
        "detection_rate",
    ]
    # --- support (second) hand -------------------------------------------
    names += ["sup_presence"]
    names += [f"sup_mean_{n}" for n in SHAPE_FEATURE_NAMES]
    names += [f"sup_std_{n}" for n in SHAPE_FEATURE_NAMES]
    for i in range(N_RESAMPLE):
        names += [f"sup_t{i}_offset_x", f"sup_t{i}_offset_y"]
    names += ["sup_path_length", "sup_speed_mean", "sup_shape_energy", "sup_scale_ratio"]
    return names


N_SUPPORT_FEATURES = 1 + 2 * N_SHAPE + 2 * N_RESAMPLE + 4


FEATURE_NAMES = _build_feature_names()
N_FEATURES = len(FEATURE_NAMES)


def extract_features(frames: list[HandFrame], aspect: float = DEFAULT_ASPECT):
    """Turn one clip (or one live window) into a fixed-length feature vector.

    Returns (features, info). features is None when the clip is unusable --
    too few frames, or the hand missing from most of them. `info` always carries
    diagnostics, which the runtime gate uses to reject non-attempts before the
    classifier ever sees them.
    """
    info = {"n_frames": len(frames), "detection_rate": 0.0, "reason": None}

    # Decide dominant vs support over THIS window. Doing it here rather than at
    # load time means a live sliding window and a recorded clip go through
    # identical code -- the roles can't drift apart between training and runtime.
    frames = assign_roles_to_frames(frames)

    # One chirality decision per hand for the whole window, before any feature
    # is computed. See majority_chirality() for why per-frame is wrong.
    chir_dom = majority_chirality([f.xyz for f in frames])
    chir_sup = majority_chirality([getattr(f, "xyz2", None) for f in frames])

    usable = []
    for f in frames:
        if f.xyz is None:
            continue
        c = canonicalize(f.xyz, aspect=aspect, force_chir=chir_dom)
        if c is None:
            continue
        rel, wrist_xy, scale, chir = c

        # Support hand, expressed in the DOMINANT hand's canonical frame.
        # Applying the dominant hand's own mirror to the support wrist is what
        # keeps the offset mirror-invariant: flip the video and both the raw x
        # and the chirality sign flip, so their product doesn't.
        sup = None
        if getattr(f, "xyz2", None) is not None:
            c2 = canonicalize(f.xyz2, aspect=aspect, force_chir=chir_sup)
            if c2 is not None:
                rel2, _w2, scale2, _chir2 = c2
                sx = float(f.xyz2[WRIST, 0]) * aspect
                if chir < 0:
                    sx = -sx
                sy = float(f.xyz2[WRIST, 1])
                offset = (np.array([sx, sy], dtype=np.float64) - wrist_xy) / max(scale, 1e-6)
                sup = (shape_descriptor(rel2), offset, scale2)

        usable.append((f.t, shape_descriptor(rel), wrist_xy, scale, chir, sup))

    if len(frames) > 0:
        info["detection_rate"] = len(usable) / len(frames)

    if len(usable) < 4:
        info["reason"] = "too few frames with a detected hand"
        return None, info

    times = np.array([u[0] for u in usable], dtype=np.float64)
    duration = float(times[-1] - times[0])
    if duration <= 1e-3:
        info["reason"] = "clip has no duration"
        return None, info
    info["duration"] = duration

    shapes = np.stack([u[1] for u in usable])           # (T, 20)
    wrists = np.stack([u[2] for u in usable])           # (T, 2)
    scales = np.array([u[3] for u in usable])[:, None]  # (T, 1)
    info["chirality"] = float(np.sign(np.mean([u[4] for u in usable])))

    # --- pose over time -------------------------------------------------
    shape_rs = _resample(times, shapes, N_RESAMPLE)     # (8, 20)

    # --- trajectory -----------------------------------------------------
    # x relative to the clip's own mean: removes which side of frame the
    # signer stands on (and, with mirroring, which hand they used) while
    # keeping the horizontal MOTION of the sign. y stays absolute: signing
    # height is meaningful. Both divided by hand size so the signer's
    # distance from the camera doesn't change the numbers.
    mean_scale = float(scales.mean())
    dx = (wrists[:, 0:1] - wrists[:, 0].mean()) / mean_scale
    y = wrists[:, 1:2] / mean_scale
    traj = np.concatenate([dx, y, scales / mean_scale], axis=1)   # (T, 3)
    traj_rs = _resample(times, traj, N_RESAMPLE)

    # --- motion summary -------------------------------------------------
    xy = np.concatenate([dx, y], axis=1)
    steps = np.diff(xy, axis=0)
    dt = np.clip(np.diff(times), 1e-3, None)[:, None]
    speeds = np.linalg.norm(steps, axis=1) / dt[:, 0]   # hand-widths / second
    path_length = float(np.linalg.norm(steps, axis=1).sum())
    bbox_w = float(xy[:, 0].max() - xy[:, 0].min())
    bbox_h = float(xy[:, 1].max() - xy[:, 1].min())

    # Uniformly-sampled copies, so reversal counting and the FFT are not
    # skewed by the camera's irregular frame timing.
    xy_rs = _resample(times, xy, max(N_RESAMPLE * 4, 16))
    rev_x = _reversals(xy_rs[:, 0], min_amp=0.15 * max(bbox_w, 1e-6))
    rev_y = _reversals(xy_rs[:, 1], min_amp=0.15 * max(bbox_h, 1e-6))
    fx, px = _dominant_freq(xy_rs[:, 0], duration)
    fy, py = _dominant_freq(xy_rs[:, 1], duration)

    # How much the HANDSHAPE itself changes (finger flutter with a still wrist
    # looks nothing like a whole-arm wave in this feature).
    shape_rs_fine = _resample(times, shapes, max(N_RESAMPLE * 4, 16))
    shape_steps = np.linalg.norm(np.diff(shape_rs_fine, axis=0), axis=1)
    shape_steps = shape_steps / (duration / len(shape_steps))

    # --- support hand ----------------------------------------------------
    # Zeros plus presence=0 when there is no second hand. The presence flag is
    # what stops "no support hand" being confused with "support hand at the
    # origin holding a neutral shape".
    sup_rows = [(u[0], u[5]) for u in usable if u[5] is not None]
    info["support_presence"] = len(sup_rows) / len(usable)
    if len(sup_rows) >= 3:
        sup_t = np.array([r[0] for r in sup_rows], dtype=np.float64)
        sup_shapes = np.stack([r[1][0] for r in sup_rows])
        sup_off = np.stack([r[1][1] for r in sup_rows])
        sup_scales = np.array([r[1][2] for r in sup_rows])
        if sup_t[-1] - sup_t[0] > 1e-3:
            sup_off_rs = _resample(sup_t, sup_off, N_RESAMPLE)
        else:
            sup_off_rs = np.repeat(sup_off[:1], N_RESAMPLE, axis=0)
        sup_steps = np.linalg.norm(np.diff(sup_off, axis=0), axis=1)
        sup_shape_steps = np.linalg.norm(np.diff(sup_shapes, axis=0), axis=1)
        support_block = np.concatenate([
            [info["support_presence"]],
            sup_shapes.mean(axis=0), sup_shapes.std(axis=0),
            sup_off_rs.ravel(),
            [float(sup_steps.sum()), float(sup_steps.mean()) if len(sup_steps) else 0.0,
             float(sup_shape_steps.mean()) if len(sup_shape_steps) else 0.0,
             float(sup_scales.mean() / max(mean_scale, 1e-6))],
        ])
    else:
        support_block = np.zeros(N_SUPPORT_FEATURES)

    features = np.concatenate([
        shape_rs.ravel(),
        shapes.mean(axis=0),
        shapes.std(axis=0),
        traj_rs.ravel(),
        [path_length, bbox_w, bbox_h,
         speeds.mean(), speeds.std(), speeds.max(),
         rev_x, rev_y,
         fx, px, fy, py,
         shape_steps.mean(), shape_steps.std(),
         mean_scale, float(scales.std()),
         info["detection_rate"]],
        support_block,
    ]).astype(np.float32)

    if not np.all(np.isfinite(features)):
        features = np.nan_to_num(features, nan=0.0, posinf=0.0, neginf=0.0)

    assert features.shape[0] == N_FEATURES, (features.shape, N_FEATURES)
    info["motion_energy"] = float(path_length + shape_steps.mean())
    return features, info


# --------------------------------------------------------------------------
# Source adapters
# --------------------------------------------------------------------------

def frames_from_capture_clip(clip: dict) -> list[HandFrame]:
    """Browser capture JSON -> HandFrames, keeping BOTH hands when present.

    No role decision is made here. extract_features() assigns dominant and
    support over whatever window it is given, so a 3-second live window and a
    recorded clip are treated identically.

    Reads only `landmarks` (absolute image coordinates). Files from the
    full-tracking capture tool carry `pose`, `bodyRef` and `face` too; those are
    ignored by the recogniser and used by the avatar/ghost-hand exporter.
    """
    frames = []
    t0 = clip["frames"][0]["t"] / 1000.0
    for f in clip["frames"]:
        t = f["t"] / 1000.0 - t0
        hands = f.get("hands") or []
        pts = [np.array([[p["x"], p["y"], p["z"]] for p in h["landmarks"]],
                        dtype=np.float32) for h in hands]
        frames.append(HandFrame(t, pts[0] if pts else None,
                                pts[1] if len(pts) > 1 else None))
    return frames


def hand_frame_from_mp_result(result, t: float) -> HandFrame:
    """Live mediapipe Tasks HandLandmarkerResult -> HandFrame.

    Pass the FIRST element returned by HandTracker.process() (the raw result),
    not the second. process()'s `rel` output has already thrown away where the
    hand is in frame, and this pipeline needs that.

    Keeps up to two hands. HandTracker must therefore be constructed with
    max_hands=2 (its default) -- max_hands=1 silently halves what two-handed
    signs look like.
    """
    if not getattr(result, "hand_landmarks", None):
        return HandFrame(t, None)
    pts = [np.array([[lm.x, lm.y, lm.z] for lm in hand], dtype=np.float32)
           for hand in result.hand_landmarks[:2]]
    return HandFrame(t, pts[0], pts[1] if len(pts) > 1 else None)


def load_captures(path: str):
    """Read the capture JSON. Returns [(label, take, [HandFrame, ...]), ...]."""
    with open(path) as fh:
        clips = json.load(fh)
    out = []
    for c in clips:
        if not c.get("frames"):
            continue
        out.append((c["label"], c.get("take"), frames_from_capture_clip(c)))
    return out
