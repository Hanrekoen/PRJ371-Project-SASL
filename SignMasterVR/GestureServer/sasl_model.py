"""
The trained artefact itself: classifier + confidence gate + metadata.

Kept in its own module because joblib pickles by import path -- both
train_gesture_model.py and the live runtime must be able to import these exact
classes, and defining them in the training script would make saved models
unloadable anywhere else.

The gate is the part that matters for a tutor. A two-class classifier is a
forced choice: show it a shrug, a scratched nose, or a half-finished sign and it
will still answer "Hello" or "Bye", often confidently, because softmax has to
sum to one. Three independent checks stand in front of it:

  1. Liveness  -- was a hand actually present and moving for most of the window?
  2. Novelty   -- does this attempt look like ANY sign the model was trained on,
                  measured as distance from the training distribution rather
                  than by asking the classifier?
  3. Confidence-- is the calibrated probability of the winning class high enough?

An attempt has to pass all three before the model commits to a label. That is
the difference between a demo and something you can put in front of a learner.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
from sklearn.covariance import LedoitWolf


class NoveltyGate:
    """Is this attempt anything like the training data?

    Projects features into a low-dimensional PCA space (fitted upstream in the
    pipeline) and measures Mahalanobis distance to each class's training cloud.
    A shrunk (Ledoit-Wolf) covariance is used because 20 examples per class
    cannot support a full covariance estimate.
    """

    def __init__(self, threshold_percentile: float = 99.0):
        self.threshold_percentile = threshold_percentile
        self.means_: dict = {}
        self.precisions_: dict = {}
        self.threshold_: float = np.inf

    def fit(self, Z: np.ndarray, y: np.ndarray):
        for cls in np.unique(y):
            Zi = Z[y == cls]
            lw = LedoitWolf().fit(Zi)
            self.means_[cls] = Zi.mean(axis=0)
            self.precisions_[cls] = lw.precision_
        d = np.array([self.distance(z) for z in Z])
        # Threshold set from the training distribution itself: anything further
        # out than almost every genuine training example is treated as "not a
        # sign I know". Tune with --novelty-percentile if it is too strict.
        self.threshold_ = float(np.percentile(d, self.threshold_percentile))
        return self

    def distance(self, z: np.ndarray) -> float:
        best = np.inf
        for cls, mu in self.means_.items():
            diff = z - mu
            m = float(diff @ self.precisions_[cls] @ diff)
            best = min(best, np.sqrt(max(m, 0.0)))
        return best

    def accepts(self, z: np.ndarray) -> tuple[bool, float]:
        d = self.distance(z)
        return d <= self.threshold_, d


@dataclass
class GestureModel:
    """Everything the runtime needs, in one picklable object."""
    pipeline: object                 # StandardScaler -> PCA -> calibrated clf
    embedder: object                 # StandardScaler -> PCA (for the gate)
    gate: NoveltyGate
    labels: list
    feature_names: list
    min_confidence: float = 0.75
    min_detection_rate: float = 0.6
    min_motion_energy: float = 0.5
    window_seconds: float = 2.9
    metadata: dict = field(default_factory=dict)

    def predict(self, features: np.ndarray, info: dict | None = None) -> dict:
        """Classify one window.

        Returns a dict with `label` (None when rejected), `confidence`,
        `probabilities`, `accepted`, and `reason` -- the reason being what a
        tutor UI shows the learner instead of a wrong answer.
        """
        info = info or {}
        out = {
            "label": None, "confidence": 0.0, "probabilities": {},
            "accepted": False, "reason": None, "novelty_distance": None,
        }

        det = info.get("detection_rate", 1.0)
        if det < self.min_detection_rate:
            out["reason"] = f"hand visible in only {det:.0%} of the window"
            return out

        energy = info.get("motion_energy")
        if energy is not None and energy < self.min_motion_energy:
            out["reason"] = "hand is holding still -- no sign attempted"
            return out

        X = features.reshape(1, -1)
        z = self.embedder.transform(X)[0]
        ok, dist = self.gate.accepts(z)
        out["novelty_distance"] = round(float(dist), 3)

        proba = self.pipeline.predict_proba(X)[0]
        order = np.argsort(proba)[::-1]
        out["probabilities"] = {str(self.labels[i]): float(proba[i]) for i in order}
        top = int(order[0])
        conf = float(proba[top])
        out["confidence"] = conf

        if not ok:
            out["reason"] = (f"doesn't match any known sign "
                             f"(distance {dist:.1f} > {self.gate.threshold_:.1f})")
            return out
        if conf < self.min_confidence:
            out["reason"] = (f"between signs -- best guess {self.labels[top]} "
                             f"at {conf:.0%}, below the {self.min_confidence:.0%} bar")
            return out

        out["label"] = str(self.labels[top])
        out["accepted"] = True
        return out
