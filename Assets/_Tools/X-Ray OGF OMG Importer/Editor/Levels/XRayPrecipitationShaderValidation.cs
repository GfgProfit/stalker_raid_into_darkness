using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayPrecipitationShaderValidation
    {
        private const string PRODUCTION = "E:\\Projects\\stalker_raid_into_darkness";
        private const string ARCHIVE = "E:\\Projects\\stalker_raid_into_darkness_Archive";
        private const string SHADER_FOLDER = "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/";
        private const string SHADER_NAME = "HDRP/MonolithLit";

        public static string BeginReport(string archiveFolder, string candidateFolder)
        {
            return QueueReport(archiveFolder, candidateFolder, null);
        }

        public static string BeginReportFromContext(string archiveFolder, string candidateFolder, string contextReport)
        {
            return QueueReport(archiveFolder, candidateFolder, RequireArchive(contextReport));
        }

        public static string BeginFocusedReportFromContext(string archiveFolder, string candidateFolder, string contextReport)
        {
            return QueueReport(archiveFolder, candidateFolder, RequireArchive(contextReport), true);
        }

        public static string BeginExpandedReportFromContext(string archiveFolder, string candidateFolder, string contextReport)
        {
            return QueueReport(archiveFolder, candidateFolder, RequireArchive(contextReport), false, true);
        }

        private static string QueueReport(string archiveFolder, string candidateFolder, string contextReport, bool focused = false, bool expanded = false)
        {
            string output = Path.Combine(RequireArchive(archiveFolder), "ShaderBindingJob_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            string statusPath = Path.Combine(output, "Status.json");
            var status = new JobStatus { state = "Queued" };
            File.WriteAllText(statusPath, JsonUtility.ToJson(status, true));
            EditorApplication.CallbackFunction execute = null;
            execute = () =>
            {
                EditorApplication.update -= execute;
                status.state = "Running";
                File.WriteAllText(statusPath, JsonUtility.ToJson(status, true));
                try
                {
                    status.result = ReportInternal(archiveFolder, candidateFolder, contextReport, focused, expanded);
                    status.state = "Complete";
                }
                catch (Exception exception)
                {
                    status.error = exception.ToString();
                    status.state = "Failed";
                }
                File.WriteAllText(statusPath, JsonUtility.ToJson(status, true));
            };
            EditorApplication.update += execute;
            return statusPath;
        }

        public static string Report(string archiveFolder, string candidateFolder)
        {
            return ReportInternal(archiveFolder, candidateFolder, null);
        }

        private static string ReportInternal(string archiveFolder, string candidateFolder, string contextReport, bool focused = false, bool expanded = false)
        {
            if (!string.Equals(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), PRODUCTION, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Inspect precipitation shader resources in the Production Editor.");
            string archivePath = RequireArchive(archiveFolder);
            string output = Path.Combine(archivePath, "ShaderBindings_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            Shader baseline = AssetDatabase.LoadAssetAtPath<Shader>(SHADER_FOLDER + "MonolithLit.shader");
            if (baseline == null) throw new InvalidOperationException("Monolith shader is unavailable.");
            var report = new BindingReport();
            report.project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            report.unityVersion = Application.unityVersion;
            report.graphicsDevice = SystemInfo.graphicsDeviceType.ToString();
            report.compiler = focused ? ShaderCompilerPlatform.D3D + " (" + (int)ShaderCompilerPlatform.D3D + ")" : ShaderCompilerPlatform.D3D + " (" + (int)ShaderCompilerPlatform.D3D + "), " + ShaderCompilerPlatform.D3D12 + " (" + (int)ShaderCompilerPlatform.D3D12 + ")";
            report.compilerSelection = "Compiler metadata is recorded separately from the active rendering API; a Direct3D12 device does not establish which compiler generated a live pass.";
            report.target = BuildTarget.StandaloneWindows64.ToString();
            report.focused = focused;
            report.expanded = expanded;
            report.globalKeywords = GlobalKeywords();
            report.materials = TerrainMaterials();
            if (!string.IsNullOrEmpty(contextReport))
            {
                BindingReport context = JsonUtility.FromJson<BindingReport>(File.ReadAllText(contextReport));
                if (context == null || context.materials == null || context.materials.Length == 0 || context.globalKeywords == null || !string.Equals(Path.GetFullPath(context.project), report.project, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Expected shader context from the same Production project.");
                foreach (MaterialRecord material in context.materials)
                {
                    Material asset = AssetDatabase.LoadAssetAtPath<Material>(material.path);
                    if (asset == null || asset.shader == null || asset.shader.name != SHADER_NAME || material.keywords == null) throw new InvalidDataException("The recorded terrain material is no longer available: " + material.path);
                }
                report.globalKeywords = context.globalKeywords;
                report.materials = context.materials;
                report.contextReport = contextReport;
            }
            var variants = new List<VariantRecord>();
            Shader candidate = null;
            try
            {
                if (!string.IsNullOrEmpty(candidateFolder))
                {
                    string candidatePath = RequireArchive(candidateFolder);
                    string source = CandidateSource(candidatePath);
                    File.WriteAllText(Path.Combine(output, "Candidate.shader.txt"), source);
                    candidate = ShaderUtil.CreateShaderAsset(source, false);
                    if (candidate == null) throw new InvalidOperationException("Unable to compile the isolated in-memory precipitation shader candidate.");
                    candidate.hideFlags = HideFlags.HideAndDontSave;
                    report.candidateFolder = candidatePath;
                }
                ShaderCompilerPlatform[] platforms = focused ? new[] { ShaderCompilerPlatform.D3D } : new[] { ShaderCompilerPlatform.D3D12, ShaderCompilerPlatform.D3D };
                foreach (ShaderCompilerPlatform platform in platforms)
                {
                    Compile(baseline, "Baseline", report.materials, report.globalKeywords, output, variants, false, platform, focused, expanded);
                    if (candidate == null) continue;
                    Compile(candidate, "Candidate", report.materials, report.globalKeywords, output, variants, false, platform, focused, expanded);
                    foreach (var keyword in candidate.keywordSpace.keywords)
                    {
                        if (keyword.name == "_MONOLITH_PRECIPITATION") Compile(candidate, "CandidateWet", report.materials, report.globalKeywords, output, variants, true, platform, focused, expanded);
                    }
                }
            }
            finally
            {
                if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
            }
            report.variants = variants.ToArray();
            string reportPath = Path.Combine(output, "Result.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            return reportPath;
        }

        private static string RequireArchive(string folder)
        {
            string path = Path.GetFullPath(folder ?? string.Empty);
            if (!path.StartsWith(ARCHIVE + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Shader resource evidence must remain under Archive.", nameof(folder));
            return path;
        }

        private static string[] GlobalKeywords()
        {
            var keywords = new List<string>();
            foreach (var keyword in Shader.enabledGlobalKeywords) keywords.Add(keyword.name);
            keywords.Sort(StringComparer.Ordinal);
            return keywords.ToArray();
        }

        private static MaterialRecord[] TerrainMaterials()
        {
            var materials = new HashSet<Material>();
            var records = new List<MaterialRecord>();
            foreach (MeshRenderer renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || material.shader.name != SHADER_NAME || !material.IsKeywordEnabled("_MONOLITH_LEVEL_TERRAIN") || !materials.Add(material)) continue;
                    var record = new MaterialRecord();
                    record.name = material.name;
                    record.path = AssetDatabase.GetAssetPath(material);
                    record.keywords = material.shaderKeywords;
                    Array.Sort(record.keywords, StringComparer.Ordinal);
                    record.sourceTerrain = material.GetFloat("_MonolithSourceTerrain");
                    record.trueStalkerTerrain = material.GetFloat("_MonolithTrueStalkerSourceTerrain");
                    record.terrainQuality = material.GetFloat("_MonolithTerrainQuality");
                    records.Add(record);
                }
            }
            if (records.Count == 0) records.Add(new MaterialRecord { name = "TerrainFallback", keywords = new[] { "_MONOLITH_LEVEL", "_MONOLITH_LEVEL_TERRAIN", "_MATERIAL_FEATURE_SPECULAR_COLOR", "_NORMALMAP_TANGENT_SPACE" } });
            return records.ToArray();
        }

        private static string CandidateSource(string folder)
        {
            string source = File.ReadAllText(Path.Combine(folder, "MonolithLit.shader"));
            source = source.Replace("Shader \"" + SHADER_NAME + "\"", "Shader \"Hidden/XRayPrecipitationBindingCandidate_" + Guid.NewGuid().ToString("N") + "\"");
            foreach (string include in new[] { "MonolithProperties.hlsl", "MonolithData.hlsl", "MonolithWetness.hlsl" })
            {
                string path = Path.Combine(folder, include);
                if (File.Exists(path)) source = source.Replace("#include \"" + include + "\"", File.ReadAllText(path));
            }
            return Regex.Replace(source, "#include \\\"(Monolith[^\\\"]+\\.hlsl)\\\"", match => "#include \"" + SHADER_FOLDER + match.Groups[1].Value + "\"");
        }

        private static void Compile(Shader shader, string label, MaterialRecord[] materials, string[] globals, string output, List<VariantRecord> results, bool precipitation, ShaderCompilerPlatform platform, bool focused, bool expanded)
        {
            var available = new HashSet<string>(StringComparer.Ordinal);
            foreach (var keyword in shader.keywordSpace.keywords) available.Add(keyword.name);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var current = new HashSet<string>(materials[materialIndex].keywords, StringComparer.Ordinal);
                if (precipitation) current.Add("_MONOLITH_PRECIPITATION");
                foreach (string keyword in globals)
                {
                    if (available.Contains(keyword)) current.Add(keyword);
                }
                DefaultChoice(current, "SCREEN_SPACE_SHADOWS_OFF", "SCREEN_SPACE_SHADOWS_ON");
                DefaultChoice(current, "DECALS_OFF", "DECALS_3RT", "DECALS_4RT");
                DefaultChoice(current, "PUNCTUAL_SHADOW_HIGH", "PUNCTUAL_SHADOW_LOW", "PUNCTUAL_SHADOW_MEDIUM");
                DefaultChoice(current, "DIRECTIONAL_SHADOW_HIGH", "DIRECTIONAL_SHADOW_LOW", "DIRECTIONAL_SHADOW_MEDIUM");
                DefaultChoice(current, "AREA_SHADOW_HIGH", "AREA_SHADOW_MEDIUM");
                DefaultChoice(current, "USE_FPTL_LIGHTLIST", "USE_CLUSTERED_LIGHTLIST");
                int profileCount = expanded ? 12 : 4;
                for (int combination = 0; combination < profileCount; combination++)
                {
                    int profile = expanded ? combination % 3 == 0 ? 0 : combination % 3 + 1 : combination;
                    if (focused && profile != 0 && profile != 3) continue;
                    var keywords = new HashSet<string>(current, StringComparer.Ordinal);
                    if (expanded)
                    {
                        keywords.Remove("USE_FPTL_LIGHTLIST");
                        keywords.Remove("USE_CLUSTERED_LIGHTLIST");
                        keywords.Add(combination < 6 ? "USE_FPTL_LIGHTLIST" : "USE_CLUSTERED_LIGHTLIST");
                        keywords.Remove("INSTANCING_ON");
                        if (combination / 3 % 2 != 0) keywords.Add("INSTANCING_ON");
                    }
                    if (profile > 0)
                    {
                        keywords.Remove("DECALS_OFF");
                        keywords.Remove("DECALS_3RT");
                        keywords.Remove("DECALS_4RT");
                        keywords.Remove("_DISABLE_DECALS");
                        keywords.Remove("_DISABLE_SSR");
                        keywords.Remove("SCREEN_SPACE_SHADOWS_OFF");
                        keywords.Add("SCREEN_SPACE_SHADOWS_ON");
                        keywords.Add(profile == 1 ? "DECALS_OFF" : profile == 2 ? "DECALS_3RT" : "DECALS_4RT");
                    }
                    var sorted = new List<string>();
                    foreach (string keyword in keywords)
                    {
                        if (available.Contains(keyword)) sorted.Add(keyword);
                    }
                    sorted.Sort(StringComparer.Ordinal);
                    string signature = string.Join(" ", sorted);
                    if (!seen.Add(signature)) continue;
                    for (int pass = 0; pass < subshader.PassCount; pass++)
                    {
                        ShaderData.Pass shaderPass = subshader.GetPass(pass);
                        if (focused && shaderPass.Name != "ForwardOnly") continue;
                        if (expanded && shaderPass.Name == "DepthForwardOnly" && combination >= 6) continue;
                        if (shaderPass.Name != "ForwardOnly" && (profile != 0 || shaderPass.Name != "DepthForwardOnly")) continue;
                        var record = new VariantRecord { shader = label, material = materials[materialIndex].name, pass = shaderPass.Name, profile = profile, keywords = sorted.ToArray(), compiler = platform.ToString(), compilerValue = (int)platform, lightList = sorted.Contains("USE_CLUSTERED_LIGHTLIST") ? "Clustered" : sorted.Contains("USE_FPTL_LIGHTLIST") ? "FPTL" : "None", instancing = sorted.Contains("INSTANCING_ON") };
                        try
                        {
                            ShaderData.VariantCompileInfo info = shaderPass.CompileVariant(ShaderType.Fragment, record.keywords, platform, BuildTarget.StandaloneWindows64);
                            record.success = info.Success;
                            record.bytecodeBytes = info.ShaderData != null ? info.ShaderData.Length : 0;
                            var messages = new List<string>();
                            if (info.Messages != null)
                            {
                                foreach (ShaderMessage message in info.Messages) messages.Add(message.severity + ": " + message.file + ":" + message.line + " " + message.message);
                            }
                            record.messages = messages.ToArray();
                            var textures = new List<TextureRecord>();
                            if (info.TextureBindings != null)
                            {
                                foreach (ShaderData.TextureBindingInfo binding in info.TextureBindings)
                                {
                                    textures.Add(new TextureRecord { name = binding.Name, slot = binding.Index, sampler = binding.SamplerIndex });
                                    record.maxTextureSlot = Mathf.Max(record.maxTextureSlot, binding.Index);
                                }
                            }
                            record.textures = textures.ToArray();
                            record.textureCount = textures.Count;
                            var resources = new List<ResourceRecord>();
                            if (info.ResourceBindings != null)
                            {
                                foreach (ShaderData.ResourceBindingInfo binding in info.ResourceBindings) resources.Add(new ResourceRecord { name = binding.Name, slot = binding.Index, kind = binding.Kind.ToString(), writable = binding.Writable });
                            }
                            record.resources = resources.ToArray();
                            if (record.bytecodeBytes > 0) File.WriteAllBytes(Path.Combine(output, label + "_" + platform + "_" + results.Count.ToString(CultureInfo.InvariantCulture) + "_" + shaderPass.Name + ".bin"), info.ShaderData);
                        }
                        catch (Exception exception)
                        {
                            record.success = false;
                            record.messages = new[] { exception.ToString() };
                        }
                        results.Add(record);
                    }
                }
            }
        }

        private static void DefaultChoice(HashSet<string> keywords, params string[] choices)
        {
            foreach (string choice in choices)
            {
                if (keywords.Contains(choice)) return;
            }
            keywords.Add(choices[0]);
        }

        [Serializable]
        private sealed class JobStatus
        {
            public string state;
            public string result;
            public string error;
        }

        [Serializable]
        private sealed class BindingReport
        {
            public string project;
            public string unityVersion;
            public string graphicsDevice;
            public string compiler;
            public string compilerSelection;
            public string target;
            public bool focused;
            public bool expanded;
            public string candidateFolder;
            public string contextReport;
            public string[] globalKeywords;
            public MaterialRecord[] materials;
            public VariantRecord[] variants;
        }

        [Serializable]
        private sealed class MaterialRecord
        {
            public string name;
            public string path;
            public string[] keywords;
            public float sourceTerrain;
            public float trueStalkerTerrain;
            public float terrainQuality;
        }

        [Serializable]
        private sealed class VariantRecord
        {
            public string shader;
            public string compiler;
            public int compilerValue;
            public string material;
            public string pass;
            public int profile;
            public string lightList;
            public bool instancing;
            public string[] keywords;
            public bool success;
            public int bytecodeBytes;
            public int textureCount;
            public int maxTextureSlot = -1;
            public TextureRecord[] textures;
            public ResourceRecord[] resources;
            public string[] messages;
        }

        [Serializable]
        private sealed class TextureRecord
        {
            public string name;
            public int slot;
            public int sampler;
        }

        [Serializable]
        private sealed class ResourceRecord
        {
            public string name;
            public int slot;
            public string kind;
            public bool writable;
        }
    }
}
