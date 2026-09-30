using System;
using System.Collections.Generic;
using Puttshack.Protocol;
using Puttshack.Time;

namespace Puttshack.Hardware
{
    /// <summary>
    /// Deterministic developer emulator (spec section 36). Produces the exact normalized events the game consumes,
    /// including boot IDs, sequences, timestamps, duplicates, delays, and faults.
    /// </summary>
    public sealed class EmulatorCabinetAdapter : ICabinetAdapter
    {
        public const string CalibrationVersion = "emulator-map-1";

        private readonly IMonotonicClock _clock;
        private readonly List<Pending> _queue = new List<Pending>();
        private readonly List<CabinetEvent> _delivered = new List<CabinetEvent>();
        private readonly Random _jitterRandom;
        private readonly string _deviceId;
        private int _bootCount;
        private long _sequence;
        private double _lastHeartbeatMs;
        private CabinetCapabilities _capabilities;

        /// <summary>Delivery delay added to every event (bridge latency).</summary>
        public double DelayMs { get; set; }
        /// <summary>Uniform extra delay in [0, JitterMs), from a seeded generator.</summary>
        public double JitterMs { get; set; }
        /// <summary>The next lane event is delivered twice with the same identity.</summary>
        public bool DuplicateNext { get; set; }
        public bool Connected { get; private set; } = true;
        /// <summary>Heartbeats stop while true, so the service must refuse to arm.</summary>
        public bool HeartbeatSuppressed { get; set; }
        public bool SupplyCaptureTimestamp { get; set; } = true;
        public int EventsEmitted { get; private set; }
        public readonly int[] EventsPerLane = new int[21];

        public string BootId => _capabilities.BootId;
        public string DeviceId => _deviceId;

        private struct Pending
        {
            public double DeliverAtMs;
            public CabinetEvent Event;
        }

        public EmulatorCabinetAdapter(IMonotonicClock clock, string deviceId = "cabinet-emulator", int seed = 1234)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _deviceId = deviceId;
            _jitterRandom = new Random(seed);
            Boot();
        }

        public CabinetHealth Health => new CabinetHealth
        {
            Connected = Connected,
            LastHeartbeatMs = _lastHeartbeatMs,
            Capabilities = _capabilities,
        };

        public IReadOnlyList<CabinetEvent> Poll(double nowMs)
        {
            if (Connected && !HeartbeatSuppressed) _lastHeartbeatMs = nowMs;

            _delivered.Clear();
            if (!Connected) return _delivered;

            _queue.Sort((a, b) =>
            {
                int byTime = a.DeliverAtMs.CompareTo(b.DeliverAtMs);
                return byTime != 0 ? byTime : a.Event.Sequence.CompareTo(b.Event.Sequence);
            });
            int count = 0;
            while (count < _queue.Count && _queue[count].DeliverAtMs <= nowMs)
            {
                CabinetEvent evt = _queue[count].Event;
                evt.BridgeReceiptMs = nowMs;
                _delivered.Add(evt);
                count++;
            }
            _queue.RemoveRange(0, count);
            return _delivered;
        }

        /// <summary>A ball reaches target hole <paramref name="lane"/> now.</summary>
        public CabinetEvent TriggerLane(int lane) => Emit(lane, _clock.NowMs);

        /// <summary>An event whose capture time is <paramref name="ageMs"/> in the past, e.g. from before the arm epoch.</summary>
        public CabinetEvent InjectLateEvent(int lane, double ageMs) => Emit(lane, _clock.NowMs - ageMs);

        /// <summary>Two target holes report for one ball within a short window.</summary>
        public void InjectConflict(int laneA, int laneB)
        {
            Emit(laneA, _clock.NowMs);
            Emit(laneB, _clock.NowMs + 5);
        }

        public void RebootController() => Boot();

        public void Disconnect()
        {
            Connected = false;
            _queue.Clear();
        }

        public void Reconnect()
        {
            Connected = true;
            _lastHeartbeatMs = _clock.NowMs;
        }

        public void SetCommissioned(bool commissioned) =>
            _capabilities.CalibrationVersion = commissioned ? CalibrationVersion : "uncommissioned";

        private void Boot()
        {
            _bootCount++;
            _sequence = 0;
            _queue.Clear();
            _capabilities = new CabinetCapabilities
            {
                DeviceId = _deviceId,
                BootId = $"emu-boot-{_bootCount}",
                TargetHoleCount = 20,
                TargetHoleDetection = true,
                CaptureTimestamp = SupplyCaptureTimestamp,
                SpeedMeasurement = false,
                BallReturnSensor = false,
                MenuActions = new List<string> { "navigate", "confirm", "back" },
                CalibrationVersion = CalibrationVersion,
            };
            _lastHeartbeatMs = _clock.NowMs;
        }

        private CabinetEvent Emit(int lane, double captureMs)
        {
            var evt = new CabinetEvent
            {
                DeviceId = _deviceId,
                BootId = _capabilities.BootId,
                Sequence = ++_sequence,
                PhysicalHoleId = lane,
                LogicalLane = lane,
                CaptureHostMs = SupplyCaptureTimestamp ? captureMs : (double?)null,
                CalibrationVersion = _capabilities.CalibrationVersion,
                Quality = SupplyCaptureTimestamp ? EventQuality.None : EventQuality.ReceiptTimeOnly,
            };

            double deliverAt = Math.Max(_clock.NowMs, captureMs) + DelayMs + (JitterMs > 0 ? _jitterRandom.NextDouble() * JitterMs : 0);
            _queue.Add(new Pending { DeliverAtMs = deliverAt, Event = evt });
            if (DuplicateNext)
            {
                DuplicateNext = false;
                _queue.Add(new Pending { DeliverAtMs = deliverAt + 1, Event = Clone(evt) });
            }

            EventsEmitted++;
            if (lane >= 1 && lane <= 20) EventsPerLane[lane]++;
            return evt;
        }

        private static CabinetEvent Clone(CabinetEvent e) => new CabinetEvent
        {
            DeviceId = e.DeviceId,
            BootId = e.BootId,
            Sequence = e.Sequence,
            PhysicalHoleId = e.PhysicalHoleId,
            LogicalLane = e.LogicalLane,
            CaptureHostMs = e.CaptureHostMs,
            CalibrationVersion = e.CalibrationVersion,
            Quality = e.Quality,
        };
    }
}
