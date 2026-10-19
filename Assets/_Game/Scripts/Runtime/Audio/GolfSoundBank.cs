using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Puttshack.Runtime.Audio
{
    [Serializable]
    public sealed class AmbienceSettings
    {
        [Tooltip("Listener distance to the shoreline (m) at which the 2D surf bed is at full level.")]
        public float SurfFullDistance = 30f;
        [Tooltip("Listener distance to the shoreline (m) at which the 2D surf bed reaches SurfFarLevel.")]
        public float SurfFarDistance = 240f;
        [Range(0f, 1f)] public float SurfFarLevel = 0.3f;
        [Tooltip("Positional surf emitters kept on the shoreline around the listener's nearest point.")]
        [Range(1, 5)] public int ShoreEmitters = 3;
        public float ShoreEmitterSpacing = 70f;
        [Tooltip("Height of the breaking surf above the sea surface (m).")]
        public float ShoreEmitterLift = 1.5f;

        [Tooltip("Seconds between gull calls (random in range).")]
        public Vector2 GullInterval = new Vector2(6f, 18f);
        [Tooltip("No gulls when the listener is further than this from the shoreline (m).")]
        public float GullMaxShoreDistance = 170f;

        [Range(0f, 1f)] public float WindBase = 0.45f;
        [Tooltip("Extra wind level per m/s of the hole's wind.")]
        public float WindPerMps = 0.07f;
        [Tooltip("Extra wind level with the camera 60 m above the tee (flyover).")]
        public float WindHeightBoost = 0.35f;

        [Range(0f, 1f)] public float BirdsInland = 1f;
        [Range(0f, 1f)] public float BirdsAtCoast = 0.35f;

        [Tooltip("Gallery murmur level by the green and far from it.")]
        [Range(0f, 1f)] public float MurmurNearGreen = 1f;
        [Range(0f, 1f)] public float MurmurFar = 0.45f;
    }

    /// <summary>Context ducks in dB, applied in code on top of the mixer so they work with or without GolfMixer.</summary>
    [Serializable]
    public sealed class MixSettings
    {
        [Header("Bus trims (dB)")]
        public float MusicDb;
        public float AmbienceDb;
        public float SfxDb;
        public float UiDb;
        public float CrowdDb;

        [Tooltip("Time constant of every duck and filter sweep (s).")]
        public float DuckSeconds = 0.5f;

        [Header("Hole flyover")]
        public float FlyoverAmbienceDb = -3f;
        public float FlyoverCrowdDb = -8f;

        [Header("Golfer over the ball (quiet please)")]
        public float AddressMurmurDb = -8f;

        [Header("Putting: setup until the putt comes to rest")]
        public float PuttMurmurDb = -18f;
        public float PuttWindDb = -6f;
        public float PuttAmbienceDb = -4f;

        [Header("Pro Shop / Player Setup (indoors)")]
        public float InteriorAmbienceDb = -15f;
        public float InteriorCrowdDb = -40f;
        public float InteriorLowpassHz = 800f;

        [Header("Pause / fault overlay")]
        public float PausedDb = -10f;
        public float PausedLowpassHz = 1500f;

        [Header("Other")]
        public float CartAmbienceDb = -2f;
        public float CelebrationAmbienceDb = -4f;
    }

    [Serializable]
    public sealed class ImpactSettings
    {
        [Tooltip("Ball speed into the ground (m/s) for a full-level landing; below LandSpeedMin a bounce is silent.")]
        public float LandSpeedForFull = 22f;
        public float LandSpeedMin = 0.6f;
        [Range(0f, 1f)] public float LandQuietestLevel = 0.25f;
        [Tooltip("Ball speed (m/s) for a full-level roll loop.")]
        public float RollSpeedForFull = 4f;
        public float RollPitchMin = 0.75f;
        public float RollPitchMax = 1.3f;
        [Tooltip("Seconds before impact the swing whoosh starts.")]
        public float WhooshLead = 0.2f;
        [Tooltip("How much stroke power scales the strike level (0 = none).")]
        [Range(0f, 1f)] public float StrikePowerRange = 0.35f;
    }

    [Serializable]
    public sealed class CrowdSettings
    {
        [Tooltip("An approach finishing on the green within this of the cup (m) gets the big applause.")]
        public float CloseApproachM = 3f;
        [Tooltip("An approach finishing on the green within this of the cup (m) gets polite applause.")]
        public float GoodApproachM = 8f;
        [Tooltip("A tee shot of at least this (m) finishing on the fairway gets polite applause.")]
        public float LongDriveM = 230f;
        [Tooltip("A putt stopping within this of the cup (m) without dropping gets an 'ooh'.")]
        public float NearMissM = 0.3f;
        [Tooltip("Holed putts longer than this (m) get the big applause even for par or worse.")]
        public float LongPuttM = 5f;
        [Tooltip("Delay from the ball stopping to the gallery reacting (s).")]
        public float ReactionDelay = 0.25f;
    }

    /// <summary>
    /// Every game sound, data-driven: cues keyed by <see cref="GolfSound"/> plus the tuning the director reads. The asset
    /// lives at Resources/Audio/GolfSoundBank (create it with Puttshack > Audio > Create or Update Sound Bank); without
    /// it the director builds this from <see cref="Preset"/> and the clips under Resources/Audio by filename prefix.
    /// </summary>
    [CreateAssetMenu(menuName = "Puttshack/Audio/Golf Sound Bank", fileName = "GolfSoundBank")]
    public sealed class GolfSoundBank : ScriptableObject
    {
        public const string ResourcePath = "Audio/GolfSoundBank";
        public const string MixerResourcePath = "Audio/GolfMixer";
        public const string ClipRoot = "Audio/";

        [Tooltip("Optional. Groups named after AudioBus under Master; exposed '{Bus}Volume' / 'MasterVolume' parameters.")]
        public AudioMixer Mixer;
        public List<SoundCue> Cues = new List<SoundCue>();
        public AmbienceSettings Ambience = new AmbienceSettings();
        public MixSettings Mix = new MixSettings();
        public ImpactSettings Impacts = new ImpactSettings();
        public CrowdSettings Crowd = new CrowdSettings();

        [NonSerialized] private Dictionary<GolfSound, SoundCue> _lookup;

        public SoundCue Get(GolfSound id)
        {
            if (_lookup == null || _lookup.Count != Cues.Count)
            {
                _lookup = new Dictionary<GolfSound, SoundCue>();
                foreach (SoundCue cue in Cues)
                {
                    if (cue != null) _lookup[cue.Id] = cue;
                }
            }
            return _lookup.TryGetValue(id, out SoundCue found) ? found : null;
        }

        /// <summary>
        /// The bank the game plays from: a copy of the authored asset (so runtime fills never touch it) or the defaults,
        /// with any cue that has no clips filled from Resources by convention.
        /// </summary>
        public static GolfSoundBank LoadForRuntime()
        {
            GolfSoundBank authored = Resources.Load<GolfSoundBank>(ResourcePath);
            GolfSoundBank bank = authored != null ? Instantiate(authored) : CreateInstance<GolfSoundBank>();
            bank.name = authored != null ? authored.name + " (runtime)" : "GolfSoundBank (defaults)";
            bank.EnsureCues(fillEmptyFromResources: true);
            if (bank.Mixer == null) bank.Mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            return bank;
        }

        /// <summary>Adds a preset for every missing <see cref="GolfSound"/>; optionally fills clip-less cues from Resources. Returns cues still silent.</summary>
        public List<GolfSound> EnsureCues(bool fillEmptyFromResources)
        {
            var silent = new List<GolfSound>();
            foreach (GolfSound id in (GolfSound[])Enum.GetValues(typeof(GolfSound)))
            {
                SoundCue cue = Get(id);
                if (cue == null)
                {
                    cue = Preset(id);
                    Cues.Add(cue);
                    _lookup = null;
                }
                if (fillEmptyFromResources && !cue.HasClips) cue.Clips = LoadConventionClips(id);
                if (!cue.HasClips) silent.Add(id);
            }
            return silent;
        }

        /// <summary>Resources path prefix of a cue's clips: every clip in the folder whose name starts with "{prefix}_".</summary>
        public static string ResourceKey(GolfSound id)
        {
            switch (id)
            {
                case GolfSound.SurfBed: return "Ambience/surf_bed";
                case GolfSound.SurfShore: return "Ambience/surf_shore";
                case GolfSound.Wind: return "Ambience/wind";
                case GolfSound.Birds: return "Ambience/birds";
                case GolfSound.Gull: return "Ambience/gull";
                case GolfSound.CartHum: return "Ambience/cart_hum";
                case GolfSound.CrowdMurmur: return "Crowd/murmur";
                case GolfSound.ApplauseSmall: return "Crowd/applause_small";
                case GolfSound.ApplauseBig: return "Crowd/applause_big";
                case GolfSound.Cheer: return "Crowd/cheer";
                case GolfSound.Ovation: return "Crowd/ovation";
                case GolfSound.CrowdOoh: return "Crowd/ooh";
                case GolfSound.CrowdAww: return "Crowd/aww";
                case GolfSound.StrikeDriver: return "Ball/strike_driver";
                case GolfSound.StrikeWood: return "Ball/strike_wood";
                case GolfSound.StrikeIron: return "Ball/strike_iron";
                case GolfSound.StrikeWedge: return "Ball/strike_wedge";
                case GolfSound.StrikePutter: return "Ball/strike_putter";
                case GolfSound.StrikeSand: return "Ball/strike_sand";
                case GolfSound.StrikeRough: return "Ball/strike_rough";
                case GolfSound.Whoosh: return "Ball/whoosh";
                case GolfSound.ClubChange: return "Ball/club_change";
                case GolfSound.LandFairway: return "Ball/land_fairway";
                case GolfSound.LandRough: return "Ball/land_rough";
                case GolfSound.LandGreen: return "Ball/land_green";
                case GolfSound.LandBunker: return "Ball/land_bunker";
                case GolfSound.LandCartPath: return "Ball/land_cartpath";
                case GolfSound.Splash: return "Ball/splash";
                case GolfSound.TreeHit: return "Ball/tree_hit";
                case GolfSound.RollGreen: return "Ball/roll_green";
                case GolfSound.RollFairway: return "Ball/roll_fairway";
                case GolfSound.RollCartPath: return "Ball/roll_cartpath";
                case GolfSound.CupDrop: return "Ball/cup_drop";
                case GolfSound.LipOut: return "Ball/lip_out";
                case GolfSound.StingerAce: return "Stingers/stinger_ace";
                case GolfSound.StingerEagle: return "Stingers/stinger_eagle";
                case GolfSound.StingerBirdie: return "Stingers/stinger_birdie";
                case GolfSound.StingerPar: return "Stingers/stinger_par";
                case GolfSound.StingerBogey: return "Stingers/stinger_bogey";
                case GolfSound.StingerDouble: return "Stingers/stinger_double";
                case GolfSound.StingerRound: return "Stingers/stinger_round";
                case GolfSound.UiSelect: return "UI/ui_select";
                case GolfSound.UiConfirm: return "UI/ui_confirm";
                case GolfSound.UiBack: return "UI/ui_back";
                case GolfSound.UiTitle: return "UI/ui_title";
                case GolfSound.MusicMenu: return "Music/music_menu";
                case GolfSound.MusicRound: return "Music/music_round";
                default: return null;
            }
        }

        public static AudioClip[] LoadConventionClips(GolfSound id)
        {
            string key = ResourceKey(id);
            if (key == null) return new AudioClip[0];
            int slash = key.LastIndexOf('/');
            string folder = ClipRoot + key.Substring(0, slash);
            string prefix = key.Substring(slash + 1) + "_";
            var clips = new List<AudioClip>();
            foreach (AudioClip clip in Resources.LoadAll<AudioClip>(folder))
            {
                if (clip.name.StartsWith(prefix, StringComparison.Ordinal)) clips.Add(clip);
            }
            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips.ToArray();
        }

        /// <summary>Default routing and levels per cue; the asset's values win once it exists.</summary>
        public static SoundCue Preset(GolfSound id)
        {
            switch (id)
            {
                case GolfSound.SurfBed: return Bed(id, AudioBus.Ambience, 0.6f);
                case GolfSound.SurfShore: return Positional(id, AudioBus.Ambience, 1f, 25f, 320f, loop: true);
                case GolfSound.Wind: return Bed(id, AudioBus.Ambience, 0.38f);
                case GolfSound.Birds: return Bed(id, AudioBus.Ambience, 0.22f);
                case GolfSound.Gull: return Jitter(Positional(id, AudioBus.Ambience, 0.55f, 18f, 280f), 0.15f, 0.1f, cooldown: 0.8f);
                case GolfSound.CartHum: return Positional(id, AudioBus.SFX, 0.4f, 3f, 60f, loop: true);

                case GolfSound.CrowdMurmur: return Bed(id, AudioBus.Crowd, 0.22f);
                case GolfSound.ApplauseSmall: return Flat(id, AudioBus.Crowd, 0.5f, cooldown: 1.5f);
                case GolfSound.ApplauseBig: return Flat(id, AudioBus.Crowd, 0.7f, cooldown: 1.5f);
                case GolfSound.Cheer: return Flat(id, AudioBus.Crowd, 0.65f, cooldown: 1.5f);
                case GolfSound.Ovation: return Flat(id, AudioBus.Crowd, 0.75f, cooldown: 3f);
                case GolfSound.CrowdOoh: return Flat(id, AudioBus.Crowd, 0.55f, cooldown: 1.5f);
                case GolfSound.CrowdAww: return Flat(id, AudioBus.Crowd, 0.5f, cooldown: 1.5f);

                case GolfSound.StrikeDriver: return Strike(id, 1f);
                case GolfSound.StrikeWood: return Strike(id, 0.95f);
                case GolfSound.StrikeIron: return Strike(id, 0.9f);
                case GolfSound.StrikeWedge: return Strike(id, 0.85f);
                case GolfSound.StrikePutter: return Strike(id, 0.65f);
                case GolfSound.StrikeSand: return Strike(id, 0.8f);
                case GolfSound.StrikeRough: return Strike(id, 0.55f);
                case GolfSound.Whoosh: return Jitter(Strike(id, 0.5f), 0.1f, 0.08f, cooldown: 0.2f);
                case GolfSound.ClubChange: return Flat(id, AudioBus.SFX, 0.4f, cooldown: 0.08f);

                case GolfSound.LandFairway: return Landing(id, 0.8f);
                case GolfSound.LandRough: return Landing(id, 0.7f);
                case GolfSound.LandGreen: return Landing(id, 0.75f);
                case GolfSound.LandBunker: return Landing(id, 0.85f);
                case GolfSound.LandCartPath: return Landing(id, 0.85f);
                case GolfSound.Splash: return Landing(id, 1f);
                case GolfSound.TreeHit: return Landing(id, 0.9f);

                case GolfSound.RollGreen: return Positional(id, AudioBus.SFX, 0.55f, 2f, 45f, loop: true);
                case GolfSound.RollFairway: return Positional(id, AudioBus.SFX, 0.5f, 2f, 45f, loop: true);
                case GolfSound.RollCartPath: return Positional(id, AudioBus.SFX, 0.55f, 2f, 45f, loop: true);
                case GolfSound.CupDrop: return Jitter(Positional(id, AudioBus.SFX, 1f, 2.5f, 70f), 0.05f, 0.03f, cooldown: 0.5f, spatial: 0.75f);
                case GolfSound.LipOut: return Jitter(Positional(id, AudioBus.SFX, 0.9f, 2.5f, 70f), 0.05f, 0.04f, cooldown: 0.3f, spatial: 0.75f);

                case GolfSound.StingerAce:
                case GolfSound.StingerEagle:
                case GolfSound.StingerBirdie:
                case GolfSound.StingerPar:
                case GolfSound.StingerBogey:
                case GolfSound.StingerDouble:
                case GolfSound.StingerRound:
                    return Jitter(Flat(id, AudioBus.Music, 0.6f, cooldown: 1f), 0f, 0f, cooldown: 1f);

                case GolfSound.UiSelect:
                case GolfSound.UiConfirm:
                case GolfSound.UiBack:
                case GolfSound.UiTitle:
                    return Jitter(Flat(id, AudioBus.UI, 0.5f, cooldown: 0.05f), 0.03f, 0.02f, cooldown: 0.05f);

                // Music sits well under the course: the round bed is a hint beneath surf and wind, not a soundtrack.
                case GolfSound.MusicMenu: return Bed(id, AudioBus.Music, 0.3f);
                case GolfSound.MusicRound: return Bed(id, AudioBus.Music, 0.16f);

                default:
                    return Flat(id, AudioBus.SFX, 1f, cooldown: 0.05f);
            }
        }

        private static SoundCue Bed(GolfSound id, AudioBus bus, float volume) => new SoundCue
        {
            Id = id, Bus = bus, Volume = volume, Loop = true, SpatialBlend = 0f, VolumeJitter = 0f, PitchJitter = 0f, Cooldown = 0f, Priority = 32,
        };

        private static SoundCue Flat(GolfSound id, AudioBus bus, float volume, float cooldown) => new SoundCue
        {
            Id = id, Bus = bus, Volume = volume, SpatialBlend = 0f, Cooldown = cooldown, Priority = 64,
        };

        private static SoundCue Positional(GolfSound id, AudioBus bus, float volume, float min, float max, bool loop = false) => new SoundCue
        {
            Id = id, Bus = bus, Volume = volume, SpatialBlend = 1f, MinDistance = min, MaxDistance = max, Loop = loop,
            VolumeJitter = loop ? 0f : 0.08f, PitchJitter = loop ? 0f : 0.04f, Cooldown = loop ? 0f : 0.03f, Priority = loop ? 48 : 128,
        };

        private static SoundCue Strike(GolfSound id, float volume) => new SoundCue
        {
            Id = id, Bus = AudioBus.SFX, Volume = volume, SpatialBlend = 0.55f, MinDistance = 5f, MaxDistance = 90f,
            VolumeJitter = 0.05f, PitchJitter = 0.03f, Cooldown = 0.1f, Priority = 16,
        };

        private static SoundCue Landing(GolfSound id, float volume) => new SoundCue
        {
            Id = id, Bus = AudioBus.SFX, Volume = volume, SpatialBlend = 1f, MinDistance = 4f, MaxDistance = 120f,
            VolumeJitter = 0.1f, PitchJitter = 0.06f, Cooldown = 0.04f, Priority = 96,
        };

        private static SoundCue Jitter(SoundCue cue, float volume, float pitch, float cooldown, float spatial = -1f)
        {
            cue.VolumeJitter = volume;
            cue.PitchJitter = pitch;
            cue.Cooldown = cooldown;
            if (spatial >= 0f) cue.SpatialBlend = spatial;
            return cue;
        }
    }
}
