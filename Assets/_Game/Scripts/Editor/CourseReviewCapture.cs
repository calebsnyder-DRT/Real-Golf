using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime;
using Puttshack.Session;
using UnityEditor;
using UnityEngine;
using SessionState = Puttshack.Domain.SessionState;

namespace Puttshack.EditorTools
{
    using Time = UnityEngine.Time;

    /// <summary>
    /// Play-mode review run for the course art and the cart transition: from the Pro Shop it starts a one-player round,
    /// photographs each hole's tee from the gameplay camera, taps the ball until the stroke cap ends the hole, then
    /// photographs the cart drive to the next tee. Frame times are logged alongside. Driven from EditorApplication.update
    /// so each step sees real frames; call <see cref="Begin"/> once play mode is running.
    /// </summary>
    public static class CourseReviewCapture
    {
        private const int Width = 1600, Height = 900;

        private static string _dir;
        private static int _maxHoles;
        private static int _hole = -1;
        private static float _stateSince;
        private static SessionState _state;
        private static float _nextDriveShot;
        private static int _driveShot;
        private static bool _teeShot;
        private static readonly List<float> Frames = new List<float>();
        private static readonly StringBuilder Log = new StringBuilder();

        public static bool Running { get; private set; }
        public static string Report => Log.ToString();

        public static void Begin(string directory, int holes)
        {
            _dir = directory;
            _maxHoles = holes;
            Directory.CreateDirectory(_dir);
            Log.Clear();
            _hole = -1;
            _state = (SessionState)(-1);
            Application.runInBackground = true;
            if (!Running) EditorApplication.update += Step;
            Running = true;
            Note($"review into {_dir}");
        }

        /// <summary>Where <see cref="Shoot"/> writes when used on its own, without a review run.</summary>
        public static void Into(string directory)
        {
            _dir = directory;
            Directory.CreateDirectory(_dir);
        }

        public static void Stop()
        {
            if (Running) EditorApplication.update -= Step;
            Running = false;
            File.WriteAllText(Path.Combine(_dir, "review_log.txt"), Log.ToString());
        }

        private static void Note(string line)
        {
            Log.AppendLine($"[{Time.realtimeSinceStartup:F1}] {line}");
            Debug.Log("[CourseReview] " + line);
        }

        private static void Step()
        {
            if (!EditorApplication.isPlaying)
            {
                Stop();
                return;
            }
            var host = UnityEngine.Object.FindAnyObjectByType<GameHost>();
            if (host == null || !host.Initialized) return;
            Frames.Add(Time.unscaledDeltaTime);

            SessionSnapshot snap = host.Coordinator.Snapshot;
            SessionState state = snap.State;
            if (state != _state)
            {
                if (state == SessionState.SHOT_SETUP) Frames.Clear();
                _state = state;
                _stateSince = Time.realtimeSinceStartup;
                Note($"state {state} hole {snap.Data.HoleIndex}");
            }
            float inState = Time.realtimeSinceStartup - _stateSince;

            if (host.ShopOpen)
            {
                if (inState > 1f) host.Shop.Back();
                return;
            }
            switch (state)
            {
                case SessionState.ATTRACT:
                    host.Send(CommandType.Start);
                    break;
                case SessionState.LOBBY:
                    if (snap.Data.Players.Count == 0) host.Send(CommandType.RosterSet, c => c.Names = new List<string> { "Caleb" });
                    else host.Send(CommandType.SettingsConfirm, c => c.Assistance = AssistanceProfileId.Club);
                    break;
                case SessionState.HOLE_LOAD:
                    if (snap.Data.HoleIndex != _hole) StartDrive(snap);
                    if (Time.realtimeSinceStartup >= _nextDriveShot)
                    {
                        // Photograph only the first drive so the later ones give clean frame times.
                        bool photograph = snap.Data.HoleIndex == 1;
                        if (_driveShot == 5) Note($"h{snap.Hole.Number:00} drive {FrameStats()} | {Benchmark()}");
                        if (photograph) Shoot($"h{snap.Hole.Number:00}_drive_{_driveShot:00}");
                        if (photograph && _driveShot == 3) ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"h{snap.Hole.Number:00}_drive_hud.png"));
                        _driveShot++;
                        _nextDriveShot = Time.realtimeSinceStartup + 0.55f;
                    }
                    break;
                case SessionState.BRIEFING:
                    if (inState > 1.5f) host.Send(CommandType.BriefingReady);
                    break;
                case SessionState.SHOT_SETUP:
                    if (!_teeShot && inState > 3f)
                    {
                        _teeShot = true;
                        Note($"h{snap.Hole.Number:00} tee {FrameStats()} | {Benchmark()}");
                        Shoot($"h{snap.Hole.Number:00}_tee_setup");
                    }
                    if (_teeShot && inState > 3.2f) host.Send(CommandType.Arm);
                    break;
                case SessionState.SHOT_ARMED:
                    if (inState > 0.2f) host.Emulator.TriggerLane(10);
                    break;
                case SessionState.SHOT_PRESENTING:
                    if (inState > 0.3f) host.SkipPresentation();
                    break;
                case SessionState.SHOT_RESULT:
                    if (inState > 0.3f) host.Send(CommandType.ResultContinue);
                    break;
                case SessionState.HANDOFF:
                    host.Send(CommandType.NextPlayerReady);
                    break;
                case SessionState.HOLE_COMPLETE:
                    if (inState < 0.05f) ShootGreen(snap.Hole);
                    if (snap.Data.HoleIndex + 1 >= _maxHoles)
                    {
                        Note("done " + FrameStats());
                        Stop();
                    }
                    else if (inState > 1f) host.Send(CommandType.HoleContinue);
                    break;
                case SessionState.ROUND_COMPLETE:
                    Note("round complete " + FrameStats());
                    Stop();
                    break;
            }
        }

        private static void StartDrive(SessionSnapshot snap)
        {
            _hole = snap.Data.HoleIndex;
            _teeShot = false;
            _driveShot = 0;
            _nextDriveShot = Time.realtimeSinceStartup + 0.25f;
            Frames.Clear();
        }

        private static string FrameStats()
        {
            if (Frames.Count < 2) return "no frames";
            var sorted = new List<float>(Frames);
            sorted.Sort();
            float sum = 0f;
            foreach (float f in Frames) sum += f;
            float mean = sum / Frames.Count;
            float p95 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))];
            string s = $"frames {Frames.Count} mean {mean * 1000f:F1} ms ({1f / Mathf.Max(mean, 1e-4f):F0} fps) p95 {p95 * 1000f:F1} ms";
            Frames.Clear();
            return s;
        }

        /// <summary>
        /// Average wall time of 20 synchronous 1080p renders of the gameplay camera (CPU and GPU, independent of the
        /// editor's background throttling), plus the Game view's last draw-call and triangle counts.
        /// </summary>
        public static string Benchmark()
        {
            Camera cam = Camera.main;
            if (cam == null) return "no camera";
            const int count = 20;
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = cam.targetTexture;
            var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            double t0 = EditorApplication.timeSinceStartup;
            for (int i = 0; i < count; i++) cam.Render();
            probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            double ms = (EditorApplication.timeSinceStartup - t0) * 1000.0 / count;
            RenderTexture.active = null;
            cam.targetTexture = previous;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(probe);
            return $"render {ms:F1} ms/frame at 1080p, draw calls {UnityStats.drawCalls}, setpass {UnityStats.setPassCalls}, tris {UnityStats.triangles / 1000}k";
        }

        /// <summary>The green from the approach side, low enough to read the cup, flag, collar and bunker lips.</summary>
        public static void ShootGreen(HoleDefinition hole)
        {
            Camera cam = Camera.main;
            if (cam == null || hole == null) return;
            Vector3 cup = hole.CupM.ToUnity(), tee = hole.TeeM.ToUnity();
            Vector3 back = tee - cup;
            back.y = 0f;
            back.Normalize();
            Vector3 from = cup + back * 4.2f + Vector3.up * 1.15f;
            Vector3 position = cam.transform.position;
            Quaternion rotation = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(cup + Vector3.up * 0.55f - from));
            Shoot($"h{hole.Number:00}_green");
            cam.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Renders the gameplay camera (post-processing included, HUD excluded) to a PNG.</summary>
        public static void Shoot(string name)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previous;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
