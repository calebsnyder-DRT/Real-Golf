using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RealGolf.Core
{
    /// <summary>
    /// Single place the game reads input from, so the rest of the code does not
    /// care which input backend the project is configured to use.
    /// </summary>
    public static class GolfInput
    {
#if ENABLE_INPUT_SYSTEM
        private static Keyboard Keys => Keyboard.current;
        private static Mouse MouseDevice => Mouse.current;

        public static bool SwingPressed =>
            (Keys != null && Keys.spaceKey.wasPressedThisFrame) ||
            (MouseDevice != null && MouseDevice.leftButton.wasPressedThisFrame);

        public static bool ConfirmPressed =>
            (Keys != null && (Keys.spaceKey.wasPressedThisFrame || Keys.enterKey.wasPressedThisFrame)) ||
            (MouseDevice != null && MouseDevice.leftButton.wasPressedThisFrame);

        public static float AimAxis
        {
            get
            {
                if (Keys == null)
                {
                    return 0f;
                }

                float axis = 0f;
                if (Keys.aKey.isPressed || Keys.leftArrowKey.isPressed) axis -= 1f;
                if (Keys.dKey.isPressed || Keys.rightArrowKey.isPressed) axis += 1f;
                return axis;
            }
        }

        public static float PitchAxis
        {
            get
            {
                if (Keys == null)
                {
                    return 0f;
                }

                float axis = 0f;
                if (Keys.sKey.isPressed || Keys.downArrowKey.isPressed) axis -= 1f;
                if (Keys.wKey.isPressed || Keys.upArrowKey.isPressed) axis += 1f;
                return axis;
            }
        }

        // Menu navigation. The aim axes cannot serve here: they report isPressed so a
        // held key would run the whole course list past in a frame or two. Submit is
        // deliberately keyboard-only, unlike ConfirmPressed -- a menu is full of
        // clickable buttons, and a mouse-inclusive submit would start the round on
        // the same click that picked a course.
        public static bool MenuUpPressed =>
            Keys != null && (Keys.upArrowKey.wasPressedThisFrame || Keys.wKey.wasPressedThisFrame);

        public static bool MenuDownPressed =>
            Keys != null && (Keys.downArrowKey.wasPressedThisFrame || Keys.sKey.wasPressedThisFrame);

        public static bool MenuSubmitPressed =>
            Keys != null && Keys.enterKey.wasPressedThisFrame;

        public static bool ClubUpPressed => Keys != null && Keys.eKey.wasPressedThisFrame;
        public static bool ClubDownPressed => Keys != null && Keys.qKey.wasPressedThisFrame;
        public static bool ScorecardPressed => Keys != null && Keys.tabKey.wasPressedThisFrame;
        public static bool OverheadPressed => Keys != null && Keys.cKey.wasPressedThisFrame;
        public static bool RestartPressed => Keys != null && Keys.rKey.wasPressedThisFrame;
        public static bool AnyKeyPressed => Keys != null && Keys.anyKey.wasPressedThisFrame;
        public static bool PausePressed => Keys != null && Keys.escapeKey.wasPressedThisFrame;

        /// <summary>True on the frame the given top-row digit (1-4) is pressed.</summary>
        public static bool DigitPressed(int digit)
        {
            if (Keys == null)
            {
                return false;
            }

            return digit switch
            {
                1 => Keys.digit1Key.wasPressedThisFrame,
                2 => Keys.digit2Key.wasPressedThisFrame,
                3 => Keys.digit3Key.wasPressedThisFrame,
                4 => Keys.digit4Key.wasPressedThisFrame,
                _ => false
            };
        }
#else
        public static bool SwingPressed => Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
        public static bool ConfirmPressed => Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0);
        public static float AimAxis => Input.GetAxisRaw("Horizontal");
        public static float PitchAxis => Input.GetAxisRaw("Vertical");
        public static bool MenuUpPressed => Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
        public static bool MenuDownPressed => Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
        public static bool MenuSubmitPressed => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        public static bool ClubUpPressed => Input.GetKeyDown(KeyCode.E);
        public static bool ClubDownPressed => Input.GetKeyDown(KeyCode.Q);
        public static bool ScorecardPressed => Input.GetKeyDown(KeyCode.Tab);
        public static bool OverheadPressed => Input.GetKeyDown(KeyCode.C);
        public static bool RestartPressed => Input.GetKeyDown(KeyCode.R);
        public static bool AnyKeyPressed => Input.anyKeyDown;
        public static bool PausePressed => Input.GetKeyDown(KeyCode.Escape);

        /// <summary>True on the frame the given top-row digit (1-4) is pressed.</summary>
        public static bool DigitPressed(int digit)
        {
            if (digit < 1 || digit > 4)
            {
                return false;
            }

            return Input.GetKeyDown(KeyCode.Alpha0 + digit);
        }
#endif
    }
}
