using Convai.Editor.Compatibility;
using Convai.Shared.Compatibility;
using UnityEngine;

namespace Convai.Editor.AI
{
    /// <summary>
    ///     The MCP tool surface's name for object identity. Tools exchange <c>instanceId</c> values
    ///     with an assistant, so the vocabulary stays MCP-shaped here while the actual identity
    ///     representation — which differs per Unity version — lives in
    ///     <see cref="ConvaiObjectId" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ids are valid only within the session that produced them; that has always been true of
    ///         Unity object ids and is not a consequence of the version split.
    ///     </para>
    ///     <para>
    ///         Resolution goes through <see cref="ConvaiEditorObjectId" /> rather than the runtime
    ///         seam: an assistant hands an id back on a later tool call, by which time Unity may have
    ///         unloaded the asset it names, and only the editor half can load it again.
    ///     </para>
    /// </remarks>
    internal static class ConvaiMcpEntityRef
    {
        internal static long ToToolId(Object value) => ConvaiObjectId.Of(value);

        internal static Object Resolve(long id) =>
            ConvaiEditorObjectId.TryResolve(id, out Object value) ? value : null;

        internal static bool TryResolve<T>(long id, out T value) where T : Object =>
            ConvaiEditorObjectId.TryResolve(id, out value);
    }
}
