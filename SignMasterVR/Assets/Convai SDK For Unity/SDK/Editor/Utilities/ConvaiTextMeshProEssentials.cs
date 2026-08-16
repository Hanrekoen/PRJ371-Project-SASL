using UnityEditor;

namespace Convai.Editor.Utilities
{
    /// <summary>
    ///     Knows whether TextMesh Pro's runtime resources are present in the project, and can import
    ///     them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         TextMesh Pro's runtime shaders and default font do not ship inside
    ///         <c>com.unity.ugui</c>. The package carries them as
    ///         <c>Package Resources/TMP Essential Resources.unitypackage</c>, which Unity unpacks
    ///         into the consuming project's own <c>Assets/TextMesh Pro/</c> folder. It is therefore
    ///         a per-project import, not a package dependency, and it cannot be declared in
    ///         <c>package.json</c> — no UPM dependency expresses "and the user must also run this
    ///         menu item".
    ///     </para>
    ///     <para>
    ///         Convai's shipped UI prefabs and its own Inter font assets reference those resources,
    ///         so without them the SDK's UI does not merely look wrong: opening a scene that
    ///         contains Convai UI raises a <c>NullReferenceException</c> inside
    ///         <c>TextMeshProUGUI.Cull</c>. Measured, not assumed — it is what a clean consumer
    ///         project does, and it is why this check exists rather than a line in the docs alone.
    ///     </para>
    ///     <para>
    ///         The two guids below are fixed by the unitypackage, so every project that imports the
    ///         essentials resolves the same values. They are the same guids
    ///         <c>ShippedAssetIntegrityTests</c> exempts, and it reads them from here so the
    ///         exemption and the check can never drift apart.
    ///     </para>
    /// </remarks>
    internal static class ConvaiTextMeshProEssentials
    {
        /// <summary>
        ///     <c>Assets/TextMesh Pro/Shaders/TMP_SDF.shader</c> — the runtime SDF shader every
        ///     TextMesh Pro font material points at, including Convai's own Inter assets.
        /// </summary>
        internal const string SdfShaderGuid = "68e6db2ebdc24f95958faec2be5558d6";

        /// <summary>
        ///     <c>Assets/TextMesh Pro/Resources/Fonts &amp; Materials/LiberationSans SDF.asset</c> —
        ///     TextMesh Pro's default font, used by the shipped Convai UI prefabs.
        /// </summary>
        internal const string DefaultFontGuid = "8f586378b4e144a9851e7b34d9b748ee";

        /// <summary>Unity's own importer, invoked by menu path so no assembly reference is needed.</summary>
        private const string ImportMenuPath = "Window/TextMeshPro/Import TMP Essential Resources";

        /// <summary>What to tell a user who has to do this by hand.</summary>
        internal const string ImportInstruction =
            "Import them with Window > TextMeshPro > Import TMP Essential Resources.";

        /// <summary>
        ///     <c>true</c> when both resources Convai's UI depends on resolve in this project.
        /// </summary>
        internal static bool AreImported =>
            Resolves(SdfShaderGuid) && Resolves(DefaultFontGuid);

        /// <summary>
        ///     Runs Unity's own essentials importer. Returns <c>false</c> when the menu item is
        ///     unavailable, in which case the caller should fall back to
        ///     <see cref="ImportInstruction" /> rather than reporting success.
        /// </summary>
        internal static bool TryImport() => EditorApplication.ExecuteMenuItem(ImportMenuPath);

        private static bool Resolves(string guid) =>
            !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid));
    }
}
