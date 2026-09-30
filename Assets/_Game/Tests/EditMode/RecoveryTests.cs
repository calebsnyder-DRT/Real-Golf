using System;
using System.IO;
using NUnit.Framework;
using Puttshack.Domain;
using Puttshack.Protocol;
using Puttshack.Session;
using Puttshack.Storage;

namespace Puttshack.Tests
{
    /// <summary>Spec section 39: a crash at any point leaves exactly one stroke per accepted input.</summary>
    public sealed class RecoveryTests
    {
        private string _tempDir;

        [TearDown]
        public void Cleanup()
        {
            if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            _tempDir = null;
        }

        private ISessionStore JournalStore()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "puttshack-tests-" + Guid.NewGuid().ToString("N"));
            return new JournalSessionStore(_tempDir, snapshotEvery: 5);
        }

        [Test]
        public void CrashAfterReservation_ResolvesSavedIntent_WithoutNewInput([Values(false, true)] bool onDisk)
        {
            var h = new SessionHarness(onDisk ? JournalStore() : null);
            h.StartRound("Caleb", "Maya");
            h.Service.CrashAfter = CrashPoint.AfterReservation;
            h.Arm();
            h.Clock.Advance(900);
            CabinetEvent evt = h.Emulator.TriggerLane(13);
            h.Pump();
            Assert.IsNotNull(h.LastCrash);
            Assert.AreEqual(CrashPoint.AfterReservation, h.LastCrash.Point);

            ISessionStore store = onDisk ? new JournalSessionStore(_tempDir, snapshotEvery: 5) : h.Store;
            SessionCoordinator reloaded = SessionCoordinator.StartOrResume(h.Course, store, h.Clock);
            Assert.IsTrue(reloaded.Recovered);
            Assert.AreEqual(SessionState.SHOT_RESERVED, reloaded.State);
            reloaded.UpdateCabinetHealth(h.Emulator.Health);
            reloaded.Tick();

            SessionSnapshot snap = reloaded.Snapshot;
            Assert.AreEqual(SessionState.SHOT_RESOLVED, snap.State);
            Assert.AreEqual(1, snap.Shots.Count);
            Assert.AreEqual(ShotStatus.RESOLVED, snap.LastShot.Status);
            Assert.AreEqual(13, snap.LastShot.Intent.Lane);
            Assert.AreEqual(1, snap.ProgressFor(0).Played);

            InputAck again = reloaded.HandleCabinetEvent(evt);
            Assert.IsTrue(again.Duplicate);
            Assert.AreEqual(1, reloaded.Snapshot.Shots.Count);
        }

        [Test]
        public void CrashAfterResolution_RestoresResult_WithOneStroke([Values(false, true)] bool onDisk)
        {
            var h = new SessionHarness(onDisk ? JournalStore() : null);
            h.StartRound("Caleb", "Maya");
            h.Service.CrashAfter = CrashPoint.AfterResolution;
            h.Shoot(10, 0.7);
            Assert.IsNotNull(h.LastCrash);
            string hashBefore = h.Service.Snapshot.LastShot.Result.ResultHash;

            ISessionStore store = onDisk ? new JournalSessionStore(_tempDir, snapshotEvery: 5) : h.Store;
            SessionCoordinator reloaded = SessionCoordinator.StartOrResume(h.Course, store, h.Clock);
            reloaded.UpdateCabinetHealth(h.Emulator.Health);
            reloaded.Tick();

            SessionSnapshot snap = reloaded.Snapshot;
            Assert.AreEqual(SessionState.SHOT_RESOLVED, snap.State);
            Assert.AreEqual(1, snap.Shots.Count);
            Assert.AreEqual(1, snap.ProgressFor(0).Played);
            Assert.AreEqual(hashBefore, snap.LastShot.Result.ResultHash);
        }

        [Test]
        public void CrashWhileArmed_RecoversToSetup_NeverRestoresTheMeter()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Arm();
            h.Restart();
            Assert.AreEqual(SessionState.SHOT_SETUP, h.Service.State);
            Assert.IsNull(h.Snap.Data.Arm);
            Assert.AreEqual(0, h.Snap.Shots.Count);
        }

        [Test]
        public void CrashDuringPresentation_ReplaysCommittedResult()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Shoot(10, 0.6);
            h.Expect(CommandType.PresentationBegin);
            h.Restart();
            Assert.AreEqual(SessionState.SHOT_RESOLVED, h.Service.State);
            Assert.AreEqual(1, h.Snap.Shots.Count);
        }

        [Test]
        public void TornJournalTail_IsIgnored_OnDisk()
        {
            ISessionStore store = JournalStore();
            var h = new SessionHarness(store);
            h.StartRound("Caleb");
            h.Shoot(10, 0.5);
            long revision = h.Service.Revision;

            string journal = Directory.GetFiles(_tempDir, "session-*.jsonl")[0];
            File.AppendAllText(journal, "{\"SessionId\":1,\"Revision\":99,\"Ki");

            var reloadedStore = new JournalSessionStore(_tempDir, snapshotEvery: 5);
            SessionCoordinator reloaded = SessionCoordinator.StartOrResume(h.Course, reloadedStore, h.Clock);
            Assert.AreEqual(revision, reloaded.Revision);
            Assert.AreEqual(SessionState.SHOT_RESOLVED, reloaded.State);
            Assert.AreEqual(1, reloaded.Snapshot.Shots.Count);
            Assert.AreEqual(1, reloaded.Snapshot.ProgressFor(0).Played);
        }

        [Test]
        public void TornWrite_InMemory_LeavesStateUnchanged()
        {
            var store = new InMemorySessionStore();
            var h = new SessionHarness(store);
            h.StartRound("Caleb");
            long revision = h.Service.Revision;
            store.TearNextCommit = true;
            Assert.IsFalse(h.Send(CommandType.Arm).Accepted);
            Assert.AreEqual(revision, h.Service.Revision);
            Assert.AreEqual(SessionState.SHOT_SETUP, h.Service.State);

            h.Restart();
            Assert.AreEqual(revision, h.Service.Revision);
            h.Arm();
        }

        [Test]
        public void CompletedRound_StartsFreshSessionOnReload()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Expect(CommandType.EndRound);
            int oldSession = h.Snap.Data.SessionId;
            h.Restart();
            Assert.AreEqual(SessionState.ATTRACT, h.Service.State);
            Assert.AreNotEqual(oldSession, h.Snap.Data.SessionId);
        }
    }
}
