using System.Diagnostics;

namespace Puttshack.Time
{
    /// <summary>Monotonic milliseconds for durations. Wall-clock time is for logs only.</summary>
    public interface IMonotonicClock
    {
        double NowMs { get; }
    }

    public sealed class SystemMonotonicClock : IMonotonicClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public double NowMs => _stopwatch.Elapsed.TotalMilliseconds;
    }

    /// <summary>Deterministic clock for tests and replayed emulator scripts.</summary>
    public sealed class VirtualClock : IMonotonicClock
    {
        public double NowMs { get; private set; }

        public VirtualClock(double startMs = 0)
        {
            NowMs = startMs;
        }

        public void Advance(double ms)
        {
            if (ms < 0) throw new System.ArgumentOutOfRangeException(nameof(ms), "A monotonic clock cannot go backwards.");
            NowMs += ms;
        }

        public void AdvanceTo(double ms)
        {
            if (ms < NowMs) throw new System.ArgumentOutOfRangeException(nameof(ms), "A monotonic clock cannot go backwards.");
            NowMs = ms;
        }
    }
}
