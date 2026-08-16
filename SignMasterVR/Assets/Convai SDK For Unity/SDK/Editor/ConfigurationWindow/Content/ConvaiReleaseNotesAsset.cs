using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Editor.ConfigurationWindow.Content
{
    /// <summary>
    ///     Local release notes store for the configuration window Updates section.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiReleaseNotes", menuName = "Convai/Release Notes Asset")]
    public sealed class ConvaiReleaseNotesAsset : ScriptableObject
    {
        private const string ResourcePath = "ConvaiReleaseNotes";

        private static ConvaiReleaseNotesAsset _instance;

        [SerializeField] private List<ReleaseNoteEntry> _entries = new()
        {
            new ReleaseNoteEntry
            {
                Version = "0.1.0-beta.1",
                ReleaseDate = "2026-02-05",
                Summary = "First major WebRTC beta release for the Unity core SDK.",
                Highlights = new List<string>
                {
                    "New architecture split across Domain, Application, Runtime, and Infrastructure layers.",
                    "Improved setup and validation tooling for faster scene integration.",
                    "Operational dashboard refactor in progress (Updates section remains gated)."
                }
            }
        };

        /// <summary>Gets the singleton release-notes asset instance.</summary>
        public static ConvaiReleaseNotesAsset Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<ConvaiReleaseNotesAsset>(ResourcePath);
                    if (_instance == null) _instance = CreateInstance<ConvaiReleaseNotesAsset>();
                }

                return _instance;
            }
        }

        /// <summary>Gets release-note entries in newest-first order.</summary>
        public IReadOnlyList<ReleaseNoteEntry> Entries => _entries;

        /// <summary>
        ///     One release-note entry.
        /// </summary>
        [Serializable]
        public sealed class ReleaseNoteEntry
        {
            [SerializeField] private string _version;
            [SerializeField] private string _releaseDate;
            [SerializeField] private string _summary;
            [SerializeField] private List<string> _highlights = new();

            /// <summary>Version identifier (for example: 0.1.0-beta.1).</summary>
            public string Version
            {
                get => _version;
                set => _version = value;
            }

            /// <summary>Release date in YYYY-MM-DD format.</summary>
            public string ReleaseDate
            {
                get => _releaseDate;
                set => _releaseDate = value;
            }

            /// <summary>Short summary text.</summary>
            public string Summary
            {
                get => _summary;
                set => _summary = value;
            }

            /// <summary>Bullet highlights.</summary>
            public List<string> Highlights
            {
                get => _highlights;
                set => _highlights = value;
            }
        }
    }
}
