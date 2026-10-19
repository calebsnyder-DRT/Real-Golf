using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Puttshack.Runtime.Shop
{
    public enum ShopAction
    {
        Up,
        Down,
        Left,
        Right,
        Confirm,
        Back,
        PrevPlayer,
        NextPlayer,
        PrevCategory,
        NextCategory,
        Save,
        Reset,
    }

    /// <summary>
    /// Semantic Pro Shop input from keyboard and gamepad. Directions auto-repeat while held.
    /// Keyboard: arrows, Enter/Space, Esc/Backspace, Q/E players, Tab/Shift+Tab categories, A/D rotate.
    /// Gamepad: d-pad/left stick, A confirm, B back, LB/RB players, LT/RT categories, Y save, X reset, right stick rotate.
    /// </summary>
    public sealed class ShopInput
    {
        private const float RepeatDelay = 0.38f;
        private const float RepeatInterval = 0.085f;
        private const float StickThreshold = 0.55f;

        private readonly List<ShopAction> _actions = new List<ShopAction>();
        private ShopAction? _held;
        private float _nextRepeat;

        /// <summary>Raised for every action any menu polls (the audio layer's UI clicks listen here).</summary>
        public static event System.Action<ShopAction> ActionPolled;

        /// <summary>-1..1, positive turns the golfer to their left (clockwise seen from above).</summary>
        public float Rotate { get; private set; }

        public IReadOnlyList<ShopAction> Poll(float now)
        {
            _actions.Clear();
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;

            ShopAction? dir = HeldDirection(kb, pad);
            if (dir != _held)
            {
                _held = dir;
                if (dir.HasValue)
                {
                    _actions.Add(dir.Value);
                    _nextRepeat = now + RepeatDelay;
                }
            }
            else if (dir.HasValue && now >= _nextRepeat)
            {
                _actions.Add(dir.Value);
                _nextRepeat = now + RepeatInterval;
            }

            if (kb != null)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) _actions.Add(ShopAction.Confirm);
                if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) _actions.Add(ShopAction.Back);
                if (kb.qKey.wasPressedThisFrame) _actions.Add(ShopAction.PrevPlayer);
                if (kb.eKey.wasPressedThisFrame) _actions.Add(ShopAction.NextPlayer);
                if (kb.tabKey.wasPressedThisFrame) _actions.Add(shift ? ShopAction.PrevCategory : ShopAction.NextCategory);
            }
            if (pad != null)
            {
                if (pad.buttonSouth.wasPressedThisFrame) _actions.Add(ShopAction.Confirm);
                if (pad.buttonEast.wasPressedThisFrame) _actions.Add(ShopAction.Back);
                if (pad.leftShoulder.wasPressedThisFrame) _actions.Add(ShopAction.PrevPlayer);
                if (pad.rightShoulder.wasPressedThisFrame) _actions.Add(ShopAction.NextPlayer);
                if (pad.leftTrigger.wasPressedThisFrame) _actions.Add(ShopAction.PrevCategory);
                if (pad.rightTrigger.wasPressedThisFrame) _actions.Add(ShopAction.NextCategory);
                if (pad.buttonNorth.wasPressedThisFrame) _actions.Add(ShopAction.Save);
                if (pad.buttonWest.wasPressedThisFrame) _actions.Add(ShopAction.Reset);
            }

            float rotate = 0f;
            if (kb != null)
            {
                if (kb.aKey.isPressed) rotate += 1f;
                if (kb.dKey.isPressed) rotate -= 1f;
            }
            if (pad != null)
            {
                float x = pad.rightStick.ReadValue().x;
                if (Mathf.Abs(x) > 0.2f) rotate -= x;
            }
            Rotate = Mathf.Clamp(rotate, -1f, 1f);
            if (ActionPolled != null) foreach (ShopAction a in _actions) ActionPolled(a);
            return _actions;
        }

        private static ShopAction? HeldDirection(Keyboard kb, Gamepad pad)
        {
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed) return ShopAction.Up;
                if (kb.downArrowKey.isPressed) return ShopAction.Down;
                if (kb.leftArrowKey.isPressed) return ShopAction.Left;
                if (kb.rightArrowKey.isPressed) return ShopAction.Right;
            }
            if (pad != null)
            {
                Vector2 d = pad.dpad.ReadValue();
                Vector2 s = pad.leftStick.ReadValue();
                Vector2 v = d.sqrMagnitude > 0.1f ? d : s;
                if (v.magnitude > StickThreshold)
                {
                    if (Mathf.Abs(v.y) >= Mathf.Abs(v.x)) return v.y > 0 ? ShopAction.Up : ShopAction.Down;
                    return v.x > 0 ? ShopAction.Right : ShopAction.Left;
                }
            }
            return null;
        }
    }
}
