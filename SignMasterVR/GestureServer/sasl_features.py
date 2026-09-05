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
    """One frame of one hand. `xyz` is (21, 3) absolute image-normalized."""
    t: float          # seconds
    xyz: np.ndarray | None   # None when no hand was detected in this frame


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


def canonicalize(xyz: np.ndarray, aspect: float = DEFAULT_ASPECT):
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

    chir = palm_chirality(p)
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
    return names


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

    usable = []
    for f in frames:
        if f.xyz is None:
            continue
        c = canonicalize(f.xyz, aspect=aspect)
        if c is None:
            continue
        rel, wrist_xy, scale, chir = c
        usable.append((f.t, shape_descriptor(rel), wrist_xy, scale, chir))

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
    """Browser capture JSON -> HandFrames.

    Where two hands are visible (28 frames across the set, mostly the other
    hand drifting into shot) we keep the hand that dominates the clip, matched
    by proximity to the previous kept hand.
    """
    frames, prev = [], None
    t0 = clip["frames"][0]["t"] / 1000.0
    for f in clip["frames"]:
        t = f["t"] / 1000.0 - t0
        hands = f.get("hands") or []
        if not hands:
            frames.append(HandFrame(t, None))
            continue
        pts = [np.array([[p["x"], p["y"], p["z"]] for p in h["landmarks"]],
                        dtype=np.float32) for h in hands]
        if len(pts) == 1 or prev is None:
            chosen = pts[0]
        else:
            chosen = min(pts, key=lambda q: float(np.linalg.norm(q[WRIST] - prev[WRIST])))
        prev = chosen
        frames.append(HandFrame(t, chosen))
    return frames


def hand_frame_from_mp_result(result, t: float) -> HandFrame:
    """Live mediapipe Tasks HandLandmarkerResult -> HandFrame.

    Pass the FIRST element returned by HandTracker.process() (the raw result),
    not the second. process()'s `rel` output has already thrown away where the
    hand is in frame, and this pipeline needs that.
    """
    if not getattr(result, "hand_landmarks", None):
        return HandFrame(t, None)
    hand = result.hand_landmarks[0]
    xyz = np.array([[lm.x, lm.y, lm.z] for lm in hand], dtype=np.float32)
    return HandFrame(t, xyz)


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
