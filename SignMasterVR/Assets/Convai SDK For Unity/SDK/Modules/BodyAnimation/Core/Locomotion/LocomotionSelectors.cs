using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>Which foot, for plant-aware selection.</summary>
    internal enum FootSide
    {
        Unknown = 0,
        Left = 1,
        Right = 2
    }

    /// <summary>A selected locomotion one-shot plus the context the state machine needs.</summary>
    internal readonly struct MotionChoice
    {
        public readonly LocomotionClip Clip;
        public readonly string Label;

        /// <summary>Signed yaw (deg) the clip is authored to cover (0 for straight motions).</summary>
        public readonly float AuthoredYaw;

        public MotionChoice(LocomotionClip clip, string label, float authoredYaw = 0f)
        {
            Clip = clip;
            Label = label;
            AuthoredYaw = authoredYaw;
        }

        public bool IsValid => Clip != null && Clip.IsValid;
    }

    /// <summary>
    ///     Pure selection logic for directional starts, turn-in-place, and planted stops.
    ///     Selection prefers the exact directional clip and falls back to the nearest
    ///     available one; a fully empty family returns an invalid choice so the state machine
    ///     degrades to plain blending.
    /// </summary>
    internal static class LocomotionSelectors
    {
        /// <summary>
        ///     Picks a directional start for the given signed steering angle (deg, +right).
        ///     |angle| &lt; 45 → forward, 45..turn180Min → 90° side, above → 180° side.
        /// </summary>
        public static MotionChoice SelectStart(
            LocomotionSection section, bool jog, float signedAngle, float turn180MinAngle)
        {
            LocomotionClip forward = jog ? section.JogStartForward : section.WalkStartForward;
            LocomotionClip left90 = jog ? section.JogStart90Left : section.WalkStart90Left;
            LocomotionClip right90 = jog ? section.JogStart90Right : section.WalkStart90Right;
            LocomotionClip left180 = jog ? section.JogStart180Left : section.WalkStart180Left;
            LocomotionClip right180 = jog ? section.JogStart180Right : section.WalkStart180Right;

            string family = jog ? "JogStart" : "WalkStart";
            float magnitude = Mathf.Abs(signedAngle);
            bool right = signedAngle >= 0f;

            if (magnitude < 45f)
                return Choice(forward, $"{family}:Fwd", 0f);

            if (magnitude < turn180MinAngle)
            {
                MotionChoice side = right
                    ? Choice(right90, $"{family}:90R", 90f)
                    : Choice(left90, $"{family}:90L", -90f);
                return side.IsValid ? side : Choice(forward, $"{family}:Fwd(fallback)", 0f);
            }

            MotionChoice back = right
                ? Choice(right180, $"{family}:180R", 180f)
                : Choice(left180, $"{family}:180L", -180f);
            if (back.IsValid) return back;

            MotionChoice side90 = right
                ? Choice(right90, $"{family}:90R(fallback)", 90f)
                : Choice(left90, $"{family}:90L(fallback)", -90f);
            return side90.IsValid ? side90 : Choice(forward, $"{family}:Fwd(fallback)", 0f);
        }

        /// <summary>Picks a turn-in-place clip for the given signed yaw error (deg, +right).</summary>
        public static MotionChoice SelectTurn(
            LocomotionSection section, float signedAngle, float turn180MinAngle)
        {
            float magnitude = Mathf.Abs(signedAngle);
            bool right = signedAngle >= 0f;

            if (magnitude >= turn180MinAngle)
            {
                MotionChoice turn180 = right
                    ? Choice(section.Turn180Right, "Turn:180R", 180f)
                    : Choice(section.Turn180Left, "Turn:180L", -180f);
                if (turn180.IsValid) return turn180;
            }

            return right
                ? Choice(section.Turn90Right, "Turn:90R", 90f)
                : Choice(section.Turn90Left, "Turn:90L", -90f);
        }

        /// <summary>
        ///     Picks a stop clip: abrupt stops override, low speeds use the low-speed stop,
        ///     otherwise the plant-matched (LF/RF) stop for the foot about to plant.
        /// </summary>
        public static MotionChoice SelectStop(
            LocomotionSection section,
            bool jogging,
            bool abrupt,
            bool lowSpeed,
            FootSide upcomingPlant)
        {
            if (jogging)
            {
                if (abrupt)
                {
                    MotionChoice hard = Choice(section.JogStopAbrupt, "JogStop:Abrupt");
                    if (hard.IsValid) return hard;
                }

                MotionChoice jogStop = Choice(section.JogStopLeftPlant, "JogStop:LF");
                if (jogStop.IsValid) return jogStop;
                // No jog stop authored — fall through to the walk family.
            }

            if (abrupt)
            {
                MotionChoice hard = Choice(section.WalkStopAbrupt, "WalkStop:Abrupt");
                if (hard.IsValid) return hard;
            }

            if (lowSpeed)
            {
                MotionChoice soft = Choice(section.WalkStopLowSpeed, "WalkStop:LowSpeed");
                if (soft.IsValid) return soft;
            }

            MotionChoice planted = upcomingPlant == FootSide.Right
                ? Choice(section.WalkStopRightPlant, "WalkStop:RF")
                : Choice(section.WalkStopLeftPlant, "WalkStop:LF");
            if (planted.IsValid) return planted;

            // Any planted stop is better than none.
            MotionChoice other = upcomingPlant == FootSide.Right
                ? Choice(section.WalkStopLeftPlant, "WalkStop:LF(fallback)")
                : Choice(section.WalkStopRightPlant, "WalkStop:RF(fallback)");
            if (other.IsValid) return other;

            return Choice(section.WalkStopLowSpeed, "WalkStop:LowSpeed(fallback)");
        }

        /// <summary>Picks a walk↔jog transition clip matched to the foot about to plant.</summary>
        public static MotionChoice SelectSpeedChange(
            LocomotionSection section, bool toJog, FootSide upcomingPlant)
        {
            MotionChoice preferred;
            MotionChoice fallback;

            if (toJog)
            {
                preferred = upcomingPlant == FootSide.Right
                    ? Choice(section.WalkToJogRight, "WalkToJog:RF")
                    : Choice(section.WalkToJogLeft, "WalkToJog:LF");
                fallback = upcomingPlant == FootSide.Right
                    ? Choice(section.WalkToJogLeft, "WalkToJog:LF(fallback)")
                    : Choice(section.WalkToJogRight, "WalkToJog:RF(fallback)");
            }
            else
            {
                preferred = upcomingPlant == FootSide.Right
                    ? Choice(section.JogToWalkRight, "JogToWalk:RF")
                    : Choice(section.JogToWalkLeft, "JogToWalk:LF");
                fallback = upcomingPlant == FootSide.Right
                    ? Choice(section.JogToWalkLeft, "JogToWalk:LF(fallback)")
                    : Choice(section.JogToWalkRight, "JogToWalk:RF(fallback)");
            }

            return preferred.IsValid ? preferred : fallback;
        }

        private static MotionChoice Choice(LocomotionClip clip, string label, float yaw = 0f) =>
            clip != null && clip.IsValid ? new MotionChoice(clip, label, yaw) : default;
    }
}
