using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Dds;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayPrecipitationPoseLocator
    {
        private const string PRODUCTION = "E:\\Projects\\stalker_raid_into_darkness";
        private const string ARCHIVE = "E:\\Projects\\stalker_raid_into_darkness_Archive\\";

        public static string Begin(string archive, string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Load the requested location additively before locating poses; the locator never opens scenes.");
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
                foreach (Camera camera in cameras)
                {
                    Vector3 position = camera.transform.position;
                    return BeginInRegion(archive, scenePath, position.x, position.z, 48.0f);
                }
            }
            throw new InvalidOperationException("No scene camera defines a bounded inspection region; call BeginInRegion with an observed camera XZ position.");
        }

        public static string BeginInRegion(string archive, string scenePath, float centerX, float centerZ, float radius)
        {
            if (!float.IsFinite(centerX) || !float.IsFinite(centerZ) || !float.IsFinite(radius) || radius < 24.0f || radius > 96.0f) throw new ArgumentOutOfRangeException(nameof(radius), "Use a finite observed XZ center and a bounded radius between 24 and 96 metres.");
            string folder = Path.GetFullPath(archive);
            if (!folder.StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Pose evidence must remain under Archive.", nameof(archive));
            string output = Path.Combine(folder, "CpuPoseLocator_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            var status = new Status { state = "Queued" };
            string statusPath = Path.Combine(output, "Status.json");
            File.WriteAllText(statusPath, JsonUtility.ToJson(status, true));
            EditorApplication.CallbackFunction execute = null;
            execute = () =>
            {
                EditorApplication.update -= execute;
                status.state = "Running";
                File.WriteAllText(statusPath, JsonUtility.ToJson(status, true));
                try
                {
                    status.result = Report(output, scenePath, new Vector2(centerX, centerZ), radius);
                    status.state = "Complete";
                }
                catch (Exception exception)
                {
                    status.state = "Failed";
                    status.error = exception.ToString();
                }
                File.WriteAllText(statusPath, JsonUtility.ToJson(status, true));
            };
            EditorApplication.update += execute;
            return statusPath;
        }

        private static string Report(string output, string scenePath, Vector2 center, float radius)
        {
            if (!string.Equals(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), PRODUCTION, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Inspect the Production project only.");
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Load the requested location additively before locating poses; the locator never opens scenes.");
            bool dirty = scene.isDirty;
            var locator = new Locator(center, radius);
            locator.Collect(scene);
            PoseReport report = locator.Find();
            report.scene = scene.path;
            report.sceneDirtyUnchanged = scene.isDirty == dirty;
            string path = Path.Combine(output, "Poses.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return path;
        }

        private sealed class Locator
        {
            private const int MAX_TRIANGLES = 2000000;
            private const int MAX_REFERENCES = 8000000;
            private const int MAX_SEEDS = 30000;
            private const int MAX_QUERY_TESTS = 80000000;
            private const int MAX_ROOM_SEARCH = 16;
            private const int MAX_WALK_NODES = 1800;
            private const float EYE_HEIGHT = 1.65f;
            private const float MIN_ROOM_HEIGHT = 2.4f;
            private const float WALK_STEP = 0.6f;
            private const float PATCH_RADIUS = 1.5f;

            private readonly List<Triangle> _triangles = new List<Triangle>();
            private readonly Dictionary<Mesh, MeshCopy> _meshes = new Dictionary<Mesh, MeshCopy>();
            private readonly Dictionary<Material, int> _materialIds = new Dictionary<Material, int>();
            private readonly Dictionary<int, TerrainMask> _terrainMasks = new Dictionary<int, TerrainMask>();
            private readonly List<MaterialRecord> _materials = new List<MaterialRecord>();
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly List<Hit> _hits = new List<Hit>();
            private readonly List<Hit> _merged = new List<Hit>();
            private readonly HashSet<int> _queryIds = new HashSet<int>();
            private readonly HashSet<long> _seedKeys = new HashSet<long>();
            private readonly List<Vector2> _seeds = new List<Vector2>();
            private readonly List<Room> _rooms = new List<Room>();
            private readonly List<Room> _intervals = new List<Room>();
            private readonly List<PoseRecord> _poses = new List<PoseRecord>();
            private readonly Vector2 _center;
            private readonly float _radius;
            private Bounds _bounds;
            private bool _hasBounds;
            private float _cellSize;
            private int _references;
            private int _queryTests;
            private int _instances;
            private int _meshRenderers;
            private int _rejectedBounds;
            private int _rejectedTriangles;
            private TerrainMask _militaryMask;

            public Locator(Vector2 center, float radius)
            {
                _center = center;
                _radius = radius;
            }

            public void Collect(Scene scene)
            {
                var filters = new List<MeshFilter>();
                var drawers = new List<XRayLevelInstancedRenderer>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(true, filters);
                    foreach (MeshFilter filter in filters)
                    {
                        MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || filter.sharedMesh == null) continue;
                        Add(filter.sharedMesh, renderer.sharedMaterials, filter.transform.localToWorldMatrix, false);
                        _meshRenderers++;
                    }
                    root.GetComponentsInChildren(true, drawers);
                    foreach (XRayLevelInstancedRenderer drawer in drawers)
                    {
                        if (!drawer.isActiveAndEnabled || drawer.BatchSet == null) continue;
                        foreach (XRayInstanceBatch batch in drawer.BatchSet.Batches)
                        {
                            if (batch.Mesh == null || batch.Materials == null) continue;
                            foreach (Matrix4x4 matrix in batch.Matrices)
                            {
                                Add(batch.Mesh, batch.Materials, matrix, true);
                                _instances++;
                            }
                        }
                    }
                }
                if (!_hasBounds) throw new InvalidDataException("No eligible opaque geometry was found.");
                _cellSize = Mathf.Max(2.0f, Mathf.Sqrt(Mathf.Max(1.0f, _bounds.size.x * _bounds.size.z) / 100000.0f));
                for (int i = 0; i < _triangles.Count; i++) Index(i);
            }

            public PoseReport Find()
            {
                SurfacePose(XRayWetSurfaceType.Soil, "Earth");
                SurfacePose(XRayWetSurfaceType.Soil, "AsphaltConcrete", true);
                AliasAsphalt();
                if (!HasPose("AsphaltConcrete")) SurfacePose(XRayWetSurfaceType.Asphalt, "AsphaltConcrete");
                if (!HasPose("AsphaltConcrete")) SurfacePose(XRayWetSurfaceType.Concrete, "AsphaltConcrete");
                SurfacePose(XRayWetSurfaceType.Concrete, "Concrete");
                SurfacePose(XRayWetSurfaceType.Soil, "Puddles", true);
                if (!HasPose("Puddles")) SurfacePose(XRayWetSurfaceType.Asphalt, "Puddles");
                if (!HasPose("Puddles")) SurfacePose(XRayWetSurfaceType.Concrete, "Puddles");
                if (!HasPose("Puddles")) SurfacePose(XRayWetSurfaceType.Soil, "Puddles");
                _seeds.Sort((a, b) => (a - _center).sqrMagnitude.CompareTo((b - _center).sqrMagnitude));
                foreach (Vector2 seed in _seeds)
                {
                    Column(seed);
                    _intervals.Clear();
                    for (int i = 0; i + 1 < _merged.Count; i++)
                    {
                        Hit floor = _merged[i];
                        Hit ceiling = _merged[i + 1];
                        float gap = ceiling.Y - floor.Y;
                        if (!floor.Up || !ceiling.Down || gap < MIN_ROOM_HEIGHT || gap > 9.0f) continue;
                        _intervals.Add(new Room(seed, floor.Y, ceiling.Y, floor.Material, ceiling.Material));
                    }
                    foreach (Room room in _intervals)
                    {
                        if (Footprint(room, 1) < 7) continue;
                        int coherent = Footprint(room, 6);
                        if (coherent < 156) continue;
                        int walls = Walls(room);
                        room.Coherent = coherent;
                        room.Walls = walls;
                        if (walls >= 6 && !HasRoomNear(room)) _rooms.Add(room);
                        else if (walls <= 3 && !HasPose("Canopy")) AddPose("Canopy", room.Eye, Vector3.forward, room.Floor, room.Ceiling, true, "coherent covered air interval; lateral walls=" + walls + "; footprint=" + coherent + "/169", coherent, walls);
                    }
                }
                _rooms.Sort((a, b) => (b.Walls * 1000 + b.Coherent).CompareTo(a.Walls * 1000 + a.Coherent));
                int searches = 0;
                foreach (Room room in _rooms)
                {
                    if (++searches > MAX_ROOM_SEARCH) break;
                    if (Door(room)) break;
                }
                if (!HasPose("ClosedBuilding") && _rooms.Count > 0)
                {
                    Room room = _rooms[0];
                    AddPose("ClosedBuilding", room.Eye, Vector3.forward, room.Floor, room.Ceiling, true, "coherent enclosed air interval; walls=" + room.Walls + "/8; footprint=" + room.Coherent + "/169; no connected exterior path found", room.Coherent, room.Walls);
                }
                var missing = new List<string>();
                foreach (string name in new[] { "ClosedBuilding", "Doorway", "RoofEdge", "OpenOutside", "Canopy", "Earth", "Asphalt", "Concrete", "AsphaltConcrete", "Puddles" })
                {
                    if (!HasPose(name)) missing.Add(name);
                }
                return new PoseReport { poses = _poses.ToArray(), materials = _materials.ToArray(), triangles = _triangles.Count, meshRenderers = _meshRenderers, instances = _instances, indexReferences = _references, queryTests = _queryTests, roomCandidates = _rooms.Count, missing = missing.ToArray(), evidence = "CPU-only candidates inside the reported XZ region; geometry outside the region is not inspected. Actual Player pixels, exposure cells and geometry visibility remain the acceptance gate.", fov = 60.0f, exposure = 9.0f, regionCenter = _center, regionRadius = _radius, rejectedBounds = _rejectedBounds, rejectedTriangles = _rejectedTriangles };
            }

            private void Add(Mesh mesh, Material[] materials, Matrix4x4 matrix, bool instanced)
            {
                Bounds sourceBounds = mesh.bounds;
                Vector3 worldCenter = matrix.MultiplyPoint3x4(sourceBounds.center);
                Vector3 extents = sourceBounds.extents;
                Vector3 right = matrix.MultiplyVector(new Vector3(extents.x, 0.0f, 0.0f));
                Vector3 up = matrix.MultiplyVector(new Vector3(0.0f, extents.y, 0.0f));
                Vector3 forward = matrix.MultiplyVector(new Vector3(0.0f, 0.0f, extents.z));
                float extentX = Mathf.Abs(right.x) + Mathf.Abs(up.x) + Mathf.Abs(forward.x);
                float extentZ = Mathf.Abs(right.z) + Mathf.Abs(up.z) + Mathf.Abs(forward.z);
                if (!Overlaps(worldCenter.x - extentX, worldCenter.x + extentX, worldCenter.z - extentZ, worldCenter.z + extentZ))
                {
                    _rejectedBounds++;
                    return;
                }
                MeshCopy copy = null;
                for (int submesh = 0; submesh < Mathf.Min(mesh.subMeshCount, materials.Length); submesh++)
                {
                    Material material = materials[submesh];
                    if (material == null) continue;
                    int materialId = MaterialId(material, instanced);
                    if (!Eligible(material)) continue;
                    if (copy == null)
                    {
                        if (!_meshes.TryGetValue(mesh, out copy))
                        {
                            copy = new MeshCopy(mesh);
                            _meshes.Add(mesh, copy);
                        }
                    }
                    int[] indices = copy.Indices[submesh];
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Vector3 a = matrix.MultiplyPoint3x4(copy.Vertices[indices[i]]);
                        Vector3 b = matrix.MultiplyPoint3x4(copy.Vertices[indices[i + 1]]);
                        Vector3 c = matrix.MultiplyPoint3x4(copy.Vertices[indices[i + 2]]);
                        if (!Overlaps(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), Mathf.Max(a.x, Mathf.Max(b.x, c.x)), Mathf.Min(a.z, Mathf.Min(b.z, c.z)), Mathf.Max(a.z, Mathf.Max(b.z, c.z))))
                        {
                            _rejectedTriangles++;
                            continue;
                        }
                        Vector3 cross = Vector3.Cross(b - a, c - a);
                        if (cross.sqrMagnitude < 0.000001f) continue;
                        if (_triangles.Count >= MAX_TRIANGLES) throw new InvalidDataException("CPU triangle budget exhausted; no unbounded geometry allocation was attempted.");
                        _triangles.Add(new Triangle(a, b, c, cross.normalized, materialId, copy.Uv[indices[i]], copy.Uv[indices[i + 1]], copy.Uv[indices[i + 2]]));
                        _materials[materialId].triangles++;
                        if (!_hasBounds)
                        {
                            _bounds = new Bounds(a, Vector3.zero);
                            _hasBounds = true;
                        }
                        _bounds.Encapsulate(a);
                        _bounds.Encapsulate(b);
                        _bounds.Encapsulate(c);
                        if (cross.normalized.y < -0.65f && new Vector2(cross.x, cross.z).sqrMagnitude + cross.y * cross.y > 1.0f)
                        {
                            Seed((a + b + c) / 3.0f);
                            Seed(a * 0.5f + b * 0.25f + c * 0.25f);
                            Seed(a * 0.25f + b * 0.5f + c * 0.25f);
                            Seed(a * 0.25f + b * 0.25f + c * 0.5f);
                        }
                    }
                }
            }

            private int MaterialId(Material material, bool instanced)
            {
                if (!_materialIds.TryGetValue(material, out int id))
                {
                    id = _materials.Count;
                    Texture texture = material.HasProperty("_MonolithHasTerrainSourceColor") && material.GetFloat("_MonolithHasTerrainSourceColor") > 0.5f ? material.GetTexture("_MonolithTerrainSourceBase") : material.HasProperty("_BaseColorMap") ? material.GetTexture("_BaseColorMap") : null;
                    _materials.Add(new MaterialRecord { name = material.name, path = AssetDatabase.GetAssetPath(material), texture = texture != null ? texture.name : string.Empty, surface = XRayWetMaterialBindings.Classify(material).ToString() });
                    _materialIds.Add(material, id);
                    Texture mask = material.HasProperty("_MonolithTerrainMask") ? material.GetTexture("_MonolithTerrainMask") : null;
                    bool sourceTerrain = material.IsKeywordEnabled("_MONOLITH_LEVEL_TERRAIN") && material.HasProperty("_MonolithTerrainSource") && material.GetFloat("_MonolithTerrainSource") > 0.5f && material.GetFloat("_MonolithTerrainSource") < 1.5f && material.HasProperty("_MonolithTerrainMaskFlipV") && material.GetFloat("_MonolithTerrainMaskFlipV") < 0.5f;
                    if (sourceTerrain && mask != null && mask.name == "terrain_military_1_mask_packed")
                    {
                        if (_militaryMask == null) _militaryMask = new TerrainMask(AssetDatabase.GetAssetPath(mask));
                        Vector2 scale = material.GetTextureScale("_BaseColorMap");
                        Vector2 offset = material.GetTextureOffset("_BaseColorMap");
                        _materials[id].baseTextureST = new Vector4(scale.x, scale.y, offset.x, offset.y);
                        _terrainMasks.Add(id, _militaryMask);
                    }
                }
                if (instanced) _materials[id].instanceReferences++;
                return id;
            }

            private static bool Eligible(Material material)
            {
                XRayWetSurfaceType type = XRayWetMaterialBindings.Classify(material);
                if (type == XRayWetSurfaceType.Excluded || type == XRayWetSurfaceType.Vegetation || material.IsKeywordEnabled("_MONOLITH_LEVEL_LOD")) return false;
                if (material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f) return false;
                return !material.IsKeywordEnabled("_ALPHATEST_ON");
            }

            private void Seed(Vector3 point)
            {
                if (Mathf.Abs(point.x - _center.x) > _radius - 18.0f || Mathf.Abs(point.z - _center.y) > _radius - 18.0f) return;
                if (_seeds.Count >= MAX_SEEDS) return;
                long key = Key(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z));
                if (_seedKeys.Add(key)) _seeds.Add(new Vector2(point.x, point.z));
            }

            private void Index(int index)
            {
                Triangle triangle = _triangles[index];
                int minX = Cell(Mathf.Max(_center.x - _radius, Mathf.Min(triangle.A.x, Mathf.Min(triangle.B.x, triangle.C.x))));
                int maxX = Cell(Mathf.Min(_center.x + _radius, Mathf.Max(triangle.A.x, Mathf.Max(triangle.B.x, triangle.C.x))));
                int minZ = Cell(Mathf.Max(_center.y - _radius, Mathf.Min(triangle.A.z, Mathf.Min(triangle.B.z, triangle.C.z))));
                int maxZ = Cell(Mathf.Min(_center.y + _radius, Mathf.Max(triangle.A.z, Mathf.Max(triangle.B.z, triangle.C.z))));
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (++_references > MAX_REFERENCES) throw new InvalidDataException("CPU spatial-index reference budget exhausted.");
                        long key = Key(x, z);
                        if (!_cells.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>();
                            _cells.Add(key, list);
                        }
                        list.Add(index);
                    }
                }
            }

            private void Column(Vector2 point)
            {
                _hits.Clear();
                _merged.Clear();
                if (Mathf.Abs(point.x - _center.x) >= _radius || Mathf.Abs(point.y - _center.y) >= _radius) return;
                if (!_cells.TryGetValue(Key(Cell(point.x), Cell(point.y)), out List<int> list)) return;
                Vector3 origin = new Vector3(point.x, _bounds.min.y - 1.0f, point.y);
                foreach (int index in list)
                {
                    Triangle triangle = _triangles[index];
                    if (Ray(triangle, origin, Vector3.up, out float distance)) _hits.Add(new Hit(origin.y + distance, triangle.Normal.y > 0.6f, triangle.Normal.y < -0.6f, triangle.Material, index));
                }
                _hits.Sort((a, b) => a.Y.CompareTo(b.Y));
                foreach (Hit hit in _hits)
                {
                    int last = _merged.Count - 1;
                    if (last >= 0 && Mathf.Abs(_merged[last].Y - hit.Y) < 0.06f)
                    {
                        Hit previous = _merged[last];
                        _merged[last] = new Hit(Mathf.Max(previous.Y, hit.Y), previous.Up || hit.Up, previous.Down || hit.Down, previous.Material, previous.Triangle);
                    }
                    else _merged.Add(hit);
                }
            }

            private int Footprint(Room room, int rings)
            {
                int count = 0;
                for (int z = -rings; z <= rings; z++)
                {
                    for (int x = -rings; x <= rings; x++)
                    {
                        Column(room.Point + new Vector2(x, z) * (PATCH_RADIUS / rings));
                        for (int i = 0; i + 1 < _merged.Count; i++)
                        {
                            if (_merged[i].Up && _merged[i + 1].Down && _merged[i + 1].Y - _merged[i].Y >= MIN_ROOM_HEIGHT && Mathf.Abs(_merged[i].Y - room.Floor) <= 0.35f && Mathf.Abs(_merged[i + 1].Y - room.Ceiling) <= 0.5f)
                            {
                                count++;
                                break;
                            }
                        }
                    }
                }
                return count;
            }

            private int Walls(Room room)
            {
                int walls = 0;
                for (int direction = 0; direction < 8; direction++)
                {
                    float angle = direction * Mathf.PI * 0.25f;
                    Vector3 forward = new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));
                    float first = Nearest(room.Eye, forward, 12.0f);
                    float second = Nearest(room.Eye + Vector3.up * 0.45f, forward, 12.0f);
                    if (first < 0.45f) return 0;
                    if (first < 12.0f && second < 12.0f && Mathf.Abs(first - second) < 0.7f) walls++;
                }
                return walls;
            }

            private bool Door(Room room)
            {
                var nodes = new List<WalkNode>();
                var visited = new HashSet<long>();
                nodes.Add(new WalkNode(room.Point, room.Floor, room.Ceiling, -1));
                visited.Add(Key(0, 0));
                for (int cursor = 0; cursor < nodes.Count && cursor < MAX_WALK_NODES; cursor++)
                {
                    WalkNode parent = nodes[cursor];
                    for (int direction = 0; direction < 4; direction++)
                    {
                        Vector2 offset = direction == 0 ? Vector2.right : direction == 1 ? Vector2.left : direction == 2 ? Vector2.up : Vector2.down;
                        Vector2 point = parent.Point + offset * WALK_STEP;
                        int x = Mathf.RoundToInt((point.x - room.Point.x) / WALK_STEP);
                        int z = Mathf.RoundToInt((point.y - room.Point.y) / WALK_STEP);
                        long key = Key(x, z);
                        if (Mathf.Abs(x) > 26 || Mathf.Abs(z) > 26 || visited.Contains(key)) continue;
                        if (!Stand(point, parent.Floor, out float floor, out float ceiling)) continue;
                        Vector3 start = new Vector3(parent.Point.x, parent.Floor, parent.Point.y);
                        Vector3 end = new Vector3(point.x, floor, point.y);
                        if (!Walkable(start, end)) continue;
                        visited.Add(key);
                        int index = nodes.Count;
                        nodes.Add(new WalkNode(point, floor, ceiling, cursor));
                        if (float.IsPositiveInfinity(ceiling))
                        {
                            int insideIndex = cursor;
                            int boundaryIndex = index;
                            while (insideIndex >= 0 && float.IsPositiveInfinity(nodes[insideIndex].Ceiling))
                            {
                                boundaryIndex = insideIndex;
                                insideIndex = nodes[insideIndex].Parent;
                            }
                            if (insideIndex < 0) continue;
                            WalkNode inside = nodes[insideIndex];
                            WalkNode boundary = nodes[boundaryIndex];
                            if (Vector2.Distance(point, inside.Point) < 3.0f) continue;
                            int openCoherent = OpenFootprint(point, floor);
                            if (openCoherent < 156) continue;
                            Vector3 outsideEye = new Vector3(point.x, floor + EYE_HEIGHT, point.y);
                            Vector3 edge = new Vector3((inside.Point.x + boundary.Point.x) * 0.5f, boundary.Floor + EYE_HEIGHT, (inside.Point.y + boundary.Point.y) * 0.5f);
                            Vector3 forward = (outsideEye - room.Eye).normalized;
                            AddPose("ClosedBuilding", room.Eye, forward, room.Floor, room.Ceiling, true, "floor-up/ceiling-down free interval; coherent=" + room.Coherent + "/169; wall bearings=" + room.Walls + "/8; collision-free exterior path nodes=" + index, room.Coherent, room.Walls);
                            AddPose("Doorway", edge, outsideEye - edge, floor, inside.Ceiling, true, "connected traversable covered-to-open transition, three body-height rays and floor continuity");
                            AddPose("OpenOutside", outsideEye, room.Eye - outsideEye, floor, 0.0f, false, "connected exterior at least3m from last covered path node; roofless footprint=" + openCoherent + "/169");
                            PoseRecord openPose = _poses[_poses.Count - 1];
                            openPose.outsidePatchRadius = PATCH_RADIUS;
                            openPose.outsideCoherentSamples = openCoherent;
                            Vector3 roof = new Vector3(edge.x, inside.Ceiling, edge.z);
                            AddPose("RoofEdge", outsideEye, roof - outsideEye, floor, inside.Ceiling, false, "outside view of the coherent room roof/opening boundary");
                            return true;
                        }
                    }
                }
                return false;
            }

            private int OpenFootprint(Vector2 point, float floor)
            {
                int coherent = 0;
                for (int z = -6; z <= 6; z++)
                {
                    for (int x = -6; x <= 6; x++)
                    {
                        if (Stand(point + new Vector2(x, z) * (PATCH_RADIUS / 6.0f), floor, out float candidateFloor, out float ceiling) && float.IsPositiveInfinity(ceiling)) coherent++;
                    }
                }
                return coherent;
            }

            private bool Stand(Vector2 point, float referenceFloor, out float floor, out float ceiling)
            {
                Column(point);
                floor = 0.0f;
                ceiling = float.PositiveInfinity;
                float difference = float.PositiveInfinity;
                int selected = -1;
                for (int i = 0; i < _merged.Count; i++)
                {
                    float delta = Mathf.Abs(_merged[i].Y - referenceFloor);
                    if (!_merged[i].Up || delta > 0.4f || delta >= difference) continue;
                    floor = _merged[i].Y;
                    difference = delta;
                    selected = i;
                }
                if (selected < 0) return false;
                for (int i = selected + 1; i < _merged.Count; i++)
                {
                    if (_merged[i].Y <= floor + 0.08f) continue;
                    ceiling = _merged[i].Y;
                    break;
                }
                return ceiling - floor >= MIN_ROOM_HEIGHT;
            }

            private bool Walkable(Vector3 start, Vector3 end)
            {
                Vector3 delta = end - start;
                float distance = delta.magnitude;
                if (distance < 0.01f) return true;
                foreach (float height in new[] { 0.25f, 1.1f, 1.95f })
                {
                    if (Nearest(start + Vector3.up * height, delta / distance, distance) < distance - 0.03f) return false;
                }
                return true;
            }

            private float Nearest(Vector3 origin, Vector3 direction, float maximum)
            {
                _queryIds.Clear();
                Vector3 end = origin + direction * maximum;
                for (int z = Cell(Mathf.Min(origin.z, end.z) - 0.05f); z <= Cell(Mathf.Max(origin.z, end.z) + 0.05f); z++)
                {
                    for (int x = Cell(Mathf.Min(origin.x, end.x) - 0.05f); x <= Cell(Mathf.Max(origin.x, end.x) + 0.05f); x++)
                    {
                        if (_cells.TryGetValue(Key(x, z), out List<int> list)) foreach (int index in list) _queryIds.Add(index);
                    }
                }
                float nearest = maximum;
                foreach (int index in _queryIds)
                {
                    if (Ray(_triangles[index], origin, direction, out float distance) && distance > 0.01f && distance < nearest) nearest = distance;
                }
                return nearest;
            }

            private bool Ray(Triangle triangle, Vector3 origin, Vector3 direction, out float distance)
            {
                if (++_queryTests > MAX_QUERY_TESTS) throw new InvalidDataException("CPU triangle query budget exhausted; partial candidates are not acceptance evidence.");
                distance = 0.0f;
                Vector3 first = triangle.B - triangle.A;
                Vector3 second = triangle.C - triangle.A;
                Vector3 p = Vector3.Cross(direction, second);
                float determinant = Vector3.Dot(first, p);
                if (Mathf.Abs(determinant) < 0.000001f) return false;
                float inverse = 1.0f / determinant;
                Vector3 t = origin - triangle.A;
                float u = Vector3.Dot(t, p) * inverse;
                if (u < -0.0001f || u > 1.0001f) return false;
                Vector3 q = Vector3.Cross(t, first);
                float v = Vector3.Dot(direction, q) * inverse;
                if (v < -0.0001f || u + v > 1.0001f) return false;
                distance = Vector3.Dot(second, q) * inverse;
                return distance >= 0.0f;
            }

            private void SurfacePose(XRayWetSurfaceType type, string name, bool asphaltLayer = false)
            {
                bool requireOpen = name != "AsphaltConcrete" && name != "Concrete" || asphaltLayer;
                int candidates = 0;
                for (int i = 0; i < _triangles.Count; i++)
                {
                    Triangle triangle = _triangles[i];
                    if (triangle.Normal.y < 0.85f || _materials[triangle.Material].surface != type.ToString() || Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).sqrMagnitude < 4.0f) continue;
                    Vector3 point = (triangle.A + triangle.B + triangle.C) / 3.0f;
                    if (Mathf.Abs(point.x - _center.x) > _radius - 4.0f || Mathf.Abs(point.z - _center.y) > _radius - 4.0f) continue;
                    Vector4 weights = default;
                    if (asphaltLayer && (!TerrainWeights(triangle, new Vector2(point.x, point.z), out weights) || weights.y < 0.6f)) continue;
                    if (++candidates > 256) return;
                    if (!Stand(new Vector2(point.x, point.z), point.y, out float floor, out float ceiling) || (requireOpen && !float.IsPositiveInfinity(ceiling))) continue;
                    float patchRadius = requireOpen ? 1.25f : 0.75f;
                    int coherent = SurfaceFootprint(point, triangle.Material, patchRadius, requireOpen, asphaltLayer);
                    if (!requireOpen && coherent < 140)
                    {
                        patchRadius = 0.4f;
                        coherent = SurfaceFootprint(point, triangle.Material, patchRadius, false, false);
                    }
                    if (coherent < 140) continue;
                    for (int direction = 0; direction < 8; direction++)
                    {
                        float angle = direction * Mathf.PI * 0.25f;
                        float eyeDistance = requireOpen ? 2.25f : 1.0f;
                        Vector3 eye = new Vector3(point.x + Mathf.Sin(angle) * eyeDistance, floor + 1.1f, point.z + Mathf.Cos(angle) * eyeDistance);
                        if (!Stand(new Vector2(eye.x, eye.z), floor, out float eyeFloor, out float eyeCeiling) || (requireOpen && !float.IsPositiveInfinity(eyeCeiling))) continue;
                        eye.y = eyeFloor + 1.1f;
                        Vector3 target = point + Vector3.up * 0.025f;
                        float distance = Vector3.Distance(target, eye);
                        if (Nearest(eye, (target - eye) / distance, distance) < distance - 0.08f) continue;
                        MaterialRecord material = _materials[triangle.Material];
                        bool covered = !float.IsPositiveInfinity(ceiling);
                        AddPose(name, eye, target - eye, floor, covered ? ceiling : 0.0f, covered, "real opaque " + type + " close-up; material=" + material.name + "; CPU clear view; " + (requireOpen ? "uncovered" : "controlled visual response") + " material patch=" + coherent + "/169");
                        PoseRecord pose = _poses[_poses.Count - 1];
                        pose.targetPosition = point;
                        pose.targetMaterial = material.name;
                        pose.targetMaterialPath = material.path;
                        pose.targetTexture = material.texture;
                        pose.targetSurface = material.surface;
                        pose.targetTriangleA = triangle.A;
                        pose.targetTriangleB = triangle.B;
                        pose.targetTriangleC = triangle.C;
                        pose.surfacePatchRadius = patchRadius;
                        pose.surfaceCoherentSamples = coherent;
                        pose.surfacePatchSamples = 169;
                        if (asphaltLayer)
                        {
                            TerrainMask mask = _terrainMasks[triangle.Material];
                            pose.targetSourceLayer = "Asphalt (ground_asphalt_01, G)";
                            pose.targetTerrainWeights = weights;
                            pose.targetUvA = triangle.UvA;
                            pose.targetUvB = triangle.UvB;
                            pose.targetUvC = triangle.UvC;
                            pose.targetMeshUv = triangle.MeshUv(new Vector2(point.x, point.z));
                            pose.baseTextureST = material.baseTextureST;
                            pose.maskPackedPath = mask.PackedPath;
                            pose.maskPackedSha256 = mask.PackedSha256;
                            pose.maskSourcePath = mask.SourcePath;
                            pose.maskSourceSha256 = mask.SourceSha256;
                            pose.evidence += "; source asphalt G>=0.6 in verified patch; centerWeights=" + weights;
                        }
                        return;
                    }
                }
            }

            private int SurfaceFootprint(Vector3 point, int material, float radius, bool requireOpen, bool asphaltLayer)
            {
                int coherent = 0;
                for (int z = -6; z <= 6; z++)
                {
                    for (int x = -6; x <= 6; x++)
                    {
                        Vector2 position = new Vector2(point.x + x * (radius / 6.0f), point.z + z * (radius / 6.0f));
                        Column(position);
                        if (_merged.Count == 0) continue;
                        for (int i = _merged.Count - 1; i >= 0; i--)
                        {
                            Hit hit = _merged[i];
                            if (hit.Up && hit.Material == material && Mathf.Abs(hit.Y - point.y) <= 0.2f && (!requireOpen || i == _merged.Count - 1))
                            {
                                if (asphaltLayer && (!TerrainWeights(_triangles[hit.Triangle], position, out Vector4 weights) || weights.y < 0.6f)) continue;
                                coherent++;
                                break;
                            }
                        }
                    }
                }
                return coherent;
            }

            private bool TerrainWeights(Triangle triangle, Vector2 point, out Vector4 weights)
            {
                weights = default;
                if (!_terrainMasks.TryGetValue(triangle.Material, out TerrainMask mask)) return false;
                Vector2 uv = triangle.MeshUv(point);
                Vector4 st = _materials[triangle.Material].baseTextureST;
                weights = mask.Weights(new Vector2(uv.x * st.x + st.z, uv.y * st.y + st.w));
                return true;
            }

            private void AddPose(string name, Vector3 position, Vector3 direction, float floor, float roof, bool covered, string evidence, int coherent = 0, int walls = 0)
            {
                if (HasPose(name)) return;
                if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
                Column(new Vector2(position.x, position.z));
                float top = _merged.Count > 0 ? _merged[_merged.Count - 1].Y : floor;
                float bottom = _merged.Count > 0 ? _merged[0].Y : floor;
                _poses.Add(new PoseRecord { name = name, position = position, rotation = Quaternion.LookRotation(direction, Vector3.up), groundY = floor, roofY = roof, hasRoof = covered, kind = name, evidence = evidence, fov = 60.0f, exposure = 9.0f, roofPatchRadius = coherent > 0 ? PATCH_RADIUS : 0.0f, roofPatchWorldSpan = coherent > 0 ? PATCH_RADIUS * 2.0f : 0.0f, coherentSamples = coherent, patchSamples = coherent > 0 ? 169 : 0, wallBearings = walls, columnTopY = top, columnBottomY = bottom, selectedWetLayer = floor >= top - 0.15f ? "Top" : "Lower", selectedLayerWorldY = floor });
            }

            private bool HasPose(string name)
            {
                foreach (PoseRecord pose in _poses) if (pose.name == name) return true;
                return false;
            }

            private void AliasAsphalt()
            {
                for (int i = 0; i < _poses.Count; i++)
                {
                    PoseRecord pose = _poses[i];
                    if (pose.name != "AsphaltConcrete" || string.IsNullOrEmpty(pose.targetSourceLayer)) continue;
                    _poses.Add(pose.CopyAs("Asphalt"));
                    break;
                }
            }

            private bool HasRoomNear(Room candidate)
            {
                foreach (Room room in _rooms)
                {
                    if ((candidate.Point - room.Point).sqrMagnitude < 16.0f && Mathf.Abs(candidate.Floor - room.Floor) < 0.5f) return true;
                }
                return false;
            }

            private int Cell(float value)
            {
                return Mathf.FloorToInt(value / _cellSize);
            }

            private bool Overlaps(float minX, float maxX, float minZ, float maxZ)
            {
                return maxX >= _center.x - _radius && minX <= _center.x + _radius && maxZ >= _center.y - _radius && minZ <= _center.y + _radius;
            }

            private static long Key(int x, int z)
            {
                return ((long)x << 32) | (uint)z;
            }
        }

        private sealed class TerrainMask
        {
            private const string PACKED_SHA = "23e9b833415818c92aa2336b4f1132e8e9ef852af2f866e5f8a8463d212ae95a";
            private const string SOURCE_SHA = "f88a9ea1ff56460e6bb249a193dccb8a1a42df89f7da26f7afb9058cd376f1eb";

            private readonly byte[] _pixels;
            private readonly int _width;
            private readonly int _height;

            public string PackedPath { get; }
            public string PackedSha256 { get; }
            public string SourcePath { get; }
            public string SourceSha256 { get; }

            public TerrainMask(string packedPath)
            {
                PackedPath = Path.GetFullPath(Path.Combine(PRODUCTION, packedPath));
                SourcePath = "D:\\GRA2.5\\Anomaly 1.5.3 up\\tools\\_unpacked\\textures\\terrain\\terrain_military_1_mask.dds";
                byte[] packed = File.ReadAllBytes(PackedPath);
                byte[] source = File.ReadAllBytes(SourcePath);
                PackedSha256 = Hash(packed);
                SourceSha256 = Hash(source);
                if (PackedSha256 != PACKED_SHA || SourceSha256 != SOURCE_SHA) throw new InvalidDataException("The observed Military terrain mask differs from the verified source/packed orientation evidence.");
                DdsHeader header = DdsHeader.Parse(source);
                DdsHeader packedHeader = DdsHeader.Parse(packed);
                if (header.Width != 1024 || header.Height != 1024 || header.Format != DdsFormat.BC3 || packedHeader.Width != header.Width || packedHeader.Height != header.Height) throw new InvalidDataException("Expected the verified bounded1024 BC3 terrain mask.");
                _width = header.Width;
                _height = header.Height;
                _pixels = DdsDecoder.DecodeTopLevel(source, header);
                byte[] packedPixels = DdsDecoder.DecodeTopLevel(packed, packedHeader);
                if (packedPixels.Length != _pixels.Length) throw new InvalidDataException("Packed/source terrain mask dimensions differ.");
                for (int i = 0; i < _pixels.Length; i++)
                {
                    if (_pixels[i] != packedPixels[i]) throw new InvalidDataException("Packed/source terrain mask rows are no longer identical.");
                }
            }

            public Vector4 Weights(Vector2 uv)
            {
                float x = uv.x * _width - 0.5f;
                float y = (1.0f - uv.y) * _height - 0.5f;
                int firstX = Mathf.FloorToInt(x);
                int firstY = Mathf.FloorToInt(y);
                Vector4 first = Vector4.Lerp(Pixel(firstX, firstY), Pixel(firstX + 1, firstY), x - firstX);
                Vector4 second = Vector4.Lerp(Pixel(firstX, firstY + 1), Pixel(firstX + 1, firstY + 1), x - firstX);
                Vector4 value = Vector4.Lerp(first, second, y - firstY);
                float sum = value.x + value.y + value.z + value.w;
                return sum > 0.00001f ? value / sum : Vector4.zero;
            }

            private Vector4 Pixel(int x, int y)
            {
                int column = (x % _width + _width) % _width;
                int row = (y % _height + _height) % _height;
                int offset = (row * _width + column) * 4;
                return new Vector4(_pixels[offset], _pixels[offset + 1], _pixels[offset + 2], _pixels[offset + 3]) / 255.0f;
            }

            private static string Hash(byte[] bytes)
            {
                using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private sealed class MeshCopy
        {
            public Vector3[] Vertices { get; }
            public int[][] Indices { get; }
            public Vector2[] Uv { get; }

            public MeshCopy(Mesh mesh)
            {
                using (Mesh.MeshDataArray snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    Mesh.MeshData data = snapshot[0];
                    using (var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetVertices(vertices);
                        Vertices = vertices.ToArray();
                    }
                    Uv = new Vector2[data.vertexCount];
                    if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
                    {
                        using (var uv = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp))
                        {
                            data.GetUVs(0, uv);
                            Uv = uv.ToArray();
                        }
                    }
                    Indices = new int[data.subMeshCount][];
                    for (int submesh = 0; submesh < data.subMeshCount; submesh++)
                    {
                        var descriptor = data.GetSubMesh(submesh);
                        if (descriptor.topology != UnityEngine.MeshTopology.Triangles)
                        {
                            Indices[submesh] = Array.Empty<int>();
                            continue;
                        }
                        using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                        {
                            data.GetIndices(indices, submesh, true);
                            Indices[submesh] = indices.ToArray();
                        }
                    }
                }
            }
        }

        private readonly struct Triangle
        {
            public Vector3 A { get; }
            public Vector3 B { get; }
            public Vector3 C { get; }
            public Vector3 Normal { get; }
            public int Material { get; }
            public Vector2 UvA { get; }
            public Vector2 UvB { get; }
            public Vector2 UvC { get; }

            public Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, int material, Vector2 uvA, Vector2 uvB, Vector2 uvC)
            {
                A = a;
                B = b;
                C = c;
                Normal = normal;
                Material = material;
                UvA = uvA;
                UvB = uvB;
                UvC = uvC;
            }

            public Vector2 MeshUv(Vector2 point)
            {
                float denominator = (B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z);
                if (Mathf.Abs(denominator) < 0.000001f) return (UvA + UvB + UvC) / 3.0f;
                float first = ((B.z - C.z) * (point.x - C.x) + (C.x - B.x) * (point.y - C.z)) / denominator;
                float second = ((C.z - A.z) * (point.x - C.x) + (A.x - C.x) * (point.y - C.z)) / denominator;
                return UvA * first + UvB * second + UvC * (1.0f - first - second);
            }
        }

        private readonly struct Hit
        {
            public float Y { get; }
            public bool Up { get; }
            public bool Down { get; }
            public int Material { get; }
            public int Triangle { get; }

            public Hit(float y, bool up, bool down, int material, int triangle)
            {
                Y = y;
                Up = up;
                Down = down;
                Material = material;
                Triangle = triangle;
            }
        }

        private sealed class Room
        {
            public Vector2 Point { get; }
            public float Floor { get; }
            public float Ceiling { get; }
            public int FloorMaterial { get; }
            public int RoofMaterial { get; }
            public Vector3 Eye => new Vector3(Point.x, Floor + 1.65f, Point.y);
            public int Coherent { get; set; }
            public int Walls { get; set; }

            public Room(Vector2 point, float floor, float ceiling, int floorMaterial, int roofMaterial)
            {
                Point = point;
                Floor = floor;
                Ceiling = ceiling;
                FloorMaterial = floorMaterial;
                RoofMaterial = roofMaterial;
            }
        }

        private readonly struct WalkNode
        {
            public Vector2 Point { get; }
            public float Floor { get; }
            public float Ceiling { get; }
            public int Parent { get; }

            public WalkNode(Vector2 point, float floor, float ceiling, int parent)
            {
                Point = point;
                Floor = floor;
                Ceiling = ceiling;
                Parent = parent;
            }
        }

        [Serializable]
        private sealed class Status
        {
            public string state;
            public string result;
            public string error;
        }

        [Serializable]
        private sealed class PoseReport
        {
            public string scene;
            public bool sceneDirtyUnchanged;
            public int triangles;
            public int meshRenderers;
            public int instances;
            public int indexReferences;
            public int queryTests;
            public int roomCandidates;
            public Vector2 regionCenter;
            public float regionRadius;
            public int rejectedBounds;
            public int rejectedTriangles;
            public float fov;
            public float exposure;
            public float roofPatchRadius;
            public string evidence;
            public string[] missing;
            public PoseRecord[] poses;
            public MaterialRecord[] materials;
        }

        [Serializable]
        private sealed class PoseRecord
        {
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public float groundY;
            public float roofY;
            public bool hasRoof;
            public string kind;
            public string evidence;
            public float fov;
            public float exposure;
            public float roofPatchRadius;
            public float roofPatchWorldSpan;
            public int coherentSamples;
            public int patchSamples;
            public int wallBearings;
            public float columnTopY;
            public float columnBottomY;
            public float selectedLayerWorldY;
            public string selectedWetLayer;
            public Vector3 targetPosition;
            public string targetMaterial;
            public string targetMaterialPath;
            public string targetTexture;
            public string targetSurface;
            public Vector3 targetTriangleA;
            public Vector3 targetTriangleB;
            public Vector3 targetTriangleC;
            public float surfacePatchRadius;
            public int surfaceCoherentSamples;
            public int surfacePatchSamples;
            public float outsidePatchRadius;
            public int outsideCoherentSamples;
            public string targetSourceLayer;
            public Vector4 targetTerrainWeights;
            public Vector2 targetUvA;
            public Vector2 targetUvB;
            public Vector2 targetUvC;
            public Vector2 targetMeshUv;
            public Vector4 baseTextureST;
            public string maskPackedPath;
            public string maskPackedSha256;
            public string maskSourcePath;
            public string maskSourceSha256;

            public PoseRecord CopyAs(string poseName)
            {
                var copy = (PoseRecord)MemberwiseClone();
                copy.name = poseName;
                copy.kind = poseName;
                return copy;
            }
        }

        [Serializable]
        private sealed class MaterialRecord
        {
            public string name;
            public string path;
            public string texture;
            public string surface;
            public int triangles;
            public int instanceReferences;
            public Vector4 baseTextureST;
        }
    }
}
