using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor
{
    [Serializable]
    public sealed class XRayAssetMarker
    {
        public const string PREFIX = "xray-asset:";
        public const string FOREIGN_KEY = "";

        [SerializeField] private string _key;
        [SerializeField] private string _role;
        [SerializeField] private string _source;
        [SerializeField] private string _sourcePath;
        [SerializeField] private string _stamp;

        public XRayAssetMarker(string key, string role, string source, string sourcePath, string stamp)
        {
            _key = key;
            _role = role;
            _source = source;
            _sourcePath = sourcePath;
            _stamp = stamp;
        }

        public string Key => _key;
        public string Role => _role;
        public string Source => _source;
        public string SourcePath => _sourcePath;
        public string Stamp => _stamp;

        public string Serialize()
        {
            return PREFIX + JsonUtility.ToJson(this);
        }

        public static XRayAssetMarker Parse(string userData)
        {
            if (string.IsNullOrEmpty(userData) || !userData.StartsWith(PREFIX, StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<XRayAssetMarker>(userData.Substring(PREFIX.Length));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static XRayAssetMarker Read(string assetPath)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            return importer != null ? Parse(importer.userData) : null;
        }

        public static XRayAssetMarker ReadEffective(string assetPath)
        {
            if (XRayTextureImportQueue.TryPeek(assetPath, out XRayTextureImportRequest request))
            {
                return request.Marker;
            }

            return Read(assetPath);
        }

        public static string ReadKey(string assetPath)
        {
            if (!File.Exists(assetPath))
            {
                return null;
            }

            XRayAssetMarker marker = ReadEffective(assetPath);
            return marker != null ? marker.Key : FOREIGN_KEY;
        }

        public static void Write(string assetPath, XRayAssetMarker marker)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                return;
            }

            string userData = marker.Serialize();
            if (importer.userData != userData)
            {
                importer.userData = userData;
                EditorUtility.SetDirty(importer);
                AssetDatabase.WriteImportSettingsIfDirty(assetPath);
            }
        }

        public static string CreateStamp(string file)
        {
            var info = new FileInfo(file);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : string.Empty;
        }
    }
}
