using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Editor.Ownership;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;
using Convai.Modules.Emotion.Taxonomy;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>Which link of the resting-mood chain decided what a character rests at.</summary>
    /// <remarks>
    ///     Ordered lowest priority first, matching the table in the Emotions documentation.
    /// </remarks>
    internal enum EmotionRestingMoodSource
    {
        /// <summary>Nothing is configured, so the face relaxes to true neutral.</summary>
        None,

        /// <summary>The personality asset's own resting mood, shared by every character on it.</summary>
        ProfileBaseline,

        /// <summary>This character's own resting-mood override.</summary>
        InitialMoodOverride,

        /// <summary>
        ///     This character forces a truly neutral rest, deliberately suppressing the
        ///     personality's resting mood rather than falling through to it.
        /// </summary>
        ForcedNeutralOverride
    }

    /// <summary>
    ///     Which resting mood a character actually settles on, and which link of the chain decided
    ///     it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The authored chain only — <c>SetMood()</c> is a runtime layer on top of this, and
    ///         nothing an editor can read distinguishes it from mood drift once both are moving.
    ///         A surface that wants to report the live mood reads
    ///         <see cref="ConvaiEmotionController.CurrentMoodLabel" /> and says plainly that it may
    ///         differ, rather than guessing at a cause.
    ///     </para>
    ///     <para>
    ///         This exists so the rule lives once. It was previously private inside the controller
    ///         and re-implemented, slightly differently, by the component inspector: the inspector
    ///         compared the override label to the literal string <c>"neutral"</c> and treated any
    ///         non-empty label as winning, while the controller resolves the label through the
    ///         character's own vocabulary and falls through to the personality when it does not
    ///         resolve. The two disagreed on a custom vocabulary and on a typo.
    ///     </para>
    /// </remarks>
    internal readonly struct EmotionRestingMood
    {
        public EmotionRestingMood(
            EmotionRestingMoodSource source,
            string label,
            float intensity,
            string explanation,
            string suppressed,
            bool labelResolves)
        {
            Source = source;
            Label = label;
            Intensity = intensity;
            Explanation = explanation;
            Suppressed = suppressed;
            LabelResolves = labelResolves;
        }

        /// <summary>Which link won.</summary>
        public EmotionRestingMoodSource Source { get; }

        /// <summary>Canonical label the character rests at; the vocabulary's neutral when none.</summary>
        public string Label { get; }

        /// <summary>Resting strength in <c>[0, 1]</c>. Zero means the face rests at plain neutral.</summary>
        public float Intensity { get; }

        /// <summary>One line naming the winner and its value, in the labels the user sees.</summary>
        public string Explanation { get; }

        /// <summary>What the winner overrode, or empty when it overrode nothing.</summary>
        public string Suppressed { get; }

        /// <summary>
        ///     Whether this character's own override label resolves against its vocabulary. False
        ///     means a label was typed that the vocabulary does not define, so it was ignored —
        ///     the single most confusing way this control fails.
        /// </summary>
        public bool LabelResolves { get; }
    }

    /// <summary>State of a single preflight row on the setup card.</summary>
    internal enum EmotionCheckState
    {
        /// <summary>Already satisfied.</summary>
        Ok,

        /// <summary>Not satisfied, but setup can resolve it.</summary>
        Fixable,

        /// <summary>Not satisfied and setup cannot resolve it — the user must act first.</summary>
        Blocked,

        /// <summary>Not present, and that is a legitimate choice rather than a problem.</summary>
        Optional
    }

    /// <summary>One preflight row: what is checked, what was found, and whether setup can fix it.</summary>
    internal readonly struct EmotionCheck
    {
        public EmotionCheck(string id, string label, string detail, EmotionCheckState state)
        {
            Id = id;
            Label = label;
            Detail = detail;
            State = state;
        }

        /// <summary>Stable identifier, never shown to the user.</summary>
        public string Id { get; }

        /// <summary>Plain-language name of what is being checked, e.g. "Face".</summary>
        public string Label { get; }

        /// <summary>What was actually found — a mesh name, or the reason it is not satisfied.</summary>
        public string Detail { get; }

        public EmotionCheckState State { get; }
    }

    /// <summary>
    ///     The read-only answer to "is this character ready, and what will the setup button do?" —
    ///     computed fresh on every inspector repaint, so it never promises something it cannot
    ///     deliver.
    /// </summary>
    internal readonly struct EmotionPreflight
    {
        public EmotionPreflight(
            IReadOnlyList<EmotionCheck> checks, RigConvention convention, float conventionConfidence,
            int resolvedShapeCount)
        {
            Checks = checks;
            Convention = convention;
            ConventionConfidence = conventionConfidence;
            ResolvedShapeCount = resolvedShapeCount;
        }

        public IReadOnlyList<EmotionCheck> Checks { get; }

        /// <summary>The face-rig convention detected from the character's own blendshape names.</summary>
        public RigConvention Convention { get; }

        /// <summary>How sure the detection is, in <c>[0, 1]</c>. Shown so it is never a black box.</summary>
        public float ConventionConfidence { get; }

        /// <summary>How many blendshapes the character's face actually offers.</summary>
        public int ResolvedShapeCount { get; }

        /// <summary>True when nothing is blocked and nothing remains for setup to do.</summary>
        public bool IsConfigured
        {
            get
            {
                if (Checks == null) return false;
                for (int i = 0; i < Checks.Count; i++)
                    if (Checks[i].State is EmotionCheckState.Fixable or EmotionCheckState.Blocked)
                        return false;
                return true;
            }
        }

        /// <summary>True when at least one row is blocked — setup cannot complete on its own.</summary>
        /// <remarks>
        ///     Counting <em>any</em> blocked row is only safe because every row that can block here
        ///     is structural: no Convai character to receive emotions, and no face with blendshapes
        ///     to move. The rig row blocks only as a restatement of the second — with shapes present
        ///     but a naming convention Convai does not recognise it is <c>Optional</c>, because
        ///     expressions still resolve, just fewer of them.
        ///     <para>
        ///         Missing content is deliberately not among them. The personality is
        ///         <c>Fixable</c>, never blocking, and a character without one runs on the built-in
        ///         defaults — an empty recipe list falls back to Convai's own expression library, so
        ///         the face moves. That is why this module needs no needs-content verdict: a
        ///         correctly rigged character with nothing authored is working, not unfinished. A
        ///         row that ever blocks on absent content would make this property answer the wrong
        ///         question, and the fix would be a separate state rather than a wider blocker.
        ///     </para>
        /// </remarks>
        public bool HasBlocker
        {
            get
            {
                if (Checks == null) return false;
                for (int i = 0; i < Checks.Count; i++)
                    if (Checks[i].State == EmotionCheckState.Blocked) return true;
                return false;
            }
        }
    }

    /// <summary>What the setup button should do beyond assigning a profile.</summary>
    internal struct EmotionSetupOptions
    {
        /// <summary>Which character type the new profile starts from.</summary>
        public CharacterDemeanor CharacterType;

        public static EmotionSetupOptions Default => new() { CharacterType = CharacterDemeanor.Warm };
    }

    /// <summary>Outcome of a setup run, rendered identically by every caller.</summary>
    internal readonly struct EmotionSetupResult
    {
        public EmotionSetupResult(bool changed, string summary, IReadOnlyList<string> notes)
        {
            Changed = changed;
            Summary = summary;
            Notes = notes;
        }

        /// <summary>Whether anything was actually written.</summary>
        public bool Changed { get; }

        /// <summary>One-line result the surface shows immediately.</summary>
        public string Summary { get; }

        /// <summary>Everything that happened, and everything that could not be done.</summary>
        public IReadOnlyList<string> Notes { get; }
    }

    /// <summary>
    ///     Answers "is this character ready for emotions?" and, on request, makes it ready.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The user is never asked to classify their own face rig. The convention is detected
    ///         from the mesh's blendshape names and reported with its confidence, with a manual
    ///         override available — because expression recipes are rig-independent, getting this
    ///         wrong costs coverage (some channels resolve to nothing) rather than correctness.
    ///     </para>
    ///     <para>
    ///         Mirrors <c>BodyAnimationSetupService</c> deliberately: same preflight/apply shape,
    ///         same "one button, and it says what it did" contract.
    ///     </para>
    /// </remarks>
    internal static class EmotionSetupService
    {
        /// <summary>
        ///     Evaluates <paramref name="controller" /> without changing anything.
        /// </summary>
        internal static EmotionPreflight Inspect(ConvaiEmotionController controller)
        {
            var checks = new List<EmotionCheck>(4);
            if (controller == null)
                return new EmotionPreflight(checks, RigConvention.Unknown, 0f, 0);

            // 1. The character itself. Without a ConvaiCharacter ancestor the controller can never
            //    match an incoming emotion event, and stays neutral forever with nothing to show
            //    for it — so this is the first thing checked and it is a blocker.
            var character = controller.GetComponentInParent<ConvaiCharacter>(true);
            checks.Add(character != null
                ? new EmotionCheck("character", "Character", character.name, EmotionCheckState.Ok)
                : new EmotionCheck("character", "Character",
                    "Not on a Convai character. Move this component onto the character GameObject.",
                    EmotionCheckState.Blocked));

            // 2. The face. Expression needs blendshapes to drive; nothing else can substitute.
            List<SkinnedMeshRenderer> faceMeshes = CollectFaceMeshes(controller);
            int shapeCount = CountBlendshapes(faceMeshes);
            checks.Add(shapeCount > 0
                ? new EmotionCheck("face", "Face", DescribeFace(faceMeshes, shapeCount), EmotionCheckState.Ok)
                : new EmotionCheck("face", "Face",
                    "No skinned mesh with blendshapes found. Emotions need a face that can deform.",
                    EmotionCheckState.Blocked));

            // 3. The rig convention, detected rather than asked for.
            RigConvention convention = RigConvention.Unknown;
            float confidence = 0f;
            if (shapeCount > 0) convention = RigConventionResolver.Detect(faceMeshes, out confidence);

            checks.Add(convention != RigConvention.Unknown
                ? new EmotionCheck("rig", "Face Rig",
                    $"{DescribeConvention(convention)} ({Mathf.RoundToInt(confidence * 100f)}% match)",
                    EmotionCheckState.Ok)
                : new EmotionCheck("rig", "Face Rig",
                    shapeCount > 0
                        ? "Not a naming convention Convai recognises. Expressions will be limited; " +
                          "assign a Custom Rig Convention Map to map it yourself."
                        : "Cannot be detected without a face.",
                    shapeCount > 0 ? EmotionCheckState.Optional : EmotionCheckState.Blocked));

            // 4. The profile. This is the one thing setup can create.
            ConvaiEmotionProfile profile = ResolveAssignedProfile(controller);
            checks.Add(profile != null
                ? new EmotionCheck("profile", "Personality", profile.name, EmotionCheckState.Ok)
                : new EmotionCheck("profile", "Personality",
                    "None yet. Setup will create one you can tune.", EmotionCheckState.Fixable));

            return new EmotionPreflight(checks, convention, confidence, shapeCount);
        }

        /// <summary>
        ///     Creates and assigns a profile for <paramref name="controller" />, starting from the
        ///     requested character type. Never touches the rig, and never overwrites a profile the
        ///     character already has.
        /// </summary>
        internal static EmotionSetupResult Apply(
            ConvaiEmotionController controller, EmotionSetupOptions options)
        {
            var notes = new List<string>(3);
            if (controller == null)
                return new EmotionSetupResult(false, "No character selected.", notes);

            if (ResolveAssignedProfile(controller) != null)
                return new EmotionSetupResult(false, "This character already has a personality assigned.", notes);

            EmotionPreflight preflight = Inspect(controller);

            ConvaiEmotionProfile profile = CreateProfileAsset(controller, options.CharacterType);
            if (profile == null)
                return new EmotionSetupResult(false,
                    "Could not create the personality asset. Check that the project folder is writable.", notes);

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("profile").objectReferenceValue = profile;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);

            notes.Add($"Created '{profile.name}' and assigned it to this character.");

            if (preflight.Convention != RigConvention.Unknown)
                notes.Add($"Detected a {DescribeConvention(preflight.Convention)} face " +
                          $"({Mathf.RoundToInt(preflight.ConventionConfidence * 100f)}% match), so the built-in " +
                          "expressions will map onto it automatically.");
            else
                notes.Add("This character's blendshape names do not match a convention Convai recognises, so " +
                          "expressions will be limited. Assign a Custom Rig Convention Map to map them yourself.");

            EditorGUIUtility.PingObject(profile);
            return new EmotionSetupResult(true,
                $"{controller.name} is set up for emotions.", notes);
        }

        /// <summary>
        ///     Resolves which resting mood <paramref name="controller" /> settles on, and which
        ///     link of the chain decided it.
        /// </summary>
        /// <remarks>
        ///     Mirrors <c>ConvaiEmotionController.ResolveAuthoredBaseline</c> step for step,
        ///     including the case an earlier inspector copy got wrong: an override label that the
        ///     character's vocabulary does not define is <em>ignored</em>, and the personality's
        ///     resting mood is used instead.
        /// </remarks>
        internal static EmotionRestingMood ResolveEffectiveRestingMood(ConvaiEmotionController controller)
        {
            if (controller == null)
                return new EmotionRestingMood(EmotionRestingMoodSource.None, EmotionReading.NeutralLabel,
                    0f, "No character selected.", string.Empty, true);

            ConvaiEmotionProfile profile = ResolveAssignedProfile(controller);
            ReadInitialMood(controller, out string overrideLabel, out float overrideIntensity);

            EmotionTaxonomyAsset taxonomy = null;
            bool synthesized = false;
            try
            {
                taxonomy = profile != null
                    ? profile.ResolveTaxonomyOrDefault(out synthesized)
                    : SynthesizeDefaultTaxonomy(out synthesized);

                return Resolve(profile, taxonomy, overrideLabel, overrideIntensity);
            }
            finally
            {
                // ResolveTaxonomyOrDefault builds a throwaway vocabulary when the personality
                // leaves the field empty. Editor surfaces call this on every repaint, so leaking
                // one ScriptableObject per call would fill the scene with hidden objects.
                if (synthesized && taxonomy != null) Object.DestroyImmediate(taxonomy);
            }
        }

        private static EmotionTaxonomyAsset SynthesizeDefaultTaxonomy(out bool synthesized)
        {
            synthesized = true;
            return EmotionTaxonomyAsset.CreateDefault();
        }

        private static EmotionRestingMood Resolve(
            ConvaiEmotionProfile profile,
            EmotionTaxonomyAsset taxonomy,
            string overrideLabel,
            float overrideIntensity)
        {
            string neutralLabel = taxonomy?.Neutral?.Label ?? EmotionReading.NeutralLabel;
            string profileSummary = DescribeProfileBaseline(profile, taxonomy, out bool profileHasBaseline,
                out string profileLabel, out float profileIntensity);

            bool wantsOverride = !string.IsNullOrWhiteSpace(overrideLabel);
            EmotionDescriptor overrideDescriptor = null;
            bool overrideResolved = wantsOverride && taxonomy != null &&
                                    taxonomy.TryResolve(overrideLabel, out overrideDescriptor) &&
                                    overrideDescriptor != null;

            string suppressedByOverride = profileHasBaseline
                ? $"The personality's resting mood ({profileSummary}) is overridden on this character."
                : string.Empty;

            // A label that resolves to the vocabulary's neutral entry FORCES a neutral rest rather
            // than falling through to the personality — the one case where "no mood" and "use the
            // personality's mood" are different answers.
            if (overrideResolved && overrideDescriptor.IsNeutral)
            {
                return new EmotionRestingMood(
                    EmotionRestingMoodSource.ForcedNeutralOverride, neutralLabel, 0f,
                    "This character is set to rest neutral, so it holds no resting mood at all.",
                    suppressedByOverride, true);
            }

            if (overrideResolved)
            {
                float strength = Mathf.Clamp01(overrideIntensity);
                return new EmotionRestingMood(
                    EmotionRestingMoodSource.InitialMoodOverride, overrideDescriptor.Label, strength,
                    $"This character rests at {overrideDescriptor.Label} at {strength:0.00}.",
                    suppressedByOverride, true);
            }

            // Reached when the override was left empty, or when a label was typed that this
            // character's vocabulary does not define — in which case it is ignored, exactly as the
            // runtime ignores it. LabelResolves is what tells the two cases apart.
            bool labelResolves = !wantsOverride;

            if (profileHasBaseline)
            {
                return new EmotionRestingMood(
                    EmotionRestingMoodSource.ProfileBaseline, profileLabel, profileIntensity,
                    $"This character rests at the personality's mood: {profileSummary}.",
                    string.Empty, labelResolves);
            }

            return new EmotionRestingMood(
                EmotionRestingMoodSource.None, neutralLabel, 0f,
                "This character has no resting mood, so its face relaxes to plain neutral between " +
                "what it feels.",
                string.Empty, labelResolves);
        }

        /// <summary>Reads the per-character override pair; both fields are private by design.</summary>
        private static void ReadInitialMood(
            ConvaiEmotionController controller, out string label, out float intensity)
        {
            var serialized = new SerializedObject(controller);
            label = serialized.FindProperty("initialMoodLabel")?.stringValue ?? string.Empty;
            intensity = serialized.FindProperty("initialMoodIntensity")?.floatValue ?? 0f;
        }

        private static string DescribeProfileBaseline(
            ConvaiEmotionProfile profile,
            EmotionTaxonomyAsset taxonomy,
            out bool hasBaseline,
            out string label,
            out float intensity)
        {
            hasBaseline = false;
            label = taxonomy?.Neutral?.Label ?? EmotionReading.NeutralLabel;
            intensity = 0f;

            if (profile == null || string.IsNullOrWhiteSpace(profile.BaselineEmotionLabel)) return "neutral";
            if (taxonomy == null ||
                !taxonomy.TryResolve(profile.BaselineEmotionLabel, out EmotionDescriptor descriptor) ||
                descriptor.IsNeutral)
                return "neutral";

            hasBaseline = true;
            label = descriptor.Label;
            intensity = profile.BaselineIntensity;
            return $"{label} at {intensity:0.00}";
        }

        /// <summary>The personality asset assigned to this controller, or <c>null</c>.</summary>
        /// <remarks>
        ///     Reads the controller directly. This is called from several draw paths per repaint, and
        ///     building a <see cref="SerializedObject" /> to read one reference — which is what it
        ///     used to do — allocated on each of them.
        /// </remarks>
        internal static ConvaiEmotionProfile ResolveAssignedProfile(ConvaiEmotionController controller) =>
            controller != null ? controller.AssignedProfile : null;

        /// <summary>Human-readable name for a rig convention. Never shows the enum member.</summary>
        internal static string DescribeConvention(RigConvention convention) => convention switch
        {
            RigConvention.ARKit => "Apple ARKit",
            RigConvention.ReallusionCC3 => "Reallusion Character Creator 3",
            RigConvention.ReallusionCC4Extended => "Reallusion Character Creator 4",
            RigConvention.MetaHuman => "Epic MetaHuman",
            RigConvention.Custom => "Custom mapping",
            _ => "Unrecognised"
        };

        /// <summary>Every skinned mesh under the character that carries blendshapes.</summary>
        internal static List<SkinnedMeshRenderer> CollectFaceMeshes(ConvaiEmotionController controller)
        {
            var meshes = new List<SkinnedMeshRenderer>(4);
            if (controller == null) return meshes;

            Transform root = controller.transform;
            var character = controller.GetComponentInParent<ConvaiCharacter>(true);
            if (character != null) root = character.transform;

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                if (renderer.sharedMesh.blendShapeCount == 0) continue;
                meshes.Add(renderer);
            }
            return meshes;
        }

        private static int CountBlendshapes(List<SkinnedMeshRenderer> meshes)
        {
            int total = 0;
            for (int i = 0; i < meshes.Count; i++) total += meshes[i].sharedMesh.blendShapeCount;
            return total;
        }

        private static string DescribeFace(List<SkinnedMeshRenderer> meshes, int shapeCount)
        {
            if (meshes.Count == 1) return $"{meshes[0].name} · {shapeCount} shapes";
            return $"{meshes.Count} meshes · {shapeCount} shapes";
        }

        /// <summary>
        ///     Creates the profile asset where every Convai-authored settings asset goes: beside the
        ///     character's own prefab when it has one, otherwise <c>Assets/Convai/Emotion</c>.
        /// </summary>
        private static ConvaiEmotionProfile CreateProfileAsset(
            ConvaiEmotionController controller, CharacterDemeanor characterType)
        {
            ConvaiEmotionProfile profile = BuildProfile(characterType);
            if (profile == null) return null;

            string directory = ConvaiProjectAssetFolder.For(controller, "Emotion");
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{ConvaiProjectAssetFolder.SanitizeName(controller)}_Emotion.asset");

            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ConvaiEmotionProfile>(path);
        }

        /// <summary>Builds an unsaved profile for the requested character type.</summary>
        /// <remarks>
        ///     Reads the same <see cref="EmotionPersonalityTable" /> the public preset factories and
        ///     the apply-a-character-type writer read, so a profile created here is byte-for-byte the
        ///     profile pressing that character type's button would produce.
        /// </remarks>
        internal static ConvaiEmotionProfile BuildProfile(CharacterDemeanor characterType) =>
            ConvaiEmotionProfile.CreateFrom(EmotionPersonalityTable.For(characterType), null);

    }
}
