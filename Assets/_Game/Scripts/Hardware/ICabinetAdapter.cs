using System.Collections.Generic;
using Puttshack.Protocol;

namespace Puttshack.Hardware
{
    /// <summary>
    /// The only component allowed to talk to the controller. Real and emulated adapters emit the same normalized events,
    /// so both go through the same acceptance path.
    /// </summary>
    public interface ICabinetAdapter
    {
        CabinetHealth Health { get; }

        /// <summary>Returns events whose delivery time has arrived and refreshes heartbeat state.</summary>
        IReadOnlyList<CabinetEvent> Poll(double nowMs);
    }
}
