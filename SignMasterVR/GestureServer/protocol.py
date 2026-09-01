"""
Wire protocol between the laptop (this program) and the Quest headset
(Unity, Assets/Scripts/ML/NetworkGestureRecognizer.cs). Newline-delimited
JSON over a plain TCP socket -- no external protocol library needed on
either side, and it's readable in a packet capture if something misbehaves.

The laptop is the TCP SERVER (it listens; server.py binds a port and waits).
The headset is the TCP CLIENT (NetworkGestureRecognizer connects out to the
laptop's IP). This way the laptop can be started once and left running --
it doesn't need to know the headset's address, only its own listening port.

Message shapes -- one per line, always a flat JSON object:

  Headset -> Laptop
    {"type": "target", "gestureId": "A", "level": 1}
        Sent every time the learner is shown a new sign to attempt.
        gestureId must match GestureData.gestureId in Unity (e.g. "A".."Z").
    {"type": "ping"}
        Optional heartbeat; laptop replies with "pong".

  Laptop -> Headset
    {"type": "hello_ack"}
        Sent once, right after accepting the connection.
    {"type": "result", "gestureId": "A", "confidence": 0.92}
        Sent whenever the classifier's stabilized guess changes. gestureId is
        always the classifier's BEST GUESS, correct or not -- this program
        never decides pass/fail itself. Unity's LessonManager does that, by
        comparing this against the current target and GestureData's
        requiredConfidence -- exactly like it already does for
        FakeGestureRecognizer and would for any future real classifier. That
        keeps GestureResult's contract (see GestureResult.cs's doc comment)
        identical no matter which recognizer is plugged in.
    {"type": "pong"}
        Reply to "ping".

Keep this file and NetworkGestureRecognizer.cs's private WireMessage class in
sync by hand -- there's no shared schema generator between Python and C# here,
so this docstring is the single source of truth for the wire format itself.
"""
import json


def encode(message: dict) -> bytes:
    """dict -> one line of JSON + \\n, ready to write straight to a socket."""
    return (json.dumps(message) + "\n").encode("utf-8")


def decode(line: str) -> dict:
    """One line of JSON (no trailing newline needed) -> dict."""
    return json.loads(line)
