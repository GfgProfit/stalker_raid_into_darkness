using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class XRayTerrainLevelFixture : IDisposable
    {
        private const string PARENT = "Assets/__XRayLevelImportTests_tmp";

        private static readonly Dictionary<bool, SourceData> SOURCES = new Dictionary<bool, SourceData>();

        private readonly TestDirectory _directory = new TestDirectory();
        private readonly bool _createdParent;
        private readonly bool _trueStalker;
        private readonly Mo2Fixture _mo2;
        private readonly SourceData _source;
        private readonly bool _ownsHost;
        private int _imports;

        public XRayTerrainLevelFixture(bool trueStalker)
        {
            _trueStalker = trueStalker;
            Root = PARENT + "/" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Assert.IsFalse(Directory.Exists(Root) || File.Exists(Root + ".meta"));
            _createdParent = !Directory.Exists(PARENT) && !File.Exists(PARENT + ".meta");
            LevelName = "terrain_acceptance";
            _source = Source(trueStalker);
            string output = Output();
            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "Case.txt"), TestContext.CurrentContext.Test.FullName);
                File.WriteAllLines(Path.Combine(output, "OriginalSources.csv"), _source.Provenance);
                File.WriteAllText(Path.Combine(output, "Input.txt"), SourceIdentity + "\nShader=" + SourceShader + "\nVisual=" + SourceVisual + "\n" + _source.GeometryRange);
            }
            if (!trueStalker)
            {
                _mo2 = new Mo2Fixture(_directory);
                _mo2.WriteIni("TerrainAcceptance");
                _mo2.AddProfile("TerrainAcceptance", "+Terrain");
            }
            foreach (KeyValuePair<string, byte[]> file in _source.Files) Write(file.Key, file.Value);
            Write("levels/" + LevelName + "/level", _source.Level);
            Write("levels/" + LevelName + "/level.geom", _source.Geometry);
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Assert.IsTrue(Application.isBatchMode, "Save the active untitled scene before running full import acceptance in a live Editor.");
                XRayLevelPaths.EnsureFolder(Root);
                Scene host = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Assert.IsTrue(EditorSceneManager.SaveScene(host, Root + "/AcceptanceHost.unity"));
                _ownsHost = true;
            }
        }

        public string Root { get; }
        public string LevelName { get; }
        public string SourceIdentity => _source.Identity;
        public string SourceShader => _source.Shader;
        public int SourceVisual => _source.Visual;
        public string HeightPath => _source.HeightPath;
        public string BumpPath => _source.BumpPath;
        public string InputRoot => _trueStalker ? _directory.Combine("source") : _directory.Root;

        public XRaySourcePlan Plan()
        {
            if (_trueStalker) return XRayLevelGamedata.CreatePlan(XRayLevelGamedata.Inspect(InputRoot), null, false);
            XRaySourceConfiguration configuration = _mo2.CreateConfiguration("TerrainAcceptance");
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "textures", "textures.ltx", "levels", "shaders", "shaders.xr", "configs" });
            return Mo2VirtualFileSystem.Plan(configuration);
        }

        public XRayLevelImportRequest Request(XRayTerrainImportMode mode, XRayTerrainQuality quality)
        {
            var settings = new XRayLevelImportSettings { ImportCollision = false, ImportDetails = false, ImportLights = false, OpenSceneAfterImport = false, CopySourceFiles = true, TerrainImportMode = mode, TerrainQuality = quality };
            return new XRayLevelImportRequest { LevelName = LevelName, Settings = settings, SourcePlan = Plan(), OutputRoot = Root, Identification = "Isolated full-job terrain acceptance" };
        }

        public void RemoveHeight()
        {
            string file = _trueStalker ? Path.Combine(InputRoot, HeightPath) : _directory.Combine("MO2/mods/Terrain/gamedata/" + HeightPath);
            if (File.Exists(file)) File.Delete(file);
        }

        public void ReplaceWinner(bool shader)
        {
            string file = shader ? "shaders/r3/lmodel.h" : BumpPath;
            byte[] bytes = shader ? Encoding.ASCII.GetBytes("unverified terrain BRDF") : XRayTestBinaries.Bc3Solid(4, 4, 60, 190, 160, 90);
            if (_trueStalker) Write(file, bytes);
            else _mo2.AddOverwriteFile(file, bytes);
        }

        public void RemoveLayerMetadata()
        {
            Write("shaders.xr", _source.PartialShaders);
        }

        private void Write(string file, byte[] bytes)
        {
            if (_trueStalker) _directory.WriteFile("source/" + file, bytes);
            else _mo2.AddModFile("Terrain", file, bytes);
        }

        public void Dispose()
        {
            Assert.IsNull(XRayLevelImportJob.Current, "An import job must finish before fixture cleanup.");
            string output = Output();
            if (output != null && Directory.Exists(Root))
            {
                var assets = new List<string> { "Asset,SHA256,GUID" };
                foreach (string file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) assets.Add(Csv(file) + "," + XRayLevelSourceRefresh.HashFile(file) + "," + AssetDatabase.AssetPathToGUID(file));
                if (File.Exists(Root + ".meta")) assets.Add(Csv(Root + ".meta") + "," + XRayLevelSourceRefresh.HashFile(Root + ".meta") + "," + AssetDatabase.AssetPathToGUID(Root));
                File.WriteAllLines(Path.Combine(output, "CreatedAssets.csv"), assets);
            }
            if (_ownsHost && SceneManager.GetActiveScene().path == Root + "/AcceptanceHost.unity") EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (Directory.Exists(Root)) Assert.IsTrue(AssetDatabase.DeleteAsset(Root));
            Assert.IsFalse(Directory.Exists(Root) || File.Exists(Root + ".meta"));
            if (_createdParent && Directory.Exists(PARENT) && Directory.GetFileSystemEntries(PARENT).Length == 0) AssetDatabase.DeleteAsset(PARENT);
            if (output != null) File.WriteAllText(Path.Combine(output, "Cleanup.txt"), "AssetDatabase.DeleteAsset completed; root and root.meta absent: " + Path.GetFullPath(Root));
            _directory.Dispose();
        }

        public void SaveEvidence()
        {
            string output = Output();
            if (output == null) return;
            string prefix = Path.Combine(output, "Import" + (++_imports));
            string savedAssets = prefix + "_Assets";
            foreach (string asset in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (!(asset.EndsWith(".mat", StringComparison.Ordinal) || asset.EndsWith(".prefab", StringComparison.Ordinal) || asset.EndsWith(".meta", StringComparison.Ordinal) || asset.EndsWith("LevelImportManifest.asset", StringComparison.Ordinal))) continue;
                string destination = Path.Combine(savedAssets, asset.Substring(Root.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(asset, destination);
            }
            File.WriteAllText(prefix + "_Manifest.json", XRayLevelImportJob.LastRecord.ToJson());
            Material material = MonolithTerrainFullImportTests.Material(this);
            File.WriteAllText(prefix + "_Material.json", EditorJsonUtility.ToJson(material, true));
            var rows = new List<string> { "Property,Asset,GUID,SHA256,Importer,TextureType,ImporterSRGB,DataSRGB,GraphicsFormat,Width,Height" };
            foreach (string property in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(property);
                if (texture == null) continue;
                string path = AssetDatabase.GetAssetPath(texture);
                AssetImporter assetImporter = AssetImporter.GetAtPath(path);
                var importer = assetImporter as TextureImporter;
                var image = texture as Texture2D;
                Assert.IsNotNull(image);
                if (property.StartsWith("_MonolithTerrainDetail", StringComparison.Ordinal)) Assert.IsTrue(image.isDataSRGB, property + ": " + image.graphicsFormat);
                else if (property.StartsWith("_MonolithTerrain", StringComparison.Ordinal)) Assert.IsFalse(image.isDataSRGB, property + ": " + image.graphicsFormat);
                rows.Add(Csv(property) + "," + Csv(path) + "," + AssetDatabase.AssetPathToGUID(path) + "," + XRayLevelSourceRefresh.HashFile(path) + "," + assetImporter.GetType().Name + "," + importer?.textureType + "," + importer?.sRGBTexture + "," + image.isDataSRGB + "," + image.graphicsFormat + "," + image.width + "," + image.height);
            }
            File.WriteAllLines(prefix + "_Bindings.csv", rows);
        }

        private string Output()
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE05C_OUTPUT");
            if (string.IsNullOrEmpty(output)) return null;
            Assert.IsTrue(Path.GetFullPath(output).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase));
            return Path.Combine(output, Root.Substring(Root.LastIndexOf('/') + 1));
        }

        private static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static SourceData Source(bool trueStalker)
        {
            if (SOURCES.TryGetValue(trueStalker, out SourceData cached)) return cached;
            XRaySourcePlan plan;
            if (trueStalker) plan = XRayLevelGamedata.CreatePlan(XRayLevelGamedata.Inspect("E:/True Stalker Unpacked Patch 1.5"), null, false);
            else
            {
                XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
                configuration.IndexScope.Clear();
                configuration.IndexScope.AddRange(new[] { "textures", "textures.ltx", "shaders", "shaders.xr", "levels/l01_escape" });
                plan = Mo2VirtualFileSystem.Plan(configuration);
            }
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
            var effective = new XRayEffectiveGamedata(XRayAssetIndex.Build(plan));
            var resolver = new XRayTextureResolver(effective.Index) { LevelFolder = "levels/" + (trueStalker ? "escape" : "l01_escape") };
            XRayLevelData level = XRayLevelReader.Read(effective.ReadAllBytes(resolver.LevelFolder + "/level"), "Actual terrain source");
            XRayShaderLibrary shaders = XRayShaderLibrary.Read(effective.ReadAllBytes("shaders.xr"), "Actual shaders.xr");
            var result = new SourceData();
            using (XRayLevelGeometry geometry = XRayLevelGeometry.Open(effective.Materialize(resolver.LevelFolder + "/level.geom")))
            {
                XRayLevelVisual selected = null;
                XRayBlender blender = null;
                foreach (XRayLevelVisual visual in level.Visuals)
                {
                    if (visual.ModelType != XRayLevelFormat.MT_NORMAL || !visual.HasGeometry || visual.Geometry.VertexCount > 4096) continue;
                    XRayLevelShader candidate = level.Shaders[visual.ShaderId];
                    if (!shaders.TryGet(candidate.EngineShader, out XRayBlender metadata) || !XRayTerrainSourceBinding.IsTerrainBlender(metadata)) continue;
                    XRayResolvedFile mask = resolver.ResolveTexture(candidate.Texture + "_mask", ".dds");
                    if (!mask.IsResolved || !Mixed(geometry, visual, mask)) continue;
                    selected = visual;
                    blender = metadata;
                    break;
                }
                Assert.IsNotNull(selected, "A real mixed-layer terrain visual is required.");
                XRayLevelShader shader = level.Shaders[selected.ShaderId];
                result.Identity = effective.Resolve(resolver.LevelFolder + "/level").Location + " visual=" + selected.Index;
                result.Visual = selected.Index;
                result.Shader = shader.EngineShader;
                result.Level = Level(level, shader, selected);
                result.Geometry = Geometry(geometry, selected, effective.Materialize(resolver.LevelFolder + "/level.geom"));
                result.GeometryRange = "VertexBuffer=" + selected.Geometry.VertexBuffer + ";VertexOffset=" + selected.Geometry.VertexOffset + ";VertexCount=" + selected.Geometry.VertexCount + ";IndexBuffer=" + selected.Geometry.IndexBuffer + ";IndexOffset=" + selected.Geometry.IndexOffset + ";IndexCount=" + selected.Geometry.IndexCount + ";ShaderRaw=" + shader.Raw;
                result.Provenance.Add(Csv(resolver.LevelFolder + "/level") + "," + Csv(effective.Resolve(resolver.LevelFolder + "/level").Location) + "," + Hash(effective.ReadAllBytes(resolver.LevelFolder + "/level")) + "," + Csv("levels/terrain_acceptance/level") + "," + Hash(result.Level) + ",ReducedContainer");
                result.Provenance.Add(Csv(resolver.LevelFolder + "/level.geom") + "," + Csv(effective.Resolve(resolver.LevelFolder + "/level.geom").Location) + "," + XRayLevelSourceRefresh.HashFile(effective.Materialize(resolver.LevelFolder + "/level.geom")) + "," + Csv("levels/terrain_acceptance/level.geom") + "," + Hash(result.Geometry) + ",ExactVertexAndIndexRanges");
                result.Files["shaders.xr"] = effective.ReadAllBytes("shaders.xr");
                if (effective.Exists("textures.ltx")) result.Files["textures.ltx"] = effective.ReadAllBytes("textures.ltx");
                result.PartialShaders = Partial(blender);
                Add(result, resolver.Resolve(shader.Texture).Diffuse, resolver.LevelFolder);
                Add(result, resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(shader.Texture, ".thm")), resolver.LevelFolder);
                Add(result, resolver.ResolveTexture(shader.Texture + "_mask", ".dds"), resolver.LevelFolder);
                XRayResolvedFile atlas = resolver.ResolveTexture(shader.Texture + "_lod_textures", ".dds");
                Add(result, atlas.IsResolved ? atlas : resolver.ResolveTexture("terrain/default_lod_textures", ".dds"), resolver.LevelFolder);
                for (int i = 0; i < 4; i++)
                {
                    string detail = blender.GetString("Detail map/R2-" + "RGBA"[i]);
                    Assert.IsNotEmpty(detail);
                    Add(result, resolver.Resolve(detail).Diffuse, resolver.LevelFolder);
                    Add(result, resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(detail, ".thm")), resolver.LevelFolder);
                    XRayResolvedFile bump = resolver.ResolveTexture(detail + "_bump", ".dds");
                    Add(result, bump, resolver.LevelFolder);
                    XRayResolvedFile height = resolver.ResolveTexture(detail + "_height", ".dds");
                    if (!trueStalker) Add(result, height, resolver.LevelFolder);
                    if (i == 0)
                    {
                        result.BumpPath = bump.VirtualPath;
                        result.HeightPath = height.VirtualPath;
                    }
                }
            }
            foreach (string file in trueStalker ? new[] { "shaders/r3/deffer_impl_flat.ps", "shaders/r3/lmodel.h" } : new[] { "shaders/r3/deffer_terrain_high_flat_d.ps", "shaders/r3/deffer_terrain_mid_flat.ps", "shaders/r3/deffer_terrain_low_flat.ps", "shaders/r3/lmodel.h" }) result.Files[file] = effective.ReadAllBytes(file);
            if (trueStalker)
            {
                foreach (string path in XRayTerrainSourceBinding.TrueStalkerFiles) result.Files[path] = effective.ReadAllBytes(path);
            }
            foreach (KeyValuePair<string, byte[]> file in result.Files)
            {
                if (!effective.Exists(file.Key)) continue;
                result.Provenance.Add(Csv(file.Key) + "," + Csv(effective.Resolve(file.Key).Location) + "," + Hash(file.Value) + "," + Csv(file.Key) + "," + Hash(file.Value) + ",ExactSourceBytes");
            }
            SOURCES[trueStalker] = result;
            return result;
        }

        private static bool Mixed(XRayLevelGeometry geometry, XRayLevelVisual visual, XRayResolvedFile mask)
        {
            byte[] source = mask.Winner.ReadAllBytes();
            DdsHeader header = DdsHeader.Parse(source);
            if (!DdsDecoder.CanDecode(header)) return false;
            byte[] pixels = DdsDecoder.DecodeTopLevel(source, header);
            XRayGeometryRange range = visual.Geometry;
            XRayLevelVertexBlock vertices = geometry.ReadVertices(range.VertexBuffer, range.VertexOffset, range.VertexCount);
            ushort[] indices = geometry.ReadIndices(range.IndexBuffer, range.IndexOffset, range.IndexCount);
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                float u = (vertices.Uv[indices[i] * 2] + vertices.Uv[indices[i + 1] * 2] + vertices.Uv[indices[i + 2] * 2]) / 3.0f;
                float v = (vertices.Uv[indices[i] * 2 + 1] + vertices.Uv[indices[i + 1] * 2 + 1] + vertices.Uv[indices[i + 2] * 2 + 1]) / 3.0f;
                int x = Math.Min(header.Width - 1, (int)((u - Math.Floor(u)) * header.Width));
                int y = Math.Min(header.Height - 1, (int)((v - Math.Floor(v)) * header.Height));
                int active = 0;
                for (int channel = 0; channel < 4; channel++) if (pixels[(y * header.Width + x) * 4 + channel] > 24) active++;
                if (active >= 3) return true;
            }
            return false;
        }

        private static void Add(SourceData data, XRayResolvedFile file, string levelFolder)
        {
            if (file == null || !file.IsResolved) return;
            string path = file.VirtualPath.StartsWith(levelFolder + "/", StringComparison.OrdinalIgnoreCase) ? "levels/terrain_acceptance/" + file.VirtualPath.Substring(levelFolder.Length + 1) : file.VirtualPath;
            data.Files[path] = file.Winner.ReadAllBytes();
            data.Provenance.Add(Csv(file.VirtualPath) + "," + Csv(file.Winner.Location) + "," + Hash(data.Files[path]) + "," + Csv(path) + "," + Hash(data.Files[path]) + ",ExactSourceBytes");
        }

        private static byte[] Level(XRayLevelData level, XRayLevelShader shader, XRayLevelVisual visual)
        {
            byte[] header = Payload(writer => { writer.Write(level.Version); writer.Write(level.Quality); });
            byte[] table = Payload(writer => { writer.Write(1u); writer.Write(Encoding.ASCII.GetBytes(shader.Raw + "\0")); });
            byte[] visualHeader = Payload(writer =>
            {
                writer.Write((byte)4);
                writer.Write((byte)0);
                writer.Write((ushort)0);
                foreach (float value in visual.BoundsMin) writer.Write(value);
                foreach (float value in visual.BoundsMax) writer.Write(value);
                foreach (float value in visual.SphereCenter) writer.Write(value);
                writer.Write(visual.SphereRadius);
            });
            byte[] ranges = Payload(writer => { writer.Write(0); writer.Write(0); writer.Write(visual.Geometry.VertexCount); writer.Write(0); writer.Write(0); writer.Write(visual.Geometry.IndexCount); });
            byte[] shape = Pack((1u, visualHeader), (21u, ranges));
            byte[] sectors = Pack((0u, Pack((2u, BitConverter.GetBytes(0)))));
            return Pack((1u, header), (2u, table), (3u, Pack((0u, shape))), (8u, sectors));
        }

        private static byte[] Geometry(XRayLevelGeometry geometry, XRayLevelVisual visual, string path)
        {
            XRayGeometryRange range = visual.Geometry;
            XRayVertexBufferInfo info = geometry.VertexBuffers[range.VertexBuffer];
            var vertices = new byte[range.VertexCount * info.Stride];
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                reader.BaseStream.Position = info.DataOffset + (long)range.VertexOffset * info.Stride;
                Assert.AreEqual(vertices.Length, reader.Read(vertices, 0, vertices.Length));
            }
            byte[] vb = Payload(writer =>
            {
                writer.Write(1u);
                foreach (XRayVertexElement element in info.Elements)
                {
                    writer.Write((ushort)0);
                    writer.Write((ushort)element.Offset);
                    writer.Write(element.Type);
                    writer.Write(element.Method);
                    writer.Write(element.Usage);
                    writer.Write(element.UsageIndex);
                }
                writer.Write((ushort)255);
                writer.Write((ushort)0);
                writer.Write((byte)17);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((uint)range.VertexCount);
                writer.Write(vertices);
            });
            byte[] ib = Payload(writer =>
            {
                writer.Write(1u);
                writer.Write((uint)range.IndexCount);
                foreach (ushort index in geometry.ReadIndices(range.IndexBuffer, range.IndexOffset, range.IndexCount)) writer.Write(index);
            });
            return Pack((1u, BitConverter.GetBytes(geometry.Version)), (9u, vb), (10u, ib));
        }

        private static byte[] Partial(XRayBlender blender)
        {
            var header = new byte[176];
            byte[] id = Encoding.ASCII.GetBytes(blender.ClassId.PadRight(8));
            Array.Reverse(id);
            Buffer.BlockCopy(id, 0, header, 0, 8);
            byte[] name = Encoding.ASCII.GetBytes(blender.Name);
            Buffer.BlockCopy(name, 0, header, 8, name.Length);
            byte[] properties = Payload(writer =>
            {
                writer.Write(header);
                writer.Write(0u);
                writer.Write(Encoding.ASCII.GetBytes("Detail map\0"));
                for (int i = 0; i < 3; i++)
                {
                    writer.Write(3u);
                    writer.Write(Encoding.ASCII.GetBytes("R2-" + "RGBA"[i] + "\0"));
                    writer.Write(Encoding.ASCII.GetBytes(blender.GetString("Detail map/R2-" + "RGBA"[i]) + "\0"));
                }
            });
            return Pack((2u, Pack((0u, properties))));
        }

        private static byte[] Pack(params (uint Id, byte[] Bytes)[] chunks)
        {
            return Payload(writer =>
            {
                foreach ((uint id, byte[] bytes) in chunks)
                {
                    writer.Write(id);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                }
            });
        }

        private static byte[] Payload(Action<BinaryWriter> action)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                action(writer);
                return stream.ToArray();
            }
        }

        private sealed class SourceData
        {
            public string Identity { get; set; }
            public string Shader { get; set; }
            public int Visual { get; set; }
            public byte[] Level { get; set; }
            public byte[] Geometry { get; set; }
            public byte[] PartialShaders { get; set; }
            public string HeightPath { get; set; }
            public string BumpPath { get; set; }
            public string GeometryRange { get; set; }
            public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();
            public List<string> Provenance { get; } = new List<string> { "OriginalVirtualPath,OriginalWinner,OriginalSHA256,FixtureVirtualPath,FixtureSHA256,Method" };
        }
    }
}
