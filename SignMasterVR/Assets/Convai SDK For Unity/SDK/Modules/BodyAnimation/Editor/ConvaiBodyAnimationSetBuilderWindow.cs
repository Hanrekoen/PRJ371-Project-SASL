using System;
using System.Collections.Generic;
using System.IO;
using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     The "Create Animation Set" wizard: point it at a folder of conventionally named
    ///     clips, review the auto-matched proposals, then build. This is the tool that turns a future
    ///     character archetype (male, creature, …) into a one-click set instead of hand-filling 26
    ///     locomotion slots and 15 pointing directions.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Open" /> is called by the Body Animation Editor window's Content mode
    ///         ("Create Animation Set…" button) — keep its signature stable. <see cref="OpenFor" />
    ///         targets an existing set — which is also how the shipped female animation folder is
    ///         treated, exactly as if it were third-party content.
    ///     </para>
    ///     <para>
    ///         Every write goes through <see cref="BodyAnimationSetBuilder.Build" />, which folds mask
    ///         generation and clip metadata analysis into the same call — this window never gives the
    ///         user a way to skip either step, which is the structural fix for the "forgot to run the
    ///         analyzer" foot-slide failure mode.
    ///     </para>
    /// </remarks>
    internal sealed class ConvaiBodyAnimationSetBuilderWindow : EditorWindow
    {
        private const string PreviewClipPrefix = "__preview__";

        #region Cached content

        private static readonly GUIContent HeroTitleContent = new("Create Animation Set");

        private static readonly GUIContent HeroSubtitleContent = new(
            "Point this at a folder of Humanoid clips named with the Convai convention and review what it proposes. " +
            "Nothing is written until you press Build.");

        private static readonly GUIContent TargetHeaderContent = new("Target");
        private static readonly GUIContent SourceHeaderContent = new("Clip Source");
        private static readonly GUIContent MatchingHeaderContent = new("Matching");
        private static readonly GUIContent BuildHeaderContent = new("Build");
        private static readonly GUIContent ReportHeaderContent = new("Report");

        private static readonly GUIContent ExistingSetLabel = new(
            "Existing Set (optional)", "Leave empty to create a new set, or assign one to merge clips into it.");

        private static readonly GUIContent DisplayNameLabel = new("Display Name");
        private static readonly GUIContent NewSetPathLabel = new("New Set Path");
        private static readonly GUIContent FolderLabel = new("Folder");

        private static readonly GUIContent BrowseContent = new("Browse…");
        private static readonly GUIContent ScanFolderContent = new("Scan Folder");
        private static readonly GUIContent AddSelectedContent = new("Add Selected Clips");
        private static readonly GUIContent ClearContent = new("Clear");
        private static readonly GUIContent BuildSetContent = new("Build Animation Set");
        private static readonly GUIContent PingSetContent = new("Ping Set");
        private static readonly GUIContent CreateProfileContent = new("Create Matching Profile");

        private static readonly GUIContent FixImportSettingsContent = new(
            "Fix Clip Import Settings",
            "Normalizes root-motion locking, take naming, and strips stray facial curves for every clip " +
            "under the source folder. Loop flags are tuned for the Convai naming convention " +
            "(Walk/Jog/Idle/Talk/…) — review loop settings on custom-named clips afterward.");

        private static readonly GUIContent ScratchUnmatched = new("Pick a category to include this clip.");

        // Reused for per-row labels whose text is computed per draw.
        private static readonly GUIContent ScratchButton = new();
        private static readonly GUIContent ScratchClipLabel = new();
        private static readonly GUIContent ScratchBadge = new();

        #endregion

        private Vector2 _pageScroll;

        private ConvaiBodyAnimationSet _existingSet;
        private string _displayName = "New Character";
        private string _newSetAssetPath = "Assets/ConvaiBodyAnimationSet.asset";
        private DefaultAsset _sourceFolder;

        private readonly List<AnimationClip> _clips = new();
        private readonly List<BodyAnimationClipProposal> _proposals = new();
        private readonly List<string> _report = new();

        private Vector2 _proposalsScroll;
        private Vector2 _reportScroll;
        private bool _built;
        private string[] _pointingDirectionLabels;

        /// <summary>Opens the wizard for a brand new set. Called by the Body Animation Editor window's Content mode.</summary>
        internal static void Open()
        {
            ConvaiBodyAnimationSetBuilderWindow window = GetWindow<ConvaiBodyAnimationSetBuilderWindow>(false, "Create Animation Set");
            window._existingSet = null;
            window.minSize = new Vector2(680f, 480f);
            window.Show();
        }

        /// <summary>Opens the wizard targeting <paramref name="existingSet" /> — matched clips fill gaps in an already-authored set rather than creating a new one.</summary>
        internal static void OpenFor(ConvaiBodyAnimationSet existingSet)
        {
            ConvaiBodyAnimationSetBuilderWindow window = GetWindow<ConvaiBodyAnimationSetBuilderWindow>(false, "Create Animation Set");
            window._existingSet = existingSet;
            if (existingSet != null) window._displayName = existingSet.DisplayName;
            window.minSize = new Vector2(680f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            IReadOnlyList<string> directions = BodyAnimationClipMatcher.PointingDirections;
            _pointingDirectionLabels = new string[directions.Count];
            for (int i = 0; i < directions.Count; i++) _pointingDirectionLabels[i] = directions[i];
        }

        private void OnGUI()
        {
            ConvaiEditorTheme.EnsureStyles();
            ConvaiEditorTheme.Fill(new Rect(0f, 0f, position.width, position.height), ConvaiEditorTheme.WindowBg);

            DrawHeroBand();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_pageScroll))
            {
                _pageScroll = scroll.scrollPosition;
                using (new EditorGUILayout.VerticalScope(ConvaiEditorStyles.PaneContent))
                {
                    DrawTargetSection();
                    DrawSourceSection();

                    if (_clips.Count > 0)
                        DrawProposalsSection();

                    DrawBuildSection();

                    if (_report.Count > 0)
                        DrawReportSection();
                }
            }
        }

        /// <summary>Convai hero band — the same window-opening language the module windows use.</summary>
        private void DrawHeroBand() =>
            ConvaiEditorTheme.WindowHero(position.width, HeroTitleContent, HeroSubtitleContent);

        // ------------------------------------------------------------------ target

        private void DrawTargetSection()
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Identity, TargetHeaderContent);

                _existingSet = (ConvaiBodyAnimationSet)EditorGUILayout.ObjectField(
                    ExistingSetLabel, _existingSet, typeof(ConvaiBodyAnimationSet), false);

                if (_existingSet == null)
                {
                    _displayName = EditorGUILayout.TextField(DisplayNameLabel, _displayName);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _newSetAssetPath = EditorGUILayout.TextField(NewSetPathLabel, _newSetAssetPath);
                        Rect browse = GUILayoutUtility.GetRect(80f, 18f, GUILayout.Width(80f));
                        if (ConvaiEditorControls.GhostButton(browse, BrowseContent))
                            BrowseForSetPath();
                    }
                }
                else
                {
                    GUILayout.Space(4f);
                    ConvaiEditorFrame.InfoBox(
                        "Adding To An Existing Set",
                        $"Clips are merged into '{_existingSet.DisplayName}'. Locomotion slots are " +
                        "overwritten only when you match a new clip to them; idle, talk, pointing and " +
                        "action clips already present are skipped rather than duplicated.");
                }
            }
        }

        /// <summary>
        ///     Opens the save panel after the current IMGUI pass. The panel is modal, and a modal
        ///     raised from inside a layout scope discards the layout state the enclosing scope is about
        ///     to close, which leaves the window throwing on every later repaint.
        /// </summary>
        private void BrowseForSetPath()
        {
            EditorApplication.delayCall += () =>
            {
                string chosen = EditorUtility.SaveFilePanelInProject(
                    "Create Animation Set", SanitizeFileName(_displayName), "asset",
                    "Choose where the new animation set asset is saved.");
                if (!string.IsNullOrEmpty(chosen))
                {
                    _newSetAssetPath = chosen;
                    Repaint();
                }
            };
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "ConvaiBodyAnimationSet";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            return new string(chars);
        }

        // ------------------------------------------------------------------ clip source

        private void DrawSourceSection()
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Content, SourceHeaderContent);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                        FolderLabel, _sourceFolder, typeof(DefaultAsset), false);

                    using (new EditorGUI.DisabledScope(_sourceFolder == null))
                    {
                        Rect scan = GUILayoutUtility.GetRect(100f, 18f, GUILayout.Width(100f));
                        if (ConvaiEditorControls.GhostButton(scan, ScanFolderContent)) ScanFolder();
                    }
                }

                GUILayout.Space(4f);
                if (ConvaiEditorControls.GhostButtonLayout(AddSelectedContent))
                    AddSelectedClips();

                if (_clips.Count == 0) return;

                GUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{_clips.Count} clip(s) picked up.", ConvaiEditorStyles.CaptionWrapped);
                    GUILayout.FlexibleSpace();
                    Rect clear = GUILayoutUtility.GetRect(64f, 18f, GUILayout.Width(64f));
                    if (ConvaiEditorControls.GhostButton(clear, ClearContent)) ClearClips();
                }
            }
        }

        private void ScanFolder()
        {
            if (_sourceFolder == null) return;
            string folderPath = AssetDatabase.GetAssetPath(_sourceFolder);
            if (!AssetDatabase.IsValidFolder(folderPath)) return;

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
            var found = new HashSet<AnimationClip>();
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith(PreviewClipPrefix, StringComparison.Ordinal))
                        found.Add(clip);
                }
            }

            ClearClips();
            _clips.AddRange(found);
            _clips.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        private void AddSelectedClips()
        {
            AnimationClip[] selected = Selection.GetFiltered<AnimationClip>(SelectionMode.DeepAssets);
            if (selected.Length == 0) return;

            var existing = new HashSet<AnimationClip>(_clips);
            for (int i = 0; i < selected.Length; i++)
            {
                AnimationClip clip = selected[i];
                if (clip.name.StartsWith(PreviewClipPrefix, StringComparison.Ordinal)) continue;
                if (existing.Add(clip)) _clips.Add(clip);
            }
            _clips.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        private void ClearClips()
        {
            _clips.Clear();
            _proposals.Clear();
            _report.Clear();
            _built = false;
        }

        // ------------------------------------------------------------------ matching

        private void DrawProposalsSection()
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Discovery, MatchingHeaderContent);

                ScratchButton.text = $"Match {_clips.Count} Clip(s) By Name";
                if (ConvaiEditorControls.GhostButtonLayout(ScratchButton))
                {
                    BodyAnimationClipMatcher.MatchAll(_clips, _proposals);
                    _report.Clear();
                    _built = false;
                }

                if (_proposals.Count == 0) return;

                int unmatched = CountByCategory(BodyAnimationSlotCategory.Unmatched);
                if (unmatched > 0)
                {
                    ConvaiEditorFrame.WarningBox(
                        "Some Clips Were Not Recognised",
                        $"{unmatched} clip(s) could not be matched by name. They are listed below and " +
                        "excluded from the build — assign a category to include one, or leave it out.");
                }

                GUILayout.Space(2f);
                _proposalsScroll = EditorGUILayout.BeginScrollView(_proposalsScroll, GUILayout.MaxHeight(320f));
                for (int i = 0; i < _proposals.Count; i++)
                    DrawProposalRow(_proposals[i]);
                EditorGUILayout.EndScrollView();
            }
        }

        private int CountByCategory(BodyAnimationSlotCategory category)
        {
            int count = 0;
            for (int i = 0; i < _proposals.Count; i++)
            {
                if (_proposals[i].Category == category) count++;
            }
            return count;
        }

        private void DrawProposalRow(BodyAnimationClipProposal proposal)
        {
            using (ConvaiEditorFrame.Panel(null, 3f))
            using (new EditorGUILayout.HorizontalScope())
            {
                proposal.Included = EditorGUILayout.Toggle(proposal.Included, GUILayout.Width(18f));

                ScratchClipLabel.text = proposal.Clip != null ? proposal.Clip.name : "(missing clip)";
                ScratchClipLabel.tooltip = proposal.Reason;
                EditorGUILayout.LabelField(ScratchClipLabel, ConvaiEditorStyles.CardName, GUILayout.Width(190f));

                EditorGUI.BeginChangeCheck();
                var newCategory = (BodyAnimationSlotCategory)EditorGUILayout.EnumPopup(proposal.Category, GUILayout.Width(100f));
                if (EditorGUI.EndChangeCheck())
                {
                    proposal.Category = newCategory;
                    proposal.IsOverridden = true;
                    if (newCategory != BodyAnimationSlotCategory.Unmatched) proposal.Included = true;
                }

                DrawCategoryFields(proposal);

                GUILayout.FlexibleSpace();
                DrawConfidenceBadge(proposal);
            }
        }

        private void DrawCategoryFields(BodyAnimationClipProposal proposal)
        {
            switch (proposal.Category)
            {
                case BodyAnimationSlotCategory.Locomotion:
                    EditorGUI.BeginChangeCheck();
                    var newSlot = (BodyAnimationLocomotionSlot)EditorGUILayout.EnumPopup(proposal.LocomotionSlot, GUILayout.Width(160f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        proposal.LocomotionSlot = newSlot;
                        proposal.IsOverridden = true;
                    }
                    break;

                case BodyAnimationSlotCategory.Pointing:
                    int currentIndex = Mathf.Max(0, IndexOfDirection(proposal.PointingDirection));
                    EditorGUI.BeginChangeCheck();
                    int newIndex = EditorGUILayout.Popup(currentIndex, _pointingDirectionLabels, GUILayout.Width(60f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        string direction = _pointingDirectionLabels[newIndex];
                        BodyAnimationClipMatcher.TryResolvePointingDirection(direction, out float yaw, out float pitch);
                        proposal.PointingDirection = direction;
                        proposal.PointingYaw = yaw;
                        proposal.PointingPitch = pitch;
                        proposal.IsOverridden = true;
                    }
                    break;

                case BodyAnimationSlotCategory.Action:
                    EditorGUI.BeginChangeCheck();
                    string newName = EditorGUILayout.TextField(proposal.ActionName, GUILayout.Width(110f));
                    var newMask = (ActionMaskMode)EditorGUILayout.EnumPopup(proposal.ActionMaskMode, GUILayout.Width(90f));
                    var newLoop = (ActionLoopMode)EditorGUILayout.EnumPopup(proposal.ActionLoopMode, GUILayout.Width(120f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        proposal.ActionName = newName;
                        proposal.ActionMaskMode = newMask;
                        proposal.ActionLoopMode = newLoop;
                        proposal.IsOverridden = true;
                    }
                    break;

                case BodyAnimationSlotCategory.Unmatched:
                    ScratchUnmatched.tooltip = proposal.Reason;
                    EditorGUILayout.LabelField(
                        ScratchUnmatched, ConvaiEditorStyles.CaptionWrapped, GUILayout.Width(280f));
                    break;
            }
        }

        private int IndexOfDirection(string direction)
        {
            if (_pointingDirectionLabels == null || string.IsNullOrEmpty(direction)) return 0;
            for (int i = 0; i < _pointingDirectionLabels.Length; i++)
            {
                if (string.Equals(_pointingDirectionLabels[i], direction, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        /// <summary>
        ///     How confident the name match was, as a tinted pill — so a row scan reads as
        ///     "these are certain, these need a look" without reading any text.
        /// </summary>
        private static void DrawConfidenceBadge(BodyAnimationClipProposal proposal)
        {
            (string label, Color tint) = proposal.IsOverridden
                ? ("Edited", ConvaiEditorTheme.StatusInfo)
                : proposal.Confidence switch
                {
                    BodyAnimationMatchConfidence.High => ("Match", ConvaiEditorTheme.StatusReady),
                    BodyAnimationMatchConfidence.Medium => ("Guess", ConvaiEditorTheme.StatusWarn),
                    BodyAnimationMatchConfidence.Low => ("Review", ConvaiEditorTheme.StatusWarn),
                    _ => ("Unrecognised", ConvaiEditorTheme.StatusIdle)
                };

            ScratchBadge.text = label;
            ScratchBadge.tooltip = proposal.Reason;
            Rect rect = GUILayoutUtility.GetRect(84f, 18f, GUILayout.Width(84f));
            rect.y += 1f;
            rect.height = 16f;
            ConvaiEditorControls.Pill(rect, ScratchBadge, tint);
        }

        // ------------------------------------------------------------------ build

        private void DrawBuildSection()
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Run, BuildHeaderContent);

                using (new EditorGUI.DisabledScope(_sourceFolder == null))
                {
                    if (ConvaiEditorControls.GhostButtonLayout(FixImportSettingsContent))
                        FixClipImportSettings();
                }

                GUILayout.Space(6f);

                using (new EditorGUI.DisabledScope(!CanBuild()))
                {
                    if (ConvaiEditorControls.PrimaryButtonLayout(BuildSetContent))
                        RunBuild();
                }

                if (!CanBuild() && _proposals.Count > 0)
                {
                    GUILayout.Space(4f);
                    GUILayout.Label(
                        "No proposal is included yet — nothing would be written.",
                        ConvaiEditorStyles.CaptionWrapped);
                }
            }
        }

        private void FixClipImportSettings()
        {
            string folderPath = AssetDatabase.GetAssetPath(_sourceFolder);
            int changed = BodyAnimationImportNormalizer.Normalize(folderPath);
            _report.Clear();
            _report.Add($"Fixed import settings on {changed} FBX file(s) under '{folderPath}'.");
        }

        private bool CanBuild()
        {
            if (_proposals.Count == 0) return false;
            for (int i = 0; i < _proposals.Count; i++)
            {
                if (_proposals[i].Included) return true;
            }
            return false;
        }

        private void RunBuild()
        {
            var request = new BodyAnimationSetBuildRequest
            {
                ExistingSet = _existingSet,
                NewSetAssetPath = _newSetAssetPath,
                DisplayName = _displayName,
                Proposals = _proposals
            };

            _report.Clear();
            ConvaiBodyAnimationSet built = BodyAnimationSetBuilder.Build(request, _report);
            _built = built != null;
            if (built != null)
            {
                _existingSet = built;
                EditorGUIUtility.PingObject(built);
            }
        }

        // ------------------------------------------------------------------ report

        private void DrawReportSection()
        {
            using (ConvaiEditorFrame.Card())
            {
                ConvaiEditorFrame.SectionHeader(ConvaiEditorGlyphs.Validation, ReportHeaderContent);

                _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.MaxHeight(160f));
                for (int i = 0; i < _report.Count; i++)
                    GUILayout.Label("• " + _report[i], ConvaiEditorStyles.MutedWrapped);
                EditorGUILayout.EndScrollView();

                if (!_built || _existingSet == null) return;

                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect ping = GUILayoutUtility.GetRect(100f, 20f, GUILayout.Width(100f));
                    if (ConvaiEditorControls.GhostButton(ping, PingSetContent))
                        EditorGUIUtility.PingObject(_existingSet);

                    GUILayout.Space(6f);
                    Rect profile = GUILayoutUtility.GetRect(180f, 20f, GUILayout.Width(180f));
                    if (ConvaiEditorControls.GhostButton(profile, CreateProfileContent))
                        CreateMatchingProfile();

                    GUILayout.FlexibleSpace();
                }
            }
        }

        /// <summary>Emits a <see cref="ConvaiBodyAnimationProfile" /> next to the built set. Config is left unassigned — the controller falls back to SDK runtime defaults, as it already does for any profile with no config.</summary>
        private void CreateMatchingProfile()
        {
            if (_existingSet == null) return;

            string setPath = AssetDatabase.GetAssetPath(_existingSet);
            string directory = Path.GetDirectoryName(setPath)?.Replace('\\', '/') ?? string.Empty;
            string profilePath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{SanitizeFileName(_existingSet.DisplayName)}_Profile.asset");

            var profile = ScriptableObject.CreateInstance<ConvaiBodyAnimationProfile>();
            profile.Initialize(_existingSet, null);
            AssetDatabase.CreateAsset(profile, profilePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(profile);
            _report.Add($"Created a matching profile at '{profilePath}'.");
        }
    }
}
