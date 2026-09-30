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
    }

    public static class SurfaceCatalog
    {
        public const string Version = "surfaces-1.0";

        private static readonly Dictionary<SurfaceId, SurfaceDefinition> Surfaces = new Dictionary<SurfaceId, SurfaceDefinition>
        {
            [SurfaceId.Tee] = new SurfaceDefinition { Id = SurfaceId.Tee, DisplayName = "Tee", LaunchMultiplier = 1.00, Restitution = 0.34, TangentialDamping = 0.26, RollingResistance = 0.85, HoldingAcceleration = 0.55 },
            [SurfaceId.Fairway] = new SurfaceDefinition { Id = SurfaceId.Fairway, DisplayName = "Fairway", LaunchMultiplier = 1.00, Restitution = 0.32, TangentialDamping = 0.28, RollingResistance = 0.75, HoldingAcceleration = 0.50 },
            [SurfaceId.LightRough] = new SurfaceDefinition { Id = SurfaceId.LightRough, DisplayName = "Light rough", LaunchMultiplier = 0.92, Restitution = 0.22, TangentialDamping = 0.45, RollingResistance = 2.2, HoldingAcceleration = 1.2 },
            [SurfaceId.DeepRough] = new SurfaceDefinition { Id = SurfaceId.DeepRough, DisplayName = "Deep rough", LaunchMultiplier = 0.82, Restitution = 0.12, TangentialDamping = 0.60, RollingResistance = 4.0, HoldingAcceleration = 2.0 },
            [SurfaceId.Bunker] = new SurfaceDefinition { Id = SurfaceId.Bunker, DisplayName = "Bunker", LaunchMultiplier = 0.70, Restitution = 0.05, TangentialDamping = 0.85, RollingResistance = 6.0, HoldingAcceleration = 3.0 },
            [SurfaceId.Green] = new SurfaceDefinition { Id = SurfaceId.Green, DisplayName = "Green", LaunchMultiplier = 1.00, Restitution = 0.25, TangentialDamping = 0.30, RollingResistance = 0.55, HoldingAcceleration = 0.20 },
            [SurfaceId.CartPath] = new SurfaceDefinition { Id = SurfaceId.CartPath, DisplayName = "Cart path", LaunchMultiplier = 1.00, Restitution = 0.50, TangentialDamping = 0.10, RollingResistance = 0.40, HoldingAcceleration = 0.30 },
            [SurfaceId.Water] = new SurfaceDefinition { Id = SurfaceId.Water, DisplayName = "Water", LaunchMultiplier = 0.0, Restitution = 0.0, TangentialDamping = 1.0, RollingResistance = 100.0, HoldingAcceleration = 100.0 },
        };

        public static SurfaceDefinition Get(SurfaceId id) => Surfaces[id];
    }
}
