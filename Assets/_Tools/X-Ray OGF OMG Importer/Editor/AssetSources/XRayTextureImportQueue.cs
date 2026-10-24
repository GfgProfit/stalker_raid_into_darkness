using System.Collections.Generic;
using UnityEditor;

namespace XRay.Importer.Editor
{
    public enum XRayTextureRole
    {
        Albedo,
        Normal,
        Packed
    }

    public sealed class XRayTextureImportRequest
    {
        public XRayTextureImportRequest(XRayTextureRole role, XRayAssetMarker marker)
        {
            Role = role;
            Marker = marker;
        }

        public XRayTextureRole Role { get; }
        public XRayAssetMarker Marker { get; }
        public bool WasImported { get; set; }
        public bool ResetSettings { get; set; }
    }

    public static class XRayTextureImportQueue
    {
        private const int MAX_TEXTURE_SIZE = 8192;

        private static readonly Dictionary<string, XRayTextureImportRequest> _requests = new Dictionary<string, XRayTextureImportRequest>();

        public static void Enqueue(string assetPath, XRayTextureImportRequest request)
        {
            _requests[assetPath] = request;
        }

        public static bool TryPeek(string assetPath, out XRayTextureImportRequest request)
        {
            return _requests.TryGetValue(assetPath, out request);
        }

        public static void Complete(string assetPath)
        {
            _requests.Remove(assetPath);
        }

        public static void Apply(AssetImporter importer, string assetPath)
        {
            if (!_requests.TryGetValue(assetPath, out XRayTextureImportRequest request))
            {
                return;
            }

            request.WasImported = true;
            importer.userData = request.Marker.Serialize();
            if (!importer.importSettingsMissing && !request.ResetSettings)
            {
                return;
            }

            if (importer is TextureImporter textureImporter)
            {
                textureImporter.textureShape = TextureImporterShape.Texture2D;
                textureImporter.textureType = request.Role == XRayTextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                textureImporter.sRGBTexture = request.Role == XRayTextureRole.Albedo;
                textureImporter.alphaSource = request.Role != XRayTextureRole.Normal ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                textureImporter.mipmapEnabled = true;
                textureImporter.maxTextureSize = MAX_TEXTURE_SIZE;
                textureImporter.textureCompression = request.Role == XRayTextureRole.Packed ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            }
            else if (importer is IHVImageFormatImporter ddsImporter)
            {
                ddsImporter.isReadable = false;
                if (request.Role == XRayTextureRole.Packed)
                {
                    var serialized = new SerializedObject(ddsImporter);
                    serialized.FindProperty("m_sRGBTexture").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
