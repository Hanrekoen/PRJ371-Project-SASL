using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Companion component that discovers and dispatches events to IConvaiCharacterBehavior components.
    ///     Follows the composition pattern similar to ConvaiAudioOutput.
    /// </summary>
    /// <remarks>
    ///     This component:
    ///     - Auto-discovers all IConvaiCharacterBehavior components on the same GameObject
    ///     - Sorts behaviors by Priority (highest first)
    ///     - Dispatches ConvaiCharacter events to behaviors in priority order
    ///     - Supports interception pattern: if a behavior returns true, stops dispatch chain
    ///     Usage:
    ///     1. Add to the same GameObject as ConvaiCharacter
    ///     2. Add IConvaiCharacterBehavior components that extend character-specific behavior
    ///     3. The dispatcher auto-discovers and invokes behaviors when character events occur
    /// </remarks>
    [AddComponentMenu("Convai/Character Behavior Dispatcher")]
    [RequireComponent(typeof(ConvaiCharacter))]
    public class CharacterBehaviorDispatcher : MonoBehaviour
    {
        #region Behavior Discovery

        /// <summary>
        ///     Discovers all IConvaiCharacterBehavior components and sorts by priority.
        ///     Called automatically in Awake, but can be called manually to refresh.
        /// </summary>
        public void DiscoverBehaviors()
        {
            IConvaiCharacterBehavior[] allBehaviors = GetComponents<IConvaiCharacterBehavior>();

            _behaviors = allBehaviors
                .OrderByDescending(b => b.Priority)
                .ToList();

            if (_behaviors.Count == 0)
            {
                ConvaiLogger.Debug(
                    $"No IConvaiCharacterBehavior components found on {gameObject.name}. " +
                    "Add behavior components to extend character functionality.", LogCategory.Character);
            }
        }

        #endregion

        #region Components

        private ConvaiCharacter _character;
        private List<IConvaiCharacterBehavior> _behaviors;
        private bool _isInitialized;

        #endregion

        #region Public Properties

        /// <summary>The discovered behaviors sorted by priority (highest first).</summary>
        public IReadOnlyList<IConvaiCharacterBehavior> Behaviors => _behaviors;

        /// <summary>Number of discovered behaviors.</summary>
        public int BehaviorCount => _behaviors?.Count ?? 0;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _character = GetComponent<ConvaiCharacter>();
            DiscoverBehaviors();
        }

        private void OnEnable()
        {
            if (_character == null)
            {
                ConvaiLogger.Error(
                    $"ConvaiCharacter component not found on {gameObject.name}",
                    LogCategory.Character);
                enabled = false;
                return;
            }

            SubscribeToEvents();
            NotifyCharacterInitialized();
            _isInitialized = true;
        }

        private void OnDisable()
        {
            if (_isInitialized) NotifyCharacterShutdown();
            UnsubscribeFromEvents();
            _isInitialized = false;
        }

        #endregion

        #region Event Subscription

        private void SubscribeToEvents()
        {
            if (_character == null) return;

            _character.OnSpeechStarted += OnSpeechStarted;
            _character.OnSpeechStopped += OnSpeechStopped;
            _character.OnTurnCompleted += OnTurnCompleted;
            _character.OnTranscriptReceived += OnTranscriptReceived;
            _character.OnCharacterReady += OnCharacterReady;
        }

        private void UnsubscribeFromEvents()
        {
            if (_character == null) return;

            _character.OnSpeechStarted -= OnSpeechStarted;
            _character.OnSpeechStopped -= OnSpeechStopped;
            _character.OnTurnCompleted -= OnTurnCompleted;
            _character.OnTranscriptReceived -= OnTranscriptReceived;
            _character.OnCharacterReady -= OnCharacterReady;
        }

        #endregion

        #region Event Handlers

        private void OnSpeechStarted() => DispatchSpeechStarted();

        private void OnSpeechStopped() => DispatchSpeechStopped();

        private void OnTurnCompleted(bool wasInterrupted) => DispatchTurnCompleted(wasInterrupted);

        private void OnTranscriptReceived(string transcript, bool isFinal) =>
            DispatchTranscriptReceived(transcript, isFinal);

        private void OnCharacterReady() => DispatchCharacterReady();

        #endregion

        #region Dispatch Methods

        private void NotifyCharacterInitialized()
        {
            if (_behaviors == null || _character == null) return;

            foreach (IConvaiCharacterBehavior behavior in _behaviors)
            {
                try
                {
                    behavior.OnCharacterInitialized(_character);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error in OnCharacterInitialized for {behavior.GetType().Name}: {ex.Message}",
                        LogCategory.Character);
                }
            }
        }

        private void NotifyCharacterShutdown()
        {
            if (_behaviors == null || _character == null) return;

            foreach (IConvaiCharacterBehavior behavior in _behaviors)
            {
                try
                {
                    behavior.OnCharacterShutdown(_character);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error in OnCharacterShutdown for {behavior.GetType().Name}: {ex.Message}",
                        LogCategory.Character);
                }
            }
        }

        private void DispatchSpeechStarted()
        {
            if (_behaviors == null || _character == null) return;

            foreach (IConvaiCharacterBehavior behavior in _behaviors)
            {
                try
                {
                    bool intercepted = behavior.OnSpeechStarted(_character);
                    if (intercepted) return;
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error in OnSpeechStarted for {behavior.GetType().Name}: {ex.Message}",
                        LogCategory.Character);
                }
            }
        }

        private void DispatchSpeechStopped()
        {
            if (_behaviors == null || _character == null) return;

            foreach (IConvaiCharacterBehavior behavior in _behaviors)
            {
                try
                {
                    bool intercepted = behavior.OnSpeechStopped(_character);
                    if (intercepted) return;
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error in OnSpeechStopped for {behavior.GetType().Name}: {ex.Message}",
                        LogCategory.Character);
                }
            }
        }

        private void DispatchTurnCompleted(bool wasInterrupted)
        {
            if (_behaviors == null || _character == null) return;

            foreach (IConvaiCharacterBehavior behavior in _behaviors)
            {
                try
                {
                    bool intercepted = behavior.OnTurnCompleted(_character, wasInterrupted);
                    if (intercepted) return;
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error in OnTurnCompleted for {behavior.GetType().Name}: {ex.Message}",
                        LogCategory.Character);
                }
            }
        }

        private void DispatchTranscriptReceived(string transcript, bool isFinal)
        {
            if (_behaviors == null || _character == null) return;

            foreach (IConvaiCharacterBehavior behavior in _behaviors)
            {
                try
                {
                    bool intercepted = behavior.OnTranscriptReceived(_character, transcript, isFinal);
                    if (intercepted) return;
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error in OnTranscriptReceived for {behavior.GetType().Name}: {ex.Message}",
                        LogCategory.Character);
                }
            }
        }

        private void DispatchCharacterReady()
        {
            if (_behaviors == null || _character == null) return;

            foreach (IConvaiCharacterBehavior behavior in _behaviors)
            {
                try
                {
                    behavior.OnCharacterReady(_character);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"Error in OnCharacterReady for {behavior.GetType().Name}: {ex.Message}",
                        LogCategory.Character);
                }
            }
        }

        #endregion
    }
}
