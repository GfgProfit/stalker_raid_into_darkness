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

        public XRayWeatherResourceImporter(XRayAssetIndex index, string folder)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            string normalized = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            string assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || !Path.GetFullPath(normalized).StartsWith(assets, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Weather textures require a dedicated Assets/... folder.", nameof(folder));
            _folder = normalized;
            _resolver = new XRayTextureResolver(index);
            _importer = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>()) { ContentKeys = true };
        }

        public Texture Bind(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string virtualPath = XRayVirtualPath.TextureToVirtualPath(name, ".dds");
            if (_textures.TryGetValue(virtualPath, out Texture cached)) return cached;
            XRayResolvedFile file = _resolver.ResolveFile(virtualPath);
            if (!file.IsResolved) throw new InvalidDataException("INPUT BLOCKED: missing effective weather resource " + virtualPath);
            DdsHeader header = DdsHeader.Parse(file.Winner.ReadAllBytes());
            PrepareFolder();
            XRayTextureAssetResult result = _importer.ImportWeather(file, _folder, header.IsCubemap);
            Texture texture = string.IsNullOrEmpty(result.AssetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(result.AssetPath);
            if (texture == null) throw new InvalidDataException("INPUT BLOCKED: cannot import effective weather resource " + virtualPath + ": " + result.Note);
            _textures.Add(virtualPath, texture);
            return texture;
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
