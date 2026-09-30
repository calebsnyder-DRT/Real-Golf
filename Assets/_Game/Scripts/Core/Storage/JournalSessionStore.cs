using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Puttshack.Storage
{
    /// <summary>
    /// Single-writer append-only JSON-lines journal per session, flushed to disk on every commit, plus a periodic
    /// snapshot that only speeds up loading. SQLite can replace this behind <see cref="ISessionStore"/> later.
    /// </summary>
    public sealed class JournalSessionStore : ISessionStore
    {
        private const string Prefix = "session-";
        private const string JournalExt = ".jsonl";
        private const string SnapshotExt = ".snapshot.json";

        private readonly string _directory;
        private readonly int _snapshotEvery;
        private readonly Dictionary<int, long> _seq = new Dictionary<int, long>();
        private readonly Dictionary<int, StoredSession> _mirror = new Dictionary<int, StoredSession>();

        public JournalSessionStore(string directory, int snapshotEvery = 100)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _snapshotEvery = Math.Max(10, snapshotEvery);
            Directory.CreateDirectory(_directory);
        }

        public string DirectoryPath => _directory;

        public int NextSessionId()
        {
            int max = 0;
            foreach (int id in ExistingSessionIds()) max = Math.Max(max, id);
            return max + 1;
        }

        public void Commit(JournalEntry entry)
        {
            string path = JournalPath(entry.SessionId);
            if (!_seq.TryGetValue(entry.SessionId, out long seq))
            {
                StoredSession existing = File.Exists(path) ? Load(entry.SessionId) : null;
                seq = existing?.LastSeq ?? 0;
                _seq[entry.SessionId] = seq;
                if (existing != null) _mirror[entry.SessionId] = existing;
            }

            entry.Seq = seq + 1;
            entry.WallUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            byte[] bytes = Encoding.UTF8.GetBytes(JournalSerializer.Serialize(entry) + "\n");

            try
            {
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new StoreException($"Journal append failed for session {entry.SessionId}.", ex);
            }

            _seq[entry.SessionId] = entry.Seq;
            Fold(entry);
            if (entry.Seq % _snapshotEvery == 0) TryWriteSnapshot(entry.SessionId);
        }

        public StoredSession LoadLatest()
        {
            int latest = 0;
            foreach (int id in ExistingSessionIds()) latest = Math.Max(latest, id);
            if (latest == 0) return null;
            StoredSession stored = Load(latest);
            if (stored != null)
            {
                _seq[latest] = stored.LastSeq;
                _mirror[latest] = JournalSerializer.Clone(stored);
            }
            return stored;
        }

        private StoredSession Load(int sessionId)
        {
            StoredSession start = null;
            string snapshotPath = SnapshotPath(sessionId);
            if (File.Exists(snapshotPath))
            {
                try
                {
                    start = JournalSerializer.Deserialize<StoredSession>(File.ReadAllText(snapshotPath, Encoding.UTF8));
                }
                catch (Exception)
                {
                    start = null;
                }
            }

            string path = JournalPath(sessionId);
            if (!File.Exists(path)) return start;

            string text = File.ReadAllText(path, Encoding.UTF8);
            var lines = new List<string>(text.Split('\n'));
            bool endsClean = text.Length == 0 || text[text.Length - 1] == '\n';
            if (endsClean && lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);

            StoredSession stored = JournalSerializer.Replay(lines, start, out int valid);
            if (valid < lines.Count || !endsClean) TruncateTornTail(path, lines, valid);
            return stored;
        }

        private static void TruncateTornTail(string path, List<string> lines, int validCount)
        {
            File.Copy(path, path + ".torn-" + DateTime.UtcNow.Ticks, overwrite: false);
            var sb = new StringBuilder();
            for (int i = 0; i < validCount; i++) sb.Append(lines[i]).Append('\n');
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private void Fold(JournalEntry entry)
        {
            if (!_mirror.TryGetValue(entry.SessionId, out StoredSession mirror))
            {
                mirror = new StoredSession();
                _mirror[entry.SessionId] = mirror;
            }
            if (entry.Session != null) mirror.Session = JournalSerializer.Clone(entry.Session);
            if (entry.Shot != null) mirror.Shots[entry.Shot.ShotId] = JournalSerializer.Clone(entry.Shot);
            mirror.LastSeq = entry.Seq;
        }

        private void TryWriteSnapshot(int sessionId)
        {
            if (!_mirror.TryGetValue(sessionId, out StoredSession mirror) || mirror.Session == null) return;
            string target = SnapshotPath(sessionId);
            string temp = target + ".tmp";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JournalSerializer.Serialize(mirror));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
            }
            catch (Exception)
            {
                // The journal remains authoritative; a failed snapshot only costs load time.
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private IEnumerable<int> ExistingSessionIds()
        {
            foreach (string file in Directory.GetFiles(_directory, Prefix + "*" + JournalExt))
            {
                string name = Path.GetFileName(file);
                string digits = name.Substring(Prefix.Length, name.Length - Prefix.Length - JournalExt.Length);
                if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int id)) yield return id;
            }
        }

        private string JournalPath(int sessionId) => Path.Combine(_directory, $"{Prefix}{sessionId:D6}{JournalExt}");
        private string SnapshotPath(int sessionId) => Path.Combine(_directory, $"{Prefix}{sessionId:D6}{SnapshotExt}");
    }
}
