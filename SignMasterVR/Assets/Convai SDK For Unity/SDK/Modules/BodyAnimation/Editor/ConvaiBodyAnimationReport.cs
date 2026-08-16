using System;
using System.Collections.Generic;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor.AI
{
    /// <summary>
    ///     How far along a character's body animation setup is. Four states rather than a boolean,
    ///     because "not working" has three causes with three different next steps, and telling them
    ///     apart is the whole point of diagnosing a content-gated module.
    /// </summary>
    internal enum BodyAnimationReadiness
    {
        /// <summary>No Body Animation component on this character at all.</summary>
        NotInstalled,

        /// <summary>Present, but the rig or the project cannot support it until someone acts.</summary>
        Blocked,

        /// <summary>Present and the rig is fine, but no animation content is assigned, so it is inert.</summary>
        NeedsContent,

        /// <summary>Set up. The character idles, talks and gestures.</summary>
        Working
    }

    /// <summary>
    ///     Everything the Convai body animation tools know about one character, gathered once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every verdict here comes from <see cref="BodyAnimationSetupService" /> and
    ///         <see cref="BodyAnimationTroubleshooter" /> — the same code the component inspector
    ///         and the Body Animation Editor window draw. Nothing in this assembly evaluates a
    ///         character itself, so an assistant and the editor cannot describe the same character
    ///         differently.
    ///     </para>
    ///     <para>
    ///         Shared by the diagnose tool and the scene surveyor so those two cannot drift apart
    ///         either.
    ///     </para>
    /// </remarks>
    internal readonly struct ConvaiBodyAnimationReport
    {
        private ConvaiBodyAnimationReport(
            ConvaiBodyAnimationController controller,
            BodyAnimationPreflight preflight,
            BodyAnimationTroubleshooterInput input,
            List<BodyAnimationTroubleshooterFinding> findings,
            ConvaiBodyAnimationSet set,
            ConvaiBodyAnimationConfig config,
            Animator animator)
        {
            Controller = controller;
            Preflight = preflight;
            Input = input;
            Findings = findings;
            Set = set;
            Config = config;
            Animator = animator;
        }

        internal ConvaiBodyAnimationController Controller { get; }
        internal BodyAnimationPreflight Preflight { get; }
        internal BodyAnimationTroubleshooterInput Input { get; }
        internal List<BodyAnimationTroubleshooterFinding> Findings { get; }
        internal ConvaiBodyAnimationSet Set { get; }
        internal ConvaiBodyAnimationConfig Config { get; }
        internal Animator Animator { get; }

        internal bool IsPresent => Controller != null;

        /// <summary>Whether the character will actually animate at runtime.</summary>
        internal bool IsWorking => State == BodyAnimationReadiness.Working;

        internal BodyAnimationReadiness State
        {
            get
            {
                if (Controller == null) return BodyAnimationReadiness.NotInstalled;
                if (Preflight.HasBlocker) return BodyAnimationReadiness.Blocked;
                return Input.HasSetAssigned
                    ? BodyAnimationReadiness.Working
                    : BodyAnimationReadiness.NeedsContent;
            }
        }

        /// <summary>The one line a survey shows without expanding anything.</summary>
        internal string Summary => State switch
        {
            BodyAnimationReadiness.NotInstalled =>
                "Not on this character — it will not idle, talk or gesture.",
            BodyAnimationReadiness.Blocked => Blocker,
            BodyAnimationReadiness.NeedsContent =>
                "Set up, but no animation content is assigned yet, so the character stays still.",
            _ => Set != null
                ? $"Animating from the '{Set.DisplayName}' animation set."
                : "Animating."
        };

        /// <summary>The first preflight row that stops setup completing, or an empty string.</summary>
        internal string Blocker
        {
            get
            {
                IReadOnlyList<BodyAnimationCheck> checks = Preflight.Checks;
                if (checks == null) return string.Empty;

                for (int i = 0; i < checks.Count; i++)
                    if (checks[i].State == BodyAnimationCheckState.Blocked)
                        return $"{checks[i].Label}: {checks[i].Detail}.";

                return string.Empty;
            }
        }

        /// <summary>
        ///     Gathers the report for <paramref name="characterRoot" />. Read-only and safe to call
        ///     from any diagnostic path; returns a <see cref="BodyAnimationReadiness.NotInstalled" />
        ///     report rather than throwing when the character has no controller.
        /// </summary>
        internal static ConvaiBodyAnimationReport For(GameObject characterRoot)
        {
            ConvaiBodyAnimationController controller = characterRoot != null
                ? characterRoot.GetComponentInChildren<ConvaiBodyAnimationController>(true)
                : null;

            return For(controller);
        }

        internal static ConvaiBodyAnimationReport For(ConvaiBodyAnimationController controller)
        {
            var findings = new List<BodyAnimationTroubleshooterFinding>(16);
            if (controller == null)
            {
                return new ConvaiBodyAnimationReport(
                    null, default, default, findings, null, null, null);
            }

            var serialized = new SerializedObject(controller);
            BodyAnimationTroubleshooterInput input = BodyAnimationTroubleshooter.GatherFrom(
                controller,
                serialized.FindProperty("_animationSet"),
                serialized.FindProperty("_config"),
                serialized.FindProperty("profile"),
                serialized.FindProperty("_animatorOverride"),
                serialized.FindProperty("_locomotionProviderOverride"),
                new List<string>(8),
                out ConvaiBodyAnimationSet set,
                out Animator animator);

            BodyAnimationTroubleshooter.Evaluate(in input, findings);

            return new ConvaiBodyAnimationReport(
                controller,
                BodyAnimationSetupService.Inspect(controller),
                input,
                findings,
                set,
                BodyAnimationSetupService.ResolveAssignedConfig(controller),
                animator);
        }

        /// <summary>
        ///     A stable issue code from a finding's own id — <c>content.no-talk</c> becomes
        ///     <c>BODY_ANIMATION_CONTENT_NO_TALK</c>. Derived rather than tabulated, so the codes an
        ///     assistant sees stay a projection of the one finding engine instead of a second table
        ///     that has to be kept in step with it.
        /// </summary>
        internal static string IssueCode(string findingId)
        {
            if (string.IsNullOrEmpty(findingId)) return "BODY_ANIMATION_ISSUE";

            var builder = new System.Text.StringBuilder("BODY_ANIMATION_", findingId.Length + 16);
            for (int i = 0; i < findingId.Length; i++)
            {
                char character = findingId[i];
                builder.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_');
            }

            return builder.ToString();
        }

        /// <summary>Locomotion slots this set fills, out of <see cref="BodyAnimationContentCoverage.TotalSlots" />.</summary>
        internal static int CountFilledLocomotionSlots(ConvaiBodyAnimationSet set)
        {
            if (set == null) return 0;

            var assigned = new List<(string slot, LocomotionClip clip)>();
            set.Locomotion.CollectAssigned(assigned);
            return assigned.Count;
        }

        /// <summary>Talk entries carrying an Additive Clip, which is the better walk-and-talk tier.</summary>
        internal static int CountTalkEntriesWithAdditiveClip(ConvaiBodyAnimationSet set)
        {
            if (set == null) return 0;

            int count = 0;
            IReadOnlyList<TalkEntry> talks = set.Talks;
            for (int i = 0; i < talks.Count; i++)
                if (talks[i] != null && talks[i].IsValid && talks[i].AdditiveClip != null)
                    count++;
            return count;
        }

        /// <summary>Names of the valid entries in a variant pool, for a content listing.</summary>
        internal static string[] VariantClipNames<T>(IReadOnlyList<T> pool) where T : class
        {
            if (pool == null) return Array.Empty<string>();

            var names = new List<string>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                AnimationClip clip = pool[i] switch
                {
                    IdleEntry idle when idle.IsValid => idle.Clip,
                    TalkEntry talk when talk.IsValid => talk.Clip,
                    _ => null
                };
                if (clip != null) names.Add(clip.name);
            }

            return names.ToArray();
        }
    }
}
