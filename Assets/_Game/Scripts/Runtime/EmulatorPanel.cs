using Puttshack.Hardware;
using Puttshack.Session;
using UnityEngine;

namespace Puttshack.Runtime
{
    /// <summary>
    /// Developer-only cabinet emulator (spec section 36). Emits the same normalized events as hardware and injects
    /// faults: delay, jitter, duplicates, late events, conflicts, reboots, disconnects, heartbeat loss, crashes.
    /// </summary>
    public sealed class EmulatorPanel : MonoBehaviour
    {
        public GameHost Host;
        public bool Visible = true;

        private Rect _window = new Rect(20, 220, 330, 560);
        private Vector2 _logScroll;

        private void OnGUI()
        {
            if (!Visible || Host == null || !Host.Initialized) return;
            _window = GUI.Window(0x5057, _window, DrawWindow, "Cabinet emulator (F1)");
        }

        private void DrawWindow(int id)
        {
            EmulatorCabinetAdapter emu = Host.Emulator;
            SessionCoordinator svc = Host.Coordinator;

            GUILayout.Label($"State: {svc.State}   r{svc.Revision}   boot {emu.BootId}");
            svc.CabinetReady(out string status);
            GUILayout.Label($"Cabinet: {status}");

            GUILayout.Label("Target holes (lanes):");
            for (int row = 0; row < 4; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 5; col++)
                {
                    int lane = row * 5 + col + 1;
                    if (GUILayout.Button(lane.ToString(), GUILayout.Width(56))) emu.TriggerLane(lane);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Delay {emu.DelayMs:0} ms", GUILayout.Width(110));
            emu.DelayMs = GUILayout.HorizontalSlider((float)emu.DelayMs, 0, 1000);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Jitter {emu.JitterMs:0} ms", GUILayout.Width(110));
            emu.JitterMs = GUILayout.HorizontalSlider((float)emu.JitterMs, 0, 300);
            GUILayout.EndHorizontal();

            emu.DuplicateNext = GUILayout.Toggle(emu.DuplicateNext, "Duplicate next event");
            emu.HeartbeatSuppressed = GUILayout.Toggle(emu.HeartbeatSuppressed, "Suppress heartbeat");
            emu.SupplyCaptureTimestamp = GUILayout.Toggle(emu.SupplyCaptureTimestamp, "Supply capture timestamps");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Late event")) emu.InjectLateEvent(10, 60000);
            if (GUILayout.Button("Conflict 7+8")) emu.InjectConflict(7, 8);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reboot")) emu.RebootController();
            if (GUILayout.Button(emu.Connected ? "Disconnect" : "Reconnect"))
            {
                if (emu.Connected) emu.Disconnect();
                else emu.Reconnect();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("Crash the service (restarts from storage):");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(svc.CrashAfter == CrashPoint.AfterReservation ? "[armed] after reserve" : "After reserve"))
                svc.CrashAfter = svc.CrashAfter == CrashPoint.AfterReservation ? CrashPoint.None : CrashPoint.AfterReservation;
            if (GUILayout.Button(svc.CrashAfter == CrashPoint.AfterResolution ? "[armed] after result" : "After result"))
                svc.CrashAfter = svc.CrashAfter == CrashPoint.AfterResolution ? CrashPoint.None : CrashPoint.AfterResolution;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Restart service now")) Host.SimulateRestart(CrashPoint.None);

            GUILayout.Space(4);
            GUILayout.Label($"Last input: {(Host.LastAck != null ? Host.LastAck.ToString() : "-")}");
            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.Height(110));
            var log = svc.Log;
            for (int i = log.Count - 1; i >= 0 && i >= log.Count - 30; i--) GUILayout.Label(log[i]);
            GUILayout.EndScrollView();

            GUI.DragWindow();
        }
    }
}
