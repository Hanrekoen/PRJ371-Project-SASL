using Convai.Runtime.Room;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.PropertyDrawers
{
    [CustomPropertyDrawer(typeof(TurnTakingOptions))]
    internal sealed class TurnTakingOptionsDrawer : PropertyDrawer
    {
        private const string ModeFieldName = "<Mode>k__BackingField";
        private const string TurnDetectionFieldName = "<TurnDetection>k__BackingField";
        private const string CustomTurnDetectionFieldName = "<CustomTurnDetection>k__BackingField";
        private const string InitialServerSttFieldName = "<InitialServerStt>k__BackingField";
        private const string LocalAudioPolicyFieldName = "<LocalAudioPolicy>k__BackingField";
        private const string PushToTalkPolicyFieldName = "<PushToTalkPolicy>k__BackingField";
        private const string BargeInFieldName = "<BargeIn>k__BackingField";
        private const string SmoothInterruptionFieldName = "<SmoothInterruption>k__BackingField";
        private const string FadeOutSecondsFieldName = "<FadeOutSeconds>k__BackingField";
        private const string ClientDetectionFieldName = "<ClientDetection>k__BackingField";
        private const string StartMutedInPushToTalkFieldName = "<StartMutedInPushToTalk>k__BackingField";
        private const string EnableAcousticEchoCancellationFieldName = "<EnableAcousticEchoCancellation>k__BackingField";
        private const string PushToTalkStartupModeFieldName = "<PushToTalkStartupMode>k__BackingField";
        private const string EnableServerSttToggleFieldName = "<EnableServerSttToggle>k__BackingField";
        private const string InterruptBotOnPressFieldName = "<InterruptBotOnPress>k__BackingField";

        private const string RequireTurnCompletionBeforeNextPressFieldName =
            "<RequireTurnCompletionBeforeNextPress>k__BackingField";

        private const string TurnCompletionTimeoutMsFieldName = "<TurnCompletionTimeoutMs>k__BackingField";

        /// <summary>Left inset an explanatory paragraph loses inside its indented row.</summary>
        private const float ParagraphInset = 10f;

        /// <summary>Floor for the wrap width, so a very narrow Inspector still measures something sane.</summary>
        private const float MinParagraphWidth = 60f;

        /// <summary>Inspector chrome (margins plus the scrollbar) the measuring pass cannot see.</summary>
        private const float InspectorChrome = 60f;

        /// <summary>Width one <see cref="EditorGUI.indentLevel" /> step costs a row.</summary>
        private const float IndentStep = 15f;

        private const string ReleaseTailMsFieldName = "<ReleaseTailMs>k__BackingField";

        private const string AllowSpeechStoppedFallbackAfterSpeechStartFieldName =
            "<AllowSpeechStoppedFallbackAfterSpeechStart>k__BackingField";

        private const string StopSecsFieldName = "<StopSecs>k__BackingField";
        private const string PreSpeechMsFieldName = "<PreSpeechMs>k__BackingField";
        private const string MaxDurationSecsFieldName = "<MaxDurationSecs>k__BackingField";
        private const string PushToTalkTurnDetectionSummary =
            "Push To Talk does not use automatic turn detection. The user turn ends when the player releases the push-to-talk control, and the SDK sends force-user-stopped-speaking to the backend. That signal is the authoritative end of the turn and overrides smart turn for it, so the backend waits only briefly for the trailing transcript before responding.";

        private const string PushToTalkKeyOwnershipSummary =
            "Push-to-talk key binding is configured on ConvaiRoomManager for this scene. These settings control the advanced push-to-talk behavior.";

        private static readonly GUIContent[] HandsFreeTurnDetectionOptions =
        {
            new("SDK Default"), new("Custom Values")
        };

        private static readonly GUIContent[] AllTurnDetectionOptions =
        {
            new("SDK Default"), new("No Automatic Turn Detection"), new("Custom Values")
        };

        private static readonly GUIContent[] InitialServerSttOptions =
        {
            new("SDK Default"), new("Start Enabled"), new("Start Disabled")
        };

        private static readonly TurnDetectionMode[] HandsFreeTurnDetectionValues =
        {
            TurnDetectionMode.UseDefault, TurnDetectionMode.Custom
        };

        private static readonly TurnDetectionMode[] AllTurnDetectionValues =
        {
            TurnDetectionMode.UseDefault, TurnDetectionMode.Disabled, TurnDetectionMode.Custom
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            // OnGUI indents once past the foldout row, so measure at that depth.
            return height + GetContentHeight(property, EditorGUI.indentLevel + 1);
        }

        /// <summary>
        ///     Height of the option rows alone, without the foldout row. Lets hosts that already provide their
        ///     own collapsible group header render the options flat instead of nesting a second foldout.
        /// </summary>
        /// <param name="indentLevel">Indent the rows will actually be drawn at; narrows the wrap estimate.</param>
        internal static float GetContentHeight(SerializedProperty property, int indentLevel)
        {
            float height = 0f;
            SerializedProperty modeProp = property.FindPropertyRelative(ModeFieldName);
            SerializedProperty turnDetectionProp = property.FindPropertyRelative(TurnDetectionFieldName);
            SerializedProperty customTurnDetectionProp = property.FindPropertyRelative(CustomTurnDetectionFieldName);
            SerializedProperty initialServerSttProp = property.FindPropertyRelative(InitialServerSttFieldName);
            SerializedProperty localAudioPolicyProp = property.FindPropertyRelative(LocalAudioPolicyFieldName);
            SerializedProperty pushToTalkPolicyProp = property.FindPropertyRelative(PushToTalkPolicyFieldName);
            SerializedProperty bargeInProp = property.FindPropertyRelative(BargeInFieldName);

            ConversationInputMode mode = GetConversationMode(modeProp);
            float contentWidth = ParagraphWidth(EstimatedRowWidth(indentLevel));

            height += GetChildHeight(modeProp);
            if (mode == ConversationInputMode.HandsFree)
            {
                height += GetSingleLineBlockHeight();
                height += GetWrappedLabelHeight(
                    GetTurnDetectionStatusText(mode, turnDetectionProp, customTurnDetectionProp), contentWidth);
                height += GetTurnDetectionValuesHeight(customTurnDetectionProp);
            }
            else
                height += GetWrappedLabelHeight(PushToTalkTurnDetectionSummary, contentWidth);

            height += GetSingleLineBlockHeight();
            height += GetWrappedLabelHeight(GetInitialServerSttStatusText(mode, initialServerSttProp), contentWidth);

            if (mode == ConversationInputMode.PushToTalk)
            {
                height += GetWrappedLabelHeight(PushToTalkKeyOwnershipSummary, contentWidth);
                height += GetLocalAudioPolicyHeight(localAudioPolicyProp, mode);
                height += GetPushToTalkPolicyHeight(pushToTalkPolicyProp);
            }
            else
                height += GetLocalAudioPolicyHeight(localAudioPolicyProp, mode);

            height += GetBargeInHeight(bargeInProp);
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect lineRect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(lineRect, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            var contentRect = new Rect(
                position.x,
                lineRect.yMax + EditorGUIUtility.standardVerticalSpacing,
                position.width,
                position.height - EditorGUIUtility.singleLineHeight);

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;
            DrawContent(contentRect, property);
            EditorGUI.indentLevel = previousIndent;
            EditorGUI.EndProperty();
        }

        /// <summary>
        ///     Draws the option rows without the foldout row, for hosts that already provide a group header.
        ///     Pair with <see cref="GetContentHeight" /> to reserve the rect.
        /// </summary>
        internal static void DrawContent(Rect position, SerializedProperty property)
        {
            SerializedProperty modeProp = property.FindPropertyRelative(ModeFieldName);
            SerializedProperty turnDetectionProp = property.FindPropertyRelative(TurnDetectionFieldName);
            SerializedProperty customTurnDetectionProp = property.FindPropertyRelative(CustomTurnDetectionFieldName);
            SerializedProperty initialServerSttProp = property.FindPropertyRelative(InitialServerSttFieldName);
            SerializedProperty localAudioPolicyProp = property.FindPropertyRelative(LocalAudioPolicyFieldName);
            SerializedProperty pushToTalkPolicyProp = property.FindPropertyRelative(PushToTalkPolicyFieldName);
            SerializedProperty bargeInProp = property.FindPropertyRelative(BargeInFieldName);

            ConversationInputMode mode = GetConversationMode(modeProp);
            NormalizeHiddenTurnDetectionOption(mode, turnDetectionProp);

            float y = position.y;
            y = DrawProperty(position, y, modeProp, ConvaiInspectorContent.TurnTakingMode);
            mode = GetConversationMode(modeProp);

            if (mode == ConversationInputMode.HandsFree)
            {
                NormalizeHiddenTurnDetectionOption(mode, turnDetectionProp);
                y = DrawTurnDetectionSelector(position, y, mode, turnDetectionProp);
                y = DrawWrappedMiniLabel(
                    position,
                    y,
                    GetTurnDetectionStatusText(mode, turnDetectionProp, customTurnDetectionProp));
                y = DrawTurnDetectionValues(position, y, customTurnDetectionProp,
                    IsTurnDetectionEditable(turnDetectionProp));
            }
            else
                y = DrawWrappedMiniLabel(position, y, PushToTalkTurnDetectionSummary);

            y = DrawInitialServerSttSelector(position, y, initialServerSttProp);
            y = DrawWrappedMiniLabel(position, y, GetInitialServerSttStatusText(mode, initialServerSttProp));

            if (mode == ConversationInputMode.PushToTalk)
            {
                y = DrawWrappedMiniLabel(position, y, PushToTalkKeyOwnershipSummary);
                y = DrawLocalAudioPolicy(position, y, localAudioPolicyProp, mode);
                y = DrawPushToTalkPolicy(position, y, pushToTalkPolicyProp);
            }
            else
                y = DrawLocalAudioPolicy(position, y, localAudioPolicyProp, mode);

            DrawBargeIn(position, y, bargeInProp);
        }

        /// <summary>
        ///     Layout-friendly wrapper: reserves the content rect and draws the options flat, with no foldout.
        /// </summary>
        internal static void DrawContentLayout(SerializedProperty property)
        {
            if (property == null)
                return;

            Rect rect = GUILayoutUtility.GetRect(
                0f,
                GetContentHeight(property, EditorGUI.indentLevel),
                GUILayout.ExpandWidth(true));
            DrawContent(rect, property);
        }

        private static void NormalizeHiddenTurnDetectionOption(
            ConversationInputMode mode,
            SerializedProperty turnDetectionProp)
        {
            if (turnDetectionProp == null ||
                ConvaiInspectorDeveloperSettings.AreInspectorDeveloperSettingsVisible ||
                mode != ConversationInputMode.HandsFree)
                return;

            if (GetTurnDetectionMode(turnDetectionProp) == TurnDetectionMode.Disabled)
                turnDetectionProp.enumValueIndex = (int)TurnDetectionMode.UseDefault;
        }

        private static bool IsTurnDetectionEditable(SerializedProperty turnDetectionProp) =>
            GetTurnDetectionMode(turnDetectionProp) == TurnDetectionMode.Custom;

        private static float GetChildHeight(SerializedProperty property, bool includeChildren = false)
        {
            if (property == null)
                return 0f;

            return EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property, includeChildren);
        }

        private static float GetSingleLineBlockHeight() =>
            EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;

        /// <summary>
        ///     Wrap width of one explanatory paragraph, given the width of the row it sits in.
        /// </summary>
        /// <remarks>
        ///     Measuring and drawing both come through here. They used to compute the width apart from
        ///     one another, and drifted: the height was reserved for un-indented text and the paragraph
        ///     was then drawn indented, so it wrapped to more lines than it had room for and clipped
        ///     into the row beneath it.
        /// </remarks>
        private static float ParagraphWidth(float rowWidth) =>
            Mathf.Max(MinParagraphWidth, rowWidth - ParagraphInset);

        /// <summary>
        ///     Width of the row at <paramref name="indentLevel" />, estimated for the measuring pass,
        ///     which runs before there is a rect to ask.
        /// </summary>
        /// <remarks>
        ///     Deliberately pessimistic: a slightly narrow estimate reserves one line too many rather
        ///     than one too few, and an unused line reads as spacing while a missing one reads as a
        ///     rendering fault.
        /// </remarks>
        private static float EstimatedRowWidth(int indentLevel) =>
            EditorGUIUtility.currentViewWidth - InspectorChrome - (Mathf.Max(0, indentLevel) * IndentStep);

        /// <summary>
        ///     Height of one explanatory paragraph, measured through the shared metrics cache.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <see cref="GetPropertyHeight" /> runs at least twice per frame — once for Layout, once
        ///         for Repaint — and this drawer asks for up to five paragraphs each time, on the Room
        ///         Manager inspector every customer opens. Measuring wrapped text runs the text generator,
        ///         so those ten runs a frame were the most expensive per-event work in Convai's editor UI.
        ///         The cache is keyed on the text and the width themselves, so it stays correct as the
        ///         copy changes with the selected mode and the custom turn-detection values, with no
        ///         invalidation key here to fall out of step with what the paragraph actually says.
        ///     </para>
        ///     <para>
        ///         Measuring at a width the paragraph is not drawn at is what makes wrapped text clip into
        ///         the row beneath it, so the width comes from <see cref="ParagraphWidth" /> here and at
        ///         the draw site both.
        ///     </para>
        /// </remarks>
        /// <param name="width">Width the label will wrap at; callers pass the indented content width.</param>
        private static float GetWrappedLabelHeight(string text, float width)
        {
            return EditorGUIUtility.standardVerticalSpacing +
                   ConvaiEditorTextMetrics.WrappedHeight(ConvaiEditorStyles.CaptionWrapped, text, width);
        }

        private static float GetTurnDetectionValuesHeight(SerializedProperty customTurnDetectionProp)
        {
            if (customTurnDetectionProp == null)
                return 0f;

            SerializedProperty stopSecsProp = customTurnDetectionProp.FindPropertyRelative(StopSecsFieldName);
            SerializedProperty preSpeechMsProp = customTurnDetectionProp.FindPropertyRelative(PreSpeechMsFieldName);
            SerializedProperty maxDurationSecsProp =
                customTurnDetectionProp.FindPropertyRelative(MaxDurationSecsFieldName);

            float height = GetSingleLineBlockHeight();
            height += GetChildHeight(stopSecsProp);
            height += GetChildHeight(preSpeechMsProp);
            height += GetChildHeight(maxDurationSecsProp);
            return height;
        }

        private static float GetLocalAudioPolicyHeight(
            SerializedProperty localAudioPolicyProp,
            ConversationInputMode mode)
        {
            if (localAudioPolicyProp == null)
                return 0f;

            float height = GetSingleLineBlockHeight();
            height += GetChildHeight(
                localAudioPolicyProp.FindPropertyRelative(EnableAcousticEchoCancellationFieldName));

            if (mode == ConversationInputMode.PushToTalk)
            {
                height += GetChildHeight(localAudioPolicyProp.FindPropertyRelative(StartMutedInPushToTalkFieldName));
                height += GetChildHeight(localAudioPolicyProp.FindPropertyRelative(PushToTalkStartupModeFieldName));
            }

            return height;
        }

        private static float GetPushToTalkPolicyHeight(SerializedProperty pushToTalkPolicyProp)
        {
            if (pushToTalkPolicyProp == null)
                return 0f;

            float height = GetSingleLineBlockHeight();
            if (ConvaiInspectorDeveloperSettings.AreInspectorDeveloperSettingsVisible)
                height += GetChildHeight(pushToTalkPolicyProp.FindPropertyRelative(EnableServerSttToggleFieldName));

            height += GetChildHeight(pushToTalkPolicyProp.FindPropertyRelative(InterruptBotOnPressFieldName));
            height += GetChildHeight(
                pushToTalkPolicyProp.FindPropertyRelative(RequireTurnCompletionBeforeNextPressFieldName));
            height += GetChildHeight(pushToTalkPolicyProp.FindPropertyRelative(TurnCompletionTimeoutMsFieldName));

            if (ConvaiInspectorDeveloperSettings.AreInspectorDeveloperSettingsVisible)
            {
                height += GetChildHeight(pushToTalkPolicyProp.FindPropertyRelative(ReleaseTailMsFieldName));
                height += GetChildHeight(
                    pushToTalkPolicyProp.FindPropertyRelative(AllowSpeechStoppedFallbackAfterSpeechStartFieldName));
            }

            return height;
        }

        private static float GetBargeInHeight(SerializedProperty bargeInProp)
        {
            if (bargeInProp == null)
                return 0f;

            float height = GetSingleLineBlockHeight();
            height += GetChildHeight(bargeInProp.FindPropertyRelative(SmoothInterruptionFieldName));
            height += GetChildHeight(bargeInProp.FindPropertyRelative(FadeOutSecondsFieldName));
            height += GetChildHeight(bargeInProp.FindPropertyRelative(ClientDetectionFieldName));
            return height;
        }

        private static float DrawProperty(
            Rect position,
            float y,
            SerializedProperty property,
            GUIContent label,
            bool includeChildren = false)
        {
            if (property == null)
                return y;

            float height = EditorGUI.GetPropertyHeight(property, includeChildren);
            Rect rect = new(position.x, y, position.width, height);
            EditorGUI.PropertyField(rect, property, label, includeChildren);
            return rect.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float DrawTurnDetectionSelector(
            Rect position,
            float y,
            ConversationInputMode mode,
            SerializedProperty turnDetectionProp)
        {
            if (turnDetectionProp == null)
                return y;

            Rect rect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            GUIContent[] labels = GetTurnDetectionLabels(mode);
            TurnDetectionMode[] values = GetTurnDetectionValues(mode);
            int selectedIndex = GetSelectedIndex(values, GetTurnDetectionMode(turnDetectionProp));
            int nextIndex = EditorGUI.Popup(rect, ConvaiInspectorContent.TurnDetection, selectedIndex, labels);
            turnDetectionProp.enumValueIndex = (int)values[Mathf.Clamp(nextIndex, 0, values.Length - 1)];
            return rect.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float DrawInitialServerSttSelector(
            Rect position,
            float y,
            SerializedProperty initialServerSttProp)
        {
            if (initialServerSttProp == null)
                return y;

            Rect rect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            int nextIndex = EditorGUI.Popup(
                rect,
                ConvaiInspectorContent.InitialServerStt,
                Mathf.Clamp(initialServerSttProp.enumValueIndex, 0, InitialServerSttOptions.Length - 1),
                InitialServerSttOptions);
            initialServerSttProp.enumValueIndex = nextIndex;
            return rect.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float DrawWrappedMiniLabel(Rect position, float y, string text)
        {
            float bodyWidth = ParagraphWidth(EditorGUI.IndentedRect(position).width);
            float height = ConvaiEditorTextMetrics.WrappedHeight(
                ConvaiEditorStyles.CaptionWrapped, text, bodyWidth);
            Rect rect = new(position.x, y, position.width, height);
            EditorGUI.LabelField(rect, text, ConvaiEditorStyles.CaptionWrapped);
            return rect.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float DrawTurnDetectionValues(
            Rect position,
            float y,
            SerializedProperty customTurnDetectionProp,
            bool editable)
        {
            if (customTurnDetectionProp == null)
                return y;

            SerializedProperty stopSecsProp = customTurnDetectionProp.FindPropertyRelative(StopSecsFieldName);
            SerializedProperty preSpeechMsProp = customTurnDetectionProp.FindPropertyRelative(PreSpeechMsFieldName);
            SerializedProperty maxDurationSecsProp =
                customTurnDetectionProp.FindPropertyRelative(MaxDurationSecsFieldName);

            Rect labelRect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, ConvaiInspectorContent.CustomTurnDetection, ConvaiEditorStyles.SectionTitle);
            y = labelRect.yMax + EditorGUIUtility.standardVerticalSpacing;

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(!editable))
            {
                y = DrawProperty(position, y, stopSecsProp, ConvaiInspectorContent.StopSecs);
                y = DrawProperty(position, y, preSpeechMsProp, ConvaiInspectorContent.PreSpeechMs);
                y = DrawProperty(position, y, maxDurationSecsProp, ConvaiInspectorContent.MaxDurationSecs);
            }

            EditorGUI.indentLevel = previousIndent;
            return y;
        }

        private static float DrawLocalAudioPolicy(
            Rect position,
            float y,
            SerializedProperty localAudioPolicyProp,
            ConversationInputMode mode)
        {
            if (localAudioPolicyProp == null)
                return y;

            Rect labelRect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, ConvaiInspectorContent.LocalAudioPolicy, ConvaiEditorStyles.SectionTitle);
            y = labelRect.yMax + EditorGUIUtility.standardVerticalSpacing;

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;
            y = DrawProperty(
                position,
                y,
                localAudioPolicyProp.FindPropertyRelative(EnableAcousticEchoCancellationFieldName),
                ConvaiInspectorContent.EnableAcousticEchoCancellation);

            if (mode == ConversationInputMode.PushToTalk)
            {
                y = DrawProperty(
                    position,
                    y,
                    localAudioPolicyProp.FindPropertyRelative(StartMutedInPushToTalkFieldName),
                    ConvaiInspectorContent.StartMutedInPushToTalk);
                y = DrawProperty(
                    position,
                    y,
                    localAudioPolicyProp.FindPropertyRelative(PushToTalkStartupModeFieldName),
                    ConvaiInspectorContent.PushToTalkStartupMode);
            }
            EditorGUI.indentLevel = previousIndent;
            return y;
        }

        private static float DrawPushToTalkPolicy(
            Rect position,
            float y,
            SerializedProperty pushToTalkPolicyProp)
        {
            if (pushToTalkPolicyProp == null)
                return y;

            Rect labelRect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, ConvaiInspectorContent.PushToTalkPolicy, ConvaiEditorStyles.SectionTitle);
            y = labelRect.yMax + EditorGUIUtility.standardVerticalSpacing;

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;

            if (ConvaiInspectorDeveloperSettings.AreInspectorDeveloperSettingsVisible)
            {
                y = DrawProperty(
                    position,
                    y,
                    pushToTalkPolicyProp.FindPropertyRelative(EnableServerSttToggleFieldName),
                    ConvaiInspectorContent.EnableServerSttToggle);
            }

            y = DrawProperty(
                position,
                y,
                pushToTalkPolicyProp.FindPropertyRelative(InterruptBotOnPressFieldName),
                ConvaiInspectorContent.InterruptCharacterWhenPressed);
            y = DrawProperty(
                position,
                y,
                pushToTalkPolicyProp.FindPropertyRelative(RequireTurnCompletionBeforeNextPressFieldName),
                ConvaiInspectorContent.WaitForCharacterToFinishBeforeTalkingAgain);
            y = DrawProperty(
                position,
                y,
                pushToTalkPolicyProp.FindPropertyRelative(TurnCompletionTimeoutMsFieldName),
                ConvaiInspectorContent.FallbackWaitTimeMs);

            if (ConvaiInspectorDeveloperSettings.AreInspectorDeveloperSettingsVisible)
            {
                y = DrawProperty(
                    position,
                    y,
                    pushToTalkPolicyProp.FindPropertyRelative(ReleaseTailMsFieldName),
                    ConvaiInspectorContent.ReleaseTailMs);
                y = DrawProperty(
                    position,
                    y,
                    pushToTalkPolicyProp.FindPropertyRelative(AllowSpeechStoppedFallbackAfterSpeechStartFieldName),
                    ConvaiInspectorContent.AllowSpeechStoppedFallbackAfterSpeechStart);
            }

            EditorGUI.indentLevel = previousIndent;
            return y;
        }

        private static float DrawBargeIn(
            Rect position,
            float y,
            SerializedProperty bargeInProp)
        {
            if (bargeInProp == null)
                return y;

            Rect labelRect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, ConvaiInspectorContent.BargeIn, ConvaiEditorStyles.SectionTitle);
            y = labelRect.yMax + EditorGUIUtility.standardVerticalSpacing;

            SerializedProperty smoothProp =
                bargeInProp.FindPropertyRelative(SmoothInterruptionFieldName);
            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;
            y = DrawProperty(
                position,
                y,
                smoothProp,
                ConvaiInspectorContent.SmoothInterruption);

            using (new EditorGUI.DisabledScope(smoothProp != null && !smoothProp.boolValue))
            {
                y = DrawProperty(
                    position,
                    y,
                    bargeInProp.FindPropertyRelative(FadeOutSecondsFieldName),
                    ConvaiInspectorContent.BargeInFadeOutSeconds);
            }

            y = DrawProperty(
                position,
                y,
                bargeInProp.FindPropertyRelative(ClientDetectionFieldName),
                ConvaiInspectorContent.ClientBargeInDetection);
            EditorGUI.indentLevel = previousIndent;
            return y;
        }

        private static GUIContent[] GetTurnDetectionLabels(ConversationInputMode mode)
        {
            if (!ConvaiInspectorDeveloperSettings.AreInspectorDeveloperSettingsVisible &&
                mode == ConversationInputMode.HandsFree)
                return HandsFreeTurnDetectionOptions;

            return AllTurnDetectionOptions;
        }

        private static TurnDetectionMode[] GetTurnDetectionValues(ConversationInputMode mode)
        {
            if (!ConvaiInspectorDeveloperSettings.AreInspectorDeveloperSettingsVisible &&
                mode == ConversationInputMode.HandsFree)
                return HandsFreeTurnDetectionValues;

            return AllTurnDetectionValues;
        }

        private static int GetSelectedIndex(TurnDetectionMode[] values, TurnDetectionMode currentValue)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == currentValue)
                    return i;
            }

            return 0;
        }

        private static string GetTurnDetectionStatusText(
            ConversationInputMode mode,
            SerializedProperty turnDetectionProp,
            SerializedProperty customTurnDetectionProp)
        {
            TurnDetectionMode turnDetection = GetTurnDetectionMode(turnDetectionProp);
            return turnDetection switch
            {
                TurnDetectionMode.Custom =>
                    $"Custom Values resolves to smart turn enabled with Stop Secs = {GetStopSecs(customTurnDetectionProp):0.###}, Pre Speech Ms = {GetPreSpeechMs(customTurnDetectionProp)}, Max Duration Secs = {GetMaxDurationSecs(customTurnDetectionProp):0.###}.",
                TurnDetectionMode.Disabled =>
                    "No Automatic Turn Detection resolves to no turn_detection_config being sent, so the backend will not automatically decide when the player finished speaking.",
                _ => mode == ConversationInputMode.HandsFree
                    ? $"SDK Default resolves to smart turn enabled with Stop Secs = {SmartTurnSettings.DefaultStopSecs:0.###}, Pre Speech Ms = {SmartTurnSettings.DefaultPreSpeechMs}, Max Duration Secs = {SmartTurnSettings.DefaultMaxDurationSecs:0.###}."
                    : "SDK Default resolves to no automatic turn detection in Push To Talk mode."
            };
        }

        private static string GetInitialServerSttStatusText(
            ConversationInputMode mode,
            SerializedProperty initialServerSttProp)
        {
            ServerSttInitialState initialServerStt =
                initialServerSttProp == null
                    ? ServerSttInitialState.UseDefault
                    : (ServerSttInitialState)initialServerSttProp.enumValueIndex;

            return initialServerStt switch
            {
                ServerSttInitialState.Enabled =>
                    "Start Enabled means backend speech-to-text starts enabled when the session begins.",
                ServerSttInitialState.Disabled =>
                    "Start Disabled means backend speech-to-text starts disabled when the session begins.",
                _ => mode == ConversationInputMode.HandsFree
                    ? "SDK Default resolves to Start Enabled in Hands Free mode."
                    : "SDK Default resolves to Start Disabled in Push To Talk mode so backend speech-to-text stays off until the push-to-talk flow unmutes it."
            };
        }

        private static float GetStopSecs(SerializedProperty customTurnDetectionProp)
        {
            SerializedProperty property = customTurnDetectionProp?.FindPropertyRelative(StopSecsFieldName);
            return property?.floatValue ?? SmartTurnSettings.DefaultStopSecs;
        }

        private static int GetPreSpeechMs(SerializedProperty customTurnDetectionProp)
        {
            SerializedProperty property = customTurnDetectionProp?.FindPropertyRelative(PreSpeechMsFieldName);
            return property?.intValue ?? SmartTurnSettings.DefaultPreSpeechMs;
        }

        private static float GetMaxDurationSecs(SerializedProperty customTurnDetectionProp)
        {
            SerializedProperty property = customTurnDetectionProp?.FindPropertyRelative(MaxDurationSecsFieldName);
            return property?.floatValue ?? SmartTurnSettings.DefaultMaxDurationSecs;
        }

        private static ConversationInputMode GetConversationMode(SerializedProperty property) =>
            property == null
                ? ConversationInputMode.HandsFree
                : (ConversationInputMode)property.enumValueIndex;

        private static TurnDetectionMode GetTurnDetectionMode(SerializedProperty property) =>
            property == null
                ? TurnDetectionMode.UseDefault
                : (TurnDetectionMode)property.enumValueIndex;
    }
}
