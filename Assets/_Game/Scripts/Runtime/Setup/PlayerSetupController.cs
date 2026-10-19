using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime.Shop;
using Puttshack.Session;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Puttshack.Runtime.Setup
{
    public enum SetupField
    {
        Back,
        Count,
        Players,
        Name,
        Body,
        Skin,
        Hair,
        Style,
        Shop,
        Start,
    }

    /// <summary>
    /// Player Setup over LOBBY: how many play, and per player their name, body (male or female), skin tone, hair colour
    /// and hair style, previewed live on the Pro Shop turntable. Every change is one <c>RosterSet</c> carrying all names
    /// and profiles, so the session (and the remembered roster) always holds what is on screen; the round's turns and
    /// scorecard follow that roster. Appearance only: nothing here reaches the rules, solver or scoring.
    /// </summary>
    public sealed class PlayerSetupController : MonoBehaviour
    {
        public PlayerSetupView View;
        [Tooltip("Degrees per second while rotate is held.")]
        public float RotateSpeed = 110f;

        public bool IsOpen { get; private set; }
        public SetupField Field { get; private set; } = SetupField.Players;
        public int Selected { get; private set; }

        private static readonly SetupField[] Order =
        {
            SetupField.Back, SetupField.Count, SetupField.Players, SetupField.Name, SetupField.Body, SetupField.Skin,
            SetupField.Hair, SetupField.Style, SetupField.Shop, SetupField.Start,
        };

        private GameHost _host;
        private ProShopController _shop;
        private readonly ShopInput _input = new ShopInput();
        private int _openedFrame = -1;
        private int _quietFrame = -1;
        private bool _wired;

        private List<PlayerState> Players => _host.Coordinator.Snapshot.Data.Players;
        public PlayerState SelectedPlayer => Selected < Players.Count ? Players[Selected] : null;
        public PlayerProfile SelectedProfile => ProfileCatalog.ProfileOf(SelectedPlayer);

        // ---------------------------------------------------------------- Open / close

        public void Open(GameHost host, ProShopController shop, int selected = 0)
        {
            if (IsOpen) return;
            _host = host;
            _shop = shop;
            Selected = Mathf.Clamp(selected, 0, Mathf.Max(0, Players.Count - 1));
            Field = SetupField.Players;
            WireClicks();
            if (_shop != null)
            {
                _shop.BeginStage(host);
                if (_shop.Studio != null) _shop.Studio.Invalidate();
            }
            View.Show(true);
            IsOpen = true;
            _openedFrame = UnityEngine.Time.frameCount;
            Preview();
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (View.NameField != null && View.NameField.isFocused) View.NameField.DeactivateInputField();
            View.Show(false);
            if (_shop != null) _shop.EndStage();
            IsOpen = false;
        }

        // ---------------------------------------------------------------- Commands (also used by tests and captures)

        /// <summary>Adds default players or drops the last ones; kept players keep their names and golfers.</summary>
        public bool SetCount(int count)
        {
            count = Mathf.Clamp(count, 1, SessionCoordinator.MaxPlayers);
            List<string> names = Names();
            List<PlayerProfile> profiles = Profiles();
            if (count == names.Count) return false;
            while (names.Count < count)
            {
                names.Add(SessionCoordinator.DefaultName(names));
                profiles.Add(null);
            }
            if (names.Count > count)
            {
                names.RemoveRange(count, names.Count - count);
                profiles.RemoveRange(count, profiles.Count - count);
            }
            bool ok = Commit(names, profiles);
            Selected = Mathf.Clamp(Selected, 0, Players.Count - 1);
            Preview();
            return ok;
        }

        public void Select(int index)
        {
            if (index < 0 || index >= Players.Count) return;
            Selected = index;
            if (_shop != null) _shop.Spin(0f, reset: true);
            Preview();
        }

        public bool Rename(int index, string name)
        {
            List<string> names = Names();
            if (index < 0 || index >= names.Count) return false;
            name = (name ?? "").Trim();
            if (name == names[index]) return false;
            if (name.Length == 0)
            {
                View.ShowToast("A NAME NEEDS AT LEAST ONE LETTER", 2f);
                return false;
            }
            names[index] = name.Length > SessionCoordinator.MaxNameLength ? name.Substring(0, SessionCoordinator.MaxNameLength) : name;
            return Commit(names, Profiles());
        }

        public bool SetGender(Gender gender) => Edit(p => p.Gender = gender);

        public bool SetSkin(string id) => Edit(p => p.Skin = id);

        public bool SetHairColor(string id) => Edit(p => p.HairColor = id);

        public bool SetHairStyle(string id) => Edit(p => p.HairStyle = id);

        /// <summary>Into the Pro Shop on the selected player; its Back returns here.</summary>
        public bool OpenShop()
        {
            int selected = Selected;
            Close();
            if (_host.OpenShop(selected, fromSetup: true)) return true;
            Open(_host, _shop, selected);
            return false;
        }

        /// <summary>Confirms the lobby settings (assistance, pace cap) and tees off with this roster.</summary>
        public bool StartRound()
        {
            CommandReply reply = _host.StartRound();
            if (reply != null && reply.Accepted)
            {
                Close();
                return true;
            }
            View.ShowToast("COULD NOT START \u00b7 " + (reply?.Detail ?? "TRY AGAIN").ToUpperInvariant(), 3f);
            return false;
        }

        /// <summary>To the lobby card (assistance and pace settings); S there reopens Player Setup.</summary>
        public void Back() => Close();

        public void ForceRefresh() => Refresh();

        // ---------------------------------------------------------------- Loop

        private void Update()
        {
            if (!IsOpen) return;
            if (_host.Coordinator.State != SessionState.LOBBY || Players.Count == 0)
            {
                Close();
                return;
            }

            bool typing = View.NameField != null && View.NameField.isFocused;
            if (!typing)
            {
                IReadOnlyList<ShopAction> actions = _input.Poll(UnityEngine.Time.unscaledTime);
                int frame = UnityEngine.Time.frameCount;
                if (frame != _openedFrame && frame != _quietFrame)
                {
                    foreach (ShopAction action in actions)
                    {
                        Handle(action);
                        if (!IsOpen) return;
                    }
                }
                if (_shop != null && Mathf.Abs(_input.Rotate) > 0.01f) _shop.Spin(_input.Rotate * RotateSpeed * UnityEngine.Time.unscaledDeltaTime);
            }
            Refresh();
        }

        private void Handle(ShopAction action)
        {
            switch (action)
            {
                case ShopAction.Back:
                    Back();
                    break;
                case ShopAction.PrevPlayer:
                    Select((Selected + Players.Count - 1) % Players.Count);
                    break;
                case ShopAction.NextPlayer:
                    Select((Selected + 1) % Players.Count);
                    break;
                case ShopAction.Up:
                    if (Field == SetupField.Players && Selected > 0) Select(Selected - 1);
                    else Move(-1);
                    break;
                case ShopAction.Down:
                    if (Field == SetupField.Players && Selected < Players.Count - 1) Select(Selected + 1);
                    else Move(1);
                    break;
                case ShopAction.Left:
                    Step(-1);
                    break;
                case ShopAction.Right:
                    Step(1);
                    break;
                case ShopAction.Confirm:
                    Activate();
                    break;
            }
        }

        private void Move(int delta)
        {
            int i = Array.IndexOf(Order, Field);
            Field = Order[(i + delta + Order.Length) % Order.Length];
        }

        /// <summary>Left / right changes the focused setting in place.</summary>
        private void Step(int delta)
        {
            PlayerProfile p = SelectedProfile;
            switch (Field)
            {
                case SetupField.Count:
                    SetCount(Players.Count + delta);
                    break;
                case SetupField.Body:
                    SetGender(p.Gender == Gender.Male ? Gender.Female : Gender.Male);
                    break;
                case SetupField.Skin:
                    SetSkin(ProfileCatalog.Step(ProfileCatalog.SkinTones, p.Skin, delta));
                    break;
                case SetupField.Hair:
                    SetHairColor(ProfileCatalog.Step(ProfileCatalog.HairColors, p.HairColor, delta));
                    break;
                case SetupField.Style:
                    SetHairStyle(ProfileCatalog.Step(ProfileCatalog.HairStyles, p.HairStyle, delta));
                    break;
                case SetupField.Shop:
                case SetupField.Start:
                    Field = delta < 0 ? SetupField.Shop : SetupField.Start;
                    break;
            }
        }

        private void Activate()
        {
            switch (Field)
            {
                case SetupField.Back:
                    Back();
                    break;
                case SetupField.Players:
                    Field = SetupField.Name;
                    break;
                case SetupField.Name:
                    EditName();
                    break;
                case SetupField.Shop:
                    OpenShop();
                    break;
                case SetupField.Start:
                    StartRound();
                    break;
                default:
                    Step(1);
                    break;
            }
        }

        private void EditName()
        {
            if (View.NameField == null) return;
            Field = SetupField.Name;
            View.NameField.Select();
            View.NameField.ActivateInputField();
        }

        // ---------------------------------------------------------------- Session

        private bool Edit(Action<PlayerProfile> change)
        {
            List<PlayerProfile> profiles = Profiles();
            if (Selected >= profiles.Count) return false;
            PlayerProfile before = profiles[Selected].Clone();
            change(profiles[Selected]);
            if (profiles[Selected].Same(before)) return false;
            bool ok = Commit(Names(), profiles);
            Preview();
            return ok;
        }

        private bool Commit(List<string> names, List<PlayerProfile> profiles)
        {
            CommandReply reply = _host.Send(CommandType.RosterSet, c =>
            {
                c.Names = names;
                c.Profiles = profiles;
            });
            if (reply != null && reply.Accepted) return true;
            View.ShowToast("NOT SAVED \u00b7 " + (reply?.Detail ?? "TRY AGAIN").ToUpperInvariant(), 3f);
            return false;
        }

        private List<string> Names() => Players.ConvertAll(p => p.Name);

        private List<PlayerProfile> Profiles() => Players.ConvertAll(ProfileCatalog.ProfileOf);

        private void Preview()
        {
            if (_shop != null && IsOpen) _shop.Preview(SelectedPlayer);
        }

        // ---------------------------------------------------------------- Presentation

        private void Refresh()
        {
            if (!IsOpen) return;
            List<PlayerState> players = Players;
            PlayerState selected = SelectedPlayer;
            PlayerProfile profile = SelectedProfile;
            PortraitStudio studio = _shop != null ? _shop.Studio : null;

            View.SetHeader(players.Count);
            View.SetBack(Field == SetupField.Back);
            View.SetCount(players.Count, SessionCoordinator.MaxPlayers, Field == SetupField.Count);
            for (int i = 0; i < View.Rows.Length; i++)
            {
                if (i >= players.Count)
                {
                    View.SetRow(i, null);
                    continue;
                }
                PlayerState p = players[i];
                PlayerProfile pp = ProfileCatalog.ProfileOf(p);
                View.SetRow(i, new SetupRowModel
                {
                    Name = p.Name,
                    Detail = Describe(pp),
                    Identity = Palette.Player(p.ColorIndex),
                    Portrait = studio != null ? studio.Portrait(i, CosmeticCatalog.LoadoutOf(p), GolferAppearance.CharacterOf(pp), pp) : null,
                    Selected = i == Selected,
                    Focused = Field == SetupField.Players && i == Selected,
                });
            }

            View.SetSection(View.Name, $"PLAYER {Selected + 1}", Field == SetupField.Name);
            View.SetName(selected != null ? selected.Name : "");
            View.SetSection(View.Body, ProfileCatalog.GenderLabel(profile.Gender), Field == SetupField.Body);
            for (int i = 0; i < View.Bodies.Length; i++)
            {
                if (i < ProfileCatalog.Genders.Length) View.SetChoice(View.Bodies[i], ProfileCatalog.GenderLabel(ProfileCatalog.Genders[i]), ProfileCatalog.Genders[i] == profile.Gender);
                else View.Bodies[i].Root.gameObject.SetActive(false);
            }
            RenderSwatches(View.Skin, View.SkinSwatches, ProfileCatalog.SkinTones, profile.Skin, Field == SetupField.Skin);
            RenderSwatches(View.Hair, View.HairSwatches, ProfileCatalog.HairColors, profile.HairColor, Field == SetupField.Hair);
            ProfileOption style = ProfileCatalog.Find(ProfileCatalog.HairStyles, profile.HairStyle);
            View.SetSection(View.Style, style != null ? style.Name.ToUpperInvariant() : "", Field == SetupField.Style);
            for (int i = 0; i < View.Styles.Length; i++)
            {
                if (i < ProfileCatalog.HairStyles.Count) View.SetChoice(View.Styles[i], ProfileCatalog.HairStyles[i].Name.ToUpperInvariant(), ProfileCatalog.HairStyles[i].Id == profile.HairStyle);
                else View.Styles[i].Root.gameObject.SetActive(false);
            }

            View.SetAction(View.Shop, "PRO SHOP \u00b7 OUTFIT", Field == SetupField.Shop);
            View.SetAction(View.Start, players.Count == 1 ? "START ROUND \u00b7 SOLO" : $"START ROUND \u00b7 {players.Count} PLAYERS", Field == SetupField.Start);

            bool pad = UnityEngine.InputSystem.Gamepad.current != null;
            string browse = Key(pad ? "D-PAD" : "\u2191\u2193"), change = Key(pad ? "D-PAD" : "\u2190\u2192"), player = Key(pad ? "LB / RB" : "Q / E");
            View.SetHints(Field == SetupField.Name && !pad
                ? $"{Key("ENTER")} TYPE A NAME   {player} PLAYER\n{browse} MOVE   {Key("A / D")} ROTATE\n{Key("ESC")} LOBBY"
                : $"{browse} MOVE   {change} CHANGE\n{player} PLAYER   {Key(pad ? "R-STICK" : "A / D")} ROTATE\n{Key(pad ? "A" : "ENTER")} SELECT   {Key(pad ? "B" : "ESC")} LOBBY");
        }

        private void RenderSwatches(SetupSection section, SetupSwatch[] swatches, IReadOnlyList<ProfileOption> options, string current, bool focused)
        {
            ProfileOption chosen = ProfileCatalog.Find(options, current);
            View.SetSection(section, chosen != null ? chosen.Name.ToUpperInvariant() : "", focused);
            for (int i = 0; i < swatches.Length; i++)
            {
                if (i >= options.Count)
                {
                    View.HideSwatch(swatches[i]);
                    continue;
                }
                ColorUtility.TryParseHtmlString(options[i].Hex, out Color c);
                View.SetSwatch(swatches[i], c, options[i].Id == current);
            }
        }

        private static string Describe(PlayerProfile p)
        {
            string skin = ProfileCatalog.Find(ProfileCatalog.SkinTones, p.Skin)?.Name ?? "";
            string hair = ProfileCatalog.Find(ProfileCatalog.HairColors, p.HairColor)?.Name ?? "";
            string style = ProfileCatalog.Find(ProfileCatalog.HairStyles, p.HairStyle)?.Name ?? "";
            return $"{ProfileCatalog.GenderLabel(p.Gender)} \u00b7 {skin} \u00b7 {hair} {style}".ToUpperInvariant();
        }

        private static string Key(string label) => $"<color=#00CBE7>{label}</color>";

        // ---------------------------------------------------------------- Pointer

        private void WireClicks()
        {
            if (_wired || View == null) return;
            _wired = true;
            View.Back.Button?.onClick.AddListener(() => Click(SetupField.Back, Back));
            View.Fewer.Button?.onClick.AddListener(() => Click(SetupField.Count, () => SetCount(Players.Count - 1)));
            View.More.Button?.onClick.AddListener(() => Click(SetupField.Count, () => SetCount(Players.Count + 1)));
            for (int i = 0; i < View.Rows.Length; i++)
            {
                int row = i;
                View.Rows[i].Button?.onClick.AddListener(() => Click(SetupField.Players, () => Select(row)));
            }
            for (int i = 0; i < View.Bodies.Length && i < ProfileCatalog.Genders.Length; i++)
            {
                Gender g = ProfileCatalog.Genders[i];
                View.Bodies[i].Button?.onClick.AddListener(() => Click(SetupField.Body, () => SetGender(g)));
            }
            for (int i = 0; i < View.SkinSwatches.Length && i < ProfileCatalog.SkinTones.Count; i++)
            {
                string id = ProfileCatalog.SkinTones[i].Id;
                View.SkinSwatches[i].Button?.onClick.AddListener(() => Click(SetupField.Skin, () => SetSkin(id)));
            }
            for (int i = 0; i < View.HairSwatches.Length && i < ProfileCatalog.HairColors.Count; i++)
            {
                string id = ProfileCatalog.HairColors[i].Id;
                View.HairSwatches[i].Button?.onClick.AddListener(() => Click(SetupField.Hair, () => SetHairColor(id)));
            }
            for (int i = 0; i < View.Styles.Length && i < ProfileCatalog.HairStyles.Count; i++)
            {
                string id = ProfileCatalog.HairStyles[i].Id;
                View.Styles[i].Button?.onClick.AddListener(() => Click(SetupField.Style, () => SetHairStyle(id)));
            }
            View.Shop.Button?.onClick.AddListener(() => Click(SetupField.Shop, () => OpenShop()));
            View.Start.Button?.onClick.AddListener(() => Click(SetupField.Start, () => StartRound()));
            if (View.NameField != null)
            {
                View.NameField.characterLimit = SessionCoordinator.MaxNameLength;
                View.NameField.onSelect.AddListener(_ => Field = SetupField.Name);
                View.NameField.onEndEdit.AddListener(text =>
                {
                    // The Enter or Esc that ended typing must not also act on the screen this frame.
                    _quietFrame = UnityEngine.Time.frameCount;
                    if (IsOpen) Rename(Selected, text);
                });
            }
        }

        private void Click(SetupField field, Action action)
        {
            if (!IsOpen) return;
            Field = field;
            action();
            if (IsOpen && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
