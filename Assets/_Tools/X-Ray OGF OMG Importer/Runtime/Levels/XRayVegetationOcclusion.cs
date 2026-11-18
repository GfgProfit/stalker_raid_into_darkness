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
        private const int MESH_RASTER_TILE_RESERVE = MAX_RASTER_TILE_TESTS / 2;
        private const int RASTER_TILE_BLOCK = 8;
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
        private int _nearestCorner;
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
            float centerTileDepth = _depths[(int)center.y * WIDTH + (int)center.x] + DEPTH_MARGIN;
            if (centerTileDepth >= center.z) return false;

            Vector3 lower = bounds.min;
            Vector3 upper = bounds.max;
            if (!Project(GetCorner(lower, upper, _nearestCorner), out Vector3 nearest) || centerTileDepth >= nearest.z) return false;
            Vector2 min = Vector2.Min(center, nearest);
            Vector2 max = Vector2.Max(center, nearest);
            float nearestDepth = Mathf.Min(center.z, nearest.z);
            for (int offset = 1; offset < 8; offset++)
            {
                if (!Project(GetCorner(lower, upper, _nearestCorner ^ offset), out Vector3 projected)) return false;
                min = Vector2.Min(min, projected);
                max = Vector2.Max(max, projected);
                nearestDepth = Mathf.Min(nearestDepth, projected.z);
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
                    if (_depths[row + x] + DEPTH_MARGIN >= nearestDepth) return false;
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
            _nearestCorner = (view.m20 > 0.0f ? 1 : 0) | (view.m21 > 0.0f ? 2 : 0) | (view.m22 > 0.0f ? 4 : 0);
            _rasterTestsRemaining = MAX_RASTER_TILE_TESTS;
            _coveredTiles = 0;
            Array.Fill(_depths, float.PositiveInfinity);
        }

        internal void AddTriangle(Vector3 a, Vector3 b, Vector3 c, bool doubleSided)
        {
            RasterizeTriangle(a, b, c, doubleSided, 0);
        }

        internal void AddHomTriangle(Vector3 a, Vector3 b, Vector3 c, bool doubleSided)
        {
            RasterizeTriangle(a, b, c, doubleSided, MESH_RASTER_TILE_RESERVE);
        }

        private static Vector3 GetCorner(Vector3 lower, Vector3 upper, int corner)
        {
            return new Vector3((corner & 1) == 0 ? lower.x : upper.x, (corner & 2) == 0 ? lower.y : upper.y, (corner & 4) == 0 ? lower.z : upper.z);
        }

        private void RasterizeTriangle(Vector3 a, Vector3 b, Vector3 c, bool doubleSided, int reserve)
        {
            if (_rasterTestsRemaining <= reserve || !Project(a, out Vector3 p) || !Project(b, out Vector3 q) || !Project(c, out Vector3 r)) return;
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
            int width = endX - startX + 1;
            if (width <= 0 || endY < startY) return;
            var first = new Edge(p, q);
            var second = new Edge(q, r);
            var third = new Edge(r, p);
            var depthPlane = new DepthPlane(p, q, r, Mathf.Abs(signedArea), _perspective);
            for (int y = startY; y <= endY; y++)
            {
                int rowTiles = Math.Min(width, _rasterTestsRemaining - reserve);
                if (rowTiles <= 0) return;
                _rasterTestsRemaining -= rowTiles;
                int rowStart = startX;
                int rowEnd = startX + rowTiles - 1;
                if (rowTiles >= RASTER_TILE_BLOCK * 2)
                {
                    while (rowStart + RASTER_TILE_BLOCK - 1 <= rowEnd && !MayCover(first, second, third, rowStart, rowStart + RASTER_TILE_BLOCK - 1, y))
                    {
                        rowStart += RASTER_TILE_BLOCK;
                    }

                    while (rowEnd - RASTER_TILE_BLOCK + 1 >= rowStart && !MayCover(first, second, third, rowEnd - RASTER_TILE_BLOCK + 1, rowEnd, y))
                    {
                        rowEnd -= RASTER_TILE_BLOCK;
                    }
                }

                int row = y * WIDTH;
                for (int x = rowStart; x <= rowEnd; x++)
                {
                    int index = row + x;
                    if (min.z >= _depths[index] || !first.Covers(x, y) || !second.Covers(x, y) || !third.Covers(x, y)) continue;
                    float depth = depthPlane.Farthest(x, y, min.z, max.z, _perspective);
                    if (depth >= _depths[index]) continue;
                    if (float.IsPositiveInfinity(_depths[index])) _coveredTiles++;
                    _depths[index] = depth;
                }
            }
        }

        private static bool MayCover(in Edge first, in Edge second, in Edge third, int startX, int endX, int y)
        {
            return first.MayCover(startX, endX, y) && second.MayCover(startX, endX, y) && third.MayCover(startX, endX, y);
        }

        private bool Project(Vector3 point, out Vector3 projected)
        {
            projected = default;
            float depth = -(_view.m20 * point.x + _view.m21 * point.y + _view.m22 * point.z + _view.m23);
            if (!(depth > _near && depth < _far)) return false;
            float w = _viewProjection.m30 * point.x + _viewProjection.m31 * point.y + _viewProjection.m32 * point.z + _viewProjection.m33;
            if (!(w > 0.0f)) return false;
            float clipX = _viewProjection.m00 * point.x + _viewProjection.m01 * point.y + _viewProjection.m02 * point.z + _viewProjection.m03;
            float clipY = _viewProjection.m10 * point.x + _viewProjection.m11 * point.y + _viewProjection.m12 * point.z + _viewProjection.m13;
            float x = (clipX / w + 1.0f) * (WIDTH * 0.5f);
            float y = (clipY / w + 1.0f) * (HEIGHT * 0.5f);
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

            public bool MayCover(int startX, int endX, int y) => Covers(_x < 0.0f ? startX : endX, y);
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
