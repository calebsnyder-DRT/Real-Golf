using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;

namespace Puttshack.Simulation
{
    /// <summary>
    /// Game-feel forgiveness layered on each hole's <see cref="CupProfile"/>. Part of the pinned solver parameters:
    /// any change requires a new simulation version. Zero margins and assist with both speed scales at 1 give the sim-1.2
    /// cup outcomes (hard CaptureSpeed cut-off inside the profile's CaptureRadius).
    /// </summary>
    public sealed class CupCaptureParameters
    {
        /// <summary>TUNE. Added to the profile's CaptureRadius so a ball visibly catching the edge drops (m).</summary>
        public double EdgeMarginM = 0.008;
        /// <summary>TUNE. Fastest drop dead centre, as a multiple of the profile's CaptureSpeed.</summary>
        public double CentreSpeedScale = 1.2;
        /// <summary>TUNE. Fastest drop at the very edge of the capture radius, as a multiple of CaptureSpeed.</summary>
        public double EdgeSpeedScale = 0.9;
        /// <summary>TUNE. How far past the capture radius the rim assist reaches (m).</summary>
        public double AssistReachM = 0.02;
        /// <summary>TUNE. Pull toward the cup centre at the capture edge (m/s^2); fades out with distance and speed.</summary>
        public double AssistAccel = 0.6;
        /// <summary>TUNE. Balls rolling at or above this speed feel no rim assist (m/s).</summary>
        public double AssistMaxSpeed = 0.6;
        /// <summary>TUNE. A ball coming to rest within the profile's CaptureRadius plus this margin topples in (m).</summary>
        public double RestDropMarginM = 0.012;
        /// <summary>Slowest roll-in speed of the drop animation, so a ball dying on the lip visibly rolls in (m/s).</summary>
        public double DropMinSpeed = 0.3;
        /// <summary>Depth the ball base sinks below the green when holed (m). The ball view holds a holed ball here.</summary>
        public double DropDepthM = 0.06;
    }

    /// <summary>Pure cup capture rules used by <see cref="ShotSolver"/>.</summary>
    public static class CupCapture
    {
        /// <summary>Largest ball-centre offset from the cup centre that can still drop (m).</summary>
        public static double CaptureRadius(CupProfile cup, CupCaptureParameters c) => cup.CaptureRadius + c.EdgeMarginM;

        /// <summary>
        /// Fastest speed (m/s) at which a ball whose centre passes <paramref name="offset"/> from the cup centre still
        /// drops; 0 outside the capture radius. Scales with the chord the ball crosses, like a real cup: a centred ball has
        /// the most hole under it to fall into.
        /// </summary>
        public static double MaxCaptureSpeed(double offset, CupProfile cup, CupCaptureParameters c)
        {
            double radius = CaptureRadius(cup, c);
            if (offset > radius) return 0;
            double u = offset / radius;
            double chord = Math.Sqrt(Math.Max(0, 1 - u * u));
            return cup.CaptureSpeed * (c.EdgeSpeedScale + (c.CentreSpeedScale - c.EdgeSpeedScale) * chord);
        }

        public static bool Drops(double offset, double speed, CupProfile cup, CupCaptureParameters c) =>
            offset <= CaptureRadius(cup, c) && speed < MaxCaptureSpeed(offset, cup, c);

        public static bool DropsAtRest(Vec3d ball, Vec3d cupCentre, CupProfile cup, CupCaptureParameters c) =>
            Vec3d.HorizontalDistance(ball, cupCentre) <= cup.CaptureRadius + c.RestDropMarginM;

        /// <summary>Horizontal pull toward the cup on a slow ball near the rim (m/s^2).</summary>
        public static Vec3d RimAssist(Vec3d ball, double speed, Vec3d cupCentre, CupProfile cup, CupCaptureParameters c)
        {
            if (c.AssistAccel <= 0 || speed >= c.AssistMaxSpeed) return Vec3d.Zero;
            double dx = cupCentre.x - ball.x;
            double dz = cupCentre.z - ball.z;
            double d = Math.Sqrt(dx * dx + dz * dz);
            double inner = CaptureRadius(cup, c);
            double outer = inner + c.AssistReachM;
            if (d >= outer || d < 1e-9) return Vec3d.Zero;
            double reach = d <= inner ? 1 : (outer - d) / c.AssistReachM;
            double gain = c.AssistAccel * reach * (1 - speed / c.AssistMaxSpeed) / d;
            return new Vec3d(dx * gain, 0, dz * gain);
        }

        /// <summary>
        /// Appends the visible drop: the ball carries on along its line, curls onto the cup centre and falls once its centre
        /// is over the hole, ending <see cref="CupCaptureParameters.DropDepthM"/> below <paramref name="cupBase"/>.
        /// Positions are the ball base, so they dip below the green. Returns the time the ball settles.
        /// </summary>
        public static double AppendDrop(List<TrajectorySample> samples, double t, Vec3d entryBase, Vec3d velocity, Vec3d cupBase,
            CupProfile cup, CupCaptureParameters c, double gravity, double sampleDt)
        {
            Vec3d vh = velocity.Horizontal;
            double speed = vh.Length;
            double fallTime = Math.Sqrt(2 * c.DropDepthM / gravity);
            Vec3d control = speed > 1e-9 ? entryBase + vh * (Math.Min(speed * fallTime, cup.CupRadius) * 0.5 / speed) : entryBase;
            double path = Vec3d.HorizontalDistance(entryBase, control) + Vec3d.HorizontalDistance(control, cupBase);
            double rollTime = path / Math.Max(speed, c.DropMinSpeed);

            if (samples.Count == 0 || samples[samples.Count - 1].t < t)
                samples.Add(new TrajectorySample(t, new Vec3d(entryBase.x, cupBase.y, entryBase.z)));
            double fallStart = Vec3d.HorizontalDistance(entryBase, cupBase) <= cup.CupRadius ? 0 : -1;
            double elapsed = 0;
            while (true)
            {
                elapsed += sampleDt;
                double f = rollTime > 1e-9 ? Math.Min(1, elapsed / rollTime) : 1;
                double g = 1 - f;
                double x = g * g * entryBase.x + 2 * f * g * control.x + f * f * cupBase.x;
                double z = g * g * entryBase.z + 2 * f * g * control.z + f * f * cupBase.z;
                double fromCentre = Math.Sqrt((x - cupBase.x) * (x - cupBase.x) + (z - cupBase.z) * (z - cupBase.z));
                if (fallStart < 0 && fromCentre <= cup.CupRadius) fallStart = elapsed;
                double fall = fallStart < 0 ? 0 : elapsed - fallStart;
                double depth = Math.Min(c.DropDepthM, 0.5 * gravity * fall * fall);
                samples.Add(new TrajectorySample(t + elapsed, new Vec3d(x, cupBase.y - depth, z)));
                if (f >= 1 && depth >= c.DropDepthM) return t + elapsed;
            }
        }
    }
}
