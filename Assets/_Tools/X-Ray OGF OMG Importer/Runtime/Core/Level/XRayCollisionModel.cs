using System;
using System.Collections.Generic;
using System.IO;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Level
{
    public sealed class XRayCollisionModel
    {
        public const uint VERSION = 4;
        public const int HEADER_BYTES = 36;
        public const int TRIANGLE_BYTES = 16;
        public const float EPS = 0.00001f;

        private int[] _cellStart;
        private int[] _cellItems;
        private int _cellsX;
        private int _cellsZ;
        private float _cellSize;

        public float[] Vertices { get; private set; }
        public int[] Triangles { get; private set; }
        public ushort[] Materials { get; private set; }
        public ushort[] Sectors { get; private set; }
        public float[] BoundsMin { get; } = new float[3];
        public float[] BoundsMax { get; } = new float[3];
        public int VertexCount => Vertices.Length / 3;
        public int TriangleCount => Materials.Length;

        public static XRayCollisionModel Read(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var reader = new BinaryReader(stream))
            {
                string context = Path.GetFileName(path);
                if (stream.Length < HEADER_BYTES)
                {
                    throw new XRayFormatException($"{context}: file is smaller than the CFORM header.");
                }

                uint version = reader.ReadUInt32();
                if (version != VERSION)
                {
                    throw new XRayFormatException($"{context}: CFORM version {version} is not supported (expected {VERSION}).");
                }

                int vertexCount = reader.ReadInt32();
                int faceCount = reader.ReadInt32();
                var model = new XRayCollisionModel();
                for (int i = 0; i < 3; i++)
                {
                    model.BoundsMin[i] = reader.ReadSingle();
                }

                for (int i = 0; i < 3; i++)
                {
                    model.BoundsMax[i] = reader.ReadSingle();
                }

                long expected = HEADER_BYTES + vertexCount * 12L + faceCount * (long)TRIANGLE_BYTES;
                if (vertexCount < 0 || faceCount < 0 || expected != stream.Length)
                {
                    throw new XRayFormatException($"{context}: {vertexCount} vertices and {faceCount} faces need {expected} bytes, file has {stream.Length}.");
                }

                model.Vertices = new float[vertexCount * 3];
                byte[] vertexBytes = reader.ReadBytes(vertexCount * 12);
                Buffer.BlockCopy(vertexBytes, 0, model.Vertices, 0, vertexBytes.Length);
                model.Triangles = new int[faceCount * 3];
                model.Materials = new ushort[faceCount];
                model.Sectors = new ushort[faceCount];
                byte[] faceBytes = reader.ReadBytes(faceCount * TRIANGLE_BYTES);
                for (int f = 0; f < faceCount; f++)
                {
                    int p = f * TRIANGLE_BYTES;
                    for (int k = 0; k < 3; k++)
                    {
                        int index = BitConverter.ToInt32(faceBytes, p + k * 4);
                        if (index < 0 || index >= vertexCount)
                        {
                            throw new XRayFormatException($"{context}: face {f} references vertex {index} outside {vertexCount}.");
                        }

                        model.Triangles[f * 3 + k] = index;
                    }

                    uint dummy = BitConverter.ToUInt32(faceBytes, p + 12);
                    model.Materials[f] = (ushort)(dummy & 0x3FFF);
                    model.Sectors[f] = (ushort)(dummy >> 16);
                }

                return model;
            }
        }

        public void BuildGrid(float cellSize)
        {
            _cellSize = cellSize;
            _cellsX = Math.Max(1, (int)Math.Ceiling((BoundsMax[0] - BoundsMin[0]) / cellSize) + 1);
            _cellsZ = Math.Max(1, (int)Math.Ceiling((BoundsMax[2] - BoundsMin[2]) / cellSize) + 1);
            var counts = new int[_cellsX * _cellsZ + 1];
            ForEachCell((cell, face) => counts[cell]++);
            _cellStart = new int[counts.Length];
            int total = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                _cellStart[i] = total;
                total += counts[i];
            }

            _cellItems = new int[total];
            var fill = new int[counts.Length];
            ForEachCell((cell, face) => _cellItems[_cellStart[cell] + fill[cell]++] = face);
        }

        public void QueryBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ, List<int> result, HashSet<int> seen)
        {
            result.Clear();
            seen.Clear();
            int x0 = CellX(minX);
            int x1 = CellX(maxX);
            int z0 = CellZ(minZ);
            int z1 = CellZ(maxZ);
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            float hx = (maxX - minX) * 0.5f;
            float hy = (maxY - minY) * 0.5f;
            float hz = (maxZ - minZ) * 0.5f;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int cell = z * _cellsX + x;
                    for (int i = _cellStart[cell]; i < _cellStart[cell + 1]; i++)
                    {
                        int face = _cellItems[i];
                        if (seen.Add(face) && TriangleBoxOverlap(face, cx, cy, cz, hx, hy, hz))
                        {
                            result.Add(face);
                        }
                    }
                }
            }
        }

        public bool RayDown(int face, float px, float py, float pz, out float range)
        {
            int a = Triangles[face * 3] * 3;
            int b = Triangles[face * 3 + 1] * 3;
            int c = Triangles[face * 3 + 2] * 3;
            float e1x = Vertices[b] - Vertices[a];
            float e1y = Vertices[b + 1] - Vertices[a + 1];
            float e1z = Vertices[b + 2] - Vertices[a + 2];
            float e2x = Vertices[c] - Vertices[a];
            float e2y = Vertices[c + 1] - Vertices[a + 1];
            float e2z = Vertices[c + 2] - Vertices[a + 2];
            float pvx = -e2z;
            float pvy = 0.0f;
            float pvz = e2x;
            float det = e1x * pvx + e1y * pvy + e1z * pvz;
            range = 0.0f;
            if (det < EPS)
            {
                return false;
            }

            float tx = px - Vertices[a];
            float ty = py - Vertices[a + 1];
            float tz = pz - Vertices[a + 2];
            float u = tx * pvx + ty * pvy + tz * pvz;
            if (u < 0.0f || u > det)
            {
                return false;
            }

            float qx = ty * e1z - tz * e1y;
            float qy = tz * e1x - tx * e1z;
            float qz = tx * e1y - ty * e1x;
            float v = -qy;
            if (v < 0.0f || u + v > det)
            {
                return false;
            }

            range = (e2x * qx + e2y * qy + e2z * qz) * (1.0f / det);
            return true;
        }

        private void ForEachCell(Action<int, int> action)
        {
            for (int f = 0; f < TriangleCount; f++)
            {
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                float minZ = float.MaxValue;
                float maxZ = float.MinValue;
                for (int k = 0; k < 3; k++)
                {
                    int v = Triangles[f * 3 + k] * 3;
                    minX = Math.Min(minX, Vertices[v]);
                    maxX = Math.Max(maxX, Vertices[v]);
                    minZ = Math.Min(minZ, Vertices[v + 2]);
                    maxZ = Math.Max(maxZ, Vertices[v + 2]);
                }

                int x0 = CellX(minX);
                int x1 = CellX(maxX);
                int z0 = CellZ(minZ);
                int z1 = CellZ(maxZ);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        action(z * _cellsX + x, f);
                    }
                }
            }
        }

        private int CellX(float x)
        {
            return Math.Max(0, Math.Min(_cellsX - 1, (int)Math.Floor((x - BoundsMin[0]) / _cellSize)));
        }

        private int CellZ(float z)
        {
            return Math.Max(0, Math.Min(_cellsZ - 1, (int)Math.Floor((z - BoundsMin[2]) / _cellSize)));
        }

        private bool TriangleBoxOverlap(int face, float cx, float cy, float cz, float hx, float hy, float hz)
        {
            int ia = Triangles[face * 3] * 3;
            int ib = Triangles[face * 3 + 1] * 3;
            int ic = Triangles[face * 3 + 2] * 3;
            float v0x = Vertices[ia] - cx;
            float v0y = Vertices[ia + 1] - cy;
            float v0z = Vertices[ia + 2] - cz;
            float v1x = Vertices[ib] - cx;
            float v1y = Vertices[ib + 1] - cy;
            float v1z = Vertices[ib + 2] - cz;
            float v2x = Vertices[ic] - cx;
            float v2y = Vertices[ic + 1] - cy;
            float v2z = Vertices[ic + 2] - cz;
            if (Math.Min(v0x, Math.Min(v1x, v2x)) > hx || Math.Max(v0x, Math.Max(v1x, v2x)) < -hx)
            {
                return false;
            }

            if (Math.Min(v0y, Math.Min(v1y, v2y)) > hy || Math.Max(v0y, Math.Max(v1y, v2y)) < -hy)
            {
                return false;
            }

            if (Math.Min(v0z, Math.Min(v1z, v2z)) > hz || Math.Max(v0z, Math.Max(v1z, v2z)) < -hz)
            {
                return false;
            }

            float e0x = v1x - v0x;
            float e0y = v1y - v0y;
            float e0z = v1z - v0z;
            float e1x = v2x - v1x;
            float e1y = v2y - v1y;
            float e1z = v2z - v1z;
            float e2x = v0x - v2x;
            float e2y = v0y - v2y;
            float e2z = v0z - v2z;
            float nx = e0y * e1z - e0z * e1y;
            float ny = e0z * e1x - e0x * e1z;
            float nz = e0x * e1y - e0y * e1x;
            float d = nx * v0x + ny * v0y + nz * v0z;
            float r = hx * Math.Abs(nx) + hy * Math.Abs(ny) + hz * Math.Abs(nz);
            if (Math.Abs(d) > r)
            {
                return false;
            }

            return AxisTest(e0x, e0y, e0z, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z, hx, hy, hz) && AxisTest(e1x, e1y, e1z, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z, hx, hy, hz) && AxisTest(e2x, e2y, e2z, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z, hx, hy, hz);
        }

        private static bool AxisTest(float ex, float ey, float ez, float v0x, float v0y, float v0z, float v1x, float v1y, float v1z, float v2x, float v2y, float v2z, float hx, float hy, float hz)
        {
            return Separated(0.0f, -ez, ey, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z, hx, hy, hz) && Separated(ez, 0.0f, -ex, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z, hx, hy, hz) && Separated(-ey, ex, 0.0f, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z, hx, hy, hz);
        }

        private static bool Separated(float ax, float ay, float az, float v0x, float v0y, float v0z, float v1x, float v1y, float v1z, float v2x, float v2y, float v2z, float hx, float hy, float hz)
        {
            float p0 = ax * v0x + ay * v0y + az * v0z;
            float p1 = ax * v1x + ay * v1y + az * v1z;
            float p2 = ax * v2x + ay * v2y + az * v2z;
            float r = hx * Math.Abs(ax) + hy * Math.Abs(ay) + hz * Math.Abs(az);
            return !(Math.Min(p0, Math.Min(p1, p2)) > r || Math.Max(p0, Math.Max(p1, p2)) < -r);
        }
    }
}
