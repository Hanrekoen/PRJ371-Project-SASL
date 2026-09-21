"""
The trained model, wearing the GestureClassifier interface server.py expects.

This is the class classifier.py's docstring said to write once the ML side had
a real model: same `classify(...) -> (gesture_id, confidence)` contract as
PlaceholderClassifier, so server.py, protocol.py and Unity's LessonManager are
unaffected. Swapping it in is one line in server.py.

Two things differ from the placeholder, both deliberate:

1. It needs the RAW HandLandmarker result, not just the (21, 3) `rel` array.
   The signs in the dataset are dynamic -- Hello and Bye are both waves -- so
   where the hand travels is most of the signal, and `rel` has already had the
   wrist subtracted out. `classify()` therefore takes an optional second
   argument, and server.py passes `results` alongside `landmarks`. The
   placeholder ignores it, so both classifiers still satisfy one interface.

2. It answers from a ~3 second window, not a single frame. A frame cannot tell
   a wave from a hand held still, so the classifier buffers frames internally
   and reports a sign only once several overlapping windows agree. Between
   signs it returns "NONE", which server.py already treats as "say nothing".

The pass/fail decision still belongs to Unity: this reports the best guess and
a calibrated confidence, and LessonManager compares that against the target and
GestureData.requiredConfidence exactly as it does for FakeGestureRecognizer.
What this class adds is the ability to say nothing at all when an attempt isn't
recognisable -- see ML_MODEL.md on the confidence gate.
"""
from __future__ import annotations

import glob
import os
import re
import time

from classifier import GestureClassifier
from gesture_classifier import GestureValidator

DEFAULT_MODEL = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                             "models", "sasl_gesture_model.joblib")
UNITY_GESTURE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                 "..", "Assets", "Data", "Gestures")


def unity_gesture_ids() -> set:
    """gestureIds Unity actually has GestureData assets for. Best effort --
    returns an empty set if the Assets folder isn't where we expect."""
    found = set()
    for path in glob.glob(os.path.join(UNITY_GESTURE_DIR, "*.asset")):
        try:
            with open(path, encoding="utf-8", errors="ignore") as fh:
                m = re.search(r"^\s*gestureId:\s*(\S+)\s*$", fh.read(), re.M)
            if m:
                found.add(m.group(1))
        except OSError:
            continue
    return found


class SASLGestureClassifier(GestureClassifier):
    def __init__(self, model_path: str = DEFAULT_MODEL,
                 hold_seconds: float = 1.5,
                 votes: int = 2,
                 evaluate_every: float = 0.05,
                 min_confidence: float | None = None,
                 gesture_id_map: dict | None = None,
                 verbose: bool = True):
        """
        votes          consecutive agreeing windows before a sign is reported.
        evaluate_every how often (seconds) to score the rolling window.
                       These two were picked by sweeping both against recorded
                       clips rather than guessed: (2, 0.05) gave 43/48 correct
                       with 0 wrong, where (3, 0.15) gave 38/48 with 10 missed.
                       Scoring more often costs a few ms per frame and buys
                       detections; demanding more votes mostly just costs.
        hold_seconds   how long a recognised sign keeps being reported, so the
                       headset sees it even though the underlying event is
                       momentary. server.py re-sends every 2s, so this should
                       stay a little under that.
        gesture_id_map maps model labels to Unity gestureIds where they differ,
                       e.g. {"Hello": "HELLO"}. Unmapped labels pass through.
        """
        self._validator = GestureValidator(model_path, votes_needed=votes,
                                           evaluate_every=evaluate_every,
                                           min_confidence=min_confidence)
        self._map = dict(gesture_id_map or {})
        self._hold_seconds = hold_seconds
        self._held = None          # (gesture_id, confidence, expires_at)
        self._target = None        # what the lesson is currently asking for
        self._target_conf = 0.0    # P(target) from the window that last fired

        if verbose:
            meta = self._validator.model.metadata
            acc = meta.get("cv_accuracy", meta.get("loco_accuracy", 0.0))
            print(f"[SASLGestureClassifier] {os.path.basename(model_path)} -- "
                  f"{meta.get('selected_model')}, {acc:.1%} "
                  f"({meta.get('cv_scheme', 'cross-validated')})")
            print(f"[SASLGestureClassifier] Signs: {', '.join(self._validator.labels)}")
            self._warn_about_unity_ids()

    def _warn_about_unity_ids(self):
        known = unity_gesture_ids()
        if not known:
            return
        missing = [self.to_gesture_id(l) for l in self._validator.labels
                   if self.to_gesture_id(l) not in known]
        if missing:
            print(f"[SASLGestureClassifier] WARNING: no GestureData asset in Unity for "
                  f"{', '.join(missing)}. LessonManager can never match these as a "
                  f"target until someone creates Assets/Data/Gestures/Gesture_<id>.asset "
                  f"(Unity currently has {len(known)} gestures: A-Z fingerspelling). "
                  f"Alternatively map the model's labels onto existing ids with "
                  f"gesture_id_map=.")

    def to_gesture_id(self, label: str) -> str:
        return self._map.get(label, label)

    def from_gesture_id(self, gesture_id: str) -> str:
        """Unity gestureId -> the model's own label."""
        for label, gid in self._map.items():
            if gid == gesture_id:
                return label
        return gesture_id

    def set_target(self, gesture_id):
        """Tell the classifier which sign the lesson is currently asking for.

        This does NOT bias the answer -- `classify()` still reports the model's
        best guess across every sign it knows. What it enables is reporting
        P(target) alongside that guess, via `target_confidence`.

        The distinction matters. The lesson's question is binary -- "did they
        sign the target?" -- and the model already computes exactly that number,
        then throws it away by reporting the argmax. On a confusable pair, a
        learner who signs the target correctly can be scored wrong because the
        runner-up edged it: target 45%, other 42%, reported "other". Reporting
        the target's own 45% answers the real question honestly, and it is still
        a real bar -- 45% across 42 signs is nineteen times chance, and all three
        gates still have to pass first.

        What this deliberately avoids is nudging the classifier toward the
        target. That turns the tutor into a rubber stamp, and it fails worst on
        exactly the pairs that are hard, where a learner signing the wrong one of
        a confusable pair would be told they were right.
        """
        new = self.from_gesture_id(gesture_id) if gesture_id else None
        if new != self._target:
            self._target = new
            self._target_conf = 0.0

    @property
    def target(self):
        return self._target

    @property
    def target_confidence(self) -> float:
        """P(target) from the window that produced the sign currently being
        reported. 0.0 when nothing is being reported, or no target is set."""
        return self._target_conf if self._held else 0.0

    @property
    def labels(self):
        return self._validator.labels

    @property
    def model(self):
        """The underlying GestureModel -- thresholds, labels, metadata."""
        return self._validator.model

    @property
    def status(self) -> dict:
        """Live read-out for the preview window: the current best guess and,
        when nothing is being accepted, the reason why."""
        return self._validator.status

    def reset(self):
        self._validator.reset()
        self._held = None
        self._target_conf = 0.0

    def classify(self, landmarks, result=None, t=None):
        """landmarks is accepted for interface compatibility and unused --
        this model reads the raw `result` instead, for the reasons in the
        module docstring. Returns (gesture_id, confidence).

        `t` overrides the clock, so a recorded clip can be replayed through the
        full temporal path faster than real time (see test_pipeline.py). Live,
        leave it alone."""
        now = time.monotonic() if t is None else t

        if result is None:
            # Called the old single-argument way. Fail loudly rather than
            # silently reporting NONE forever, which would look like a camera
            # or lighting problem rather than a wiring mistake.
            raise TypeError(
                "SASLGestureClassifier.classify() needs the raw HandLandmarker "
                "result as its second argument: classifier.classify(landmarks, results). "
                "See server.py's camera loop.")

        event = self._validator.update(result, now)
        if event is not None:
            self._held = (self.to_gesture_id(event.label), event.confidence,
                          now + self._hold_seconds)
            # Capture P(target) from the same window that fired, not from
            # whatever the buffer holds later.
            self._target_conf = float(event.probabilities.get(self._target, 0.0)) \
                if self._target else 0.0

        if self._held and now < self._held[2]:
            return self._held[0], self._held[1]

        self._held = None
        return "NONE", 0.0


class RoutedGestureClassifier(GestureClassifier):
    """Several models behind one GestureClassifier, routed by the current target.

    WHY THIS EXISTS
    One 42-sign model and two smaller ones were measured against the same 84
    replayed clips. The single model fired the WRONG sign 7 times; routing between
    a 26-sign letters model and a 16-sign phrases model brought that to 1, at no
    cost in detection (64 -> 65 correct).

    The reason is not that the split models are cleverer. It is that every
    threshold in the pipeline is calibrated against the number of classes, and
    mixing static letters with dynamic phrases wrecks both ends of that:

        one 42-sign model : confidence bar 0.27, liveness floor 0.47
        letters model     : confidence bar 0.46, liveness floor 0.36
        phrases model     : confidence bar 0.51, liveness floor 1.56

    The liveness floor is derived from the quietest genuine clip. Held letters
    barely move, so mixing them in drags the floor down and the gate stops
    rejecting a resting hand -- for the phrases too, which never needed that.
    Splitting lets each model keep the thresholds its own signs justify.

    ROUTING RULE
    Whichever model knows the target sign handles it. Not the level number --
    routing on what a model actually knows needs no convention to be kept in sync,
    and it keeps working when signs move between levels. When no target is set, or
    no model knows it, the first model is used so the preview window still shows
    something sensible.
    """

    def __init__(self, model_paths, verbose: bool = True, **kwargs):
        self._members = [SASLGestureClassifier(p, verbose=False, **kwargs)
                         for p in model_paths]
        if not self._members:
            raise ValueError("RoutedGestureClassifier needs at least one model")
        self._active = self._members[0]
        self._target = None

        if verbose:
            for path, m in zip(model_paths, self._members):
                meta = m.model.metadata
                acc = meta.get("cv_accuracy", 0.0)
                print(f"[RoutedGestureClassifier] {os.path.basename(path)} -- "
                      f"{len(m.labels)} signs, {acc:.1%} "
                      f"({meta.get('cv_scheme', 'cross-validated')}), "
                      f"bar {m.model.min_confidence:.2f}")
            overlap = self._overlapping_labels()
            if overlap:
                print(f"[RoutedGestureClassifier] NOTE: {', '.join(sorted(overlap))} "
                      f"appear(s) in more than one model; the first listed wins. "
                      f"That is usually a sign the capture files are split wrongly.")
            self._members[0]._warn_about_unity_ids()

    def _overlapping_labels(self):
        seen, dupes = set(), set()
        for m in self._members:
            for l in m.labels:
                if l in seen:
                    dupes.add(l)
                seen.add(l)
        return dupes

    @property
    def labels(self):
        out = []
        for m in self._members:
            for l in m.labels:
                if l not in out:
                    out.append(l)
        return out

    @property
    def model(self):
        return self._active.model

    @property
    def status(self) -> dict:
        return self._active.status

    @property
    def target_confidence(self) -> float:
        return self._active.target_confidence

    def set_target(self, gesture_id):
        self._target = gesture_id
        chosen = None
        if gesture_id:
            for m in self._members:
                if gesture_id in set(m.labels):
                    chosen = m
                    break
        self._active = chosen or self._members[0]
        # Every member gets the target so none keeps a stale one, and every member
        # is reset so a model that takes over later starts from a clean window
        # rather than frames captured while a different sign was on screen.
        for m in self._members:
            m.set_target(gesture_id)
            if m is not self._active:
                m.reset()

    def reset(self):
        for m in self._members:
            m.reset()

    def classify(self, landmarks, result=None, t=None):
        return self._active.classify(landmarks, result, t=t)
