using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayWeatherResourceImporter
    {
        private readonly XRayTextureResolver _resolver;
        private readonly XRayTextureImporter _importer;
        private readonly string _folder;
        private readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _reusable = new Dictionary<string, string>(StringComparer.Ordinal);

        public XRayWeatherResourceImporter(XRayAssetIndex index, string folder, string reuseRoot = null)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            string normalized = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            string assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || !Path.GetFullPath(normalized).StartsWith(assets, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Weather textures require a dedicated Assets/... folder.", nameof(folder));
            _folder = normalized;
            _resolver = new XRayTextureResolver(index);
            _importer = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>()) { ContentKeys = true };
            if (!string.IsNullOrEmpty(reuseRoot) && Directory.Exists(reuseRoot))
            {
                var paths = new List<string>(Directory.EnumerateFiles(reuseRoot, "*_weather*.*", SearchOption.AllDirectories));
                paths.Sort(StringComparer.Ordinal);
                foreach (string candidate in paths)
                {
                    string path = candidate.Replace('\\', '/');
                    string extension = Path.GetExtension(path);
                    if (extension != ".asset" && extension != ".png")
                    {
                        continue;
                    }

                    string key = XRayAssetMarker.ReadKey(path);
                    if (!string.IsNullOrEmpty(key) && key.Contains("#weather-unorm@")) _reusable.TryAdd(key, path);
                }
            }
        }

        public Texture Bind(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string virtualPath = XRayVirtualPath.TextureToVirtualPath(name, ".dds");
            if (_textures.TryGetValue(virtualPath, out Texture cached)) return cached;
            XRayResolvedFile file = _resolver.ResolveFile(virtualPath);
            if (!file.IsResolved) throw new InvalidDataException("INPUT BLOCKED: missing effective weather resource " + virtualPath);
            byte[] bytes = file.Winner.ReadAllBytes();
            DdsHeader header = DdsHeader.Parse(bytes);
            if (_reusable.TryGetValue(_importer.WeatherKey(virtualPath, bytes), out string existingPath))
            {
                Texture existing = AssetDatabase.LoadAssetAtPath<Texture>(existingPath);
                if (MatchesSource(existingPath, existing, header))
                {
                    _textures.Add(virtualPath, existing);
                    return existing;
                }
            }
            PrepareFolder();
            XRayTextureAssetResult result = _importer.ImportWeather(file, _folder, header.IsCubemap);
            Texture texture = string.IsNullOrEmpty(result.AssetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(result.AssetPath);
            if (texture == null) throw new InvalidDataException("INPUT BLOCKED: cannot import effective weather resource " + virtualPath + " from " + file.Winner.Location + ": " + result.Note);
            _textures.Add(virtualPath, texture);
            return texture;
        }

        private static bool MatchesSource(string path, Texture texture, DdsHeader header)
        {
            if (texture == null)
            {
                return false;
            }

            if (!header.IsCubemap && header.Format != DdsFormat.RgbaHalf && texture is Texture2D && AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                return width == header.Width && height == header.Height && !importer.sRGBTexture && importer.mipmapEnabled;
            }

            return texture.width == header.Width && texture.height == header.Height && texture.mipmapCount == header.MipCount && (texture is Cubemap) == header.IsCubemap;
        }

        private void PrepareFolder()
        {
            string parent = "Assets";
            foreach (string part in _folder.Substring(7).Split('/'))
            {
                string path = parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, part);
                parent = path;
            }
        }
    }
}
