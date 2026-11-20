using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Dds;

namespace XRay.Importer.Editor
{
    public enum XRayTextureRole
    {
        Albedo,
        Normal,
        Packed,
        Flora
    }

    public sealed class XRayTextureImportRequest
    {
        public XRayTextureImportRequest(XRayTextureRole role, XRayAssetMarker marker, DdsHeader header)
        {
            Role = role;
            Marker = marker;
            Header = header;
        }

        public XRayTextureRole Role { get; }
        public XRayAssetMarker Marker { get; }
        public DdsHeader Header { get; }
        public bool WasImported { get; set; }
        public bool ResetSettings { get; set; }
        public int? Anisotropy { get; set; }
        public float? MipBias { get; set; }
    }

    public static class XRayTextureImportQueue
    {
        private const int MAX_TEXTURE_SIZE = 2048;

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
            if (!_requests.TryGetValue(assetPath, out XRayTextureImportRequest request) || !(importer is TextureImporter textureImporter))
            {
                return;
            }

            request.WasImported = true;
            importer.userData = request.Role == XRayTextureRole.Flora ? XRayAuthoredSampler.SerializeMarker(request.Marker, XRayAuthoredSampler.ReadRecord(importer) ?? XRayAuthoredSampler.Record(request.Anisotropy ?? 1, request.MipBias ?? 0.0f)) : request.Marker.Serialize();
            if (importer.importSettingsMissing || request.ResetSettings)
            {
                textureImporter.textureShape = TextureImporterShape.Texture2D;
                textureImporter.textureType = request.Role == XRayTextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                textureImporter.sRGBTexture = request.Role == XRayTextureRole.Albedo;
                textureImporter.alphaSource = request.Role == XRayTextureRole.Normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                textureImporter.alphaIsTransparency = false;
                textureImporter.textureCompression = request.Role == XRayTextureRole.Packed ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
                ConfigureSourceFormat(textureImporter, request);
                if (request.Role == XRayTextureRole.Flora)
                {
                    textureImporter.filterMode = FilterMode.Trilinear;
                    textureImporter.anisoLevel = request.Anisotropy ?? 1;
                    textureImporter.mipMapBias = request.MipBias ?? 0.0f;
                    textureImporter.mipMapsPreserveCoverage = true;
                    textureImporter.alphaTestReferenceValue = Levels.XRayFloraSourceBinding.AREF;
                }
            }
            else
            {
                RestorePngAlpha(textureImporter, true);
            }

            ApplyPngMipPolicy(textureImporter, request.Header.Width, request.Header.Height);
        }

        public static bool RestorePngAlpha(TextureImporter importer, bool sourceBeingImported = false)
        {
            if (importer == null || importer.alphaSource != TextureImporterAlphaSource.FromInput)
            {
                return false;
            }

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            if (!standalone.overridden || standalone.format != TextureImporterFormat.DXT1 || (!sourceBeingImported && !importer.DoesSourceTextureHaveAlpha()))
            {
                return false;
            }

            standalone.format = TextureImporterFormat.Automatic;
            importer.SetPlatformTextureSettings(standalone);
            return true;
        }

        public static bool ApplyPngMipPolicy(TextureImporter importer, int sourceWidth, int sourceHeight)
        {
            if (!NeedsPngMipPolicy(importer, sourceWidth, sourceHeight))
            {
                return false;
            }

            int size = Mathf.Max(sourceWidth, sourceHeight);
            importer.mipmapEnabled = true;
            importer.ignoreMipmapLimit = false;
            importer.ignorePngGamma = true;
            importer.npotScale = PngScale(sourceWidth, sourceHeight);
            importer.maxTextureSize = SizeLimit(importer.maxTextureSize, size);

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            if (standalone.overridden && standalone.maxTextureSize != SizeLimit(standalone.maxTextureSize, size))
            {
                standalone.maxTextureSize = SizeLimit(standalone.maxTextureSize, size);
                importer.SetPlatformTextureSettings(standalone);
            }

            return true;
        }

        internal static bool NeedsPngMipPolicy(TextureImporter importer, int sourceWidth, int sourceHeight)
        {
            int size = Mathf.Max(sourceWidth, sourceHeight);
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            return !importer.mipmapEnabled || importer.ignoreMipmapLimit || !importer.ignorePngGamma || importer.npotScale != PngScale(sourceWidth, sourceHeight) || importer.maxTextureSize != SizeLimit(importer.maxTextureSize, size) || (standalone.overridden && standalone.maxTextureSize != SizeLimit(standalone.maxTextureSize, size));
        }

        private static int SizeLimit(int current, int sourceSize)
        {
            return sourceSize > MAX_TEXTURE_SIZE ? MAX_TEXTURE_SIZE : Mathf.Max(current, Mathf.NextPowerOfTwo(sourceSize));
        }

        private static TextureImporterNPOTScale PngScale(int width, int height)
        {
            if (Mathf.Max(width, height) > MAX_TEXTURE_SIZE && (!Mathf.IsPowerOfTwo(width) || !Mathf.IsPowerOfTwo(height)))
            {
                return TextureImporterNPOTScale.ToNearest;
            }

            return TextureImporterNPOTScale.None;
        }

        private static void ConfigureSourceFormat(TextureImporter importer, XRayTextureImportRequest request)
        {
            if (request.Role == XRayTextureRole.Normal)
            {
                return;
            }

            TextureImporterFormat format;
            switch (request.Header.Format)
            {
                case DdsFormat.BC1:
                    format = TextureImporterFormat.Automatic;
                    break;
                case DdsFormat.BC3:
                    format = TextureImporterFormat.DXT5;
                    break;
                case DdsFormat.BC7:
                    format = TextureImporterFormat.BC7;
                    break;
                default:
                    return;
            }

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.format = format;
            standalone.textureCompression = TextureImporterCompression.Compressed;
            standalone.crunchedCompression = false;
            standalone.compressionQuality = 100;
            importer.SetPlatformTextureSettings(standalone);
        }
    }
}
