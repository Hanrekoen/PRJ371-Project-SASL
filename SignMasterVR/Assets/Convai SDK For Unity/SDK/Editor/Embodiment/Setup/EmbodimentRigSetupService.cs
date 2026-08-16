using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.Setup
{
    /// <summary>
    ///     Checks whether a character's rig is understood well enough for the expressive features to
    ///     work, and sets it up in one step when it is not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Rig resolution is the one setup step every feature depends on: gaze needs the eyes and
    ///         head, emotion needs the face meshes, body animation needs a humanoid avatar. It is also
    ///         the step that was hardest to see — the binding is created automatically at runtime and
    ///         appears in no shipped scene, so a user whose face rig was mis-detected had nowhere to
    ///         look and nothing to correct.
    ///     </para>
    ///     <para>
    ///         Auto-creation stays (it is what makes a character work with no setup), but it now has to
    ///         be <em>right</em>, and <see cref="Inspect" /> is how a user sees whether it is. Safe to
    ///         call every repaint: it reads the hierarchy and never mutates.
    ///     </para>
    /// </remarks>
    internal static class EmbodimentRigSetupService
    {
        /// <summary>
        ///     Confidence below which auto-detection is reported as a guess worth checking. Shared
        ///     with the rig inspector so a detection this report calls weak is never the same one the
        ///     inspector shows as healthy.
        /// </summary>
        private const float LowConfidence = RigConventionDisplay.LowConfidence;

        /// <summary>
        ///     Reports what the rig resolved to and what is missing, without changing anything.
        /// </summary>
        internal static EmbodimentSetupReport Inspect(GameObject characterRoot)
        {
            var findings = new List<EmbodimentFinding>();

            if (characterRoot == null)
            {
                findings.Add(EmbodimentFinding.Error(
                    "rig.no-character", "No character",
                    "Select the object with the Convai Character component."));
                return new EmbodimentSetupReport(findings);
            }

            if (characterRoot.GetComponentInParent<ConvaiCharacter>(true) == null)
            {
                findings.Add(EmbodimentFinding.Error(
                    "rig.not-a-character", "Not a Convai character",
                    "This object has no Convai Character component, so Convai will not drive it.",
                    "Add Convai Character",
                    () => Undo.AddComponent<ConvaiCharacter>(characterRoot)));
                return new EmbodimentSetupReport(findings);
            }

            var animator = characterRoot.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                findings.Add(EmbodimentFinding.Warning(
                    "rig.no-animator", "No Animator (Optional)",
                    "This is fine for a character that does not use skeletal body movement. Features " +
                    "that need a Humanoid rig, such as Body Animation or Body Language, will report " +
                    "their own blocking setup issue."));
            }
            else if (!animator.isHuman)
            {
                findings.Add(EmbodimentFinding.Warning(
                    "rig.not-humanoid", "Rig is not Humanoid",
                    "Head and eye aiming use the humanoid bone map. Set the model's Rig type to " +
                    "Humanoid in its import settings to get gaze and posture."));
            }

            var binding = characterRoot.GetComponentInChildren<StandardRigBinding>(true);
            if (binding == null)
            {
                findings.Add(EmbodimentFinding.Info(
                    "rig.auto-resolved", "Rig will be worked out automatically",
                    "Convai resolves the bones and face meshes when the character starts. Add the rig " +
                    "component if you want to see the result now, or correct a mis-detected face rig.",
                    "Set Up Rig Now",
                    () => Apply(characterRoot)));
                return new EmbodimentSetupReport(findings);
            }

            AddResolvedRigFindings(binding, findings);
            return new EmbodimentSetupReport(findings);
        }

        private static void AddResolvedRigFindings(StandardRigBinding binding, List<EmbodimentFinding> findings)
        {
            int faceMeshes = binding.FacialMeshes?.Count ?? 0;

            if (binding.DetectedConvention == RigConvention.Unknown)
            {
                findings.Add(EmbodimentFinding.Warning(
                    "rig.unknown-convention", "Face rig not recognized",
                    "Convai could not tell which blendshape naming convention this face uses, so " +
                    "expression and lip sync may not reach it. Pick the convention manually on the rig " +
                    "component, or supply a Custom Rig Convention Map."));
            }
            else if (binding.DetectionConfidence < LowConfidence)
            {
                findings.Add(EmbodimentFinding.Warning(
                    "rig.low-confidence",
                    $"Face rig detected as {RigConventionDisplay.DisplayName(binding.DetectedConvention)}, " +
                    "but only just",
                    $"Confidence is {binding.DetectionConfidence:P0}. Check that expression looks right; " +
                    "if it does not, set the convention manually on the rig component."));
            }
            else
            {
                findings.Add(EmbodimentFinding.Ok(
                    "rig.convention-ok",
                    $"Face rig: {RigConventionDisplay.DisplayName(binding.DetectedConvention)}",
                    $"Detected with {binding.DetectionConfidence:P0} confidence."));
            }

            if (faceMeshes == 0)
            {
                findings.Add(EmbodimentFinding.Warning(
                    "rig.no-face-meshes", "No face meshes found",
                    "Expression and lip sync write to blendshapes on skinned meshes, and none were " +
                    "found. Check that the character's head mesh has blendshapes."));
            }
            else
            {
                findings.Add(EmbodimentFinding.Ok(
                    "rig.face-meshes-ok",
                    faceMeshes == 1 ? "1 face mesh" : $"{faceMeshes} face meshes"));
            }

            AddBoneFinding(binding, StandardBone.Head, "Head", "head aiming and nods", findings);
            AddBoneFinding(binding, StandardBone.LeftEye, "Left eye", "eye movement", findings);
            AddBoneFinding(binding, StandardBone.RightEye, "Right eye", "eye movement", findings);
        }

        private static void AddBoneFinding(
            StandardRigBinding binding,
            StandardBone bone,
            string label,
            string whatItCosts,
            List<EmbodimentFinding> findings)
        {
            if (binding.TryGetBone(bone, out Transform resolved) && resolved != null)
            {
                findings.Add(EmbodimentFinding.Ok($"rig.bone.{bone}", $"{label}: {resolved.name}"));
                return;
            }

            findings.Add(EmbodimentFinding.Warning(
                $"rig.bone-missing.{bone}", $"{label} bone not found",
                $"Without it there is no {whatItCosts}. This usually means the model's Rig type is not " +
                "Humanoid, or the humanoid avatar has no bone mapped for it."));
        }

        /// <summary>
        ///     Sets the character's rig up: adds the rig component if needed and resolves it, in one
        ///     undo group.
        /// </summary>
        internal static EmbodimentRigSetupResult Apply(GameObject characterRoot)
        {
            if (characterRoot == null)
                return new EmbodimentRigSetupResult(false, "No character to set up.");

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Up Convai Rig");

            var notes = new List<string>();
            var binding = characterRoot.GetComponentInChildren<StandardRigBinding>(true);

            if (binding == null)
            {
                binding = Undo.AddComponent<StandardRigBinding>(characterRoot);
                notes.Add("Added the rig component.");
            }

            Undo.RecordObject(binding, "Resolve Convai Rig");
            binding.Rebuild();

            notes.Add(binding.DetectedConvention == RigConvention.Unknown
                ? "Could not recognize the face rig — set the convention manually."
                : $"Face rig: {RigConventionDisplay.DisplayName(binding.DetectedConvention)} " +
                  $"({binding.DetectionConfidence:P0} confidence), " +
                  $"{binding.FacialMeshes?.Count ?? 0} face mesh(es).");

            EditorUtility.SetDirty(binding);
            Undo.CollapseUndoOperations(group);

            return new EmbodimentRigSetupResult(true, string.Join(" ", notes));
        }
    }

    /// <summary>Outcome of <see cref="EmbodimentRigSetupService.Apply" />.</summary>
    internal readonly struct EmbodimentRigSetupResult
    {
        public EmbodimentRigSetupResult(bool changed, string summary)
        {
            Changed = changed;
            Summary = summary;
        }

        public bool Changed { get; }
        public string Summary { get; }
    }
}
