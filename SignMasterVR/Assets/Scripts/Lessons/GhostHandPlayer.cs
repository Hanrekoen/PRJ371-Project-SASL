using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SignMasterVR.Lessons
{
    /// <summary>
    /// Plays a recorded "ghost hand" demonstration of a sign, so the learner can
    /// watch the sign before attempting it.
    ///
    /// The clips come from GestureServer/export_ghost_hand.py, which replays what
    /// was actually captured -- 21 hand landmarks per hand, resampled and
    /// smoothed. It is deliberately NOT an avatar: the capture data has no arm or
    /// shoulder in it (unless recorded with webcam_capture_full.html), so a rigged
    /// arm would have to be invented, and an invented demonstration is worse than
    /// none in a tutor.
    ///
    /// SETUP
    ///   1. Copy the exported folder to Assets/StreamingAssets/GhostHands/
    ///      (file names are the sign with non-alphanumerics replaced by "_",
    ///       e.g. "Thank you" -> Thank_you.json)
    ///   2. Drop this component on an empty GameObject where the demo should
    ///      appear -- typically a child of the tutor panel. Everything is drawn
    ///      in this object's LOCAL space, so move/rotate/scale the parent to
    ///      place it.
    ///   3. Assign a material (any unlit colour material works).
    ///
    /// HOOKING IT UP
    /// One line in LessonManager.LoadCurrentGesture():
    ///     ghostHand.Play(gesture.gestureId);
    /// and one in GiveFeedbackAndAdvance() if you want it replayed on a wrong
    /// attempt. Nothing else changes.
    /// </summary>
    public class GhostHandPlayer : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Folder under StreamingAssets holding the exported clips.")]
        public string clipFolder = "GhostHands";

        [Tooltip("Play this sign on Start, for testing in the Editor. Leave empty in a real lesson.")]
        public string previewSignOnStart = "";

        [Header("Appearance")]
        [Tooltip("Unlit material for the bones. One is enough -- colours are set per renderer.")]
        public Material lineMaterial;
        public Color dominantColor = new Color(0.43f, 0.91f, 1f, 1f);
        public Color supportColor = new Color(0.62f, 0.70f, 0.78f, 0.65f);
        [Tooltip("Metres per normalized unit. With body-normalized clips one unit is the signer's shoulder width.")]
        public float scale = 0.35f;
        public float boneWidth = 0.006f;

        [Header("Playback")]
        public bool loop = true;
        [Tooltip("Seconds to hold still between loops, so each repetition reads as a separate demonstration.")]
        public float loopPause = 0.6f;
        [Range(0.25f, 2f)] public float speed = 1f;
        [Tooltip("Hide the ghost when nothing is playing.")]
        public bool hideWhenIdle = true;

        public bool IsPlaying { get; private set; }
        public string CurrentSign { get; private set; }

        // ---- loaded clip ----
        [Serializable] private class Frame { public float[][] dominant; public float[][] support; }
        private class Clip
        {
            public string sign;
            public int fps;
            public int[][] connections;
            public List<Vector3[]> dominant = new List<Vector3[]>();
            public List<Vector3[]> support = new List<Vector3[]>();
            public bool twoHanded;
        }

        private readonly Dictionary<string, Clip> _cache = new Dictionary<string, Clip>();
        private Clip _clip;
        private LineRenderer[] _domBones, _supBones;
        private Coroutine _playRoutine;

        private void Start()
        {
            if (!string.IsNullOrEmpty(previewSignOnStart)) Play(previewSignOnStart);
            else SetVisible(false);
        }

        /// <summary>Play the demonstration for a gestureId. Safe to call with an
        /// id that has no clip -- it just hides the ghost and logs once.</summary>
        public void Play(string gestureId)
        {
            if (_playRoutine != null) { StopCoroutine(_playRoutine); _playRoutine = null; }
            CurrentSign = gestureId;
            _playRoutine = StartCoroutine(LoadThenPlay(gestureId));
        }

        public void Stop()
        {
            if (_playRoutine != null) { StopCoroutine(_playRoutine); _playRoutine = null; }
            IsPlaying = false;
            SetVisible(false);
        }

        private IEnumerator LoadThenPlay(string gestureId)
        {
            Clip clip;
            if (!_cache.TryGetValue(gestureId, out clip))
            {
                yield return StartCoroutine(Load(gestureId, c => clip = c));
                if (clip == null)
                {
                    Debug.LogWarning($"[GhostHandPlayer] No clip for '{gestureId}'. Expected " +
                                     $"{Path.Combine(Application.streamingAssetsPath, clipFolder, SafeName(gestureId))}.json " +
                                     $"— run export_ghost_hand.py and copy the folder into StreamingAssets.");
                    SetVisible(false);
                    yield break;
                }
                _cache[gestureId] = clip;
            }

            _clip = clip;
            BuildRenderers();
            SetVisible(true);
            IsPlaying = true;

            do
            {
                float t = 0f;
                float frameTime = 1f / Mathf.Max(1, _clip.fps);
                int n = _clip.dominant.Count;
                for (int i = 0; i < n; i++)
                {
                    ApplyFrame(i);
                    t = 0f;
                    while (t < frameTime)
                    {
                        t += Time.deltaTime * Mathf.Max(0.01f, speed);
                        yield return null;
                    }
                }
                if (loop && loopPause > 0f) yield return new WaitForSeconds(loopPause);
            } while (loop);

            IsPlaying = false;
            if (hideWhenIdle) SetVisible(false);
            _playRoutine = null;
        }

        private static string SafeName(string sign)
        {
            var chars = sign.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars).Trim('_');
        }

        private IEnumerator Load(string gestureId, Action<Clip> done)
        {
            string path = Path.Combine(Application.streamingAssetsPath, clipFolder, SafeName(gestureId) + ".json");
            string json = null;

            // On Android (Quest) StreamingAssets lives inside the APK, so it has
            // to be read through UnityWebRequest rather than File.IO.
            if (path.Contains("://"))
            {
                using (var req = UnityWebRequest.Get(path))
                {
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success) json = req.downloadHandler.text;
                }
            }
            else if (File.Exists(path))
            {
                json = File.ReadAllText(path);
            }

            if (string.IsNullOrEmpty(json)) { done(null); yield break; }
            done(Parse(json));
        }

        /// <summary>Minimal reader for the exported format. Unity's JsonUtility
        /// can't do jagged float[][], so the arrays are walked directly.</summary>
        private Clip Parse(string json)
        {
            var clip = new Clip { fps = 30 };
            clip.sign = ReadString(json, "\"sign\":");
            clip.fps = Mathf.Max(1, (int)ReadNumber(json, "\"fps\":", 30));
            clip.connections = ReadConnections(json);

            int idx = json.IndexOf("\"frames\":", StringComparison.Ordinal);
            if (idx < 0) return null;
            int i = json.IndexOf('[', idx);
            int depth = 0;
            var frames = new List<string>();
            int start = -1;
            for (; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '{') { if (depth == 0) start = i; depth++; }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0) { frames.Add(json.Substring(start, i - start + 1)); start = -1; }
                }
                else if (ch == ']' && depth == 0) break;
            }

            foreach (var f in frames)
            {
                clip.dominant.Add(ReadHand(f, "\"dominant\":"));
                var sup = ReadHand(f, "\"support\":");
                clip.support.Add(sup);
                if (sup != null) clip.twoHanded = true;
            }
            return clip.dominant.Count > 0 ? clip : null;
        }

        private static string ReadString(string s, string key)
        {
            int i = s.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";
            i = s.IndexOf('"', i + key.Length);
            int j = s.IndexOf('"', i + 1);
            return (i < 0 || j < 0) ? "" : s.Substring(i + 1, j - i - 1);
        }

        private static float ReadNumber(string s, string key, float fallback)
        {
            int i = s.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return fallback;
            i += key.Length;
            int j = i;
            while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.' || s[j] == '-' || s[j] == '+')) j++;
            float v;
            return float.TryParse(s.Substring(i, j - i), System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static int[][] ReadConnections(string s)
        {
            int i = s.IndexOf("\"connections\":", StringComparison.Ordinal);
            if (i < 0) return new int[0][];
            int open = s.IndexOf('[', i), close = s.IndexOf(']', open);
            var pairs = new List<int[]>();
            int p = open;
            while (true)
            {
                int a = s.IndexOf('[', p + 1);
                if (a < 0) break;
                int b = s.IndexOf(']', a);
                if (b < 0) break;
                var parts = s.Substring(a + 1, b - a - 1).Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), out int x) && int.TryParse(parts[1].Trim(), out int y))
                    pairs.Add(new[] { x, y });
                p = b;
                if (s.IndexOf(']', p + 1) == p + 1) break;      // end of the outer array
                if (pairs.Count > 64) break;
            }
            return pairs.ToArray();
        }

        private static Vector3[] ReadHand(string frameJson, string key)
        {
            int i = frameJson.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            int j = i + key.Length;
            while (j < frameJson.Length && frameJson[j] == ' ') j++;
            if (j < frameJson.Length && frameJson[j] == 'n') return null;   // null

            var pts = new List<Vector3>();
            int p = j;
            while (true)
            {
                int a = frameJson.IndexOf('[', p + 1);
                if (a < 0) break;
                int b = frameJson.IndexOf(']', a);
                if (b < 0) break;
                var parts = frameJson.Substring(a + 1, b - a - 1).Split(',');
                if (parts.Length >= 2)
                {
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float x);
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float y);
                    float z = 0f;
                    if (parts.Length >= 3)
                        float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out z);
                    // Image y grows downward, Unity's grows upward.
                    pts.Add(new Vector3(x, -y, z));
                }
                p = b;
                if (pts.Count >= 21) break;
            }
            return pts.Count == 21 ? pts.ToArray() : null;
        }

        // ---- rendering ----
        private void BuildRenderers()
        {
            int need = _clip.connections.Length;
            if (_domBones != null && _domBones.Length == need) return;

            ClearRenderers();
            _domBones = new LineRenderer[need];
            _supBones = new LineRenderer[need];
            for (int i = 0; i < need; i++)
            {
                _domBones[i] = MakeBone("dom_" + i, dominantColor, boneWidth);
                _supBones[i] = MakeBone("sup_" + i, supportColor, boneWidth * 0.8f);
            }
        }

        private LineRenderer MakeBone(string name, Color colour, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.startWidth = lr.endWidth = width;
            lr.numCapVertices = 4;
            lr.material = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = colour;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void ClearRenderers()
        {
            if (_domBones != null) foreach (var b in _domBones) if (b) Destroy(b.gameObject);
            if (_supBones != null) foreach (var b in _supBones) if (b) Destroy(b.gameObject);
            _domBones = _supBones = null;
        }

        private void ApplyFrame(int i)
        {
            ApplyHand(_domBones, _clip.dominant[i]);
            ApplyHand(_supBones, i < _clip.support.Count ? _clip.support[i] : null);
        }

        private void ApplyHand(LineRenderer[] bones, Vector3[] pts)
        {
            if (bones == null) return;
            bool show = pts != null;
            for (int b = 0; b < bones.Length; b++)
            {
                if (!show) { bones[b].enabled = false; continue; }
                var c = _clip.connections[b];
                bones[b].enabled = true;
                bones[b].SetPosition(0, pts[c[0]] * scale);
                bones[b].SetPosition(1, pts[c[1]] * scale);
            }
        }

        private void SetVisible(bool on)
        {
            if (_domBones != null) foreach (var b in _domBones) if (b) b.enabled = on;
            if (_supBones != null) foreach (var b in _supBones) if (b) b.enabled = on;
        }

        private void OnDestroy() => ClearRenderers();
    }
}
