"""
Real-time sign validation on top of hand_tracker.HandTracker.

Training clips are neatly trimmed; a live camera is not. This module turns the
frame stream into overlapping fixed-length windows, scores each one, and only
announces a sign when several consecutive windows agree -- which is what stops a
single lucky frame-window from firing "Hello" while a learner is still settling.

Typical use:

    from hand_tracker import HandTracker
    from gesture_classifier import GestureValidator

    tracker = HandTracker(max_hands=1)
    validator = GestureValidator("models/sasl_gesture_model.joblib")

    result, _rel = tracker.process(frame)
    event = validator.update(result)     # None most frames
    if event:
        print(event.label, event.confidence)

`validator.status` carries the live read-out for a tutor UI (current best guess
and why it is or isn't being accepted) even when no event has fired.
"""
from __future__ import annotations

import time
from collections import deque
from dataclasses import dataclass

import joblib
import numpy as np

from sasl_features import extract_features, hand_frame_from_mp_result


@dataclass
class SignEvent:
    label: str
    confidence: float
    probabilities: dict
    t: float


class GestureValidator:
    def __init__(self, model_path: str = "models/sasl_gesture_model.joblib",
                 window_seconds: float | None = None,
                 evaluate_every: float = 0.15,
                 votes_needed: int = 3,
                 refractory_seconds: float = 1.5,
                 min_confidence: float | None = None):
        self.model = joblib.load(model_path)
        # Default to the window length the model was trained on. Velocity and
        # oscillation features are duration-sensitive, so a mismatched window
        # quietly shifts every attempt out of the training distribution.
        self.window_seconds = window_seconds or self.model.window_seconds
        if min_confidence is not None:
            self.model.min_confidence = min_confidence

        self.evaluate_every = evaluate_every
        self.votes_needed = votes_needed
        self.refractory_seconds = refractory_seconds

        self._frames: deque = deque()
        self._votes: deque = deque(maxlen=votes_needed)
        self._last_eval = 0.0
        self._last_fire = -1e9
        self.status: dict = {"label": None, "reason": "warming up",
                             "confidence": 0.0, "accepted": False}

    @property
    def labels(self):
        return self.model.labels

    def reset(self):
        self._frames.clear()
        self._votes.clear()

    def update(self, mp_result, t: float | None = None) -> SignEvent | None:
        """Feed one frame. Pass the FIRST value from HandTracker.process().

        Returns a SignEvent on the frame where a sign is confirmed, else None.
        """
        t = time.monotonic() if t is None else t
        self._frames.append(hand_frame_from_mp_result(mp_result, t))

        cutoff = t - self.window_seconds
        while self._frames and self._frames[0].t < cutoff:
            self._frames.popleft()

        if t - self._last_eval < self.evaluate_every:
            return None
        self._last_eval = t

        span = self._frames[-1].t - self._frames[0].t if len(self._frames) > 1 else 0.0
        if span < self.window_seconds * 0.8:
            self.status = {"label": None, "confidence": 0.0, "accepted": False,
                           "reason": f"filling window ({span:.1f}/{self.window_seconds:.1f}s)"}
            return None

        feats, info = extract_features(list(self._frames))
        if feats is None:
            self._votes.clear()
            self.status = {"label": None, "confidence": 0.0, "accepted": False,
                           "reason": info.get("reason") or "no hand"}
            return None

        out = self.model.predict(feats, info)
        self.status = out

        if not out["accepted"]:
            self._votes.clear()
            return None

        self._votes.append(out["label"])
        if len(self._votes) < self.votes_needed or len(set(self._votes)) != 1:
            return None
        if t - self._last_fire < self.refractory_seconds:
            return None

        self._last_fire = t
        self._votes.clear()
        # Clear the buffer so the next sign is judged on its own frames rather
        # than on the tail of the one just recognised.
        self._frames.clear()
        return SignEvent(out["label"], out["confidence"], out["probabilities"], t)


def score_clip(model_path: str, frames) -> dict:
    """Offline scoring of one whole clip -- used by the tests and handy for
    checking a newly recorded capture before adding it to the training set."""
    model = joblib.load(model_path)
    feats, info = extract_features(frames)
    if feats is None:
        return {"label": None, "accepted": False,
                "reason": info.get("reason"), "confidence": 0.0}
    return model.predict(feats, info)
