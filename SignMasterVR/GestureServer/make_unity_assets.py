"""
Generate the Unity GestureData / LevelData assets for whatever the model knows.

    python make_unity_assets.py                 # dry run, prints what it would do
    python make_unity_assets.py --write

WHY A SCRIPT AND NOT HAND-WRITTEN ASSETS
`LessonManager` can only ask for a sign that has a GestureData asset AND appears
in a LevelData. The model's sign list changes every time someone captures more
data, so hand-maintaining 40+ .asset files guarantees they drift out of sync with
the model. This reads the trained model and makes the assets match it.

WHAT IT SETS requiredConfidence TO -- and why not 0.75
The 0.75 default was chosen when there were two classes. Calibrated probabilities
spread across however many signs exist, so the same "confident" answer is ~0.95
with 2 signs and ~0.3 with 42. Left at 0.75 most correct attempts are marked
wrong.

The default is a flat floor that scales with the number of signs:
min(0.5, max(0.20, 3/n)). This assumes LessonManager compares `targetConfidence`
(see protocol.py) -- the model's probability for the sign that was ASKED for. A
wrong sign then cannot pass by construction, so the bar's only remaining job is
to reject non-attempts, and the liveness and novelty gates already do that
upstream.

A per-sign bar (--per-sign) sounded better and measured worse: on the 42-sign
model it passed 61 of 84 replayed clips where the flat floor passed 69. The
per-sign strictness rejected genuine attempts without preventing anything.

If you leave LessonManager comparing the argmax confidence instead of
targetConfidence, raise --floor -- that number is systematically higher. The
script prints both the median and the 10th percentile per sign so you can see
what you are choosing between.

SAFETY
Existing assets are updated in place for `requiredConfidence` only -- displayName,
audio keys, reference images and animation triggers are left exactly as they are.
New assets get a deterministic GUID derived from the sign name, so re-running
never orphans a reference. Nothing is deleted, ever.
"""
from __future__ import annotations

import argparse
import glob
import hashlib
import os
import re
from collections import defaultdict

import numpy as np

UNITY_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Data")
GESTURE_DIR = os.path.join(UNITY_ROOT, "Gestures")
LEVEL_DIR = os.path.join(UNITY_ROOT, "Levels")

ASSET_TEMPLATE = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {name}
  m_EditorClassIdentifier: Assembly-CSharp::SignMasterVR.Lessons.GestureData
  gestureId: {gesture_id}
  displayName: {display_name}
  tutorAnimationTrigger: Active
  referenceImage: {{fileID: 0}}
  requiredConfidence: {required_confidence}
  successMessage: Excellent!
  successAudioKey: Correct
  tryAgainAudioKey: TryAgain
"""

LEVEL_TEMPLATE = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {name}
  m_EditorClassIdentifier: Assembly-CSharp::SignMasterVR.Lessons.LevelData
  levelNumber: {level_number}
  levelName: {level_name}
  description: {description}
  gestures:
{gesture_refs}  requiredPreviousLevel: {{fileID: 0}}
"""

META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def safe_name(sign: str) -> str:
    return "".join(ch if ch.isalnum() else "_" for ch in sign).strip("_")


def stable_guid(seed: str) -> str:
    """Deterministic 32-hex Unity GUID. Same sign -> same GUID on every run, so
    re-running never breaks a LevelData's references to earlier assets."""
    return hashlib.md5(("SignMasterVR/" + seed).encode()).hexdigest()


def read_script_guid(directory: str, class_name: str):
    """Pull the MonoScript GUID out of an existing asset rather than hardcoding
    it -- it differs per project and changes if the script is ever reimported."""
    for path in sorted(glob.glob(os.path.join(directory, "*.asset"))):
        with open(path, encoding="utf-8", errors="ignore") as fh:
            text = fh.read()
        if class_name in text:
            m = re.search(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})", text)
            if m:
                return m.group(1)
    return None


def existing_assets(directory: str):
    """gestureId -> (path, guid, current requiredConfidence)."""
    out = {}
    for path in sorted(glob.glob(os.path.join(directory, "*.asset"))):
        with open(path, encoding="utf-8", errors="ignore") as fh:
            text = fh.read()
        gid = re.search(r"^\s*gestureId:\s*(.+?)\s*$", text, re.M)
        if not gid:
            continue
        conf = re.search(r"^\s*requiredConfidence:\s*([0-9.]+)", text, re.M)
        meta = path + ".meta"
        guid = None
        if os.path.exists(meta):
            mg = re.search(r"guid: ([0-9a-f]{32})", open(meta, encoding="utf-8").read())
            guid = mg.group(1) if mg else None
        out[gid.group(1)] = (path, guid, float(conf.group(1)) if conf else None)
    return out


def measure_bars(model, floor, cap, percentile):
    """Per-sign requiredConfidence from the model's own behaviour on real clips."""
    from sasl_features import extract_features
    from test_pipeline import load_clips

    known = set(model.labels)
    per_sign = defaultdict(list)
    for label, _take, frames in load_clips():
        if label not in known:
            continue
        feats, info = extract_features(frames)
        if feats is None:
            continue
        out = model.predict(feats, info)
        p = out["probabilities"].get(label, 0.0)
        per_sign[label].append(p)

    bars = {}
    for label in model.labels:
        vals = per_sign.get(label)
        if not vals:
            bars[label] = floor
            continue
        bars[label] = float(np.clip(np.percentile(vals, percentile), floor, cap))
    return bars, per_sign


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", action="append", default=None,
                    help="Trained model. Repeat it when the runtime routes between "
                         "several (e.g. letters + phrases) -- each sign then gets the "
                         "bar from the model that actually owns it, which is the one "
                         "whose thresholds it will be judged against.")
    ap.add_argument("--write", action="store_true", help="actually write files (default: dry run)")
    ap.add_argument("--per-sign", action="store_true",
                    help="bar per sign from its own clips (10th percentile) instead of a "
                         "flat floor. Measured on the 42-sign model this passed 61/84 "
                         "replayed clips where the flat floor passed 69 -- the per-sign "
                         "bars were doing more harm than good. Use it only if you have "
                         "evidence for your own data.")
    ap.add_argument("--percentile", type=float, default=10.0,
                    help="percentile used by --per-sign")
    ap.add_argument("--floor", type=float, default=None,
                    help="fixed requiredConfidence floor. Default: derived per model as "
                         "the value that passes --target-pass of its own genuine clips.")
    ap.add_argument("--target-pass", type=float, default=0.95,
                    help="share of genuine clips the bar should let through (default 0.95)")
    ap.add_argument("--cap", type=float, default=0.90)
    ap.add_argument("--no-levels", action="store_true")
    args = ap.parse_args()

    import joblib
    paths = args.model or ["models/sasl_gesture_model.joblib"]
    models = [joblib.load(p) for p in paths]

    labels, owner = [], {}
    for path, m in zip(paths, models):
        for l in (str(x) for x in m.labels):
            if l not in owner:
                owner[l] = (path, m)
                labels.append(l)

    print(f"{len(paths)} model(s), {len(labels)} signs total:")
    for path, m in zip(paths, models):
        mine = [l for l in labels if owner[l][0] == path]
        n = len(m.labels)
        print(f"  {os.path.basename(path):<28} {n:>3} signs, owns {len(mine):>3}")
    print()

    gesture_guid = read_script_guid(GESTURE_DIR, "GestureData")
    level_guid = read_script_guid(LEVEL_DIR, "LevelData")
    if not gesture_guid:
        raise SystemExit(f"Could not find the GestureData script GUID in {GESTURE_DIR}. "
                         f"Is the Unity project where this script expects it?")

    # Each sign's bar comes from the model that owns it: that is the model whose
    # thresholds it will actually be judged against at runtime.
    bars, per_sign, floors = {}, {}, {}
    for path, m in zip(paths, models):
        # Provisional pass to see how this model actually scores its own signs,
        # then set the floor from that rather than from the class count alone.
        # A count-based floor gave 0.20 for both split models while their typical
        # correct answer scores 0.72-0.79 -- a bar that filters nothing.
        _b, ps_probe = measure_bars(m, 0.0, 1.0, args.percentile)
        allp = np.array([v for vals in ps_probe.values() for v in vals])
        if args.floor is not None:
            f = args.floor
        elif len(allp):
            f = float(np.clip(np.percentile(allp, 100 * (1 - args.target_pass)), 0.20, 0.60))
        else:
            f = 0.20
        floors[path] = f
        b, ps = measure_bars(m, f, args.cap, args.percentile)
        for l in (str(x) for x in m.labels):
            if owner[l][0] == path:
                bars[l] = b.get(l, f)
                per_sign[l] = ps.get(l, [])
    floor = min(floors.values()) if floors else 0.20
    for path in paths:
        print(f"  bar for {os.path.basename(path):<28} {floors[path]:.2f} "
              f"(passes ~{args.target_pass:.0%} of its genuine clips)")
    print()
    if not args.per_sign:
        # Flat floor by default. When LessonManager compares targetConfidence, a
        # wrong sign cannot pass by construction -- it is the target's own
        # probability being thresholded -- so the bar's only remaining job is to
        # reject non-attempts, and the liveness and novelty gates already do that
        # upstream. Measured: flat floor passed 69/84 replayed clips, per-sign
        # p10 passed 61. The extra strictness bought nothing.
        bars = {l: floors[owner[l][0]] for l in labels}
    existing = existing_assets(GESTURE_DIR)

    print(f"{'sign':<20}{'clips':>6}{'median P':>10}{'p10':>8}{'bar':>7}  action")
    plan = []
    for label in sorted(labels):
        vals = per_sign.get(label, [])
        med = np.median(vals) if vals else float("nan")
        p10 = np.percentile(vals, args.percentile) if vals else float("nan")
        bar = bars[label]
        if label in existing:
            path, guid, current = existing[label]
            action = "update bar" if current is None or abs(current - bar) > 0.005 else "unchanged"
        else:
            path = os.path.join(GESTURE_DIR, f"Gesture_{safe_name(label)}.asset")
            guid = stable_guid("gesture/" + label)
            action = "CREATE"
        plan.append((label, path, guid, bar, action))
        print(f"{label:<20}{len(vals):>6}{med:>10.2f}{p10:>8.2f}{bar:>7.2f}  {action}")

    if not args.write:
        print("\nDry run. Re-run with --write to apply.")
        return

    created = updated = 0
    for label, path, guid, bar, action in plan:
        if action == "CREATE":
            os.makedirs(GESTURE_DIR, exist_ok=True)
            with open(path, "w", encoding="utf-8", newline="\r\n") as fh:
                fh.write(ASSET_TEMPLATE.format(
                    script_guid=gesture_guid,
                    name=f"Gesture_{safe_name(label)}",
                    gesture_id=label,
                    display_name=label if len(label) > 1 else f"Letter {label}",
                    required_confidence=f"{bar:.2f}"))
            with open(path + ".meta", "w", encoding="utf-8", newline="\r\n") as fh:
                fh.write(META_TEMPLATE.format(guid=guid))
            created += 1
        elif action == "update bar":
            text = open(path, encoding="utf-8").read()
            text = re.sub(r"^(\s*requiredConfidence:\s*)[0-9.]+$",
                          lambda m: m.group(1) + f"{bar:.2f}", text, flags=re.M)
            with open(path, "w", encoding="utf-8", newline="") as fh:
                fh.write(text)
            updated += 1

    print(f"\n{created} asset(s) created, {updated} bar(s) updated.")

    if args.no_levels:
        return

    # One level per kind: the alphabet, and the phrases.
    guid_for = {label: (existing[label][1] if label in existing else stable_guid("gesture/" + label))
                for label in labels}
    letters = sorted(l for l in labels if len(l) == 1 and l.isalpha())
    phrases = sorted(l for l in labels if l not in letters)

    os.makedirs(LEVEL_DIR, exist_ok=True)
    for num, (name, desc, members) in enumerate([
        ("Level_1_Letters", "Learn the 26 letters of the SASL manual alphabet.", letters),
        ("Level_2_Phrases", "Everyday SASL phrases and greetings.", phrases),
    ], start=1):
        if not members:
            continue
        path = os.path.join(LEVEL_DIR, name + ".asset")
        if os.path.exists(path) and name == "Level_1_Letters":
            print(f"  {name} already exists -- left alone")
            continue
        refs = "".join(f"  - {{fileID: 11400000, guid: {guid_for[m]}, type: 2}}\n" for m in members)
        with open(path, "w", encoding="utf-8", newline="\r\n") as fh:
            fh.write(LEVEL_TEMPLATE.format(
                script_guid=level_guid or "", name=name, level_number=num,
                level_name=name.split("_", 2)[-1].replace("_", " "),
                description=desc, gesture_refs=refs))
        meta = path + ".meta"
        if not os.path.exists(meta):
            with open(meta, "w", encoding="utf-8", newline="\r\n") as fh:
                fh.write(META_TEMPLATE.format(guid=stable_guid("level/" + name)))
        print(f"  wrote {name} with {len(members)} signs")

    print("\nOpen Unity and let it import. Check one asset in the Inspector before "
          "trusting the rest.")


if __name__ == "__main__":
    main()
