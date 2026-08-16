using Convai.Runtime.Components;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Where the player actually is — the transform their rig displaces, which is not always the
    ///     one carrying <see cref="ConvaiPlayer" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this is public.</b> It was internal, and the rule it holds is not one anybody
    ///         guesses: first-person controllers — Unity's own Starter Assets among them — put the
    ///         <see cref="CharacterController" /> on a capsule <i>inside</i> the rig and move that,
    ///         leaving the prefab root parked at the spawn point forever. This SDK's own movement
    ///         behaviors resolved it correctly and every Action Behavior written outside the package
    ///         had to rediscover it. One did not, and the copy that was missing the step shipped in
    ///         this repository's demo — a character who led the visitor somewhere, stopped to wait,
    ///         and then ignored them standing right beside her.
    ///     </para>
    ///     <para>
    ///         <see cref="ConvaiActionExecutorBase.ResolvePlayer" /> is the shorthand for this
    ///         inside an Action Behavior; this type is here for scene code that is not one.
    ///     </para>
    /// </remarks>
    public static class ConvaiPlayerBody
    {
        /// <summary>
        ///     The transform to measure the player by, or <c>null</c> when the scene has neither a
        ///     <see cref="ConvaiPlayer" /> nor a main camera.
        /// </summary>
        /// <remarks>
        ///     The Convai Player first, because a scene that has one has said explicitly where the
        ///     player is; the main camera otherwise, because a scene without one is almost always
        ///     first-person, where the camera <i>is</i> the player. Falling back the other way round
        ///     would have a character in a third-person scene take up position in front of the
        ///     camera rig rather than in front of the person.
        /// </remarks>
        public static Transform Resolve()
        {
            var player = Object.FindAnyObjectByType<ConvaiPlayer>(FindObjectsInactive.Exclude);
            if (player != null)
                return ResolveMovingBody(player);

            Camera main = Camera.main;
            return main != null ? main.transform : null;
        }

        /// <summary>
        ///     Where the player is standing, flattened onto a given height.
        /// </summary>
        /// <remarks>
        ///     Cameras sit at head height, and a character that measures distance to one stands
        ///     slightly too far away — every time, in every scene where the camera is the player.
        /// </remarks>
        /// <param name="floorHeight">The height to answer at — usually the character's own.</param>
        /// <param name="position">Where the player is, at that height.</param>
        /// <returns>False when there is nobody to measure to.</returns>
        public static bool TryResolveFloorPosition(float floorHeight, out Vector3 position)
        {
            Transform player = Resolve();
            if (player == null)
            {
                position = default;
                return false;
            }

            position = player.position;
            position.y = floorHeight;
            return true;
        }

        /// <summary>
        ///     The body inside the rig that the controller moves, falling back to the rig itself.
        /// </summary>
        /// <remarks>
        ///     Costs nothing when the rig is wired the other way round:
        ///     <see cref="Component.GetComponentInChildren{T}(bool)" /> matches the object itself
        ///     first, so a <see cref="ConvaiPlayer" /> that already sits on the moving body resolves
        ///     to exactly the same transform as before.
        /// </remarks>
        private static Transform ResolveMovingBody(ConvaiPlayer player)
        {
            var controller = player.GetComponentInChildren<CharacterController>(true);
            if (controller != null)
                return controller.transform;

            var body = player.GetComponentInChildren<Rigidbody>(true);
            if (body != null)
                return body.transform;

            return player.transform;
        }
    }
}
