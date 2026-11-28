using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Dds;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class XRayParticleImporter
    {
        public const string ROOT = "Assets/_Source/Effects/XRay";
        public const string PARTICLES_FOLDER = ROOT + "/Particles";
        public const string TEXTURES_FOLDER = ROOT + "/Textures";
        public const string SHADER_NAME = "RaidIntoDarkness/XRayParticle";

        private const string MENU = "Tools/Effects/Import IWP Weapon Particles";
        private const string LOG = "[XRayParticles] ";
        private const string PREFIX = "weapons\\";
        private const string PARTICLES_FILE = "particles.xr";
        private const float ADDITIVE_INTENSITY = 4.0f;

        private static readonly Regex CALIBER = new Regex(@"^weapons\\(effects\\)?\d", RegexOptions.IgnoreCase);

        [MenuItem(MENU)]
        public static void ImportFromMenu()
        {
            Import();
        }

        public static void ImportBatch()
        {
            bool success = false;

            try
            {
                success = Import() > 0;
            }
            catch (Exception exception)
            {
                Debug.LogError(LOG + "Import failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        public static string GroupPrefabPath(string groupName)
        {
            return AssetPath(groupName, ".prefab");
        }

        public static bool IsImported(string name)
        {
            return name.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase) && name.IndexOf("shell", StringComparison.OrdinalIgnoreCase) < 0 && CALIBER.IsMatch(name) == false;
        }

        public static int Import()
        {
            string source = Path.Combine(IwpWeaponSetup.IWP_ROOT, PARTICLES_FILE);

            if (File.Exists(source) == false)
            {
                Debug.LogError($"{LOG}'{source}' is missing.");
                return 0;
            }

            var effects = new List<XRayParticleEffectData>();
            var groups = new List<XRayParticleGroupData>();
            XRayParticleLibraryReader.Read(File.ReadAllBytes(source), IsImported, effects, groups);
            Shader shader = Shader.Find(SHADER_NAME);

            if (shader == null)
            {
                Debug.LogError($"{LOG}Shader '{SHADER_NAME}' is missing.");
                return 0;
            }

            var log = new StringBuilder();
            Dictionary<string, Texture2D> textures = ImportTextures(effects, log);
            var assets = new Dictionary<string, XRayParticleEffectAsset>(StringComparer.OrdinalIgnoreCase);

            foreach (XRayParticleEffectData data in effects)
            {
                assets[data.Name] = CreateEffect(data, shader, textures, log);
            }

            int prefabs = 0;

            foreach (XRayParticleGroupData data in groups)
            {
                XRayParticleGroupAsset group = CreateGroup(data, assets, log);

                if (CreatePrefab(group) != null)
                {
                    prefabs++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{LOG}Imported {assets.Count} effect(s), {groups.Count} group(s), {prefabs} prefab(s), {textures.Count} texture(s).\n{log}");
            return prefabs;
        }

        private static Dictionary<string, Texture2D> ImportTextures(List<XRayParticleEffectData> effects, StringBuilder log)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (XRayParticleEffectData effect in effects)
            {
                string texture = MainTexture(effect.TextureName);

                if (string.IsNullOrEmpty(texture) == false && BlendFromShader(effect.ShaderName) != XRayParticleBlendMode.Distort)
                {
                    names.Add(texture);
                }
            }

            var imported = new Dictionary<string, (string Path, int Width, int Height)>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in names)
            {
                StalkerXRaySources.EnsureFolder(Path.GetDirectoryName(TexturePath(name)).Replace('\\', '/'));
            }

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string name in names)
                {
                    string file = Path.Combine(IwpWeaponSetup.IWP_ROOT, "textures", name.Replace('\\', '/') + ".dds");

                    if (File.Exists(file) == false)
                    {
                        log.AppendLine($"Texture '{name}' is missing in IWP.");
                        continue;
                    }

                    byte[] data = File.ReadAllBytes(file);
                    DdsHeader header;
                    byte[] png;
                    try
                    {
                        header = DdsHeader.Parse(data);
                        png = XRayTextureImporter.EncodeVerifiedPng(data, header, false);
                    }
                    catch (Exception exception) when (exception is FormatException || exception is NotSupportedException || exception is UnityException)
                    {
                        throw new InvalidOperationException($"Particle texture '{file}' could not be converted to PNG: {exception.Message}", exception);
                    }

                    string target = TexturePath(name);
                    StorePng(target, png);
                    imported[name] = (target, header.Width, header.Height);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var result = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, (string Path, int Width, int Height)> pair in imported)
            {
                string texturePath = pair.Value.Path;
                if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer)
                {
                    bool changed = XRayTextureImportQueue.ApplyPngMipPolicy(importer, pair.Value.Width, pair.Value.Height);
                    if (!importer.sRGBTexture || importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency || importer.textureCompression != TextureImporterCompression.CompressedHQ)
                    {
                        importer.sRGBTexture = true;
                        importer.alphaSource = TextureImporterAlphaSource.FromInput;
                        importer.alphaIsTransparency = false;
                        importer.textureCompression = TextureImporterCompression.CompressedHQ;
                        changed = true;
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                    }
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

                if (texture == null)
                {
                    log.AppendLine($"Texture '{texturePath}' failed to import.");
                    continue;
                }

                result[pair.Key] = texture;
            }

            return result;
        }

        private static XRayParticleEffectAsset CreateEffect(XRayParticleEffectData data, Shader shader, Dictionary<string, Texture2D> textures, StringBuilder log)
        {
            string path = AssetPath(data.Name, ".asset");
            XRayParticleEffectAsset asset = LoadOrCreate<XRayParticleEffectAsset>(path);
            XRayParticleBlendMode blend = BlendFromShader(data.ShaderName);
            asset.SetImportData(data.Name, data.MaxParticles, data.Flags, data.ShaderName, data.TextureName, blend, data.FrameSize, data.FrameColumns, data.FrameCount, data.FrameSpeed, data.TimeLimit, data.VelocityScale, data.DefaultRotation, new List<XRayParticleAction>(data.Actions));

            if (data.Unsupported.Count > 0)
            {
                log.AppendLine($"Effect '{data.Name}' skips actions: {string.Join(", ", data.Unsupported)}.");
            }

            Material material = null;

            if (blend != XRayParticleBlendMode.Distort && textures.TryGetValue(MainTexture(data.TextureName), out Texture2D texture))
            {
                material = CreateMaterial(AssetPath(data.Name, ".mat"), shader, texture, blend, data);
            }

            asset.SetMaterial(material);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static Material CreateMaterial(string path, Shader shader, Texture2D texture, XRayParticleBlendMode blend, XRayParticleEffectData data)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_MainTex", texture);
            bool framed = (data.Flags & XRayParticleEffectAsset.FRAMED) != 0;
            material.SetVector("_FrameSize", framed ? new Vector4(data.FrameSize.x, data.FrameSize.y, 0f, 0f) : new Vector4(1f, 1f, 0f, 0f));
            material.SetFloat("_FrameColumns", framed ? Mathf.Max(1, data.FrameColumns) : 1f);
            GetBlend(blend, out BlendMode src, out BlendMode dst);
            material.SetFloat("_SrcBlend", (float)src);
            material.SetFloat("_DstBlend", (float)dst);
            material.SetFloat("_Intensity", blend == XRayParticleBlendMode.Add || blend == XRayParticleBlendMode.AlphaAdd ? ADDITIVE_INTENSITY : 1.0f);
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static XRayParticleGroupAsset CreateGroup(XRayParticleGroupData data, Dictionary<string, XRayParticleEffectAsset> effects, StringBuilder log)
        {
            XRayParticleGroupAsset asset = LoadOrCreate<XRayParticleGroupAsset>(AssetPath(data.Name, ".asset"));
            var entries = new List<XRayParticleGroupEntry>();

            foreach (XRayParticleGroupEffectData item in data.Effects)
            {
                if (effects.TryGetValue(item.EffectName, out XRayParticleEffectAsset effect) == false)
                {
                    log.AppendLine($"Group '{data.Name}' references missing effect '{item.EffectName}'.");
                }

                entries.Add(new XRayParticleGroupEntry(effect, item.Time0, item.Time1, item.Flags));
            }

            asset.SetImportData(data.Name, data.TimeLimit, entries);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static GameObject CreatePrefab(XRayParticleGroupAsset group)
        {
            string name = Path.GetFileName(group.GroupName.Replace('\\', '/'));
            var root = new GameObject(name);

            try
            {
                var players = new XRayParticleEffectPlayer[group.Entries.Count];

                for (int i = 0; i < group.Entries.Count; i++)
                {
                    XRayParticleEffectAsset effect = group.Entries[i].Effect;

                    if (effect == null)
                    {
                        continue;
                    }

                    var child = new GameObject(Path.GetFileName(effect.EffectName.Replace('\\', '/')));
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<ParticleSystem>();
                    players[i] = child.AddComponent<XRayParticleEffectPlayer>();
                    players[i].SetAsset(effect);
                }

                root.AddComponent<XRayParticleGroupPlayer>().Setup(group, players);
                return PrefabUtility.SaveAsPrefabAsset(root, GroupPrefabPath(group.GroupName));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset != null)
            {
                return asset;
            }

            StalkerXRaySources.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static string AssetPath(string name, string extension)
        {
            return PARTICLES_FOLDER + "/" + name.Replace('\\', '/') + extension;
        }

        private static string TexturePath(string name)
        {
            return TEXTURES_FOLDER + "/" + name.Replace('\\', '/') + ".png";
        }

        private static void StorePng(string assetPath, byte[] png)
        {
            string legacyPath = Path.ChangeExtension(assetPath, ".dds");
            bool exists = File.Exists(assetPath);
            if (exists && File.ReadAllBytes(assetPath).AsSpan().SequenceEqual(png))
            {
                return;
            }

            bool hasLegacy = File.Exists(legacyPath);
            if (hasLegacy && exists)
            {
                throw new IOException($"Particle texture '{legacyPath}' cannot replace the existing '{assetPath}'.");
            }

            string pendingPath = assetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool moved = false;
            try
            {
                File.WriteAllBytes(pendingPath, png);
                if (hasLegacy)
                {
                    string error = AssetDatabase.MoveAsset(legacyPath, assetPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        throw new IOException($"Could not preserve the particle texture GUID when moving '{legacyPath}' to '{assetPath}': {error}");
                    }

                    moved = true;
                    exists = true;
                }

                if (exists)
                {
                    AssetDatabase.ReleaseCachedFileHandles();
                    File.Replace(pendingPath, assetPath, null);
                }
                else
                {
                    File.Move(pendingPath, assetPath);
                }
            }
            catch (Exception exception)
            {
                if (moved)
                {
                    string error = AssetDatabase.MoveAsset(assetPath, legacyPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        throw new IOException($"Could not restore the particle texture from '{assetPath}' to '{legacyPath}': {error}", exception);
                    }
                }

                throw;
            }
            finally
            {
                File.Delete(pendingPath);
            }
        }

        private static string MainTexture(string textures)
        {
            if (string.IsNullOrEmpty(textures))
            {
                return string.Empty;
            }

            int comma = textures.IndexOf(',');
            return (comma >= 0 ? textures.Substring(0, comma) : textures).Trim();
        }

        private static XRayParticleBlendMode BlendFromShader(string shader)
        {
            string name = (shader ?? string.Empty).ToLowerInvariant();

            if (name.Contains("distort"))
            {
                return XRayParticleBlendMode.Distort;
            }

            if (name.Contains("alpha_add"))
            {
                return XRayParticleBlendMode.AlphaAdd;
            }

            if (name.EndsWith("add"))
            {
                return XRayParticleBlendMode.Add;
            }

            if (name.Contains("mul2x") || name.Contains("mul_2x"))
            {
                return XRayParticleBlendMode.Multiply2X;
            }

            if (name.Contains("mul"))
            {
                return XRayParticleBlendMode.Multiply;
            }

            if (name.Contains("opaque"))
            {
                return XRayParticleBlendMode.Opaque;
            }

            return XRayParticleBlendMode.Blend;
        }

        private static void GetBlend(XRayParticleBlendMode mode, out BlendMode src, out BlendMode dst)
        {
            switch (mode)
            {
                case XRayParticleBlendMode.Opaque:
                    src = BlendMode.One;
                    dst = BlendMode.Zero;
                    break;
                case XRayParticleBlendMode.Add:
                    src = BlendMode.One;
                    dst = BlendMode.One;
                    break;
                case XRayParticleBlendMode.Multiply:
                    src = BlendMode.DstColor;
                    dst = BlendMode.Zero;
                    break;
                case XRayParticleBlendMode.Multiply2X:
                    src = BlendMode.DstColor;
                    dst = BlendMode.SrcColor;
                    break;
                case XRayParticleBlendMode.AlphaAdd:
                    src = BlendMode.SrcAlpha;
                    dst = BlendMode.One;
                    break;
                default:
                    src = BlendMode.SrcAlpha;
                    dst = BlendMode.OneMinusSrcAlpha;
                    break;
            }
        }
    }
}
