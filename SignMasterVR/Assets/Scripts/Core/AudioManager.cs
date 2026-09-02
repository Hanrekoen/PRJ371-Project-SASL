using System;
using System.Collections.Generic;
using UnityEngine;

namespace SignMasterVR.Core
{
    /// <summary>
    /// PHASE 21 — Single place that plays pre-recorded tutor/UI audio by key.
    /// Populate `clips` in the Inspector as recordings come in. A missing key
    /// just logs and does nothing — safe to call before any audio exists.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Serializable]
        public struct ClipEntry
        {
            public string key;
            public AudioClip clip;
        }

        public List<ClipEntry> clips = new List<ClipEntry>();
        public AudioSource source;

        private Dictionary<string, AudioClip> _lookup;

        private void Awake()
        {
            Instance = this;
            _lookup = new Dictionary<string, AudioClip>();
            foreach (var entry in clips)
                if (!string.IsNullOrEmpty(entry.key) && entry.clip != null)
                    _lookup[entry.key] = entry.clip;
        }

        public void Play(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (source == null) { Debug.LogWarning("[AudioManager] No AudioSource assigned."); return; }
            if (_lookup.TryGetValue(key, out var clip)) source.PlayOneShot(clip);
            else Debug.Log($"[AudioManager] No clip registered for key \"{key}\" yet — skipping.");
        }
    }
}
