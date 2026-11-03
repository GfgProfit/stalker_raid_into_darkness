using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        public const string AUTHORED_KEY_SUFFIX = "#authored";
        public const string AUTHORED_NAME_SUFFIX = "_authored";

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
        public bool ContentKeys { get; set; }

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
                source = file.Winner.ReadAllBytes();
                header = DdsHeader.Parse(source);
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException || exception is XRaySourceException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Note = $"Cannot read '{file.Winner.Location}': {exception.Message}" };
            }

            string stamp = file.Winner.Stamp;
            var marker = new XRayAssetMarker(file.VirtualPath + ALBEDO_KEY_SUFFIX + ContentQualifier(source), XRayTextureRole.Albedo.ToString(), file.Winner.SourceName, file.Winner.Location, stamp);
            if (IsUnityNative(header) && HasExactPayload(source, header))
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
                bumpData = bump.Winner.ReadAllBytes();
                bumpHeader = DdsHeader.Parse(bumpData);
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException || exception is XRaySourceException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Note = $"Cannot read '{bump.Winner.Location}': {exception.Message}" };
            }

            if (!DdsDecoder.CanDecode(bumpHeader))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = bumpHeader.Description, Note = $"Bump map format {bumpHeader.Description} cannot be decoded; the material uses no normal map." };
            }

            bool useCorrection = correction != null && correction.IsResolved;
            string stamp = bump.Winner.Stamp + (useCorrection ? "|" + correction.Winner.Stamp : string.Empty) + (_options.FlipNormalGreen ? "|flipY" : string.Empty);
            var marker = new XRayAssetMarker(bump.VirtualPath + NORMAL_KEY_SUFFIX, XRayTextureRole.Normal.ToString(), bump.Winner.SourceName, bump.Winner.Location, stamp);
            var producer = new NormalMapProducer(bumpData, bumpHeader, useCorrection ? correction : null, _options.FlipNormalGreen);
            string note = useCorrection ? "Converted from X-Ray bump + bump# (normal = bump.agb + bump#.rgb - 1, bump# sampled with the same UV)." : "Converted from X-Ray bump (normal = bump.agb); bump# was not found.";
            XRayTextureAssetResult result = Store(marker, bump.Winner.RelativePath, NORMAL_NAME_SUFFIX, ".png", folder, XRayTextureRole.Normal, bumpHeader.Description, producer.Produce, note);
            if (producer.Note != null)
            {
                result.Note = producer.Note;
            }

            return result;
        }

        public void ImportPendingBatched()
        {
            if (_pendingImports.Count == 0)
            {
                return;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in _pendingImports)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            _pendingImports.Clear();
        }

        private string ContentQualifier(byte[] source)
        {
            if (!ContentKeys)
            {
                return string.Empty;
            }

            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(source);
                return "@" + BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public void ImportPending()
        {
            foreach (string path in _pendingImports)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        public XRayTextureAssetResult ImportPacked(XRayResolvedFile file, string folder)
        {
            if (file == null || !file.IsResolved)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Missing };
            }

            byte[] source;
            DdsHeader header;
            try
            {
                source = file.Winner.ReadAllBytes();
                header = DdsHeader.Parse(source);
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException || exception is XRaySourceException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Note = $"Cannot read packed texture '{file.Winner.Location}': {exception.Message}" };
            }
            var marker = new XRayAssetMarker(file.VirtualPath + "#packed" + ContentQualifier(source), XRayTextureRole.Packed.ToString(), file.Winner.SourceName, file.Winner.Location, file.Winner.Stamp);
            if (IsUnityNative(header) && HasExactPayload(source, header))
            {
                return EnsurePackedLinear(Store(marker, file.Winner.RelativePath, "_packed", ".dds", folder, XRayTextureRole.Packed, header.Description, () => DdsColorSpace.TryGetDxgiFormat(header.Format, false, out uint dxgi) ? DdsColorSpace.WithDx10Header(source, header, dxgi) : source, "Packed linear X-Ray channels; no normal conversion."));
            }

            if (!DdsDecoder.CanDecode(header))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Note = "Packed DDS format cannot be decoded." };
            }

            return Store(marker, file.Winner.RelativePath, "_packed", ".png", folder, XRayTextureRole.Packed, header.Description, () => EncodePng(DdsDecoder.DecodeTopLevel(source, header), header.Width, header.Height, true), "Packed mip0 converted losslessly to PNG; Unity generates remaining mips.");
        }

        public XRayTextureAssetResult ImportAuthoredMips(XRayResolvedFile file, string folder, int anisotropy, float mipBias)
        {
            if (file == null || !file.IsResolved)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Missing };
            }

            byte[] source;
            DdsHeader header;
            try
            {
                source = file.Winner.ReadAllBytes();
                header = DdsHeader.Parse(source);
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException || exception is XRaySourceException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Note = $"Cannot read '{file.Winner.Location}': {exception.Message}" };
            }

            if (IsUnityNative(header) && HasExactPayload(source, header))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Skipped, Format = header.Description, Note = "The DDS is imported natively with its own mip chain; the albedo texture already keeps the authored mips." };
            }

            if (!DdsMipChain.TryReadFlipped(source, header, out List<byte[]> levels, out string reason))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = "Authored mip chain cannot be preserved: " + reason };
            }

            var marker = new XRayAssetMarker(file.VirtualPath + AUTHORED_KEY_SUFFIX + ContentQualifier(source), XRayTextureRole.Albedo.ToString(), file.Winner.SourceName, file.Winner.Location, file.Winner.Stamp);
            string assetPath = XRayAssetNaming.Place(folder, XRayAssetNaming.CreateCandidates(file.Winner.RelativePath, AUTHORED_NAME_SUFFIX), ".asset", marker.Key, XRayAssetMarker.ReadKey, _claimed);
            string note = $"Authored {levels.Count}-level {header.Description} mip chain kept block-exact (vertical block flip, no recompression, UNorm sampling of the encoded values).";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            XRayAuthoredSamplerPlan sampler = null;
            if (existing != null)
            {
                XRayAssetMarker previous = XRayAssetMarker.ReadEffective(assetPath);
                bool sameSource = previous != null && previous.Stamp == marker.Stamp && previous.SourcePath == marker.SourcePath;
                sampler = XRayAuthoredSampler.Plan(assetPath, anisotropy, mipBias);
                if (sameSource || !_options.OverwriteExistingTextures && !_forceUpdate)
                {
                    bool refreshed = XRayAuthoredSampler.Apply(sampler) && sampler.ChangesTexture;
                    string kept = sameSource ? note : $"Source changed ({previous?.Source ?? "unknown"} -> {marker.Source}); kept the existing authored texture because 'Overwrite Existing Textures' is off.";
                    return new XRayTextureAssetResult { AssetPath = assetPath, Status = refreshed ? XRayResourceStatus.Updated : XRayResourceStatus.Reused, Format = header.Description, Note = refreshed || sampler.Overrides.Count > 0 ? kept + " " + sampler.Note : kept };
                }
            }

            GraphicsFormat format = header.Format == DdsFormat.BC1 ? GraphicsFormat.RGBA_DXT1_UNorm : header.Format == DdsFormat.BC2 ? GraphicsFormat.RGBA_DXT3_UNorm : GraphicsFormat.RGBA_DXT5_UNorm;
            int finalAnisotropy = sampler != null && sampler.Eligible ? sampler.FinalAnisotropy : anisotropy;
            float finalMipBias = sampler != null && sampler.Eligible ? sampler.FinalMipBias : mipBias;
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(header.Width, header.Height, format, levels.Count, TextureCreationFlags.None) { name = Path.GetFileNameWithoutExtension(assetPath), filterMode = FilterMode.Trilinear, anisoLevel = finalAnisotropy, mipMapBias = finalMipBias, wrapMode = TextureWrapMode.Repeat };
                for (int mip = 0; mip < levels.Count; mip++)
                {
                    texture.SetPixelData(levels[mip], mip);
                }

                texture.Apply(false, false);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is UnityException)
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = "Authored mip chain cannot be preserved: " + exception.Message };
            }

            Directory.CreateDirectory(folder);
            if (existing != null)
            {
                EditorUtility.CopySerialized(texture, existing);
                UnityEngine.Object.DestroyImmediate(texture);
                texture = existing;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(texture, assetPath);
            }

            var serialized = new SerializedObject(texture);
            SerializedProperty readable = serialized.FindProperty("m_IsReadable");
            if (readable != null && readable.boolValue)
            {
                readable.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssetIfDirty(texture);
            XRayAuthoredSampler.WriteMarker(assetPath, marker, sampler != null && sampler.Eligible ? sampler.FinalRecord : XRayAuthoredSampler.Record(anisotropy, mipBias));
            return new XRayTextureAssetResult { AssetPath = assetPath, Status = existing != null ? XRayResourceStatus.Updated : XRayResourceStatus.Imported, Format = header.Description, Note = sampler != null && sampler.Overrides.Count > 0 ? note + " " + sampler.Note : note };
        }

        public XRayTextureAssetResult ImportWeather(XRayResolvedFile file, string folder, bool cubemap)
        {
            if (file == null || !file.IsResolved) return new XRayTextureAssetResult { Status = XRayResourceStatus.Missing };
            byte[] source = file.Winner.ReadAllBytes();
            DdsHeader header = DdsHeader.Parse(source);
            GraphicsFormat format = WeatherFormat(header.Format);
            if (header.IsCubemap != cubemap || header.IsVolume || header.ArraySize != 1 || cubemap && header.Width != header.Height || header.Width > DdsMipChain.MAX_DIMENSION || header.Height > DdsMipChain.MAX_DIMENSION || header.MipCount > DdsMipChain.FullChainLength(header.Width, header.Height) || format == GraphicsFormat.None) return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = "Weather textures require a supported 2D texture or a complete square cubemap." };
            if (cubemap && !header.HasDx10Header && (BitConverter.ToUInt32(source, 112) & 0xFC00) != 0xFC00) return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = "Weather cubemap is missing one or more DDS faces." };
            int faceSize = 0;
            int width = header.Width;
            int height = header.Height;
            for (int mip = 0; mip < header.MipCount; mip++)
            {
                faceSize = checked(faceSize + DdsHeader.GetLevelSize(header.Format, width, height));
                width = Math.Max(1, width >> 1);
                height = Math.Max(1, height >> 1);
            }
            long payloadSize = (long)faceSize * (cubemap ? 6 : 1);
            long trailingBytes = source.Length - header.DataOffset - payloadSize;
            if (trailingBytes < 0) return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = "Weather DDS payload is shorter than its authored face/mip layout." };
            string trailingNote = string.Empty;
            if (trailingBytes > 0)
            {
                using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                {
                    string tailHash = BitConverter.ToString(hash.ComputeHash(source, (int)(header.DataOffset + payloadSize), (int)trailingBytes)).Replace("-", string.Empty).ToLowerInvariant();
                    trailingNote = " Header-described payload=" + payloadSize + " bytes; trailing source bytes outside the declared surfaces=" + trailingBytes + "; tailSHA256=" + tailHash + ".";
                }
            }
            List<byte[]> levels = null;
            if (!cubemap && !DdsMipChain.TryReadFlipped(source, header, out levels, out string reason)) return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = reason };
            var marker = new XRayAssetMarker(file.VirtualPath + "#weather-unorm" + ContentQualifier(source), XRayTextureRole.Packed.ToString(), file.Winner.SourceName, file.Winner.Location, file.Winner.Stamp);
            string assetPath = XRayAssetNaming.Place(folder, XRayAssetNaming.CreateCandidates(file.Winner.RelativePath, "_weather"), ".asset", marker.Key, XRayAssetMarker.ReadKey, _claimed);
            Texture existing = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (existing != null)
            {
                XRayAssetMarker previous = XRayAssetMarker.ReadEffective(assetPath);
                if (previous != null && previous.Stamp == marker.Stamp && previous.SourcePath == marker.SourcePath || !_options.OverwriteExistingTextures && !_forceUpdate) return new XRayTextureAssetResult { AssetPath = assetPath, Status = XRayResourceStatus.Reused, Format = header.Description, Note = "Preserved the existing weather texture and authored mip chain." + trailingNote };
            }
            Texture texture = null;
            try
            {
                if (cubemap)
                {
                    var cube = new Cubemap(header.Width, format, TextureCreationFlags.None, header.MipCount);
                    texture = cube;
                    int offset = header.DataOffset;
                    for (int face = 0; face < 6; face++)
                    {
                        width = header.Width;
                        height = header.Height;
                        for (int mip = 0; mip < header.MipCount; mip++)
                        {
                            if (header.Format == DdsFormat.Bgr24)
                            {
                                var rgba = new byte[width * height * 4];
                                for (int pixel = 0; pixel < width * height; pixel++)
                                {
                                    rgba[pixel * 4] = source[offset + pixel * 3 + 2];
                                    rgba[pixel * 4 + 1] = source[offset + pixel * 3 + 1];
                                    rgba[pixel * 4 + 2] = source[offset + pixel * 3];
                                    rgba[pixel * 4 + 3] = 255;
                                }
                                cube.SetPixelData(rgba, mip, (CubemapFace)face);
                            }
                            else cube.SetPixelData(source, mip, (CubemapFace)face, offset);
                            offset += DdsHeader.GetLevelSize(header.Format, width, height);
                            width = Math.Max(1, width >> 1);
                            height = Math.Max(1, height >> 1);
                        }
                    }
                    cube.Apply(false, false);
                }
                else
                {
                    var image = new Texture2D(header.Width, header.Height, format, header.MipCount, TextureCreationFlags.None);
                    texture = image;
                    for (int mip = 0; mip < levels.Count; mip++) image.SetPixelData(levels[mip], mip);
                    image.Apply(false, false);
                }
                texture.name = Path.GetFileNameWithoutExtension(assetPath);
                texture.filterMode = cubemap ? FilterMode.Bilinear : FilterMode.Trilinear;
                texture.wrapMode = cubemap ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                texture.anisoLevel = 1;
                Directory.CreateDirectory(folder);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(texture, existing);
                    UnityEngine.Object.DestroyImmediate(texture);
                    texture = existing;
                    EditorUtility.SetDirty(texture);
                }
                else AssetDatabase.CreateAsset(texture, assetPath);
                var serialized = new SerializedObject(texture);
                SerializedProperty readable = serialized.FindProperty("m_IsReadable");
                if (readable != null)
                {
                    readable.boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.SaveAssetIfDirty(texture);
                XRayAssetMarker.Write(assetPath, marker);
                string note = cubemap ? "UNorm DDS faces and authored mips preserved in native D3D face order." : "UNorm authored DDS mips preserved with the existing block-exact vertical flip.";
                note += trailingNote;
                return new XRayTextureAssetResult { AssetPath = assetPath, Status = existing != null ? XRayResourceStatus.Updated : XRayResourceStatus.Imported, Format = header.Description, Note = note };
            }
            catch
            {
                if (texture != null && !AssetDatabase.Contains(texture)) UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        private static GraphicsFormat WeatherFormat(DdsFormat format)
        {
            return format switch
            {
                DdsFormat.BC1 => GraphicsFormat.RGBA_DXT1_UNorm,
                DdsFormat.BC2 => GraphicsFormat.RGBA_DXT3_UNorm,
                DdsFormat.BC3 => GraphicsFormat.RGBA_DXT5_UNorm,
                DdsFormat.BC7 => GraphicsFormat.RGBA_BC7_UNorm,
                DdsFormat.Rgba32 => GraphicsFormat.R8G8B8A8_UNorm,
                DdsFormat.Bgra32 => GraphicsFormat.B8G8R8A8_UNorm,
                DdsFormat.Bgrx32 => GraphicsFormat.B8G8R8A8_UNorm,
                DdsFormat.Bgr24 => GraphicsFormat.R8G8B8A8_UNorm,
                DdsFormat.RgbaHalf => GraphicsFormat.R16G16B16A16_SFloat,
                _ => GraphicsFormat.None
            };
        }

        public string FindPacked(XRayResolvedFile file, string folder, out bool upToDate)
        {
            upToDate = false;
            if (file == null || !file.IsResolved)
            {
                return null;
            }

            string key = file.VirtualPath + "#packed" + (ContentKeys ? ContentQualifier(file.Winner.ReadAllBytes()) : string.Empty);
            foreach (string candidate in XRayAssetNaming.CreateCandidates(file.Winner.RelativePath, "_packed"))
            {
                foreach (string extension in new[] { ".dds", ".png" })
                {
                    string path = folder + "/" + candidate + extension;
                    if (XRayAssetMarker.ReadKey(path) != key)
                    {
                        continue;
                    }

                    XRayAssetMarker marker = XRayAssetMarker.ReadEffective(path);
                    upToDate = marker != null && marker.Stamp == file.Winner.Stamp && marker.SourcePath == file.Winner.Location;
                    return path;
                }
            }

            return null;
        }

        private static XRayTextureAssetResult EnsurePackedLinear(XRayTextureAssetResult result)
        {
            if (AssetImporter.GetAtPath(result.AssetPath) is IHVImageFormatImporter importer)
            {
                var serialized = new SerializedObject(importer);
                SerializedProperty srgb = serialized.FindProperty("m_sRGBTexture");
                if (srgb.boolValue)
                {
                    srgb.boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    importer.SaveAndReimport();
                }
            }

            return result;
        }

        public static bool IsUnityNative(DdsHeader header)
        {
            if (header.IsCubemap || header.IsVolume || header.ArraySize > 1)
            {
                return false;
            }

            if (header.IsBlockCompressed && (header.Width % 4 != 0 || header.Height % 4 != 0))
            {
                return false;
            }

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

        public static bool HasExactPayload(byte[] source, DdsHeader header)
        {
            long expected = 0;
            int width = header.Width;
            int height = header.Height;
            for (int mip = 0; mip < Math.Max(1, header.MipCount); mip++)
            {
                expected += DdsHeader.GetLevelSize(header.Format, Math.Max(1, width), Math.Max(1, height));
                width >>= 1;
                height >>= 1;
            }

            return source.Length - header.DataOffset == expected;
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
            List<string> candidates = XRayAssetNaming.CreateCandidates(relativePath, nameSuffix);
            string assetPath = XRayAssetNaming.Place(folder, candidates, extension, marker.Key, XRayAssetMarker.ReadKey, _claimed);
            string previousPath = null;
            if (!ContentKeys && _options.OverwriteExistingTextures && !File.Exists(assetPath))
            {
                string previousExtension = extension == ".dds" ? ".png" : ".dds";
                foreach (string candidate in candidates)
                {
                    string path = folder + "/" + candidate + previousExtension;
                    if (File.Exists(path) && XRayAssetMarker.ReadKey(path) == marker.Key)
                    {
                        previousPath = path;
                        break;
                    }
                }
            }

            var result = new XRayTextureAssetResult { AssetPath = assetPath, Format = format, Note = note };
            bool exists = File.Exists(assetPath);
            bool invalidShape = exists && AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter && textureImporter.textureShape != TextureImporterShape.Texture2D;
            if (exists)
            {
                XRayAssetMarker existing = XRayAssetMarker.ReadEffective(assetPath);
                bool sameSource = existing != null && existing.Stamp == marker.Stamp && existing.SourcePath == marker.SourcePath;
                if (!invalidShape && (sameSource || (!_options.OverwriteExistingTextures && !_forceUpdate)))
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
            catch (Exception exception) when (exception is IOException || exception is FormatException || exception is NotSupportedException || exception is ArgumentException || exception is XRaySourceException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = format, Note = exception.Message };
            }

            Directory.CreateDirectory(folder);
            if (previousPath != null)
            {
                AssetDatabase.StartAssetEditing();
            }

            try
            {
                if (previousPath != null)
                {
                    string error = AssetDatabase.MoveAsset(previousPath, assetPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        throw new XRaySourceException($"Cannot preserve the texture GUID while moving '{previousPath}' to '{assetPath}': {error}");
                    }

                    exists = true;
                }

                if (exists) AssetDatabase.ReleaseCachedFileHandles();
                File.WriteAllBytes(assetPath, bytes);
                XRayTextureImportQueue.Enqueue(assetPath, new XRayTextureImportRequest(role, marker) { ResetSettings = previousPath != null || invalidShape });
                _pendingImports.Add(assetPath);
            }
            finally
            {
                if (previousPath != null)
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

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
                    byte[] data = _correction.Winner.ReadAllBytes();
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
