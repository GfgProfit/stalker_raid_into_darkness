using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XRay.Importer.Core.Spawn;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelAnomalyImportPlan
    {
        public string LevelName { get; internal set; }
        public string SpawnAssetPath { get; internal set; }
        public string Origin { get; internal set; }
        public string Sha256 { get; internal set; }
        public XRayLevelSpawnData Data { get; internal set; }
        public XRayLevelAnomalyCatalog Catalog { get; internal set; }
    }

    [Serializable]
    public sealed class XRayLevelAnomalyImportReport
    {
        public string LevelName;
        public string SpawnAssetPath;
        public string Origin;
        public string Sha256;
        public string PrefabPath;
        public string ReportPath;
        public string ImportedUtc;
        public bool Completed;
        public int SourceCount;
        public int ImportedCount;
        public List<string> Missing = new List<string>();
        public List<string> Unsupported = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();
    }

    public static class XRayLevelAnomalyImporter
    {
        public const string SPAWN_PATH = "spawns/all.spawn";

        private static readonly Dictionary<string, XRayLevelAnomalyImportReport> _reports = new Dictionary<string, XRayLevelAnomalyImportReport>(StringComparer.OrdinalIgnoreCase);

        public static void DrawSettings(XRayLevelImportSettings settings)
        {
            settings.ImportAnomalies = EditorGUILayout.Toggle(new GUIContent("Import Anomalies", "Создавать аномалии из выигравшего all.spawn при импорте локации."), settings.ImportAnomalies);
            XRayLevelAnomalyCatalog catalog = settings.AnomalyCatalog != null ? settings.AnomalyCatalog : XRayLevelAnomalyCatalog.LoadDefault();
            EditorGUI.BeginChangeCheck();
            XRayLevelAnomalyCatalog selected = (XRayLevelAnomalyCatalog)EditorGUILayout.ObjectField(new GUIContent("Anomaly Catalog", "Префабы аномалий по секциям all.spawn. По умолчанию используется общий конфиг импортёра."), catalog, typeof(XRayLevelAnomalyCatalog), false);
            if (EditorGUI.EndChangeCheck())
            {
                settings.AnomalyCatalog = selected;
            }

            if (catalog == null)
            {
                EditorGUILayout.HelpBox("Конфиг префабов аномалий не найден.", MessageType.Warning);
            }
            else if (!catalog.Validate(out string error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else
            {
                var missing = new HashSet<string>();
                foreach (XRayLevelAnomalyEntry entry in catalog.Entries)
                {
                    if (entry.Prefab == null)
                    {
                        missing.Add(entry.DisplayName);
                    }
                }

                if (missing.Count > 0)
                {
                    EditorGUILayout.HelpBox("В проекте нет префабов: " + string.Join(", ", missing) + ". Подробности по локации появятся после импорта аномалий.", MessageType.Warning);
                }
            }
        }

        public static void DrawLevelAction(XRayLevelEntry entry, string root)
        {
            string prefabPath = PrefabPath(root, entry.Name);
            using (new EditorGUI.DisabledScope(!File.Exists(prefabPath) || Application.isPlaying || XRayLevelImportJob.Current != null || XRayLevelImportJob.IsBusy(entry.Name)))
            {
                if (GUILayout.Button(new GUIContent("Anomalies Only", "Обновить только аномалии в префабе локации. Геометрия, материалы и сцена не пересобираются."), GUILayout.Width(112f)))
                {
                    ImportOnly(entry.Name, root, XRayAssetSourceUserSettings.instance.LevelImport.AnomalyCatalog);
                }
            }
        }

        public static void DrawLastResult(string level, string root)
        {
            if (!_reports.TryGetValue(PrefabPath(root, level), out XRayLevelAnomalyImportReport report))
            {
                return;
            }

            string message = report.Completed ? $"Аномалии: создано {report.ImportedCount} из {report.SourceCount}." : "Импорт аномалий не завершён: " + string.Join("; ", report.Errors);
            if (report.Missing.Count > 0)
            {
                message += "\nНет префабов: " + string.Join("; ", report.Missing);
            }

            if (report.Unsupported.Count > 0)
            {
                message += "\nНе импортированы: " + string.Join("; ", report.Unsupported);
            }

            if (report.Warnings.Count > 0)
            {
                message += "\n" + string.Join("\n", report.Warnings);
            }

            EditorGUILayout.HelpBox(message, !report.Completed || report.Missing.Count > 0 || report.Unsupported.Count > 0 || report.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
        }

        public static XRayLevelAnomalyImportPlan Prepare(XRayAssetIndex index, string levelName, string outputRoot, XRayLevelAnomalyCatalog catalog)
        {
            if (string.IsNullOrWhiteSpace(levelName) || string.IsNullOrWhiteSpace(outputRoot) || !outputRoot.StartsWith("Assets/", StringComparison.Ordinal) || !XRayVirtualPath.TryNormalizeSafe(outputRoot, out string normalizedRoot) || !string.Equals(normalizedRoot, outputRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Требуется имя локации и корректный корень импорта внутри Assets.");
            }

            if (index == null || !index.TryGet(SPAWN_PATH, out XRayIndexedFile file))
            {
                throw new XRaySourceException("Не найден победитель MO2 spawns/all.spawn. Обновите виртуальный индекс с папкой spawns.");
            }

            catalog = catalog != null ? catalog : XRayLevelAnomalyCatalog.LoadDefault();
            if (catalog == null)
            {
                throw new XRaySourceException("Не найден конфиг префабов аномалий.");
            }

            if (!catalog.Validate(out string error))
            {
                throw new XRaySourceException(error);
            }

            XRayAssetSource source = index.GetSource(file.Winner);
            string origin = source.Origin;
            byte[] bytes = index.ReadAllBytes(file.Winner);
            XRayLtxDatabase database = XRayLtxDatabase.LoadSystem(index);
            var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string section in XRayLevelSpawnReader.ReadSections(bytes, origin))
            {
                string className = XRayLtxDatabase.Unquote(database.GetValue(section, "class"));
                if (IsAnomalyClass(className))
                {
                    sections.Add(section);
                }
                else if (className == null && (section.StartsWith("zone_", StringComparison.OrdinalIgnoreCase) || section.EndsWith("_zone", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new XRaySourceException($"Для секции {section} из all.spawn отсутствует class в действующих configs/system.ltx и DLTX. Обновите индекс конфигов.");
                }
            }

            XRayLevelSpawnData data = XRayLevelSpawnReader.Read(bytes, origin, sections);
            if (!data.Levels.Exists(level => string.Equals(level.Name, levelName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new XRaySourceException($"В all.spawn победителя «{origin}» нет локации {levelName}. Выберите совместимый профиль MO2; перекрытые spawn-файлы не используются.");
            }

            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }

            bool baseSource = source.Kind == XRayAssetSourceKind.BaseAnomaly || source.Kind == XRayAssetSourceKind.GameGamedata || string.Equals(source.Origin, Mo2ArchiveLayout.GAME_FOLDER_ORIGIN, StringComparison.OrdinalIgnoreCase);
            string folder = SharedSpawnFolder(outputRoot);
            string stem = SpawnFileStem(source.Origin, baseSource);
            XRayLevelPaths.EnsureFolder(folder);
            string target = folder + "/" + stem + "_all.spawn";
            if (File.Exists(target) && !string.Equals(XRayLevelGamedata.ComputeHash(target), hash, StringComparison.OrdinalIgnoreCase))
            {
                target = folder + "/" + stem + "_" + hash + "_all.spawn";
            }

            if (!File.Exists(target))
            {
                File.WriteAllBytes(target, bytes);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            }
            else if (!string.Equals(XRayLevelGamedata.ComputeHash(target), hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Общая копия all.spawn имеет неверный SHA-256: " + target);
            }

            return new XRayLevelAnomalyImportPlan { LevelName = levelName, SpawnAssetPath = target, Origin = origin, Sha256 = hash, Data = data, Catalog = catalog };
        }

        public static bool IsAnomalyClass(string className)
        {
            return !string.IsNullOrEmpty(className) && (className.StartsWith("ZS_", StringComparison.OrdinalIgnoreCase) || className.StartsWith("Z_", StringComparison.OrdinalIgnoreCase)) && !string.Equals(className, "Z_CFIRE", StringComparison.OrdinalIgnoreCase);
        }

        public static XRayLevelAnomalyImportPlan PrepareCurrent(string levelName, string outputRoot, XRayLevelAnomalyCatalog catalog)
        {
            return Prepare(XRayLevelSourceCache.GetIndex(false), levelName, outputRoot, catalog);
        }

        public static string SpawnFileStem(string sourceName, bool baseSource)
        {
            if (baseSource)
            {
                return "anomaly";
            }

            string name = sourceName ?? string.Empty;
            int bracket = name.LastIndexOf(']');
            if (bracket >= 0)
            {
                name = name.Substring(bracket + 1);
            }

            var builder = new StringBuilder();
            foreach (char ch in name)
            {
                if (ch < 128 && char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.Length == 0 ? "source" : builder.ToString();
        }

        public static XRayLevelAnomalyPlacementResult Apply(GameObject root, XRayLevelAnomalyImportPlan plan)
        {
            return XRayLevelAnomalyPlacement.Apply(root, plan.LevelName, plan.SpawnAssetPath, plan.Origin, plan.Sha256, plan.Data, plan.Catalog);
        }

        public static XRayLevelAnomalyPlacementResult UpdatePrefab(string prefabPath, XRayLevelAnomalyImportPlan plan)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && string.Equals(stage.assetPath, prefabPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Закройте Prefab Mode локации перед импортом аномалий: " + prefabPath);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                XRayLevelAnomalyPlacementResult result = Apply(root, plan);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                if (!saved)
                {
                    throw new IOException("Не удалось сохранить префаб локации: " + prefabPath);
                }

                return result;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static XRayLevelAnomalyImportReport ImportOnly(string levelName, string root, XRayLevelAnomalyCatalog catalog)
        {
            string prefabPath = PrefabPath(root, levelName);
            var report = new XRayLevelAnomalyImportReport { LevelName = levelName, PrefabPath = prefabPath, ImportedUtc = DateTime.UtcNow.ToString("u") };
            try
            {
                if (Application.isPlaying || XRayLevelImportJob.Current != null || XRayLevelImportJob.IsBusy(levelName))
                {
                    throw new InvalidOperationException("Импорт аномалий недоступен в Play Mode или во время импорта локации.");
                }

                if (!File.Exists(prefabPath))
                {
                    throw new FileNotFoundException("Сначала импортируйте геометрию локации.", prefabPath);
                }

                EditorUtility.DisplayProgressBar("X-Ray Anomalies", "Чтение all.spawn победителя MO2", 0.1f);
                XRayLevelAnomalyImportPlan plan = PrepareCurrent(levelName, root, catalog);
                FillReport(report, plan);
                EditorUtility.DisplayProgressBar("X-Ray Anomalies", "Обновление аномалий в префабе локации", 0.6f);
                XRayLevelAnomalyPlacementResult result = UpdatePrefab(prefabPath, plan);
                CompleteReport(report, result);
            }
            catch (Exception exception)
            {
                report.Errors.Add(exception.Message);
                Debug.LogError("[X-Ray Anomalies] " + levelName + ": " + exception.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                SaveReport(report);
            }

            return report;
        }

        public static XRayLevelAnomalyImportReport Record(string prefabPath, XRayLevelAnomalyImportPlan plan, XRayLevelAnomalyPlacementResult result)
        {
            var report = new XRayLevelAnomalyImportReport { LevelName = plan.LevelName, PrefabPath = prefabPath, ImportedUtc = DateTime.UtcNow.ToString("u") };
            FillReport(report, plan);
            CompleteReport(report, result);
            SaveReport(report);
            return report;
        }

        private static string PrefabPath(string root, string levelName)
        {
            return XRayLevelPaths.LevelFolder(root, levelName) + "/Prefabs/" + levelName + ".prefab";
        }

        private static string SharedSpawnFolder(string outputRoot)
        {
            return outputRoot == XRayLevelPaths.ROOT || outputRoot.StartsWith(XRayLevelPaths.ROOT + "/", StringComparison.OrdinalIgnoreCase) ? XRayLevelPaths.ANOMALY_ROOT : outputRoot + "/Sources";
        }

        private static void FillReport(XRayLevelAnomalyImportReport report, XRayLevelAnomalyImportPlan plan)
        {
            report.SpawnAssetPath = plan.SpawnAssetPath;
            report.Origin = plan.Origin;
            report.Sha256 = plan.Sha256;
        }

        private static void CompleteReport(XRayLevelAnomalyImportReport report, XRayLevelAnomalyPlacementResult result)
        {
            report.SourceCount = result.SourceCount;
            report.ImportedCount = result.ImportedCount;
            report.Missing.AddRange(result.Missing);
            report.Unsupported.AddRange(result.Unsupported);
            report.Warnings.AddRange(result.Warnings);
            report.Completed = true;
        }

        private static void SaveReport(XRayLevelAnomalyImportReport report)
        {
            string folder = Path.Combine(XRayLevelPaths.ArchiveRoot, "XRayLevelAnomalies", XRayLevelPaths.SanitizeName(report.LevelName), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));
            Directory.CreateDirectory(folder);
            report.ReportPath = Path.Combine(folder, "AnomalyImportReport.json");
            File.WriteAllText(report.ReportPath, JsonUtility.ToJson(report, true));
            _reports[report.PrefabPath] = report;
        }
    }
}
