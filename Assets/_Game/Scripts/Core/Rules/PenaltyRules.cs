using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Simulation;

namespace Puttshack.Rules
{
    /// <summary>
    /// Simplified, disclosed penalty procedures (spec section 22). The played stroke always counts.
    /// Water: +1, authored drop (validated). Out-of-bounds: +1, previous shot origin.
    /// </summary>
    public static class PenaltyRules
    {
        public static PenaltyRecord Evaluate(ShotIntent intent, ShotResult result, ICollisionWorld world)
        {
            switch (result.Outcome)
            {
                case ShotOutcome.Water:
                    return WaterRelief(intent, result, world);
                case ShotOutcome.OutOfBounds:
                    return new PenaltyRecord
                    {
                        Type = PenaltyType.OutOfBounds,
                        Strokes = 1,
                        OriginalPosition = result.FinalPosition,
                        ReliefPosition = intent.OriginM,
                        ReliefLie = intent.Lie,
                        Explanation = "Out of bounds: one penalty stroke. Replay from the previous spot.",
                    };
                default:
                    return null;
            }
        }

        private static PenaltyRecord WaterRelief(ShotIntent intent, ShotResult result, ICollisionWorld world)
        {
            HoleDefinition hole = world.Hole;
            var record = new PenaltyRecord
            {
                Type = PenaltyType.Water,
                Strokes = 1,
                HazardId = result.HazardId,
                OriginalPosition = result.FinalPosition,
            };

            HazardVolume hazard = hole.Hazards.Find(h => h.Id == result.HazardId);
            DropPoint drop = hazard == null ? null : hole.Drops.Find(d => d.Id == hazard.DropPointId);
            if (drop != null && IsValidRelief(world, drop.Position, result.CrossingPosition))
            {
                record.ReliefPosition = new Vec3d(drop.Position.x, world.GroundHeight(drop.Position.x, drop.Position.z), drop.Position.z);
                record.ReliefLie = world.SurfaceAt(drop.Position.x, drop.Position.z);
                record.Explanation = "Water: one penalty stroke. Drop at the marked relief area.";
                return record;
            }

            record.UsedFallback = true;
            record.ReliefPosition = intent.OriginM;
            record.ReliefLie = intent.Lie;
            record.Explanation = "Water: one penalty stroke. Replay from the previous spot.";
            return record;
        }

        /// <summary>Relief must be on playable ground, outside hazards, and not closer to the cup than the crossing.</summary>
        public static bool IsValidRelief(ICollisionWorld world, Vec2d drop, Vec3d crossing)
        {
            if (!world.InBounds(drop.x, drop.z)) return false;
            if (world.HazardAt(drop.x, drop.z) != null) return false;
            Vec3d cup = world.Hole.CupM;
            var dropPoint = new Vec3d(drop.x, 0, drop.z);
            return Vec3d.HorizontalDistance(dropPoint, cup) + 1e-9 >= Vec3d.HorizontalDistance(crossing, cup);
        }
    }
}
