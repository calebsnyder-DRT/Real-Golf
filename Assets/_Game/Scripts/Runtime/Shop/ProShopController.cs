using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime.Views;
using Puttshack.Session;
using Puttshack.Shop;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Puttshack.Runtime.Shop
{
    public enum ShopZone
    {
        Back,
        Categories,
        Items,
        Actions,
        Players,
    }

    public enum ShopCategory
    {
        Tops,
        Bottoms,
        Shoes,
        Socks,
        Bags,
        Clubs,
    }

    /// <summary>
    /// Pro Shop presentation over LOBBY or PAUSED (spec section 28). Edits live in a <see cref="LoadoutEditor"/>; the
    /// dressed golfer, plinth and player tabs preview the unsaved look. Save sends one <c>SetLoadout</c> command for the
    /// edited player only; Back discards every unsaved edit and returns to where the shop was opened. Nothing here can
    /// touch clubs, aim or the shot: cosmetics are validated by the session and never read by the solver.
    /// </summary>
    public sealed class ProShopController : MonoBehaviour
    {
        public ProShopView View;
        public ProShopCameraRig CameraRig;
        public GolferAppearance Golfer;
        public Transform Turntable;
        public BagDisplay Plinth;
        public PortraitStudio Studio;
        public CosmeticArt Art;
        [Tooltip("Degrees per second while rotate is held.")]
        public float RotateSpeed = 110f;

        [Header("Interior ambient (applied while the shop is open)")]
        [Tooltip("The course lights its ambient from the open sky; indoors that reads cold and flat, so the shop swaps in a warm bounce.")]
        public bool ShopAmbient = true;
        public Color AmbientSky = new Color(0.96f, 0.88f, 0.76f);
        public Color AmbientEquator = new Color(0.78f, 0.64f, 0.5f);
        public Color AmbientGround = new Color(0.46f, 0.34f, 0.25f);
        [Range(0f, 2f)] public float AmbientIntensity = 1f;

        public bool IsOpen { get; private set; }
        public LoadoutEditor Editor { get; private set; }
        public ShopCategory Category { get; private set; }
        public ShopZone Zone { get; private set; }
        public int CategoryCount => CategoryKeys.Length;

        private static readonly string[] CategoryKeys = { "Top", "Bottom", "Shoes", "Socks", "Bag", "Clubs" };
        private static readonly string[] CategoryLabels = { "TOPS", "BOTTOMS", "SHOES", "SOCKS", "BAGS", "CLUBS" };
        private static readonly List<Entry>[] Entries = BuildEntries();

        private GameHost _host;
        private readonly ShopInput _input = new ShopInput();
        private readonly int[] _focus = new int[CategoryKeys.Length];
        private readonly int[] _scroll = new int[CategoryKeys.Length];
        private int _action;
        private ShopZone _returnZone = ShopZone.Items;
        private float _yaw;
        private GolfCameraRig _courseRig;
        private UniversalAdditionalCameraData _cameraData;
        private bool _cameraPost;
        private (AmbientMode Mode, Color Sky, Color Equator, Color Ground, float Intensity, SphericalHarmonicsL2 Probe)? _courseAmbient;
        private bool _dirty;
        private bool _wired;
        private bool _fromSetup;
        private int _openedFrame = -1;

        private struct Entry
        {
            public CosmeticItem Item;
            public ClubDefinition Club;
            public string Id => Item != null ? Item.Id : Club.Id;
        }

        // ---------------------------------------------------------------- Open / close

        /// <summary>
        /// <paramref name="fromSetup"/>: opened from Player Setup, so Back is labelled as returning there (the host reopens
        /// it once the shop closes).
        /// </summary>
        public void Open(GameHost host, ShopOrigin origin, int turnIndex, int editIndex, bool fromSetup = false)
        {
            if (IsOpen) return;
            if (Staged) EndStage();
            _host = host;
            _fromSetup = fromSetup;
            Editor = new LoadoutEditor(host.Coordinator.Snapshot.Data.Players, origin, turnIndex, editIndex);
            Category = ShopCategory.Tops;
            Zone = ShopZone.Items;
            _action = 0;
            _yaw = 0f;
            _focus[(int)ShopCategory.Clubs] = ClubCatalog.IndexOf("iron5");
            FocusEquipped();
            WireClicks();
            EnsureEventSystem();
            EnterStage(host.ReducedMotion);

            if (Studio != null) Studio.Invalidate();
            View.ReducedMotion = host.ReducedMotion;
            View.Show(true);
            IsOpen = true;
            _openedFrame = UnityEngine.Time.frameCount;
            ApplyPreview();
            Refresh();
        }

        // ---------------------------------------------------------------- Stage (Player Setup's live preview)

        /// <summary>True while Player Setup holds the shop floor (camera, lights, turntable golfer) without the shop UI.</summary>
        public bool Staged { get; private set; }

        public void BeginStage(GameHost host)
        {
            if (IsOpen || Staged) return;
            _host = host;
            _yaw = 0f;
            EnsureEventSystem();
            EnterStage(host.ReducedMotion);
            Staged = true;
        }

        public void EndStage()
        {
            if (!Staged) return;
            LeaveStage();
            Staged = false;
        }

        /// <summary>Dresses the turntable golfer as one player (outfit and profile) and puts their bag on the plinth.</summary>
        public void Preview(PlayerState player)
        {
            if (player == null) return;
            Loadout look = CosmeticCatalog.LoadoutOf(player);
            if (Golfer != null)
            {
                int before = Golfer.Shown;
                Golfer.Apply(look, ProfileCatalog.ProfileOf(player));
                GolferMotion motion = Golfer.GetComponent<GolferMotion>();
                if (motion != null && Golfer.Shown != before) motion.Refresh();
            }
            if (Plinth == null) return;
            Plinth.ClubSet = player.ColorIndex;
            Plinth.ShowBag(look.Bag);
        }

        /// <summary>Turns the turntable by <paramref name="degrees"/>; zero faces the golfer to the camera.</summary>
        public void Spin(float degrees, bool reset = false)
        {
            _yaw = reset ? 0f : Mathf.Repeat(_yaw + degrees + 180f, 360f) - 180f;
            if (Turntable != null) Turntable.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        private void EnterStage(bool reducedMotion)
        {
            Camera cam = Camera.main;
            _courseRig = cam != null ? cam.GetComponent<GolfCameraRig>() : null;
            if (_courseRig != null) _courseRig.enabled = false;
            if (cam != null && CameraRig != null) CameraRig.Begin(cam, reducedMotion);
            _cameraData = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
            if (_cameraData != null)
            {
                _cameraPost = _cameraData.renderPostProcessing;
                _cameraData.renderPostProcessing = true;
            }
            UseShopAmbient(true);
        }

        private void LeaveStage()
        {
            if (CameraRig != null) CameraRig.End();
            if (_cameraData != null) _cameraData.renderPostProcessing = _cameraPost;
            _cameraData = null;
            UseShopAmbient(false);
            if (_courseRig != null)
            {
                _courseRig.enabled = true;
                _courseRig.Snap();
            }
        }

        /// <summary>Back: unsaved edits for every player are discarded.</summary>
        public void Back()
        {
            if (!IsOpen) return;
            bool discarded = Editor.AnyUnsaved;
            Editor.DiscardAll();
            Close();
            if (discarded) _host.ShowNotice("Pro Shop: unsaved changes were not kept.");
        }

        private void Close()
        {
            View.Show(false);
            LeaveStage();
            IsOpen = false;
            ApplyPreview();
        }

        // ---------------------------------------------------------------- Commands (also used by tests and captures)

        public void SetCategory(ShopCategory category)
        {
            Category = category;
            FocusEquipped();
            ApplyPlinth();
            _dirty = true;
        }

        public bool Focus(string id)
        {
            List<Entry> list = Entries[(int)Category];
            int index = list.FindIndex(e => e.Id == id);
            if (index < 0) return false;
            _focus[(int)Category] = index;
            Zone = ShopZone.Items;
            ApplyPlinth();
            _dirty = true;
            return true;
        }

        /// <summary>Focuses and tries on an item in the current category. Returns false if nothing changed.</summary>
        public bool TryOn(string itemId)
        {
            if (!Focus(itemId) || Category == ShopCategory.Clubs) return false;
            bool changed = Editor.Equip(itemId);
            ApplyPreview();
            return changed;
        }

        public void SelectPlayer(int index)
        {
            Editor.SelectPlayer(index);
            OnPlayerChanged();
        }

        public bool Save()
        {
            int index = Editor.EditedIndex;
            string name = Editor.EditedPlayer.Name.ToUpperInvariant();
            if (!Editor.HasUnsaved(index))
            {
                View.ShowToast($"{name}'S LOADOUT IS UP TO DATE", 1.6f);
                return false;
            }
            Loadout pending = Editor.PendingSave;
            int id = Editor.EditedPlayerId;
            CommandReply reply = _host.Send(CommandType.SetLoadout, c =>
            {
                c.PlayerId = id;
                c.Loadout = pending;
            });
            _dirty = true;
            if (reply == null || !reply.Accepted)
            {
                View.ShowToast("COULD NOT SAVE \u00b7 " + (reply?.Detail ?? "TRY AGAIN").ToUpperInvariant(), 3f);
                return false;
            }
            PlayerState committed = _host.Coordinator.Snapshot.Data.Players.Find(p => p.Id == id);
            Editor.Committed(index, CosmeticCatalog.LoadoutOf(committed));
            View.ShowToast($"SAVED \u00b7 {name}'S LOADOUT", 2.2f);
            return true;
        }

        public void ResetLook()
        {
            Editor.Reset();
            ApplyPreview();
            View.ShowToast("STARTER LOOK \u00b7 SAVE TO KEEP IT", 2f);
        }

        public void ForceRefresh()
        {
            _dirty = true;
            Refresh();
        }

        // ---------------------------------------------------------------- Loop

        private void Update()
        {
            if (!IsOpen) return;
            SessionState state = _host.Coordinator.State;
            if (state != SessionState.LOBBY && state != SessionState.PAUSED)
            {
                Editor.DiscardAll();
                Close();
                _host.ShowNotice("Pro Shop closed - the round needs attention.");
                return;
            }

            IReadOnlyList<ShopAction> actions = _input.Poll(UnityEngine.Time.unscaledTime);
            if (UnityEngine.Time.frameCount != _openedFrame)
            {
                foreach (ShopAction action in actions)
                {
                    Handle(action);
                    if (!IsOpen) return;
                }
            }

            if (Mathf.Abs(_input.Rotate) > 0.01f) _yaw = Mathf.Repeat(_yaw + _input.Rotate * RotateSpeed * UnityEngine.Time.unscaledDeltaTime + 180f, 360f) - 180f;
            if (Turntable != null) Turntable.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            Refresh();
        }

        private void Handle(ShopAction action)
        {
            _dirty = true;
            switch (action)
            {
                case ShopAction.Back:
                    Back();
                    break;
                case ShopAction.Save:
                    Save();
                    break;
                case ShopAction.Reset:
                    ResetLook();
                    break;
                case ShopAction.PrevPlayer:
                    Editor.CyclePlayer(-1);
                    OnPlayerChanged();
                    break;
                case ShopAction.NextPlayer:
                    Editor.CyclePlayer(1);
                    OnPlayerChanged();
                    break;
                case ShopAction.PrevCategory:
                    SetCategory((ShopCategory)(((int)Category + CategoryCount - 1) % CategoryCount));
                    break;
                case ShopAction.NextCategory:
                    SetCategory((ShopCategory)(((int)Category + 1) % CategoryCount));
                    break;
                case ShopAction.Confirm:
                    Activate();
                    break;
                default:
                    Navigate(action);
                    break;
            }
        }

        private void Navigate(ShopAction dir)
        {
            int cat = (int)Category;
            int count = Entries[cat].Count;
            switch (Zone)
            {
                case ShopZone.Categories:
                    if (dir == ShopAction.Up)
                    {
                        if (cat > 0) SetCategory((ShopCategory)(cat - 1));
                        else Zone = ShopZone.Back;
                    }
                    else if (dir == ShopAction.Down)
                    {
                        if (cat < CategoryCount - 1) SetCategory((ShopCategory)(cat + 1));
                        else ToPlayers(ShopZone.Categories);
                    }
                    else if (dir == ShopAction.Right) Zone = ShopZone.Items;
                    break;
                case ShopZone.Items:
                    if (dir == ShopAction.Up && _focus[cat] > 0) MoveFocus(-1);
                    else if (dir == ShopAction.Down)
                    {
                        if (_focus[cat] < count - 1) MoveFocus(1);
                        else ToPlayers(ShopZone.Items);
                    }
                    else if (dir == ShopAction.Left) Zone = ShopZone.Categories;
                    else if (dir == ShopAction.Right)
                    {
                        Zone = ShopZone.Actions;
                        _action = 0;
                    }
                    break;
                case ShopZone.Actions:
                    if (dir == ShopAction.Up)
                    {
                        if (_action == 1) _action = 0;
                        else Zone = ShopZone.Back;
                    }
                    else if (dir == ShopAction.Down)
                    {
                        if (_action == 0) _action = 1;
                        else ToPlayers(ShopZone.Actions);
                    }
                    else if (dir == ShopAction.Left) Zone = ShopZone.Items;
                    break;
                case ShopZone.Back:
                    if (dir == ShopAction.Down)
                    {
                        Zone = ShopZone.Actions;
                        _action = 0;
                    }
                    else if (dir == ShopAction.Left) Zone = ShopZone.Items;
                    break;
                case ShopZone.Players:
                    if (dir == ShopAction.Left)
                    {
                        Editor.CyclePlayer(-1);
                        OnPlayerChanged();
                    }
                    else if (dir == ShopAction.Right)
                    {
                        Editor.CyclePlayer(1);
                        OnPlayerChanged();
                    }
                    else if (dir == ShopAction.Up) Zone = _returnZone;
                    break;
            }
        }

        private void Activate()
        {
            switch (Zone)
            {
                case ShopZone.Categories:
                case ShopZone.Players:
                    Zone = ShopZone.Items;
                    break;
                case ShopZone.Items:
                    if (Category == ShopCategory.Clubs)
                    {
                        View.ShowToast("EVERY PLAYER CARRIES ALL 9 CLUBS", 1.8f);
                        break;
                    }
                    Editor.Equip(FocusedEntry.Id);
                    ApplyPreview();
                    break;
                case ShopZone.Actions:
                    if (_action == 0) Save();
                    else ResetLook();
                    break;
                case ShopZone.Back:
                    Back();
                    break;
            }
        }

        private void MoveFocus(int delta)
        {
            int cat = (int)Category;
            _focus[cat] = Mathf.Clamp(_focus[cat] + delta, 0, Entries[cat].Count - 1);
            if (Category == ShopCategory.Clubs) ApplyPlinth();
        }

        private void ToPlayers(ShopZone from)
        {
            _returnZone = from;
            Zone = ShopZone.Players;
        }

        private void OnPlayerChanged()
        {
            _yaw = 0f;
            FocusEquipped();
            ApplyPreview();
            _dirty = true;
        }

        private Entry FocusedEntry => Entries[(int)Category][_focus[(int)Category]];

        private void FocusEquipped()
        {
            if (Editor == null) return;
            for (int c = 0; c < CategoryKeys.Length; c++)
            {
                if ((ShopCategory)c == ShopCategory.Clubs) continue;
                List<Entry> list = Entries[c];
                int equipped = list.FindIndex(e => Editor.StateOf(e.Id) == ItemState.Equipped);
                _focus[c] = Mathf.Max(0, equipped);
            }
        }

        // ---------------------------------------------------------------- Presentation

        private static int CharacterOf(PlayerState p) => GolferAppearance.CharacterOf(p);

        private void ApplyPreview()
        {
            if (Editor == null) return;
            if (Golfer != null)
            {
                int before = Golfer.Shown;
                Golfer.Apply(Editor.Current, ProfileCatalog.ProfileOf(Editor.EditedPlayer));
                GolferMotion motion = Golfer.GetComponent<GolferMotion>();
                if (motion != null && Golfer.Shown != before) motion.Refresh();
            }
            ApplyPlinth();
            _dirty = true;
        }

        private void ApplyPlinth()
        {
            if (Plinth == null || Editor == null) return;
            if (Editor.EditedPlayer != null) Plinth.ClubSet = Editor.EditedPlayer.ColorIndex;
            if (IsOpen && Category == ShopCategory.Clubs) Plinth.ShowClub(FocusedEntry.Id);
            else Plinth.ShowBag(Editor.Current.Bag);
        }

        private void Refresh()
        {
            if (!_dirty || Editor == null) return;
            _dirty = false;
            View.BeginFrame();
            bool clubs = Category == ShopCategory.Clubs;

            View.SetTitle(clubs || Category == ShopCategory.Bags ? "PRO SHOP" : "PLAYER LOADOUT");
            View.SetBack(Editor.Origin == ShopOrigin.Paused ? "BACK TO ROUND" : _fromSetup ? "BACK TO PLAYERS" : "BACK TO LOBBY", Zone == ShopZone.Back);

            for (int c = 0; c < View.Categories.Length; c++)
            {
                if (c >= CategoryKeys.Length)
                {
                    View.HideCategory(c);
                    continue;
                }
                View.SetCategory(c, CategoryLabels[c], Art != null ? Art.CategoryIcon(CategoryKeys[c]) : null, c == (int)Category, Zone == ShopZone.Categories && c == (int)Category);
            }

            RenderRows();
            if (clubs) RenderClubDetails();
            else RenderSummary();

            View.SetNote(clubs ? "FULL 9-CLUB BAG FOR EVERY PLAYER \u00b7 VIEW ONLY"
                : Category == ShopCategory.Bags ? "BAGS ARE COSMETIC \u00b7 SAME 9 CLUBS"
                : "COSMETIC ONLY \u00b7 NO GAMEPLAY EFFECT");

            int cost = NewItemsCost();
            bool unsaved = Editor.HasUnsaved(Editor.EditedIndex);
            View.SetButton(View.Save, cost > 0 ? $"SAVE LOADOUT \u00b7 {cost} {CosmeticCatalog.PriceUnit}" : "SAVE LOADOUT", unsaved, Zone == ShopZone.Actions && _action == 0, true);
            View.SetButton(View.Reset, "RESET", true, Zone == ShopZone.Actions && _action == 1, false);

            View.LayoutPlayers(Editor.PlayerCount);
            for (int i = 0; i < Editor.PlayerCount && i < View.Players.Length; i++)
            {
                PlayerState p = Editor.Player(i);
                View.SetPlayer(i, new ShopPlayerModel
                {
                    Name = p.Name,
                    Identity = Palette.Player(p.ColorIndex),
                    Portrait = Studio != null ? Studio.Portrait(i, Editor.Working(i), CharacterOf(p), ProfileCatalog.ProfileOf(p)) : null,
                    Edited = i == Editor.EditedIndex,
                    Focused = Zone == ShopZone.Players && i == Editor.EditedIndex,
                    Turn = Editor.Origin == ShopOrigin.Paused && i == Editor.TurnIndex,
                    Unsaved = Editor.HasUnsaved(i),
                });
            }
            View.SetPlayerHints(Editor.PlayerCount > 1);
            View.SetRotateHint(!clubs);
            bool pad = UnityEngine.InputSystem.Gamepad.current != null;
            string browse = Key(pad ? "D-PAD" : "\u2191\u2193"), player = Key(pad ? "LB / RB" : "Q / E"), back = Key(pad ? "B" : "ESC");
            View.SetHints(clubs
                ? $"{browse} BROWSE CLUBS\n{player} PLAYER\n{back} BACK"
                : $"{browse} BROWSE   {Key(pad ? "A" : "ENTER")} TRY ON\n{Key(pad ? "Y" : "\u2192")} SAVE   {Key(pad ? "R-STICK" : "A / D")} ROTATE\n{player} PLAYER   {back} BACK");
        }

        private static string Key(string label) => $"<color=#00CBE7>{label}</color>";

        private void UseShopAmbient(bool shop)
        {
            if (shop && ShopAmbient && _courseAmbient == null)
            {
                _courseAmbient = (RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor,
                    RenderSettings.ambientGroundColor, RenderSettings.ambientIntensity, RenderSettings.ambientProbe);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = AmbientSky;
                RenderSettings.ambientEquatorColor = AmbientEquator;
                RenderSettings.ambientGroundColor = AmbientGround;
                RenderSettings.ambientIntensity = AmbientIntensity;
                RenderSettings.ambientProbe = TrilightProbe(AmbientSky.linear, AmbientEquator.linear, AmbientGround.linear, AmbientIntensity);
            }
            else if (!shop && _courseAmbient is var (mode, sky, equator, ground, intensity, probe))
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.ambientProbe = probe;
                _courseAmbient = null;
            }
        }

        /// <summary>Sky/equator/ground gradient as the L2 probe shaders read (Unity only rebuilds it for baked lighting).</summary>
        private static SphericalHarmonicsL2 TrilightProbe(Color sky, Color equator, Color ground, float intensity)
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(equator * intensity);
            sh.AddDirectionalLight(Vector3.up, (sky - equator) * intensity, 0.9f);
            sh.AddDirectionalLight(Vector3.down, (ground - equator) * intensity, 0.9f);
            return sh;
        }

        private void RenderRows()
        {
            int cat = (int)Category;
            List<Entry> list = Entries[cat];
            int visible = View.Rows.Length;
            int focus = _focus[cat] = Mathf.Clamp(_focus[cat], 0, list.Count - 1);
            int scroll = _scroll[cat];
            if (focus < scroll) scroll = focus;
            if (focus > scroll + visible - 1) scroll = focus - visible + 1;
            scroll = _scroll[cat] = Mathf.Clamp(scroll, 0, Mathf.Max(0, list.Count - visible));

            int owned = 0;
            for (int r = 0; r < visible; r++)
            {
                int i = scroll + r;
                if (i >= list.Count)
                {
                    View.SetRow(r, null);
                    continue;
                }
                Entry e = list[i];
                var model = new ShopRowModel
                {
                    Focused = i == focus,
                    ZoneActive = Zone == ShopZone.Items,
                    Icon = Art != null ? Art.Icon(e.Club != null ? "club." + e.Club.Id : e.Id) : null,
                };
                if (e.Club != null)
                {
                    model.Name = e.Club.DisplayName.ToUpperInvariant();
                    model.Detail = e.Club.ShotType == ShotType.Putt ? "FEET SCALE \u00b7 IN THE BAG" : $"{e.Club.RangeMinYd}-{e.Club.RangeMaxYd} YD \u00b7 IN THE BAG";
                }
                else
                {
                    ItemState state = Editor.StateOf(e.Id);
                    model.Name = e.Item.Name.ToUpperInvariant();
                    model.Equipped = state == ItemState.Equipped;
                    model.ForSale = state == ItemState.ForSale;
                    string tail = state == ItemState.Equipped ? "EQUIPPED" : state == ItemState.Owned ? "OWNED" : $"{e.Item.Price} {CosmeticCatalog.PriceUnit}";
                    model.Detail = $"{e.Item.StyleLabel} \u00b7 {tail}";
                }
                View.SetRow(r, model);
            }

            foreach (Entry e in list)
            {
                if (e.Club != null || CosmeticCatalog.IsOwned(Editor.Saved(Editor.EditedIndex), e.Id)) owned++;
            }
            string label = Category == ShopCategory.Clubs ? "IN THE BAG" : "OWNED";
            View.SetScroll(scroll > 0, scroll + visible < list.Count, $"{label}  <color=#00CBE7>{owned}/{list.Count}</color>");
        }

        private void RenderSummary()
        {
            View.ShowLoadoutDetails("EQUIPPED");
            Loadout current = Editor.Current;
            Loadout saved = Editor.Saved(Editor.EditedIndex);
            for (int i = 0; i < View.Summary.Length; i++)
            {
                if (i >= Loadout.Slots.Length)
                {
                    View.Summary[i].Root.SetActive(false);
                    continue;
                }
                CosmeticSlot slot = Loadout.Slots[i];
                CosmeticItem item = CosmeticCatalog.Get(current.Get(slot));
                string label = slot == CosmeticSlot.Socks ? "SOCKS" : slot == CosmeticSlot.Belt ? "BELT" : item.StyleLabel;
                View.SetSummary(i, Art != null ? Art.Icon(item.Id) : null, label, item.Name.ToUpperInvariant(), current.Get(slot) != saved.Get(slot));
            }
        }

        private void RenderClubDetails()
        {
            ClubDefinition club = FocusedEntry.Club;
            ClubTraits traits = ClubTraits.Of(club);
            var stats = new double[ClubTraits.Labels.Length];
            for (int i = 0; i < stats.Length; i++) stats[i] = traits[i];
            string range = club.ShotType == ShotType.Putt ? "ROLL IN FEET" : $"{club.RangeMinYd}\u2013{club.RangeMaxYd} YD";
            View.ShowClubDetails(club.DisplayName.ToUpperInvariant(), range, stats, traits.Summary);
        }

        private int NewItemsCost()
        {
            Loadout saved = Editor.Saved(Editor.EditedIndex);
            Loadout current = Editor.Current;
            int cost = 0;
            foreach (CosmeticSlot slot in Loadout.Slots)
            {
                string id = current.Get(slot);
                if (!CosmeticCatalog.IsOwned(saved, id)) cost += CosmeticCatalog.Get(id).Price;
            }
            return cost;
        }

        private static List<Entry>[] BuildEntries()
        {
            var lists = new List<Entry>[CategoryKeys.Length];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<Entry>();
            foreach (CosmeticItem item in CosmeticCatalog.All)
            {
                switch (item.Slot)
                {
                    case CosmeticSlot.Top: lists[(int)ShopCategory.Tops].Add(new Entry { Item = item }); break;
                    case CosmeticSlot.Bottom: lists[(int)ShopCategory.Bottoms].Add(new Entry { Item = item }); break;
                    case CosmeticSlot.Shoes: lists[(int)ShopCategory.Shoes].Add(new Entry { Item = item }); break;
                    case CosmeticSlot.Socks: lists[(int)ShopCategory.Socks].Add(new Entry { Item = item }); break;
                    case CosmeticSlot.Bag: lists[(int)ShopCategory.Bags].Add(new Entry { Item = item }); break;
                }
            }
            // Belts finish the Bottoms list, after chinos and shorts.
            foreach (CosmeticItem item in CosmeticCatalog.ForSlot(CosmeticSlot.Belt)) lists[(int)ShopCategory.Bottoms].Add(new Entry { Item = item });
            foreach (ClubDefinition club in ClubCatalog.All) lists[(int)ShopCategory.Clubs].Add(new Entry { Club = club });
            return lists;
        }

        // ---------------------------------------------------------------- Pointer

        private void WireClicks()
        {
            if (_wired || View == null) return;
            _wired = true;
            for (int i = 0; i < View.Rows.Length; i++)
            {
                int row = i;
                View.Rows[i].Button?.onClick.AddListener(() => Click(() =>
                {
                    Zone = ShopZone.Items;
                    _focus[(int)Category] = _scroll[(int)Category] + row;
                    Activate();
                    if (Category == ShopCategory.Clubs) ApplyPlinth();
                }));
            }
            for (int i = 0; i < View.Categories.Length && i < CategoryKeys.Length; i++)
            {
                var category = (ShopCategory)i;
                View.Categories[i].Button?.onClick.AddListener(() => Click(() =>
                {
                    Zone = ShopZone.Categories;
                    SetCategory(category);
                }));
            }
            for (int i = 0; i < View.Players.Length; i++)
            {
                int player = i;
                View.Players[i].Button?.onClick.AddListener(() => Click(() => SelectPlayer(player)));
            }
            View.Save.Button?.onClick.AddListener(() => Click(() => Save()));
            View.Reset.Button?.onClick.AddListener(() => Click(ResetLook));
            View.Back.Button?.onClick.AddListener(() => Click(Back));
        }

        private void Click(System.Action action)
        {
            if (!IsOpen) return;
            action();
            _dirty = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
