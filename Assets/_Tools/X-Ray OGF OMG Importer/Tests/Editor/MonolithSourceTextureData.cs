using System;
using System.Security.Cryptography;
using UnityEngine;

namespace XRay.Importer.Tests
{
    public static class MonolithSourceTextureData
    {
        public const int SIZE = 64;
        public const int CASES_PER_SET = 24;
        public const int CASE_STRIDE = 6;
        public const int RESULT_STRIDE = 6;

        public static readonly string[] TextureNames = { "base", "bump", "bumpXFlat", "bumpXSmooth", "bumpXSharp", "detail", "detailBump", "detailBumpX" };
        public static readonly int[] VariantParallax = { 0, 0, 0, 1, 1, 1, 2, 2, 2 };
        public static readonly int[] VariantDetail = { 0, 1, 2, 0, 1, 2, 0, 1, 2 };
        public static readonly int[] SetHeight = { 0, 1, 2, 1, 2, 2 };
        public static readonly Vector4[] SetPom = { new Vector4(36, 12, 0.035f, 0.5f), new Vector4(36, 12, 0.035f, 0.5f), new Vector4(36, 12, 0.035f, 0.5f), new Vector4(16, 12, 0.1f, 0.4f), new Vector4(36, 12, 0.01f, 1.0f), new Vector4(8, 6, 0.06f, 0.0f) };
        public static readonly float[] SetParallaxHeight = { 0.02f, 0.02f, 0.02f, 0.08f, 0.005f, 0.04f };
        public static readonly float[] SetDetailScale = { 1.0f, 6.0f, 8.0f, 12.0f, 4.0f, 0.5f };
        public static readonly float[] SetMaterial = { 0.24731183f, 0.5591398f, 0.8172043f, 0.24731183f, 1.0322581f, 0.38709676f };

        private static readonly Vector2[] _tc = { new Vector2(0.25f, 0.4f), new Vector2(0.999f, 0.5f), new Vector2(0.001f, 0.002f), new Vector2(-0.3f, 1.7f), new Vector2(3.62f, -2.15f), new Vector2(0.5f, 0.999f) };
        private static readonly Vector3[] _view = { new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.5f, 0.3f, 0.81f), new Vector3(-0.8f, 0.45f, 0.18f), new Vector3(0.2f, -0.98f, 0.035f) };
        private static readonly float[] _depth = { 1.5f, 6.0f, 10.5f, 12.5f };

        public static Color32[] Texture(int index)
        {
            var pixels = new Color32[SIZE * SIZE];
            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    double u = x / (double)SIZE;
                    double v = y / (double)SIZE;
                    pixels[y * SIZE + x] = Pixel(index, x, y, u, v);
                }
            }

            return pixels;
        }

        public static Texture2D Create(int index, bool flipRows)
        {
            Color32[] source = Texture(index);
            var pixels = new Color32[source.Length];
            for (int y = 0; y < SIZE; y++)
            {
                Array.Copy(source, (flipRows ? SIZE - 1 - y : y) * SIZE, pixels, y * SIZE, SIZE);
            }

            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
            texture.name = TextureNames[index];
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.anisoLevel = 0;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        public static string Hash(int index)
        {
            Color32[] pixels = Texture(index);
            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = pixels[i].r;
                bytes[i * 4 + 1] = pixels[i].g;
                bytes[i * 4 + 2] = pixels[i].b;
                bytes[i * 4 + 3] = pixels[i].a;
            }

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static Vector4[] Cases(int set)
        {
            var cases = new Vector4[CASES_PER_SET * CASE_STRIDE];
            for (int k = 0; k < CASES_PER_SET; k++)
            {
                Vector2 tc = _tc[(k + set) % _tc.Length];
                Vector3 view = _view[(k / 2 + set) % _view.Length].normalized;
                int frame = (k * 3 + set) % 4;
                float depth = _depth[(k / 3 + set * 2) % _depth.Length];
                Basis(frame, set, k, out Vector3 tangent, out Vector3 bitangent, out Vector3 normal);
                Vector3 t = tangent.normalized;
                Vector3 n = Vector3.Cross(t, Vector3.Cross(normal, t).normalized).normalized;
                n = Vector3.Dot(n, normal) < 0 ? -n : n;
                Vector3 b = Vector3.Cross(n, t);
                Vector3 toCamera = (t * view.x + b * view.y + n * view.z).normalized;
                Vector3 forward = new Vector3(0.2f, -0.1f, 1.0f).normalized;
                Quaternion rotation = Quaternion.FromToRotation(-toCamera, forward);
                float distance = depth / forward.z;
                Vector3 position = forward * distance;
                Vector3 rt = rotation * tangent;
                Vector3 rb = rotation * bitangent;
                Vector3 rn = rotation * normal;
                Vector3 light = (rotation * (t * 0.3f + b * 0.5f + n * 0.8f)).normalized;
                cases[k * CASE_STRIDE + 0] = new Vector4(tc.x, tc.y, 0.0f, 0.0f);
                cases[k * CASE_STRIDE + 1] = new Vector4(position.x, position.y, position.z, 0.5f);
                cases[k * CASE_STRIDE + 2] = new Vector4(rt.x, rb.x, rn.x, 0.0f);
                cases[k * CASE_STRIDE + 3] = new Vector4(rt.y, rb.y, rn.y, 0.0f);
                cases[k * CASE_STRIDE + 4] = new Vector4(rt.z, rb.z, rn.z, 0.0f);
                cases[k * CASE_STRIDE + 5] = new Vector4(light.x, light.y, light.z, SetMaterial[set]);
            }

            return cases;
        }

        private static void Basis(int frame, int set, int k, out Vector3 tangent, out Vector3 bitangent, out Vector3 normal)
        {
            tangent = new Vector3(1, 0, 0);
            bitangent = new Vector3(0, 1, 0);
            normal = new Vector3(0, 0, 1);
            if (frame == 1)
            {
                Quaternion q = Quaternion.Euler(23 + set * 7, -41 + k, 67);
                tangent = q * tangent;
                bitangent = q * bitangent;
                normal = q * normal;
            }
            else if (frame == 2)
            {
                bitangent = -bitangent;
            }
            else if (frame == 3)
            {
                tangent = new Vector3(1.9f, 0.3f, -0.2f);
                bitangent = new Vector3(0.25f, 2.05f, 0.4f);
                normal = new Vector3(-0.1f, 0.2f, 1.96f);
            }
        }

        private static Color32 Pixel(int index, int x, int y, double u, double v)
        {
            double tau = Math.PI * 2.0;
            switch (index)
            {
                case 0:
                    return Pack(0.5 + 0.35 * Math.Sin(tau * 3 * u), 0.45 + 0.3 * Math.Cos(tau * 2 * v), 0.4 + 0.25 * Math.Sin(tau * (u + v)), 1.0);
                case 1:
                {
                    double dx = 0.9 * Math.Cos(tau * 2 * u) * Math.Cos(tau * v);
                    double dy = -0.6 * Math.Sin(tau * 2 * u) * Math.Sin(tau * v) + 0.3 * Math.Cos(tau * 3 * v);
                    double length = Math.Sqrt(dx * dx + dy * dy + 1.0);
                    return Pack(0.2 + 0.6 * (0.5 + 0.5 * Math.Sin(tau * (2 * u - v))), 0.5 + 0.5 / length, 0.5 + 0.5 * dy / length, 0.5 + 0.5 * dx / length);
                }
                case 2:
                    return Pack(Noise(x, y, 1), Noise(x, y, 2), Noise(x, y, 3), 0.6);
                case 3:
                    return Pack(Noise(x, y, 4), Noise(x, y, 5), Noise(x, y, 6), 0.5 + 0.47 * Math.Sin(tau * 2 * u) * Math.Cos(tau * 3 * v));
                case 4:
                {
                    int row = y / 8;
                    int column = (x + (row % 2) * 8) / 16;
                    bool mortar = y % 8 == 0 || (x + (row % 2) * 8) % 16 == 0;
                    double brick = 0.8 + 0.15 * (Hash(column, row, 7) / 255.0);
                    return Pack(Noise(x, y, 8), Noise(x, y, 9), Noise(x, y, 10), mortar ? 0.04 : brick);
                }
                case 5:
                    return Pack(0.5 + 0.3 * Math.Sin(tau * 5 * u + 1.0), 0.5 + 0.25 * Math.Sin(tau * 4 * v), 0.5 + 0.2 * Math.Cos(tau * 3 * (u - v)), 0.4 + 0.25 * (Hash(x, y, 11) / 255.0));
                case 6:
                {
                    double dx = 0.5 * Math.Sin(tau * 4 * u + tau * v);
                    double dy = 0.4 * Math.Cos(tau * 5 * v);
                    double length = Math.Sqrt(dx * dx + dy * dy + 1.0);
                    return Pack(0.4 + 0.2 * (Hash(x, y, 12) / 255.0), 0.5 + 0.5 / length, 0.5 + 0.5 * dy / length, 0.5 + 0.5 * dx / length);
                }
                default:
                    return Pack(Noise(x, y, 13), Noise(x, y, 14), Noise(x, y, 15), 0.5);
            }
        }

        private static double Noise(int x, int y, int seed)
        {
            return 0.5 + (Hash(x, y, seed) - 127.5) / 255.0 * 0.1;
        }

        private static int Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (int)(h & 255);
        }

        private static Color32 Pack(double r, double g, double b, double a)
        {
            return new Color32(Byte(r), Byte(g), Byte(b), Byte(a));
        }

        private static byte Byte(double value)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value * 255.0)));
        }
    }
}
