using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Puttshack.Domain;
using Puttshack.Session;

namespace Puttshack.Storage
{
    /// <summary>
    /// One durable transaction. A reservation carries the RESERVED shot and the session in the same entry, so they
    /// commit or fail together (spec section 38, transactions A and B).
    /// </summary>
    [Serializable]
    public sealed class JournalEntry
    {
        public long Seq;
        public int SessionId;
        public long Revision;
        public string Kind;
        public double MonotonicMs;
        public string WallUtc;
        public SessionData Session;
        public ShotRecord Shot;
    }

    public sealed class StoredSession
    {
        public SessionData Session;
        public Dictionary<int, ShotRecord> Shots = new Dictionary<int, ShotRecord>();
        public long LastSeq;
    }

    public sealed class StoreException : Exception
    {
        public StoreException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>Raised when a committed record cannot be read. The original file is preserved for staff diagnosis.</summary>
    public sealed class StoreCorruptException : Exception
    {
        public StoreCorruptException(string message, Exception inner = null) : base(message, inner) { }
    }

    public interface ISessionStore
    {
        int NextSessionId();

        /// <summary>Durably appends one entry. Throws <see cref="StoreException"/> if the entry may not be durable.</summary>
        void Commit(JournalEntry entry);

        /// <summary>Latest session by ID, or null when none exists.</summary>
        StoredSession LoadLatest();
    }

    public static class JournalSerializer
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            FloatFormatHandling = FloatFormatHandling.String,
        };

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);
        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        public static T Clone<T>(T value) => Deserialize<T>(Serialize(value));

        /// <summary>
        /// Folds journal lines into the latest state. A malformed final line is a torn write that never committed and is
        /// reported through <paramref name="validLineCount"/>; a malformed earlier line is corruption.
        /// </summary>
        public static StoredSession Replay(IReadOnlyList<string> lines, StoredSession start, out int validLineCount)
        {
            StoredSession stored = start ?? new StoredSession();
            validLineCount = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    validLineCount++;
                    continue;
                }

                JournalEntry entry;
                try
                {
                    entry = Deserialize<JournalEntry>(line);
                    if (entry == null) throw new JsonException("Empty entry.");
                }
                catch (JsonException ex)
                {
                    if (i == lines.Count - 1) break;
                    throw new StoreCorruptException($"Journal line {i + 1} is unreadable.", ex);
                }

                validLineCount++;
                if (entry.Seq <= stored.LastSeq) continue;
                if (entry.Session != null) stored.Session = entry.Session;
                if (entry.Shot != null) stored.Shots[entry.Shot.ShotId] = entry.Shot;
                stored.LastSeq = entry.Seq;
            }
            return stored.Session == null ? null : stored;
        }
    }

    /// <summary>
    /// Test/dev store that still serializes every entry, so recovery tests exercise the real round trip.
    /// </summary>
    public sealed class InMemorySessionStore : ISessionStore
    {
        private readonly SortedDictionary<int, List<string>> _journals = new SortedDictionary<int, List<string>>();
        private readonly Dictionary<int, long> _seq = new Dictionary<int, long>();
        private readonly HashSet<int> _torn = new HashSet<int>();

        /// <summary>The next commit throws before anything is written.</summary>
        public bool FailNextCommit { get; set; }
        /// <summary>The next commit writes half a line and then throws, like a power cut mid-write.</summary>
        public bool TearNextCommit { get; set; }
        public int CommitCount { get; private set; }

        public int NextSessionId()
        {
            int max = 0;
            foreach (int id in _journals.Keys) max = Math.Max(max, id);
            return max + 1;
        }

        public void Commit(JournalEntry entry)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new StoreException("Simulated storage failure.");
            }

            if (!_journals.TryGetValue(entry.SessionId, out List<string> lines))
            {
                lines = new List<string>();
                _journals[entry.SessionId] = lines;
            }
            if (_torn.Remove(entry.SessionId)) lines.RemoveAt(lines.Count - 1);
            _seq.TryGetValue(entry.SessionId, out long seq);
            entry.Seq = seq + 1;
            string json = JournalSerializer.Serialize(entry);

            if (TearNextCommit)
            {
                TearNextCommit = false;
                lines.Add(json.Substring(0, json.Length / 2));
                _torn.Add(entry.SessionId);
                throw new StoreException("Simulated torn write.");
            }

            lines.Add(json);
            _seq[entry.SessionId] = entry.Seq;
            CommitCount++;
        }

        public StoredSession LoadLatest()
        {
            if (_journals.Count == 0) return null;
            int latest = 0;
            foreach (int id in _journals.Keys) latest = Math.Max(latest, id);
            List<string> lines = _journals[latest];
            StoredSession stored = JournalSerializer.Replay(lines, null, out int valid);
            if (valid < lines.Count) lines.RemoveRange(valid, lines.Count - valid);
            _torn.Remove(latest);
            if (stored != null) _seq[latest] = stored.LastSeq;
            return stored;
        }

        public int LineCount(int sessionId) => _journals.TryGetValue(sessionId, out List<string> lines) ? lines.Count : 0;
    }
}
