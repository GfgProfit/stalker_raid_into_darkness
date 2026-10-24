using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Core.Level;

namespace XRay.Importer.Editor.Levels
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct XRayLevelMeshVertex
    {
        private readonly Vector3 _position;
        private readonly Vector3 _normal;
        private readonly ushort _tangentX;
        private readonly ushort _tangentY;
        private readonly ushort _tangentZ;
        private readonly ushort _tangentW;
        private readonly Color32 _color;
        private readonly Vector2 _uv0;
        private readonly ushort _binormalX;
        private readonly ushort _binormalY;
        private readonly ushort _binormalZ;
        private readonly ushort _hemi;
        private readonly Vector2 _lightmap;

        public XRayLevelMeshVertex(Vector3 position, Vector3 normal, Vector4 tangent, Color32 color, Vector2 uv0, ushort binormalX, ushort binormalY, ushort binormalZ, ushort hemi, Vector2 lightmap)
        {
            _position = position;
            _normal = normal;
            _tangentX = Mathf.FloatToHalf(tangent.x);
            _tangentY = Mathf.FloatToHalf(tangent.y);
            _tangentZ = Mathf.FloatToHalf(tangent.z);
            _tangentW = Mathf.FloatToHalf(tangent.w);
            _color = color;
            _uv0 = uv0;
            _binormalX = binormalX;
            _binormalY = binormalY;
            _binormalZ = binormalZ;
            _hemi = hemi;
            _lightmap = lightmap;
        }

        public Vector3 Position => _position;
    }

    public sealed class XRayLevelGeometrySlice
    {
        public XRayLevelVertexBlock Vertices { get; set; }
        public int VertexCount { get; set; }
        public ushort[] Indices { get; set; }
        public int ShaderId { get; set; }
    }

    public sealed class XRayLevelMeshBuilder
    {
        public static readonly VertexAttributeDescriptor[] LAYOUT =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm16, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.UNorm16, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2)
        };

        public const string LAYOUT_DESCRIPTION = "POSITION Float32x3 (X-Ray world/local, identity axes); NORMAL Float32x3 (N = NORMAL.bgr*2-1); TANGENT Float16x4 (normalized T, w = sign(dot(cross(N,T), -B))); COLOR UNorm8x4 (exact T bytes, rgb = T.xyz); TEXCOORD0 Float32x2 (u, 1-v) with T.w/B.w correction; TEXCOORD1 UNorm16x2 (exact B.x, B.y bytes); TEXCOORD2 UNorm16x2 (exact B.z byte, NORMAL.w hemi byte); TEXCOORD3 Float32x2 (lightmap uv = short/32768, D3D orientation)";

        private readonly XRayLevelData _level;
        private readonly XRayLevelGeometry _geometry;

        public XRayLevelMeshBuilder(XRayLevelData level, XRayLevelGeometry geometry)
        {
            _level = level;
            _geometry = geometry;
        }

        public XRayLevelGeometrySlice ReadSlice(XRayLevelVisual visual)
        {
            XRayGeometryRange range = visual.Geometry;
            int indexOffset = range.IndexOffset;
            int indexCount = range.IndexCount;
            int vertexCount = range.VertexCount;
            if (visual.ModelType == XRayLevelFormat.MT_PROGRESSIVE && visual.SwiTriangles >= 0)
            {
                indexOffset += visual.SwiOffset;
                indexCount = visual.SwiTriangles * 3;
                vertexCount = Math.Min(range.VertexCount, visual.SwiVertices);
            }
            else if (visual.ModelType == XRayLevelFormat.MT_TREE_PM)
            {
                if (visual.SwiContainer < 0 || visual.SwiContainer >= _geometry.SlideWindows.Count || _geometry.SlideWindows[visual.SwiContainer].Count == 0)
                {
                    throw new Core.IO.XRayFormatException($"Visual {visual.Index}: slide window container {visual.SwiContainer} does not exist in level.geom ({_geometry.SlideWindows.Count}).");
                }

                XRaySlideWindow window = _geometry.SlideWindows[visual.SwiContainer][0];
                indexOffset += window.Offset;
                indexCount = window.Triangles * 3;
                vertexCount = Math.Min(range.VertexCount, window.Vertices);
            }

            ushort[] indices = _geometry.ReadIndices(range.IndexBuffer, indexOffset, indexCount);
            foreach (ushort index in indices)
            {
                if (index >= vertexCount)
                {
                    throw new Core.IO.XRayFormatException($"Visual {visual.Index}: index {index} exceeds {vertexCount} vertices.");
                }
            }

            return new XRayLevelGeometrySlice { Vertices = _geometry.ReadVertices(range.VertexBuffer, range.VertexOffset, vertexCount), VertexCount = vertexCount, Indices = indices, ShaderId = visual.ShaderId };
        }

        public Mesh BuildMesh(string name, List<XRayLevelGeometrySlice> slices, List<int> submeshShaders, Mesh target)
        {
            var groups = new SortedDictionary<int, List<XRayLevelGeometrySlice>>();
            int vertexTotal = 0;
            foreach (XRayLevelGeometrySlice slice in slices)
            {
                if (!groups.TryGetValue(slice.ShaderId, out List<XRayLevelGeometrySlice> list))
                {
                    list = new List<XRayLevelGeometrySlice>();
                    groups.Add(slice.ShaderId, list);
                }

                list.Add(slice);
                vertexTotal += slice.VertexCount;
            }

            Mesh mesh = target != null ? target : new Mesh();
            mesh.Clear();
            mesh.name = name;
            var vertices = new NativeArray<XRayLevelMeshVertex>(vertexTotal, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            try
            {
                var indices = new List<int>();
                var descriptors = new List<SubMeshDescriptor>();
                int vertexBase = 0;
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                submeshShaders.Clear();
                foreach (KeyValuePair<int, List<XRayLevelGeometrySlice>> group in groups)
                {
                    int indexStart = indices.Count;
                    Vector3 groupMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3 groupMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    foreach (XRayLevelGeometrySlice slice in group.Value)
                    {
                        for (int v = 0; v < slice.VertexCount; v++)
                        {
                            XRayLevelMeshVertex vertex = Convert(slice.Vertices, v);
                            vertices[vertexBase + v] = vertex;
                            groupMin = Vector3.Min(groupMin, vertex.Position);
                            groupMax = Vector3.Max(groupMax, vertex.Position);
                        }

                        foreach (ushort index in slice.Indices)
                        {
                            indices.Add(vertexBase + index);
                        }

                        vertexBase += slice.VertexCount;
                    }

                    var bounds = new Bounds((groupMin + groupMax) * 0.5f, groupMax - groupMin);
                    descriptors.Add(new SubMeshDescriptor(indexStart, indices.Count - indexStart) { bounds = bounds });
                    submeshShaders.Add(group.Key);
                    min = Vector3.Min(min, groupMin);
                    max = Vector3.Max(max, groupMax);
                }

                mesh.SetVertexBufferParams(vertexTotal, LAYOUT);
                mesh.SetVertexBufferData(vertices, 0, 0, vertexTotal, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                IndexFormat format = vertexTotal > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetIndexBufferParams(indices.Count, format);
                if (format == IndexFormat.UInt32)
                {
                    mesh.SetIndexBufferData(indices, 0, 0, indices.Count, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }
                else
                {
                    var shortIndices = new ushort[indices.Count];
                    for (int i = 0; i < shortIndices.Length; i++)
                    {
                        shortIndices[i] = (ushort)indices[i];
                    }

                    mesh.SetIndexBufferData(shortIndices, 0, 0, shortIndices.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }

                mesh.subMeshCount = descriptors.Count;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    mesh.SetSubMesh(i, descriptors[i], MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }

                mesh.bounds = vertexTotal > 0 ? new Bounds((min + max) * 0.5f, max - min) : new Bounds();
            }
            finally
            {
                vertices.Dispose();
            }

            return mesh;
        }

        public static XRayLevelMeshVertex Convert(XRayLevelVertexBlock block, int v)
        {
            int p = v * 3;
            int c = v * 4;
            Vector3 normal = Decode(block.Normals, c);
            Vector3 tangent = Decode(block.Tangents, c);
            Vector3 binormal = Decode(block.Binormals, c);
            Vector3 unitTangent = tangent.sqrMagnitude > 1e-12f ? tangent.normalized : Vector3.right;
            float sign = Vector3.Dot(Vector3.Cross(normal, unitTangent), -binormal) < 0.0f ? -1.0f : 1.0f;
            var position = new Vector3(block.Positions[p], block.Positions[p + 1], block.Positions[p + 2]);
            var color = new Color32(block.Tangents[c + 2], block.Tangents[c + 1], block.Tangents[c], 255);
            var uv = new Vector2(block.Uv[v * 2], 1.0f - block.Uv[v * 2 + 1]);
            Vector2 lightmap = block.HasLightmap ? new Vector2(block.Lightmap[v * 2], block.Lightmap[v * 2 + 1]) : Vector2.zero;
            return new XRayLevelMeshVertex(position, normal, new Vector4(unitTangent.x, unitTangent.y, unitTangent.z, sign), color, uv, (ushort)(block.Binormals[c + 2] * 257), (ushort)(block.Binormals[c + 1] * 257), (ushort)(block.Binormals[c] * 257), (ushort)(block.Normals[c + 3] * 257), lightmap);
        }

        public static Vector3 Decode(byte[] packed, int offset)
        {
            return new Vector3(packed[offset + 2] / 255.0f * 2.0f - 1.0f, packed[offset + 1] / 255.0f * 2.0f - 1.0f, packed[offset] / 255.0f * 2.0f - 1.0f);
        }

        public static Matrix4x4 ToUnityMatrix(float[] rowMajor)
        {
            var matrix = new Matrix4x4();
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    matrix[row, column] = rowMajor[column * 4 + row];
                }
            }

            return matrix;
        }
    }
}
