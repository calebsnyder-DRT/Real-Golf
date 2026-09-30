using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Puttshack.Protocol
{
    /// <summary>
    /// Normalized application protocol (spec section 34). A driver translates the controller's native protocol into this.
    /// False/empty values mean unverified or disabled, not verified absence.
    /// </summary>
    [Serializable]
    public sealed class CabinetCapabilities
    {
        public int ProtocolVersion = Domain.Versions.Protocol;
        public string DeviceId;
        public string BootId;
        public int TargetHoleCount;
        public bool TargetHoleDetection;
        public bool CaptureTimestamp;
        public bool SpeedMeasurement;
        public bool BallReturnSensor;
        public List<string> MenuActions = new List<string>();
        public List<string> LightingGroups = new List<string>();
        public string CalibrationVersion = "uncommissioned";

        [JsonIgnore]
        public bool IsCommissioned => !string.IsNullOrEmpty(CalibrationVersion) && CalibrationVersion != "uncommissioned";
    }

    [Flags]
    public enum EventQuality
    {
        None = 0,
        /// <summary>No controller timestamp; capture time is the bridge receipt time.</summary>
        ReceiptTimeOnly = 1,
        ClockUncertain = 2,
    }

    [Serializable]
    public sealed class CabinetEvent
    {
        public string DeviceId;
        public string BootId;
        public long Sequence;
        public int PhysicalHoleId;
        /// <summary>Logical lane 1-20 from the commissioned mapping, 1 on the left facing the display.</summary>
        public int LogicalLane;
        /// <summary>Capture time mapped into service monotonic time, when the controller supplies one.</summary>
        public double? CaptureHostMs;
        public double BridgeReceiptMs;
        public string CalibrationVersion;
        public EventQuality Quality;

        [JsonIgnore]
        public string Key => $"{DeviceId}|{BootId}|{Sequence}";
        [JsonIgnore]
        public double EffectiveCaptureMs => CaptureHostMs ?? BridgeReceiptMs;
    }

    /// <summary>Health view the service needs before arming. Pushed by the host from the active adapter.</summary>
    public struct CabinetHealth
    {
        public bool Connected;
        public double LastHeartbeatMs;
        public CabinetCapabilities Capabilities;
    }

    public enum InputRejection
    {
        None,
        NotArmed,
        UnknownDevice,
        StaleBoot,
        LaneOutOfRange,
        OutsideArmInterval,
        CalibrationMismatch,
        ClockInvalid,
        StorageFailure,
    }

    public sealed class InputAck
    {
        public bool Accepted;
        public bool Duplicate;
        public int ShotId;
        public InputRejection Rejection;
        public string EventKey;
        public double Power01;
        public int Lane;

        public override string ToString() => Accepted
            ? $"accepted shot {ShotId}{(Duplicate ? " (duplicate delivery)" : "")} lane {Lane} power {Power01:P0}"
            : $"rejected {Rejection} ({EventKey})";
    }
}
