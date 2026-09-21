# Unity-side changes for `targetConfidence`

Three small edits. **None of this has been compiled** — it was written against the
project's existing code but never opened in Unity. Treat it as a proposed patch,
not tested code.

The Python side is already done: `server.py` now sends a `targetConfidence` field
on every `result` message (see `protocol.py` for the format and the reasoning).
Unity currently ignores it.

## Why

`LessonManager` decides correctness with:

```csharp
bool correct = result.GestureId == gesture.gestureId
               && result.Confidence >= gesture.requiredConfidence;
```

`GestureId` is the model's best guess across **all** signs it knows. With 42 signs
that means a learner who signs the target correctly gets marked wrong whenever a
confusable sign edges it out — the model's own numbers may show the target at 45%
and the winner at 47%.

`targetConfidence` is the model's probability for the sign that was *asked for*,
taken from the same window. It answers the question the lesson is actually asking:
"did they sign the target?" A wrong sign cannot pass this check by construction —
it is the target's own probability being thresholded.

Keep `GestureId` for feedback ("you signed Sorry instead of Please"); use
`targetConfidence` for pass/fail.

**Measured, so you know what you're getting:** across 84 replayed clips spanning
all 42 signs, argmax gave 68 passes and 7 cases where the model named a different
sign; `targetConfidence` at the flat floor gave 69 passes. Roughly a wash today.
Its value is that it asks the right question and degrades more gracefully as the
vocabulary grows — argmax gets harder with every sign added, `P(target)` does not
in the same way.

---

## 1. `Assets/Scripts/ML/GestureResult.cs`

Add a field and a three-argument constructor. **Keep the two-argument one** —
`FakeGestureRecognizer` uses it.

```csharp
public struct GestureResult
{
    public string GestureId;         // best guess, correct or not
    public float Confidence;         // 0..1, probability of GestureId
    public float TargetConfidence;   // 0..1, probability of the sign that was ASKED for

    public GestureResult(string gestureId, float confidence)
        : this(gestureId, confidence, 0f) { }

    public GestureResult(string gestureId, float confidence, float targetConfidence)
    {
        GestureId = gestureId;
        Confidence = confidence;
        TargetConfidence = targetConfidence;
    }
}
```

`TargetConfidence` is 0 when the recognizer doesn't supply one — the fake
recognizer, the placeholder classifier, or an older server build. Step 3 handles
that case, so nothing breaks if you run against an old server.

## 2. `Assets/Scripts/ML/NetworkGestureRecognizer.cs`

Add the field to the private `WireMessage` class so `JsonUtility` picks it up:

```csharp
public float targetConfidence;
```

and pass it through where the result is raised:

```csharp
case "result":
    OnGestureDetected?.Invoke(
        new GestureResult(msg.gestureId, msg.confidence, msg.targetConfidence));
    break;
```

## 3. `Assets/Scripts/Lessons/LessonManager.cs`

Replace the correctness line in `HandleGestureResult`:

```csharp
private void HandleGestureResult(GestureResult result)
{
    var gesture = CurrentGesture;

    // Prefer the target's own probability: it answers "did they sign the target?"
    // directly, and a wrong sign cannot pass it. Fall back to the old string
    // comparison when the recognizer doesn't supply one (FakeGestureRecognizer,
    // --placeholder, or an older server build).
    bool correct = result.TargetConfidence > 0f
        ? result.TargetConfidence >= gesture.requiredConfidence
        : (result.GestureId == gesture.gestureId
           && result.Confidence >= gesture.requiredConfidence);

    StartCoroutine(GiveFeedbackAndAdvance(correct, result, gesture));
}
```

Optionally, in `GiveFeedbackAndAdvance`, use `result.GestureId` to say what they
actually signed when it wasn't the target — that's the feedback `targetConfidence`
alone can't give you:

```csharp
string hint = (!correct && result.GestureId != gesture.gestureId && result.GestureId != "NONE")
    ? $"That looked like \"{result.GestureId}\"."
    : "";
```

## After applying

1. Run `python make_unity_assets.py --write` in `GestureServer/` first — it sets
   every `requiredConfidence` to a value that matches the current model. The 0.75
   still on the A–Z assets is calibrated for two classes and will fail almost
   everything at 42.
2. Let Unity import, then check one asset in the Inspector.
3. Start `python server.py --port 8765`, enter Play mode, and watch the server
   console — it prints each target it receives and each result it sends.
