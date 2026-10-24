using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelDiagnostics
    {
        public const string SHADERS_NOT_READY = "NotReady: shader compilation is in progress; nothing was captured";

        public static string AuditSurfaces(string sourceFolder, string level)
        {
            string libraryPath = Path.Combine(sourceFolder, "shaders.xr");
            var library = XRay.Importer.Core.Level.XRayShaderLibrary.Read(File.ReadAllBytes(libraryPath), libraryPath);
            string levelPath = Path.Combine(sourceFolder, "levels", level, "level");
            var data = XRay.Importer.Core.Level.XRayLevelReader.Read(File.ReadAllBytes(levelPath), levelPath);
            var seen = new HashSet<string>();
            var builder = new StringBuilder();
            foreach (var shader in data.Shaders)
            {
                if (!seen.Add(shader.EngineShader))
                {
                    continue;
                }

                library.TryGet(shader.EngineShader, out var blender);
                XRayLevelSurface surface = XRayLevelSurfaceRules.Classify(shader, blender, false);
                builder.Append(shader.EngineShader).Append(" class=").Append(blender?.ClassId).Append(" kind=").Append(surface.Kind).Append(" fallback=").Append(surface.IsFallback).Append(" properties=");
                if (blender != null)
                {
                    foreach (var property in blender.Properties)
                    {
                        builder.Append(property.Key).Append('=').Append(property.Value).Append(';');
                    }
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        public static string AuditDetailGround(string level, int sampleCount)
        {
            var database = AssetDatabase.LoadAssetAtPath<XRayDetailDatabase>(XRayLevelPaths.Details(level) + "/DetailDatabase.asset");
            if (database == null || database.Instances == null)
            {
                return "no detail database: " + level;
            }

            Physics.SyncTransforms();
            byte[] bytes = database.Instances.bytes;
            int stride = Math.Max(1, database.InstanceCount / Math.Max(1, sampleCount));
            int sampled = 0;
            int hitCount = 0;
            int separated = 0;
            int aboveGround = 0;
            int belowGround = 0;
            double totalGap = 0.0;
            float maxGap = 0.0f;
            var models = new Dictionary<int, int>();
            var examples = new StringBuilder();
            foreach (XRayDetailChunk chunk in database.Chunks)
            {
                for (int i = 0; i < chunk.InstanceCount; i++)
                {
                    int index = chunk.InstanceStart + i;
                    if (index % stride != 0)
                    {
                        continue;
                    }

                    int p = index * XRayDetailDatabase.INSTANCE_STRIDE;
                    Vector3 origin = chunk.PositionOrigin;
                    float x = origin.x + BitConverter.ToUInt16(bytes, p) / XRayDetailDatabase.POSITION_QUANT * database.ChunkSize;
                    float z = origin.z + BitConverter.ToUInt16(bytes, p + 2) / XRayDetailDatabase.POSITION_QUANT * database.ChunkSize;
                    float y = BitConverter.ToSingle(bytes, p + 4);
                    sampled++;
                    if (Physics.Raycast(new Vector3(x, y + 1.0f, z), Vector3.down, out RaycastHit hit, 20.0f))
                    {
                        hitCount++;
                        float gap = y - hit.point.y;
                        totalGap += Math.Abs(gap);
                        maxGap = Mathf.Max(maxGap, Math.Abs(gap));
                        if (Math.Abs(gap) > 0.05f)
                        {
                            separated++;
                            int model = bytes[p + 12];
                            models.TryGetValue(model, out int count);
                            models[model] = count + 1;
                            aboveGround += gap > 0.0f ? 1 : 0;
                            belowGround += gap < 0.0f ? 1 : 0;
                            if (separated <= 16)
                            {
                                examples.Append($"\nmodel={model} root=({x:0.000},{y:0.000},{z:0.000}) gap={gap:0.0000} collider={hit.collider.name}");
                            }
                        }
                    }
                }
            }

            return $"samples={sampled} hits={hitCount} gapOver5cm={separated} aboveGround={aboveGround} belowGround={belowGround} meanAbsGap={totalGap / Math.Max(1, hitCount):0.0000} maxAbsGap={maxGap:0.0000} affectedModels={models.Count}{examples}";
        }

        public static string Benchmark(string level, float px, float py, float pz, float tx, float ty, float tz, int frames)
        {
            string scenePath = XRayLevelPaths.ScenePath(level);
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.isLoaded)
            {
                return "scene is not loaded: " + scenePath;
            }

            var cameraObject = new GameObject("XRay Benchmark Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
#if XRAY_HDRP
                cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
#endif
                camera.transform.position = new Vector3(px, py, pz);
                camera.transform.LookAt(new Vector3(tx, ty, tz));
                camera.fieldOfView = 67.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000.0f;
                camera.targetTexture = target;
                for (int i = 0; i < 3; i++)
                {
                    camera.Render();
                }

                using (Unity.Profiling.ProfilerRecorder draws = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count"))
                using (Unity.Profiling.ProfilerRecorder batches = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count"))
                using (Unity.Profiling.ProfilerRecorder triangles = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count"))
                using (Unity.Profiling.ProfilerRecorder setPass = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count"))
                {
                    var times = new List<double>();
                    var watch = new System.Diagnostics.Stopwatch();
                    for (int i = 0; i < frames; i++)
                    {
                        watch.Restart();
                        camera.Render();
                        times.Add(watch.Elapsed.TotalMilliseconds);
                    }

                    times.Sort();
                    var builder = new StringBuilder();
                    builder.Append($"renderMedianMs={times[times.Count / 2]:0.00} renderP95Ms={times[Math.Min(times.Count - 1, (int)(times.Count * 0.95))]:0.00} ");
                    builder.Append($"draws={draws.CurrentValue} batches={batches.CurrentValue} setPass={setPass.CurrentValue} triangles={triangles.CurrentValue} ");
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                    int visibleRenderers = 0;
                    int visibleSubmeshes = 0;
                    int totalRenderers = 0;
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
                        {
                            totalRenderers++;
                            if (GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                            {
                                visibleRenderers++;
                                visibleSubmeshes += renderer.sharedMaterials.Length;
                            }
                        }
                    }

                    builder.Append($"staticRenderers={totalRenderers} frustumRenderers={visibleRenderers} frustumSubmeshes={visibleSubmeshes} ");
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (XRayDetailVegetation vegetation in root.GetComponentsInChildren<XRayDetailVegetation>())
                        {
                            builder.Append($"detailVisible={vegetation.LastVisibleInstances} detailDraws={vegetation.LastDrawCalls} ");
                        }

                        foreach (XRayLevelInstancedRenderer instanced in root.GetComponentsInChildren<XRayLevelInstancedRenderer>())
                        {
                            builder.Append($"muVisibleBatches={instanced.LastVisibleBatches} muVisibleInstances={instanced.LastVisibleInstances} muShadowOnlyBatches={instanced.LastShadowOnlyBatches} ");
                        }
                    }

                    return builder.ToString();
                }
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        public static string SetInstanceCellSize(float size)
        {
            XRayLevelImportSettings settings = XRayAssetSourceUserSettings.instance.LevelImport;
            settings.InstanceCellSize = size;
            XRayAssetSourceUserSettings.instance.SaveSettings();
            return JsonUtility.ToJson(settings);
        }

        public static string GroundHeight(float x, float z)
        {
            Physics.SyncTransforms();
            return Physics.Raycast(new Vector3(x, 2000.0f, z), Vector3.down, out RaycastHit hit, 4000.0f) ? hit.point.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " + hit.collider.name : "miss";
        }

        public static string OpenScene(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return scene.path;
        }

        public static string DeleteGeneratedLevels()
        {
            if (XRayLevelImportJob.Current != null)
            {
                return "import running";
            }

            bool deleted = AssetDatabase.DeleteAsset(XRayLevelPaths.ROOT);
            return deleted ? "deleted " + XRayLevelPaths.ROOT : "nothing deleted";
        }

        public static string MeasureSceneLoad(string level)
        {
            string scenePath = XRayLevelPaths.ScenePath(level);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            double open = watch.Elapsed.TotalMilliseconds;
            long meshBytes = 0;
            long textureBytes = 0;
            int meshes = 0;
            var textures = new HashSet<Texture>();
            var seenMeshes = new HashSet<Mesh>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null && seenMeshes.Add(filter.sharedMesh))
                    {
                        meshBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(filter.sharedMesh);
                    }
                }

                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (collider.sharedMesh != null && seenMeshes.Add(collider.sharedMesh))
                    {
                        meshBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(collider.sharedMesh);
                    }
                }

                foreach (XRayLevelInstancedRenderer instanced in root.GetComponentsInChildren<XRayLevelInstancedRenderer>(true))
                {
                    foreach (XRayInstanceBatch batch in instanced.BatchSet.Batches)
                    {
                        if (batch.Mesh != null && seenMeshes.Add(batch.Mesh))
                        {
                            meshBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(batch.Mesh);
                        }
                    }
                }
            }

            foreach (Material material in CollectMaterials(scene))
            {
                foreach (int id in material.GetTexturePropertyNameIDs())
                {
                    Texture texture = material.GetTexture(id);
                    if (texture != null && textures.Add(texture))
                    {
                        textureBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                    }
                }
            }

            meshes = seenMeshes.Count;
            return $"openSceneMs={open:0} meshes={meshes} meshMB={meshBytes / 1048576.0:0.0} textures={textures.Count} textureMB={textureBytes / 1048576.0:0.0}";
        }

        public static string StartImport(string level)
        {
            bool queued = XRayLevelDropHandler.Start(level, null, "diagnostics request");
            return queued ? "queued " + level : "busy " + level;
        }

        public static string ReimportGenerated(string level)
        {
            XRayLevelImportSettings settings = XRayAssetSourceUserSettings.instance.LevelImport.Clone();
            settings.MaterialUpdateMode = XRayLevelMaterialUpdateMode.Update;
            settings.OpenSceneAfterImport = false;
            bool queued = XRayLevelImportJob.Enqueue(new XRayLevelImportRequest { LevelName = level, Identification = "generated rendering repair", Settings = settings });
            return queued ? "queued " + level : "busy " + level;
        }

        public static string Status()
        {
            XRayLevelImportJob job = XRayLevelImportJob.Current;
            var builder = new StringBuilder();
            if (job != null)
            {
                builder.Append($"running {job.LevelName} stage='{job.Stage}' progress={job.Progress:0.000}");
                return builder.ToString();
            }

            XRayLevelImportRecord record = XRayLevelImportJob.LastRecord;
            if (record == null)
            {
                return "idle, no import in this session; " + XRayLevelDropHandler.LastMessage;
            }

            builder.Append($"last {record.LevelName} {XRayLevelImportJob.LastState}; errors={record.Errors.Count} warnings={record.Warnings.Count}\n");
            foreach (string error in record.Errors)
            {
                builder.Append("E: ").Append(error).Append('\n');
            }

            foreach (XRayLevelMetric metric in record.Metrics)
            {
                builder.Append(metric.Name).Append('=').Append(metric.Value.ToString("0.###")).Append(' ');
            }

            return builder.ToString();
        }

        public static string Warnings(int max)
        {
            XRayLevelImportRecord record = XRayLevelImportJob.LastRecord;
            if (record == null)
            {
                return "none";
            }

            var builder = new StringBuilder();
            for (int i = 0; i < record.Warnings.Count && i < max; i++)
            {
                builder.Append("W: ").Append(record.Warnings[i]).Append('\n');
            }

            foreach (string limitation in record.Limitations)
            {
                builder.Append("L: ").Append(limitation).Append('\n');
            }

            return builder.ToString();
        }

        public static string SimulateExplorerDrop(string sourceFile, string assetFolder)
        {
            string folder = assetFolder.TrimEnd('/');
            Directory.CreateDirectory(folder);
            string target = folder + "/level";
            File.Copy(sourceFile, target, true);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            AssetImporter importer = AssetImporter.GetAtPath(target);
            Type type = AssetDatabase.GetMainAssetTypeAtPath(target);
            return $"asset={target} guid={AssetDatabase.AssetPathToGUID(target)} importer={importer?.GetType().Name ?? "none"} type={type?.Name ?? "none"} meta={File.Exists(target + ".meta")} handlerMessage={XRayLevelDropHandler.LastMessage}";
        }

        public static string ShaderMessages(string shaderName)
        {
            Shader shader = Shader.Find(string.IsNullOrEmpty(shaderName) ? MonolithMaterialConfigurator.SHADER_NAME : shaderName);
            if (shader == null)
            {
                return "shader not found";
            }

            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            var builder = new StringBuilder($"messages={messages.Length} errors={ShaderUtil.ShaderHasError(shader)}\n");
            foreach (ShaderMessage message in messages)
            {
                builder.Append(message.severity).Append(' ').Append(message.file).Append(':').Append(message.line).Append(' ').Append(message.message).Append(' ').Append(message.messageDetails).Append('\n');
            }

            return builder.ToString();
        }

        public static string VerifyScene(string level)
        {
            string scenePath = XRayLevelPaths.ScenePath(level);
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                opened = true;
            }

            var builder = new StringBuilder();
            int renderers = 0;
            int nullMaterials = 0;
            int missingMeshes = 0;
            int submeshes = 0;
            long triangles = 0;
            var materials = new HashSet<Material>();
            var shaders = new Dictionary<string, int>();
            int instancedBatches = 0;
            int instancedInstances = 0;
            int detailInstances = 0;
            int colliders = 0;
            int missingScripts = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                }

                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderers++;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        missingMeshes++;
                        continue;
                    }

                    submeshes += filter.sharedMesh.subMeshCount;
                    for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                    {
                        triangles += filter.sharedMesh.GetSubMesh(i).indexCount / 3;
                    }

                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            nullMaterials++;
                            continue;
                        }

                        materials.Add(material);
                    }
                }

                foreach (XRayLevelInstancedRenderer instanced in root.GetComponentsInChildren<XRayLevelInstancedRenderer>(true))
                {
                    if (instanced.BatchSet == null)
                    {
                        builder.Append("instanced renderer without batch set\n");
                        continue;
                    }

                    foreach (XRayInstanceBatch batch in instanced.BatchSet.Batches)
                    {
                        instancedBatches++;
                        instancedInstances += batch.Count;
                        if (batch.Mesh == null)
                        {
                            missingMeshes++;
                        }

                        foreach (Material material in batch.Materials)
                        {
                            if (material == null)
                            {
                                nullMaterials++;
                            }
                            else
                            {
                                materials.Add(material);
                            }
                        }
                    }
                }

                foreach (XRayDetailVegetation vegetation in root.GetComponentsInChildren<XRayDetailVegetation>(true))
                {
                    if (vegetation.Database == null)
                    {
                        builder.Append("vegetation without database\n");
                        continue;
                    }

                    detailInstances += vegetation.Database.InstanceCount;
                    foreach (XRayDetailModelEntry model in vegetation.Database.Models)
                    {
                        if (model.Material == null || model.Mesh == null)
                        {
                            nullMaterials++;
                        }
                        else
                        {
                            materials.Add(model.Material);
                        }
                    }
                }

                colliders += root.GetComponentsInChildren<MeshCollider>(true).Length;
            }

            int noTexture = 0;
            foreach (Material material in materials)
            {
                string name = material.shader.name;
                shaders[name] = shaders.TryGetValue(name, out int count) ? count + 1 : 1;
                if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") == null)
                {
                    noTexture++;
                }
            }

            builder.Append($"scene={scenePath} renderers={renderers} submeshes={submeshes} triangles={triangles} materials={materials.Count} materialsWithoutBaseMap={noTexture} nullMaterials={nullMaterials} missingMeshes={missingMeshes} missingScripts={missingScripts} instancedBatches={instancedBatches} instancedInstances={instancedInstances} detailInstances={detailInstances} colliders={colliders}\n");
            foreach (KeyValuePair<string, int> pair in shaders)
            {
                builder.Append($"shader {pair.Key}: {pair.Value}\n");
            }

            if (opened)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            return builder.ToString();
        }

        private static HashSet<Material> CollectMaterials(Scene scene)
        {
            var materials = new HashSet<Material>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    materials.UnionWith(renderer.sharedMaterials);
                }

                foreach (XRayLevelInstancedRenderer instanced in root.GetComponentsInChildren<XRayLevelInstancedRenderer>(true))
                {
                    foreach (XRayInstanceBatch batch in instanced.BatchSet.Batches)
                    {
                        materials.UnionWith(batch.Materials);
                    }

                    if (instanced.BatchSet.Lods != null)
                    {
                        materials.Add(instanced.BatchSet.Lods.Material);
                    }
                }

                foreach (XRayDetailVegetation vegetation in root.GetComponentsInChildren<XRayDetailVegetation>(true))
                {
                    foreach (XRayDetailModelEntry model in vegetation.Database.Models)
                    {
                        materials.Add(model.Material);
                    }
                }
            }

            materials.Remove(null);
            return materials;
        }

        public static string ListBatches(string filter, int max)
        {
            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            if (renderer == null || renderer.BatchSet == null)
            {
                return "no instanced renderer";
            }

            var builder = new StringBuilder();
            IReadOnlyList<XRayInstanceBatch> batches = renderer.BatchSet.Batches;
            for (int i = 0; i < batches.Count && max > 0; i++)
            {
                XRayInstanceBatch batch = batches[i];
                string material = batch.Materials.Length > 0 && batch.Materials[0] != null ? batch.Materials[0].name : "-";
                if (!material.Contains(filter))
                {
                    continue;
                }

                for (int j = 0; j < batch.Count && max > 0; j++)
                {
                    Matrix4x4 m = batch.Matrices[j];
                    Vector3 scale = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
                    float tilt = Vector3.Angle(m.GetColumn(1), Vector3.up);
                    builder.Append($"{i}/{j} {material} pos={(Vector3)m.GetColumn(3)} scale={scale} tilt={tilt:0.#} det={m.determinant:0.###}\n");
                    max--;
                }
            }

            return builder.ToString();
        }

        public static string FindInstances(float x, float y, float z, float radius, int max)
        {
            var point = new Vector3(x, y, z);
            var builder = new StringBuilder();
            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            if (renderer != null && renderer.BatchSet != null)
            {
                IReadOnlyList<XRayInstanceBatch> batches = renderer.BatchSet.Batches;
                for (int i = 0; i < batches.Count && max > 0; i++)
                {
                    XRayInstanceBatch batch = batches[i];
                    if (batch.Mesh == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < batch.Count && max > 0; j++)
                    {
                        Bounds local = batch.Mesh.bounds;
                        Vector3 localPoint = batch.Matrices[j].inverse.MultiplyPoint3x4(point);
                        if (local.SqrDistance(localPoint) > radius * radius)
                        {
                            continue;
                        }

                        Vector4 data = batch.Data != null && j < batch.Data.Length ? batch.Data[j] : Vector4.zero;
                        builder.Append($"batch {i} '{batch.Name}' instance {j}/{batch.Count} mesh={batch.Mesh.name} submeshes={batch.Mesh.subMeshCount} lods={batch.HasLods} shadows={batch.CastShadows} pos={(Vector3)batch.Matrices[j].GetColumn(3)} data={data} dataLength={(batch.Data != null ? batch.Data.Length : 0)}\n");
                        foreach (Material material in batch.Materials)
                        {
                            if (material != null)
                            {
                                builder.Append($"  material {material.name} shader={material.shader.name} instancing={material.enableInstancing} hemiMode={(material.HasProperty("_MonolithLevelHemiMode") ? material.GetFloat("_MonolithLevelHemiMode") : -1.0f)} keywords={string.Join(" ", material.shaderKeywords)}\n");
                            }
                        }

                        max--;
                    }
                }
            }

            foreach (Renderer sceneRenderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (max <= 0 || sceneRenderer.bounds.SqrDistance(point) > radius * radius)
                {
                    continue;
                }

                builder.Append($"renderer {sceneRenderer.name} type={sceneRenderer.GetType().Name} path={GetPath(sceneRenderer.transform)} bounds={sceneRenderer.bounds} materials={string.Join(",", Array.ConvertAll(sceneRenderer.sharedMaterials, material => material != null ? material.name : "-"))}\n");
                max--;
            }

            return builder.ToString();
        }

        public static string ClearInstancedProperties()
        {
            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            if (renderer == null)
            {
                return "no instanced renderer";
            }

            var properties = (List<MaterialPropertyBlock>)typeof(XRayLevelInstancedRenderer).GetField("_properties", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(renderer);
            int count = properties.Count;
            properties.Clear();
            return "cleared " + count;
        }

        public static string ProbeLodFrames(string level, int lod, float distance, float elevation, float yaw, int width, int height, int frames, string output, int compileMode)
        {
            Scene scene = SceneManager.GetSceneByPath(XRayLevelPaths.ScenePath(level));
            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            XRayLevelLodSet set = renderer != null && renderer.BatchSet != null ? renderer.BatchSet.Lods : null;
            if (!scene.isLoaded || set == null || lod < 0 || lod >= set.Count)
            {
                return "scene is not loaded, no LOD set or LOD index is out of range";
            }

            Vector4 sphere = set.Spheres[lod];
            var center = new Vector3(sphere.x, sphere.y, sphere.z);
            Vector3 eye = center + Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.back * distance + Vector3.up * elevation;
            float geometryLod = set.GeometryLod;
            bool previousLod = renderer.LodImpostors;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            var builder = new StringBuilder($"lod={lod} async={previousAsync} editorAsync={EditorSettings.asyncShaderCompilation} compileMode={compileMode} material={(set.Material != null ? set.Material.name : "null")} passes={(set.Material != null ? set.Material.passCount : 0)}");
            for (int pass = 0; set.Material != null && pass < set.Material.passCount; pass++)
            {
                builder.Append(' ').Append(pass).Append('=').Append(set.Material.GetPassName(pass)).Append(set.Material.GetShaderPassEnabled(set.Material.GetPassName(pass)) ? "" : "(off)");
            }

            builder.Append('\n');
            var cameraObject = new GameObject("XRay LOD Probe Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
#if XRAY_HDRP
                var cameraData = cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                cameraData.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
                cameraData.customRenderingSettings = true;
                cameraData.renderingPathCustomFrameSettings.SetEnabled(UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess, false);
                cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess] = true;
#endif
                camera.fieldOfView = 67.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000.0f;
                camera.targetTexture = target;
                camera.transform.position = eye;
                camera.transform.LookAt(center);
                float ssa = sphere.w / ((center - eye).sqrMagnitude + XRayLevelLodRenderer.EPS) * set.LodFactors[lod];
                SetGeometryLod(set, 50.0f / ssa / (width * height * (90.0f / 67.5f) * (90.0f / 67.5f)));
                renderer.LodImpostors = true;
                if (compileMode > 0)
                {
                    ShaderUtil.allowAsyncCompilation = compileMode == 2;
                }

                for (int frame = 0; frame <= frames; frame++)
                {
                    bool none = frame == frames;
                    renderer.enabled = !none;
                    string passesBefore = CompiledPasses(set.Material);
                    bool compilingBefore = ShaderUtil.anythingCompiling;
                    camera.Render();
                    string passesAfter = CompiledPasses(set.Material);
                    SaveFloat(target, texture, output + (none ? "_none" : "_f" + frame) + ".exr");
                    builder.Append($"{(none ? "none" : "frame " + frame)}: compilingBefore={compilingBefore} passesBefore={passesBefore} impostors={renderer.LastImpostors} passesAfter={passesAfter} compilingAfter={ShaderUtil.anythingCompiling}\n");
                }

                return builder.ToString();
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsync;
                SetGeometryLod(set, geometryLod);
                renderer.LodImpostors = previousLod;
                renderer.enabled = true;
                cameraObject.GetComponent<Camera>().targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        public static string CaptureWithoutFrameSettings(string level, float px, float py, float pz, float tx, float ty, float tz, int width, int height, string output, string fields)
        {
            Scene scene = SceneManager.GetSceneByPath(XRayLevelPaths.ScenePath(level));
            if (!scene.isLoaded)
            {
                return "scene is not loaded";
            }

            if (ShaderUtil.anythingCompiling)
            {
                return SHADERS_NOT_READY;
            }

            var cameraObject = new GameObject("XRay Frame Settings Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
#if XRAY_HDRP
                var cameraData = cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                cameraData.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
                cameraData.customRenderingSettings = true;
                foreach (string name in ("Postprocess," + fields).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var field = (UnityEngine.Rendering.HighDefinition.FrameSettingsField)Enum.Parse(typeof(UnityEngine.Rendering.HighDefinition.FrameSettingsField), name.Trim());
                    cameraData.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
#endif
                camera.fieldOfView = 67.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000.0f;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(px, py, pz);
                camera.transform.LookAt(new Vector3(tx, ty, tz));
                for (int frame = 0; frame < 3; frame++)
                {
                    camera.Render();
                }

                SaveFloat(target, texture, output + ".exr");
                return $"saved {output} disabled=Postprocess,{fields}";
            }
            finally
            {
                cameraObject.GetComponent<Camera>().targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        public static string LodResources()
        {
            int meshes = 0;
            foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (mesh.name == "X-Ray LOD impostors")
                {
                    meshes++;
                }
            }

            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            return $"lodMeshes={meshes} cameraEntries={(renderer != null ? renderer.LodMeshCount : -1)} lodImpostors={(renderer != null && renderer.LodImpostors)} enabled={(renderer != null && renderer.enabled)}";
        }

        public static string KickAsyncCompile(string path)
        {
            return CompileMaterialPasses(path, false);
        }

        public static string CompileMaterialPasses(string path, bool forceSync)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                return "no material " + path;
            }

            for (int pass = 0; pass < material.passCount; pass++)
            {
                ShaderUtil.CompilePass(material, pass, forceSync);
            }

            return $"requested {material.passCount} passes compiled={CompiledPasses(material)} compiling={ShaderUtil.anythingCompiling}";
        }

        private static string CompiledPasses(Material material)
        {
            if (material == null)
            {
                return "-";
            }

            var builder = new StringBuilder();
            for (int pass = 0; pass < material.passCount; pass++)
            {
                builder.Append(ShaderUtil.IsPassCompiled(material, pass) ? '1' : '0');
            }

            return builder.ToString();
        }

        private static void SaveFloat(RenderTexture target, Texture2D texture, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
        }

        public static string ShaderBatcherInfo(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            var builder = new StringBuilder($"shader={shaderName} found={shader != null} srpBatcher={GraphicsSettings.useScriptableRenderPipelineBatching}\n");
            foreach (System.Reflection.MethodInfo method in typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (!method.Name.Contains("SRPBatcher"))
                {
                    continue;
                }

                System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                builder.Append(method.Name).Append('(').Append(string.Join(",", Array.ConvertAll(parameters, parameter => parameter.ParameterType.Name))).Append(')');
                if (shader != null && parameters.Length == 2 && parameters[0].ParameterType == typeof(Shader) && parameters[1].ParameterType == typeof(int))
                {
                    builder.Append(" = ").Append(method.Invoke(null, new object[] { shader, 0 }));
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static string GetPath(Transform transform)
        {
            return transform.parent != null ? GetPath(transform.parent) + "/" + transform.name : transform.name;
        }

        public static string CaptureBatch(string level, string output, string specs)
        {
            var builder = new StringBuilder();
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string spec in specs.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(spec))
                {
                    continue;
                }

                string[] parts = spec.Trim().Split('|');
                float[] eye = Array.ConvertAll(parts[1].Split(','), value => float.Parse(value, invariant));
                float[] target = Array.ConvertAll(parts[2].Split(','), value => float.Parse(value, invariant));
                float[] sun = Array.ConvertAll(parts[3].Split(','), value => float.Parse(value, invariant));
                float[] light = Array.ConvertAll(parts[4].Split(','), value => float.Parse(value, invariant));
                float debug = float.Parse(parts[5], invariant);
                float[] options = Array.ConvertAll(parts[6].Split(','), value => float.Parse(value, invariant));
                bool raw = parts[7] == "1";
                bool taa = parts[8] == "1";
                builder.Append(parts[0]).Append(": ").Append(CaptureLighting(level, eye[0], eye[1], eye[2], target[0], target[1], target[2], 960, 540, output + "_" + parts[0], debug, options[0], options[1], options[2], options[3], sun[0], sun[1], light[0], light[1], light[2], light[3], taa, raw)).Append('\n');
            }

            return builder.ToString();
        }

        public static string CaptureLighting(string level, float px, float py, float pz, float tx, float ty, float tz, int width, int height, string output, float debug, float ox, float oy, float oz, float ow, float sunPitch, float sunYaw, float intensityScale, float r, float g, float b, bool taa, bool raw)
        {
            Light sun = null;
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional && light.isActiveAndEnabled)
                {
                    sun = light;
                    break;
                }
            }

            if (sun == null)
            {
                return "no directional light";
            }

            float intensity = sun.intensity;
            Color color = sun.color;
            try
            {
                sun.intensity = intensity * intensityScale;
                sun.color = r < 0.0f ? color : new Color(r, g, b);
                return CaptureDiagnostic(level, px, py, pz, tx, ty, tz, width, height, output, debug, ox, oy, oz, ow, sunPitch, sunYaw, true, taa, raw) + $" intensity={sun.intensity:0.###}";
            }
            finally
            {
                sun.intensity = intensity;
                sun.color = color;
            }
        }

        public static string CaptureLodLighting(string level, int lod, float distance, float elevation, float yaw, int width, int height, string output, string debugList, bool raw)
        {
            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            XRayLevelLodSet set = renderer != null && renderer.BatchSet != null ? renderer.BatchSet.Lods : null;
            if (set == null || lod < 0 || lod >= set.Count)
            {
                return "no LOD set in the open scene or LOD index is out of range";
            }

            Vector4 sphere = set.Spheres[lod];
            var center = new Vector3(sphere.x, sphere.y, sphere.z);
            Vector3 eye = center + Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.back * distance + Vector3.up * elevation;
            float geometryLod = set.GeometryLod;
            bool previous = renderer.LodImpostors;
            var builder = new StringBuilder($"lod={lod} radius={sphere.w:0.##} eye=({eye.x:0.##},{eye.y:0.##},{eye.z:0.##})\n");
            try
            {
                foreach (string item in debugList.Split(','))
                {
                    float debug = float.Parse(item, System.Globalization.CultureInfo.InvariantCulture);
                    string suffix = "_d" + item;
                    renderer.LodImpostors = false;
                    renderer.enabled = true;
                    builder.Append("full ").Append(CaptureDiagnostic(level, eye.x, eye.y, eye.z, center.x, center.y, center.z, width, height, output + "_full" + suffix, debug, 0, 0, 0, 0, -1000.0f, 0.0f, true, false, raw)).Append('\n');
                    float ssa = sphere.w / ((center - eye).sqrMagnitude + XRayLevelLodRenderer.EPS) * set.LodFactors[lod];
                    SetGeometryLod(set, 50.0f / ssa / (width * height * (90.0f / 67.5f) * (90.0f / 67.5f)));
                    renderer.LodImpostors = true;
                    builder.Append("impostor ").Append(CaptureDiagnostic(level, eye.x, eye.y, eye.z, center.x, center.y, center.z, width, height, output + "_impostor" + suffix, debug, 0, 0, 0, 0, -1000.0f, 0.0f, true, false, raw)).Append('\n');
                    SetGeometryLod(set, geometryLod);
                    renderer.LodImpostors = false;
                    renderer.enabled = false;
                    builder.Append("none ").Append(CaptureDiagnostic(level, eye.x, eye.y, eye.z, center.x, center.y, center.z, width, height, output + "_none" + suffix, debug, 0, 0, 0, 0, -1000.0f, 0.0f, true, false, raw)).Append('\n');
                    renderer.enabled = true;
                }

                return builder.ToString();
            }
            finally
            {
                SetGeometryLod(set, geometryLod);
                renderer.LodImpostors = previous;
                renderer.enabled = true;
            }
        }

        public static string FindLods(float x, float z, float radius, int max)
        {
            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            XRayLevelLodSet set = renderer != null && renderer.BatchSet != null ? renderer.BatchSet.Lods : null;
            if (set == null)
            {
                return "no LOD set";
            }

            var builder = new StringBuilder();
            for (int i = 0; i < set.Count && max > 0; i++)
            {
                Vector4 sphere = set.Spheres[i];
                if (new Vector2(sphere.x - x, sphere.z - z).magnitude <= radius)
                {
                    builder.Append($"{i}: ({sphere.x:0.#},{sphere.y:0.#},{sphere.z:0.#}) R={sphere.w:0.#} factor={set.LodFactors[i]:0.###}\n");
                    max--;
                }
            }

            return builder.ToString();
        }

        public static string CaptureFloraComparison(string level, float px, float py, float pz, float tx, float ty, float tz, int width, int height, string output, float debug, float sunPitch, float sunYaw, bool taa)
        {
            Scene scene = SceneManager.GetSceneByPath(XRayLevelPaths.ScenePath(level));
            if (!scene.isLoaded)
            {
                return "scene is not loaded; open it first: " + XRayLevelPaths.ScenePath(level);
            }

            var flora = new List<(Material Material, float Id, float Weight)>();
            foreach (Material material in CollectMaterials(scene))
            {
                if (material.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA"))
                {
                    flora.Add((material, material.GetFloat("_MonolithMaterialId"), material.GetFloat("_MonolithMaterialWeight")));
                }
            }

            var builder = new StringBuilder($"floraMaterials={flora.Count}\n");
            try
            {
                foreach ((Material material, float id, float weight) in flora)
                {
                    material.DisableKeyword("_MONOLITH_LEVEL_FLORA");
                    System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(XRayAssetMarker.ReadKey(AssetDatabase.GetAssetPath(material)) ?? string.Empty, @"\|m(\d+)w([0-9.,]+)b");
                    if (match.Success)
                    {
                        material.SetFloat("_MonolithMaterialId", float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                        material.SetFloat("_MonolithMaterialWeight", float.Parse(match.Groups[2].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture));
                    }
                }

                builder.Append("legacy: ").Append(CaptureDiagnostic(level, px, py, pz, tx, ty, tz, width, height, output + "_legacy", debug, 0, 0, 0, 0, sunPitch, sunYaw, true, taa, false)).Append('\n');
            }
            finally
            {
                foreach ((Material material, float id, float weight) in flora)
                {
                    material.EnableKeyword("_MONOLITH_LEVEL_FLORA");
                    material.SetFloat("_MonolithMaterialId", id);
                    material.SetFloat("_MonolithMaterialWeight", weight);
                }
            }

            builder.Append("flora: ").Append(CaptureDiagnostic(level, px, py, pz, tx, ty, tz, width, height, output + "_flora", debug, 0, 0, 0, 0, sunPitch, sunYaw, true, taa, false));
            return builder.ToString();
        }

        public static string CompareInstancing(string level, int batchIndex, int instance, float yaw, int width, int height, string output, float debug)
        {
            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            if (renderer == null || renderer.BatchSet == null || batchIndex < 0 || batchIndex >= renderer.BatchSet.Batches.Count)
            {
                return "no instanced renderer or batch index out of range";
            }

            XRayInstanceBatch batch = renderer.BatchSet.Batches[batchIndex];
            instance = Mathf.Clamp(instance, 0, batch.Count - 1);
            Matrix4x4 matrix = batch.Matrices[instance];
            Vector4 data = batch.Data != null && instance < batch.Data.Length ? batch.Data[instance] : new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            Bounds bounds = batch.Mesh.bounds;
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            float extent = Mathf.Max(1.0f, matrix.MultiplyVector(bounds.extents).magnitude);
            Vector3 eye = center + Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.back * extent * 2.2f + Vector3.up * extent * 0.2f;
            XRayInstanceBatchSet original = renderer.BatchSet;
            XRayInstanceBatchSet single = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            single.Initialize("instancing-probe", original.CellSize, new List<XRayInstanceBatch> { new XRayInstanceBatch(batch.Name, batch.Mesh, batch.Materials, batch.Bounds, new[] { matrix }, new[] { data }, batch.CastShadows) });
            GameObject probe = null;
            var builder = new StringBuilder();
            try
            {
                renderer.BatchSet = single;
                builder.Append("instanced: ").Append(CaptureDiagnostic(level, eye.x, eye.y, eye.z, center.x, center.y, center.z, width, height, output + "_instanced", debug, 0, 0, 0, 0, -1000.0f, 0.0f, true, false, false)).Append('\n');
                renderer.enabled = false;
                probe = new GameObject("XRay Instancing Probe");
                SceneManager.MoveGameObjectToScene(probe, SceneManager.GetSceneByPath(XRayLevelPaths.ScenePath(level)));
                probe.transform.SetPositionAndRotation(matrix.GetColumn(3), Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1)));
                probe.transform.localScale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude) * Mathf.Sign(matrix.determinant);
                probe.AddComponent<MeshFilter>().sharedMesh = batch.Mesh;
                var meshRenderer = probe.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterials = batch.Materials;
                var block = new MaterialPropertyBlock();
                block.SetVector(XRayLevelInstancedRenderer.INSTANCE_DATA_PROPERTY, data);
                meshRenderer.SetPropertyBlock(block);
                Matrix4x4 rebuilt = probe.transform.localToWorldMatrix;
                float matrixError = 0.0f;
                for (int i = 0; i < 16; i++)
                {
                    matrixError = Mathf.Max(matrixError, Mathf.Abs(rebuilt[i] - matrix[i]));
                }

                builder.Append("meshRenderer: ").Append(CaptureDiagnostic(level, eye.x, eye.y, eye.z, center.x, center.y, center.z, width, height, output + "_meshrenderer", debug, 0, 0, 0, 0, -1000.0f, 0.0f, true, false, false)).Append('\n');
                builder.Append($"batch={batch.Name} instance={instance} det={matrix.determinant:0.####} scale=({matrix.GetColumn(0).magnitude:0.###},{matrix.GetColumn(1).magnitude:0.###},{matrix.GetColumn(2).magnitude:0.###}) transformRebuildError={matrixError:0.######}");
                return builder.ToString();
            }
            finally
            {
                if (probe != null)
                {
                    UnityEngine.Object.DestroyImmediate(probe);
                }

                renderer.enabled = true;
                renderer.BatchSet = original;
                UnityEngine.Object.DestroyImmediate(single);
            }
        }

        public static string CaptureDiagnostic(string level, float px, float py, float pz, float tx, float ty, float tz, int width, int height, string output, float debug, float ox, float oy, float oz, float ow, float sunPitch, float sunYaw, bool shadows, bool taa, bool raw)
        {
            Scene scene = SceneManager.GetSceneByPath(XRayLevelPaths.ScenePath(level));
            if (!scene.isLoaded)
            {
                return "scene is not loaded; open it first: " + XRayLevelPaths.ScenePath(level);
            }

            if (ShaderUtil.anythingCompiling)
            {
                return SHADERS_NOT_READY;
            }

            Light sun = null;
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional && light.isActiveAndEnabled)
                {
                    sun = light;
                    break;
                }
            }

            Quaternion sunRotation = sun != null ? sun.transform.rotation : Quaternion.identity;
            LightShadows sunShadows = sun != null ? sun.shadows : LightShadows.None;
            XRayDetailVegetation details = UnityEngine.Object.FindFirstObjectByType<XRayDetailVegetation>();
            System.Reflection.FieldInfo windField = typeof(XRayDetailVegetation).GetField("_wind", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool wind = details != null && (bool)windField.GetValue(details);
            var materials = new List<(Material Material, float Debug, Vector4 Options)>();
            foreach (Material material in CollectMaterials(scene))
            {
                if (material.HasProperty("_MonolithDebug"))
                {
                    materials.Add((material, material.GetFloat("_MonolithDebug"), material.GetVector("_MonolithDiagnosticOptions")));
                    material.SetFloat("_MonolithDebug", debug);
                    material.SetVector("_MonolithDiagnosticOptions", new Vector4(ox, oy, oz, ow));
                }
            }

            var cameraObject = new GameObject("XRay Diagnostic Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            try
            {
                if (details != null)
                {
                    windField.SetValue(details, false);
                }

                if (sun != null)
                {
                    if (sunPitch > -900.0f)
                    {
                        sun.transform.rotation = Quaternion.Euler(sunPitch, sunYaw, 0.0f);
                    }

                    sun.shadows = shadows ? (sunShadows == LightShadows.None ? LightShadows.Soft : sunShadows) : LightShadows.None;
                }

                Camera camera = cameraObject.AddComponent<Camera>();
#if XRAY_HDRP
                var cameraData = cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                cameraData.antialiasing = taa ? UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing : UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
                if (raw)
                {
                    cameraData.customRenderingSettings = true;
                    cameraData.renderingPathCustomFrameSettings.SetEnabled(UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess, false);
                    cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess] = true;
                }
#endif
                camera.fieldOfView = 67.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000.0f;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(px, py, pz);
                camera.transform.LookAt(new Vector3(tx, ty, tz));
                ShaderUtil.allowAsyncCompilation = false;
                for (int frame = 0; frame < (taa ? 8 : 3); frame++)
                {
                    camera.Render();
                }

                ShaderUtil.allowAsyncCompilation = previousAsync;
                if (ShaderUtil.anythingCompiling)
                {
                    return SHADERS_NOT_READY;
                }

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllBytes(output + ".exr", texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                Color[] pixels = texture.GetPixels();
                var preview = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                var encoded = new Color[pixels.Length];
                double sum = 0.0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    encoded[i] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(pixels[i].r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(pixels[i].g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(pixels[i].b)), 1.0f);
                    sum += (pixels[i].r + pixels[i].g + pixels[i].b) / 3.0;
                }

                preview.SetPixels(encoded);
                preview.Apply();
                File.WriteAllBytes(output + ".png", preview.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(preview);
                return $"saved {output} meanLinear={sum / pixels.Length:0.#####} sun={(sun != null ? sun.transform.forward.ToString("F3") : "none")}";
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsync;
                foreach ((Material material, float previousDebug, Vector4 previousOptions) in materials)
                {
                    material.SetFloat("_MonolithDebug", previousDebug);
                    material.SetVector("_MonolithDiagnosticOptions", previousOptions);
                }

                if (sun != null)
                {
                    sun.transform.rotation = sunRotation;
                    sun.shadows = sunShadows;
                }

                if (details != null)
                {
                    windField.SetValue(details, wind);
                }

                cameraObject.GetComponent<Camera>().targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        public static string CaptureSurface(string level, float cx, float cy, float cz, float nx, float ny, float nz, float distance, float elevation, float azimuth, int steps, float step, int width, int height, string output, bool taa)
        {
            var center = new Vector3(cx, cy, cz);
            Vector3 normal = new Vector3(nx, ny, nz).normalized;
            Vector3 tangent = Vector3.Cross(Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up, normal).normalized;
            tangent = Quaternion.AngleAxis(azimuth, normal) * tangent;
            Vector3 direction = (Mathf.Sin(elevation * Mathf.Deg2Rad) * normal + Mathf.Cos(elevation * Mathf.Deg2Rad) * tangent).normalized;
            Vector3 start = center + normal * 0.05f;
            if (Physics.Raycast(start, direction, out RaycastHit hit, distance))
            {
                distance = Mathf.Max(0.5f, hit.distance * 0.8f);
            }

            Vector3 eye = center + direction * distance;
            return $"eye=({eye.x:0.##},{eye.y:0.##},{eye.z:0.##}) distance={distance:0.##} " + CaptureSequence(level, eye.x, eye.y, eye.z, cx, cy, cz, steps, step, 0.0f, width, height, output, taa);
        }

        public static string CaptureSequence(string level, float px, float py, float pz, float tx, float ty, float tz, int steps, float step, float yawStep, int width, int height, string output, bool taa)
        {
            Scene scene = SceneManager.GetSceneByPath(XRayLevelPaths.ScenePath(level));
            if (!scene.isLoaded)
            {
                return "scene is not loaded; open it first: " + XRayLevelPaths.ScenePath(level);
            }

            XRayDetailVegetation details = UnityEngine.Object.FindFirstObjectByType<XRayDetailVegetation>();
            System.Reflection.FieldInfo windField = typeof(XRayDetailVegetation).GetField("_wind", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool wind = details != null && (bool)windField.GetValue(details);
            if (details != null)
            {
                windField.SetValue(details, false);
            }

            var cameraObject = new GameObject("XRay Sequence Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var builder = new StringBuilder();
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
#if XRAY_HDRP
                var cameraData = cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                cameraData.antialiasing = taa ? UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing : UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
#endif
                camera.fieldOfView = 67.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000.0f;
                camera.targetTexture = target;
                var origin = new Vector3(px, py, pz);
                Quaternion rotation = Quaternion.LookRotation(new Vector3(tx, ty, tz) - origin);
                Color32[] previous = null;
                double sum = 0.0;
                double max = 0.0;
                for (int i = 0; i < steps; i++)
                {
                    camera.transform.SetPositionAndRotation(origin + rotation * Vector3.forward * (step * i), Quaternion.AngleAxis(yawStep * i, Vector3.up) * rotation);
                    for (int frame = 0; frame < (taa ? 8 : 2); frame++)
                    {
                        camera.Render();
                    }

                    RenderTexture previousActive = RenderTexture.active;
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    texture.Apply();
                    RenderTexture.active = previousActive;
                    Color32[] pixels = texture.GetPixels32();
                    if (i == 0 || i == steps - 1)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        File.WriteAllBytes(output + "_step" + i + ".png", texture.EncodeToPNG());
                    }

                    if (previous != null)
                    {
                        int changed = 0;
                        for (int p = 0; p < pixels.Length; p++)
                        {
                            int difference = Mathf.Abs(pixels[p].r - previous[p].r) + Mathf.Abs(pixels[p].g - previous[p].g) + Mathf.Abs(pixels[p].b - previous[p].b);
                            changed += difference > 48 ? 1 : 0;
                        }

                        double fraction = (double)changed / pixels.Length;
                        sum += fraction;
                        max = Math.Max(max, fraction);
                        builder.Append(fraction.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                    }

                    previous = pixels;
                }

                return $"changedMean={(steps > 1 ? sum / (steps - 1) : 0.0):0.00000} changedMax={max:0.00000} perStep={builder}";
            }
            finally
            {
                if (details != null)
                {
                    windField.SetValue(details, wind);
                }

                cameraObject.GetComponent<Camera>().targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        public static string CaptureLod(string level, int lod, float distance, float elevation, float yaw, int width, int height, string output, bool impostor)
        {
            XRayLevelInstancedRenderer renderer = UnityEngine.Object.FindFirstObjectByType<XRayLevelInstancedRenderer>();
            XRayLevelLodSet set = renderer != null && renderer.BatchSet != null ? renderer.BatchSet.Lods : null;
            if (set == null || lod < 0 || lod >= set.Count)
            {
                return "no LOD set in the open scene or LOD index is out of range";
            }

            Vector4 sphere = set.Spheres[lod];
            var center = new Vector3(sphere.x, sphere.y, sphere.z);
            Vector3 eye = center + Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.back * distance + Vector3.up * elevation;
            float geometryLod = set.GeometryLod;
            bool previous = renderer.LodImpostors;
            try
            {
                if (impostor)
                {
                    float ssa = sphere.w / ((center - eye).sqrMagnitude + XRayLevelLodRenderer.EPS) * set.LodFactors[lod];
                    float pixels = width * height * (90.0f / 67.5f) * (90.0f / 67.5f);
                    SetGeometryLod(set, 50.0f / ssa / pixels);
                }

                renderer.LodImpostors = impostor;
                return Capture(level, eye.x, eye.y, eye.z, center.x, center.y, center.z, width, height, output, 0.0f) + $"; lod={lod} radius={sphere.w:0.##} factor={set.LodFactors[lod]:0.###} impostors={renderer.LastImpostors}";
            }
            finally
            {
                SetGeometryLod(set, geometryLod);
                renderer.LodImpostors = previous;
            }
        }

        private static void SetGeometryLod(XRayLevelLodSet set, float geometryLod)
        {
            set.Initialize(set.Material, set.Spheres, set.LodFactors, set.Positions, set.Uvs, set.Hemi, set.Sun, set.Normals, geometryLod, set.SsaLodA, set.SsaLodB, set.SsaDiscard);
        }

        public static string Capture(string level, float px, float py, float pz, float tx, float ty, float tz, int width, int height, string output, float debug)
        {
            string scenePath = XRayLevelPaths.ScenePath(level);
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.isLoaded)
            {
                return "scene is not loaded; open it first: " + scenePath;
            }

            var overridden = new List<Material>();
            if (debug > 0.0f)
            {
                foreach (Material material in CollectMaterials(scene))
                {
                    if (material.HasProperty("_MonolithDebug"))
                    {
                        material.SetFloat("_MonolithDebug", debug);
                        overridden.Add(material);
                    }
                }
            }

            var cameraObject = new GameObject("XRay Capture Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
#if XRAY_HDRP
                cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
#endif
                camera.transform.position = new Vector3(px, py, pz);
                camera.transform.LookAt(new Vector3(tx, ty, tz));
                camera.fieldOfView = 67.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000.0f;
                camera.targetTexture = target;
                camera.Render();
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;
                Color32[] pixels = texture.GetPixels32();
                double sum = 0.0;
                int black = 0;
                foreach (Color32 pixel in pixels)
                {
                    sum += pixel.r + pixel.g + pixel.b;
                    if (pixel.r + pixel.g + pixel.b < 6)
                    {
                        black++;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllBytes(output, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                return $"saved {output}, mean={sum / pixels.Length / 3.0:0.0}, nearBlack={black * 100.0 / pixels.Length:0.0}%";
            }
            finally
            {
                foreach (Material material in overridden)
                {
                    material.SetFloat("_MonolithDebug", 0.0f);
                }

                cameraObject.GetComponent<Camera>().targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
