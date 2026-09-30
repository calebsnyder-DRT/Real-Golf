using System;
using System.Collections.Generic;

namespace Puttshack.Hardware
{
    /// <summary>
    /// Replays emulator actions at fixed virtual times so tests never depend on a human clicking at the right moment.
    /// </summary>
    public sealed class EmulatorScript
    {
        private readonly List<(double atMs, Action<EmulatorCabinetAdapter> action)> _steps = new List<(double, Action<EmulatorCabinetAdapter>)>();
        private int _next;

        public EmulatorScript At(double atMs, Action<EmulatorCabinetAdapter> action)
        {
            _steps.Add((atMs, action));
            _steps.Sort((a, b) => a.atMs.CompareTo(b.atMs));
            return this;
        }

        public EmulatorScript Lane(double atMs, int lane) => At(atMs, e => e.TriggerLane(lane));

        public bool Finished => _next >= _steps.Count;

        /// <summary>Runs every step scheduled at or before <paramref name="nowMs"/>.</summary>
        public void Run(EmulatorCabinetAdapter emulator, double nowMs)
        {
            while (_next < _steps.Count && _steps[_next].atMs <= nowMs)
            {
                _steps[_next].action(emulator);
                _next++;
            }
        }
    }
}
