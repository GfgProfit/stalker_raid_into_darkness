using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Tests
{
    public static class MonolithSourceTerrainData
    {
        public const int SIZE = 32;
        public const int CASE_STRIDE = 8;
        public const int RESULT_STRIDE = 21;

        public static Texture2D CreateTexture(int layer, int kind)
        {
            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true) { name = "Terrain_" + layer + "_" + kind, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[SIZE * SIZE];
            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    if (kind == 0)
                    {
                        pixels[y * SIZE + x] = new Color32((byte)(24 + (x * 3 + layer * 31) % 190), (byte)(32 + (y * 5 + layer * 43) % 180), (byte)(20 + (x + y * 2 + layer * 19) % 170), 255);
                    }
                    else if (kind == 1)
                    {
                        pixels[y * SIZE + x] = new Color32((byte)(8 + (x + y + layer * 29) % 210), (byte)(210 + (x + layer * 5) % 45), (byte)(80 + (y * 2 + layer * 17) % 96), (byte)(80 + (x * 2 + layer * 13) % 96));
                    }
                    else
                    {
                        byte height = layer == 0 ? (byte)96 : (byte)((x * (layer + 1) * 5 + y * 3 + layer * 41) % 256);
                        pixels[y * SIZE + x] = new Color32(height, 0, 0, 255);
                    }
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        public static Vector4[] Cases()
        {
            Vector4[] masks = { new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1), new Vector4(0.2f, 0.8f, 0, 0), new Vector4(1, 2, 3, 4), new Vector4(1e-8f, 2e-8f, 0, 1e-8f), Vector4.zero };
            Vector2[] uvs = { new Vector2(0.25f, 0.4f), new Vector2(-0.3f, 1.7f), new Vector2(0.999f, 0.002f) };
            float[] scales = { 0.5f, 8.0f, 144.0f };
            float[] depths = { 1.5f, 17.0f, 19.0f, 21.0f };
            Vector3[] views = { Vector3.forward, new Vector3(0.5f, 0.3f, 0.81f).normalized, new Vector3(-0.8f, 0.45f, 0.18f).normalized, new Vector3(0.2f, -0.98f, 0.035f).normalized };
            var cases = new List<Vector4>();
            for (int mode = 0; mode < 4; mode++)
            {
                for (int mask = 0; mask < masks.Length; mask++)
                {
                    for (int angle = 0; angle < views.Length; angle++)
                    {
                        for (int depth = 0; depth < depths.Length; depth++)
                        {
                            for (int uv = 0; uv < uvs.Length; uv++)
                            {
                                Vector3 position = views[angle] * (depths[depth] / views[angle].z);
                                float skew = uv == 2 ? 0.13f : 0.0f;
                                float stretch = uv == 1 ? 1.6f : 1.0f;
                                cases.Add(new Vector4(uvs[uv].x, uvs[uv].y, scales[uv], mode));
                                cases.Add(new Vector4(position.x, position.y, position.z, 0.7f));
                                cases.Add(new Vector4(stretch, skew, 0.0f, 0.0f));
                                cases.Add(new Vector4(0.0f, uv == 2 ? -1.0f : 1.0f, 0.0f, 0.0f));
                                cases.Add(new Vector4(skew, 0.0f, -stretch, 0.0f));
                                cases.Add(masks[mask]);
                                cases.Add(new Vector4(angle == 3 ? 36.0f : 12.0f, depth == 2 ? 30.0f : 20.0f, uv == 2 ? 0.1f : 0.04f, 1.0f));
                                cases.Add(new Vector4(-0.07f, -0.05f, -0.07f, 0.0f));
                            }
                        }
                    }
                }
            }
            return cases.ToArray();
        }
    }
}
