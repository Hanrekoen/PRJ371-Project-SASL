using System;
using System.IO;
using System.Text;

namespace Convai.Editor
{
    internal enum ConvaiAgentClient
    {
        Codex,
        ClaudeCode,
        Cursor,
        Gemini,
        Copilot
    }

    internal static class ConvaiAgentInstructionsInstaller
    {
        internal const string BeginSentinel = "<!-- BEGIN CONVAI UNITY SDK -->";
        internal const string EndSentinel = "<!-- END CONVAI UNITY SDK -->";

        private const string ManagedInstructions =
            "Use the packaged `convai-unity-sdk` skill for Convai work.\n" +
            "Use Unity MCP tools for generic scene, GameObject, asset, script, console, and Play Mode operations.\n" +
            "Use Convai tools for SDK configuration and diagnosis. Inspect first; use returned instance IDs.\n" +
            "Do safe reversible work before asking questions. Preview mutators, then apply when requested.\n" +
            "Never request or expose API keys or transcript text. Never save scenes or change Play Mode unless explicitly asked.\n" +
            "Read `Packages/com.convai.convai-sdk-for-unity/AIAssistantSkills/convai-unity-sdk/SKILL.md` and its linked references.";

        internal static string GetRelativePath(ConvaiAgentClient client) => client switch
        {
            ConvaiAgentClient.Codex => "AGENTS.md",
            ConvaiAgentClient.ClaudeCode => "CLAUDE.md",
            ConvaiAgentClient.Cursor => ".cursor/rules/convai-unity-sdk.mdc",
            ConvaiAgentClient.Gemini => "GEMINI.md",
            ConvaiAgentClient.Copilot => ".github/copilot-instructions.md",
            _ => throw new ArgumentOutOfRangeException(nameof(client), client, null)
        };

        internal static bool ContainsManagedBlock(string projectRoot, ConvaiAgentClient client)
        {
            string path = ResolvePath(projectRoot, client);
            if (!File.Exists(path)) return false;
            string content = ReadAllTextStrict(path);
            return content.Contains(BeginSentinel, StringComparison.Ordinal) &&
                   content.Contains(EndSentinel, StringComparison.Ordinal);
        }

        internal static void Upsert(string projectRoot, ConvaiAgentClient client)
        {
            string path = ResolvePath(projectRoot, client);
            string original = File.Exists(path) ? ReadAllTextStrict(path) : string.Empty;
            string lineEnding = DetectLineEnding(original);
            string withoutBlock = RemoveManagedBlock(original);
            string block = BeginSentinel + lineEnding +
                           ManagedInstructions.Replace("\n", lineEnding) + lineEnding +
                           EndSentinel;

            string updated = AppendBlock(withoutBlock, block, lineEnding);
            WriteAtomic(path, updated);
        }

        internal static void Remove(string projectRoot, ConvaiAgentClient client)
        {
            string path = ResolvePath(projectRoot, client);
            if (!File.Exists(path)) return;
            string original = ReadAllTextStrict(path);
            string updated = RemoveManagedBlock(original);
            if (string.Equals(original, updated, StringComparison.Ordinal)) return;
            WriteAtomic(path, updated);
        }

        private static string ResolvePath(string projectRoot, ConvaiAgentClient client)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            return Path.Combine(projectRoot, GetRelativePath(client));
        }

        private static string ReadAllTextStrict(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                return reader.ReadToEnd();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Cannot read agent instruction file '{path}'.", exception);
            }
        }

        private static string RemoveManagedBlock(string content)
        {
            int begin = content.IndexOf(BeginSentinel, StringComparison.Ordinal);
            if (begin < 0) return content;
            int end = content.IndexOf(EndSentinel, begin, StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidOperationException("Convai instruction block has a begin sentinel but no end sentinel.");

            end += EndSentinel.Length;
            string prefix = content.Substring(0, begin).TrimEnd('\r', '\n');
            string suffix = content.Substring(end).TrimStart('\r', '\n');
            if (prefix.Length == 0) return suffix;
            if (suffix.Length == 0) return prefix;
            string lineEnding = DetectLineEnding(content);
            return prefix + lineEnding + lineEnding + suffix;
        }

        private static string AppendBlock(string content, string block, string lineEnding)
        {
            if (string.IsNullOrEmpty(content)) return block + lineEnding;
            string trimmed = content.TrimEnd('\r', '\n');
            return trimmed + lineEnding + lineEnding + block + lineEnding;
        }

        private static string DetectLineEnding(string content) =>
            content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        private static void WriteAtomic(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Instruction path has no directory.");
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Cannot atomically write agent instruction file '{path}'.", exception);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
