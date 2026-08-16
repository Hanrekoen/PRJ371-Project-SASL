using System.Linq;
using Convai.Runtime;
using Convai.Runtime.Components;
using Convai.Shared.Compatibility;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Editor.AI
{
    internal static class ConvaiMcpResolvers
    {
        internal const string CharacterErrorCode = "INVALID_CHARACTER";
        internal const string ManagerErrorCode = "MANAGER_AMBIGUOUS";

        internal static bool TryCharacter(
            long id,
            bool includeInactive,
            out ConvaiCharacter character,
            out string error)
        {
            if (id != 0)
            {
                ConvaiMcpEntityRef.TryResolve(id, out character);
                if (character != null && character.gameObject.scene == SceneManager.GetActiveScene())
                {
                    error = string.Empty;
                    return true;
                }

                character = null;
                error = "characterInstanceId must identify ConvaiCharacter in the active scene.";
                return false;
            }

            ConvaiCharacter[] candidates = ConvaiObjectFind.All<ConvaiCharacter>(includeInactive)
                .Where(value => value.gameObject.scene == SceneManager.GetActiveScene())
                .ToArray();
            character = candidates.Length == 1 ? candidates[0] : null;
            error = candidates.Length switch
            {
                0 => "No ConvaiCharacter exists in the active scene.",
                > 1 => "Multiple ConvaiCharacter components exist; provide characterInstanceId.",
                _ => string.Empty
            };
            return character != null;
        }

        internal static bool TryManager(
            long id,
            bool includeInactive,
            out ConvaiManager manager,
            out string error)
        {
            if (id != 0)
            {
                ConvaiMcpEntityRef.TryResolve(id, out manager);
                if (manager != null && manager.gameObject.scene == SceneManager.GetActiveScene())
                {
                    error = string.Empty;
                    return true;
                }

                manager = null;
                error = "managerInstanceId must identify ConvaiManager in the active scene.";
                return false;
            }

            ConvaiManager[] candidates = ConvaiObjectFind.All<ConvaiManager>(includeInactive)
                .Where(value => value.gameObject.scene == SceneManager.GetActiveScene())
                .ToArray();
            manager = candidates.Length == 1 ? candidates[0] : null;
            error = candidates.Length switch
            {
                0 => "No ConvaiManager exists in the active scene.",
                > 1 => "Multiple ConvaiManager components exist; provide managerInstanceId.",
                _ => string.Empty
            };
            return manager != null;
        }

        internal static bool TryHost(
            long id,
            GameObject fallback,
            bool allowComponentCarrier,
            out GameObject host,
            out string error)
        {
            host = id == 0
                ? fallback
                : allowComponentCarrier
                    ? ResolveGameObject(id)
                    : ConvaiMcpEntityRef.Resolve(id) as GameObject;
            if (host != null && host.scene == SceneManager.GetActiveScene())
            {
                error = string.Empty;
                return true;
            }

            host = null;
            error = $"Host instance ID {id} must identify a GameObject in active scene.";
            return false;
        }

        private static GameObject ResolveGameObject(long id)
        {
            ConvaiMcpEntityRef.TryResolve(id, out GameObject host);
            return host;
        }
    }
}
