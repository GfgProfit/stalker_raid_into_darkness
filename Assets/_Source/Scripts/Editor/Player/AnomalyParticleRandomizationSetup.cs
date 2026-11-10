using System;
using System.Collections.Generic;
using System.IO;
using RaidIntoDarkness.Effects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RaidIntoDarkness.Player.Editor
{
    [Serializable]
    public sealed class AnomalyParticleSourceActionData
    {
        public float ratePerSecond;
    }

    [Serializable]
    public sealed class AnomalyParticleUnityBinding
    {
        public string path;
        public bool driveEmission;
        public bool eventControlled;
        public bool canonicalDirectional;
        public bool duplicateDirectional;
    }

    [Serializable]
    public sealed class AnomalyParticleEffectSourceData
    {
        public string sourceEffect;
        public string basename;
        public string[] unityAliases;
        public AnomalyParticleUnityBinding[] unityBindings;
        public int nativeMaxParticles;
        public bool framed;
        public bool animated;
        public bool randomStartFrame;
        public bool randomPlayback;
        public int frameCount;
        public int frameColumns;
        public float frameSizeX;
        public float frameSizeY;
        public float fps;
        public bool finiteEmitter;
        public float emitterTimeLimit;
        public AnomalyParticleSourceActionData[] sourceActions;
    }

    [Serializable]
    public sealed class AnomalyParticleFamilySourceData
    {
        public string family;
        public string prefab;
        public AnomalyParticleEffectSourceData[] effects;
    }

    [Serializable]
    public sealed class AnomalyParticleSourceManifest
    {
        public int schemaVersion;
        public AnomalyParticleFamilySourceData[] families;
    }

    [Serializable]
    public sealed class AnomalyParticleRandomizationFamilyReport
    {
        public string Family;
        public string PrefabPath;
        public string BackupPath;
        public int EmissionSources;
        public int FramePresenters;
        public int PreservedEventEmitters;
        public int MergedDirectionalCopies;
        public List<string> Systems = new List<string>();
        public List<string> UnrepresentedSourceEffects = new List<string>();
    }

    [Serializable]
    public sealed class AnomalyParticleRandomizationReport
    {
        public bool Completed;
        public string ManifestPath;
        public string ReportPath;
        public List<AnomalyParticleRandomizationFamilyReport> Families = new List<AnomalyParticleRandomizationFamilyReport>();
        public List<string> Errors = new List<string>();
    }

    public static class AnomalyParticleRandomizationSetup
    {
        public const string SOURCE_MANIFEST = "Assets/_Source/Scripts/Editor/Player/AnomalyParticleSources.json";

        private const string ARCHIVE = "E:/Projects/stalker_raid_into_darkness_Archive/AnomalyStartupRandomization_20261110";
        private const string GLOW = "studen_idle_bottom_glow_02";

        private static readonly string[] FAMILIES = { "Electra", "Zharka", "Tramplin", "Vortex", "Carousel", "Kisel", "Gazirovka" };

        public static AnomalyParticleRandomizationReport Apply(string manifestPath, string reportFolder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Настройка аномалий выполняется только вне Play Mode.");
            }

            string folder = ValidateFolder(reportFolder);
            AnomalyParticleSourceManifest manifest = JsonUtility.FromJson<AnomalyParticleSourceManifest>(File.ReadAllText(manifestPath));
            ValidateManifest(manifest);
            var roots = new List<GameObject>();
            var report = new AnomalyParticleRandomizationReport { ManifestPath = Path.GetFullPath(manifestPath), ReportPath = Path.Combine(folder, "anomaly-particle-randomization.json") };
            try
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                foreach (AnomalyParticleFamilySourceData family in manifest.families)
                {
                    if (stage != null && string.Equals(stage.assetPath, family.prefab, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Закройте Prefab Mode перед настройкой: " + family.prefab);
                    }

                    GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(family.prefab);
                    if (asset == null || EditorUtility.IsDirty(asset))
                    {
                        throw new InvalidOperationException("Префаб отсутствует или имеет несохранённые изменения: " + family.prefab);
                    }

                    GameObject root = PrefabUtility.LoadPrefabContents(family.prefab);
                    roots.Add(root);
                    Configure(root, family);
                }

                Directory.CreateDirectory(folder);
                string backups = Path.Combine(folder, "Backups");
                Directory.CreateDirectory(backups);
                for (int index = 0; index < roots.Count; index++)
                {
                    AnomalyParticleFamilySourceData family = manifest.families[index];
                    string backup = Path.Combine(backups, family.family + ".prefab");
                    File.Copy(family.prefab, backup, false);
                    File.Copy(family.prefab + ".meta", backup + ".meta", false);
                    AnomalyParticleRandomizationFamilyReport result = Inspect(roots[index], family);
                    result.BackupPath = backup;
                    report.Families.Add(result);
                }

                for (int index = 0; index < roots.Count; index++)
                {
                    PrefabUtility.SaveAsPrefabAsset(roots[index], manifest.families[index].prefab, out bool saved);
                    if (!saved)
                    {
                        throw new IOException("Не удалось сохранить префаб: " + manifest.families[index].prefab);
                    }
                }

                report.Completed = true;
                File.WriteAllText(report.ReportPath, JsonUtility.ToJson(report, true));
                return report;
            }
            catch (Exception exception)
            {
                report.Errors.Add(exception.GetType().Name + ": " + exception.Message);
                if (Directory.Exists(folder))
                {
                    File.WriteAllText(report.ReportPath, JsonUtility.ToJson(report, true));
                }

                throw;
            }
            finally
            {
                foreach (GameObject root in roots)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        public static void Configure(GameObject root, AnomalyParticleFamilySourceData family)
        {
            Transform idle = root.transform.Find("Idle");
            if (idle == null)
            {
                throw new InvalidOperationException("Не найдена группа Idle: " + family.family);
            }

            AnomalyParticleEmission existing = root.GetComponent<AnomalyParticleEmission>();
            var owned = new Dictionary<ParticleSystem, float>();
            if (existing != null)
            {
                foreach (AnomalyParticleEmissionSource source in existing.Sources)
                {
                    owned.Add(source.System, source.Rate);
                }
            }

            var sources = new List<AnomalyParticleEmissionSource>();
            var configured = new HashSet<ParticleSystem>();
            foreach (AnomalyParticleEffectSourceData effect in family.effects)
            {
                if (effect.unityBindings == null || effect.unityBindings.Length == 0)
                {
                    continue;
                }

                foreach (AnomalyParticleUnityBinding binding in effect.unityBindings)
                {
                    Transform node = root.transform.Find(binding.path);
                    ParticleSystem system = node != null ? node.GetComponent<ParticleSystem>() : null;
                    if (system == null)
                    {
                        throw new InvalidOperationException("Не найдена система частиц: " + family.family + "/" + binding.path);
                    }

                    if (!configured.Add(system))
                    {
                        throw new InvalidOperationException("Повторная привязка системы частиц: " + family.family + "/" + binding.path);
                    }

                    ParticleSystem.EmissionModule emission = system.emission;
                    ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                    if (binding.duplicateDirectional)
                    {
                        if (family.family != "Gazirovka" || effect.basename != GLOW || system.name != GLOW + "__backward")
                        {
                            throw new InvalidOperationException("Недопустимая направленная копия: " + binding.path);
                        }

                        emission.enabled = false;
                        renderer.enabled = false;
                        continue;
                    }

                    float rate = owned.TryGetValue(system, out float previousRate) ? previousRate : emission.rateOverTime.constant;
                    if (binding.driveEmission)
                    {
                        if (binding.eventControlled || effect.sourceActions == null || effect.sourceActions.Length != 1)
                        {
                            throw new InvalidOperationException("Требуется самостоятельный Source: " + effect.sourceEffect);
                        }

                        float originalRate = effect.sourceActions[0].ratePerSecond;
                        if (binding.canonicalDirectional)
                        {
                            ParticleSystem.MainModule main = system.main;
                            main.maxParticles = effect.nativeMaxParticles;
                            rate = originalRate;
                        }
                        else if (Mathf.Abs(rate - originalRate) > 0.001f || !emission.enabled)
                        {
                            throw new InvalidOperationException("Частота префаба отличается от GRA: " + family.family + "/" + binding.path + " (" + rate + " / " + originalRate + ").");
                        }

                        emission.rateOverTime = 0f;
                        float timeLimit = effect.finiteEmitter ? Mathf.Max(0f, effect.emitterTimeLimit) : 0f;
                        sources.Add(new AnomalyParticleEmissionSource(system, rate, timeLimit));
                    }

                    if (effect.framed && renderer != null && renderer.enabled)
                    {
                        ValidateMaterials(renderer);
                        ParticleSystem.TextureSheetAnimationModule textureAnimation = system.textureSheetAnimation;
                        textureAnimation.enabled = false;
                        ParticleSystem.CustomDataModule custom = system.customData;
                        custom.enabled = true;
                        custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                        custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
                        custom.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(0f));
                        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1X });
                        AnomalyParticleFrames frames = system.GetComponent<AnomalyParticleFrames>();
                        if (frames == null)
                        {
                            frames = system.gameObject.AddComponent<AnomalyParticleFrames>();
                        }

                        frames.Configure(system, effect.frameCount, effect.frameColumns, new Vector2(effect.frameSizeX, effect.frameSizeY), effect.fps, effect.animated, effect.randomStartFrame, effect.randomPlayback);
                    }
                }
            }

            foreach (ParticleSystem system in IdleSystems(root, idle))
            {
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                if (!configured.Contains(system) && (system.emission.rateOverTime.constant > 0f || renderer != null && renderer.enabled))
                {
                    throw new InvalidOperationException("Нет исходных параметров существующей системы: " + family.family + "/" + system.name);
                }
            }

            if (sources.Count == 0)
            {
                throw new InvalidOperationException("Не найдены источники частиц: " + family.family);
            }

            AnomalyParticleEmission controller = existing != null ? existing : root.AddComponent<AnomalyParticleEmission>();
            controller.Configure(sources.ToArray());
        }

        public static AnomalyParticleRandomizationFamilyReport Inspect(GameObject root, AnomalyParticleFamilySourceData family)
        {
            AnomalyParticleEmission emission = root.GetComponent<AnomalyParticleEmission>();
            var report = new AnomalyParticleRandomizationFamilyReport { Family = family.family, PrefabPath = family.prefab, EmissionSources = emission != null ? emission.Sources.Count : 0 };
            foreach (AnomalyParticleEffectSourceData effect in family.effects)
            {
                if (effect.unityBindings == null || effect.unityBindings.Length == 0)
                {
                    report.UnrepresentedSourceEffects.Add(effect.sourceEffect);
                    continue;
                }

                foreach (AnomalyParticleUnityBinding binding in effect.unityBindings)
                {
                    ParticleSystem system = root.transform.Find(binding.path).GetComponent<ParticleSystem>();
                    if (system.GetComponent<AnomalyParticleFrames>() != null)
                    {
                        report.FramePresenters++;
                    }

                    if (system.emission.burstCount > 0)
                    {
                        report.PreservedEventEmitters++;
                    }

                    if (binding.duplicateDirectional && !system.emission.enabled)
                    {
                        report.MergedDirectionalCopies++;
                    }

                    report.Systems.Add(binding.path);
                }
            }

            return report;
        }

        private static HashSet<ParticleSystem> IdleSystems(GameObject root, Transform idle)
        {
            var systems = new HashSet<ParticleSystem>(idle.GetComponentsInChildren<ParticleSystem>(true));
            AnomalyVisual visual = root.GetComponent<AnomalyVisual>();
            if (visual != null)
            {
                SerializedProperty auxiliary = new SerializedObject(visual).FindProperty("_idleAuxiliarySystems");
                if (auxiliary != null)
                {
                    for (int index = 0; index < auxiliary.arraySize; index++)
                    {
                        var system = auxiliary.GetArrayElementAtIndex(index).objectReferenceValue as ParticleSystem;
                        if (system != null)
                        {
                            foreach (ParticleSystem child in system.GetComponentsInChildren<ParticleSystem>(true))
                            {
                                systems.Add(child);
                            }
                        }
                    }
                }
            }

            return systems;
        }

        private static void ValidateMaterials(ParticleSystemRenderer renderer)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || !material.HasProperty("_FrameSize") || !material.HasProperty("_FrameColumns"))
                {
                    throw new InvalidOperationException("Материал не поддерживает исходные кадры X-Ray: " + renderer.name);
                }
            }
        }

        private static void ValidateManifest(AnomalyParticleSourceManifest manifest)
        {
            if (manifest == null || manifest.schemaVersion != 1 || manifest.families == null || manifest.families.Length != FAMILIES.Length)
            {
                throw new ArgumentException("Требуется source manifest семи семейств аномалий.");
            }

            var names = new HashSet<string>();
            foreach (AnomalyParticleFamilySourceData family in manifest.families)
            {
                string expected = "Assets/_Source/Anomalies/" + family.family + "/" + family.family + ".prefab";
                if (Array.IndexOf(FAMILIES, family.family) < 0 || !names.Add(family.family) || family.prefab != expected || family.effects == null || family.effects.Length == 0)
                {
                    throw new ArgumentException("Недопустимое семейство или путь префаба: " + family.family);
                }
            }
        }

        private static string ValidateFolder(string reportFolder)
        {
            string archive = Path.GetFullPath(ARCHIVE).TrimEnd(Path.DirectorySeparatorChar);
            string folder = Path.GetFullPath(reportFolder ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(folder), archive, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(folder).StartsWith("Apply_", StringComparison.Ordinal))
            {
                throw new ArgumentException("Результаты должны находиться в новой папке " + ARCHIVE + "/Apply_<timestamp>.");
            }

            for (DirectoryInfo parent = new DirectoryInfo(folder); parent != null; parent = parent.Parent)
            {
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Каталог результатов проходит через reparse point: " + parent.FullName);
                }
            }

            if (Directory.Exists(folder) && (Directory.GetFiles(folder).Length != 0 || Directory.GetDirectories(folder).Length != 0))
            {
                throw new IOException("Каталог уже содержит результаты: " + folder);
            }

            return folder;
        }
    }
}
