using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayVegetationOcclusion
    {
        private const int WIDTH = 96;
        private const int HEIGHT = 54;
        private const int MAX_LEVELS = 4;
        private const int MAX_RASTER_TILE_TESTS = 180000;
        private const int MAX_QUERY_TILES = 48;
        private const float TILE_MARGIN = 0.005f;
        private const float DEPTH_MARGIN = 0.05f;
        private const float MAX_FLORA_HEIGHT = 5.0f;
        private const float MAX_FLORA_WIDTH = 8.0f;
        private const string FLORA_SHADER = "HDRP/MonolithLit";
        private const string FLORA_KEYWORD = "_MONOLITH_LEVEL_FLORA";

        private static readonly List<LevelCache> _levels = new List<LevelCache>(MAX_LEVELS);

        private static GameConfig _configuration;

        private readonly float[] _depths = new float[WIDTH * HEIGHT];

        private Matrix4x4 _view;
        private Matrix4x4 _viewProjection;
        private float _near;
        private float _far;
        private bool _perspective;
        private int _rasterTestsRemaining;
        private int _coveredTiles;

        public static XRayVegetationOcclusion Begin(Camera camera, Transform levelRoot)
        {
            if (!Application.isPlaying || camera == null || camera.cameraType != CameraType.Game || camera != Camera.main || camera.stereoEnabled || GL.invertCulling || levelRoot == null) return null;
            if (_configuration == null) _configuration = GameConfig.Load();
            if (_configuration != null && !_configuration.VegetationOcclusionEnabled) return null;

            LevelCache level = null;
            for (int i = _levels.Count - 1; i >= 0; i--)
            {
                LevelCache entry = _levels[i];
                if (entry.Root == null || entry.Geometry == null) _levels.RemoveAt(i);
                else if (entry.Root == levelRoot) level = entry;
            }

            if (level == null)
            {
                Transform geometry = levelRoot.Find("Geometry");
                if (geometry == null) return null;
                if (_levels.Count == MAX_LEVELS)
                {
                    int oldest = 0;
                    for (int i = 1; i < _levels.Count; i++)
                    {
                        if (_levels[i].Frame < _levels[oldest].Frame) oldest = i;
                    }

                    _levels.RemoveAt(oldest);
                }

                level = new LevelCache(levelRoot, geometry);
                _levels.Add(level);
            }

            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 projection = camera.nonJitteredProjectionMatrix;
            if (!StandardProjection(projection, camera.orthographic)) return null;
            Matrix4x4 viewProjection = projection * view;
            if (level.Frame != Time.frameCount || level.Camera != camera || !level.Query._viewProjection.Equals(viewProjection))
            {
                level.Camera = camera;
                level.Frame = Time.frameCount;
                level.Query.Reset(view, projection, camera.nearClipPlane, camera.farClipPlane);
                level.Occluders.Rasterize(camera, level.Query);
            }

            return level.Query._coveredTiles != 0 ? level.Query : null;
        }

        public static bool IsFlora(Material[] materials)
        {
            if (materials == null || materials.Length == 0) return false;
            foreach (Material material in materials)
            {
                if (!IsFlora(material)) return false;
            }

            return true;
        }

        public static bool IsFlora(Material material)
        {
            return material != null && material.shader != null && material.shader.name == FLORA_SHADER && material.IsKeywordEnabled(FLORA_KEYWORD);
        }

        public static bool IsSmallFlora(Bounds bounds)
        {
            Vector3 size = bounds.size;
            return size.x > 0.0f && size.y > 0.0f && size.z > 0.0f && size.x <= MAX_FLORA_WIDTH && size.y <= MAX_FLORA_HEIGHT && size.z <= MAX_FLORA_WIDTH;
        }

        public bool IsOccluded(Bounds bounds)
        {
            if (_coveredTiles == 0) return false;
            if (!Project(bounds.center, out Vector3 center)) return false;
            if (center.x < 0.0f || center.x >= WIDTH || center.y < 0.0f || center.y >= HEIGHT) return false;
            if (_depths[(int)center.y * WIDTH + (int)center.x] + DEPTH_MARGIN >= center.z) return false;

            Vector3 lower = bounds.min;
            Vector3 upper = bounds.max;
            Vector3 min = center;
            Vector3 max = center;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? lower.x : upper.x, (corner & 2) == 0 ? lower.y : upper.y, (corner & 4) == 0 ? lower.z : upper.z);
                if (!Project(point, out Vector3 projected)) return false;
                min = Vector3.Min(min, projected);
                max = Vector3.Max(max, projected);
            }

            int startX = Mathf.FloorToInt(min.x - TILE_MARGIN);
            int startY = Mathf.FloorToInt(min.y - TILE_MARGIN);
            int endX = Mathf.FloorToInt(max.x + TILE_MARGIN);
            int endY = Mathf.FloorToInt(max.y + TILE_MARGIN);
            if (startX < 0 || startY < 0 || endX >= WIDTH || endY >= HEIGHT || (endX - startX + 1) * (endY - startY + 1) > MAX_QUERY_TILES) return false;
            for (int y = startY; y <= endY; y++)
            {
                int row = y * WIDTH;
                for (int x = startX; x <= endX; x++)
                {
                    if (_depths[row + x] + DEPTH_MARGIN >= min.z) return false;
                }
            }

            return true;
        }

        internal void Reset(Matrix4x4 view, Matrix4x4 projection, float near, float far)
        {
            _view = view;
            _viewProjection = projection * view;
            _near = near + DEPTH_MARGIN;
            _far = far;
            _perspective = projection.m32 != 0.0f;
            _rasterTestsRemaining = MAX_RASTER_TILE_TESTS;
            _coveredTiles = 0;
            Array.Fill(_depths, float.PositiveInfinity);
        }

        internal void AddTriangle(Vector3 a, Vector3 b, Vector3 c, bool doubleSided)
        {
            if (_rasterTestsRemaining <= 0 || !Project(a, out Vector3 p) || !Project(b, out Vector3 q) || !Project(c, out Vector3 r)) return;
            float signedArea = (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            if (!float.IsFinite(signedArea) || Mathf.Abs(signedArea) < TILE_MARGIN || !doubleSided && signedArea >= 0.0f) return;
            if (signedArea < 0.0f) (q, r) = (r, q);

            Vector3 min = Vector3.Min(Vector3.Min(p, q), r);
            Vector3 max = Vector3.Max(Vector3.Max(p, q), r);
            if (max.x - min.x < 1.0f || max.y - min.y < 1.0f || max.x < 1.0f || max.y < 1.0f || min.x >= WIDTH - 1 || min.y >= HEIGHT - 1) return;
            int startX = Mathf.CeilToInt(Mathf.Max(0.0f, min.x));
            int startY = Mathf.CeilToInt(Mathf.Max(0.0f, min.y));
            int endX = Mathf.FloorToInt(Mathf.Min(WIDTH, max.x)) - 1;
            int endY = Mathf.FloorToInt(Mathf.Min(HEIGHT, max.y)) - 1;
            var first = new Edge(p, q);
            var second = new Edge(q, r);
            var third = new Edge(r, p);
            var depthPlane = new DepthPlane(p, q, r, Mathf.Abs(signedArea), _perspective);
            for (int y = startY; y <= endY; y++)
            {
                int row = y * WIDTH;
                for (int x = startX; x <= endX; x++)
                {
                    if (--_rasterTestsRemaining < 0) return;
                    int index = row + x;
                    if (min.z >= _depths[index] || !first.Covers(x, y) || !second.Covers(x, y) || !third.Covers(x, y)) continue;
                    float depth = depthPlane.Farthest(x, y, min.z, max.z, _perspective);
                    if (depth >= _depths[index]) continue;
                    if (float.IsPositiveInfinity(_depths[index])) _coveredTiles++;
                    _depths[index] = depth;
                }
            }
        }

        private bool Project(Vector3 point, out Vector3 projected)
        {
            projected = default;
            float depth = -(_view.m20 * point.x + _view.m21 * point.y + _view.m22 * point.z + _view.m23);
            if (!(depth > _near && depth < _far)) return false;
            Vector4 clip = _viewProjection * new Vector4(point.x, point.y, point.z, 1.0f);
            if (!(clip.w > 0.0f)) return false;
            float x = (clip.x / clip.w + 1.0f) * (WIDTH * 0.5f);
            float y = (clip.y / clip.w + 1.0f) * (HEIGHT * 0.5f);
            projected = new Vector3(x, y, depth);
            return float.IsFinite(x) && float.IsFinite(y) && Mathf.Abs(x) < 1e6f && Mathf.Abs(y) < 1e6f;
        }

        private static bool StandardProjection(Matrix4x4 matrix, bool orthographic)
        {
            return Mathf.Abs(matrix.m20) < 1e-6f && Mathf.Abs(matrix.m21) < 1e-6f && Mathf.Abs(matrix.m30) < 1e-6f && Mathf.Abs(matrix.m31) < 1e-6f && matrix.m32 == (orthographic ? 0.0f : -1.0f) && matrix.m33 == (orthographic ? 1.0f : 0.0f) && matrix.m00 > 0.0f && matrix.m11 > 0.0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Clear()
        {
            _levels.Clear();
            _configuration = null;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            for (int i = _levels.Count - 1; i >= 0; i--)
            {
                if (_levels[i].Root == null || _levels[i].Root.gameObject.scene == scene) _levels.RemoveAt(i);
            }
        }

        private readonly struct Edge
        {
            private readonly float _x;
            private readonly float _y;
            private readonly float _offset;

            public Edge(Vector3 a, Vector3 b)
            {
                _x = a.y - b.y;
                _y = b.x - a.x;
                _offset = -_x * a.x - _y * a.y + Mathf.Min(0.0f, _x) + Mathf.Min(0.0f, _y) - TILE_MARGIN * (Mathf.Abs(_x) + Mathf.Abs(_y));
            }

            public bool Covers(int x, int y) => _x * x + _y * y + _offset > 0.0f;
        }

        private readonly struct DepthPlane
        {
            private readonly double _x;
            private readonly double _y;
            private readonly double _offset;

            public DepthPlane(Vector3 a, Vector3 b, Vector3 c, double area, bool perspective)
            {
                double depthA = perspective ? 1.0 / a.z : -a.z;
                double depthB = perspective ? 1.0 / b.z : -b.z;
                double depthC = perspective ? 1.0 / c.z : -c.z;
                _x = ((depthB - depthA) * (c.y - a.y) - (depthC - depthA) * (b.y - a.y)) / area;
                _y = ((b.x - a.x) * (depthC - depthA) - (c.x - a.x) * (depthB - depthA)) / area;
                _offset = depthA - _x * a.x - _y * a.y + Math.Min(0.0, _x) + Math.Min(0.0, _y) - TILE_MARGIN * (Math.Abs(_x) + Math.Abs(_y));
            }

            public float Farthest(int x, int y, float min, float max, bool perspective)
            {
                double value = _x * x + _y * y + _offset;
                if (perspective && value <= 0.0) return max;
                double depth = perspective ? 1.0 / value : -value;
                if (!double.IsFinite(depth) || depth < min - DEPTH_MARGIN) return max;
                return Mathf.Min(max, (float)depth + DEPTH_MARGIN + max * 0.0001f);
            }
        }

        private sealed class LevelCache
        {
            public LevelCache(Transform root, Transform geometry)
            {
                Root = root;
                Geometry = geometry;
                Occluders = new XRayVegetationOccluders(geometry);
            }

            public Transform Root { get; }
            public Transform Geometry { get; }
            public XRayVegetationOccluders Occluders { get; }
            public XRayVegetationOcclusion Query { get; } = new XRayVegetationOcclusion();
            public Camera Camera { get; set; }
            public int Frame { get; set; } = -1;
        }
    }
}
