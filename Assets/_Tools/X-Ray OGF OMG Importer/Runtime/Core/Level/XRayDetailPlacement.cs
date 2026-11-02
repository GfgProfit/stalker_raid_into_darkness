using System;
using System.Collections.Generic;

namespace XRay.Importer.Core.Level
{
    public struct XRayRandom
    {
        private int _holdrand;

        public XRayRandom(int seed)
        {
            _holdrand = seed;
        }

        public int RandI()
        {
            _holdrand = unchecked(_holdrand * 214013 + 2531011);
            return (_holdrand >> 16) & 0x7fff;
        }

        public int RandI(int max)
        {
            return RandI() % max;
        }

        public float RandF()
        {
            return RandI() / 32767.0f;
        }

        public float RandF(float min, float max)
        {
            return min + RandF() * (max - min);
        }

        public float RandFs(float range)
        {
            return RandF(-range, range);
        }
    }

    public readonly struct XRayDetailInstance
    {
        public XRayDetailInstance(int model, float x, float y, float z, float yaw, float scale, float hemi, float sun, int wave, float normalX = 0.0f, float normalY = 1.0f, float normalZ = 0.0f)
        {
            Model = model;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Scale = scale;
            Hemi = hemi;
            Sun = sun;
            Wave = wave;
            NormalX = normalX;
            NormalY = normalY;
            NormalZ = normalZ;
        }

        public int Model { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Yaw { get; }
        public float Scale { get; }
        public float Hemi { get; }
        public float Sun { get; }
        public int Wave { get; }
        public float NormalX { get; }
        public float NormalY { get; }
        public float NormalZ { get; }
    }

    public sealed class XRayDetailPlacementSettings
    {
        public float Density { get; set; } = 0.6f;
        public float HeightScale { get; set; } = 1.0f;
        public bool JitterZBeforeX { get; set; } = true;
        public bool MonolithRelease { get; set; }
        public float TerrainSlope { get; set; }
    }

    public sealed class XRayDetailPlacementStatistics
    {
        public int SlotsProcessed { get; internal set; }
        public int SlotsWithoutGround { get; internal set; }
        public int Candidates { get; internal set; }
        public int Rejected { get; internal set; }
        public int Instances { get; internal set; }
        public int[] PerModel { get; internal set; }
    }

    public sealed class XRayDetailPlacement
    {
        public const int SEED = 0x12071980;
        public const float EPS_L = 0.001f;

        private static readonly int[,] MAGIC4X4 = { { 0, 14, 3, 13 }, { 11, 5, 8, 6 }, { 12, 2, 15, 1 }, { 7, 9, 4, 10 } };

        private readonly XRayDetails _details;
        private readonly XRayCollisionModel _collision;
        private readonly bool[] _passable;
        private readonly XRayDetailPlacementSettings _settings;
        private readonly int[,] _dither = new int[16, 16];
        private readonly List<int> _triangles = new List<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly float[][] _alpha = { new float[4], new float[4], new float[4], new float[4] };
        private readonly int[] _selected = new int[4];

        public XRayDetailPlacement(XRayDetails details, XRayCollisionModel collision, bool[] passableMaterials, XRayDetailPlacementSettings settings)
        {
            _details = details;
            _collision = collision;
            _passable = passableMaterials;
            _settings = settings;
            Statistics.PerModel = new int[details.Models.Count];
            BuildDither();
        }

        public XRayDetailPlacementStatistics Statistics { get; } = new XRayDetailPlacementStatistics();
        public int DitherSize => (int)MathF.Ceiling(XRayDetails.SLOT_SIZE / _settings.Density);

        public static int MonolithSeed(int x, int z)
        {
            ulong value = ((ulong)(uint)x << 32) | (uint)z;
            return unchecked(SEED ^ (int)((9199940308585234877UL * value + 9199940308585234877UL) >> 32));
        }

        public void PlaceSlot(int dbX, int dbZ, List<XRayDetailInstance> output)
        {
            XRayDetailSlot slot = _details.GetSlot(dbX, dbZ);
            if (slot.IsEmpty)
            {
                return;
            }

            Statistics.SlotsProcessed++;
            int sx = dbX - _details.OffsetX;
            int sz = dbZ - _details.OffsetZ;
            float minX = sx * XRayDetails.SLOT_SIZE - EPS_L;
            float minY = slot.YBase - EPS_L;
            float minZ = sz * XRayDetails.SLOT_SIZE - EPS_L;
            float maxX = sx * XRayDetails.SLOT_SIZE + XRayDetails.SLOT_SIZE + EPS_L;
            float maxY = slot.YBase + slot.YHeight + EPS_L;
            float maxZ = sz * XRayDetails.SLOT_SIZE + XRayDetails.SLOT_SIZE + EPS_L;
            _collision.QueryBox(minX, minY, minZ, maxX, maxY, maxZ, _triangles, _seen);
            if (_triangles.Count == 0)
            {
                Statistics.SlotsWithoutGround++;
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                ushort palette = slot.GetPalette(i);
                _alpha[i][0] = 255.0f * (palette & 0xF) / 15.0f;
                _alpha[i][1] = 255.0f * ((palette >> 4) & 0xF) / 15.0f;
                _alpha[i][2] = 255.0f * ((palette >> 8) & 0xF) / 15.0f;
                _alpha[i][3] = 255.0f * ((palette >> 12) & 0xF) / 15.0f;
            }

            float density = _settings.Density;
            float jitter = density / 1.7f;
            int size = DitherSize;
            int seed = _settings.MonolithRelease ? MonolithSeed(sx, sz) : SEED ^ unchecked(sx * sz);
            var selection = new XRayRandom(seed);
            var jitterRandom = new XRayRandom(seed);
            var yawRandom = new XRayRandom(seed);
            var scaleRandom = new XRayRandom(seed);
            var waveRandom = new XRayRandom(seed ^ 0x5bd1e995);
            float hemi = slot.Hemi / 15.0f;
            float sun = slot.Sun / 15.0f;
            for (int z = 0; z <= size; z++)
            {
                for (int x = 0; x <= size; x++)
                {
                    int shiftX = jitterRandom.RandI(16);
                    int shiftZ = jitterRandom.RandI(16);
                    int selectedCount = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        if (slot.GetId(i) != XRayDetails.ID_EMPTY && InterpolateAndDither(_alpha[i], x, z, shiftX, shiftZ, size))
                        {
                            _selected[selectedCount++] = i;
                        }
                    }

                    if (selectedCount == 0)
                    {
                        continue;
                    }

                    int index = selectedCount == 1 ? _selected[0] : _selected[selection.RandI(selectedCount)];
                    int modelId = slot.GetId(index);
                    Statistics.Candidates++;
                    float rx = (float)x / size * XRayDetails.SLOT_SIZE + minX;
                    float rz = (float)z / size * XRayDetails.SLOT_SIZE + minZ;
                    float jx;
                    float jz;
                    if (_settings.JitterZBeforeX)
                    {
                        jz = jitterRandom.RandFs(jitter);
                        jx = jitterRandom.RandFs(jitter);
                    }
                    else
                    {
                        jx = jitterRandom.RandFs(jitter);
                        jz = jitterRandom.RandFs(jitter);
                    }

                    float px = rx + jx;
                    float pz = rz + jz;
                    float py = maxY;
                    float y = minY - 5.0f;
                    float nx = 0.0f;
                    float ny = 1.0f;
                    float nz = 0.0f;
                    foreach (int face in _triangles)
                    {
                        if (_passable != null && _collision.Materials[face] < _passable.Length && _passable[_collision.Materials[face]])
                        {
                            continue;
                        }

                        if (_collision.RayDown(face, px, py, pz, out float range) && range >= 0.0f)
                        {
                            int a = _collision.Triangles[face * 3] * 3;
                            int b = _collision.Triangles[face * 3 + 1] * 3;
                            int c = _collision.Triangles[face * 3 + 2] * 3;
                            float ax = _collision.Vertices[b] - _collision.Vertices[a];
                            float ay = _collision.Vertices[b + 1] - _collision.Vertices[a + 1];
                            float az = _collision.Vertices[b + 2] - _collision.Vertices[a + 2];
                            float bx = _collision.Vertices[c] - _collision.Vertices[a];
                            float by = _collision.Vertices[c + 1] - _collision.Vertices[a + 1];
                            float bz = _collision.Vertices[c + 2] - _collision.Vertices[a + 2];
                            nx = ay * bz - az * by;
                            ny = az * bx - ax * bz;
                            nz = ax * by - ay * bx;
                            float length = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                            if (length > 0.0f)
                            {
                                nx /= length;
                                ny /= length;
                                nz /= length;
                            }
                            float test = py - range;
                            if (test > y)
                            {
                                y = test;
                            }
                        }
                    }

                    if (_settings.MonolithRelease && -ny > -(1.0f - scaleRandom.RandF(_settings.TerrainSlope * 0.8f, _settings.TerrainSlope)))
                    {
                        Statistics.Rejected++;
                        continue;
                    }

                    if (y < minY || modelId >= _details.Models.Count)
                    {
                        Statistics.Rejected++;
                        continue;
                    }

                    XRayDetailModel model = _details.Models[modelId];
                    float scale = scaleRandom.RandF(model.MinScale * 0.5f, model.MaxScale * 0.9f) * _settings.HeightScale;
                    float yaw = yawRandom.RandF(0.0f, (float)(Math.PI * 2.0));
                    int wave = model.NoWaving ? 0 : waveRandom.RandI(3) == 0 ? 2 : 1;
                    output.Add(new XRayDetailInstance(modelId, px, y, pz, yaw, scale, hemi, sun, wave, nx, ny, nz));
                    Statistics.Instances++;
                    Statistics.PerModel[modelId]++;
                }
            }
        }

        private bool InterpolateAndDither(float[] alpha, int x, int y, int sx, int sy, int size)
        {
            x = Math.Max(0, Math.Min(size - 1, x));
            y = Math.Max(0, Math.Min(size - 1, y));
            int c = (int)Math.Floor(Interpolate(alpha, x, y, size) + 0.5f);
            c = Math.Max(0, Math.Min(255, c));
            int row = (y + sy) % 16;
            int col = (x + sx) % 16;
            return c > _dither[col, row];
        }

        private static float Interpolate(float[] values, int x, int y, int size)
        {
            float f = size;
            float fx = x / f;
            float ifx = 1.0f - fx;
            float fy = y / f;
            float ify = 1.0f - fy;
            float c01 = values[0] * ifx + values[1] * fx;
            float c23 = values[2] * ifx + values[3] * fx;
            float c02 = values[0] * ify + values[2] * fy;
            float c13 = values[1] * ify + values[3] * fy;
            float cx = ify * c01 + fy * c23;
            float cy = ifx * c02 + fx * c13;
            return (cx + cy) / 2.0f;
        }

        private void BuildDither()
        {
            float n = 255.0f;
            float magic = (n - 1.0f) / 16.0f;
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        for (int l = 0; l < 4; l++)
                        {
                            _dither[4 * k + i, 4 * l + j] = (int)(0.5 + MAGIC4X4[i, j] * magic + MAGIC4X4[k, l] / 16.0 * magic);
                        }
                    }
                }
            }
        }
    }
}
