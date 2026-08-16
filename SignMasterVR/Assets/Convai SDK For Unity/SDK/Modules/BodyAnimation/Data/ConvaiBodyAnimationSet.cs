using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     The single authoring asset for a character's body animation content: idle and talk
    ///     variants, locomotion clips, named actions/gestures, directional pointing, and the
    ///     avatar masks the layers blend with. Sets are swappable at runtime, so one set per
    ///     character archetype (female, male, creature, …) is the intended workflow.
    /// </summary>
    /// <remarks>
    ///     Every section is optional. Missing content never breaks the runtime — each feature
    ///     that lacks clips deactivates itself and logs the degradation once at startup.
    ///     Use <see cref="CollectIssues" /> (surfaced by the custom inspector) to audit a set.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ConvaiBodyAnimationSet",
        menuName = "Convai/Embodiment/Body Animation Set",
        order = 141)]
    public sealed class ConvaiBodyAnimationSet : ScriptableObject
    {
        internal const int CurrentSchemaVersion = 3;
        [SerializeField, HideInInspector] private int _schemaVersion = CurrentSchemaVersion;
        internal int SchemaVersion => _schemaVersion;
        // No [Header] on serialized fields: the Convai inspector groups these into its own
        // sections, and a Header decorator would draw a second, unstyled title inside them.
        [SerializeField]
        [Tooltip("Human-readable label used in logs and HUDs (e.g. \"Female\").")]
        private string _displayName = "Unnamed Set";

        [SerializeField] private List<IdleEntry> _idles = new();

        [SerializeField] private List<TalkEntry> _talks = new();

        [SerializeField]
        [Tooltip("Looping poses the talk layer plays while the player is speaking and the " +
                 "character is Listening/Attending (conversational state acting). " +
                 "Same weighted/emotion-aware selection as Talk. Optional — an empty list " +
                 "means the layer releases instead of playing a listen pose.")]
        private List<TalkEntry> _listens = new();

        [SerializeField]
        [Tooltip("Looping poses the talk layer plays during the LLM-latency Thinking beat " +
                 "after ThinkingEnterDelaySeconds. Same weighted/emotion-aware " +
                 "selection as Talk. Optional — an empty list means the layer releases " +
                 "instead of playing a think pose.")]
        private List<TalkEntry> _thinks = new();

        [SerializeField] private LocomotionSection _locomotion = new();

        [SerializeField] private List<ActionEntry> _actions = new();

        [SerializeField] private PointingSection _pointing = new();

        [SerializeField]
        [Tooltip("Mask used by upper-body talk, gestures, and pointing. Legs and root must be disabled.")]
        private AvatarMask _upperBodyMask;

        private Dictionary<string, ActionEntry> _actionLookup;

        public string DisplayName => _displayName;
        public IReadOnlyList<IdleEntry> Idles => _idles;
        public IReadOnlyList<TalkEntry> Talks => _talks;

        /// <summary>Looping poses played while the character is Listening/Attending.</summary>
        public IReadOnlyList<TalkEntry> Listens => _listens;

        /// <summary>Looping poses played during the Thinking beat, after the enter-delay gate.</summary>
        public IReadOnlyList<TalkEntry> Thinks => _thinks;

        public LocomotionSection Locomotion => _locomotion;
        public IReadOnlyList<ActionEntry> Actions => _actions;
        public PointingSection Pointing => _pointing;
        public AvatarMask UpperBodyMask => _upperBodyMask;

        public bool HasAnyIdle
        {
            get
            {
                for (int i = 0; i < _idles.Count; i++)
                {
                    if (_idles[i].IsValid) return true;
                }
                return false;
            }
        }

        public bool HasAnyTalk
        {
            get
            {
                for (int i = 0; i < _talks.Count; i++)
                {
                    if (_talks[i].IsValid) return true;
                }
                return false;
            }
        }

        /// <summary>True while at least one Listen entry has a clip and non-zero weight.</summary>
        public bool HasAnyListen
        {
            get
            {
                for (int i = 0; i < _listens.Count; i++)
                {
                    if (_listens[i].IsValid) return true;
                }
                return false;
            }
        }

        /// <summary>True while at least one Think entry has a clip and non-zero weight.</summary>
        public bool HasAnyThink
        {
            get
            {
                for (int i = 0; i < _thinks.Count; i++)
                {
                    if (_thinks[i].IsValid) return true;
                }
                return false;
            }
        }

        public bool HasAnyAction
        {
            get
            {
                for (int i = 0; i < _actions.Count; i++)
                {
                    if (_actions[i].IsValid) return true;
                }
                return false;
            }
        }

        /// <summary>
        ///     Resolves an action by name or alias (case-insensitive, separator-insensitive).
        ///     The lookup table is built lazily and invalidated on inspector edits.
        /// </summary>
        public bool TryGetAction(string nameOrAlias, out ActionEntry entry)
        {
            entry = null;
            string key = ActionEntry.NormalizeName(nameOrAlias);
            if (string.IsNullOrEmpty(key)) return false;

            _actionLookup ??= BuildActionLookup();
            return _actionLookup.TryGetValue(key, out entry);
        }

        /// <summary>
        ///     Resolves the first authored action tagged with <paramref name="cue" /> (list
        ///     order, deterministic). Returns <c>false</c> for <see cref="GestureCueKind.None" />
        ///     or when no entry is tagged with a matching, valid cue.
        /// </summary>
        internal bool TryGetActionForCue(GestureCueKind cue, out ActionEntry entry)
        {
            entry = null;
            if (cue == GestureCueKind.None) return false;

            for (int i = 0; i < _actions.Count; i++)
            {
                ActionEntry candidate = _actions[i];
                if (candidate.Cue != cue || !candidate.IsValid) continue;

                entry = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Appends every authoring problem (missing clips, duplicate action names, non-looping
        ///     loop clips, missing masks, …) to <paramref name="findings" />, each carrying a
        ///     stable id, a severity assigned at the point it is raised (never inferred from
        ///     <see cref="BodyAnimationFinding.Message" />), and a fix id where a mechanical repair
        ///     exists. This is the single source of truth <see cref="CollectIssues" /> and
        ///     <see cref="CollectValidationFindings" /> both wrap.
        ///     Returns the number of findings found.
        /// </summary>
        public int CollectFindings(List<BodyAnimationFinding> findings)
        {
            if (findings == null) return 0;
            int before = findings.Count;

            if (!HasAnyIdle)
                findings.Add(new BodyAnimationFinding(
                    "set.idle.missing", BodyAnimationValidationSeverity.Error,
                    "No valid idle entry. The base layer needs at least one looping idle clip."));

            for (int i = 0; i < _idles.Count; i++)
            {
                IdleEntry idle = _idles[i];
                if (idle == null)
                    findings.Add(new BodyAnimationFinding(
                        "set.idle.null", BodyAnimationValidationSeverity.Error, $"Idle[{i}] is null."));
                else if (idle.Clip == null)
                    findings.Add(new BodyAnimationFinding(
                        "set.idle.clipMissing", BodyAnimationValidationSeverity.Warning, $"Idle[{i}] has no clip."));
                else if (!idle.Clip.isLooping)
                    findings.Add(new BodyAnimationFinding(
                        "set.idle.notLooping", BodyAnimationValidationSeverity.ReleaseBlocker,
                        $"Idle[{i}] '{idle.Clip.name}' is not import-set to loop (Loop Time)."));
            }

            CollectTalkEntryFindings(_talks, "Talk", "talk", findings);
            CollectTalkEntryFindings(_listens, "Listen", "listen", findings);
            CollectTalkEntryFindings(_thinks, "Think", "think", findings);

            if (_locomotion.HasMovement)
            {
                if (!_locomotion.Walk.Clip.isLooping)
                    findings.Add(new BodyAnimationFinding(
                        "set.locomotion.walkNotLooping", BodyAnimationValidationSeverity.ReleaseBlocker,
                        $"Locomotion walk '{_locomotion.Walk.ClipName}' is not import-set to loop."));
                if (_locomotion.HasJog && !_locomotion.Jog.Clip.isLooping)
                    findings.Add(new BodyAnimationFinding(
                        "set.locomotion.jogNotLooping", BodyAnimationValidationSeverity.ReleaseBlocker,
                        $"Locomotion jog '{_locomotion.Jog.ClipName}' is not import-set to loop."));
            }

            var seenActionKeys = new Dictionary<string, string>();
            for (int i = 0; i < _actions.Count; i++)
            {
                ActionEntry action = _actions[i];
                if (action == null)
                {
                    findings.Add(new BodyAnimationFinding(
                        "set.action.null", BodyAnimationValidationSeverity.Error, $"Action[{i}] is null."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(action.ActionName))
                {
                    findings.Add(new BodyAnimationFinding(
                        "set.action.nameMissing", BodyAnimationValidationSeverity.Error, $"Action[{i}] has no name."));
                    continue;
                }

                if (action.Clip == null)
                    findings.Add(new BodyAnimationFinding(
                        "set.action.clipMissing", BodyAnimationValidationSeverity.Warning,
                        $"Action[{i}] '{action.ActionName}' has no main clip."));

                if (action.MaskMode == ActionMaskMode.CustomMask && action.CustomMask == null)
                    findings.Add(new BodyAnimationFinding(
                        "set.action.customMaskMissing", BodyAnimationValidationSeverity.Warning,
                        $"Action[{i}] '{action.ActionName}' uses Custom Mask but none is assigned."));

                RegisterActionKey(seenActionKeys, action.ActionName, action.ActionName, i, findings);
                foreach (string alias in action.Aliases)
                    RegisterActionKey(seenActionKeys, alias, action.ActionName, i, findings);
            }

            bool needsUpperBodyMask =
                _upperBodyMask == null &&
                (HasAnyTalk || HasAnyListen || HasAnyThink || _pointing.HasAny || AnyActionUsesUpperBody());
            if (needsUpperBodyMask)
                findings.Add(new BodyAnimationFinding(
                    "set.mask.upperBodyMissing", BodyAnimationValidationSeverity.ReleaseBlocker,
                    "Upper Body Mask is not assigned but talk/pointing/upper-body actions need it.",
                    "GenerateUpperBodyMask"));

            for (int i = 0; i < _pointing.Entries.Count; i++)
            {
                if (_pointing.Entries[i] == null)
                    findings.Add(new BodyAnimationFinding(
                        "set.pointing.null", BodyAnimationValidationSeverity.Error, $"Pointing[{i}] is null."));
                else if (!_pointing.Entries[i].IsValid)
                    findings.Add(new BodyAnimationFinding(
                        "set.pointing.clipMissing", BodyAnimationValidationSeverity.Warning, $"Pointing[{i}] has no clip."));
            }

            return findings.Count - before;
        }

        /// <summary>
        ///     Thin wrapper over <see cref="CollectFindings" /> for callers that only want the
        ///     human-readable messages. Kept for source/behavior compatibility — public API.
        /// </summary>
        public int CollectIssues(List<string> issues)
        {
            if (issues == null) return 0;
            var findings = new List<BodyAnimationFinding>();
            CollectFindings(findings);
            for (int i = 0; i < findings.Count; i++)
                issues.Add(findings[i].Message);
            return findings.Count;
        }

        /// <summary>
        ///     Thin wrapper over <see cref="CollectFindings" /> for callers that want the typed,
        ///     severity-only finding shape. Kept for source/behavior compatibility — public API.
        /// </summary>
        public int CollectValidationFindings(List<BodyAnimationValidationFinding> findings)
        {
            if (findings == null) return 0;
            int before = findings.Count;
            var raw = new List<BodyAnimationFinding>();
            CollectFindings(raw);
            for (int i = 0; i < raw.Count; i++)
                findings.Add(new BodyAnimationValidationFinding(raw[i].Severity, name, raw[i].Message));

            return findings.Count - before;
        }

        /// <summary>
        ///     Shared per-entry validation for Talk/Listen/Think pools: missing clips,
        ///     non-looping clips, a same-pool Additive/non-Additive mode mix, and a
        ///     LOOPING Intro/Outro Clip — brackets are one-shots; a looping intro would only
        ///     hand off to the main loop via the elapsed-time safety net instead of playing
        ///     cleanly once, and a looping outro never reports finished either.
        /// </summary>
        private static void CollectTalkEntryFindings(
            List<TalkEntry> entries, string label, string idPrefix, List<BodyAnimationFinding> findings)
        {
            bool anyAdditive = false;
            bool anyOverride = false;
            for (int i = 0; i < entries.Count; i++)
            {
                TalkEntry entry = entries[i];
                if (entry == null)
                {
                    findings.Add(new BodyAnimationFinding(
                        $"set.{idPrefix}.null", BodyAnimationValidationSeverity.Error, $"{label}[{i}] is null."));
                    continue;
                }
                if (entry.Clip == null)
                    findings.Add(new BodyAnimationFinding(
                        $"set.{idPrefix}.clipMissing", BodyAnimationValidationSeverity.Warning, $"{label}[{i}] has no clip."));
                else if (!entry.Clip.isLooping)
                    findings.Add(new BodyAnimationFinding(
                        $"set.{idPrefix}.notLooping", BodyAnimationValidationSeverity.ReleaseBlocker,
                        $"{label}[{i}] '{entry.Clip.name}' is not import-set to loop (Loop Time)."));

                if (entry.IntroClip != null && entry.IntroClip.isLooping)
                    findings.Add(new BodyAnimationFinding(
                        $"set.{idPrefix}.introLooping", BodyAnimationValidationSeverity.Warning,
                        $"{label}[{i}] intro clip '{entry.IntroClip.name}' is import-set to loop (Loop Time) — intro clips must be one-shots."));

                if (entry.OutroClip != null && entry.OutroClip.isLooping)
                    findings.Add(new BodyAnimationFinding(
                        $"set.{idPrefix}.outroLooping", BodyAnimationValidationSeverity.Warning,
                        $"{label}[{i}] outro clip '{entry.OutroClip.name}' is import-set to loop (Loop Time) — outro clips must be one-shots."));

                IReadOnlyList<TalkMotionFragment> fragments = entry.Fragments;
                float previousEnd = -1f;
                for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
                {
                    TalkMotionFragment fragment = fragments[fragmentIndex];
                    if (fragment == null || !fragment.IsValid)
                    {
                        findings.Add(new BodyAnimationFinding(
                            $"set.{idPrefix}.fragmentInvalid", BodyAnimationValidationSeverity.Warning,
                            $"{label}[{i}] fragment[{fragmentIndex}] is null or has an invalid range/weight."));
                        continue;
                    }
                    if (previousEnd >= 0f && fragment.StartNormalized < previousEnd)
                        findings.Add(new BodyAnimationFinding(
                            $"set.{idPrefix}.fragmentOverlap", BodyAnimationValidationSeverity.Warning,
                            $"{label}[{i}] fragment[{fragmentIndex}] overlaps the previous motion phrase; refine its boundaries."));
                    previousEnd = Mathf.Max(previousEnd, fragment.EndNormalized);
                }

                if (!entry.IsValid) continue;
                if (entry.Additive) anyAdditive = true;
                else anyOverride = true;
            }

            if (anyAdditive && anyOverride)
            {
                findings.Add(new BodyAnimationFinding(
                    $"set.{idPrefix}.additiveModeMixed", BodyAnimationValidationSeverity.Warning,
                    $"{label} entries mix Additive and non-Additive modes. The layer must re-blend " +
                    "through zero weight whenever the mode changes mid-speech; prefer one mode " +
                    "across the whole talk pool."));
            }
        }

        private bool AnyActionUsesUpperBody()
        {
            for (int i = 0; i < _actions.Count; i++)
            {
                if (_actions[i] != null && _actions[i].MaskMode == ActionMaskMode.UpperBody) return true;
            }
            return false;
        }

        private static void RegisterActionKey(
            Dictionary<string, string> seen,
            string nameOrAlias,
            string ownerAction,
            int index,
            List<BodyAnimationFinding> findings)
        {
            string key = ActionEntry.NormalizeName(nameOrAlias);
            if (string.IsNullOrEmpty(key)) return;

            if (seen.TryGetValue(key, out string existingOwner))
            {
                findings.Add(new BodyAnimationFinding(
                    "set.action.nameCollision", BodyAnimationValidationSeverity.Warning,
                    $"Action[{index}] '{ownerAction}': name/alias '{nameOrAlias}' collides with action '{existingOwner}'."));
                return;
            }

            seen.Add(key, ownerAction);
        }

        private Dictionary<string, ActionEntry> BuildActionLookup()
        {
            var lookup = new Dictionary<string, ActionEntry>();
            for (int i = 0; i < _actions.Count; i++)
            {
                ActionEntry action = _actions[i];
                if (!action.IsValid) continue;

                TryAddKey(lookup, action.ActionName, action);
                foreach (string alias in action.Aliases)
                    TryAddKey(lookup, alias, action);
            }

            return lookup;

            static void TryAddKey(Dictionary<string, ActionEntry> lookup, string name, ActionEntry entry)
            {
                string key = ActionEntry.NormalizeName(name);
                if (!string.IsNullOrEmpty(key))
                    lookup.TryAdd(key, entry);
            }
        }

        private void OnValidate()
        {
            _schemaVersion = CurrentSchemaVersion;
            _actionLookup = null;
        }

        /// <summary>Editor/wizard writers. Not part of the public runtime API.</summary>
        internal void InitializeContent(
            string displayName,
            List<IdleEntry> idles,
            List<TalkEntry> talks,
            List<ActionEntry> actions,
            AvatarMask upperBodyMask,
            List<TalkEntry> listens = null,
            List<TalkEntry> thinks = null)
        {
            _displayName = displayName;
            if (idles != null) _idles = idles;
            if (talks != null) _talks = talks;
            if (actions != null) _actions = actions;
            if (listens != null) _listens = listens;
            if (thinks != null) _thinks = thinks;
            _upperBodyMask = upperBodyMask;
            _actionLookup = null;
        }
    }
}
