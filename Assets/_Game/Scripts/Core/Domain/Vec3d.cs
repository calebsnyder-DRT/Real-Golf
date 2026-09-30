using System;
using Newtonsoft.Json;

namespace Puttshack.Domain
{
    /// <summary>Right-handed, Y-up, meters. Double precision keeps the solver repeatable.</summary>
    [Serializable]
    public struct Vec3d : IEquatable<Vec3d>
    {
        public double x;
        public double y;
        public double z;

        public Vec3d(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static readonly Vec3d Zero = new Vec3d(0, 0, 0);
        public static readonly Vec3d Up = new Vec3d(0, 1, 0);
        public static readonly Vec3d Forward = new Vec3d(0, 0, 1);

        [JsonIgnore]
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        [JsonIgnore]
        public double LengthSquared => x * x + y * y + z * z;
        [JsonIgnore]
        public double HorizontalLength => Math.Sqrt(x * x + z * z);
        [JsonIgnore]
        public Vec3d Horizontal => new Vec3d(x, 0, z);

        [JsonIgnore]
        public bool IsFinite =>
            !double.IsNaN(x) && !double.IsInfinity(x) &&
            !double.IsNaN(y) && !double.IsInfinity(y) &&
            !double.IsNaN(z) && !double.IsInfinity(z);

        [JsonIgnore]
        public Vec3d Normalized
        {
            get
            {
                double len = Length;
                return len > 1e-12 ? this / len : Zero;
            }
        }

        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3d operator -(Vec3d a) => new Vec3d(-a.x, -a.y, -a.z);
        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.x * s, a.y * s, a.z * s);
        public static Vec3d operator *(double s, Vec3d a) => new Vec3d(a.x * s, a.y * s, a.z * s);
        public static Vec3d operator /(Vec3d a, double s) => new Vec3d(a.x / s, a.y / s, a.z / s);

        public static double Dot(Vec3d a, Vec3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vec3d Cross(Vec3d a, Vec3d b) =>
            new Vec3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        public static Vec3d Lerp(Vec3d a, Vec3d b, double t) => a + (b - a) * t;

        public static double HorizontalDistance(Vec3d a, Vec3d b)
        {
            double dx = a.x - b.x;
            double dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Rotates around world +Y. Positive yaw turns toward the player's right when facing +Z.</summary>
        public Vec3d RotateYaw(double radians)
        {
            double c = Math.Cos(radians);
            double s = Math.Sin(radians);
            return new Vec3d(x * c + z * s, y, -x * s + z * c);
        }

        public bool Equals(Vec3d other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);
        public override bool Equals(object obj) => obj is Vec3d other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    [Serializable]
    public struct Vec2d
    {
        public double x;
        public double z;

        public Vec2d(double x, double z)
        {
            this.x = x;
            this.z = z;
        }
    }
}
