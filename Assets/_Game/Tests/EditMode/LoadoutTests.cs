using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Protocol;
using Puttshack.Session;
using Puttshack.Shop;
using Puttshack.Storage;

namespace Puttshack.Tests
{
    /// <summary>Pro Shop loadouts (spec sections 17, 20, 28): Back restores, Save is per player, cosmetics never touch play.</summary>
    public sealed class LoadoutTests
    {
        private static SessionHarness Lobby(params string[] names)
        {
            var h = new SessionHarness();
            h.Expect(CommandType.Start);
            h.Expect(CommandType.RosterSet, c => c.Names = new List<string>(names));
            Assert.AreEqual(SessionState.LOBBY, h.Service.State);
            return h;
        }

        private static LoadoutEditor Shop(SessionHarness h, ShopOrigin origin = ShopOrigin.Lobby, int turn = -1) =>
            new LoadoutEditor(h.Snap.Data.Players, origin, turn);

        private static CommandReply Save(SessionHarness h, LoadoutEditor shop)
        {
            Loadout pending = shop.PendingSave;
            int playerId = shop.EditedPlayerId;
            CommandReply reply = h.Send(CommandType.SetLoadout, c =>
            {
                c.PlayerId = playerId;
                c.Loadout = pending;
            });
            if (reply.Accepted) shop.Committed(shop.EditedIndex, pending);
            return reply;
        }

        private static Loadout Committed(SessionHarness h, int roster) => CosmeticCatalog.LoadoutOf(h.Snap.Data.Players[roster]);

        [Test]
        public void Catalog_DefaultsAndItemsAreValid()
        {
            for (int i = 0; i < SessionCoordinator.MaxPlayers; i++)
                Assert.IsNull(CosmeticCatalog.Validate(CosmeticCatalog.DefaultFor(i)), $"default {i}");
            foreach (CosmeticSlot slot in Loadout.Slots)
                Assert.Greater(CosmeticCatalog.ForSlot(slot).Count, 1, $"{slot} needs choices");
            Assert.AreEqual("top.polo.royal", CosmeticCatalog.DefaultFor(0).Top, "Caleb starts in the blue polo.");
            Assert.AreEqual(CosmeticCatalog.PartPolo, CosmeticCatalog.Get(CosmeticCatalog.DefaultFor(2).Top).Style, "Jordan wears a polo.");
            Assert.IsTrue(CosmeticCatalog.Get(CosmeticCatalog.DefaultFor(0).Bag).Starter, "The starting bag is included.");
            Assert.Greater(CosmeticCatalog.ForSlot(CosmeticSlot.Bag).FindAll(b => !b.Starter).Count, 3, "The shop sells extra bags.");
        }

        [Test]
        public void Roster_StartsWithIdentityDefaults_AndKeepsSavedLooksWhenPlayersAreAdded()
        {
            SessionHarness h = Lobby("Caleb", "Maya");
            Assert.AreEqual(CosmeticCatalog.DefaultFor(1).Top, Committed(h, 1).Top);

            LoadoutEditor shop = Shop(h);
            Assert.IsTrue(shop.Equip("top.polo.sky"));
            Assert.IsTrue(Save(h, shop).Accepted);

            h.Expect(CommandType.RosterSet, c => c.Names = new List<string> { "Caleb", "Maya", "Jordan" });
            Assert.AreEqual("top.polo.sky", Committed(h, 0).Top, "Adding a player must not reset Caleb's saved look.");
            Assert.AreEqual(CosmeticCatalog.DefaultFor(2).Top, Committed(h, 2).Top);
        }

        [Test]
        public void Back_RestoresEveryUnsavedValue()
        {
            SessionHarness h = Lobby("Caleb", "Maya");
            long revision = h.Service.Revision;
            Loadout before = Committed(h, 0);

            LoadoutEditor shop = Shop(h);
            shop.Equip("top.tee.coral");
            shop.Equip("bottom.shorts.navy");
            shop.Equip("bag.cart.harbor");
            shop.SelectPlayer(1);
            shop.Equip("shoes.black.grey");
            Assert.IsTrue(shop.HasUnsaved(0) && shop.HasUnsaved(1));
            Assert.AreEqual("top.tee.coral", shop.Working(0).Top, "The preview shows the unsaved choice.");

            shop.DiscardAll();

            Assert.IsFalse(shop.AnyUnsaved);
            Assert.IsTrue(shop.Working(0).SameLook(before));
            Assert.IsTrue(shop.Working(1).SameLook(Committed(h, 1)));
            Assert.AreEqual(revision, h.Service.Revision, "Browsing and Back never write to the session.");
            Assert.IsTrue(Committed(h, 0).SameLook(before));
        }

        [Test]
        public void Save_AppliesOnlyTheEditedPlayer()
        {
            SessionHarness h = Lobby("Caleb", "Maya", "Jordan");
            Loadout jordanBefore = Committed(h, 2);

            LoadoutEditor shop = Shop(h);
            shop.Equip("top.polo.midnight");
            shop.SelectPlayer(1);
            shop.Equip("top.polo.coral");
            shop.Equip("bag.stand.sunset");

            Assert.IsTrue(Save(h, shop).Accepted);

            Assert.AreEqual("top.polo.coral", Committed(h, 1).Top);
            Assert.AreEqual("bag.stand.sunset", Committed(h, 1).Bag);
            Assert.IsTrue(Committed(h, 1).Owns("bag.stand.sunset"), "A saved priced item becomes owned for the session.");
            Assert.AreEqual(CosmeticCatalog.DefaultFor(0).Top, Committed(h, 0).Top, "Caleb's unsaved change must not be saved with Maya.");
            Assert.IsTrue(Committed(h, 2).SameLook(jordanBefore));
            Assert.IsTrue(shop.HasUnsaved(0), "Caleb's draft stays pending until his own Save or Back.");
            Assert.IsFalse(shop.HasUnsaved(1));

            shop.DiscardAll();
            Assert.AreEqual(CosmeticCatalog.DefaultFor(0).Top, shop.Working(0).Top);
            Assert.AreEqual("top.polo.coral", shop.Working(1).Top, "Back keeps what was already saved.");
        }

        [Test]
        public void Reset_ReturnsToTheIdentityLook_UntilSaved()
        {
            SessionHarness h = Lobby("Caleb");
            LoadoutEditor shop = Shop(h);
            shop.Equip("top.tee.navy");
            Assert.IsTrue(Save(h, shop).Accepted);

            shop.Reset();
            Assert.AreEqual(CosmeticCatalog.DefaultFor(0).Top, shop.Current.Top);
            Assert.AreEqual("top.tee.navy", Committed(h, 0).Top, "Reset is a draft like any other change.");
            Assert.IsTrue(shop.Current.Owns("top.tee.navy"), "Reset keeps items already taken home.");
        }

        [Test]
        public void ItemStates_DistinguishEquippedOwnedAndForSale()
        {
            SessionHarness h = Lobby("Caleb");
            LoadoutEditor shop = Shop(h);
            Assert.AreEqual(ItemState.Equipped, shop.StateOf("top.polo.royal"));
            Assert.AreEqual(ItemState.Owned, shop.StateOf("top.polo.green"));
            Assert.AreEqual(ItemState.ForSale, shop.StateOf("top.polo.sky"));

            shop.Equip("top.polo.sky");
            Assert.AreEqual(ItemState.Equipped, shop.StateOf("top.polo.sky"));
            Assert.AreEqual(ItemState.Owned, shop.StateOf("top.polo.royal"));
        }

        [Test]
        public void SetLoadout_IsGuardedByStateAndCatalog()
        {
            SessionHarness h = Lobby("Caleb", "Maya");
            Loadout good = CosmeticCatalog.DefaultFor(0);
            good.Top = "top.polo.sky";

            Loadout wrongSlot = good.Clone();
            wrongSlot.Top = "bottom.chinos.khaki";
            Assert.AreEqual(RejectReason.INVALID_VALUE, h.Send(CommandType.SetLoadout, c => { c.PlayerId = 1; c.Loadout = wrongSlot; }).Reason);

            Loadout unknown = good.Clone();
            unknown.Bag = "bag.gold.plated";
            Assert.AreEqual(RejectReason.INVALID_VALUE, h.Send(CommandType.SetLoadout, c => { c.PlayerId = 1; c.Loadout = unknown; }).Reason);
            Assert.AreEqual(RejectReason.INVALID_VALUE, h.Send(CommandType.SetLoadout, c => { c.PlayerId = 9; c.Loadout = good; }).Reason);

            h.Expect(CommandType.SettingsConfirm);
            h.Expect(CommandType.HoleReady);
            h.Expect(CommandType.BriefingReady);
            Assert.AreEqual(SessionState.SHOT_SETUP, h.Service.State);
            Assert.AreEqual(RejectReason.WRONG_STATE, h.Send(CommandType.SetLoadout, c => { c.PlayerId = 1; c.Loadout = good; }).Reason,
                "The shop is not reachable mid-turn; pause first.");

            h.Expect(CommandType.Pause);
            h.Expect(CommandType.SetLoadout, c => { c.PlayerId = 2; c.Loadout = good; });
            Assert.AreEqual("top.polo.sky", Committed(h, 1).Top);
            h.Expect(CommandType.Resume);
            Assert.AreEqual(SessionState.SHOT_SETUP, h.Service.State);
        }

        [Test]
        public void PausedShop_EditsAnotherPlayer_WithoutMovingTheTurn()
        {
            SessionHarness h = Lobby("Caleb", "Maya");
            h.Expect(CommandType.SettingsConfirm);
            h.Expect(CommandType.HoleReady);
            h.Expect(CommandType.BriefingReady);
            SessionData before = JournalSerializer.Clone(h.Snap.Data);
            h.Expect(CommandType.Pause);

            var shop = new LoadoutEditor(h.Snap.Data.Players, ShopOrigin.Paused, before.CurrentRosterIndex);
            Assert.AreEqual(0, shop.TurnIndex, "Caleb is on the tee.");
            shop.SelectPlayer(1);
            shop.Equip("top.tee.heather");
            Assert.IsTrue(Save(h, shop).Accepted);
            Assert.AreEqual(0, shop.TurnIndex, "Editing Maya does not make it her turn.");

            h.Expect(CommandType.Resume);
            SessionData after = h.Snap.Data;
            Assert.AreEqual(before.CurrentRosterIndex, after.CurrentRosterIndex);
            Assert.AreEqual(before.TurnId, after.TurnId);
            Assert.AreEqual(ContentHasher.Canonical(before.Setup), ContentHasher.Canonical(after.Setup), "Club and aim are untouched.");
            Assert.AreEqual(ContentHasher.Canonical(before.Progress), ContentHasher.Canonical(after.Progress), "Balls and scores are untouched.");
            Assert.AreEqual("top.tee.heather", Committed(h, 1).Top);
        }

        [Test]
        public void Cosmetics_NeverChangeClubsOrTheShot()
        {
            string Play(bool dressUp)
            {
                SessionHarness h = Lobby("Caleb", "Maya");
                if (dressUp)
                {
                    LoadoutEditor shop = Shop(h);
                    shop.Equip("top.tee.coral");
                    shop.Equip("bottom.shorts.navy");
                    shop.Equip("shoes.black.grey");
                    shop.Equip("bag.tour.champion");
                    Assert.IsTrue(Save(h, shop).Accepted);
                }
                h.Expect(CommandType.SettingsConfirm, c => c.Assistance = AssistanceProfileId.Club);
                h.Expect(CommandType.HoleReady);
                h.Expect(CommandType.BriefingReady);
                Assert.AreEqual(9, ClubCatalog.All.Count, "Every player carries the full bag.");
                string club = h.Snap.Data.Setup.ClubId;
                InputAck ack = h.Shoot(12, 0.8);
                Assert.IsTrue(ack.Accepted);
                ShotRecord shot = h.Snap.LastShot;
                StringAssert.DoesNotContain("Loadout", ContentHasher.Canonical(shot.Intent), "The shot intent carries no cosmetics.");
                return $"{club}|{shot.Intent.ClubId}|{shot.Intent.Power01:R}|{shot.Result.ResultHash}";
            }

            Assert.AreEqual(Play(false), Play(true));
        }

        [Test]
        public void SavedLoadout_SurvivesARestart()
        {
            SessionHarness h = Lobby("Caleb", "Maya");
            LoadoutEditor shop = Shop(h);
            shop.SelectPlayer(1);
            shop.Equip("bag.tour.twilight");
            Assert.IsTrue(Save(h, shop).Accepted);

            h.Restart();
            Assert.AreEqual("bag.tour.twilight", Committed(h, 1).Bag);
        }

        [Test]
        public void MissingOrStaleLoadouts_FallBackToTheIdentityDefault()
        {
            var legacy = new PlayerState { Id = 3, RosterIndex = 2, Name = "Jordan", ColorIndex = 2 };
            Assert.IsTrue(CosmeticCatalog.LoadoutOf(legacy).SameLook(CosmeticCatalog.DefaultFor(2)));

            var stale = new PlayerState { Id = 1, Name = "Caleb", ColorIndex = 0, Loadout = CosmeticCatalog.DefaultFor(0) };
            stale.Loadout.Top = "top.retired.item";
            stale.Loadout.Owned.Add("bag.retired");
            Loadout resolved = CosmeticCatalog.LoadoutOf(stale);
            Assert.AreEqual(CosmeticCatalog.DefaultFor(0).Top, resolved.Top);
            Assert.IsEmpty(resolved.Owned);
        }
    }
}
