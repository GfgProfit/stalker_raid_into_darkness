using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Textures;

namespace XRay.Importer.Editor
{
    public sealed class XRayTextureAssetResult
    {
        public string AssetPath { get; set; }
        public XRayResourceStatus Status { get; set; }
        public string Format { get; set; }
        public string Note { get; set; }
    }

    public sealed class XRayTextureImporter
    {
        public const string ALBEDO_KEY_SUFFIX = "#albedo";
        public const string NORMAL_KEY_SUFFIX = "#normal";
        public const string NORMAL_NAME_SUFFIX = "_normal";

        private readonly XRayImportOptions _options;
        private readonly bool _forceUpdate;
        private readonly HashSet<string> _claimed;
        private readonly List<string> _pendingImports = new List<string>();

        public XRayTextureImporter(XRayImportOptions options, bool forceUpdate, HashSet<string> claimed)
        {
            _options = options;
            _forceUpdate = forceUpdate;
            _claimed = claimed;
        }

        public IReadOnlyList<string> PendingImports => _pendingImports;

        public XRayTextureAssetResult ImportAlbedo(XRayResolvedFile file, string folder)
        {
            if (!file.IsResolved)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Missing };
            }

            byte[] source;
            DdsHeader header;
            try
            {
                source = File.ReadAllBytes(file.Winner.FullPath);
                header = DdsHeader.Parse(source);
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Note = $"Cannot read '{file.Winner.FullPath}': {exception.Message}" };
            }

            string stamp = XRayAssetMarker.CreateStamp(file.Winner.FullPath);
            var marker = new XRayAssetMarker(file.VirtualPath + ALBEDO_KEY_SUFFIX, XRayTextureRole.Albedo.ToString(), file.Winner.SourceName, file.Winner.FullPath, stamp);
            if (IsUnityNative(header))
            {
                return Store(marker, file.Winner.RelativePath, string.Empty, ".dds", folder, XRayTextureRole.Albedo, header.Description, () => ToSrgbDds(source, header), null);
            }

            if (!DdsDecoder.CanDecode(header))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = $"DDS format {header.Description} is neither supported by Unity nor decodable by the importer." };
            }

            return Store(marker, file.Winner.RelativePath, string.Empty, ".png", folder, XRayTextureRole.Albedo, header.Description, () => EncodePng(DdsDecoder.DecodeTopLevel(source, header), header.Width, header.Height, false), $"{header.Description} is not imported by Unity; converted to PNG.");
        }

        public XRayTextureAssetResult ImportNormal(XRayResolvedFile bump, XRayResolvedFile correction, string folder)
        {
            if (bump == null)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Skipped, Note = "No bump map is declared for this texture." };
            }

            if (!bump.IsResolved)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Missing };
            }

            byte[] bumpData;
            DdsHeader bumpHeader;
            try
            {
                bumpData = File.ReadAllBytes(bump.Winner.FullPath);
                bumpHeader = DdsHeader.Parse(bumpData);
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Note = $"Cannot read '{bump.Winner.FullPath}': {exception.Message}" };
            }

            if (!DdsDecoder.CanDecode(bumpHeader))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = bumpHeader.Description, Note = $"Bump map format {bumpHeader.Description} cannot be decoded; the material uses no normal map." };
            }

            bool useCorrection = correction != null && correction.IsResolved;
            string stamp = XRayAssetMarker.CreateStamp(bump.Winner.FullPath) + (useCorrection ? "|" + XRayAssetMarker.CreateStamp(correction.Winner.FullPath) : string.Empty) + (_options.FlipNormalGreen ? "|flipY" : string.Empty);
            var marker = new XRayAssetMarker(bump.VirtualPath + NORMAL_KEY_SUFFIX, XRayTextureRole.Normal.ToString(), bump.Winner.SourceName, bump.Winner.FullPath, stamp);
            var producer = new NormalMapProducer(bumpData, bumpHeader, useCorrection ? correction : null, _options.FlipNormalGreen);
            string note = useCorrection ? "Converted from X-Ray bump + bump# (normal = bump.agb + bump#.rgb - 1, bump# sampled with the same UV)." : "Converted from X-Ray bump (normal = bump.agb); bump# was not found.";
            XRayTextureAssetResult result = Store(marker, bump.Winner.RelativePath, NORMAL_NAME_SUFFIX, ".png", folder, XRayTextureRole.Normal, bumpHeader.Description, producer.Produce, note);
            if (producer.Note != null)
            {
                result.Note = producer.Note;
            }

            return result;
        }

        public void ImportPending()
        {
            foreach (string path in _pendingImports)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        public static bool IsUnityNative(DdsHeader header)
        {
            if (header.IsCubemap || header.IsVolume || header.ArraySize > 1)
            {
                return false;
            }

            // Unity rejects DDS files whose mip chain is neither single-level nor complete.
            if (header.MipCount > 1 && header.MipCount != GetFullMipCount(header.Width, header.Height))
            {
                return false;
            }

            switch (header.Format)
            {
                case DdsFormat.BC1:
                case DdsFormat.BC3:
                case DdsFormat.BC4:
                case DdsFormat.BC5:
                case DdsFormat.BC6H:
                case DdsFormat.BC7:
                    return true;
                default:
                    return false;
            }
        }

        private static int GetFullMipCount(int width, int height)
        {
            int size = Math.Max(width, height);
            int count = 1;
            while (size > 1)
            {
                size >>= 1;
                count++;
            }

            return count;
        }

        private XRayTextureAssetResult Store(XRayAssetMarker marker, string relativePath, string nameSuffix, string extension, string folder, XRayTextureRole role, string format, Func<byte[]> produce, string note)
        {
            string assetPath = XRayAssetNaming.Place(folder, XRayAssetNaming.CreateCandidates(relativePath, nameSuffix), extension, marker.Key, XRayAssetMarker.ReadKey, _claimed);
            var result = new XRayTextureAssetResult { AssetPath = assetPath, Format = format, Note = note };
            bool exists = File.Exists(assetPath);
            if (exists)
            {
                XRayAssetMarker existing = XRayAssetMarker.ReadEffective(assetPath);
                bool sameSource = existing != null && existing.Stamp == marker.Stamp && existing.SourcePath == marker.SourcePath;
                if (sameSource || (!_options.OverwriteExistingTextures && !_forceUpdate))
                {
                    result.Status = XRayResourceStatus.Reused;
                    if (!sameSource)
                    {
                        result.Note = $"Source changed ({existing?.Source ?? "unknown"} -> {marker.Source}); kept the existing texture because 'Overwrite Existing Textures' is off.";
                    }

                    return result;
                }
            }

            byte[] bytes;
            try
            {
                bytes = produce();
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException || exception is NotSupportedException || exception is ArgumentException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = format, Note = exception.Message };
            }

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(assetPath, bytes);
            XRayTextureImportQueue.Enqueue(assetPath, new XRayTextureImportRequest(role, marker));
            _pendingImports.Add(assetPath);
            result.Status = exists ? XRayResourceStatus.Updated : extension == ".png" ? XRayResourceStatus.Converted : XRayResourceStatus.Imported;
            return result;
        }

        internal static byte[] ToSrgbDds(byte[] source, DdsHeader header)
        {
            if (header.HasDx10Header && header.IsSrgb)
            {
                return source;
            }

            return DdsColorSpace.TryGetDxgiFormat(header.Format, true, out uint dxgi) ? DdsColorSpace.WithDx10Header(source, header, dxgi) : source;
        }

        private sealed class NormalMapProducer
        {
            private readonly byte[] _bumpData;
            private readonly DdsHeader _bumpHeader;
            private readonly XRayResolvedFile _correction;
            private readonly bool _flipGreen;

            public NormalMapProducer(byte[] bumpData, DdsHeader bumpHeader, XRayResolvedFile correction, bool flipGreen)
            {
                _bumpData = bumpData;
                _bumpHeader = bumpHeader;
                _correction = correction;
                _flipGreen = flipGreen;
            }

            public string Note { get; private set; }

            public byte[] Produce()
            {
                byte[] bumpPixels = DdsDecoder.DecodeTopLevel(_bumpData, _bumpHeader);
                byte[] correctionPixels = null;
                int correctionWidth = 0;
                int correctionHeight = 0;
                if (_correction != null)
                {
                    byte[] data = File.ReadAllBytes(_correction.Winner.FullPath);
                    DdsHeader header = DdsHeader.Parse(data);
                    if (DdsDecoder.CanDecode(header))
                    {
                        correctionPixels = DdsDecoder.DecodeTopLevel(data, header);
                        correctionWidth = header.Width;
                        correctionHeight = header.Height;
                    }
                    else
                    {
                        Note = $"bump# '{_correction.DisplayPath}' ({header.Description}) cannot be decoded and was ignored.";
                    }
                }

                byte[] normal = XRayBumpConverter.ToTangentNormalMap(bumpPixels, _bumpHeader.Width, _bumpHeader.Height, correctionPixels, correctionWidth, correctionHeight, _flipGreen);
                return EncodePng(normal, _bumpHeader.Width, _bumpHeader.Height, true);
            }
        }

        internal static byte[] EncodePng(byte[] topDownRgba, int width, int height, bool linear)
        {
            var bottomUp = new byte[topDownRgba.Length];
            int stride = width * 4;
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(topDownRgba, y * stride, bottomUp, (height - 1 - y) * stride, stride);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            try
            {
                texture.SetPixelData(bottomUp, 0);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
