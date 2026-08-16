using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Convai.Runtime.Components
{
    internal interface IConvaiXrButtonStateSource
    {
        bool HasRunningInputSubsystem { get; }

        bool HasController(XRNode node);

        bool TryGetButton(XRNode node, InputFeatureUsage<bool> usage, out bool isPressed);
    }

    internal sealed class ConvaiPushToTalkInputReader
    {
        private readonly Func<KeyCode, bool> _fallbackInputReader;
        private readonly HashSet<XRNode> _selectedControllerNodes = new();
        private readonly IConvaiXrButtonStateSource _xrButtonStateSource;

        internal ConvaiPushToTalkInputReader(Func<KeyCode, bool> fallbackInputReader)
            : this(new UnityConvaiXrButtonStateSource(), fallbackInputReader)
        {
        }

        internal ConvaiPushToTalkInputReader(
            IConvaiXrButtonStateSource xrButtonStateSource,
            Func<KeyCode, bool> fallbackInputReader)
        {
            _xrButtonStateSource = xrButtonStateSource ??
                                   throw new ArgumentNullException(nameof(xrButtonStateSource));
            _fallbackInputReader = fallbackInputReader ??
                                   throw new ArgumentNullException(nameof(fallbackInputReader));
        }

        public bool IsHeld(KeyCode keyCode)
        {
            if (!_xrButtonStateSource.HasRunningInputSubsystem)
            {
                _selectedControllerNodes.Clear();
                return _fallbackInputReader(keyCode);
            }

            if (!TryResolveXrButtonBinding(keyCode, out XRNode node, out InputFeatureUsage<bool> usage))
                return _fallbackInputReader(keyCode);

            if (!_xrButtonStateSource.HasController(node))
            {
                if (_selectedControllerNodes.Contains(node))
                    return false;

                return _fallbackInputReader(keyCode);
            }

            _selectedControllerNodes.Add(node);
            return _xrButtonStateSource.TryGetButton(node, usage, out bool isPressed) && isPressed;
        }

        private static bool TryResolveXrButtonBinding(
            KeyCode keyCode,
            out XRNode node,
            out InputFeatureUsage<bool> usage)
        {
            switch (keyCode)
            {
                case KeyCode.JoystickButton0:
                    node = XRNode.RightHand;
                    usage = CommonUsages.primaryButton;
                    return true;
                case KeyCode.JoystickButton1:
                    node = XRNode.RightHand;
                    usage = CommonUsages.secondaryButton;
                    return true;
                case KeyCode.JoystickButton2:
                    node = XRNode.LeftHand;
                    usage = CommonUsages.primaryButton;
                    return true;
                case KeyCode.JoystickButton3:
                    node = XRNode.LeftHand;
                    usage = CommonUsages.secondaryButton;
                    return true;
                default:
                    node = default;
                    usage = default;
                    return false;
            }
        }
    }

    internal sealed class UnityConvaiXrButtonStateSource : IConvaiXrButtonStateSource
    {
        private static readonly List<XRInputSubsystem> s_inputSubsystems = new();

        public bool HasRunningInputSubsystem
        {
            get
            {
                s_inputSubsystems.Clear();
                SubsystemManager.GetSubsystems(s_inputSubsystems);
                for (int i = 0; i < s_inputSubsystems.Count; i++)
                    if (s_inputSubsystems[i].running)
                        return true;

                return false;
            }
        }

        public bool HasController(XRNode node)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid &&
                   (device.characteristics & InputDeviceCharacteristics.Controller) != 0;
        }

        public bool TryGetButton(XRNode node, InputFeatureUsage<bool> usage, out bool isPressed)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                isPressed = false;
                return false;
            }

            return device.TryGetFeatureValue(usage, out isPressed);
        }
    }
}
