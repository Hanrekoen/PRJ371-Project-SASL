using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>Counts the currently available members of a resolved Convai Action Target Group.</summary>
    [AddComponentMenu("Convai/Actions/Count Target Group")]
    [ConvaiActionArchetype(
        "Count Target Group",
        ActionName = "Count Target Group",
        Description = "Count the currently available members of the target group and answer with the " +
                      "result. Use this when the player asks how many known objects or characters are " +
                      "present or available.",
        TargetRequirement = ConvaiActionTargetRequirement.Object,
        RequiredTargetComponent = "ConvaiActionTargetGroup",
        AnswerDelivery = ConvaiActionAnswerDelivery.TellThePlayer,
        Family = "Observation")]
    public sealed class ConvaiCountTargetGroupActionExecutor : ConvaiTargetedActionExecutor
    {
        [SerializeField]
        [Tooltip("Ignore disabled target components and inactive target objects when counting.")]
        [ConvaiInspectorSection("Counting", 0)]
        private bool _availableMembersOnly = true;

        [SerializeField]
        [Tooltip("Include member names in the answer as well as the count.")]
        [ConvaiInspectorSection("Answer", 10)]
        private bool _includeMemberNames = true;

        [SerializeField]
        [Tooltip("Optional plural name such as 'crates'. Leave empty to use the target group's name.")]
        [ConvaiInspectorSection("Answer", 11)]
        private string _memberLabel;

        /// <inheritdoc />
        protected override Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GameObject targetObject = ResolveTargetGameObject(invocation);
            ConvaiActionTargetGroup group = ResolveGroup(targetObject);
            if (group == null)
            {
                return Task.FromResult(ConvaiActionExecutionResult.Unhandled(
                    $"'{targetObject.name}' is not a target group. Add a Convai Action Target Group " +
                    "component and assign its members."));
            }

            IReadOnlyList<ConvaiActionTarget> members = group.Members;
            if (members == null || members.Count == 0)
            {
                return Task.FromResult(ConvaiActionExecutionResult.Unhandled(
                    $"The target group '{group.GroupName}' has no members. Add at least one Convai " +
                    "Action Target to its Members list."));
            }

            var availableNames = new List<string>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                ConvaiActionTarget member = members[i];
                if (member == null)
                    continue;

                if (_availableMembersOnly && (!member.isActiveAndEnabled || !member.gameObject.activeInHierarchy))
                    continue;

                availableNames.Add(string.IsNullOrWhiteSpace(member.TargetName)
                    ? member.gameObject.name
                    : member.TargetName);
            }

            string label = string.IsNullOrWhiteSpace(_memberLabel) ? group.GroupName : _memberLabel.Trim();
            if (string.IsNullOrWhiteSpace(label))
                label = "members";

            int count = availableNames.Count;
            string answer = _availableMembersOnly && count != members.Count
                ? $"{count} of {members.Count} {label} are available."
                : $"There {(count == 1 ? "is" : "are")} {count} {label}.";

            if (_includeMemberNames && count > 0)
                answer += $" {(count == 1 ? "It is" : "They are")} {string.Join(", ", availableNames)}.";

            return Task.FromResult(ConvaiActionExecutionResult.Answered(answer));
        }

        private static ConvaiActionTargetGroup ResolveGroup(GameObject targetObject)
        {
            if (targetObject == null)
                return null;

            ConvaiActionTargetGroup group = targetObject.GetComponent<ConvaiActionTargetGroup>();
            if (group == null)
                group = targetObject.GetComponentInChildren<ConvaiActionTargetGroup>(true);
            if (group == null)
                group = targetObject.GetComponentInParent<ConvaiActionTargetGroup>(true);
            return group;
        }
    }
}
