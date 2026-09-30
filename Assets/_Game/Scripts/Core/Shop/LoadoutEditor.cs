using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;

namespace Puttshack.Shop
{
    public enum ShopOrigin
    {
        Lobby,
        Paused,
    }

    public enum ItemState
    {
        ForSale,
        Owned,
        Equipped,
    }

    /// <summary>
    /// Unsaved Pro Shop edits over a detached copy of the committed roster (spec section 28). Each player has at most one
    /// draft. <see cref="PendingSave"/> covers only the player being edited; <see cref="DiscardAll"/> is Back, which
    /// restores every unsaved value. The player being edited is independent of whose turn it is.
    /// </summary>
    public sealed class LoadoutEditor
    {
        private readonly List<PlayerState> _players = new List<PlayerState>();
        private readonly Dictionary<int, Loadout> _drafts = new Dictionary<int, Loadout>();

        public ShopOrigin Origin { get; }
        /// <summary>Roster index whose turn it is behind a paused round, or -1 before the round.</summary>
        public int TurnIndex { get; }
        public int EditedIndex { get; private set; }

        public LoadoutEditor(IReadOnlyList<PlayerState> roster, ShopOrigin origin, int turnIndex = -1, int editIndex = 0)
        {
            if (roster == null || roster.Count == 0) throw new ArgumentException("The shop needs at least one player.", nameof(roster));
            foreach (PlayerState p in roster)
            {
                _players.Add(new PlayerState
                {
                    Id = p.Id,
                    RosterIndex = p.RosterIndex,
                    Name = p.Name,
                    ColorIndex = p.ColorIndex,
                    Withdrawn = p.Withdrawn,
                    Loadout = CosmeticCatalog.LoadoutOf(p),
                });
            }
            Origin = origin;
            TurnIndex = turnIndex >= 0 && turnIndex < _players.Count ? turnIndex : -1;
            EditedIndex = Math.Max(0, Math.Min(editIndex, _players.Count - 1));
        }

        public int PlayerCount => _players.Count;
        public PlayerState Player(int index) => _players[index];
        public PlayerState EditedPlayer => _players[EditedIndex];

        public Loadout Saved(int index) => _players[index].Loadout;
        public Loadout Working(int index) => _drafts.TryGetValue(index, out Loadout draft) ? draft : _players[index].Loadout;
        public Loadout Current => Working(EditedIndex);

        public bool HasUnsaved(int index) => _drafts.ContainsKey(index);

        public bool AnyUnsaved => _drafts.Count > 0;

        public void SelectPlayer(int index)
        {
            if (index < 0 || index >= _players.Count) return;
            EditedIndex = index;
        }

        public void CyclePlayer(int delta)
        {
            int n = _players.Count;
            EditedIndex = ((EditedIndex + delta) % n + n) % n;
        }

        /// <summary>Tries an item on the edited player. Returns false for unknown ids or when nothing visible changes.</summary>
        public bool Equip(string itemId)
        {
            if (!CosmeticCatalog.TryGet(itemId, out CosmeticItem item)) return false;
            Loadout current = Current;
            if (current.Get(item.Slot) == itemId) return false;
            Loadout next = current.Clone();
            next.Set(item.Slot, itemId);
            SetDraft(EditedIndex, next);
            return true;
        }

        /// <summary>Puts the edited player back in their identity starter look. Still unsaved until Save.</summary>
        public void Reset()
        {
            Loadout next = CosmeticCatalog.DefaultFor(EditedPlayer.ColorIndex);
            next.Owned = new List<string>(Saved(EditedIndex).Owned);
            SetDraft(EditedIndex, next);
        }

        public int EditedPlayerId => EditedPlayer.Id;

        /// <summary>What Save sends for the edited player only; equipped priced items become owned.</summary>
        public Loadout PendingSave => CosmeticCatalog.WithEquippedOwned(Current);

        /// <summary>Call after the service accepted the save: the committed value becomes the baseline for that player.</summary>
        public void Committed(int index, Loadout committed)
        {
            _players[index].Loadout = committed.Clone();
            _drafts.Remove(index);
        }

        /// <summary>Back: every unsaved value returns to what the service last committed.</summary>
        public void DiscardAll() => _drafts.Clear();

        public ItemState StateOf(string itemId)
        {
            if (!CosmeticCatalog.TryGet(itemId, out CosmeticItem item)) return ItemState.ForSale;
            if (Current.Get(item.Slot) == itemId) return ItemState.Equipped;
            return CosmeticCatalog.IsOwned(Saved(EditedIndex), itemId) ? ItemState.Owned : ItemState.ForSale;
        }

        public int OwnedCount(CosmeticSlot slot)
        {
            int owned = 0;
            foreach (CosmeticItem item in CosmeticCatalog.ForSlot(slot))
            {
                if (CosmeticCatalog.IsOwned(Saved(EditedIndex), item.Id)) owned++;
            }
            return owned;
        }

        private void SetDraft(int index, Loadout next)
        {
            if (next.SameLook(_players[index].Loadout)) _drafts.Remove(index);
            else _drafts[index] = next;
        }
    }
}
