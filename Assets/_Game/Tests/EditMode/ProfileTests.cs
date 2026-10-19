using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Session;

namespace Puttshack.Tests
{
    /// <summary>Player Setup profiles: catalog defaults, and RosterSet keeping, assigning and validating each golfer.</summary>
    public sealed class ProfileTests
    {
        private static SessionHarness Lobby(params string[] names)
        {
            var h = new SessionHarness();
            h.Expect(CommandType.Start);
            h.Expect(CommandType.RosterSet, c => c.Names = new List<string>(names));
            return h;
        }

        private static PlayerProfile ProfileAt(SessionHarness h, int roster) => h.Snap.Data.Players[roster].Profile;

        private static PlayerProfile Look(Gender gender, string skin, string hair, string style) =>
            new PlayerProfile { Gender = gender, Skin = skin, HairColor = hair, HairStyle = style };

        [Test]
        public void Catalog_DefaultsAndNativesAreValid()
        {
            for (int i = 0; i < SessionCoordinator.MaxPlayers; i++)
                Assert.IsNull(ProfileCatalog.Validate(ProfileCatalog.DefaultFor(i)), $"default {i}");
            Assert.AreEqual(Gender.Male, ProfileCatalog.DefaultFor(0).Gender, "Caleb starts as himself.");
            Assert.AreEqual(Gender.Female, ProfileCatalog.DefaultFor(1).Gender, "Maya starts as herself.");
            Assert.IsTrue(ProfileCatalog.IsNativeSkin(ProfileCatalog.DefaultFor(1)) && ProfileCatalog.IsNativeHairColor(ProfileCatalog.DefaultFor(1)));
            Assert.AreEqual(ProfileCatalog.PartLong, ProfileCatalog.HairPart(ProfileCatalog.Native(Gender.Female)));
            Assert.AreEqual(string.Empty, ProfileCatalog.HairPart(Look(Gender.Male, "skin.light", "hair.black", "style.shaved")));
            Assert.AreEqual("hair.silver", ProfileCatalog.Step(ProfileCatalog.HairColors, "hair.black", -1), "Stepping wraps.");
        }

        [Test]
        public void ProfileOf_FillsMissingOrRetiredChoices()
        {
            var player = new PlayerState { ColorIndex = 1 };
            Assert.IsTrue(ProfileCatalog.ProfileOf(player).Same(ProfileCatalog.DefaultFor(1)), "Older journals have no profile.");
            player.Profile = Look(Gender.Male, "skin.retired", "hair.copper", "style.short");
            PlayerProfile p = ProfileCatalog.ProfileOf(player);
            Assert.AreEqual(ProfileCatalog.DefaultFor(1).Skin, p.Skin);
            Assert.AreEqual("hair.copper", p.HairColor, "Known choices survive.");
            Assert.AreEqual(Gender.Male, p.Gender);
        }

        [Test]
        public void RosterSet_AssignsDefaults_AndKeepsProfilesWhenPlayersChange()
        {
            SessionHarness h = Lobby("Caleb", "Maya");
            Assert.IsTrue(ProfileAt(h, 0).Same(ProfileCatalog.DefaultFor(0)));
            Assert.IsTrue(ProfileAt(h, 1).Same(ProfileCatalog.DefaultFor(1)));

            PlayerProfile custom = Look(Gender.Female, "skin.deep", "hair.blonde", "style.shaved");
            h.Expect(CommandType.RosterSet, c =>
            {
                c.Names = new List<string> { "Caleb", "Sam" };
                c.Profiles = new List<PlayerProfile> { null, custom };
            });
            Assert.AreEqual("Sam", h.Snap.Data.Players[1].Name);
            Assert.IsTrue(ProfileAt(h, 1).Same(custom));
            Assert.IsTrue(ProfileAt(h, 0).Same(ProfileCatalog.DefaultFor(0)), "A null entry keeps the slot's golfer.");

            h.Expect(CommandType.RosterSet, c => c.Names = new List<string> { "Caleb", "Sam", "Jordan" });
            Assert.IsTrue(ProfileAt(h, 1).Same(custom), "Adding a player must not reset Sam's look.");
            Assert.IsTrue(ProfileAt(h, 2).Same(ProfileCatalog.DefaultFor(2)));
        }

        [Test]
        public void RosterSet_RejectsBadProfiles()
        {
            SessionHarness h = Lobby("Caleb", "Maya");
            long revision = h.Service.Revision;

            CommandReply mismatched = h.Send(CommandType.RosterSet, c =>
            {
                c.Names = new List<string> { "Caleb", "Maya" };
                c.Profiles = new List<PlayerProfile> { ProfileCatalog.DefaultFor(0) };
            });
            Assert.IsFalse(mismatched.Accepted, "One profile per name.");

            CommandReply unknown = h.Send(CommandType.RosterSet, c =>
            {
                c.Names = new List<string> { "Caleb", "Maya" };
                c.Profiles = new List<PlayerProfile> { null, Look(Gender.Female, "skin.medium", "hair.green", "style.long") };
            });
            Assert.IsFalse(unknown.Accepted, "Unknown hair colours are rejected.");
            Assert.AreEqual(revision, h.Service.Revision);
            Assert.IsTrue(ProfileAt(h, 1).Same(ProfileCatalog.DefaultFor(1)));
        }
    }
}
