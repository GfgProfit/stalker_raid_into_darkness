using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources.Textures;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelDecalDiagnostics
    {
        public static string VerifyTransientSceneChanges(string output)
        {
            Scene scene = SceneManager.GetActiveScene();
            string original = File.ReadAllText(scene.path);
            Directory.CreateDirectory(output);
            string copy = Path.Combine(output, scene.name + "_serialization.unity");
            if (!EditorSceneManager.SaveScene(scene, copy, true))
            {
                return "Scene copy failed; original scene is untouched";
            }

            string serialized = File.ReadAllText(copy);
            if (serialized != original)
            {
                return $"Scene serialization differs; original={original.Length} copy={serialized.Length}; original scene is untouched";
            }

            var method = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (method == null)
            {
                method = typeof(EditorSceneManager).GetMethod("MarkSceneClean", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            }

            if (method == null)
            {
                return "Scene serialization is byte identical; clean-state API unavailable; original scene is untouched";
            }

            method.Invoke(null, new object[] { scene });
            return "Scene serialization is byte identical; cleared transient dirty state without saving the original scene";
        }

        public static string Audit(string output, bool backup)
        {
            Directory.CreateDirectory(output);
            var materials = new HashSet<Material>();
            var builder = new StringBuilder("renderer,submesh,material,guid,shader,queue,texture,textureGuid,shadowCasting,zwrite,srcBlend,dstBlend,minX,minY,minZ,maxX,maxY,maxZ\n");
            int surfaces = 0;
            foreach (MeshRenderer renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                Material[] assigned = renderer.sharedMaterials;
                for (int i = 0; i < assigned.Length; i++)
                {
                    Material material = assigned[i];
                    string path = AssetDatabase.GetAssetPath(material);
                    string marker = XRayAssetMarker.ReadKey(path);
                    if (material == null || marker == null || !marker.StartsWith("level|") || !marker.Contains("|wallmark|"))
                    {
                        continue;
                    }

                    materials.Add(material);
                    surfaces++;
                    string texture = AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap"));
                    Bounds bounds = renderer.bounds;
                    builder.AppendLine(FormattableString.Invariant($"{renderer.name},{i},{path},{AssetDatabase.AssetPathToGUID(path)},{material.shader.name},{material.renderQueue},{texture},{AssetDatabase.AssetPathToGUID(texture)},{renderer.shadowCastingMode},{Property(material, "_ZWrite")},{Property(material, "_SrcBlend")},{Property(material, "_DstBlend")},{bounds.min.x},{bounds.min.y},{bounds.min.z},{bounds.max.x},{bounds.max.y},{bounds.max.z}"));
                }
            }

            File.WriteAllText(Path.Combine(output, "Surfaces.csv"), builder.ToString());
            if (backup)
            {
                string folder = Path.Combine(output, "Materials");
                Directory.CreateDirectory(folder);
                foreach (Material material in materials)
                {
                    string path = AssetDatabase.GetAssetPath(material);
                    string target = Path.Combine(folder, AssetDatabase.AssetPathToGUID(path) + ".mat");
                    if (!File.Exists(target))
                    {
                        File.Copy(path, target);
                    }
                }
            }

            return $"materials={materials.Count}; rendererSubmeshes={surfaces}";
        }

        public static string CompileShader()
        {
            Shader shader = Shader.Find(XRayLevelMaterialLibrary.DECAL_SHADER_NAME);
            var material = new Material(shader);
            bool previous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                material.EnableKeyword("DECALS_3RT");
                ShaderUtil.CompilePass(material, 0, true);
                material.DisableKeyword("DECALS_3RT");
                material.EnableKeyword("DECALS_4RT");
                ShaderUtil.CompilePass(material, 0, true);
                return "supported=" + shader.isSupported + "; " + XRayLevelDiagnostics.ShaderMessages(shader.name);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previous;
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        public static string AuditSource(string source, string levelName, string output)
        {
            string path = Path.Combine(source, "levels", levelName, "level");
            XRayLevelData level = XRayLevelReader.Read(File.ReadAllBytes(path), path);
            var rows = new StringBuilder("shaderIndex,shader,blender,visuals,triangles,dds,thm,bumpMode,bump,detail,detailScale,material,flags,lightmaps\n");
            XRayShaderLibrary library = XRayShaderLibrary.Read(File.ReadAllBytes(Path.Combine(source, "shaders.xr")), "shaders.xr");
            int count = 0;
            foreach (XRayLevelShader shader in level.Shaders)
            {
                string name = shader.EngineShader.ToLowerInvariant();
                if (!name.Contains("decal") && !name.Contains("wallmark"))
                {
                    continue;
                }

                int visuals = 0;
                long triangles = 0;
                foreach (XRayLevelVisual visual in level.Visuals)
                {
                    if (visual.ShaderId != shader.Index || !visual.IsGeometry)
                    {
                        continue;
                    }

                    visuals++;
                    triangles += visual.ModelType == XRayLevelFormat.MT_PROGRESSIVE && visual.SwiTriangles >= 0 ? visual.SwiTriangles : visual.Geometry.IndexCount / 3;
                }

                string texture = "textures/" + shader.Texture.Replace('\\', '/') + ".dds";
                string thm = Path.ChangeExtension(texture, ".thm");
                string absoluteThm = Path.Combine(source, thm);
                XRayTextureDescription description = File.Exists(absoluteThm) ? XRayThmReader.Read(File.ReadAllBytes(absoluteThm), thm) : null;
                library.TryGet(shader.EngineShader, out XRayBlender blender);
                rows.AppendLine(FormattableString.Invariant($"{shader.Index},{shader.EngineShader},{blender?.ClassId},{visuals},{triangles},{texture},{(description != null ? thm : "missing")},{description?.BumpMode},{description?.BumpName},{description?.DetailName},{description?.DetailScale},{description?.Material},{description?.Flags},{string.Join("|", shader.Lightmaps)}"));
                count++;
            }

            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "SourceMaterials.csv"), rows.ToString());
            return "sourceDecalMaterialEntries=" + count;
        }

        public static string AuditGeometry(string source, string levelName, string output)
        {
            string folder = Path.Combine(source, "levels", levelName);
            XRayLevelData level = XRayLevelReader.Read(File.ReadAllBytes(Path.Combine(folder, "level")), "level");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XRayLevelPaths.PrefabPath(levelName));
            var meshes = new Dictionary<string, Mesh>();
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                meshes[filter.sharedMesh.name] = filter.sharedMesh;
            }

            int tested = 0;
            int failed = 0;
            var rows = new StringBuilder("visual,asset,vertices,indices,vertexBytesEqual,indexBytesEqual\n");
            using (var geometry = XRayLevelGeometry.Open(Path.Combine(folder, "level.geom")))
            {
                var builder = new XRayLevelMeshBuilder(level, geometry);
                foreach (XRayLevelVisual visual in level.Visuals)
                {
                    if (!visual.IsGeometry || level.Shaders[visual.ShaderId].EngineShader != "effects\\wallmarkblend")
                    {
                        continue;
                    }

                    Mesh actual = null;
                    foreach (var pair in meshes)
                    {
                        if (pair.Key.EndsWith($"_V{visual.Index:00000}_decal", StringComparison.Ordinal))
                        {
                            actual = pair.Value;
                            break;
                        }
                    }

                    if (actual == null)
                    {
                        throw new InvalidOperationException("Missing separate wallmark visual " + visual.Index);
                    }

                    Mesh expected = builder.BuildMesh("reference", new List<XRayLevelGeometrySlice> { builder.ReadSlice(visual) }, new List<int>(), null);
                    try
                    {
                        using (Mesh.MeshDataArray a = MeshUtility.AcquireReadOnlyMeshData(actual))
                        {
                            using (Mesh.MeshDataArray b = Mesh.AcquireReadOnlyMeshData(expected))
                            {
                                bool vertices = BytesEqual(a[0].GetVertexData<byte>(), b[0].GetVertexData<byte>());
                                bool indices = BytesEqual(a[0].GetIndexData<byte>(), b[0].GetIndexData<byte>());
                                failed += vertices && indices ? 0 : 1;
                                tested++;
                                rows.AppendLine($"{visual.Index},{AssetDatabase.GetAssetPath(actual)},{actual.vertexCount},{actual.GetIndexCount(0)},{vertices},{indices}");
                            }
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(expected);
                    }
                }
            }

            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "GeometryComparison.csv"), rows.ToString());
            return $"visuals={tested}; mismatches={failed}; position/normal/tangent/color/UV/hemi/index buffers compared byte for byte";
        }

        private static bool BytesEqual(Unity.Collections.NativeArray<byte> a, Unity.Collections.NativeArray<byte> b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string Property(Material material, string property)
        {
            return material.HasProperty(property) ? material.GetFloat(property).ToString(CultureInfo.InvariantCulture) : "pass";
        }
    }
}
