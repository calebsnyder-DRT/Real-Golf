using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>
    /// Data-driven course content: <c>&lt;course&gt;/course.json</c> (the shared world frame: coast paths and shelves, cart
    /// path network, hole order, round routing) plus one <c>&lt;course&gt;/Holes/&lt;hole id&gt;.json</c> per hole, under
    /// <see cref="ProjectFolder"/> (a Resources folder, so players ship the same files). Shapes are authored as parameters
    /// (rect, ellipse, Catmull-Rom loop, polygon, strips and sea areas cut from a course path) and expanded with
    /// <see cref="Shapes"/> exactly as the hand-written C# did, so content hashes are stable. Blender reads the expanded
    /// result from ArtSource/Blender/pebble_course.json (Puttshack/Export Course Layout For Blender); the format is
    /// described in ArtSource/Course/README.md.
    /// </summary>
    public static class CourseData
    {
        public const int Format = 1;
        public const string DefaultCourse = "Pebble";
        public const string ProjectFolder = "Assets/_Game/Resources/Course";

        /// <summary>
        /// Reads a content file by its path below <see cref="ProjectFolder"/> (e.g. <c>Pebble/course.json</c>). The default
        /// reads the project folder relative to the working directory (editor, tests, command-line tools); players install
        /// a Resources reader before the first load.
        /// </summary>
        public static Func<string, string> ReadText = ReadProjectFile;

        private static readonly Dictionary<string, CourseSpec> Cache = new Dictionary<string, CourseSpec>();

        public static CourseSpec Load(string course = DefaultCourse)
        {
            if (!Cache.TryGetValue(course, out CourseSpec spec))
            {
                spec = CourseSpec.Parse(course, ReadText);
                Cache[course] = spec;
            }
            return spec;
        }

        /// <summary>Parses a course through the given reader, bypassing the cache (tests, tools, alternative frames).</summary>
        public static CourseSpec LoadFrom(string course, Func<string, string> read) => CourseSpec.Parse(course, read);

        /// <summary>Drops parsed content so the next <see cref="Load"/> re-reads the files (after editing them in the editor).</summary>
        public static void Invalidate() => Cache.Clear();

        public static string ReadProjectFile(string relativePath)
        {
            string path = Path.Combine(ProjectFolder, relativePath);
            if (!File.Exists(path)) throw new FileNotFoundException($"Course content file not found: {Path.GetFullPath(path)}", path);
            return File.ReadAllText(path);
        }
    }

    /// <summary>A parsed course: world-frame data plus the raw hole files, expanded into fresh definitions on every <see cref="Build"/>.</summary>
    public sealed class CourseSpec
    {
        public string Name { get; private set; }
        public string Id { get; private set; }
        public string Version { get; private set; }
        public string DisplayName { get; private set; }
        public double SeaLevelM { get; private set; }
        public IReadOnlyList<int> ProposedPars { get; private set; }
        public IReadOnlyList<string> HoleIds => _holeIds;
        /// <summary>Layout id played at each hole number of a full round (index = number - 1), see <see cref="CourseRouting"/>.</summary>
        public IReadOnlyList<string> Routing { get; private set; }
        public CartPathNetwork CartPaths { get; private set; }

        private readonly Dictionary<string, CoursePath> _paths = new Dictionary<string, CoursePath>();
        private readonly List<string> _pathOrder = new List<string>();
        private readonly List<ShelfSpec> _shelves = new List<ShelfSpec>();
        private readonly List<string> _holeIds = new List<string>();
        private readonly Dictionary<string, JObject> _holes = new Dictionary<string, JObject>();

        /// <summary>
        /// One or more open Catmull-Rom pieces end to end (each piece starts on the previous one's last control point).
        /// Pieces sample independently, so extending a path with a new piece leaves the existing samples bit-identical.
        /// </summary>
        private sealed class CoursePath
        {
            public List<Vec2d> Control;
            public List<Vec2d> Sampled;
        }

        private sealed class ShelfSpec
        {
            public string Path;
            public double Inner, Outer, Height;
        }

        /// <summary>Course paths (coast lines) by name, in file order.</summary>
        public IReadOnlyList<string> PathNames => _pathOrder;

        /// <summary>Control points of a course path, every piece's in order (shared junction points once).</summary>
        public IReadOnlyList<Vec2d> PathControl(string name) => GetPath(name, "course").Control;

        /// <summary>The sampled polyline of a course path (each piece an open Catmull-Rom of its control points); a fresh copy.</summary>
        public List<Vec2d> Path(string name) => new List<Vec2d>(GetPath(name, "course").Sampled);

        internal static CourseSpec Parse(string course, Func<string, string> read)
        {
            string file = $"{course}/course.json";
            var spec = new CourseSpec { Name = course };
            var root = new JsonObj(Json.Parse(read(file), file), file);
            int format = root.Int("Format");
            if (format != CourseData.Format) throw root.Error($"unsupported format {format} (expected {CourseData.Format})");
            spec.Id = root.Str("Id");
            spec.Version = root.Str("Version");
            spec.DisplayName = root.Str("Name");
            spec.SeaLevelM = root.Num("SeaLevelM");
            spec.ProposedPars = root.Ints("ProposedPars");

            JsonObj paths = root.Obj("Paths");
            foreach (string name in paths.Keys())
            {
                JsonObj p = paths.Obj(name);
                var path = new CoursePath { Control = new List<Vec2d>(), Sampled = new List<Vec2d>() };
                List<JsonObj> pieces = p.Has("Pieces") ? p.Objs("Pieces") : new List<JsonObj> { p };
                foreach (JsonObj piece in pieces)
                {
                    int samples = piece.Int("Samples");
                    List<Vec2d> control = piece.Points("Control");
                    if (control.Count < 2 || samples < 1) throw piece.Error("a path piece needs two control points and one sample per span");
                    List<Vec2d> sampled = Shapes.CatmullRom(control, samples);
                    if (path.Control.Count > 0)
                    {
                        Vec2d end = path.Control[path.Control.Count - 1];
                        if (end.x != control[0].x || end.z != control[0].z) throw piece.Error("a path piece must start on the previous piece's last control point");
                        control.RemoveAt(0);
                        sampled.RemoveAt(0);
                    }
                    path.Control.AddRange(control);
                    path.Sampled.AddRange(sampled);
                    if (piece != p) piece.Done();
                }
                p.Done();
                spec._paths[name] = path;
                spec._pathOrder.Add(name);
            }
            paths.Done();

            foreach (JsonObj s in root.Objs("Shelves"))
            {
                var shelf = new ShelfSpec { Path = s.Str("Path"), Inner = s.Num("Inner"), Outer = s.Num("Outer"), Height = s.Num("Height") };
                spec.GetPath(shelf.Path, s.Where);
                spec._shelves.Add(shelf);
                s.Done();
            }

            var lines = new List<CartPathLine>();
            foreach (JsonObj c in root.Objs("CartPaths"))
            {
                lines.Add(new CartPathLine(c.Str("Id"), c.Bool("Closed"), c.Points("Control"), c.OptStr("JoinStart"), c.OptStr("JoinEnd")));
                c.Done();
            }
            spec.CartPaths = new CartPathNetwork(lines);

            foreach (string id in root.Strs("Holes"))
            {
                string holeFile = $"{course}/Holes/{id}.json";
                JObject hole = Json.Parse(read(holeFile), holeFile);
                string declared = hole.Value<string>("Id");
                if (declared != id) throw new FormatException($"{holeFile}: Id '{declared}' does not match the file name.");
                if (spec._holes.ContainsKey(id)) throw new FormatException($"{file}: hole '{id}' is listed twice.");
                spec._holes[id] = hole;
                spec._holeIds.Add(id);
            }
            spec.Routing = root.Strs("Routing");
            foreach (string id in spec.Routing)
            {
                if (!spec._holes.ContainsKey(id)) throw root.Error($"Routing names unknown hole '{id}'");
            }
            root.Done();
            return spec;
        }

        /// <summary>Every hole of the course, sealed (<see cref="ContentHasher.Seal"/>).</summary>
        public CourseDefinition Build()
        {
            var course = new CourseDefinition { Id = Id, Version = Version, Name = DisplayName };
            foreach (string id in _holeIds) course.Holes.Add(BuildHole(id));
            ContentHasher.Seal(course);
            return course;
        }

        /// <summary>One hole on its own (unsealed: its <see cref="HoleDefinition.ContentHash"/> is null).</summary>
        public HoleDefinition BuildHole(string id)
        {
            if (!_holes.TryGetValue(id, out JObject source)) throw new KeyNotFoundException($"Course '{Name}' has no hole '{id}'.");
            return BuildHole(source, $"{Name}/Holes/{id}.json");
        }

        /// <summary>
        /// The one heightfield every hole shares: each hole's shaping features (plateaus, tilts, free bowls) in hole order,
        /// then every bunker bowl in hole and surface order, then the coast shelves. Additive features with no reach at a
        /// point are skipped by <see cref="TerrainModel"/>, so a hole's heights only change where new features reach it.
        /// </summary>
        public List<TerrainFeature> Terrain()
        {
            var terrain = new List<TerrainFeature>();
            foreach (string id in _holeIds)
            {
                string where = $"{Name}/Holes/{id}.json";
                var hole = new JsonObj(_holes[id], where);
                foreach (JsonObj f in hole.Objs("Terrain")) terrain.Add(Feature(f));
            }
            foreach (string id in _holeIds)
            {
                string where = $"{Name}/Holes/{id}.json";
                var hole = new JsonObj(_holes[id], where);
                foreach (JsonObj s in hole.Objs("Surfaces"))
                {
                    if (!s.Has("Bowl")) continue;
                    JsonObj bowl = s.Obj("Bowl");
                    terrain.Add(new TerrainFeature
                    {
                        Kind = TerrainFeatureKind.Bowl,
                        InnerRadius = bowl.Num("Inner"),
                        Height = bowl.Num("Height"),
                        Path = Shape(s.Obj("Shape")),
                    });
                    bowl.Done();
                }
            }
            foreach (ShelfSpec shelf in _shelves)
            {
                terrain.Add(new TerrainFeature
                {
                    Kind = TerrainFeatureKind.Shelf,
                    InnerRadius = shelf.Inner,
                    OuterRadius = shelf.Outer,
                    Height = shelf.Height,
                    Path = Path(shelf.Path),
                });
            }
            return terrain;
        }

        private HoleDefinition BuildHole(JObject source, string where)
        {
            var h = new JsonObj(source, where);
            var hole = new HoleDefinition
            {
                Id = h.Str("Id"),
                Version = h.Str("Version"),
                Name = h.Str("Name"),
                Number = h.Int("Number"),
                Par = h.Int("Par"),
                YardageYd = h.Int("YardageYd"),
                DefaultSurface = h.Enum<SurfaceId>("DefaultSurface"),
                WindMps = h.V3("WindMps"),
            };
            JsonObj briefing = h.Obj("Briefing");
            hole.Briefing = new HoleBriefing
            {
                Hazard = briefing.Str("Hazard"),
                SafePlan = briefing.Str("SafePlan"),
                SuggestedClubId = briefing.Str("SuggestedClubId"),
            };
            briefing.Done();

            hole.Terrain = Terrain();
            h.Objs("Terrain");

            foreach (JsonObj s in h.Objs("Surfaces"))
            {
                if (s.Has("CartPaths"))
                {
                    JsonObj cart = s.Obj("CartPaths");
                    // The cart path regions clip to the playable boundary, which is read below; add them afterwards in place.
                    hole.Surfaces.Add(new SurfaceRegion { Id = CartPathMarker, Priority = cart.Int("Priority") });
                    cart.Done();
                    s.Done();
                    continue;
                }
                hole.Surfaces.Add(new SurfaceRegion
                {
                    Id = s.Str("Id"),
                    Surface = s.Enum<SurfaceId>("Surface"),
                    Priority = s.Int("Priority"),
                    Polygon = Shape(s.Obj("Shape")),
                });
                if (s.Has("Bowl")) s.Obj("Bowl");
                s.Done();
            }

            foreach (JsonObj z in h.Objs("Hazards"))
            {
                var hazard = new HazardVolume { Id = z.Str("Id"), DropPointId = z.Str("DropPointId"), Polygon = Shape(z.Obj("Shape")) };
                if (z.Has("WaterLevelM"))
                {
                    JToken level = z.Raw("WaterLevelM");
                    if (level.Type == JTokenType.String && (string)level == "Sea") hazard.WaterLevelM = SeaLevelM;
                    else hazard.WaterLevelM = Json.ToDouble(level, $"{z.Where}.WaterLevelM");
                }
                hole.Hazards.Add(hazard);
                z.Done();
            }
            foreach (JsonObj d in h.Objs("Drops"))
            {
                hole.Drops.Add(new DropPoint { Id = d.Str("Id"), HazardId = d.Str("HazardId"), Position = d.V2("At") });
                d.Done();
            }
            foreach (JsonObj t in h.Objs("Trees"))
            {
                Vec2d trunk = t.V2("Trunk"), canopy = t.V2("Canopy");
                hole.Trees.Add(new TreeObstacle
                {
                    Id = t.Str("Id"),
                    Position = t.V2("At"),
                    TrunkRadius = trunk.x,
                    TrunkHeight = trunk.z,
                    CanopyRadius = canopy.x,
                    CanopyHeight = canopy.z,
                });
                t.Done();
            }

            hole.PlayableBoundary = Shape(h.Obj("PlayableBoundary"));

            int marker = hole.Surfaces.FindIndex(r => r.Id == CartPathMarker);
            if (marker >= 0)
            {
                int priority = hole.Surfaces[marker].Priority;
                var tail = hole.Surfaces.GetRange(marker + 1, hole.Surfaces.Count - marker - 1);
                hole.Surfaces.RemoveRange(marker, hole.Surfaces.Count - marker);
                CartPaths.AddSurfaces(hole, priority);
                hole.Surfaces.AddRange(tail);
            }

            Vec2d tee = h.V2("Tee"), cup = h.V2("Cup");
            hole.TeeM = OnGround(hole, tee);
            hole.CupM = OnGround(hole, cup);
            hole.PlayRouteM.Add(hole.TeeM);
            foreach (Vec2d waypoint in h.Points("Route")) hole.PlayRouteM.Add(OnGround(hole, waypoint));
            hole.PlayRouteM.Add(hole.CupM);
            h.Done();
            return hole;
        }

        private const string CartPathMarker = "\u0000cart-paths";

        private TerrainFeature Feature(JsonObj f)
        {
            string kind = f.Str("Kind");
            TerrainFeature feature;
            switch (kind)
            {
                case "Plateau":
                    feature = new TerrainFeature
                    {
                        Kind = TerrainFeatureKind.Plateau, Center = f.V2("Center"), InnerRadius = f.Num("Inner"), OuterRadius = f.Num("Outer"), Height = f.Num("Height"),
                    };
                    break;
                case "Tilt":
                    feature = new TerrainFeature
                    {
                        Kind = TerrainFeatureKind.Tilt, Center = f.V2("Center"), InnerRadius = f.Num("Inner"), OuterRadius = f.Num("Outer"), Slope = f.V2("Slope"),
                    };
                    break;
                case "Bowl":
                    feature = new TerrainFeature { Kind = TerrainFeatureKind.Bowl, InnerRadius = f.Num("Inner"), Height = f.Num("Height"), Path = Shape(f.Obj("Shape")) };
                    break;
                default:
                    throw f.Error($"unknown terrain feature kind '{kind}' (Plateau, Tilt or Bowl; coast shelves live in course.json)");
            }
            f.Done();
            return feature;
        }

        private List<Vec2d> Shape(JsonObj s)
        {
            string kind = s.Str("Kind");
            List<Vec2d> shape;
            switch (kind)
            {
                case "Rect":
                {
                    Vec2d min = s.V2("Min"), max = s.V2("Max");
                    shape = Shapes.Rect(min.x, min.z, max.x, max.z);
                    break;
                }
                case "Ellipse":
                {
                    Vec2d c = s.V2("Center"), r = s.V2("Radii");
                    shape = Shapes.Ellipse(c.x, c.z, r.x, r.z, s.Int("Segments"), s.Has("Rotation") ? s.Num("Rotation") : 0);
                    break;
                }
                case "Loop":
                    shape = Shapes.CatmullRomLoop(s.Points("Control"), s.Int("Samples"));
                    break;
                case "Polygon":
                    shape = s.Points("Points");
                    break;
                case "InlandStrip":
                    shape = InlandStrip(GetPath(s.Str("Path"), s.Where).Sampled, s.Num("Width"), Range(s, "X"), Range(s, "Z"));
                    break;
                case "Sea":
                    shape = Sea(GetPath(s.Str("Path"), s.Where).Sampled, Range(s, "X"), Range(s, "Z"), s.Points("Close"));
                    break;
                default:
                    throw s.Error($"unknown shape kind '{kind}' (Rect, Ellipse, Loop, Polygon, InlandStrip or Sea)");
            }
            s.Done();
            if (shape.Count < 3) throw s.Error($"{kind} shape has {shape.Count} points");
            return shape;
        }

        /// <summary>Optional <c>Min&lt;axis&gt;</c>/<c>Max&lt;axis&gt;</c> limits (unbounded when absent).</summary>
        private static (double min, double max) Range(JsonObj s, string axis) =>
            (s.Has("Min" + axis) ? s.Num("Min" + axis) : double.NegativeInfinity, s.Has("Max" + axis) ? s.Num("Max" + axis) : double.PositiveInfinity);

        /// <summary>The sea seaward of a coast path: its points inside the limits (in path order), closed by the given far points.</summary>
        private static List<Vec2d> Sea(List<Vec2d> coast, (double min, double max) x, (double min, double max) z, List<Vec2d> close)
        {
            var sea = new List<Vec2d>();
            foreach (Vec2d p in coast)
            {
                if (p.x >= x.min && p.x <= x.max && p.z >= z.min && p.z <= z.max) sea.Add(p);
            }
            sea.AddRange(close);
            return sea;
        }

        /// <summary>Band of the given width on the inland (right) side of a path, limited to the given ranges.</summary>
        private static List<Vec2d> InlandStrip(List<Vec2d> path, double width, (double min, double max) x, (double min, double max) z)
        {
            var edge = new List<Vec2d>();
            var inner = new List<Vec2d>();
            for (int i = 0; i < path.Count; i++)
            {
                Vec2d p = path[i];
                if (p.z < z.min || p.z > z.max || p.x < x.min || p.x > x.max) continue;
                Vec2d a = path[Math.Max(0, i - 1)], b = path[Math.Min(path.Count - 1, i + 1)];
                double tx = b.x - a.x, tz = b.z - a.z;
                double len = Math.Sqrt(tx * tx + tz * tz);
                edge.Add(p);
                inner.Add(new Vec2d(p.x + tz / len * width, p.z - tx / len * width));
            }
            inner.Reverse();
            edge.AddRange(inner);
            return edge;
        }

        private static Vec3d OnGround(HoleDefinition hole, Vec2d p) => new Vec3d(p.x, TerrainModel.Height(hole.Terrain, p.x, p.z), p.z);

        private CoursePath GetPath(string name, string where)
        {
            if (!_paths.TryGetValue(name, out CoursePath path)) throw new FormatException($"{where}: unknown course path '{name}'.");
            return path;
        }
    }

    /// <summary>Strict JSON helpers: numbers convert exactly, unknown keys are errors, messages name the file and key.</summary>
    internal static class Json
    {
        public static JObject Parse(string text, string file)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(text)) { FloatParseHandling = FloatParseHandling.Decimal, DateParseHandling = DateParseHandling.None })
                {
                    if (JToken.ReadFrom(reader) is JObject o) return o;
                }
            }
            catch (JsonReaderException e)
            {
                throw new FormatException($"{file}: {e.Message}", e);
            }
            throw new FormatException($"{file}: expected a JSON object.");
        }

        private static readonly double[] Pow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        /// <summary>
        /// A JSON number as the double nearest its decimal text (what the C# compiler gives the same literal). Floats are
        /// read as decimals and divided once by an exact power of ten, which is correctly rounded on every runtime; Mono's
        /// double.Parse is not, and a one-ulp difference would change content hashes.
        /// </summary>
        public static double ToDouble(JToken token, string where)
        {
            if (token.Type == JTokenType.Integer)
            {
                long v = token.Value<long>();
                if (v > (1L << 53) || v < -(1L << 53)) throw new FormatException($"{where}: {v} is too large to be exact.");
                return v;
            }
            if (token.Type == JTokenType.Float && ((JValue)token).Value is decimal m)
            {
                int[] bits = decimal.GetBits(m);
                int scale = (bits[3] >> 16) & 0xFF;
                ulong mantissa = ((ulong)(uint)bits[1] << 32) | (uint)bits[0];
                if (bits[2] != 0 || mantissa >= 1UL << 53 || scale >= Pow10.Length)
                    throw new FormatException($"{where}: {m} has too many significant digits (keep authored numbers to 15).");
                double v = mantissa / Pow10[scale];
                return bits[3] < 0 ? -v : v;
            }
            throw new FormatException($"{where}: expected a number, found {token.Type}.");
        }
    }

    internal sealed class JsonObj
    {
        private readonly JObject _o;
        private readonly HashSet<string> _read = new HashSet<string>();
        public readonly string Where;

        public JsonObj(JToken token, string where)
        {
            _o = token as JObject ?? throw new FormatException($"{where}: expected an object.");
            Where = where;
        }

        public FormatException Error(string message) => new FormatException($"{Where}: {message}.");

        public IEnumerable<string> Keys()
        {
            foreach (JProperty p in _o.Properties())
            {
                if (p.Name != "Notes") yield return p.Name;
            }
        }

        public bool Has(string key) => _o.TryGetValue(key, out JToken t) && t.Type != JTokenType.Null;

        public JToken Raw(string key)
        {
            _read.Add(key);
            if (!_o.TryGetValue(key, out JToken t) || t.Type == JTokenType.Null) throw Error($"missing '{key}'");
            return t;
        }

        public double Num(string key) => Json.ToDouble(Raw(key), $"{Where}.{key}");

        public int Int(string key)
        {
            JToken t = Raw(key);
            if (t.Type != JTokenType.Integer) throw Error($"'{key}' must be an integer");
            return t.Value<int>();
        }

        public bool Bool(string key)
        {
            JToken t = Raw(key);
            if (t.Type != JTokenType.Boolean) throw Error($"'{key}' must be true or false");
            return t.Value<bool>();
        }

        public string Str(string key)
        {
            JToken t = Raw(key);
            if (t.Type != JTokenType.String) throw Error($"'{key}' must be a string");
            return (string)t;
        }

        public string OptStr(string key)
        {
            _read.Add(key);
            return Has(key) ? Str(key) : null;
        }

        public T Enum<T>(string key) where T : struct
        {
            string s = Str(key);
            if (!System.Enum.TryParse(s, false, out T value) || !System.Enum.IsDefined(typeof(T), value))
                throw Error($"'{key}' has unknown {typeof(T).Name} '{s}'");
            return value;
        }

        public JsonObj Obj(string key) => new JsonObj(Raw(key), $"{Where}.{key}");

        public List<JsonObj> Objs(string key)
        {
            var list = new List<JsonObj>();
            _read.Add(key);
            if (!Has(key)) return list;
            int i = 0;
            foreach (JToken t in Array(key)) list.Add(new JsonObj(t, $"{Where}.{key}[{i++}]"));
            return list;
        }

        public List<string> Strs(string key)
        {
            var list = new List<string>();
            int i = 0;
            foreach (JToken t in Array(key))
            {
                if (t.Type != JTokenType.String) throw Error($"'{key}[{i}]' must be a string");
                list.Add((string)t);
                i++;
            }
            return list;
        }

        public List<int> Ints(string key)
        {
            var list = new List<int>();
            int i = 0;
            foreach (JToken t in Array(key))
            {
                if (t.Type != JTokenType.Integer) throw Error($"'{key}[{i}]' must be an integer");
                list.Add(t.Value<int>());
                i++;
            }
            return list;
        }

        public Vec2d V2(string key) => ToV2(Raw(key), $"{Where}.{key}");

        public Vec3d V3(string key)
        {
            string where = $"{Where}.{key}";
            if (!(Raw(key) is JArray a) || a.Count != 3) throw new FormatException($"{where}: expected [x, y, z].");
            return new Vec3d(Json.ToDouble(a[0], where), Json.ToDouble(a[1], where), Json.ToDouble(a[2], where));
        }

        /// <summary>A list of [x, z] pairs; empty when the key is absent.</summary>
        public List<Vec2d> Points(string key)
        {
            var list = new List<Vec2d>();
            _read.Add(key);
            if (!Has(key)) return list;
            int i = 0;
            foreach (JToken t in Array(key)) list.Add(ToV2(t, $"{Where}.{key}[{i++}]"));
            return list;
        }

        public void Done()
        {
            foreach (JProperty p in _o.Properties())
            {
                if (p.Name != "Notes" && !_read.Contains(p.Name)) throw Error($"unknown key '{p.Name}'");
            }
        }

        private JArray Array(string key)
        {
            if (!(Raw(key) is JArray a)) throw Error($"'{key}' must be an array");
            return a;
        }

        private static Vec2d ToV2(JToken t, string where)
        {
            if (!(t is JArray a) || a.Count != 2) throw new FormatException($"{where}: expected [x, z].");
            return new Vec2d(Json.ToDouble(a[0], where), Json.ToDouble(a[1], where));
        }
    }
}
