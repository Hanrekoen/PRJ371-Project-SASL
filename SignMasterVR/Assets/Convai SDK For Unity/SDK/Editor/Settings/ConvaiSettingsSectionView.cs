using UnityEditor;
using UnityEngine.UIElements;

namespace Convai.Editor.Settings
{
    /// <summary>
    ///     Base class for the shared settings section views mounted by both the
    ///     Project Settings page and the Convai Editor window.
    /// </summary>
    public abstract class ConvaiSettingsSectionView : VisualElement
    {
        protected ConvaiSettingsSectionView(ConvaiSettingsViewContext context, string title, bool supportsReset = true)
        {
            Context = context;
            AddToClassList("convai-settings-section");

            var header = new VisualElement();
            header.AddToClassList("convai-settings-section-header");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("convai-settings-section-title");
            header.Add(titleLabel);

            HeaderActions = new VisualElement();
            HeaderActions.AddToClassList("convai-settings-section-actions");
            header.Add(HeaderActions);

            if (supportsReset)
            {
                var resetButton = new Button(OnResetClicked)
                {
                    text = "Reset",
                    tooltip = $"Reset the {title} section to SDK defaults."
                };
                resetButton.AddToClassList("convai-settings-reset-button");
                HeaderActions.Add(resetButton);
            }

            Add(header);

            Body = new VisualElement();
            Body.AddToClassList("convai-settings-card");
            Add(Body);
        }

        /// <summary>Shared host context (serialized settings, validation, save throttling).</summary>
        protected ConvaiSettingsViewContext Context { get; }

        /// <summary>Container for section content.</summary>
        protected VisualElement Body { get; }

        /// <summary>Header-right action strip (reset button, section-specific actions).</summary>
        protected VisualElement HeaderActions { get; }

        /// <summary>True while the serialized settings target is alive and usable.</summary>
        protected bool HasValidSettings =>
            !Context.IsDisposed && Context.Settings.targetObject != null;

        /// <summary>
        ///     Called by the host when the section becomes visible (settings page activate,
        ///     window section shown). Use for re-reads of external state.
        /// </summary>
        public virtual void Activate()
        {
        }

        /// <summary>Called by the host when the section is hidden or torn down.</summary>
        public virtual void Deactivate()
        {
        }

        /// <summary>Restores this section's fields to SDK defaults. No-op by default.</summary>
        protected virtual void ResetToDefaults()
        {
        }

        /// <summary>Lets sections confirm destructive or compile-triggering resets.</summary>
        protected virtual bool ConfirmReset() => true;

        /// <summary>Runs after reset values have been applied to the settings asset.</summary>
        protected virtual void OnResetApplied()
        {
        }

        /// <summary>
        ///     Applies pending serialized changes and schedules a debounced asset save.
        /// </summary>
        protected void ApplyAndSave()
        {
            if (!HasValidSettings) return;

            if (Context.Settings.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(Context.Settings.targetObject);
                Context.RequestSave();
            }
        }

        private void OnResetClicked()
        {
            if (!HasValidSettings || !ConfirmReset()) return;

            Context.Settings.Update();
            ResetToDefaults();
            ApplyAndSave();
            OnResetApplied();
        }
    }
}
