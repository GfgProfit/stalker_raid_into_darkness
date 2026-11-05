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
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class XRayFloraLevelFixture : IDisposable
    {
        public const string PARENT = "Assets/__XRayFloraImportTests_tmp";

        private static readonly Dictionary<bool, SourceData> SOURCES = new Dictionary<bool, SourceData>();

        private readonly TestDirectory _directory;
        private readonly bool _createdParent;
        private readonly bool _trueStalker;
        private readonly Mo2Fixture _mo2;
        private readonly SourceData _source;
        private readonly bool _ownsHost;
        private bool _detailsAttached;

        public XRayFloraLevelFixture(bool trueStalker, string sourceRoot = null)
        {
            _directory = new TestDirectory(sourceRoot);
            _trueStalker = trueStalker;
            Root = PARENT + "/" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Assert.IsFalse(Directory.Exists(Root) || File.Exists(Root + ".meta"));
            _createdParent = !Directory.Exists(PARENT) && !File.Exists(PARENT + ".meta");
            LevelName = "flora_acceptance";
            _source = Source(trueStalker);
            string output = Output();
            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "Case.txt"), TestContext.CurrentContext.Test.FullName);
                File.WriteAllLines(Path.Combine(output, "OriginalSources.csv"), _source.Provenance);
                File.WriteAllText(Path.Combine(output, "Input.txt"), _source.Identity + "\nShader=" + _source.Shader + "\nVisual=" + _source.Visual + "\n" + _source.GeometryRange);
            }
            if (!trueStalker)
            {
                _mo2 = new Mo2Fixture(_directory);
                _mo2.WriteIni("FloraAcceptance");
                _mo2.AddProfile("FloraAcceptance", "+Flora");
            }
            foreach (KeyValuePair<string, byte[]> file in _source.Files) Write(file.Key, file.Value);
            Write("levels/" + LevelName + "/level", _source.Level);
            Write("levels/" + LevelName + "/level.geom", _source.Geometry);
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Assert.IsTrue(Application.isBatchMode, "Save the active untitled scene before running flora import acceptance in a live Editor.");
                XRayLevelPaths.EnsureFolder(Root);
                Scene host = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Assert.IsTrue(EditorSceneManager.SaveScene(host, Root + "/AcceptanceHost.unity"));
                _ownsHost = true;
            }
        }

        public string Root { get; }
        public string LevelName { get; }
        public string SourceShader => _source.Shader;
        public string AlbedoPath => _source.AlbedoPath;
        public string CorrectionPath => _source.CorrectionPath;
        public bool PartialMipChain => _source.PartialMipChain;
        public string InputRoot => _trueStalker ? _directory.Combine("source") : _directory.Root;

        public XRaySourcePlan Plan()
        {
            if (_trueStalker) return XRayLevelGamedata.CreatePlan(XRayLevelGamedata.Inspect(InputRoot), null, false);
            XRaySourceConfiguration configuration = _mo2.CreateConfiguration("FloraAcceptance");
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "textures", "textures.ltx", "levels", "shaders", "shaders.xr", "configs" });
            if (_detailsAttached) configuration.IndexScope.Add("gamemtl.xr");
            return Mo2VirtualFileSystem.Plan(configuration);
        }

        public XRayLevelImportRequest Request(XRayFloraImportMode mode)
        {
            return Request(mode, XRayFloraWindImportMode.Disabled);
        }

        public XRayLevelImportRequest Request(XRayFloraImportMode mode, XRayFloraWindImportMode wind)
        {
            var settings = new XRayLevelImportSettings { ImportCollision = false, ImportDetails = false, ImportLights = false, OpenSceneAfterImport = false, CopySourceFiles = true, FloraImportMode = mode, FloraWindMode = wind };
            return new XRayLevelImportRequest { LevelName = LevelName, Settings = settings, SourcePlan = Plan(), OutputRoot = Root, Identification = "Isolated full-job flora acceptance" };
        }

        public void RemoveCorrection()
        {
            Assert.IsNotNull(_source.CorrectionPath);
            string file = _trueStalker ? Path.Combine(InputRoot, _source.CorrectionPath) : _directory.Combine("MO2/mods/Flora/gamedata/" + _source.CorrectionPath);
            Assert.IsTrue(File.Exists(file));
            File.Delete(file);
        }

        public void ReplaceFloraShader()
        {
            Write(_trueStalker ? "shaders/r3/deffer_base_aref_bump-hq.ps" : "shaders/r3/deffer_tree_branch_aref_bump-hq.ps", Encoding.ASCII.GetBytes("unverified flora shader"));
        }

        public Material Material()
        {
            var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(Root + "/" + LevelName + "/Meshes/" + LevelName + "_StaticProps.asset");
            Assert.IsNotNull(set, "Tree instances must be saved in the static props batch set.");
            Assert.Greater(set.Batches.Count, 0);
            Material material = set.Batches[0].Materials[0];
            Assert.IsNotNull(material);
            Assert.AreEqual(MonolithMaterialConfiguratorName, material.shader.name);
            return material;
        }

        public XRayInstanceBatchSet Batches()
        {
            return AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(Root + "/" + LevelName + "/Meshes/" + LevelName + "_StaticProps.asset");
        }

        public static string MonolithMaterialConfiguratorName => XRay.Importer.Editor.MonolithMaterialConfigurator.SHADER_NAME;

        public string Output()
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE06B1_OUTPUT");
            if (string.IsNullOrEmpty(output)) return null;
            Assert.IsTrue(Path.GetFullPath(output).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase));
            return Path.Combine(output, "FullImport", Root.Substring(Root.LastIndexOf('/') + 1));
        }

        public void Dispose()
        {
            Assert.IsNull(XRayLevelImportJob.Current, "An import job must finish before fixture cleanup.");
            string output = Output();
            if (output != null && Directory.Exists(Root))
            {
                var assets = new List<string> { "Asset,SHA256,GUID" };
                foreach (string file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) assets.Add(Csv(file) + "," + XRayLevelSourceRefresh.HashFile(file) + "," + AssetDatabase.AssetPathToGUID(file));
                File.WriteAllLines(Path.Combine(output, "CreatedAssets.csv"), assets);
            }
            if (_ownsHost && SceneManager.GetActiveScene().path == Root + "/AcceptanceHost.unity") EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (Directory.Exists(Root)) Assert.IsTrue(AssetDatabase.DeleteAsset(Root));
            Assert.IsFalse(Directory.Exists(Root) || File.Exists(Root + ".meta"));
            if (_createdParent && Directory.Exists(PARENT) && Directory.GetFileSystemEntries(PARENT).Length == 0) AssetDatabase.DeleteAsset(PARENT);
            if (output != null) File.WriteAllText(Path.Combine(output, "Cleanup.txt"), "AssetDatabase.DeleteAsset completed; root and root.meta absent: " + Path.GetFullPath(Root));
            _directory.Dispose();
        }

        private void Write(string file, byte[] bytes)
        {
            if (_trueStalker) _directory.WriteFile("source/" + file, bytes);
            else _mo2.AddModFile("Flora", file, bytes);
        }

        public void AttachDetails(XRayEffectiveGamedata source)
        {
            _detailsAttached = true;
            string level = _trueStalker ? "escape" : "l01_escape";
            byte[] bytes = source.ReadAllBytes("levels/" + level + "/level.details");
            Write("levels/" + LevelName + "/level.details", bytes);
            Write("levels/" + LevelName + "/level.cform", source.ReadAllBytes("levels/" + level + "/level.cform"));
            Write("gamemtl.xr", source.ReadAllBytes("gamemtl.xr"));
            var resolver = new XRayTextureResolver(source.Index) { LevelFolder = "levels/" + level };
            foreach (XRayDetailModel model in XRayDetails.Read(bytes, "Real detail fixture").Models)
            {
                foreach (string texture in new[] { model.Texture, model.Texture + "_bump", model.Texture + "_bump#", "levels/" + level + "/" + model.Texture + "_bump" })
                {
                    XRayResolvedFile file = resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(texture, ".dds"));
                    if (!file.IsResolved) continue;
                    string target = file.VirtualPath.Replace("levels/" + level + "/", "levels/" + LevelName + "/");
                    Write(target, file.Winner.ReadAllBytes());
                }
            }

            foreach (string file in _trueStalker ? XRayFloraSourceBinding.TrueStalkerDetailFiles : XRayFloraSourceBinding.AnomalyDetailFiles) Write(file, source.ReadAllBytes(file));
            foreach (string file in new[] { "textures/ts_flora/build_details.dds", "textures/ts_flora/build_details_bump.dds", "textures/ts_flora/build_details_bump#.dds", XRayFloraSourceBinding.ANOMALY_WIND_WAVES })
            {
                if (source.Exists(file)) Write(file, source.ReadAllBytes(file));
            }
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
            XRayLevelData level = XRayLevelReader.Read(effective.ReadAllBytes(resolver.LevelFolder + "/level"), "Actual flora source");
            XRayShaderLibrary shaders = XRayShaderLibrary.Read(effective.ReadAllBytes("shaders.xr"), "Actual shaders.xr");
            var result = new SourceData();
            using (XRayLevelGeometry geometry = XRayLevelGeometry.Open(effective.Materialize(resolver.LevelFolder + "/level.geom")))
            {
                XRayLevelVisual selected = null;
                XRayTextureResolution selectedTextures = null;
                int selectedScore = -1;
                foreach (XRayLevelVisual visual in level.Visuals)
                {
                    if (visual.ModelType != XRayLevelFormat.MT_TREE_ST || !visual.HasGeometry || visual.Geometry.VertexCount > 8192) continue;
                    XRayLevelShader candidate = level.Shaders[visual.ShaderId];
                    shaders.TryGet(candidate.EngineShader, out XRayBlender blender);
                    if (!XRayLevelSurfaceRules.Classify(candidate, blender, true).Flora) continue;
                    XRayTextureResolution textures = resolver.Resolve(candidate.Texture);
                    if (!textures.Diffuse.IsResolved || textures.Bump == null || !textures.Bump.IsResolved || !XRayLevelMaterialLibrary.UsesFloraNormal(textures.Description)) continue;
                    if (trueStalker && (textures.BumpCorrection == null || !textures.BumpCorrection.IsResolved)) continue;
                    DdsHeader header = DdsHeader.Parse(textures.Diffuse.Winner.ReadAllBytes());
                    int score = !trueStalker && header.MipCount > 1 && header.MipCount < FullMips(header) ? 2 : 1;
                    if (score <= selectedScore) continue;
                    selected = visual;
                    selectedTextures = textures;
                    selectedScore = score;
                    if (trueStalker || score == 2) break;
                }
                Assert.IsNotNull(selected, "A real flora tree visual with bump" + (trueStalker ? " and bump#" : string.Empty) + " is required.");
                XRayLevelShader shader = level.Shaders[selected.ShaderId];
                result.Identity = effective.Resolve(resolver.LevelFolder + "/level").Location + " visual=" + selected.Index;
                result.Visual = selected.Index;
                result.Shader = shader.EngineShader;
                result.PartialMipChain = selectedScore == 2;
                result.Level = Level(level, shader, selected);
                result.Geometry = Geometry(geometry, selected, effective.Materialize(resolver.LevelFolder + "/level.geom"));
                result.GeometryRange = "VertexBuffer=" + selected.Geometry.VertexBuffer + ";VertexOffset=" + selected.Geometry.VertexOffset + ";VertexCount=" + selected.Geometry.VertexCount + ";IndexBuffer=" + selected.Geometry.IndexBuffer + ";IndexOffset=" + selected.Geometry.IndexOffset + ";IndexCount=" + selected.Geometry.IndexCount + ";ShaderRaw=" + shader.Raw + ";PartialMipChain=" + result.PartialMipChain;
                result.Provenance.Add(Csv(resolver.LevelFolder + "/level") + "," + Csv(effective.Resolve(resolver.LevelFolder + "/level").Location) + "," + Hash(effective.ReadAllBytes(resolver.LevelFolder + "/level")) + "," + Csv("levels/flora_acceptance/level") + "," + Hash(result.Level) + ",ReducedContainer");
                result.Provenance.Add(Csv(resolver.LevelFolder + "/level.geom") + "," + Csv(effective.Resolve(resolver.LevelFolder + "/level.geom").Location) + "," + XRayLevelSourceRefresh.HashFile(effective.Materialize(resolver.LevelFolder + "/level.geom")) + "," + Csv("levels/flora_acceptance/level.geom") + "," + Hash(result.Geometry) + ",ExactVertexAndIndexRanges");
                result.Files["shaders.xr"] = effective.ReadAllBytes("shaders.xr");
                if (effective.Exists("textures.ltx")) result.Files["textures.ltx"] = effective.ReadAllBytes("textures.ltx");
                Add(result, selectedTextures.Diffuse);
                Add(result, resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(shader.Texture, ".thm")));
                Add(result, selectedTextures.Bump);
                Add(result, selectedTextures.BumpCorrection);
                result.AlbedoPath = selectedTextures.Diffuse.VirtualPath;
                result.CorrectionPath = selectedTextures.BumpCorrection != null && selectedTextures.BumpCorrection.IsResolved ? selectedTextures.BumpCorrection.VirtualPath : null;
            }
            var contractFiles = new List<string>(trueStalker ? XRayFloraSourceBinding.TrueStalkerFiles : XRayFloraSourceBinding.AnomalyFiles);
            contractFiles.AddRange(trueStalker ? XRayFloraSourceBinding.TrueStalkerWindFiles : XRayFloraSourceBinding.AnomalyWindFiles);
            foreach (string file in contractFiles)
            {
                if (result.Files.ContainsKey(file)) continue;
                if (!effective.Exists(file)) continue;
                result.Files[file] = effective.ReadAllBytes(file);
                result.Provenance.Add(Csv(file) + "," + Csv(effective.Resolve(file).Location) + "," + Hash(result.Files[file]) + "," + Csv(file) + "," + Hash(result.Files[file]) + ",ExactSourceBytes");
            }
            SOURCES[trueStalker] = result;
            return result;
        }

        private static int FullMips(DdsHeader header)
        {
            int size = Math.Max(header.Width, header.Height);
            int count = 1;
            while (size > 1)
            {
                size >>= 1;
                count++;
            }
            return count;
        }

        private static void Add(SourceData data, XRayResolvedFile file)
        {
            if (file == null || !file.IsResolved) return;
            data.Files[file.VirtualPath] = file.Winner.ReadAllBytes();
            data.Provenance.Add(Csv(file.VirtualPath) + "," + Csv(file.Winner.Location) + "," + Hash(data.Files[file.VirtualPath]) + "," + Csv(file.VirtualPath) + "," + Hash(data.Files[file.VirtualPath]) + ",ExactSourceBytes");
        }

        private static byte[] Level(XRayLevelData level, XRayLevelShader shader, XRayLevelVisual visual)
        {
            byte[] header = Payload(writer => { writer.Write(level.Version); writer.Write(level.Quality); });
            byte[] table = Payload(writer => { writer.Write(1u); writer.Write(Encoding.ASCII.GetBytes(shader.Raw + "\0")); });
            byte[] visualHeader = Payload(writer =>
            {
                writer.Write(XRayLevelFormat.OGF_VERSION_4);
                writer.Write(XRayLevelFormat.MT_TREE_ST);
                writer.Write((ushort)0);
                foreach (float value in visual.BoundsMin) writer.Write(value);
                foreach (float value in visual.BoundsMax) writer.Write(value);
                foreach (float value in visual.SphereCenter) writer.Write(value);
                writer.Write(visual.SphereRadius);
            });
            byte[] ranges = Payload(writer => { writer.Write(0); writer.Write(0); writer.Write(visual.Geometry.VertexCount); writer.Write(0); writer.Write(0); writer.Write(visual.Geometry.IndexCount); });
            XRayTreeDefinition tree = visual.Tree;
            byte[] definition = Payload(writer =>
            {
                foreach (float value in tree.Matrix) writer.Write(value);
                foreach (float value in tree.ScaleRgb) writer.Write(value);
                writer.Write(tree.ScaleHemi);
                writer.Write(tree.ScaleSun);
                foreach (float value in tree.BiasRgb) writer.Write(value);
                writer.Write(tree.BiasHemi);
                writer.Write(tree.BiasSun);
            });
            byte[] shape = Pack((XRayLevelFormat.OGF_CHUNK_HEADER, visualHeader), (XRayLevelFormat.OGF_CHUNK_GCONTAINER, ranges), (XRayLevelFormat.OGF_CHUNK_TREEDEF2, definition));
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
            public bool PartialMipChain { get; set; }
            public string AlbedoPath { get; set; }
            public string CorrectionPath { get; set; }
            public byte[] Level { get; set; }
            public byte[] Geometry { get; set; }
            public string GeometryRange { get; set; }
            public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();
            public List<string> Provenance { get; } = new List<string> { "OriginalVirtualPath,OriginalWinner,OriginalSHA256,FixtureVirtualPath,FixtureSHA256,Method" };
        }
    }
}
