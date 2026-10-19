using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Tileable soft cumulus coverage for the sun's light cookie: periodic fBm gradient noise, thresholded into patchy
    /// clouds, written as 1 - strength * coverage so a cookie multiplies the sun by at most (1 - strength).
    /// </summary>
    public static class CloudShadowTexture
    {
        public const int DefaultSize = 256;

        public static Texture2D Create(float strength, int size = DefaultSize, int seed = 7)
        {
            var tex = new Texture2D(size, size, TextureFormat.R8, true, true)
            {
                name = "CloudShadows",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels(Pixels(strength, size, seed));
            tex.Apply(true);
            return tex;
        }

        public static Color[] Pixels(float strength, int size = DefaultSize, int seed = 7)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = 0f, amplitude = 0.5f, norm = 0f;
                    for (int octave = 0, period = 4; octave < 5; octave++, period *= 2)
                    {
                        n += amplitude * Noise(u * period, v * period, period, seed + octave * 31);
                        norm += amplitude;
                        amplitude *= 0.5f;
                    }
                    n = n / norm * 0.5f + 0.5f;
                    float coverage = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.68f, n));
                    float value = 1f - strength * coverage;
                    pixels[y * size + x] = new Color(value, value, value, 1f);
                }
            }
            return pixels;
        }

        /// <summary>Gradient noise in [-1, 1] whose lattice wraps every <paramref name="period"/> cells.</summary>
        private static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float a = Grad(x0, y0, period, seed, fx, fy);
            float b = Grad(x0 + 1, y0, period, seed, fx - 1f, fy);
            float c = Grad(x0, y0 + 1, period, seed, fx, fy - 1f);
            float d = Grad(x0 + 1, y0 + 1, period, seed, fx - 1f, fy - 1f);
            float sx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float sy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy) * 1.4f;
        }

        private static float Grad(int ix, int iy, int period, int seed, float dx, float dy)
        {
            ix = ((ix % period) + period) % period;
            iy = ((iy % period) + period) % period;
            uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            float angle = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
            return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
        }
    }
}
