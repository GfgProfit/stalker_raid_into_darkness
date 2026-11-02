using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelDetailBuilder
    {
        public const string TS_FALLBACK_TEXTURE = "ts_flora\\build_details";
        public const string TS_FALLBACK_BUMP = "ts_flora\\build_details_bump";
        public const string DATABASE_NAME = "DetailDatabase.asset";
        public const string INSTANCES_NAME = "DetailInstances.bytes";
        public const string SLOTS_NAME = "DetailSlots.bytes";

        private readonly string _level;
        private readonly XRayDetails _details;
        private readonly XRayLevelImportSettings _settings;
        private readonly XRayLevelImportRecord _record;
        private readonly XRayLevelGamedataInfo _info;
        private List<XRayDetailInstance> _instances;
        private XRayDetailPlacement _placement;
        private XRayLevelTextureSet[] _textureSets;
        private XRayFloraSourceBinding _source;
        private XRayLevelFloraTextures[] _floraTextures;

        public XRayLevelDetailBuilder(string level, XRayDetails details, XRayLevelImportSettings settings, XRayLevelImportRecord record, XRayLevelGamedataInfo info)
        {
            _level = level;
            _details = details;
            _settings = settings;
            _record = record;
            _info = info;
        }

        public XRayDetailPlacementStatistics Statistics => _placement?.Statistics;

        public void Place(XRayCollisionModel collision, bool[] passable)
        {
            _placement = new XRayDetailPlacement(_details, collision, passable, new XRayDetailPlacementSettings { Density = _settings.DetailDensity, HeightScale = _settings.DetailHeightScale });
            _instances = new List<XRayDetailInstance>(_settings.RuntimeDetails ? 0 : 1 << 20);
            if (_settings.RuntimeDetails) return;
            for (int z = 0; z < _details.SizeZ; z++)
            {
                for (int x = 0; x < _details.SizeX; x++)
                {
                    _placement.PlaceSlot(x, z, _instances);
                }
            }
        }

        public XRayDetailDatabase Build(XRayLevelTextureLibrary textures, XRayLevelMaterialLibrary materials)
        {
            string folder = XRayLevelPaths.Details(_level);
            string modelsFolder = folder + "/Models";
            XRayLevelPaths.EnsureFolder(modelsFolder);
            var modelEntries = new List<XRayDetailModelEntry>();
            for (int i = 0; i < _details.Models.Count; i++)
            {
                XRayDetailModel model = _details.Models[i];
                Mesh mesh = SaveModelMesh(model, $"{modelsFolder}/dm_{i:00}.asset");
                Material material = CreateMaterial(model, _textureSets[i], materials);
                float height = model.BoundsMax[1] - model.BoundsMin[1];
                int count = _placement.Statistics.PerModel[i];
                modelEntries.Add(new XRayDetailModelEntry(model.Shader, model.Texture, mesh, material, height > 1e-5f ? 1.0f / height : 0.0f, model.NoWaving, model.MinScale, model.MaxScale, count));
            }

            float chunkSize = _settings.DetailChunkSize;
            float originX = -_details.OffsetX * XRayDetails.SLOT_SIZE;
            float originZ = -_details.OffsetZ * XRayDetails.SLOT_SIZE;
            int cellsX = Mathf.Max(1, Mathf.CeilToInt(_details.SizeX * XRayDetails.SLOT_SIZE / chunkSize) + 1);
            int cellsZ = Mathf.Max(1, Mathf.CeilToInt(_details.SizeZ * XRayDetails.SLOT_SIZE / chunkSize) + 1);
            var buckets = new List<int>[cellsX * cellsZ];
            for (int i = 0; i < _instances.Count; i++)
            {
                int cell = Cell(_instances[i].X, _instances[i].Z, originX, originZ, chunkSize, cellsX, cellsZ);
                (buckets[cell] ?? (buckets[cell] = new List<int>())).Add(i);
            }

            var slotBuckets = new List<int>[cellsX * cellsZ];
            for (int z = 0; z < _details.SizeZ; z++)
            {
                for (int x = 0; x < _details.SizeX; x++)
                {
                    if (_details.GetSlot(x, z).IsEmpty)
                    {
                        continue;
                    }

                    float wx = (x - _details.OffsetX) * XRayDetails.SLOT_SIZE + 1.0f;
                    float wz = (z - _details.OffsetZ) * XRayDetails.SLOT_SIZE + 1.0f;
                    int cell = Cell(wx, wz, originX, originZ, chunkSize, cellsX, cellsZ);
                    (slotBuckets[cell] ?? (slotBuckets[cell] = new List<int>())).Add(z * _details.SizeX + x);
                }
            }

            var instanceBytes = new byte[_instances.Count * XRayDetailDatabase.INSTANCE_STRIDE];
            int slotTotal = 0;
            foreach (List<int> bucket in slotBuckets)
            {
                slotTotal += bucket?.Count ?? 0;
            }

            var slotBytes = new byte[slotTotal * XRayDetailDatabase.SLOT_STRIDE];
            var chunks = new List<XRayDetailChunk>();
            int written = 0;
            int slotsWritten = 0;
            for (int cell = 0; cell < buckets.Length; cell++)
            {
                List<int> bucket = buckets[cell];
                List<int> slots = slotBuckets[cell];
                if (bucket == null && slots == null)
                {
                    continue;
                }

                int cx = cell % cellsX;
                int cz = cell / cellsX;
                var cellMin = new Vector3(originX + cx * chunkSize, 0.0f, originZ + cz * chunkSize);
                Vector3 min = new Vector3(cellMin.x, float.MaxValue, cellMin.z);
                Vector3 max = new Vector3(cellMin.x + chunkSize, float.MinValue, cellMin.z + chunkSize);
                int start = written;
                if (bucket != null)
                {
                    foreach (int index in bucket)
                    {
                        XRayDetailInstance instance = _instances[index];
                        XRayDetailModel model = _details.Models[instance.Model];
                        float extent = Mathf.Max(Mathf.Max(Mathf.Abs(model.BoundsMin[0]), Mathf.Abs(model.BoundsMax[0])), Mathf.Max(Mathf.Abs(model.BoundsMin[2]), Mathf.Abs(model.BoundsMax[2]))) * instance.Scale + 0.5f;
                        min = Vector3.Min(min, new Vector3(instance.X - extent, instance.Y + Mathf.Min(0.0f, model.BoundsMin[1]) * instance.Scale - 0.5f, instance.Z - extent));
                        max = Vector3.Max(max, new Vector3(instance.X + extent, instance.Y + model.BoundsMax[1] * instance.Scale + 0.5f, instance.Z + extent));
                        WriteInstance(instanceBytes, written++, instance, cellMin, chunkSize);
                    }
                }

                int slotStart = slotsWritten;
                if (slots != null)
                {
                    foreach (int slotIndex in slots)
                    {
                        XRayDetailSlot slot = _details.Slots[slotIndex];
                        int p = slotsWritten++ * XRayDetailDatabase.SLOT_STRIDE;
                        int sx = slotIndex % _details.SizeX - _details.OffsetX;
                        int sz = slotIndex / _details.SizeX - _details.OffsetZ;
                        BitConverter.GetBytes((short)sx).CopyTo(slotBytes, p);
                        BitConverter.GetBytes((short)sz).CopyTo(slotBytes, p + 2);
                        BitConverter.GetBytes(slot.YBase).CopyTo(slotBytes, p + 4);
                        BitConverter.GetBytes(slot.YHeight).CopyTo(slotBytes, p + 8);
                        min.y = Mathf.Min(min.y, slot.YBase);
                        max.y = Mathf.Max(max.y, slot.YBase + slot.YHeight);
                    }
                }

                if (min.y > max.y)
                {
                    min.y = 0.0f;
                    max.y = 0.0f;
                }

                var bounds = new Bounds((min + max) * 0.5f, max - min);
                chunks.Add(new XRayDetailChunk(bounds, cellMin, start, written - start, slotStart, slotsWritten - slotStart));
            }

            string instancesPath = folder + "/" + INSTANCES_NAME;
            string slotsPath = folder + "/" + SLOTS_NAME;
            File.WriteAllBytes(instancesPath, instanceBytes);
            File.WriteAllBytes(slotsPath, slotBytes);
            AssetDatabase.ImportAsset(instancesPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(slotsPath, ImportAssetOptions.ForceUpdate);
            string databasePath = folder + "/" + DATABASE_NAME;
            var database = AssetDatabase.LoadAssetAtPath<XRayDetailDatabase>(databasePath);
            bool created = database == null;
            if (created)
            {
                database = ScriptableObject.CreateInstance<XRayDetailDatabase>();
            }

            Dictionary<string, float> swing = XRayLevelSourceCache.ReadLtxSection(_info, "configs/system.ltx", "details");
            var normal = new XRayDetailSwing(Get(swing, "swing_normal_amp1", 0.1f), Get(swing, "swing_normal_amp2", 0.05f), Get(swing, "swing_normal_rot1", 30.0f), Get(swing, "swing_normal_rot2", 1.0f), Get(swing, "swing_normal_speed", 2.0f));
            var fast = new XRayDetailSwing(Get(swing, "swing_fast_amp1", 0.35f), Get(swing, "swing_fast_amp2", 0.2f), Get(swing, "swing_fast_rot1", 5.0f), Get(swing, "swing_fast_rot2", 0.5f), Get(swing, "swing_fast_speed", 0.5f));
            if (swing.Count == 0)
            {
                _record.Warnings.Add("configs/system.ltx [details] swing parameters were not found; engine defaults were used.");
            }

            database.Initialize(_level, chunkSize, _settings.DetailDensity, _settings.DetailHeightScale, XRayDetails.SLOT_SIZE, modelEntries, chunks, AssetDatabase.LoadAssetAtPath<TextAsset>(instancesPath), AssetDatabase.LoadAssetAtPath<TextAsset>(slotsPath), normal, fast, _instances.Count, slotTotal);
            if (created)
            {
                AssetDatabase.CreateAsset(database, databasePath);
            }
            else
            {
                EditorUtility.SetDirty(database);
            }

            AssetDatabase.SaveAssetIfDirty(database);
            _record.GeneratedAssets.Add(databasePath);
            _record.GeneratedAssets.Add(instancesPath);
            _record.GeneratedAssets.Add(slotsPath);
            _record.SetMetric("DetailChunks", chunks.Count);
            return database;
        }

        private static float Get(Dictionary<string, float> values, string key, float fallback)
        {
            return values.TryGetValue(key, out float value) ? value : fallback;
        }

        private static int Cell(float x, float z, float originX, float originZ, float size, int cellsX, int cellsZ)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((x - originX) / size), 0, cellsX - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((z - originZ) / size), 0, cellsZ - 1);
            return cz * cellsX + cx;
        }

        private static void WriteInstance(byte[] bytes, int index, XRayDetailInstance instance, Vector3 cellMin, float size)
        {
            int p = index * XRayDetailDatabase.INSTANCE_STRIDE;
            ushort qx = (ushort)Mathf.Clamp(Mathf.RoundToInt((instance.X - cellMin.x) / size * XRayDetailDatabase.POSITION_QUANT), 0, 65535);
            ushort qz = (ushort)Mathf.Clamp(Mathf.RoundToInt((instance.Z - cellMin.z) / size * XRayDetailDatabase.POSITION_QUANT), 0, 65535);
            ushort yaw = (ushort)Mathf.Clamp(Mathf.RoundToInt(instance.Yaw / (Mathf.PI * 2.0f) * XRayDetailDatabase.POSITION_QUANT), 0, 65535);
            ushort scale = (ushort)Mathf.Clamp(Mathf.RoundToInt(instance.Scale * 4096.0f), 0, 65535);
            BitConverter.GetBytes(qx).CopyTo(bytes, p);
            BitConverter.GetBytes(qz).CopyTo(bytes, p + 2);
            BitConverter.GetBytes(instance.Y).CopyTo(bytes, p + 4);
            BitConverter.GetBytes(yaw).CopyTo(bytes, p + 8);
            BitConverter.GetBytes(scale).CopyTo(bytes, p + 10);
            bytes[p + 12] = (byte)instance.Model;
            bytes[p + 13] = (byte)Mathf.RoundToInt(instance.Hemi * 15.0f);
            bytes[p + 14] = (byte)Mathf.RoundToInt(instance.Sun * 15.0f);
            bytes[p + 15] = (byte)instance.Wave;
        }

        private Mesh SaveModelMesh(XRayDetailModel model, string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created)
            {
                mesh = new Mesh();
            }

            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            int count = model.VertexCount;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var tangents = new Vector4[count];
            var uv = new Vector2[count];
            for (int v = 0; v < count; v++)
            {
                vertices[v] = new Vector3(model.Positions[v * 3], model.Positions[v * 3 + 1], model.Positions[v * 3 + 2]);
                normals[v] = Vector3.up;
                tangents[v] = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
                uv[v] = new Vector2(model.Uv[v * 2], 1.0f - model.Uv[v * 2 + 1]);
            }

            var indices = new int[model.Indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = model.Indices[i];
            }

            mesh.indexFormat = IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uv;
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateNormals();
            if (created)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            _record.GeneratedAssets.Add(path);
            return mesh;
        }

        public void RequestTextures(XRayLevelTextureLibrary textures)
        {
            if (_settings.RuntimeDetails)
            {
                _source = XRayFloraSourceBinding.Resolve(textures.Resolver);
                if (!XRayFloraSourceBinding.Accepts(_settings.FloraImportMode, _source.Profile)) throw new InvalidOperationException("Runtime details require a matching verified Source Flora profile.");
                if (!XRayFloraSourceBinding.AcceptsDetails(textures.Resolver, _source.Profile)) throw new InvalidOperationException("Runtime details are blocked: the effective grass shader contract has changed.");
                string contract = _source.Profile == XRayFloraSourceProfile.AnomalySss ? XRayFloraSourceBinding.ANOMALY_DETAIL_CONTRACT_HASH : XRayFloraSourceBinding.TRUE_STALKER_DETAIL_CONTRACT_HASH;
                _record.AddLimitation("Runtime grass shader contract SHA256=" + contract);
                foreach (string path in _source.Profile == XRayFloraSourceProfile.AnomalySss ? XRayFloraSourceBinding.AnomalyDetailFiles : XRayFloraSourceBinding.TrueStalkerDetailFiles)
                {
                    var file = textures.Resolver.ResolveFile(path);
                    _record.AddDependency(XRayLevelDependencyCategory.VisualObjects, "Runtime grass source", file.DisplayPath, file.Winner.Location, file.VirtualPath, XRayLevelDependencyStatus.Found, "Source grass shader contract", null, "SHA256=" + XRayTerrainSourceBinding.Hash(file));
                }
                _floraTextures = new XRayLevelFloraTextures[_details.Models.Count];
            }

            bool tsScript = UsesTrueStalkerDetailScript();
            _textureSets = new XRayLevelTextureSet[_details.Models.Count];
            for (int i = 0; i < _details.Models.Count; i++)
            {
                XRayDetailModel model = _details.Models[i];
                string texture = model.Texture;
                string bump = null;
                string note;
                if (tsScript)
                {
                    if (!textures.Exists(texture))
                    {
                        texture = TS_FALLBACK_TEXTURE;
                    }

                    bump = textures.Exists(texture + "_bump") ? texture + "_bump" : TS_FALLBACK_BUMP;
                    if (!textures.Exists(bump))
                    {
                        bump = null;
                    }

                    note = $"True Stalker details_blend.s: base '{model.Texture}' -> '{texture}', bump '{bump ?? "none"}' ({(bump != null ? "deffer_grass" : "deffer_detail_w_flat")}).";
                }
                else
                {
                    note = "Standard details\\blend: flat detail shader (deffer_detail_w_flat + deffer_base_aref_flat).";
                }

                if (_settings.RuntimeDetails && _source.Profile == XRayFloraSourceProfile.AnomalySss)
                {
                    string levelBump = "levels\\" + _record.LevelName + "\\" + texture + "_bump";
                    bump = textures.Exists(levelBump) ? levelBump : null;
                    note = "Verified Anomaly SSFX details_blend.s: source deffer_grass with per-level bump '" + levelBump + "'.";
                }

                XRayLevelTextureSet set = textures.Request(texture, false, "Detail object diffuse");
                if (bump != null)
                {
                    XRayTextureAssetResult packed = textures.RequestPacked(XRayVirtualPath.TextureToVirtualPath(bump, ".dds"), XRayLevelDependencyCategory.VisualObjects, "Detail bump (deffer_grass)", "Grass normal perturbation (bump.agb) and flora gloss def_gloss");
                    set = new XRayLevelTextureSet { TextureName = set.TextureName, Resolution = set.Resolution, AlbedoPath = set.AlbedoPath, AlbedoStatus = set.AlbedoStatus, BumpPath = packed.AssetPath, BumpStatus = packed.Status };
                }

                _textureSets[i] = set;
                if (_settings.RuntimeDetails)
                {
                    string correction = _source.Profile == XRayFloraSourceProfile.TrueStalker ? bump + "#" : texture + "_bump#";
                    if (bump != null && textures.Exists(correction)) set.CorrectionPath = textures.RequestPacked(XRayVirtualPath.TextureToVirtualPath(correction, ".dds"), XRayLevelDependencyCategory.VisualObjects, "Detail bump correction", "Source grass bump#").AssetPath;
                    _floraTextures[i] = textures.RequestFlora(set, _source);
                    if (_source.Profile == XRayFloraSourceProfile.AnomalySss) _floraTextures[i].GrassWavesPath = textures.RequestPacked(XRayFloraSourceBinding.ANOMALY_WIND_WAVES, XRayLevelDependencyCategory.VisualObjects, "Grass wind waves", "SSFX source grass deformation").AssetPath;
                }
                _record.AddLimitation("Detail shader: " + note);
            }
        }

        private Material CreateMaterial(XRayDetailModel model, XRayLevelTextureSet set, XRayLevelMaterialLibrary materials)
        {
            var surface = new XRayLevelSurface { Kind = XRayLevelSurfaceKind.AlphaTest, AlphaCutoff = XRayLevelMaterialLibrary.DETAIL_AREF, DoubleSided = true, CastShadows = false, RuleName = "detail", BlenderClass = "D_STILL", HemiMode = XRayLevelHemiMode.Constant };
            return materials.Get(new XRayLevelMaterialRequest { ShaderName = model.Shader, Surface = surface, Textures = set, Detail = true, RuntimeDetail = _settings.RuntimeDetails, Flora = _floraTextures?[model.Index] });
        }

        public void BindRuntimeSource(XRayDetailDatabase database, string detailsPath, string collisionPath, bool[] passable)
        {
            if (!_settings.RuntimeDetails) return;
            if (passable == null) throw new InvalidOperationException("Runtime details require source gamemtl.xr passable flags.");
            string folder = XRayLevelPaths.Details(_level);
            string detailsAsset = folder + "/DetailSource.bytes";
            string collisionAsset = folder + "/DetailCollision.bytes";
            File.WriteAllBytes(detailsAsset, File.ReadAllBytes(detailsPath));
            File.WriteAllBytes(collisionAsset, File.ReadAllBytes(collisionPath));
            AssetDatabase.ImportAsset(detailsAsset, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(collisionAsset, ImportAssetOptions.ForceSynchronousImport);
            database.SetRuntimeSource(AssetDatabase.LoadAssetAtPath<TextAsset>(detailsAsset), AssetDatabase.LoadAssetAtPath<TextAsset>(collisionAsset), passable, _source.Profile == XRayFloraSourceProfile.AnomalySss ? 1 : 2);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
            _record.GeneratedAssets.Add(detailsAsset);
            _record.GeneratedAssets.Add(collisionAsset);
            _record.AddLimitation("Runtime details retain immutable source level.details and level.cform. Density resamples source slots without reimport; legacy baked DetailInstances are empty. Anomaly placement targets xray-monolith 2026.5.24; TS placement and HDRP cascades remain PARTIAL / SOURCE BLOCKED.");
        }

        private bool UsesTrueStalkerDetailScript()
        {
            if (_info.Effective != null)
            {
                string virtualPath = "shaders/r3/details_blend.s";
                return _info.Exists(virtualPath) && IsTrueStalkerDetailScript(System.Text.Encoding.UTF8.GetString(_info.ReadAllBytes(virtualPath)));
            }

            string shaders = XRayFileSystem.FindChildDirectory(_info.Root, "shaders");
            string r3 = shaders != null ? XRayFileSystem.FindChildDirectory(shaders, "r3") : null;
            string script = r3 != null ? XRayFileSystem.FindChildFile(r3, "details_blend.s") : null;
            if (script == null)
            {
                return false;
            }

            return IsTrueStalkerDetailScript(File.ReadAllText(script));
        }

        private static bool IsTrueStalkerDetailScript(string text)
        {
            return text.IndexOf("ts_flora\\\\build_details", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("ts_flora\\build_details", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
