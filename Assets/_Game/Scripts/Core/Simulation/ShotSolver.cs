using System;
using System.Globalization;
using System.Text;
using Puttshack.Content;
using Puttshack.Domain;

namespace Puttshack.Simulation
{
    /// <summary>Pinned solver parameters. Any change requires a new simulation version.</summary>
    public sealed class SolverParameters
    {
        public double Gravity = 9.81;
        /// <summary>Quadratic drag coefficient (1/m): a = -kD |v - w| (v - w).</summary>
        public double DragK = 0.0048;
        public double Dt = 1.0 / 120.0;
        public double BallRadius = 0.02135;
        /// <summary>Below this rebound normal speed the ball transitions to rolling (m/s).</summary>
        public double RollTransitionSpeed = 1.2;
        public double StopSpeed = 0.04;
        public double MaxDurationS = 40;
        public int SampleEvery = 4;
        public double TreeRestitution = 0.35;
        public double TreeTangentialRetention = 0.6;
        /// <summary>TUNE. Fraction of remaining landing spin kept after each ground impact.</summary>
        public double SpinRetentionPerImpact = 0.35;
        /// <summary>TUNE. Cup capture forgiveness on top of each hole's cup profile.</summary>
        public CupCaptureParameters Cup = new CupCaptureParameters();

        public static readonly SolverParameters Default = new SolverParameters();
    }

    /// <summary>
    /// Deterministic game golf solver (spec sections 15-21). Fixed step, no hidden randomness, no dependency on frame rate.
    /// Positions in results are the ball's ground-contact point (center minus radius).
    /// </summary>
    public static class ShotSolver
    {
        public static ShotResult Solve(ShotIntent intent, ICollisionWorld world, SolverParameters p = null)
        {
            p = p ?? SolverParameters.Default;
            ClubDefinition club = ClubCatalog.Get(intent.ClubId);
            HoleDefinition hole = world.Hole;
            double r = p.BallRadius;
            double dt = p.Dt;
            var gravity = new Vec3d(0, -p.Gravity, 0);
            Vec3d cup = hole.CupM;
            CupProfile cupProfile = hole.Cup;
            CupCaptureParameters capture = p.Cup;
            double captureRadius = CupCapture.CaptureRadius(cupProfile, capture);

            var result = new ShotResult();
            Vec3d origin = intent.OriginM;
            origin.y = world.GroundHeight(origin.x, origin.z);
            Vec3d pos = origin + Vec3d.Up * r;
            Vec3d dir = intent.AimForward.Horizontal.Normalized.RotateYaw(intent.YawRad);
            if (dir.LengthSquared < 0.5) dir = Vec3d.Forward;

            bool airborne;
            Vec3d v;
            if (club.ShotType == ShotType.Putt)
            {
                double speed = ClubCatalog.PuttSpeed(intent.Power01, intent.PuttScale, SurfaceCatalog.Get(SurfaceId.Green).RollingResistance);
                v = ProjectOnPlane(dir, world.GroundNormal(pos.x, pos.z)).Normalized * speed;
                airborne = false;
            }
            else
            {
                SurfaceDefinition lie = SurfaceCatalog.Get(intent.Lie);
                double multiplier = intent.Lie == SurfaceId.Bunker && club.BunkerLaunchMultiplier.HasValue
                    ? club.BunkerLaunchMultiplier.Value
                    : lie.LaunchMultiplier;
                double speed = ClubCatalog.LaunchSpeed(club, intent.Power01) * multiplier;
                double a = Units.DegToRad(club.LaunchAngleDeg);
                v = (dir * Math.Cos(a) + Vec3d.Up * Math.Sin(a)) * speed;
                airborne = speed > 0.05;
            }

            double t = 0;
            double apexY = pos.y;
            double carry = 0;
            double roll = 0;
            bool hadContact = !airborne;
            bool lipArmed = true;
            double spin = club.ShotType == ShotType.Putt ? 0 : club.LandingCheck;
            Vec3d lastOutsideHazard = pos;
            int step = 0;

            result.Samples.Add(new TrajectorySample(0, Base(pos, r)));
            result.Events.Add(new ShotEvent(0, ShotEventType.Launch, Base(pos, r), intent.Lie));
            if (!airborne) result.Events.Add(new ShotEvent(0, ShotEventType.StartRoll, Base(pos, r), intent.Lie));

            while (true)
            {
                if (t > p.MaxDurationS)
                    return Finish(result, ShotOutcome.SolverFault, pos, r, world, carry, roll, apexY - (origin.y + r), t, ShotEventType.Fault);

                Vec3d prev = pos;
                if (airborne)
                {
                    Vec3d rel = v - intent.WindMps;
                    double relSpeed = rel.Length;
                    Vec3d acc = gravity - rel * (p.DragK * relSpeed);
                    if (club.LiftCoefficient > 0 && relSpeed > 1e-9)
                    {
                        Vec3d side = Vec3d.Cross(Vec3d.Up, rel);
                        if (side.LengthSquared > 1e-12)
                        {
                            Vec3d liftDir = Vec3d.Cross(rel, side.Normalized).Normalized;
                            acc += liftDir * (club.LiftCoefficient * relSpeed * relSpeed);
                        }
                    }
                    v += acc * dt;
                    Vec3d next = pos + v * dt;
                    t += dt;

                    if (world.SweepTrees(pos, next, r, out TreeHit treeHit))
                    {
                        pos = treeHit.Point + treeHit.Normal * 1e-4;
                        v = ReflectOffTrunk(v, treeHit.Normal, p) + new Vec3d(0, v.y * 0.3, 0);
                        result.Events.Add(new ShotEvent(t, ShotEventType.TreeHit, Base(pos, r), world.SurfaceAt(pos.x, pos.z)));
                    }
                    else
                    {
                        double groundNext = world.GroundHeight(next.x, next.z) + r;
                        if (next.y <= groundNext)
                        {
                            double gapPrev = pos.y - (world.GroundHeight(pos.x, pos.z) + r);
                            double gapNext = next.y - groundNext;
                            double f = gapPrev - gapNext > 1e-12 ? Clamp01(gapPrev / (gapPrev - gapNext)) : 1;
                            Vec3d contact = Vec3d.Lerp(pos, next, f);
                            contact.y = world.GroundHeight(contact.x, contact.z) + r;
                            pos = contact;

                            if (!hadContact)
                            {
                                hadContact = true;
                                carry = Vec3d.HorizontalDistance(origin, contact);
                                result.Events.Add(new ShotEvent(t, ShotEventType.FirstContact, Base(pos, r), world.SurfaceAt(pos.x, pos.z)));
                            }
                            else
                            {
                                roll += Vec3d.HorizontalDistance(prev, contact);
                            }

                            if (!world.InBounds(pos.x, pos.z))
                            {
                                result.CrossingPosition = Base(lastOutsideHazard, r);
                                return Finish(result, ShotOutcome.OutOfBounds, pos, r, world, carry, roll, apexY - (origin.y + r), t, ShotEventType.OutOfBounds);
                            }
                            HazardVolume hazard = world.HazardAt(pos.x, pos.z);
                            if (hazard != null)
                            {
                                result.HazardId = hazard.Id;
                                result.CrossingPosition = Base(CrossingPoint(world, lastOutsideHazard, pos), r);
                                return Finish(result, ShotOutcome.Water, pos, r, world, carry, roll, apexY - (origin.y + r), t, ShotEventType.WaterEntry);
                            }

                            SurfaceId surfaceId = world.SurfaceAt(pos.x, pos.z);
                            SurfaceDefinition surface = SurfaceCatalog.Get(surfaceId);
                            Vec3d n = world.GroundNormal(pos.x, pos.z);
                            double vn = Vec3d.Dot(v, n);
                            Vec3d vt = ImpactTangential(v - n * vn, vn, surface, spin);
                            spin *= p.SpinRetentionPerImpact;
                            double rebound = -vn * surface.Restitution;

                            if (CupCapture.Drops(Vec3d.HorizontalDistance(pos, cup), vt.Length, cupProfile, capture))
                                return Holed(result, p, pos, vt, cup, r, world, carry, roll + Vec3d.HorizontalDistance(prev, pos), apexY - (origin.y + r), t);

                            if (rebound < p.RollTransitionSpeed)
                            {
                                airborne = false;
                                v = vt;
                                result.Events.Add(new ShotEvent(t, ShotEventType.StartRoll, Base(pos, r), surfaceId));
                            }
                            else
                            {
                                v = vt + n * rebound;
                                result.Events.Add(new ShotEvent(t, ShotEventType.Bounce, Base(pos, r), surfaceId));
                            }
                        }
                        else
                        {
                            if (hadContact) roll += Vec3d.HorizontalDistance(pos, next);
                            pos = next;
                            if (pos.y > apexY) apexY = pos.y;
                        }
                    }
                }
                else
                {
                    SurfaceId surfaceId = world.SurfaceAt(pos.x, pos.z);
                    SurfaceDefinition surface = SurfaceCatalog.Get(surfaceId);
                    Vec3d n = world.GroundNormal(pos.x, pos.z);
                    Vec3d downhill = gravity - n * Vec3d.Dot(gravity, n);
                    double speed = v.Length;

                    if (speed < p.StopSpeed)
                    {
                        if (downhill.Length <= Math.Max(surface.HoldingAcceleration, surface.RollingResistance))
                        {
                            if (CupCapture.DropsAtRest(pos, cup, cupProfile, capture))
                                return Holed(result, p, pos, Vec3d.Zero, cup, r, world, carry, roll, apexY - (origin.y + r), t);
                            return Finish(result, ShotOutcome.Rest, pos, r, world, carry, roll, apexY - (origin.y + r), t, ShotEventType.Rest);
                        }
                        v += downhill * dt;
                    }
                    else
                    {
                        double decel = surface.RollingResistance + surface.RollingDrag * Math.Max(0, speed - surface.SkidSpeed);
                        Vec3d vNew = v + (downhill - v / speed * decel) * dt;
                        v = Vec3d.Dot(vNew, v) <= 0 ? Vec3d.Zero : vNew;
                    }
                    v += CupCapture.RimAssist(pos, v.Length, cup, cupProfile, capture) * dt;

                    Vec3d next = pos + v * dt;
                    next.y = world.GroundHeight(next.x, next.z) + r;
                    t += dt;

                    if (world.SweepTrees(pos, next, r, out TreeHit treeHit))
                    {
                        next = treeHit.Point + treeHit.Normal * 1e-4;
                        next.y = world.GroundHeight(next.x, next.z) + r;
                        v = ReflectOffTrunk(v, treeHit.Normal, p);
                        result.Events.Add(new ShotEvent(t, ShotEventType.TreeHit, Base(next, r), surfaceId));
                    }

                    double closest = ClosestApproach(pos, next, cup, out double closestT);
                    if (closest <= captureRadius && lipArmed)
                    {
                        if (CupCapture.Drops(LineOffset(pos, next, cup, closest), v.Length, cupProfile, capture))
                            return Holed(result, p, Vec3d.Lerp(pos, next, closestT), v, cup, r, world, carry,
                                roll + Vec3d.HorizontalDistance(pos, next) * closestT, apexY - (origin.y + r), t);

                        lipArmed = false;
                        double side = Math.Sign((next.x - pos.x) * (cup.z - pos.z) - (next.z - pos.z) * (cup.x - pos.x));
                        double deflection = -side * 0.35 * (1 - closest / captureRadius);
                        v = v.RotateYaw(deflection) * cupProfile.LipSpeedRetention;
                        result.Events.Add(new ShotEvent(t, ShotEventType.LipOut, Base(next, r), surfaceId));
                    }
                    else if (closest > cupProfile.LipRadius * 2)
                    {
                        lipArmed = true;
                    }

                    if (!world.InBounds(next.x, next.z))
                    {
                        result.CrossingPosition = Base(pos, r);
                        return Finish(result, ShotOutcome.OutOfBounds, next, r, world, carry, roll, apexY - (origin.y + r), t, ShotEventType.OutOfBounds);
                    }
                    HazardVolume hazard = world.HazardAt(next.x, next.z);
                    if (hazard != null)
                    {
                        result.HazardId = hazard.Id;
                        result.CrossingPosition = Base(CrossingPoint(world, pos, next), r);
                        return Finish(result, ShotOutcome.Water, next, r, world, carry, roll + Vec3d.HorizontalDistance(pos, next), apexY - (origin.y + r), t, ShotEventType.WaterEntry);
                    }

                    roll += Vec3d.HorizontalDistance(pos, next);
                    Vec3d n2 = world.GroundNormal(next.x, next.z);
                    double keep = v.Length;
                    v = ProjectOnPlane(v, n2).Normalized * keep;
                    pos = next;
                }

                if (world.HazardAt(pos.x, pos.z) == null) lastOutsideHazard = pos;

                step++;
                if (step % p.SampleEvery == 0) result.Samples.Add(new TrajectorySample(t, Base(pos, r)));
            }
        }

        /// <summary>
        /// Tangential velocity after a ground impact: base damping, Coulomb friction from the normal impulse (steep
        /// landings brake more), then the club's declared landing spin scaled by the surface grip.
        /// </summary>
        private static Vec3d ImpactTangential(Vec3d vt, double vn, SurfaceDefinition surface, double spin)
        {
            double speed = vt.Length;
            if (speed < 1e-9) return Vec3d.Zero;
            double kept = speed * (1 - surface.TangentialDamping);
            kept -= surface.ImpactFriction * (1 + surface.Restitution) * Math.Abs(vn);
            kept *= 1 - spin * surface.SpinGrip;
            return kept <= 0 ? Vec3d.Zero : vt * (kept / speed);
        }

        private static Vec3d ReflectOffTrunk(Vec3d v, Vec3d normal, SolverParameters p)
        {
            Vec3d h = v.Horizontal;
            double vn = Vec3d.Dot(h, normal);
            if (vn >= 0) return h;
            Vec3d tangential = h - normal * vn;
            return tangential * p.TreeTangentialRetention - normal * (vn * p.TreeRestitution);
        }

        /// <summary>Plays the drop from <paramref name="entry"/> (ball centre where it was caught) and finishes at the cup centre.</summary>
        private static ShotResult Holed(ShotResult result, SolverParameters p, Vec3d entry, Vec3d velocity, Vec3d cup, double r,
            ICollisionWorld world, double carry, double roll, double apex, double t)
        {
            Vec3d cupBase = new Vec3d(cup.x, world.GroundHeight(cup.x, cup.z), cup.z);
            double settled = CupCapture.AppendDrop(result.Samples, t, Base(entry, r), velocity, cupBase, world.Hole.Cup, p.Cup,
                p.Gravity, p.Dt * p.SampleEvery * 0.5);
            return Finish(result, ShotOutcome.Holed, cupBase + Vec3d.Up * r, r, world, carry, roll, apex, settled, ShotEventType.Cup);
        }

        private static ShotResult Finish(ShotResult result, ShotOutcome outcome, Vec3d center, double r, ICollisionWorld world,
            double carry, double roll, double apex, double t, ShotEventType terminalEvent)
        {
            Vec3d final = Base(center, r);
            result.Outcome = outcome;
            result.FinalPosition = final;
            result.FinalLie = world.SurfaceAt(final.x, final.z);
            result.CarryM = carry;
            result.RollM = roll;
            result.TotalM = carry + roll;
            result.ApexM = Math.Max(0, apex);
            result.DurationS = t;
            result.Samples.Add(new TrajectorySample(t, final));
            result.Events.Add(new ShotEvent(t, terminalEvent, final, result.FinalLie));
            result.ResultHash = Hash(result);
            return result;
        }

        /// <summary>Bisects the horizontal segment to find the last point outside any hazard.</summary>
        private static Vec3d CrossingPoint(ICollisionWorld world, Vec3d outside, Vec3d inside)
        {
            Vec3d a = outside;
            Vec3d b = inside;
            for (int i = 0; i < 24; i++)
            {
                Vec3d m = Vec3d.Lerp(a, b, 0.5);
                if (world.HazardAt(m.x, m.z) == null) a = m;
                else b = m;
            }
            return a;
        }

        private static double ClosestApproach(Vec3d a, Vec3d b, Vec3d c, out double tAlong)
        {
            double dx = b.x - a.x;
            double dz = b.z - a.z;
            double len2 = dx * dx + dz * dz;
            tAlong = len2 > 1e-14 ? Clamp01(((c.x - a.x) * dx + (c.z - a.z) * dz) / len2) : 0;
            double px = a.x + dx * tAlong - c.x;
            double pz = a.z + dz * tAlong - c.z;
            return Math.Sqrt(px * px + pz * pz);
        }

        /// <summary>
        /// Horizontal distance from <paramref name="c"/> to the ball's line of travel. The step that first reaches the
        /// capture radius sees the cup at the rim even on a centred putt; the line offset is how far off-centre it is rolling.
        /// </summary>
        private static double LineOffset(Vec3d a, Vec3d b, Vec3d c, double fallback)
        {
            double dx = b.x - a.x;
            double dz = b.z - a.z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-7) return fallback;
            return Math.Abs(dx * (c.z - a.z) - dz * (c.x - a.x)) / len;
        }

        private static Vec3d ProjectOnPlane(Vec3d v, Vec3d n) => v - n * Vec3d.Dot(v, n);
        private static Vec3d Base(Vec3d center, double r) => new Vec3d(center.x, center.y - r, center.z);
        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>Canonical hash of the discrete outcome and metrics, used for replay comparison.</summary>
        public static string Hash(ShotResult result)
        {
            var sb = new StringBuilder();
            sb.Append(result.Outcome).Append('|').Append(result.FinalLie).Append('|');
            AppendR(sb, result.FinalPosition.x); AppendR(sb, result.FinalPosition.y); AppendR(sb, result.FinalPosition.z);
            AppendR(sb, result.CarryM); AppendR(sb, result.RollM); AppendR(sb, result.DurationS);
            sb.Append(result.HazardId ?? "-").Append('|').Append(result.Samples.Count);
            return ContentHasher.Sha256(sb.ToString());
        }

        private static void AppendR(StringBuilder sb, double v) => sb.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append('|');
    }
}
