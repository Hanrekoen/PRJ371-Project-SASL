using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     The one place authoring tooling creates an action behavior component, so every entry point
    ///     agrees about which object it lands on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Action behaviors may live on the Convai Character or on a child object assigned as the
    ///         character's action behaviors object. Both layouts run identically — but before this
    ///         seam existed, every "add behavior" path in the editor called
    ///         <c>Undo.AddComponent(source.gameObject, …)</c> directly, in four separate places. A
    ///         user who arranged their behaviors onto a child had that arrangement quietly undone by
    ///         the next thing they clicked, which made an organized hierarchy impossible to keep
    ///         rather than merely undocumented.
    ///     </para>
    ///     <para>
    ///         Deliberately knows nothing about which behaviors exist — it takes a
    ///         <see cref="Type" />. Which concrete behavior stands in for an unwired action stays in
    ///         <c>ConvaiActionsAuthoringDefaults</c>; the two seams answer different questions and
    ///         must not merge.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionBehaviorHosting
    {
        /// <summary>
        ///     Name given to a newly created action behaviors object. Purely a starting point — the
        ///     object is referenced, never looked up by name, so renaming it breaks nothing.
        /// </summary>
        internal const string DefaultHostName = "Action Behaviors";

        /// <summary>
        ///     Creates <paramref name="behaviorType" /> on <paramref name="source" />'s action
        ///     behaviors object — the assigned child when there is one, the character itself
        ///     otherwise — as an undoable operation, and returns it.
        /// </summary>
        internal static MonoBehaviour AddBehavior(ConvaiActionConfigSource source, Type behaviorType)
        {
            if (source == null || behaviorType == null)
                return null;

            GameObject host = source.BehaviorHost;
            var behavior = Undo.AddComponent(host, behaviorType) as MonoBehaviour;
            EditorUtility.SetDirty(host);
            return behavior;
        }

        /// <summary>
        ///     Creates a child object to hold this character's action behaviors and assigns it, as one
        ///     undoable step. Existing behaviors are deliberately left exactly where they are: moving
        ///     components between objects means recreating them and re-pointing every reference, and a
        ///     missed reference is an action that silently stops working. From here on, new behaviors
        ///     are created on the child and the author moves the existing ones at their own pace, with
        ///     the Actions Editor confirming each one stays bound.
        /// </summary>
        /// <returns>The host object, or the existing one when this character already has a valid one.</returns>
        internal static Transform CreateBehaviorHost(ConvaiActionConfigSource source)
        {
            if (source == null)
                return null;

            if (source.ConfiguredBehaviorHost != null && source.HasValidBehaviorHost)
                return source.ConfiguredBehaviorHost;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Action Behaviors Object");

            var host = new GameObject(DefaultHostName);
            Undo.RegisterCreatedObjectUndo(host, "Create Action Behaviors Object");
            Undo.SetTransformParent(host.transform, source.transform, "Create Action Behaviors Object");

            // Identity local transform, always. A behavior is supposed to read the character's
            // transform rather than its own, but a customer-written one that forgets would act
            // relative to this object — so it is never given an offset to act relative to.
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            Undo.RecordObject(source, "Create Action Behaviors Object");
            source.SetBehaviorHost(host.transform);
            EditorUtility.SetDirty(source);

            Undo.CollapseUndoOperations(group);
            return host.transform;
        }

        /// <summary>
        ///     Copies every action behavior that is still on the Convai Character onto its action
        ///     behaviors object, and repoints this character's actions at the copies —
        ///     <em>without deleting anything</em>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Moving components by hand is the documented route, but on a character carrying
        ///         twenty behaviors it is twenty rounds of copy-paste-delete, and the order matters in
        ///         a way that is easy to get wrong: delete first and every action reads as unbound in
        ///         the Actions Editor even though it still runs, because the editor shows the authored
        ///         reference while the runtime rebinds by type name. This does the safe part in one
        ///         step and in the right order — create, then repoint — so nothing is ever unbound.
        ///     </para>
        ///     <para>
        ///         Deliberately leaves the originals in place. Creating a component can only ever add
        ///         something; destroying one silently nulls every reference held anywhere else in the
        ///         project, which is precisely the failure mode an automated move must not have. The
        ///         author deletes the now-unused originals themselves, once they can see that every
        ///         action still shows a bound behavior — the one irreversible step, taken last and
        ///         with the evidence already on screen.
        ///     </para>
        ///     <para>
        ///         A behavior whose type already exists on the host is not copied again; its actions
        ///         are simply repointed at the existing one.
        ///     </para>
        /// </remarks>
        /// <returns>How many behaviors were copied and how many actions were repointed.</returns>
        internal static (int Copied, int Repointed) CopyBehaviorsToHost(ConvaiActionConfigSource source)
        {
            if (source == null || !source.HasValidBehaviorHost)
                return (0, 0);

            GameObject host = source.BehaviorHost;
            if (host == source.gameObject)
                return (0, 0);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Copy Action Behaviors To Object");

            var replacements = new Dictionary<MonoBehaviour, MonoBehaviour>();
            var onCharacter = source.GetComponents<MonoBehaviour>();
            int copied = 0;

            for (int i = 0; i < onCharacter.Length; i++)
            {
                MonoBehaviour original = onCharacter[i];
                if (original is not IConvaiActionExecutor)
                    continue;

                Type behaviorType = original.GetType();
                if (host.GetComponent(behaviorType) is MonoBehaviour existing)
                {
                    replacements[original] = existing;
                    continue;
                }

                if (!ComponentUtility.CopyComponent(original) || !ComponentUtility.PasteComponentAsNew(host))
                    continue;

                if (host.GetComponent(behaviorType) is not MonoBehaviour copy)
                    continue;

                Undo.RegisterCreatedObjectUndo(copy, "Copy Action Behaviors To Object");
                replacements[original] = copy;
                copied++;
            }

            int repointed = RepointReferencesIn(source, replacements);

            // The copies carry their original's references verbatim, so a Run In Order copied onto
            // the host still lists the steps that live back on the character. Left alone, the host
            // would depend on the very components this move exists to retire. Only the host's own
            // components are rewritten — a bounded, visible sweep, not a project-wide one.
            var onHost = host.GetComponents<Component>();
            for (int i = 0; i < onHost.Length; i++)
                RepointReferencesIn(onHost[i], replacements);

            EditorUtility.SetDirty(host);
            Undo.CollapseUndoOperations(group);
            return (copied, repointed);
        }

        /// <summary>
        ///     Removes the action behaviors on the Convai Character that have already been copied onto
        ///     its behaviors object and that nothing else refers to.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Fails closed, by design. A behavior is removed only when a component of its type
        ///         exists on the host <em>and</em> a scan of the open scene finds nothing still
        ///         pointing at it. Anything else is left exactly where it is and named in the result,
        ///         so the author repoints it and runs this again. The command never removes something
        ///         it cannot show is unused — the whole reason component-moving was not automated is
        ///         that a destroyed component nulls references silently, and refusing is the only
        ///         honest answer when the evidence is not there.
        ///     </para>
        ///     <para>
        ///         The scan sees the open scene. References from another scene, or from a prefab asset
        ///         that is not open, are outside it — which is why
        ///         <see cref="IsPrefabInstance" /> is reported to the caller rather than handled here.
        ///     </para>
        /// </remarks>
        /// <returns>
        ///     How many were removed, and the behaviors that were deliberately left behind with the
        ///     reason they could not be proven unused.
        /// </returns>
        internal static (int Removed, List<string> Blocked) RemoveCopiedOriginals(ConvaiActionConfigSource source)
        {
            var blocked = new List<string>();
            if (source == null || !source.HasValidBehaviorHost)
                return (0, blocked);

            GameObject host = source.BehaviorHost;
            if (host == source.gameObject)
                return (0, blocked);

            HashSet<MonoBehaviour> referenced = FindReferencedBehaviors(source);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove Copied Action Behaviors");

            var onCharacter = source.GetComponents<MonoBehaviour>();
            int removed = 0;

            for (int i = 0; i < onCharacter.Length; i++)
            {
                MonoBehaviour original = onCharacter[i];
                if (original is not IConvaiActionExecutor)
                    continue;

                string label = ConvaiComponentTypeResolver.DisplayName(original.GetType());

                if (host.GetComponent(original.GetType()) == null)
                {
                    blocked.Add($"{label} — no copy on the behaviors object yet");
                    continue;
                }

                if (referenced.Contains(original))
                {
                    blocked.Add($"{label} — something in the scene still points at this one");
                    continue;
                }

                Undo.DestroyObjectImmediate(original);
                removed++;
            }

            EditorUtility.SetDirty(source.gameObject);
            Undo.CollapseUndoOperations(group);
            return (removed, blocked);
        }

        /// <summary>Whether this character is a prefab instance, so scene-only evidence is incomplete.</summary>
        internal static bool IsPrefabInstance(ConvaiActionConfigSource source) =>
            source != null && PrefabUtility.IsPartOfPrefabInstance(source.gameObject);

        /// <summary>
        ///     Rewrites this character's authored action definitions so each one names the behavior on
        ///     the host rather than the one on the character. Only the definitions serialized on this
        ///     component are touched — Action Set assets cannot hold a scene reference and rebind
        ///     themselves by type name.
        /// </summary>
        /// <summary>
        ///     Rewrites every object reference on <paramref name="owner" /> that names a replaced
        ///     component so it names the replacement instead.
        /// </summary>
        private static int RepointReferencesIn(
            UnityEngine.Object owner,
            Dictionary<MonoBehaviour, MonoBehaviour> replacements)
        {
            if (owner == null || replacements.Count == 0)
                return 0;

            var serialized = new SerializedObject(owner);
            SerializedProperty property = serialized.GetIterator();
            int repointed = 0;

            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                if (property.objectReferenceValue is not MonoBehaviour current)
                    continue;

                if (!replacements.TryGetValue(current, out MonoBehaviour replacement) || replacement == null)
                    continue;

                property.objectReferenceValue = replacement;
                repointed++;
            }

            if (repointed > 0)
                serialized.ApplyModifiedProperties();

            return repointed;
        }

        /// <summary>
        ///     Action behaviors on the Convai Character that anything in the open scene still points
        ///     at — a <c>Run In Order</c> step, a <c>Raise Unity Event</c> target, an action
        ///     definition, a custom script.
        /// </summary>
        /// <remarks>
        ///     The behaviors on the character are excluded as <em>sources</em> of references, because
        ///     they are all retiring together: one naming another is not a reason to keep either.
        ///     Everything else in the scene counts, including this character's own action definitions
        ///     — a definition still naming an original is exactly the case where removing it would
        ///     break an action.
        /// </remarks>
        internal static HashSet<MonoBehaviour> FindReferencedBehaviors(ConvaiActionConfigSource source)
        {
            var referenced = new HashSet<MonoBehaviour>();
            if (source == null)
                return referenced;

            var behaviors = new HashSet<MonoBehaviour>();
            var onCharacter = source.GetComponents<MonoBehaviour>();
            for (int i = 0; i < onCharacter.Length; i++)
            {
                if (onCharacter[i] is IConvaiActionExecutor)
                    behaviors.Add(onCharacter[i]);
            }

            if (behaviors.Count == 0)
                return referenced;

            foreach (MonoBehaviour candidate in ConvaiObjectFind.All<MonoBehaviour>(
                         FindObjectsInactive.Include))
            {
                if (candidate == null || behaviors.Contains(candidate))
                    continue;

                var serialized = new SerializedObject(candidate);
                SerializedProperty property = serialized.GetIterator();
                while (property.NextVisible(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue is MonoBehaviour target &&
                        behaviors.Contains(target))
                        referenced.Add(target);
                }
            }

            return referenced;
        }

        /// <summary>
        ///     Points this character's action behaviors at <paramref name="host" />, or back at the
        ///     character itself when it is null, as an undoable operation.
        /// </summary>
        internal static void SetBehaviorHost(ConvaiActionConfigSource source, Transform host)
        {
            if (source == null)
                return;

            Undo.RecordObject(source, "Set Action Behaviors Object");
            source.SetBehaviorHost(host);
            EditorUtility.SetDirty(source);
        }
    }
}
