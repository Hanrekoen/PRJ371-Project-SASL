using System;
using System.Collections.Generic;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Editor.Ownership
{
    /// <summary>
    ///     Who owns a settings asset, and therefore whether editing it in place is the right thing
    ///     to do.
    /// </summary>
    /// <remarks>
    ///     The three answers are deliberately distinct, because they call for opposite treatment and
    ///     collapsing any two of them is how this became a defect in the first place:
    ///     <list type="bullet">
    ///         <item>
    ///             <see cref="ProjectOwned" /> — this character's own asset. Say nothing; the user is
    ///             editing exactly what they think they are editing.
    ///         </item>
    ///         <item>
    ///             <see cref="ProjectShared" /> — other characters in the open scenes read it too.
    ///             Editing all of them is a <b>legitimate intent</b>, so this is a notice, never a
    ///             refusal: name the count and offer a private copy.
    ///         </item>
    ///         <item>
    ///             <see cref="SdkOwned" /> — it lives in the package. Editing is never legitimate:
    ///             it is the default every future character inherits, the next SDK update replaces
    ///             it, and in a normally installed project the write is silently discarded. This one
    ///             is not warned about — it is designed out, and where it can still be reached the
    ///             copy happens for the user rather than being demanded of them.
    ///         </item>
    ///     </list>
    /// </remarks>
    internal enum ConvaiAssetOwnershipKind
    {
        /// <summary>No asset at all. The character runs on the module's code-defined defaults.</summary>
        None = 0,

        /// <summary>A project asset only this character reads.</summary>
        ProjectOwned,

        /// <summary>A project asset several characters read.</summary>
        ProjectShared,

        /// <summary>An asset that ships inside the Convai package.</summary>
        SdkOwned
    }

    /// <summary>
    ///     The one answer to "may this settings asset be edited here?", shared by every Convai
    ///     module and by the Convai MCP tools.
    /// </summary>
    /// <remarks>
    ///     This used to be four private implementations: Body Animation and Emotion each carried
    ///     their own ownership struct, path check and scene-scan cache, while Gaze, Body Language
    ///     and Conversation Flow carried none and let the user write to a package asset unwarned.
    ///     The two that existed had already diverged — one disabled its controls, the other left
    ///     them live and futile. One vocabulary is the only way that stays fixed; a guard test
    ///     (<c>AssetOwnershipGuardTests</c>) forbids a fifth private copy.
    /// </remarks>
    internal readonly struct ConvaiAssetOwnership
    {
        private ConvaiAssetOwnership(ConvaiAssetOwnershipKind kind, int userCount, bool shipsWithConvai)
        {
            Kind = kind;
            UserCount = userCount;
            ShipsWithConvai = shipsWithConvai;
        }

        internal ConvaiAssetOwnershipKind Kind { get; }

        /// <summary>
        ///     Whether this asset came from the Convai package specifically, rather than from some
        ///     other package in the project.
        /// </summary>
        /// <remarks>
        ///     The read-only verdict and the provenance are two different claims, and only the verdict
        ///     is the same for every package: nothing under <c>Packages/</c> can be written to in a
        ///     normally installed project, but only Convai's own content ships with the Convai SDK. A
        ///     studio that keeps its Convai settings in a package of its own would otherwise be told
        ///     its own file came from us.
        /// </remarks>
        internal bool ShipsWithConvai { get; }

        /// <summary>How many characters in the open scenes resolve this asset.</summary>
        internal int UserCount { get; }

        /// <summary>Whether a write to this asset would actually land and be kept.</summary>
        internal bool IsWritable =>
            Kind is ConvaiAssetOwnershipKind.ProjectOwned or ConvaiAssetOwnershipKind.ProjectShared;

        /// <summary>
        ///     Whether the character needs its own copy before an edit means anything. True only for
        ///     SDK-owned assets — sharing is a situation to report, not one to block.
        /// </summary>
        internal bool RequiresProjectCopy => Kind == ConvaiAssetOwnershipKind.SdkOwned;

        /// <summary>
        ///     Whether editing this asset in place would change something other than this character
        ///     alone — either because the SDK owns it or because other characters read it.
        /// </summary>
        /// <remarks>
        ///     The stricter question, and the one an unattended caller has to ask. A human dragging a
        ///     slider on a profile they can see is shared has decided to move all of them; an MCP tool
        ///     acting on "make Nova calmer" has decided nothing of the sort, so it takes consent
        ///     before touching a character nobody named.
        /// </remarks>
        internal bool EditingAffectsOthers =>
            Kind is ConvaiAssetOwnershipKind.SdkOwned or ConvaiAssetOwnershipKind.ProjectShared;

        /// <summary>Whether anything at all needs to be said to the user about this asset.</summary>
        internal bool HasNotice =>
            Kind is ConvaiAssetOwnershipKind.ProjectShared or ConvaiAssetOwnershipKind.SdkOwned;

        /// <summary>Heading for the notice; empty when there is nothing to say.</summary>
        internal string NoticeTitle => Kind switch
        {
            ConvaiAssetOwnershipKind.SdkOwned => ReadOnlyTitle(ShipsWithConvai),
            ConvaiAssetOwnershipKind.ProjectShared => $"Shared by {UserCount} characters",
            _ => string.Empty
        };

        /// <summary>
        ///     The heading for an asset that cannot be written to, naming where it actually came
        ///     from.
        /// </summary>
        internal static string ReadOnlyTitle(bool shipsWithConvai) =>
            shipsWithConvai ? "Read-only — part of the Convai SDK" : "Read-only — part of another package";

        /// <summary>
        ///     The opening sentence of a read-only explanation. Written once and shared, so the two
        ///     surfaces that explain this cannot end up describing the same file differently.
        /// </summary>
        internal static string ReadOnlyLead(bool shipsWithConvai) =>
            shipsWithConvai
                ? "These settings ship with the Convai SDK, so they cannot be changed here."
                : "These settings live inside a package, so they cannot be changed here.";

        /// <summary>
        ///     What is going on and what to do about it, in one breath. Written for someone opening
        ///     Unity for the first time: what this asset is, what happens if they change it, and the
        ///     single next step — never a recital of the three technical reasons behind the rule.
        /// </summary>
        internal string NoticeMessage => Kind switch
        {
            ConvaiAssetOwnershipKind.SdkOwned =>
                $"{ReadOnlyLead(ShipsWithConvai)} Create a copy in your project to make it your own.",
            ConvaiAssetOwnershipKind.ProjectShared =>
                $"{UserCount} characters in the open scenes use these settings, so a change here " +
                "changes all of them. Give this character its own copy to change it on its own.",
            _ => string.Empty
        };

        // ------------------------------------------------------------------ answering

        /// <summary>
        ///     Whether an asset lives in this project and may be edited in place.
        /// </summary>
        /// <remarks>
        ///     The SDK's shipped content lives under <c>Packages/</c>. Writing there is wrong even
        ///     when a single character reads it and even when the package happens to be embedded and
        ///     therefore writable, as it is in the SDK's own development project: it is still the
        ///     default every future character inherits, an update still overwrites it, and the change
        ///     still does not travel with the user's project.
        /// </remarks>
        internal static bool IsProjectAsset(Object asset)
        {
            if (asset == null) return false;

            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether an asset lives in a package rather than in this project's own assets.</summary>
        internal static bool IsSdkAsset(Object asset) => asset != null && !IsProjectAsset(asset);

        /// <summary>The Convai package's own folder, as Unity reports it in an asset path.</summary>
        private const string ConvaiPackageRoot = "Packages/com.convai.convai-sdk-for-unity/";

        /// <summary>
        ///     Whether an asset came from the Convai package rather than from another package.
        /// </summary>
        /// <remarks>
        ///     This only decides what the notice <i>says</i>. The verdict is unchanged either way —
        ///     no package is writable in a normally installed project — so a caller asking whether an
        ///     edit may proceed asks <see cref="IsProjectAsset" />, never this.
        /// </remarks>
        internal static bool IsConvaiPackageAsset(Object asset)
        {
            if (asset == null) return false;

            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith(ConvaiPackageRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     The exact answer, scanning the open scenes now. For commands, tools and tests —
        ///     anything making a one-shot decision rather than drawing.
        /// </summary>
        /// <param name="asset">The settings asset being judged.</param>
        /// <param name="resolveAssignedAsset">
        ///     How a character of type <typeparamref name="TComponent" /> resolves the asset it
        ///     actually reads. Passed in rather than inferred, because only the module knows whether
        ///     its asset arrives directly or through a profile — and a count that disagreed with the
        ///     runtime's own resolution would make the whole notice untrustworthy.
        /// </param>
        internal static ConvaiAssetOwnership Of<TComponent>(
            Object asset, Func<TComponent, Object> resolveAssignedAsset)
            where TComponent : Component =>
            Build(asset, () => CountUsers(asset, resolveAssignedAsset));

        /// <summary>
        ///     The same answer for draw paths, reusing a throttled scan.
        /// </summary>
        /// <remarks>
        ///     An inspector repaints every frame in Play Mode and the scan walks every object in the
        ///     loaded scenes, so counting per repaint makes a large scene's inspector visibly
        ///     stutter. The cache is invalidated on hierarchy changes and undo as well as by age, so
        ///     it cannot go stale in the way that actually matters — a character being added to or
        ///     removed from the asset while its notice is on screen.
        /// </remarks>
        internal static ConvaiAssetOwnership OfCached<TComponent>(
            Object asset, Func<TComponent, Object> resolveAssignedAsset)
            where TComponent : Component =>
            Build(asset, () => CachedCount(asset, resolveAssignedAsset));

        private static ConvaiAssetOwnership Build(Object asset, Func<int> count)
        {
            if (asset == null) return new ConvaiAssetOwnership(ConvaiAssetOwnershipKind.None, 0, false);

            // Asked before the scan on purpose: an SDK asset's verdict does not depend on how many
            // characters read it, and the scan is the expensive half. A fresh scene has exactly one
            // character on the shipped asset, which is precisely the case a count-based rule missed.
            if (IsSdkAsset(asset))
            {
                return new ConvaiAssetOwnership(
                    ConvaiAssetOwnershipKind.SdkOwned, count(), IsConvaiPackageAsset(asset));
            }

            int users = count();
            return new ConvaiAssetOwnership(
                users > 1 ? ConvaiAssetOwnershipKind.ProjectShared : ConvaiAssetOwnershipKind.ProjectOwned,
                users, false);
        }

        private static int CountUsers<TComponent>(Object asset, Func<TComponent, Object> resolve)
            where TComponent : Component
        {
            if (asset == null || resolve == null) return 0;

            TComponent[] components = ConvaiObjectFind.All<TComponent>(FindObjectsInactive.Include);
            int count = 0;
            for (int i = 0; i < components.Length; i++)
                if (resolve(components[i]) == asset)
                    count++;

            return count;
        }

        // ------------------------------------------------------------------ throttled scan

        /// <summary>Seconds a scan stays valid. Long enough to be free, short enough to feel live.</summary>
        private const double CacheLifetimeSeconds = 1d;

        private readonly struct CachedScan
        {
            internal CachedScan(int count, double takenAt)
            {
                Count = count;
                TakenAt = takenAt;
            }

            internal int Count { get; }
            internal double TakenAt { get; }
        }

        private static readonly Dictionary<Object, CachedScan> Scans = new();
        private static bool s_hooked;

        private static int CachedCount<TComponent>(Object asset, Func<TComponent, Object> resolve)
            where TComponent : Component
        {
            HookInvalidation();

            double now = EditorApplication.timeSinceStartup;
            if (Scans.TryGetValue(asset, out CachedScan cached) &&
                now - cached.TakenAt < CacheLifetimeSeconds)
                return cached.Count;

            int count = CountUsers(asset, resolve);
            Scans[asset] = new CachedScan(count, now);
            return count;
        }

        /// <summary>
        ///     Drops every cached scan. Called after a copy rewires a character, so the notice stops
        ///     describing an arrangement that no longer exists the instant it stops being true.
        /// </summary>
        internal static void Invalidate() => Scans.Clear();

        private static void HookInvalidation()
        {
            if (s_hooked) return;

            s_hooked = true;
            EditorApplication.hierarchyChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
        }
    }
}
