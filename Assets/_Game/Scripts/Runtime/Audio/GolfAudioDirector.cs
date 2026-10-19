using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Session;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Puttshack.Runtime.Audio
{
    /// <summary>
    /// The game's audio layer. Started at runtime for any scene with a <see cref="GameHost"/> (Main.unity is rebuilt from
    /// code, so nothing is placed in it), it then persists and rebinds to whichever host is current. Like the HUD it owns
    /// no rules: every frame it reads the committed snapshot and the presentation objects, and plays from
    /// <see cref="GolfSoundBank"/>. Ambience and crowd beds are loops eased toward context levels; everything else is a
    /// pooled one-shot. Context ducks (flyover, address, putting, indoors, pause) are applied in code on top of the
    /// optional GolfMixer, so the game sounds the same with or without it.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed partial class GolfAudioDirector : MonoBehaviour
    {
        private const int PoolSize = 28;
        private const float FullBand = 22000f;
        private static readonly int BusCount = Enum.GetValues(typeof(AudioBus)).Length;

        public static GolfAudioDirector Instance { get; private set; }
        /// <summary>Set before a scene loads to keep the audio layer out of it.</summary>
        public static bool Disabled;
        private static bool _sceneHooked;

        private GolfSoundBank _bank;
        private GameHost _host;
        private float _nextHostSearch;
        private AudioMixer _mixer;
        private AudioMixerGroup _masterGroup;
        private readonly Dictionary<AudioBus, AudioMixerGroup> _groups = new Dictionary<AudioBus, AudioMixerGroup>();
        private VoicePool _pool;
        private readonly List<LoopVoice> _loops = new List<LoopVoice>();
        private readonly float[] _busGain = new float[BusCount];
        private readonly float[] _busDb = new float[BusCount];
        private readonly float[] _busTargetDb = new float[BusCount];
        private readonly float[] _userVolume = new float[BusCount];
        private readonly bool[] _userInMixer = new bool[BusCount];
        private float _masterVolume = 1f;
        private bool _masterInMixer;
        private readonly Dictionary<GolfSound, float> _lastPlayed = new Dictionary<GolfSound, float>();
        private readonly List<KeyValuePair<float, Action>> _scheduled = new List<KeyValuePair<float, Action>>();
        private AudioListener _listener;
        private float _nextListenerSearch;
        private float _cutoffHz = FullBand;

        private bool _interior, _paused, _flyover, _address, _putting, _cart, _celebration, _inRound;

        public GolfSoundBank Bank => _bank;
        public Vector3 ListenerPosition => _listener != null ? _listener.transform.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        // ---------------------------------------------------------------- Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            _sceneHooked = false;
            GolfAudioEvents.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!_sceneHooked)
            {
                SceneManager.sceneLoaded += (scene, mode) => Ensure();
                _sceneHooked = true;
            }
            Ensure();
        }

        /// <summary>Creates the persistent audio layer the first time a scene with a <see cref="GameHost"/> is loaded.</summary>
        public static GolfAudioDirector Ensure()
        {
            if (Disabled || Instance != null) return Instance;
            if (FindAnyObjectByType<GameHost>() == null) return null;
            var go = new GameObject("Golf Audio");
            DontDestroyOnLoad(go);
            return go.AddComponent<GolfAudioDirector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _bank = GolfSoundBank.LoadForRuntime();
            for (int i = 0; i < BusCount; i++)
            {
                _userVolume[i] = 1f;
                _busGain[i] = 1f;
            }
            BindMixer();
            _pool = new VoicePool(transform, PoolSize, Group);
            BuildLoops();
            Preload();
        }

        private void OnEnable()
        {
            GolfAudioEvents.ShotPlaybackStarted += OnShotPlaybackStarted;
            Shop.ShopInput.ActionPolled += OnMenuAction;
        }

        private void OnDisable()
        {
            GolfAudioEvents.ShotPlaybackStarted -= OnShotPlaybackStarted;
            Shop.ShopInput.ActionPolled -= OnMenuAction;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BindMixer()
        {
            _mixer = _bank.Mixer;
            if (_mixer == null) return;
            AudioMixerGroup[] masters = _mixer.FindMatchingGroups("Master");
            _masterGroup = masters.Length > 0 ? masters[0] : null;
            foreach (AudioBus bus in (AudioBus[])Enum.GetValues(typeof(AudioBus)))
            {
                foreach (AudioMixerGroup g in _mixer.FindMatchingGroups(bus.ToString()))
                {
                    if (g.name != bus.ToString()) continue;
                    _groups[bus] = g;
                    break;
                }
            }
        }

        private AudioMixerGroup Group(AudioBus bus) => _groups.TryGetValue(bus, out AudioMixerGroup g) ? g : _masterGroup;

        private void Preload()
        {
            foreach (SoundCue cue in _bank.Cues)
            {
                if (cue?.Clips == null) continue;
                foreach (AudioClip clip in cue.Clips)
                {
                    if (clip != null && clip.loadType != AudioClipLoadType.Streaming && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                }
            }
        }

        private LoopVoice Loop(GolfSound id, bool lowPass)
        {
            var voice = new LoopVoice(transform, id.ToString(), lowPass);
            SoundCue cue = _bank.Get(id);
            if (cue != null) voice.Bind(cue, Group(cue.Bus));
            _loops.Add(voice);
            return voice;
        }

        // ---------------------------------------------------------------- Frame

        private void LateUpdate()
        {
            if (_pool == null) return;
            float dt = UnityEngine.Time.unscaledDeltaTime;
            FindListener();
            if (!BindHost())
            {
                foreach (LoopVoice loop in _loops) loop.Target = 0f;
                TickLoops(dt);
                _pool.Tick(_busGain);
                return;
            }

            SessionSnapshot snap = _host.Coordinator.Snapshot;
            ReadContext(snap);
            UpdateMix(dt);
            RunScheduled();
            UpdateShot(snap, dt);
            UpdateFlow(snap);
            UpdateUi(snap);
            UpdateAmbience(snap, dt);
            UpdateMusic();
            TickLoops(dt);
            _pool.Tick(_busGain);
        }

        private bool BindHost()
        {
            if (_host == null && UnityEngine.Time.unscaledTime >= _nextHostSearch)
            {
                _nextHostSearch = UnityEngine.Time.unscaledTime + 1f;
                _host = FindAnyObjectByType<GameHost>();
                if (_host != null) ResetTracking();
            }
            return _host != null && _host.Initialized;
        }

        private void FindListener()
        {
            if ((_listener != null && _listener.isActiveAndEnabled) || UnityEngine.Time.unscaledTime < _nextListenerSearch) return;
            _nextListenerSearch = UnityEngine.Time.unscaledTime + 0.5f;
            _listener = FindAnyObjectByType<AudioListener>();
        }

        private void ResetTracking()
        {
            EndShot(react: false);
            _trackedShotId = -1;
            _searchedShotId = -1;
            _flowState = (SessionState)(-1);
            _clubPlayer = -1;
            _clubId = null;
            _scheduled.Clear();
            _cartDrive = null;
            _pool.StopAll();
        }

        private void ReadContext(SessionSnapshot snap)
        {
            SessionState state = snap.EffectiveState;
            _interior = _host.ShopOpen || _host.SetupOpen;
            _paused = snap.State == SessionState.PAUSED || snap.State == SessionState.FAULT;
            _inRound = snap.Data.HoleCount > 0 && state >= SessionState.HOLE_LOAD && state <= SessionState.ROUND_COMPLETE;
            _flyover = state == SessionState.BRIEFING && _host.Flow != null && _host.Flow.OwnsCamera;
            _address = state == SessionState.SHOT_SETUP || state == SessionState.ARMING || state == SessionState.SHOT_ARMED ||
                       state == SessionState.SHOT_RESERVED || state == SessionState.SHOT_RESOLVED;
            string club = snap.Data.Arm?.ClubId ?? snap.Data.Setup.ClubId;
            _putting = (_address && club == ClubCatalog.PutterId) || (_shotActive && _shotIsPutt);
            _celebration = state == SessionState.ROUND_COMPLETE;
            _cart = state == SessionState.HOLE_LOAD && CartActive();
        }

        private void UpdateMix(float dt)
        {
            MixSettings m = _bank.Mix;
            float paused = _paused ? m.PausedDb : 0f;
            _busTargetDb[(int)AudioBus.Music] = m.MusicDb + paused;
            _busTargetDb[(int)AudioBus.Ambience] = m.AmbienceDb + paused +
                                                   (_interior ? m.InteriorAmbienceDb : 0f) +
                                                   (_flyover ? m.FlyoverAmbienceDb : 0f) +
                                                   (_putting ? m.PuttAmbienceDb : 0f) +
                                                   (_cart ? m.CartAmbienceDb : 0f) +
                                                   (_celebration ? m.CelebrationAmbienceDb : 0f);
            _busTargetDb[(int)AudioBus.SFX] = m.SfxDb + paused;
            _busTargetDb[(int)AudioBus.UI] = m.UiDb;
            _busTargetDb[(int)AudioBus.Crowd] = m.CrowdDb + paused + (_interior ? m.InteriorCrowdDb : 0f) + (_flyover ? m.FlyoverCrowdDb : 0f);

            float k = Ease(dt, m.DuckSeconds);
            float master = _masterInMixer ? 1f : _masterVolume;
            for (int i = 0; i < BusCount; i++)
            {
                _busDb[i] += (_busTargetDb[i] - _busDb[i]) * k;
                _busGain[i] = DbToLinear(_busDb[i]) * (_userInMixer[i] ? 1f : _userVolume[i]) * master;
            }

            float cutoff = _interior ? m.InteriorLowpassHz : _paused ? m.PausedLowpassHz : FullBand;
            _cutoffHz = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(_cutoffHz, 10f)), Mathf.Log(Mathf.Max(cutoff, 10f)), k));
        }

        private void TickLoops(float dt)
        {
            foreach (LoopVoice loop in _loops)
            {
                if (loop.Cue == null) continue;
                loop.Tick(dt, _busGain[(int)loop.Cue.Bus], loop.LowPass != null ? _cutoffHz : FullBand);
            }
        }

        // ---------------------------------------------------------------- Playback

        /// <summary>Plays a one-shot cue (honouring its cooldown) at a position; returns the voice, or null if it did not play.</summary>
        internal AudioSource PlayCue(GolfSound id, Vector3 position, float volumeScale = 1f, float pitchScale = 1f, Transform follow = null)
        {
            if (_bank == null || _pool == null) return null;
            SoundCue cue = _bank.Get(id);
            if (cue == null || volumeScale <= 0f) return null;
            float now = UnityEngine.Time.unscaledTime;
            if (_lastPlayed.TryGetValue(id, out float last) && now >= last && now - last < cue.Cooldown) return null;
            AudioClip clip = cue.Pick();
            if (clip == null) return null;
            _lastPlayed[id] = now;
            return _pool.Play(cue, clip, position, cue.RandomVolume(volumeScale), cue.RandomPitch(pitchScale), follow, _busGain);
        }

        private void PlayFlat(GolfSound id, float volume = 1f) => PlayCue(id, ListenerPosition, volume);

        private void Schedule(float delay, Action action) => _scheduled.Add(new KeyValuePair<float, Action>(UnityEngine.Time.time + delay, action));

        private void RunScheduled()
        {
            if (_scheduled.Count == 0) return;
            float now = UnityEngine.Time.time;
            for (int i = 0; i < _scheduled.Count; i++)
            {
                if (_scheduled[i].Key > now) continue;
                Action act = _scheduled[i].Value;
                _scheduled.RemoveAt(i--);
                act();
            }
        }

        public void SetBusVolume(AudioBus bus, float linear)
        {
            int i = (int)bus;
            _userVolume[i] = Mathf.Clamp01(linear);
            _userInMixer[i] = _mixer != null && _mixer.SetFloat(bus + "Volume", LinearToDb(_userVolume[i]));
        }

        public void SetMasterVolume(float linear)
        {
            _masterVolume = Mathf.Clamp01(linear);
            _masterInMixer = _mixer != null && _mixer.SetFloat("MasterVolume", LinearToDb(_masterVolume));
        }

        private static float Ease(float dt, float seconds) => seconds <= 0f ? 1f : 1f - Mathf.Exp(-dt / seconds);

        private static float DbToLinear(float db) => db <= -79f ? 0f : Mathf.Pow(10f, db / 20f);

        private static float LinearToDb(float linear) => linear <= 0.0001f ? -80f : 20f * Mathf.Log10(linear);
    }
}
