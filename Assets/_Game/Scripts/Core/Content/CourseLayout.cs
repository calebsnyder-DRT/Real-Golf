using System;
using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>
    /// Course-wide layout shared by every hole: the cart path network that links each green to the next tee, the ribbon
    /// polygons the solver can read as <see cref="SurfaceId.CartPath"/>, and the routes the between-hole cart drive follows.
    /// All holes share one world frame, so the paths are authored once in world metres (course.json, <see cref="CourseData"/>).
    /// The network's first line is the closed loop round holes 3-5; further lines are spurs joined to it. The loop-only
    /// members (<see cref="CartPath"/>, <see cref="CartPathChunks"/>, <see cref="LoopSide"/>, <see cref="Ribbon"/>,
    /// <see cref="Nearest"/>) keep their original meaning; <see cref="Lines"/> and the Network* members cover every line.
    /// </summary>
    public static class CourseLayout
    {
        public const double CartPathHalfWidthM = 1.3;
        public const int CartPathSamplesPerSpan = 12;
        public const int RibbonChunkSamples = 24;
        /// <summary>The cart parks this far from the tee centre, on the cart path side.</summary>
        public const double TeeParkDistanceM = 11.0;

        public static CartPathNetwork Network => CourseData.Load().CartPaths;

        /// <summary>Every line of the network; index 0 is the loop.</summary>
        public static IReadOnlyList<CartPathLine> Lines => Network.Lines;

        /// <summary>Control points of the closed loop, in play order round holes 3, 4 and 5.</summary>
        public static IReadOnlyList<Vec2d> CartPathControl => Network.Loop.Control;

        /// <summary>Centre line of the closed cart path loop (last point connects back to the first).</summary>
        public static IReadOnlyList<Vec2d> CartPath => Network.Loop.Samples;

        /// <summary>
        /// The loop cut into consecutive open stretches of <see cref="RibbonChunkSamples"/> segments, as loop sample indices
        /// (the last chunk wraps to index 0). Neighbours share their end sample so the ribbons tile without gaps.
        /// </summary>
        public static List<int[]> CartPathChunks() => Network.Loop.Chunks();

        /// <summary>Unit vector to the right of the loop (looking along it) at loop sample <paramref name="k"/>.</summary>
        public static Vec2d LoopSide(int k) => Network.Loop.Side(k);

        /// <summary>Closed polygon of a stretch of the loop widened to the given half width (left edge out, right edge back).</summary>
        public static List<Vec2d> Ribbon(IReadOnlyList<int> chunk, double halfWidth) => Network.Loop.Ribbon(chunk, halfWidth);

        /// <summary>Path stretches near a hole's playable area, as the hole's cart-path surface regions.</summary>
        public static void AddCartPathSurfaces(HoleDefinition hole, int priority) => Network.AddSurfaces(hole, priority);

        /// <summary>
        /// Route for the drive from one hole to the next: from the path beside the finished green, along the network by the
        /// shortest way, then off it to park beside the next tee.
        /// </summary>
        public static List<Vec2d> TransitionRoute(HoleDefinition from, HoleDefinition to) => Network.TransitionRoute(from, to);

        /// <summary>Where the cart waits beside a hole's tee: toward the nearest point of the network.</summary>
        public static Vec2d TeePark(HoleDefinition hole) => Network.TeePark(hole);

        public static double Length(IReadOnlyList<Vec2d> line)
        {
            double total = 0;
            for (int i = 1; i < line.Count; i++) total += Distance(line[i - 1], line[i]);
            return total;
        }

        /// <summary>Index of the loop sample closest to the point.</summary>
        public static int Nearest(double x, double z) => Network.Loop.Nearest(x, z);

        /// <summary>Closest sample of any line of the network.</summary>
        public static Vec2d NetworkNearest(double x, double z) => Network.NearestPoint(x, z);

        /// <summary>Unit direction of the network line closest to the point, along its sample order.</summary>
        public static Vec2d NetworkHeading(double x, double z) => Network.Heading(x, z);

        internal static double Distance(Vec2d a, Vec2d b)
        {
            double dx = a.x - b.x, dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        internal static void Bounds(IReadOnlyList<Vec2d> poly, out double minX, out double minZ, out double maxX, out double maxZ)
        {
            minX = minZ = double.MaxValue;
            maxX = maxZ = double.MinValue;
            foreach (Vec2d p in poly)
            {
                minX = Math.Min(minX, p.x);
                maxX = Math.Max(maxX, p.x);
                minZ = Math.Min(minZ, p.z);
                maxZ = Math.Max(maxZ, p.z);
            }
        }
    }

    /// <summary>One cart path centre line: a closed loop or an open spur whose ends may join other lines.</summary>
    public sealed class CartPathLine
    {
        public readonly string Id;
        public readonly bool Closed;
        public readonly IReadOnlyList<Vec2d> Control;
        /// <summary>Line the first / last sample connects to (at that line's nearest sample); null for a free end.</summary>
        public readonly string JoinStart, JoinEnd;
        private readonly List<Vec2d> _samples;

        public CartPathLine(string id, bool closed, IReadOnlyList<Vec2d> control, string joinStart = null, string joinEnd = null)
        {
            Id = id;
            Closed = closed;
            Control = control;
            JoinStart = joinStart;
            JoinEnd = joinEnd;
            if (closed && (joinStart != null || joinEnd != null)) throw new FormatException($"Cart path '{id}': a closed loop has no ends to join.");
            if (control.Count < (closed ? 3 : 2)) throw new FormatException($"Cart path '{id}' needs more control points.");
            _samples = closed
                ? Shapes.CatmullRomLoop(control, CourseLayout.CartPathSamplesPerSpan)
                : Shapes.CatmullRom(control, CourseLayout.CartPathSamplesPerSpan);
        }

        public IReadOnlyList<Vec2d> Samples => _samples;

        /// <summary>Stretches of <see cref="CourseLayout.RibbonChunkSamples"/> segments as sample indices (wrapping on a loop).</summary>
        public List<int[]> Chunks()
        {
            var chunks = new List<int[]>();
            int n = _samples.Count;
            int last = Closed ? n : n - 1;
            for (int start = 0; start < last; start += CourseLayout.RibbonChunkSamples)
            {
                int end = Math.Min(start + CourseLayout.RibbonChunkSamples, last);
                var chunk = new int[end - start + 1];
                for (int i = 0; i < chunk.Length; i++) chunk[i] = (start + i) % n;
                chunks.Add(chunk);
            }
            return chunks;
        }

        /// <summary>Unit vector to the right of the line (looking along it) at sample <paramref name="k"/>.</summary>
        public Vec2d Side(int k)
        {
            int n = _samples.Count;
            Vec2d a = Closed ? _samples[(k + n - 1) % n] : _samples[Math.Max(0, k - 1)];
            Vec2d b = Closed ? _samples[(k + 1) % n] : _samples[Math.Min(n - 1, k + 1)];
            double tx = b.x - a.x, tz = b.z - a.z;
            double len = Math.Sqrt(tx * tx + tz * tz);
            if (len < 1e-9) return new Vec2d(1, 0);
            return new Vec2d(tz / len, -tx / len);
        }

        public List<Vec2d> Ribbon(IReadOnlyList<int> chunk, double halfWidth)
        {
            var left = new List<Vec2d>(chunk.Count * 2);
            var right = new List<Vec2d>(chunk.Count);
            foreach (int k in chunk)
            {
                Vec2d p = _samples[k], side = Side(k);
                left.Add(new Vec2d(p.x - side.x * halfWidth, p.z - side.z * halfWidth));
                right.Add(new Vec2d(p.x + side.x * halfWidth, p.z + side.z * halfWidth));
            }
            right.Reverse();
            left.AddRange(right);
            return left;
        }

        public int Nearest(double x, double z) => Nearest(x, z, out _);

        public int Nearest(double x, double z, out double distanceSq)
        {
            int best = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i < _samples.Count; i++)
            {
                double dx = _samples[i].x - x, dz = _samples[i].z - z;
                double d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            distanceSq = bestD;
            return best;
        }
    }

    /// <summary>
    /// The cart path lines as one graph (consecutive samples, loop wrap, and each joined end to its partner's nearest
    /// sample). Drives between two holes served by the loop keep the original loop-only route; any other drive takes the
    /// shortest way across the graph.
    /// </summary>
    public sealed class CartPathNetwork
    {
        public readonly IReadOnlyList<CartPathLine> Lines;
        private readonly int[] _offset;
        private readonly List<Vec2d> _nodes = new List<Vec2d>();
        private readonly List<List<(int to, double length)>> _edges = new List<List<(int, double)>>();

        public CartPathNetwork(IReadOnlyList<CartPathLine> lines)
        {
            if (lines == null || lines.Count == 0 || !lines[0].Closed) throw new FormatException("The first cart path must be the closed loop.");
            Lines = lines;
            _offset = new int[lines.Count];
            var ids = new HashSet<string>();
            for (int l = 0; l < lines.Count; l++)
            {
                if (!ids.Add(lines[l].Id)) throw new FormatException($"Duplicate cart path id '{lines[l].Id}'.");
                _offset[l] = _nodes.Count;
                foreach (Vec2d p in lines[l].Samples)
                {
                    _nodes.Add(p);
                    _edges.Add(new List<(int, double)>());
                }
            }
            for (int l = 0; l < lines.Count; l++)
            {
                CartPathLine line = lines[l];
                int n = line.Samples.Count;
                for (int k = 0; k < n - 1; k++) Link(_offset[l] + k, _offset[l] + k + 1);
                if (line.Closed) Link(_offset[l] + n - 1, _offset[l]);
                if (line.JoinStart != null) Join(_offset[l], line.JoinStart, line.Id);
                if (line.JoinEnd != null) Join(_offset[l] + n - 1, line.JoinEnd, line.Id);
            }
        }

        public CartPathLine Loop => Lines[0];

        public void AddSurfaces(HoleDefinition hole, int priority)
        {
            CourseLayout.Bounds(hole.PlayableBoundary, out double minX, out double minZ, out double maxX, out double maxZ);
            const double pad = 6;
            for (int l = 0; l < Lines.Count; l++)
            {
                List<int[]> chunks = Lines[l].Chunks();
                for (int c = 0; c < chunks.Count; c++)
                {
                    List<Vec2d> ribbon = Lines[l].Ribbon(chunks[c], CourseLayout.CartPathHalfWidthM);
                    CourseLayout.Bounds(ribbon, out double rx0, out double rz0, out double rx1, out double rz1);
                    if (rx1 < minX - pad || rx0 > maxX + pad || rz1 < minZ - pad || rz0 > maxZ + pad) continue;
                    string id = l == 0 ? $"cart-path-{c + 1}" : $"cart-path-{Lines[l].Id}-{c + 1}";
                    hole.Surfaces.Add(new SurfaceRegion { Id = id, Surface = SurfaceId.CartPath, Priority = priority, Polygon = ribbon });
                }
            }
        }

        public List<Vec2d> TransitionRoute(HoleDefinition from, HoleDefinition to)
        {
            int a = NearestNode(from.CupM.x, from.CupM.z);
            int b = NearestNode(to.TeeM.x, to.TeeM.z);
            List<Vec2d> route = OnLoop(a) && OnLoop(b) ? LoopRoute(a, b) : GraphRoute(a, b);
            Vec2d park = TeePark(to);
            Vec2d last = route[route.Count - 1];
            if (CourseLayout.Distance(park, last) > 1.0) route.Add(park);
            return route;
        }

        public Vec2d TeePark(HoleDefinition hole)
        {
            Vec2d tee = new Vec2d(hole.TeeM.x, hole.TeeM.z);
            Vec2d onPath = _nodes[NearestNode(tee.x, tee.z)];
            double d = CourseLayout.Distance(tee, onPath);
            if (d <= CourseLayout.TeeParkDistanceM + 1.5) return onPath;
            return new Vec2d(tee.x + (onPath.x - tee.x) / d * CourseLayout.TeeParkDistanceM, tee.z + (onPath.z - tee.z) / d * CourseLayout.TeeParkDistanceM);
        }

        public Vec2d NearestPoint(double x, double z) => _nodes[NearestNode(x, z)];

        public Vec2d Heading(double x, double z)
        {
            int node = NearestNode(x, z);
            int l = LineOf(node);
            CartPathLine line = Lines[l];
            Vec2d side = line.Side(node - _offset[l]);
            return new Vec2d(-side.z, side.x);
        }

        /// <summary>Closest sample over every line; ties keep the earlier line, so the loop wins where lines meet.</summary>
        private int NearestNode(double x, double z)
        {
            int best = 0;
            double bestD = double.MaxValue;
            for (int l = 0; l < Lines.Count; l++)
            {
                int k = Lines[l].Nearest(x, z, out double d);
                if (d < bestD)
                {
                    bestD = d;
                    best = _offset[l] + k;
                }
            }
            return best;
        }

        private bool OnLoop(int node) => node < Loop.Samples.Count;

        private int LineOf(int node)
        {
            int l = Lines.Count - 1;
            while (_offset[l] > node) l--;
            return l;
        }

        /// <summary>The original loop drive: along the loop the shorter way round in samples.</summary>
        private List<Vec2d> LoopRoute(int a, int b)
        {
            IReadOnlyList<Vec2d> loop = Loop.Samples;
            int n = loop.Count;
            int forward = (b - a + n) % n;
            int step = forward <= n - forward ? 1 : -1;
            var route = new List<Vec2d>();
            for (int i = a; ; i = (i + step + n) % n)
            {
                route.Add(loop[i]);
                if (i == b) break;
            }
            return route;
        }

        private List<Vec2d> GraphRoute(int a, int b)
        {
            int n = _nodes.Count;
            var dist = new double[n];
            var prev = new int[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = double.MaxValue;
                prev[i] = -1;
            }
            dist[a] = 0;
            var open = new SortedSet<(double d, int node)>();
            open.Add((0, a));
            while (open.Count > 0)
            {
                (double d, int u) = open.Min;
                open.Remove(open.Min);
                if (u == b) break;
                foreach ((int v, double w) in _edges[u])
                {
                    double nd = d + w;
                    if (nd >= dist[v]) continue;
                    if (dist[v] < double.MaxValue) open.Remove((dist[v], v));
                    dist[v] = nd;
                    prev[v] = u;
                    open.Add((nd, v));
                }
            }
            if (dist[b] == double.MaxValue) throw new InvalidOperationException("The cart path network is not connected.");
            var route = new List<Vec2d>();
            for (int v = b; v >= 0; v = prev[v]) route.Add(_nodes[v]);
            route.Reverse();
            return route;
        }

        private void Join(int node, string lineId, string from)
        {
            int l = -1;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].Id == lineId) l = i;
            }
            if (l < 0) throw new FormatException($"Cart path '{from}' joins unknown path '{lineId}'.");
            Vec2d p = _nodes[node];
            Link(node, _offset[l] + Lines[l].Nearest(p.x, p.z));
        }

        private void Link(int u, int v)
        {
            double w = CourseLayout.Distance(_nodes[u], _nodes[v]);
            _edges[u].Add((v, w));
            _edges[v].Add((u, w));
        }
    }
}
