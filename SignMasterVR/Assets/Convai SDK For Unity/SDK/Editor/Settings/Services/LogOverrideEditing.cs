using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using UnityEditor;

namespace Convai.Editor.Settings.Services
{
    /// <summary>
    ///     SerializedObject-based editing of the ConvaiSettings logging block
    ///     (global level, flags, per-category overrides). Pure data manipulation so it
    ///     stays unit-testable; callers apply/save.
    /// </summary>
    public static class LogOverrideEditing
    {
        private const string GlobalLogLevelProperty = "_globalLogLevel";
        private const string IncludeStackTracesProperty = "_includeStackTraces";
        private const string ColoredOutputProperty = "_coloredOutput";
        private const string CategoryOverridesProperty = "_categoryOverrides";
        private const string CategoryProperty = "Category";
        private const string LevelProperty = "Level";

        /// <summary>Gets the override level for a category, or null when inheriting.</summary>
        public static LogLevel? GetOverride(SerializedObject settings, LogCategory category)
        {
            SerializedProperty overrides = settings.FindProperty(CategoryOverridesProperty);
            if (overrides == null) return null;

            int index = IndexOf(overrides, category);
            if (index < 0) return null;

            return (LogLevel)overrides.GetArrayElementAtIndex(index)
                .FindPropertyRelative(LevelProperty).enumValueIndex;
        }

        /// <summary>Sets (level) or removes (null) the override for a category.</summary>
        public static void SetOverride(SerializedObject settings, LogCategory category, LogLevel? level)
        {
            SerializedProperty overrides = settings.FindProperty(CategoryOverridesProperty);
            if (overrides == null) return;

            int index = IndexOf(overrides, category);
            if (level.HasValue)
            {
                if (index < 0)
                {
                    index = overrides.arraySize;
                    overrides.InsertArrayElementAtIndex(index);
                    overrides.GetArrayElementAtIndex(index)
                        .FindPropertyRelative(CategoryProperty).enumValueIndex = (int)category;
                }

                overrides.GetArrayElementAtIndex(index)
                    .FindPropertyRelative(LevelProperty).enumValueIndex = (int)level.Value;
            }
            else if (index >= 0)
            {
                overrides.DeleteArrayElementAtIndex(index);
            }
        }

        /// <summary>Snapshot of all overrides, keyed by category.</summary>
        public static Dictionary<LogCategory, LogLevel> GetOverridesSnapshot(SerializedObject settings)
        {
            var snapshot = new Dictionary<LogCategory, LogLevel>();
            SerializedProperty overrides = settings.FindProperty(CategoryOverridesProperty);
            if (overrides == null) return snapshot;

            for (int i = 0; i < overrides.arraySize; i++)
            {
                SerializedProperty element = overrides.GetArrayElementAtIndex(i);
                SerializedProperty category = element.FindPropertyRelative(CategoryProperty);
                SerializedProperty level = element.FindPropertyRelative(LevelProperty);
                if (category == null || level == null) continue;

                snapshot[(LogCategory)category.enumValueIndex] = (LogLevel)level.enumValueIndex;
            }

            return snapshot;
        }

        /// <summary>Number of configured category overrides.</summary>
        public static int GetOverrideCount(SerializedObject settings) =>
            settings.FindProperty(CategoryOverridesProperty)?.arraySize ?? 0;

        /// <summary>Removes all category overrides.</summary>
        public static void ClearOverrides(SerializedObject settings)
        {
            SerializedProperty overrides = settings.FindProperty(CategoryOverridesProperty);
            if (overrides != null) overrides.arraySize = 0;
        }

        /// <summary>
        ///     Applies a logging preset: global level plus flags, clearing all overrides.
        /// </summary>
        public static void ApplyPreset(SerializedObject settings, LogLevel globalLevel, bool includeStackTraces,
            bool coloredOutput)
        {
            SetEnum(settings, GlobalLogLevelProperty, (int)globalLevel);
            SetBool(settings, IncludeStackTracesProperty, includeStackTraces);
            SetBool(settings, ColoredOutputProperty, coloredOutput);
            ClearOverrides(settings);
        }

        /// <summary>Restores the SDK default logging configuration (Info, traces on, color on, no overrides).</summary>
        public static void ResetToDefaults(SerializedObject settings) =>
            ApplyPreset(settings, LogLevel.Info, true, true);

        private static int IndexOf(SerializedProperty overrides, LogCategory category)
        {
            for (int i = 0; i < overrides.arraySize; i++)
            {
                SerializedProperty element = overrides.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative(CategoryProperty).enumValueIndex == (int)category) return i;
            }

            return -1;
        }

        private static void SetEnum(SerializedObject settings, string property, int value)
        {
            SerializedProperty prop = settings.FindProperty(property);
            if (prop != null) prop.enumValueIndex = value;
        }

        private static void SetBool(SerializedObject settings, string property, bool value)
        {
            SerializedProperty prop = settings.FindProperty(property);
            if (prop != null) prop.boolValue = value;
        }
    }
}
