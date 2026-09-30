using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Puttshack.Content
{
    public static class ContentHasher
    {
        private static readonly JsonSerializerSettings CanonicalSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            Converters = { new StringEnumConverter() },
            FloatFormatHandling = FloatFormatHandling.String,
        };

        public static string Canonical(object value) => JsonConvert.SerializeObject(value, CanonicalSettings);

        public static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string HashHole(HoleDefinition hole)
        {
            string previous = hole.ContentHash;
            hole.ContentHash = null;
            string hash = Sha256(Canonical(hole));
            hole.ContentHash = previous;
            return hash;
        }

        public static void Seal(CourseDefinition course)
        {
            var sb = new StringBuilder();
            sb.Append(course.Id).Append('|').Append(course.Version);
            foreach (HoleDefinition hole in course.Holes)
            {
                hole.ContentHash = HashHole(hole);
                sb.Append('|').Append(hole.ContentHash);
            }
            course.ContentHash = Sha256(sb.ToString());
        }
    }
}
