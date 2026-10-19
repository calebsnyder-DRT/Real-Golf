using System;
using System.Collections.Generic;
using System.IO;
using Puttshack.Domain;
using Puttshack.Storage;
using UnityEngine;

namespace Puttshack.Runtime
{
    /// <summary>
    /// Last roster's names, golfers and saved looks, kept outside the session journal so a new session (after a round or a
    /// relaunch) opens the Pro Shop with the same players dressed as they left. A null path keeps it in memory only.
    /// </summary>
    public sealed class RosterMemory
    {
        [Serializable]
        public sealed class Entry
        {
            public string Name;
            public Loadout Loadout;
            public PlayerProfile Profile;
        }

        private readonly string _path;

        public IReadOnlyList<Entry> Players { get; private set; } = Array.Empty<Entry>();

        public RosterMemory(string path)
        {
            _path = path;
            if (_path == null || !File.Exists(_path)) return;
            try
            {
                Players = JournalSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? new List<Entry>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Puttshack] Ignoring unreadable roster memory {_path}: {ex.Message}");
            }
        }

        public void Remember(IReadOnlyList<PlayerState> players)
        {
            var entries = new List<Entry>(players.Count);
            foreach (PlayerState p in players) entries.Add(new Entry { Name = p.Name, Loadout = p.Loadout?.Clone(), Profile = p.Profile?.Clone() });
            Players = entries;
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, JournalSerializer.Serialize(entries));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Puttshack] Could not save roster memory {_path}: {ex.Message}");
            }
        }
    }
}
