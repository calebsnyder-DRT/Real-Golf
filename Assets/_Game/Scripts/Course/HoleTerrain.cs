using RealGolf.Data;
using UnityEngine;

namespace RealGolf.Course
{
    /// <summary>
    /// Analytic description of a hole's ground: height and surface type at any
    /// planar point. The mesh builder and the gameplay systems both read from
    /// here so what you see always matches what the ball rolls on.
    /// </summary>
    public sealed class HoleTerrain
    {
        private const float RoughBand = 20f;
        private const float TeeRadius = 6f;
        private const float WaterBedDepth = 3.5f;
        private const float WaterSurfaceDrop = 1.3f;

        // Shaping ported from the Blender sculpt of holes 1-9. The look there came
        // from four things this terrain did not do: mounding that only builds off the
        // mown corridor, a tee cut as a raised shelf, a green that crowns toward its
        // middle behind a collar of its own, and bunkers with a lip thrown up on the
        // far side of the hollow.
        private const float MoundAmplitude = 3.4f;   // ridge height in the rough
        private const float FairwayRoll = 0.55f;     // the same ridge, damped, on the fairway
        private const float CorridorBlendBand = 11f; // metres from mown edge to full rough

        private const float TeeLift = 1.5f;          // shelf stands above the corridor
        private const float GreenLift = 1.2f;        // green sits up out of its approach
        private const float GreenCrown = 0.35f;      // fall from the middle to the edge
        private const float CollarBand = 3.2f;       // width of the fringe around the green

        private const float BunkerDepth = 1.5f;
        private const float BunkerLipHeight = 0.55f;
        private const float BunkerLipOuter = 1.45f;  // lip reaches this multiple of the edge

        private readonly HoleDefinition _hole;
        private readonly float _noiseOffsetX;
        private readonly float _noiseOffsetZ;

        public HoleDefinition Hole => _hole;

        public HoleTerrain(HoleDefinition hole)
        {
            _hole = hole;
            var rng = new System.Random(hole.Seed);
            _noiseOffsetX = (float)rng.NextDouble() * 1000f;
            _noiseOffsetZ = (float)rng.NextDouble() * 1000f;
        }

        /// <summary>Terrain height ignoring bunker and water excavation.</summary>
        public float SampleGroundHeight(Vector2 p)
        {
            // The broad landform every part of the hole sits on.
            float broad = CorridorHeight(p) + Perlin(p, 0.006f) * 3.4f;
            // Two octaves: the long wave carries the mounds, the short one keeps them
            // from reading as one smooth swell. Unity's Perlin only spans about half
            // of [-1,1] in practice, so the amplitudes are set against that.
            float ridge = Perlin(p, 0.021f) + Perlin(p, 0.052f) * 0.45f;
            float fine = Perlin(p, 0.075f);

            // Weighted by the mown edge itself rather than a plain band off the
            // centreline, so the graded corridor follows the same wander the fairway
            // is drawn with. Mounds build in the rough and are damped to a roll on
            // the fairway, which is what stops the hole reading as a flat ribbon.
            float mown = FairwayWeight(p);
            float height = broad
                           + ridge * MoundAmplitude * (1f - mown)
                           + ridge * FairwayRoll * mown
                           + fine * (0.35f - 0.25f * mown);

            // Green complex: a plateau lifted out of its approach that crowns toward
            // the middle, with only gentle borrow on top of it.
            float greenDistance = Vector2.Distance(p, _hole.GreenCenter);
            float greenBlend = Mathf.SmoothStep(1f, 0f,
                Mathf.InverseLerp(_hole.GreenRadius * 0.74f, _hole.GreenRadius + CollarBand + 5f, greenDistance));
            if (greenBlend > 0f)
            {
                float crown = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0f, _hole.GreenRadius, greenDistance));
                float greenHeight = CorridorHeight(_hole.GreenCenter)
                                    + GreenLift
                                    + crown * GreenCrown
                                    + Perlin(p, 0.09f) * 0.30f;
                height = Mathf.Lerp(height, greenHeight, greenBlend);
            }

            // Tee: a flat shelf standing above the corridor, so the opening view
            // looks down the hole instead of along the grass.
            float teeBlend = Mathf.SmoothStep(1f, 0f,
                Mathf.InverseLerp(TeeRadius, TeeRadius + 5f, Vector2.Distance(p, _hole.TeePosition)));
            if (teeBlend > 0f)
            {
                height = Mathf.Lerp(height, CorridorHeight(_hole.TeePosition) + TeeLift, teeBlend);
            }

            return height;
        }

        /// <summary>Final rendered/collided height including hazard excavation.</summary>
        public float SampleHeight(Vector2 p)
        {
            float height = SampleGroundHeight(p);

            // Warped to match SampleSurface, so the hollow and the sand stay aligned.
            Vector2 bp = WarpEdge(p);
            foreach (CircleRegion bunker in _hole.Bunkers)
            {
                float d = Vector2.Distance(bp, bunker.Center);
                float edge = BunkerEdge(bunker, bp);
                if (d < edge)
                {
                    float t = 1f - Mathf.SmoothStep(0f, 1f, d / edge);
                    height -= BunkerDepth * t;
                }
                else if (d < edge * BunkerLipOuter)
                {
                    // Sand thrown up outside the hollow. Zero at the sand's edge so it
                    // joins the bowl without a step, and it is what makes a bunker read
                    // as excavated from the fairway rather than painted onto it.
                    float t = 1f - Mathf.InverseLerp(edge, edge * BunkerLipOuter, d);
                    height += BunkerLipHeight * Mathf.SmoothStep(0f, 1f, t);
                }
            }

            foreach (CircleRegion water in _hole.Water)
            {
                float d = Vector2.Distance(p, water.Center);
                if (d < water.Radius)
                {
                    float t = 1f - Mathf.SmoothStep(0f, 1f, d / water.Radius);
                    height -= WaterBedDepth * t;
                }
            }

            return height;
        }

        public SurfaceType SampleSurface(Vector2 p)
        {
            if (!_hole.PlotBounds.Contains(p))
            {
                return SurfaceType.OutOfBounds;
            }

            foreach (CircleRegion water in _hole.Water)
            {
                if (water.Contains(p))
                {
                    return SurfaceType.Water;
                }
            }

            // The heart of the green is green whatever the warp below does, which is
            // what keeps the cup from ever landing off the putting surface.
            if (Vector2.Distance(p, _hole.GreenCenter) <= _hole.GreenRadius * 0.82f)
            {
                return SurfaceType.Green;
            }

            // Everything past here classifies against a displaced sample point.
            // Reading the raw position on a regular grid is exactly what produces
            // the axis-aligned staircase along every surface boundary; nudging the
            // sample by about a metre of noise turns those steps into a ragged edge
            // without adding a single triangle. Gameplay reads this same function,
            // so the lie the ball gets still matches the surface that is drawn.
            Vector2 w = WarpEdge(p);

            // The wobble only ever grows the green.
            float greenEdge = _hole.GreenRadius * (1f + (Perlin(w, 0.04f) * 0.5f + 0.5f) * 0.08f);
            if (Vector2.Distance(w, _hole.GreenCenter) <= greenEdge)
            {
                return SurfaceType.Green;
            }

            foreach (CircleRegion bunker in _hole.Bunkers)
            {
                if (Vector2.Distance(w, bunker.Center) <= BunkerEdge(bunker, w))
                {
                    return SurfaceType.Bunker;
                }
            }

            // Fringe around the putting surface: cut longer than the green, shorter
            // than the approach. Tested after the bunkers so greenside sand still
            // cuts into it, and it reads as the boundary a real green has.
            float collarEdge = greenEdge + CollarBand + Perlin(w, 0.03f) * 0.8f;
            if (Vector2.Distance(w, _hole.GreenCenter) <= collarEdge)
            {
                return SurfaceType.Collar;
            }

            if (Vector2.Distance(p, _hole.TeePosition) <= TeeRadius)
            {
                return SurfaceType.TeeBox;
            }

            float distance = _hole.DistanceToCenterline(w);
            float half = FairwayHalfWidth(w);
            if (distance <= half)
            {
                return SurfaceType.Fairway;
            }

            float roughEdge = half + RoughBand + Perlin(w, 0.008f) * 5f;
            return distance <= roughEdge ? SurfaceType.Rough : SurfaceType.DeepRough;
        }

        /// <summary>
        /// Half-width of the mown corridor at a point. A broad wander plus a fine
        /// ripple, so the mown edge meanders the way a real fairway does rather than
        /// running as a perfect constant band. Shared by the surface classification
        /// and the height grading, so the ground is graded along the edge that is
        /// actually drawn.
        /// </summary>
        private float FairwayHalfWidth(Vector2 warped) =>
            _hole.FairwayWidth * 0.5f + Perlin(warped, 0.011f) * 3.4f + Perlin(warped, 0.048f) * 0.9f;

        /// <summary>
        /// 1 on the mown fairway, falling to 0 out in the rough. Drives how much
        /// mounding is allowed to build at a point.
        /// </summary>
        private float FairwayWeight(Vector2 p)
        {
            Vector2 w = WarpEdge(p);
            float half = FairwayHalfWidth(w);
            return Mathf.SmoothStep(1f, 0f,
                Mathf.InverseLerp(half, half + CorridorBlendBand, _hole.DistanceToCenterline(w)));
        }

        /// <summary>
        /// Short-wavelength domain warp used to break surface boundaries off the
        /// sampling grid. The amplitude is deliberately close to one terrain cell:
        /// big enough to dissolve the staircase, small enough that a fairway edge
        /// still reads as a fairway edge.
        /// </summary>
        private Vector2 WarpEdge(Vector2 p)
        {
            const float amplitude = 1.35f;
            const float scale = 0.24f;
            float wx = Perlin(p + new Vector2(137.2f, 11.7f), scale);
            float wy = Perlin(p + new Vector2(-19.4f, 251.3f), scale);
            return p + new Vector2(wx, wy) * amplitude;
        }

        /// <summary>
        /// Bunker outline with an irregular, hand-raked edge. Excavation and
        /// surface classification share it so the sand always matches the hollow.
        /// </summary>
        private float BunkerEdge(CircleRegion bunker, Vector2 p) =>
            bunker.Radius * (1f + Perlin(p, 0.055f) * 0.18f);

        public float WaterSurfaceHeight(CircleRegion water) =>
            SampleGroundHeight(water.Center) - WaterSurfaceDrop;

        public Vector3 ToWorld(Vector2 p) => new(p.x, SampleHeight(p), p.y);

        public Vector3 SurfaceNormal(Vector2 p)
        {
            const float e = 0.6f;
            float hL = SampleHeight(p + Vector2.left * e);
            float hR = SampleHeight(p + Vector2.right * e);
            float hD = SampleHeight(p + Vector2.down * e);
            float hU = SampleHeight(p + Vector2.up * e);
            return new Vector3(hL - hR, 2f * e, hD - hU).normalized;
        }

        /// <summary>
        /// Smooth elevation profile running the length of the hole, so the
        /// corridor rises and falls gently instead of sitting on a flat plane.
        /// </summary>
        private float CorridorHeight(Vector2 p)
        {
            return Perlin(p, 0.0045f) * 7f;
        }

        private float Perlin(Vector2 p, float scale)
        {
            float x = (p.x + _noiseOffsetX) * scale;
            float y = (p.y + _noiseOffsetZ) * scale;
            return Mathf.PerlinNoise(x, y) * 2f - 1f;
        }
    }
}
