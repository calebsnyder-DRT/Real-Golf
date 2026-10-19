using System;
using Puttshack.Domain;
using Puttshack.Runtime.Views;
using UnityEngine;

namespace Puttshack.Runtime.Audio
{
    /// <summary>
    /// Presentation hooks the audio layer listens to. Raised by the code that already owns the moment (one line each), so
    /// audio never reaches into gameplay state it would otherwise have to guess.
    /// </summary>
    public static class GolfAudioEvents
    {
        /// <summary>
        /// A committed shot started playing on its ball. <c>launchDelay</c>: seconds of presentation time (scaled by
        /// <see cref="GameHost.PlaybackSpeed"/>) until the clubhead meets the ball; 0 when there is no swing.
        /// </summary>
        public static event Action<ShotRecord, BallView, float> ShotPlaybackStarted;

        public static void RaiseShotPlaybackStarted(ShotRecord shot, BallView ball, float launchDelay) =>
            ShotPlaybackStarted?.Invoke(shot, ball, launchDelay);

        internal static void Reset() => ShotPlaybackStarted = null;
    }

    /// <summary>Entry points for other systems (menus, HUD, cutscenes). Silent when the director is not running.</summary>
    public static class GolfAudio
    {
        /// <summary>Plays a cue 2D, or at <paramref name="position"/> when the cue is positional.</summary>
        public static void Play(GolfSound id, Vector3? position = null, float volume = 1f)
        {
            GolfAudioDirector d = GolfAudioDirector.Instance;
            if (d != null) d.PlayCue(id, position ?? d.ListenerPosition, volume);
        }

        public static void PlayUi(GolfSound id) => Play(id);

        /// <summary>Player-facing level (0-1) for a bus: through the mixer's exposed "{Bus}Volume" when present, else in code.</summary>
        public static void SetBusVolume(AudioBus bus, float linear)
        {
            GolfAudioDirector d = GolfAudioDirector.Instance;
            if (d != null) d.SetBusVolume(bus, linear);
        }

        public static void SetMasterVolume(float linear)
        {
            GolfAudioDirector d = GolfAudioDirector.Instance;
            if (d != null) d.SetMasterVolume(linear);
        }
    }
}
