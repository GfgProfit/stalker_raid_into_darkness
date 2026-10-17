using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    [Category("RealData")]
    public sealed class RealSourceTests
    {
        private const string AK74_MODEL = "meshes\\anomaly_weapons\\wpn_ak74\\wpn_ak74_hud.ogf";
        private const string AK74_MOTIONS = "meshes\\anomaly_weapons\\hud_animation\\wpn_ak74_hud_animation.omf";
        private const string AK74_TEXTURE = "wpn\\wpn_ak74\\wpn_ak74";

        [Test]
        public void RealMo2ProfileIsIndexedReadOnly()
        {
            XRaySourceConfiguration configuration = CreateRealConfiguration();
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.Errors));
            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            XRayTextureResolution resolution = new XRayTextureResolver(index).Resolve(AK74_TEXTURE);
            var log = new StringBuilder();
            log.AppendLine($"[X-Ray real data] Profile '{plan.ProfileName}', MO2 instance '{plan.Installation.InstanceFolder}' (portable: {plan.Installation.IsPortable})");
            log.AppendLine($"Active mods: {index.Statistics.ActiveModCount}, sources: {index.Statistics.SourceCount}, contributing: {index.Statistics.ContributingSourceCount}, files: {index.Statistics.FileCount}, conflicts: {index.Statistics.ConflictCount}, ambiguous: {index.Statistics.AmbiguousCount}, archive sources: {index.Statistics.ArchiveSourceCount}, build {index.Statistics.BuildMilliseconds} ms");
            AppendFile(log, "Diffuse", resolution.Diffuse);
            AppendFile(log, "Bump", resolution.Bump);
            AppendFile(log, "Bump#", resolution.BumpCorrection);
            log.AppendLine($"Description: {resolution.DescriptionOrigin}");
            int printed = 0;
            foreach (XRayIndexedFile file in index.Files)
            {
                if (file.HasConflict && file.VirtualPath.EndsWith(".dds", StringComparison.Ordinal) && index.GetSource(file.Winner).Kind == XRayAssetSourceKind.Mod && printed < 5)
                {
                    printed++;
                    log.Append($"Conflict {file.VirtualPath}: winner {index.GetSource(file.Winner).Name} ({index.GetSource(file.Winner).DisplayPriority})");
                    for (int i = 1; i < file.Candidates.Count; i++)
                    {
                        log.Append($", loser {index.GetSource(file.Candidates[i]).Name} ({index.GetSource(file.Candidates[i]).DisplayPriority})");
                    }

                    log.AppendLine();
                }
            }

            Debug.Log(log.ToString());
            Assert.IsTrue(resolution.Diffuse.IsResolved);
            for (int i = 1; i < plan.Sources.Count; i++)
            {
                if (plan.Sources[i].Kind == XRayAssetSourceKind.Mod && plan.Sources[i - 1].Kind == XRayAssetSourceKind.Mod)
                {
                    Assert.Greater(plan.Sources[i - 1].Priority, plan.Sources[i].Priority);
                }
            }
        }

        [Test]
        public void RealAk74ModelIsImportedWithTexturesMaterialsAndMotions()
        {
            XRaySourceConfiguration configuration = CreateRealConfiguration();
            string modelSource = Path.Combine(configuration.BaseAnomalyFolder, AK74_MODEL);
            string motionSource = Path.Combine(configuration.BaseAnomalyFolder, AK74_MOTIONS);
            if (!File.Exists(modelSource) || !File.Exists(motionSource))
            {
                Assert.Ignore("Anomaly AK-74 HUD model or motions are not available.");
            }

            using (var scope = new XRayImportTestScope(configuration))
            {
                string model = scope.ImportModel("wpn_ak74_hud.ogf", File.ReadAllBytes(modelSource));
                string motions = scope.ImportFile("wpn_ak74_hud_animation.omf", File.ReadAllBytes(motionSource));
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(model);
                var info = root.GetComponent<XRayModel>();
                var library = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(motions);
                XRayResourceReport report = scope.LoadReport(model);
                Assert.IsNotNull(report);
                var log = new StringBuilder();
                log.AppendLine($"[X-Ray real data] {model}: bones {info.BoneNames.Count}, meshes {info.MeshCount}, materials {report.Materials.Count}, resolved textures {report.CountResolvedTextures()}, missing {report.CountTextures(XRayResourceStatus.Missing)}, motions {library.Motions.Count}");
                foreach (XRayReportMaterial material in report.Materials)
                {
                    log.AppendLine($"[{material.Index:00}] {material.Texture} | {material.Shader} -> {material.MaterialPath} ({material.MaterialStatus}, rule {material.SurfaceRule})");
                    foreach (XRayReportTexture texture in material.Textures)
                    {
                        log.AppendLine($"     {texture.Role}: {texture.Requested} -> {texture.Status} {texture.AssetPath} [{texture.Format}] source {texture.Winner?.Name} ({texture.Winner?.Priority}), losers {texture.Losers.Count} {texture.Note}");
                    }
                }

                Debug.Log(log.ToString());
                Assert.IsTrue(report.IsComplete);
                Assert.Greater(info.BoneNames.Count, 0);
                Assert.Greater(report.CountResolvedTextures(), 0);
                int clips = CountClips(library);
                Assert.Greater(clips, 0);
                string[] assets = scope.FindAssets(string.Empty, scope.Folder);
                scope.Reimport(model);
                scope.Reimport(motions);
                CollectionAssert.AreEqual(assets, scope.FindAssets(string.Empty, scope.Folder));
                Assert.AreEqual(clips, CountClips(AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(motions)));
                Assert.AreEqual(info.BoneNames.Count, AssetDatabase.LoadAssetAtPath<GameObject>(model).GetComponent<XRayModel>().BoneNames.Count);
            }
        }

        private static int CountClips(XRayMotionLibrary library)
        {
            int clips = 0;
            foreach (XRayMotionEntry entry in library.Motions)
            {
                if (entry.Clip != null)
                {
                    clips++;
                }
            }

            return clips;
        }

        private static XRaySourceConfiguration CreateRealConfiguration()
        {
            if (!Directory.Exists(XRayAssetSourceUserSettings.DEFAULT_BASE_ANOMALY_FOLDER) || !Directory.Exists(XRayAssetSourceUserSettings.DEFAULT_MO2_FOLDER))
            {
                Assert.Ignore("Real Anomaly / MO2 folders are not available on this machine.");
            }

            return new XRaySourceConfiguration { Mode = XRaySourceMode.Mo2AndBaseAnomaly, BaseAnomalyFolder = XRayAssetSourceUserSettings.DEFAULT_BASE_ANOMALY_FOLDER, Mo2Folder = XRayAssetSourceUserSettings.DEFAULT_MO2_FOLDER, LocalAppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
        }

        private static void AppendFile(StringBuilder log, string role, XRayResolvedFile file)
        {
            if (file == null)
            {
                log.AppendLine($"{role}: not declared");
                return;
            }

            log.Append($"{role}: {file.DisplayPath} -> {file.Status}");
            if (file.IsResolved)
            {
                log.Append($" from {file.Winner.SourceName} ({file.Winner.Priority}) {file.Winner.FullPath}");
                foreach (XRayResolvedSource loser in file.Losers)
                {
                    log.Append($"; lost: {loser.SourceName} ({loser.Priority})");
                }
            }

            log.AppendLine();
        }
    }
}
