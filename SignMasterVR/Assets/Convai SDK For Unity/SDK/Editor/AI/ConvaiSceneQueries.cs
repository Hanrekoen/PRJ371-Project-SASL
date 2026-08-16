using System.Linq;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Interfaces;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Editor.AI
{
    internal static class ConvaiSceneQueries
    {
        internal static bool HasCompleteVisionPipeline(GameObject target)
        {
            Component[] components = target.GetComponentsInChildren<Component>(true);
            return components.Any(component => component is IVisionPublisher) &&
                   components.Any(component => component is IVisionFrameSource);
        }

        internal static bool IsUnityEventUnwired(object reflectedValue) =>
            reflectedValue == null ||
            reflectedValue is UnityEvent unityEvent && unityEvent.GetPersistentEventCount() == 0;
    }
}
