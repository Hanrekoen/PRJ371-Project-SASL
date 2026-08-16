using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>Measures ground-plane distance from the character to a target or the player.</summary>
    [AddComponentMenu("Convai/Actions/Measure Distance")]
    [ConvaiActionArchetype(
        "Measure Distance",
        ActionName = "Measure Distance",
        Description = "Measure the ground distance from the character to the target, or to the player " +
                      "when no target is named, and answer in clear conversational terms.",
        TargetRequirement = ConvaiActionTargetRequirement.Either,
        AnswerDelivery = ConvaiActionAnswerDelivery.TellThePlayer,
        Family = "Observation")]
    public sealed class ConvaiMeasureDistanceActionExecutor : ConvaiTargetedActionExecutor
    {
        [SerializeField, Min(0f)]
        [Tooltip("Distances up to this value are described as within reach.")]
        [ConvaiInspectorSection("Distance Bands", 0)]
        private float _withinReachMetres = 1.2f;

        [SerializeField, Min(0f)]
        [Tooltip("Distances up to this value are described as a few steps away.")]
        [ConvaiInspectorSection("Distance Bands", 1)]
        private float _aFewStepsMetres = 3.5f;

        [SerializeField, Min(0f)]
        [Tooltip("Distances up to this value are described as across the area.")]
        [ConvaiInspectorSection("Distance Bands", 2)]
        private float _acrossAreaMetres = 9f;

        [SerializeField]
        [Tooltip("Include the measured value in metres in the answer.")]
        [ConvaiInspectorSection("Answer", 10)]
        private bool _includeMetres = true;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GameObject targetObject = ResolveTargetGameObject(invocation);
            Transform target = ResolveTargetInteractionPoint(invocation) ?? targetObject?.transform;
            bool measuringPlayer = target == null;
            if (measuringPlayer)
                target = ResolvePlayer();

            if (target == null)
            {
                return Task.FromResult(ConvaiActionExecutionResult.Failed(
                    "No target was resolved and no player could be found. Add a Convai Player or tag " +
                    "the active player camera as MainCamera.",
                    ConvaiActionFailureReason.TargetMissing));
            }

            Vector3 offset = target.position - CharacterTransform.position;
            offset.y = 0f;
            float metres = offset.magnitude;
            string subjectName = targetObject != null ? targetObject.name : target.name;
            string subject = measuringPlayer ? "You are" : $"{subjectName} is";
            string measurement = _includeMetres ? $" About {metres:0.0} metres." : string.Empty;
            return Task.FromResult(ConvaiActionExecutionResult.Answered(
                $"{subject} {DescribeDistance(metres)}.{measurement}".Trim()));
        }

        private string DescribeDistance(float metres)
        {
            if (metres <= _withinReachMetres)
                return "within reach";
            if (metres <= _aFewStepsMetres)
                return "a few steps away";
            return metres <= _acrossAreaMetres ? "across the area" : "a long way away";
        }

        private void OnValidate()
        {
            _withinReachMetres = Mathf.Max(0f, _withinReachMetres);
            _aFewStepsMetres = Mathf.Max(_withinReachMetres, _aFewStepsMetres);
            _acrossAreaMetres = Mathf.Max(_aFewStepsMetres, _acrossAreaMetres);
        }
    }
}
