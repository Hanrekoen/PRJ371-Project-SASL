"""
Live webcam demo: sign in front of the camera, get validated in real time.

    python live_demo.py                       # default model, camera 0
    python live_demo.py --camera 1 --min-confidence 0.85

Keys:  q / Esc quit    r reset the window    d toggle the probability read-out

The HUD deliberately shows REJECTIONS as well as hits -- "hand is holding
still", "doesn't match any known sign" -- because for a tutor that is the
useful feedback. A learner needs to know their attempt wasn't recognised and
why, not to see a coin-flip label appear.
"""
from __future__ import annotations

import argparse
import time

import cv2

from gesture_classifier import GestureValidator
from hand_tracker import HandTracker

GREEN, RED, AMBER, WHITE = (80, 220, 80), (60, 60, 235), (60, 190, 240), (240, 240, 240)


def draw_hud(frame, validator, last_event, event_age, show_probs):
    h, w = frame.shape[:2]
    cv2.rectangle(frame, (0, 0), (w, 92), (28, 28, 28), -1)

    status = validator.status
    if last_event and event_age < 2.0:
        colour = GREEN
        line = f"{last_event.label.upper()}  ({last_event.confidence:.0%})"
    elif status.get("accepted"):
        colour, line = AMBER, f"reading {status['label']} ..."
    else:
        colour = RED if status.get("reason") else WHITE
        line = status.get("reason") or "waiting"

    cv2.putText(frame, line, (16, 42), cv2.FONT_HERSHEY_SIMPLEX, 0.95, colour, 2)
    cv2.putText(frame, "signs: " + ", ".join(validator.labels), (16, 72),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (170, 170, 170), 1)

    if show_probs and status.get("probabilities"):
        y = 118
        for label, p in status["probabilities"].items():
            cv2.rectangle(frame, (16, y - 12), (16 + int(180 * p), y + 4),
                          (90, 140, 90), -1)
            cv2.putText(frame, f"{label} {p:.0%}", (204, y),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, WHITE, 1)
            y += 26
        if status.get("novelty_distance") is not None:
            cv2.putText(frame, f"novelty {status['novelty_distance']}", (16, y + 4),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.45, (170, 170, 170), 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="models/sasl_gesture_model.joblib")
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--min-confidence", type=float, default=None)
    ap.add_argument("--votes", type=int, default=3,
                    help="consecutive agreeing windows required before a sign fires")
    ap.add_argument("--no-mirror", action="store_true",
                    help="don't flip the preview (recognition is unaffected either way)")
    args = ap.parse_args()

    validator = GestureValidator(args.model, votes_needed=args.votes,
                                 min_confidence=args.min_confidence)
    print("Loaded model:", validator.model.metadata.get("selected_model"))
    print("Signs:", ", ".join(validator.labels))

    tracker = HandTracker(max_hands=1)
    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        raise SystemExit(f"Could not open camera {args.camera}.")

    last_event, last_event_t, show_probs = None, -1e9, True
    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            if not args.no_mirror:
                # Mirroring is a comfort choice for the person on camera.
                # Features are chirality-canonicalized, so it changes nothing
                # about what the model sees.
                frame = cv2.flip(frame, 1)

            result, _rel = tracker.process(frame)
            event = validator.update(result)
            if event:
                last_event, last_event_t = event, time.monotonic()
                print(f"[{time.strftime('%H:%M:%S')}] {event.label}  "
                      f"{event.confidence:.0%}  " +
                      "  ".join(f"{k}={v:.2f}" for k, v in event.probabilities.items()))

            tracker.draw(frame, result)
            draw_hud(frame, validator, last_event,
                     time.monotonic() - last_event_t, show_probs)
            cv2.imshow("SASL sign validator", frame)

            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27):
                break
            if key == ord("r"):
                validator.reset()
            if key == ord("d"):
                show_probs = not show_probs
    finally:
        cap.release()
        cv2.destroyAllWindows()
        tracker.close()


if __name__ == "__main__":
    main()
