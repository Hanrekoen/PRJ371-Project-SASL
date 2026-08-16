using System.Collections.Generic;
using Convai.Modules.BodyAnimation.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>Bootstraps editable motion-phrase ranges for limited talk content.</summary>
    internal static class TalkFragmentSuggestionUtility
    {
        public static int Generate(ConvaiBodyAnimationSet set)
        {
            if (set == null) return 0;
            Undo.RecordObject(set, "Generate Talk Motion Phrase Suggestions");
            int changed = 0;
            for (int i = 0; i < set.Talks.Count; i++)
            {
                TalkEntry entry = set.Talks[i];
                if (entry == null || entry.Clip == null || entry.HasFragments) continue;
                var fragments = new List<TalkMotionFragment>(3);
                Add(fragments, 0.03f, 0.31f, "Opening Phrase");
                Add(fragments, 0.36f, 0.64f, "Conversational Phrase");
                Add(fragments, 0.69f, 0.96f, "Closing Phrase");
                entry.ReplaceFragments(fragments);
                changed++;
            }
            if (changed > 0)
            {
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
            }
            return changed;
        }

        private static void Add(List<TalkMotionFragment> into, float start, float end, string label)
        {
            var fragment = new TalkMotionFragment();
            fragment.Initialize(start, end, 1f, label);
            into.Add(fragment);
        }
    }
}
