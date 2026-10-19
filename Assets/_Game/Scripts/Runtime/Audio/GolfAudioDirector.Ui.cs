using System;
using System.Collections.Generic;
using Puttshack.Domain;
using Puttshack.Runtime.Shop;
using Puttshack.Session;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Puttshack.Runtime.Audio
{
    /// <summary>
    /// Menu clicks, from three places so no menu wires its own sounds: every action the Pro Shop and Player Setup poll
    /// (<see cref="ShopInput.ActionPolled"/>), a listener added to every uGUI <see cref="Button"/> for pointer clicks,
    /// and ENTER / ESC on the round's own pages (briefing, hole summary, standings, lobby). Same-frame repeats collapse
    /// through the cues' cooldowns.
    /// </summary>
    public sealed partial class GolfAudioDirector
    {
        private const float ButtonScanSeconds = 1.5f;

        private readonly HashSet<int> _wiredButtons = new HashSet<int>();
        private float _nextButtonScan;
        private bool _wasInterior;

        private void OnMenuAction(ShopAction action)
        {
            switch (action)
            {
                case ShopAction.Confirm:
                case ShopAction.Save:
                    PlayUiCue(GolfSound.UiConfirm);
                    break;
                case ShopAction.Back:
                case ShopAction.Reset:
                    PlayUiCue(GolfSound.UiBack);
                    break;
                default:
                    PlayUiCue(GolfSound.UiSelect, 0.7f);
                    break;
            }
        }

        private void PlayUiCue(GolfSound id, float volume = 1f)
        {
            if (_pool != null) PlayFlat(id, volume);
        }

        private void UpdateUi(SessionSnapshot snap)
        {
            // Menus build or reveal their buttons when they open; look again then, and now and then while open.
            bool opened = _interior && !_wasInterior;
            _wasInterior = _interior;
            if (opened || (_interior && UnityEngine.Time.unscaledTime >= _nextButtonScan)) WireButtons();
            if (!_interior) PageKeys(snap);
        }

        private void WireButtons()
        {
            _nextButtonScan = UnityEngine.Time.unscaledTime + ButtonScanSeconds;
            foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include))
            {
                if (!_wiredButtons.Add(button.GetInstanceID())) continue;
                GolfSound cue = ButtonCue(button.name);
                button.onClick.AddListener(() => PlayUiCue(cue, cue == GolfSound.UiSelect ? 0.7f : 1f));
            }
        }

        private static GolfSound ButtonCue(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("back") || n.Contains("close") || n.Contains("cancel")) return GolfSound.UiBack;
            if (n.Contains("start") || n.Contains("save") || n.Contains("shop") || n.Contains("play") || n.Contains("confirm")) return GolfSound.UiConfirm;
            return GolfSound.UiSelect;
        }

        /// <summary>The keys GameHost and RoundFlow read on the round's pages (the shot itself has its own sounds).</summary>
        private void PageKeys(SessionSnapshot snap)
        {
            SessionState state = snap.EffectiveState;
            bool page = state == SessionState.ATTRACT || state == SessionState.LOBBY || state == SessionState.BRIEFING ||
                        state == SessionState.HOLE_COMPLETE || state == SessionState.ROUND_COMPLETE;
            if (!page || _paused) return;
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) ||
                           (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (confirm) PlayUiCue(GolfSound.UiConfirm);
            else if (back) PlayUiCue(GolfSound.UiBack);
            else if (state == SessionState.LOBBY && kb != null &&
                     (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame))
                PlayUiCue(GolfSound.UiSelect, 0.7f);
        }
    }
}
