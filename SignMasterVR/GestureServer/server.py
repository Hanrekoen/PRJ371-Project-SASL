"""
GestureServer -- runs on the laptop with the external webcam. Captures video,
tracks the learner's hand, classifies it against whatever sign the headset
says is the current target, and reports the result back over the network.

See protocol.py for the wire format, ML_MODEL.md for the trained classifier
this now uses by default (pass --placeholder for the old open-hand/fist
stand-in), and hand_tracker.py's module docstring for the note about the input
spec changing from XR Hands to webcam/MediaPipe.

Setup (on the laptop):
    pip install -r requirements.txt
    python server.py --port 8765

Then in Unity (headset build machine):
    1. Run Tools > SignMasterVR > 13 Switch To Network Gesture Recognizer
       (ClassRoom.unity open, Step 4 already run).
    2. Select LessonSystem in the Hierarchy -> Network Gesture Recognizer.
    3. Set "Laptop Host" to this machine's IP on the SAME Wi-Fi/network as
       the headset (Windows: run `ipconfig`, use the IPv4 Address under your
       Wi-Fi adapter -- NOT 127.0.0.1, unless you're testing in the Editor on
       this same laptop with no headset involved).
    4. Set "Laptop Port" to match --port here (default 8765).
    5. Save the scene (Ctrl+S).

Only one headset connects at a time. If it disconnects (Play mode stopped,
Wi-Fi hiccup, headset restarted), this program keeps listening and picks the
next connection back up automatically -- no need to restart it between test
runs.

A debug preview window (OpenCV) shows the tracked hand skeleton, the current
target gestureId, and the classifier's live guess -- press "q" in that window
to quit. Pass --no-preview to run headless (e.g. over SSH).
"""
import argparse
import socket
import threading
import time

import cv2

from classifier import PlaceholderClassifier
from hand_tracker import HandTracker
from protocol import decode, encode


class ClientSession:
    """One connected headset. A background thread reads its "target"
    messages; the main camera loop calls send_result() whenever the
    classifier's guess changes."""

    def __init__(self, conn: socket.socket, addr):
        self.conn = conn
        self.addr = addr
        self.alive = True
        self.current_level = 0
        self._current_target = None
        self._lock = threading.Lock()
        self._file = conn.makefile("r", encoding="utf-8")
        self._send(encode({"type": "hello_ack"}))
        self._reader_thread = threading.Thread(target=self._read_loop, daemon=True)
        self._reader_thread.start()

    def target_gesture_id(self):
        with self._lock:
            return self._current_target

    def _read_loop(self):
        try:
            for line in self._file:
                line = line.strip()
                if not line:
                    continue
                try:
                    msg = decode(line)
                except Exception as e:
                    print(f"[GestureServer] Bad message from headset: {line!r} ({e})")
                    continue

                msg_type = msg.get("type")
                if msg_type == "target":
                    with self._lock:
                        self._current_target = msg.get("gestureId")
                        self.current_level = msg.get("level", 0)
                    print(f"[GestureServer] Target set: {self._current_target} (level {self.current_level})")
                elif msg_type == "ping":
                    self._send(encode({"type": "pong"}))
        except (ConnectionError, OSError):
            pass
        finally:
            self.alive = False
            print(f"[GestureServer] Headset {self.addr} disconnected.")

    def _send(self, data: bytes):
        try:
            self.conn.sendall(data)
        except (ConnectionError, OSError):
            self.alive = False

    def send_result(self, gesture_id: str, confidence: float):
        self._send(encode({"type": "result", "gestureId": gesture_id, "confidence": confidence}))

    def close(self):
        self.alive = False
        try:
            self.conn.close()
        except OSError:
            pass


def accept_loop(server_sock, session_holder):
    """Accepts headset connections forever, one at a time. session_holder is a
    one-element list used as a simple mutable box the main thread also reads."""
    while True:
        try:
            conn, addr = server_sock.accept()
        except OSError:
            return  # server_sock was closed during shutdown
        print(f"[GestureServer] Headset connected from {addr}.")
        if session_holder[0] is not None:
            session_holder[0].close()
        session_holder[0] = ClientSession(conn, addr)


def main():
    parser = argparse.ArgumentParser(description="SignMasterVR gesture recognition server (laptop side)")
    parser.add_argument("--host", default="0.0.0.0", help="Interface to listen on (default: all interfaces)")
    parser.add_argument("--port", type=int, default=8765, help="Must match NetworkGestureRecognizer.laptopPort in Unity")
    parser.add_argument("--camera", type=int, default=0, help="Webcam index (default: 0 -- try 1, 2... if that's the wrong camera)")
    parser.add_argument("--no-preview", action="store_true", help="Don't open the debug preview window")
    parser.add_argument("--model", default=None,
                        help="Path to the trained model (default: models/sasl_gesture_model.joblib)")
    parser.add_argument("--placeholder", action="store_true",
                        help="Use the old open-hand/fist PlaceholderClassifier instead of the "
                             "trained model -- handy for testing the network path on a machine "
                             "without scikit-learn installed")
    args = parser.parse_args()

    session_holder = [None]  # holds the current ClientSession, or None

    server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server_sock.bind((args.host, args.port))
    server_sock.listen(1)
    print(f"[GestureServer] Listening on {args.host}:{args.port} -- waiting for the headset...")
    print("[GestureServer] If the headset can't connect: allow this program through Windows Firewall "
          "when prompted (or check it isn't already blocked), and make sure both devices are on the "
          "same Wi-Fi network.")

    threading.Thread(target=accept_loop, args=(server_sock, session_holder), daemon=True).start()

    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        raise SystemExit(f"[GestureServer] Could not open webcam index {args.camera}. Try a different --camera index.")

    tracker = HandTracker()
    if args.placeholder:
        classifier = PlaceholderClassifier(
            get_target_gesture_id=lambda: (session_holder[0].target_gesture_id() if session_holder[0] else None)
        )
        print("[GestureServer] Using PlaceholderClassifier (open hand = correct, fist = wrong).")
    else:
        # The trained model. Same classify() contract as the placeholder, but it
        # reads a ~3s window rather than one frame, because the signs it knows
        # are dynamic. See ML_MODEL.md. Imported here rather than at the top so
        # that --placeholder still works on a machine without scikit-learn.
        from sasl_classifier import DEFAULT_MODEL, SASLGestureClassifier
        classifier = SASLGestureClassifier(model_path=args.model or DEFAULT_MODEL)

    last_sent = None
    last_sent_time = 0.0
    RESEND_INTERVAL_SECONDS = 2.0  # safety-net resend even when the guess hasn't changed

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                print("[GestureServer] Camera read failed, retrying...")
                time.sleep(0.1)
                continue

            frame = cv2.flip(frame, 1)  # mirror -- feels natural to whoever's watching the preview
            results, landmarks = tracker.process(frame)
            # `results` carries the absolute landmark positions the trained model
            # needs; `landmarks` has had the wrist subtracted out. The
            # placeholder ignores the second argument.
            gesture_id, confidence = classifier.classify(landmarks, results)

            session = session_holder[0]
            now = time.time()
            if session is not None and session.alive and gesture_id != "NONE":
                changed = gesture_id != last_sent
                stale = (now - last_sent_time) > RESEND_INTERVAL_SECONDS
                if changed or stale:
                    session.send_result(gesture_id, confidence)
                    last_sent, last_sent_time = gesture_id, now
            elif gesture_id == "NONE":
                last_sent = "NONE"

            if not args.no_preview:
                tracker.draw(frame, results)
                target = session.target_gesture_id() if session else None
                status = "connected" if (session and session.alive) else "waiting for headset..."
                cv2.putText(frame, f"Status: {status}", (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)
                cv2.putText(frame, f"Target: {target}", (10, 50), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)
                cv2.putText(frame, f"Guess: {gesture_id} ({confidence:.2f})", (10, 75), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
                # The trained classifier explains its silence -- "hand is holding
                # still", "doesn't match any known sign" -- which is far easier to
                # debug in front of a camera than a blank "NONE".
                reason = getattr(classifier, "status", {}).get("reason")
                if reason:
                    cv2.putText(frame, reason[:64], (10, 100), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
                cv2.imshow("SignMasterVR Gesture Server", frame)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
    finally:
        cap.release()
        cv2.destroyAllWindows()
        tracker.close()
        if session_holder[0] is not None:
            session_holder[0].close()
        server_sock.close()


if __name__ == "__main__":
    main()
