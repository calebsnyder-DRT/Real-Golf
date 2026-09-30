using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>Versioned surface parameters (TUNE values from spec section 19).</summary>
    public sealed class SurfaceDefinition
    {
        public SurfaceId Id;
        public string DisplayName;
        /// <summary>Multiplier on calibrated launch speed, not on distance.</summary>
        public double LaunchMultiplier;
        /// <summary>Normal-velocity restitution on impact.</summary>
        public double Restitution;
        /// <summary>Fraction of tangential speed removed on impact.</summary>
        public double TangentialDamping;
        /// <summary>Rolling deceleration opposing motion, m/s^2.</summary>
        public double RollingResistance;
        /// <summary>A resting ball stays put while downhill acceleration is below this, m/s^2.</summary>
        public double HoldingAcceleration;
        /// <summary>TUNE. How much landing backspin the surface converts into braking (0 = none, 1 = full).</summary>
        public double SpinGrip;
        /// <summary>TUNE. Coulomb friction on impact: tangential speed lost = mu * (1 + e) * |normal speed|.</summary>
        public double ImpactFriction;
        /// <summary>TUNE. Extra rolling deceleration per m/s above <see cref="SkidSpeed"/> (1/s): fast balls skid through grass.</summary>
        public double RollingDrag;
        /// <summary>TUNE. Speed below which rolling deceleration is constant (m/s). Above every putt speed on the green.</summary>
        public double SkidSpeed;
    }

    /// <summary>
    /// TUNE targets (flat, calm, powers 0.75-1.0), asserted in SolverTests: fairway rollout driver 15-30 yd, woods and
    /// long irons 8-15, mid/short irons 3-8, wedges 0-4; rough stops within a few yards; bunkers almost at once;
    /// approaches check up within 4 yd on the green; green rolls at stimp 10 (1.83 m/s release rolls ~3.05 m).
    /// </summary>
    public static class SurfaceCatalog
    {
        public const string Version = "surfaces-1.1";

        private static readonly Dictionary<SurfaceId, SurfaceDefinition> Surfaces = new Dictionary<SurfaceId, SurfaceDefinition>
        {
            [SurfaceId.Tee] = new SurfaceDefinition { Id = SurfaceId.Tee, SpinGrip = 0.80, DisplayName = "Tee", LaunchMultiplier = 1.00, Restitution = 0.34, TangentialDamping = 0.12, RollingResistance = 1.0, HoldingAcceleration = 0.55, ImpactFriction = 0.45, RollingDrag = 0.45, SkidSpeed = 0 },
            [SurfaceId.Fairway] = new SurfaceDefinition { Id = SurfaceId.Fairway, SpinGrip = 0.80, DisplayName = "Fairway", LaunchMultiplier = 1.00, Restitution = 0.32, TangentialDamping = 0.12, RollingResistance = 1.0, HoldingAcceleration = 0.50, ImpactFriction = 0.45, RollingDrag = 0.45, SkidSpeed = 0 },
            [SurfaceId.LightRough] = new SurfaceDefinition { Id = SurfaceId.LightRough, SpinGrip = 0.55, DisplayName = "Light rough", LaunchMultiplier = 0.92, Restitution = 0.22, TangentialDamping = 0.3, RollingResistance = 3.2, HoldingAcceleration = 1.2, ImpactFriction = 0.7, RollingDrag = 0.9, SkidSpeed = 0 },
            [SurfaceId.DeepRough] = new SurfaceDefinition { Id = SurfaceId.DeepRough, SpinGrip = 0.40, DisplayName = "Deep rough", LaunchMultiplier = 0.82, Restitution = 0.12, TangentialDamping = 0.5, RollingResistance = 5.5, HoldingAcceleration = 2.0, ImpactFriction = 0.8, RollingDrag = 0.8, SkidSpeed = 0 },
            [SurfaceId.Bunker] = new SurfaceDefinition { Id = SurfaceId.Bunker, SpinGrip = 1.00, DisplayName = "Bunker", LaunchMultiplier = 0.70, Restitution = 0.05, TangentialDamping = 0.85, RollingResistance = 6.0, HoldingAcceleration = 3.0, ImpactFriction = 1.0, RollingDrag = 1.0, SkidSpeed = 0 },
            [SurfaceId.Green] = new SurfaceDefinition { Id = SurfaceId.Green, SpinGrip = 1.00, DisplayName = "Green", LaunchMultiplier = 1.00, Restitution = 0.25, TangentialDamping = 0.12, RollingResistance = 0.55, HoldingAcceleration = 0.20, ImpactFriction = 0.8, RollingDrag = 0.6, SkidSpeed = 5.8 },
            [SurfaceId.CartPath] = new SurfaceDefinition { Id = SurfaceId.CartPath, SpinGrip = 0.15, DisplayName = "Cart path", LaunchMultiplier = 1.00, Restitution = 0.50, TangentialDamping = 0.10, RollingResistance = 0.40, HoldingAcceleration = 0.30, ImpactFriction = 0.15, RollingDrag = 0.05, SkidSpeed = 0 },
            [SurfaceId.Water] = new SurfaceDefinition { Id = SurfaceId.Water, SpinGrip = 0.00, DisplayName = "Water", LaunchMultiplier = 0.0, Restitution = 0.0, TangentialDamping = 1.0, RollingResistance = 100.0, HoldingAcceleration = 100.0, ImpactFriction = 1.0, RollingDrag = 0, SkidSpeed = 0 },
        };

        public static SurfaceDefinition Get(SurfaceId id) => Surfaces[id];
    }
}
