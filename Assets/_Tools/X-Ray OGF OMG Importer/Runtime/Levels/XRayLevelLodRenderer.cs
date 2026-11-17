using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    public sealed class XRayLevelLodRenderer : IDisposable
    {
        public const byte STATE_FULL = 0;
        public const byte STATE_BOTH = 1;
        public const byte STATE_IMPOSTOR = 2;
        public const byte STATE_DISCARD = 3;
        public const float EPS = 0.00001f;
        public const float EPS_S = 0.0000001f;
        public const float HEMI_SCALE = 2.0f * 1.55f;

        private static readonly int[] VERTEX_ORDER = { 3, 0, 2, 1 };
        private static readonly VertexAttributeDescriptor[] LAYOUT =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2)
        };

        private readonly Dictionary<Camera, Mesh> _meshes = new Dictionary<Camera, Mesh>();
        private readonly List<Camera> _staleCameras = new List<Camera>();
        private readonly float[] _dots = new float[XRayLevelLodSet.FACES];
        private readonly int[] _order = new int[XRayLevelLodSet.FACES];
        private readonly Plane[] _planes = new Plane[6];
        private byte[] _states;
        private ImpostorVertex[] _vertices;
        private int[] _indices;

        [StructLayout(LayoutKind.Sequential)]
        private struct ImpostorVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 Uv0;
            public Vector2 Uv1;
            public Vector2 FactorAlpha;
            public Vector2 HemiSun;
        }

        public byte[] States => _states;
        public int LastImpostors { get; private set; }
        public int LastDiscarded { get; private set; }
        public int MeshCount => _meshes.Count;

        public void Evaluate(Camera camera, XRayLevelLodSet set)
        {
            int count = set.Count;
            if (_states == null || _states.Length != count)
            {
                _states = new byte[count];
                _vertices = new ImpostorVertex[count * 4];
                _indices = new int[count * 6];
                for (int slot = 0; slot < count; slot++)
                {
                    int index = slot * 6;
                    int quad = slot * 4;
                    _indices[index] = quad;
                    _indices[index + 1] = quad + 1;
                    _indices[index + 2] = quad + 2;
                    _indices[index + 3] = quad + 3;
                    _indices[index + 4] = quad + 2;
                    _indices[index + 5] = quad + 1;
                }
            }

            float fov = Mathf.Max(1.0f, camera.fieldOfView);
            float screen = camera.pixelWidth * camera.pixelHeight * (90.0f / fov) * (90.0f / fov) * (EPS_S + set.GeometryLod);
            if (camera.cameraType == CameraType.Game && camera.targetTexture == null)
            {
                float bias = XRayVideoOverrides.LodBias;
                screen *= bias * bias;
            }
            float lodA = (set.SsaLodA / 3.0f) * (set.SsaLodA / 3.0f) / screen;
            float lodB = (set.SsaLodB / 3.0f) * (set.SsaLodB / 3.0f) / screen;
            float discard = set.SsaDiscard * set.SsaDiscard / screen;
            float range = Mathf.Max(EPS_S, lodA - lodB);
            Vector3 eye = camera.transform.position;
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            int impostors = 0;
            int discarded = 0;
            Vector4[] spheres = set.Spheres;
            for (int lod = 0; lod < count; lod++)
            {
                Vector4 sphere = spheres[lod];
                var center = new Vector3(sphere.x, sphere.y, sphere.z);
                float distanceSqr = (center - eye).sqrMagnitude + EPS;
                float ssa = sphere.w / distanceSqr * set.LodFactors[lod];
                if (ssa >= lodA)
                {
                    _states[lod] = STATE_FULL;
                    continue;
                }

                if (ssa < discard)
                {
                    _states[lod] = STATE_DISCARD;
                    discarded++;
                    continue;
                }

                _states[lod] = ssa > lodB ? STATE_BOTH : STATE_IMPOSTOR;
                if (!InFrustum(center, sphere.w))
                {
                    continue;
                }

                float alpha = Mathf.Clamp(Mathf.Floor((1.0f - (ssa - lodB) / range) * 255.0f), 0.0f, 255.0f) / 255.0f;
                AppendImpostor(set, lod, center, sphere.w, eye, alpha, impostors++);
            }

            LastImpostors = impostors;
            LastDiscarded = discarded;
            Mesh mesh = GetMesh(camera);
            UploadMesh(mesh, impostors);
        }

        public void Render(Camera camera, XRayLevelLodSet set, int layer)
        {
            if (LastImpostors == 0 || set.Material == null || !_meshes.TryGetValue(camera, out Mesh mesh))
            {
                return;
            }

            var parameters = new RenderParams(set.Material)
            {
                camera = camera,
                worldBounds = mesh.bounds,
                layer = layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true
            };
            Graphics.RenderMesh(parameters, mesh, 0, Matrix4x4.identity);
        }

        public void Dispose()
        {
            foreach (Mesh mesh in _meshes.Values)
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            _meshes.Clear();
        }

        private bool InFrustum(Vector3 center, float radius)
        {
            for (int i = 0; i < _planes.Length; i++)
            {
                if (_planes[i].GetDistanceToPoint(center) < -radius)
                {
                    return false;
                }
            }

            return true;
        }

        private void AppendImpostor(XRayLevelLodSet set, int lod, Vector3 center, float radius, Vector3 eye, float alpha, int slot)
        {
            Vector3 direction = (center - eye).normalized;
            Vector3 shift = direction * (-0.5f * radius);
            int faceBase = lod * XRayLevelLodSet.FACES;
            for (int face = 0; face < XRayLevelLodSet.FACES; face++)
            {
                _dots[face] = Vector3.Dot(direction, set.Normals[faceBase + face]);
                _order[face] = face;
            }

            Array.Sort(_dots, _order);
            float dotBest = _dots[XRayLevelLodSet.FACES - 1];
            float dotNext = _dots[XRayLevelLodSet.FACES - 2];
            float dotThird = _dots[XRayLevelLodSet.FACES - 3];
            int best = _order[XRayLevelLodSet.FACES - 1];
            int next = _order[XRayLevelLodSet.FACES - 2];
            float spread = dotBest - dotThird;
            float blend = spread > 1e-6f ? 0.5f + 0.5f * (1.0f - (dotNext - dotThird) / spread) : 1.0f;
            float factor = Mathf.Clamp(Mathf.Floor(blend * 255.5f), 0.0f, 255.0f) / 255.0f;
            Vector3 normalNext = set.Normals[faceBase + next];
            Vector3 normalBest = set.Normals[faceBase + best];
            int vertexBase = lod * XRayLevelLodSet.VERTICES;
            for (int corner = 0; corner < 4; corner++)
            {
                int id = VERTEX_ORDER[corner];
                int nextVertex = vertexBase + next * 4 + id;
                int bestVertex = vertexBase + best * 4 + id;
                _vertices[slot * 4 + corner] = new ImpostorVertex
                {
                    Position = Vector3.Lerp(set.Positions[nextVertex] + shift, set.Positions[bestVertex] + shift, factor),
                    Normal = Vector3.Lerp(normalNext, normalBest, factor),
                    Uv0 = set.Uvs[nextVertex],
                    Uv1 = set.Uvs[bestVertex],
                    FactorAlpha = new Vector2(factor, alpha),
                    HemiSun = new Vector2(Mathf.Lerp(set.Hemi[nextVertex], set.Hemi[bestVertex], factor) * HEMI_SCALE, Mathf.Lerp(set.Sun[nextVertex], set.Sun[bestVertex], factor))
                };
            }
        }

        private Mesh GetMesh(Camera camera)
        {
            if (!_meshes.TryGetValue(camera, out Mesh mesh) || mesh == null)
            {
                RemoveDestroyedCameras();
                mesh = new Mesh { name = "X-Ray LOD impostors", hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
                _meshes[camera] = mesh;
            }

            return mesh;
        }

        private void RemoveDestroyedCameras()
        {
            _staleCameras.Clear();
            foreach (KeyValuePair<Camera, Mesh> pair in _meshes)
            {
                if (pair.Key == null)
                {
                    _staleCameras.Add(pair.Key);
                }
            }

            foreach (Camera camera in _staleCameras)
            {
                Mesh mesh = _meshes[camera];
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }

                _meshes.Remove(camera);
            }

            _staleCameras.Clear();
        }

        private void UploadMesh(Mesh mesh, int impostors)
        {
            const MeshUpdateFlags FLAGS = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
            int capacity = _states.Length;
            if (mesh.vertexCount != capacity * 4)
            {
                mesh.subMeshCount = 0;
                mesh.SetVertexBufferParams(capacity * 4, LAYOUT);
                mesh.SetIndexBufferParams(capacity * 6, IndexFormat.UInt32);
                mesh.SetIndexBufferData(_indices, 0, 0, capacity * 6, FLAGS);
            }

            if (impostors == 0)
            {
                mesh.subMeshCount = 0;
                return;
            }

            int vertexCount = impostors * 4;
            mesh.SetVertexBufferData(_vertices, 0, 0, vertexCount, 0, FLAGS);
            Vector3 min = _vertices[0].Position;
            Vector3 max = min;
            for (int i = 1; i < vertexCount; i++)
            {
                min = Vector3.Min(min, _vertices[i].Position);
                max = Vector3.Max(max, _vertices[i].Position);
            }

            var bounds = new Bounds((min + max) * 0.5f, max - min);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, impostors * 6) { bounds = bounds, vertexCount = vertexCount }, FLAGS);
            mesh.bounds = bounds;
        }
    }
}
