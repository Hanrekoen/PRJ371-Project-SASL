using UnityEditor;
using UnityEngine;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     Resolves the Convai emblem shown in editor headers, caching the result for the editor
    ///     session.
    /// </summary>
    /// <remarks>
    ///     Resolution order is project setting first, then the shipped package asset, then a Unity
    ///     built-in as a last resort. The final fallback to <see cref="Texture2D.whiteTexture" />
    ///     matters: header drawing must never return null, or an editor opened before the asset
    ///     database is ready would throw instead of simply rendering without the emblem for one
    ///     repaint. Only a real Convai emblem is cached as the emblem — a miss is retried, so the
    ///     emblem appears as soon as it becomes available.
    /// </remarks>
    internal static class ConvaiEditorIcons
    {
        private static readonly string[] CandidateIconPaths =
        {
            "Packages/com.convai.convai-sdk-for-unity/SDK/Editor/Art/UI/Branding/Convai Icon.png"
        };

        /// <summary>
        ///     How long a failed resolution is trusted before the asset database is asked again.
        /// </summary>
        /// <remarks>
        ///     A retry is genuinely wanted — see <see cref="Emblem" /> — but it must not be a retry
        ///     <em>per repaint</em>. This method is called from every Convai header, an inspector
        ///     repaints on every mouse move over it, and eleven Convai inspectors repaint continuously
        ///     in Play mode, so an unthrottled miss became hundreds of asset-database lookups a
        ///     second on a project whose emblem was missing or still importing. Half a second keeps
        ///     "assign the icon and it appears" indistinguishable from instant while making that
        ///     traffic negligible.
        /// </remarks>
        private const double RetryIntervalSeconds = 0.5d;

        private static Texture2D s_icon;
        private static Texture2D s_standIn;
        private static double s_nextRetryTime;

        /// <summary>Returns the Convai emblem. Never null.</summary>
        internal static Texture2D Emblem()
        {
            if (s_icon != null)
                return s_icon;

            if (EditorApplication.timeSinceStartup < s_nextRetryTime)
                return StandIn();

            s_nextRetryTime = EditorApplication.timeSinceStartup + RetryIntervalSeconds;

            if (ConvaiEditorSettings.Instance != null && ConvaiEditorSettings.Instance.ConvaiIconTexture != null)
            {
                s_icon = ConvaiEditorSettings.Instance.ConvaiIconTexture;
                return s_icon;
            }

            for (int i = 0; i < CandidateIconPaths.Length; i++)
            {
                s_icon = AssetDatabase.LoadAssetAtPath<Texture2D>(CandidateIconPaths[i]);
                if (s_icon != null)
                    return s_icon;
            }

            return StandIn();
        }

        /// <summary>
        ///     The Unity built-in shown while no Convai emblem has resolved.
        /// </summary>
        /// <remarks>
        ///     Cached separately from <see cref="s_icon" />, and that separation is the whole point.
        ///     The two lookups in <see cref="Emblem" /> can both miss simply because the asset database
        ///     was not ready yet, or because the user has not assigned a custom icon <em>yet</em>;
        ///     storing the stand-in as the emblem would pin the emblem-less look for the rest of the
        ///     session and make "I set the icon and nothing happened" a domain-reload-to-fix bug. Held
        ///     under its own field, it is resolved once and costs nothing thereafter, while the real
        ///     emblem keeps being looked for.
        /// </remarks>
        private static Texture2D StandIn()
        {
            if (s_standIn != null)
                return s_standIn;

            s_standIn = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image as Texture2D;
            return s_standIn != null ? s_standIn : Texture2D.whiteTexture;
        }
    }
}
