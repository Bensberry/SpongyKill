using Spongykill.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Spongykill.Controls
{
    public enum TouchButton
    {
        Fire,
        Jump,
        Crouch,
        Reload,
        Scope,
        Interact,
        Inspect,
        SwitchWeapon,
        Scoreboard,
        Pause,
    }

    /// <summary>Whether this session uses on-screen touch controls or keyboard and mouse.</summary>
    public static class ControlScheme
    {
        public static bool UseTouch
        {
            get
            {
                switch (GameSettings.Current.controls)
                {
                    case ControlsMode.Touch: return true;
                    case ControlsMode.KeyboardMouse: return false;
                    default: return Application.isMobilePlatform;
                }
            }
        }
    }

    /// <summary>
    /// State written by the on-screen controls and read by the player's input handler. Presses that
    /// happen between two reads are latched for exactly one frame, whichever runs first.
    /// </summary>
    public static class TouchInput
    {
        static int heldMask;
        static int pendingPressMask;
        static int framePressMask;
        static Vector2 pendingLook;
        static Vector2 frameLook;
        static int pendingSlot = -1;
        static int frameSlot = -1;
        static int latchedFrame = -1;

        /// <summary>Joystick position, -1..1 on both axes.</summary>
        public static Vector2 Move { get; set; }
        public static bool Sprint { get; set; }

        /// <summary>Screen pixels dragged on the look area this frame.</summary>
        public static Vector2 LookPixels
        {
            get
            {
                Latch();
                return frameLook;
            }
        }

        /// <summary>Weapon slot tapped on the weapon bar this frame, or -1.</summary>
        public static int SlotPressed
        {
            get
            {
                Latch();
                return frameSlot;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ReleaseAll();

        public static void AddLook(Vector2 pixels) => pendingLook += pixels;

        public static void SetHeld(TouchButton button, bool held)
        {
            int bit = 1 << (int)button;
            if (held)
            {
                if ((heldMask & bit) == 0) pendingPressMask |= bit;
                heldMask |= bit;
            }
            else
            {
                heldMask &= ~bit;
            }
        }

        /// <summary>A tap: pressed this frame without being held.</summary>
        public static void Tap(TouchButton button) => pendingPressMask |= 1 << (int)button;

        public static void TapSlot(int slot) => pendingSlot = slot;

        public static bool Held(TouchButton button) => (heldMask & (1 << (int)button)) != 0;

        public static bool Pressed(TouchButton button)
        {
            Latch();
            return (framePressMask & (1 << (int)button)) != 0;
        }

        public static void ReleaseAll()
        {
            heldMask = pendingPressMask = framePressMask = 0;
            pendingLook = frameLook = Vector2.zero;
            pendingSlot = frameSlot = -1;
            latchedFrame = -1;
            Move = Vector2.zero;
            Sprint = false;
        }

        static void Latch()
        {
            if (latchedFrame == Time.frameCount) return;
            latchedFrame = Time.frameCount;
            framePressMask = pendingPressMask;
            pendingPressMask = 0;
            frameLook = pendingLook;
            pendingLook = Vector2.zero;
            frameSlot = pendingSlot;
            pendingSlot = -1;
        }
    }

    /// <summary>Gyroscope aiming: how far the device turned this frame, in screen-relative degrees.</summary>
    public static class GyroInput
    {
        public static bool Available => UnityEngine.InputSystem.Gyroscope.current != null;

        /// <returns>x = degrees turned right, y = degrees tilted up.</returns>
        public static Vector2 ReadDegrees(float deltaTime)
        {
            UnityEngine.InputSystem.Gyroscope gyro = UnityEngine.InputSystem.Gyroscope.current;
            if (gyro == null) return Vector2.zero;
            if (!gyro.enabled)
            {
                InputSystem.EnableDevice(gyro);
                return Vector2.zero;
            }

            // Raw rates are radians per second around the device's portrait axes (x right, y up, z out
            // of the screen, counter-clockwise positive). Rotate them into the current screen orientation.
            Vector3 rate = gyro.angularVelocity.ReadUnprocessedValue();
            switch (Screen.orientation)
            {
                case ScreenOrientation.LandscapeLeft: rate = new Vector3(-rate.y, rate.x, rate.z); break;
                case ScreenOrientation.LandscapeRight: rate = new Vector3(rate.y, -rate.x, rate.z); break;
                case ScreenOrientation.PortraitUpsideDown: rate = new Vector3(-rate.x, -rate.y, rate.z); break;
            }

            // Turning right spins the phone clockwise around the screen's up axis; tilting the top edge
            // back (looking up) spins it counter-clockwise around the screen's right axis.
            float degrees = Mathf.Rad2Deg * deltaTime;
            return new Vector2(-rate.y * degrees, rate.x * degrees);
        }
    }
}
