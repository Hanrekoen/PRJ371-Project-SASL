using System.Collections.Generic;
using Convai.Runtime;
using Convai.Shared.Actions;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Pure, GUI-free draft/builder for a runtime <see cref="ConvaiActionConfigPatch" /> plus its
    ///     paired top-level attention override: tracks which fields are included (an unchecked field
    ///     is omitted/preserved; a checked field with no rows/text is an explicit clear) and clones
    ///     authored lists so edits never touch the confirmed snapshot.
    /// </summary>
    /// <remarks>
    ///     Backs the Live mode Advanced &gt; Runtime Session State &amp; Patch Composer card
    ///     (<c>ConvaiActionsEditorWindow.LiveAdvanced</c>) —
    ///     the only editor surface that composes, previews, and sends a runtime
    ///     <see cref="ConvaiActionConfigPatch" />. Kept as its own file rather than nested in the
    ///     window class so it stays independently unit-testable without a GUI dependency.
    /// </remarks>
    internal sealed class ConvaiActionPatchDraft
    {
        public bool IncludeActions { get; set; }
        public string ActionsText { get; set; } = string.Empty;
        public bool IncludeObjects { get; set; }
        public List<ConvaiActionObjectDefinition> Objects { get; } = new();
        public bool IncludeCharacters { get; set; }
        public List<ConvaiActionCharacterDefinition> Characters { get; } = new();
        public bool IncludeNestedAttention { get; set; }
        public string NestedAttention { get; set; } = string.Empty;
        public bool IncludeTopLevelAttention { get; set; }
        public string TopLevelAttention { get; set; } = string.Empty;
        public ConvaiRespondMode Reaction { get; set; } = ConvaiRespondMode.Silent;
        public string UpdateId { get; set; } = string.Empty;

        public bool HasMutation =>
            IncludeActions ||
            IncludeObjects ||
            IncludeCharacters ||
            IncludeNestedAttention ||
            IncludeTopLevelAttention;

        public ConvaiActionConfigPatch BuildActionConfigPatch()
        {
            if (!IncludeActions && !IncludeObjects && !IncludeCharacters && !IncludeNestedAttention)
                return null;

            return new ConvaiActionConfigPatch
            {
                Actions = IncludeActions ? ParseActionLines(ActionsText) : null,
                Objects = IncludeObjects ? CloneObjects(Objects) : null,
                Characters = IncludeCharacters ? CloneCharacters(Characters) : null,
                CurrentAttentionObject = IncludeNestedAttention ? NestedAttention ?? string.Empty : null
            };
        }

        public object BuildTopLevelAttention() =>
            IncludeTopLevelAttention ? TopLevelAttention ?? string.Empty : null;

        public void Load(ConvaiActionConfig config)
        {
            config ??= new ConvaiActionConfig();
            IncludeActions = true;
            ActionsText = config.Actions == null ? string.Empty : string.Join("\n", config.Actions);
            IncludeObjects = true;
            Objects.Clear();
            Objects.AddRange(CloneObjects(config.Objects));
            IncludeCharacters = true;
            Characters.Clear();
            Characters.AddRange(CloneCharacters(config.Characters));
            IncludeNestedAttention = true;
            NestedAttention = config.CurrentAttentionObject ?? string.Empty;
            IncludeTopLevelAttention = false;
            TopLevelAttention = string.Empty;
        }

        internal static List<string> ParseActionLines(string text)
        {
            var actions = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return actions;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string action = lines[i]?.Trim();
                if (!string.IsNullOrEmpty(action))
                    actions.Add(action);
            }

            return actions;
        }

        private static List<ConvaiActionObjectDefinition> CloneObjects(
            IReadOnlyList<ConvaiActionObjectDefinition> source)
        {
            var clone = new List<ConvaiActionObjectDefinition>(source?.Count ?? 0);
            if (source == null)
                return clone;

            for (int i = 0; i < source.Count; i++)
                clone.Add(source[i]?.Clone());
            return clone;
        }

        private static List<ConvaiActionCharacterDefinition> CloneCharacters(
            IReadOnlyList<ConvaiActionCharacterDefinition> source)
        {
            var clone = new List<ConvaiActionCharacterDefinition>(source?.Count ?? 0);
            if (source == null)
                return clone;

            for (int i = 0; i < source.Count; i++)
                clone.Add(source[i]?.Clone());
            return clone;
        }
    }
}
