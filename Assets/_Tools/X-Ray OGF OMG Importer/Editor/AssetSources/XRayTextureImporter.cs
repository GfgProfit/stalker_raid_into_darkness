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
            if (!DdsDecoder.CanDecode(header))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = $"DDS format {header.Description} cannot be represented by the RGBA PNG importer." };
            }

            return Store(marker, file.Winner.RelativePath, string.Empty, folder, XRayTextureRole.Albedo, header, () => EncodeVerifiedPng(source, header, false), "DDS mip 0 verified as RGBA PNG; Unity generates the mip chain.");
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
            XRayTextureAssetResult result = Store(marker, bump.Winner.RelativePath, NORMAL_NAME_SUFFIX, folder, XRayTextureRole.Normal, bumpHeader, producer.Produce, note);
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
            if (!DdsDecoder.CanDecode(header))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = $"Packed DDS format {header.Description} cannot be represented by the RGBA PNG importer." };
            }

            return Store(marker, file.Winner.RelativePath, "_packed", folder, XRayTextureRole.Packed, header, () => EncodeVerifiedPng(source, header, true), "Packed linear DDS mip 0 verified as RGBA PNG; Unity generates the mip chain.");
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

            if (!DdsDecoder.CanDecode(header) || header.Width > DdsMipChain.MAX_DIMENSION || header.Height > DdsMipChain.MAX_DIMENSION || header.MipCount > DdsMipChain.FullChainLength(header.Width, header.Height) || source.Length - header.DataOffset < GetMipPayloadSize(header))
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = "Flora texture is unsupported or its declared DDS mip chain is invalid." };
            }

            var marker = new XRayAssetMarker(file.VirtualPath + AUTHORED_KEY_SUFFIX + ContentQualifier(source), XRayTextureRole.Flora.ToString(), file.Winner.SourceName, file.Winner.Location, file.Winner.Stamp);
            List<string> candidates = XRayAssetNaming.CreateCandidates(file.Winner.RelativePath, AUTHORED_NAME_SUFFIX);
            string pngPath = FindOwnedPng(folder, candidates, marker.Key);
            string previousPath = pngPath ?? FindOwnedNativeTexture(folder, candidates, marker.Key);
            XRayAuthoredSamplerPlan sampler = previousPath == null ? null : XRayAuthoredSampler.Plan(previousPath, anisotropy, mipBias);
            if (pngPath == null && previousPath != null)
            {
                XRayAssetMarker previous = XRayAssetMarker.ReadEffective(previousPath);
                bool sameSource = previous != null && previous.Stamp == marker.Stamp && previous.SourcePath == marker.SourcePath;
                if (sameSource || (!_options.OverwriteExistingTextures && !_forceUpdate))
                {
                    _claimed?.Add(previousPath);
                    bool updated = XRayAuthoredSampler.Apply(sampler) && sampler.ChangesTexture;
                    return new XRayTextureAssetResult { AssetPath = previousPath, Status = updated ? XRayResourceStatus.Updated : XRayResourceStatus.Reused, Format = header.Description, Note = "Reused the existing native flora texture with its authored mip chain. " + sampler.Note };
                }
            }

            int finalAnisotropy = sampler != null && sampler.Eligible ? sampler.FinalAnisotropy : anisotropy;
            float finalMipBias = sampler != null && sampler.Eligible ? sampler.FinalMipBias : mipBias;
            XRayTextureAssetResult result = Store(marker, file.Winner.RelativePath, AUTHORED_NAME_SUFFIX, folder, XRayTextureRole.Flora, header, () => EncodeVerifiedPng(source, header, true), "Flora DDS mip 0 verified as linear RGBA PNG; Unity generates alpha-coverage-preserving mips.", finalAnisotropy, finalMipBias);
            if (pngPath != null && result.Status != XRayResourceStatus.Unsupported && XRayAuthoredSampler.Apply(sampler) && sampler.ChangesTexture && result.Status == XRayResourceStatus.Reused)
            {
                result.Status = XRayResourceStatus.Updated;
            }

            if (sampler != null && sampler.Eligible)
            {
                result.Note += " " + sampler.Note;
            }

            return result;
        }

        private string FindOwnedNativeTexture(string folder, List<string> candidates, string key)
        {
            foreach (string candidate in candidates)
            {
                string path = folder + "/" + candidate + ".asset";
                if ((_claimed == null || !_claimed.Contains(path)) && XRayAssetMarker.ReadKey(path) == key)
                {
                    return path;
                }
            }

            return null;
        }

        internal string WeatherKey(string virtualPath, byte[] source)
        {
            return virtualPath + "#weather-unorm" + ContentQualifier(source);
        }

        public XRayTextureAssetResult ImportWeather(XRayResolvedFile file, string folder, bool cubemap)
        {
            if (file == null || !file.IsResolved) return new XRayTextureAssetResult { Status = XRayResourceStatus.Missing };
            byte[] source = file.Winner.ReadAllBytes();
            DdsHeader header = DdsHeader.Parse(source);
            GraphicsFormat format = WeatherFormat(header.Format);
            if (header.IsCubemap != cubemap || header.IsVolume || header.ArraySize != 1 || cubemap && header.Width != header.Height || header.Width > DdsMipChain.MAX_DIMENSION || header.Height > DdsMipChain.MAX_DIMENSION || header.MipCount > DdsMipChain.FullChainLength(header.Width, header.Height) || format == GraphicsFormat.None) return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = "Weather textures require a supported 2D texture or a complete square cubemap." };
            if (cubemap && !header.HasDx10Header && (BitConverter.ToUInt32(source, 112) & 0xFC00) != 0xFC00) return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = "Weather cubemap is missing one or more DDS faces." };
            long payloadSize = GetMipPayloadSize(header) * (cubemap ? 6 : 1);
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
            var marker = new XRayAssetMarker(WeatherKey(file.VirtualPath, source), XRayTextureRole.Packed.ToString(), file.Winner.SourceName, file.Winner.Location, file.Winner.Stamp);
            List<string> candidates = XRayAssetNaming.CreateCandidates(file.Winner.RelativePath, "_weather");
            string retainedNative = FindOwnedNativeTexture(folder, candidates, marker.Key);
            if (!cubemap && header.Format != DdsFormat.RgbaHalf)
            {
                if (retainedNative != null && FindOwnedPng(folder, candidates, marker.Key) == null)
                {
                    XRayAssetMarker previous = XRayAssetMarker.ReadEffective(retainedNative);
                    if ((previous != null && previous.Stamp == marker.Stamp && previous.SourcePath == marker.SourcePath) || (!_options.OverwriteExistingTextures && !_forceUpdate))
                    {
                        _claimed?.Add(retainedNative);
                        return new XRayTextureAssetResult { AssetPath = retainedNative, Status = XRayResourceStatus.Reused, Format = header.Description, Note = "Preserved the existing native weather texture and authored mip chain." + trailingNote };
                    }
                }

                XRayTextureAssetResult result = Store(marker, file.Winner.RelativePath, "_weather", folder, XRayTextureRole.Packed, header, () => EncodeVerifiedPng(source, header, true), "Weather DDS mip 0 verified as linear RGBA PNG; Unity generates the mip chain." + trailingNote);
                ImportPendingBatched();
                return result;
            }

            string assetPath = retainedNative ?? XRayAssetNaming.Place(folder, candidates, ".asset", marker.Key, XRayAssetMarker.ReadKey, _claimed);
            _claimed?.Add(assetPath);
            Texture existing = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (existing != null)
            {
                XRayAssetMarker previous = XRayAssetMarker.ReadEffective(assetPath);
                if ((previous != null && previous.Stamp == marker.Stamp && previous.SourcePath == marker.SourcePath) || (!_options.OverwriteExistingTextures && !_forceUpdate))
                {
                    return new XRayTextureAssetResult { AssetPath = assetPath, Status = XRayResourceStatus.Reused, Format = header.Description, Note = "Preserved the existing weather texture and authored mip chain." + trailingNote };
                }
            }

            List<byte[]> levels = cubemap ? null : ReadUncompressedWeatherLevels(source, header);
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
                        int width = header.Width;
                        int height = header.Height;
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
                string note = cubemap ? "DDS faces and authored mips preserved in native D3D face order." : "HDR authored DDS mips preserved with a vertical flip.";
                note += trailingNote;
                return new XRayTextureAssetResult { AssetPath = assetPath, Status = existing != null ? XRayResourceStatus.Updated : XRayResourceStatus.Imported, Format = header.Description, Note = note };
            }
            catch
            {
                if (texture != null && !AssetDatabase.Contains(texture)) UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        private static long GetMipPayloadSize(DdsHeader header)
        {
            long bytes = 0;
            for (int mip = 0; mip < header.MipCount; mip++)
            {
                bytes += DdsHeader.GetLevelSize(header.Format, Math.Max(1, header.Width >> mip), Math.Max(1, header.Height >> mip));
            }

            return bytes;
        }

        private static List<byte[]> ReadUncompressedWeatherLevels(byte[] source, DdsHeader header)
        {
            var levels = new List<byte[]>(header.MipCount);
            int offset = header.DataOffset;
            int width = header.Width;
            int height = header.Height;
            int sourcePixelSize = DdsHeader.GetBytesPerPixel(header.Format);
            int targetPixelSize = header.Format == DdsFormat.Bgr24 ? 4 : sourcePixelSize;
            for (int mip = 0; mip < header.MipCount; mip++)
            {
                int sourcePitch = width * sourcePixelSize;
                int targetPitch = width * targetPixelSize;
                var level = new byte[targetPitch * height];
                for (int row = 0; row < height; row++)
                {
                    int from = offset + row * sourcePitch;
                    int to = (height - row - 1) * targetPitch;
                    if (header.Format == DdsFormat.Bgr24)
                    {
                        for (int pixel = 0; pixel < width; pixel++, from += 3, to += 4)
                        {
                            level[to] = source[from + 2];
                            level[to + 1] = source[from + 1];
                            level[to + 2] = source[from];
                            level[to + 3] = 255;
                        }
                    }
                    else
                    {
                        Buffer.BlockCopy(source, from, level, to, targetPitch);
                        if (header.Format == DdsFormat.Bgrx32) for (int pixel = to + 3; pixel < to + targetPitch; pixel += 4) level[pixel] = 255;
                    }
                }
                levels.Add(level);
                offset += sourcePitch * height;
                width = Math.Max(1, width >> 1);
                height = Math.Max(1, height >> 1);
            }
            return levels;
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
            string path = FindOwnedPng(folder, XRayAssetNaming.CreateCandidates(file.Winner.RelativePath, "_packed"), key);
            if (path == null)
            {
                return null;
            }

            XRayAssetMarker marker = XRayAssetMarker.ReadEffective(path);
            upToDate = marker != null && marker.Stamp == file.Winner.Stamp && marker.SourcePath == file.Winner.Location;
            return path;
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

        private XRayTextureAssetResult Store(XRayAssetMarker marker, string relativePath, string nameSuffix, string folder, XRayTextureRole role, DdsHeader header, Func<byte[]> produce, string note, int? anisotropy = null, float? mipBias = null)
        {
            const string EXTENSION = ".png";
            List<string> candidates = XRayAssetNaming.CreateCandidates(relativePath, nameSuffix);
            string retainedPng = FindOwnedPng(folder, candidates, marker.Key);
            if (retainedPng != null)
            {
                _claimed?.Add(retainedPng);
            }

            string assetPath = retainedPng ?? XRayAssetNaming.Place(folder, candidates, EXTENSION, marker.Key, XRayAssetMarker.ReadKey, _claimed);
            string previousPath = null;
            bool exists = File.Exists(assetPath);
            var importer = exists ? AssetImporter.GetAtPath(assetPath) as TextureImporter : null;
            bool invalidShape = importer != null && importer.textureShape != TextureImporterShape.Texture2D;
            var result = new XRayTextureAssetResult { AssetPath = assetPath, Format = header.Description, Note = note };
            if (exists)
            {
                XRayAssetMarker existing = XRayAssetMarker.ReadEffective(assetPath);
                bool sameSource = existing != null && existing.Stamp == marker.Stamp && existing.SourcePath == marker.SourcePath;
                if (!invalidShape && (sameSource || (!_options.OverwriteExistingTextures && !_forceUpdate)))
                {
                    result.Status = XRayResourceStatus.Reused;
                    if (XRayTextureImportQueue.UpgradeItemAlbedoCompression(importer, role) || XRayTextureImportQueue.RestorePngAlpha(importer))
                    {
                        AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                        SchedulePngImport(assetPath);
                        result.Status = XRayResourceStatus.Updated;
                    }

                    if (!sameSource)
                    {
                        result.Note = $"Source changed ({existing?.Source ?? "unknown"} -> {marker.Source}); kept the existing texture because 'Overwrite Existing Textures' is off.";
                    }
                    else if (importer != null && XRayTextureImportQueue.NeedsPngMipPolicy(importer, header.Width, header.Height))
                    {
                        QueuePng(assetPath, role, marker, header, false, anisotropy, mipBias);
                        result.Status = XRayResourceStatus.Updated;
                    }

                    return result;
                }
            }
            else if (role != XRayTextureRole.Normal)
            {
                foreach (string candidate in candidates)
                {
                    string legacy = folder + "/" + candidate + ".dds";
                    if (File.Exists(legacy) && XRayAssetMarker.ReadKey(legacy) == marker.Key)
                    {
                        previousPath = legacy;
                        break;
                    }
                }
            }

            byte[] bytes;
            try
            {
                if (previousPath != null && !_options.OverwriteExistingTextures && !_forceUpdate)
                {
                    XRayAssetMarker previous = XRayAssetMarker.ReadEffective(previousPath);
                    if (previous != null && (previous.Stamp != marker.Stamp || previous.SourcePath != marker.SourcePath))
                    {
                        marker = previous;
                        byte[] previousBytes = File.ReadAllBytes(previousPath);
                        header = DdsHeader.Parse(previousBytes);
                        bytes = EncodeVerifiedPng(previousBytes, header, role != XRayTextureRole.Albedo);
                    }
                    else
                    {
                        bytes = produce();
                    }
                }
                else
                {
                    bytes = produce();
                }
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException || exception is NotSupportedException || exception is ArgumentException || exception is XRaySourceException || exception is UnityException)
            {
                return new XRayTextureAssetResult { Status = XRayResourceStatus.Unsupported, Format = header.Description, Note = exception.Message };
            }

            Directory.CreateDirectory(folder);
            string staged = assetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool moved = false;
            bool installed = false;
            if (previousPath != null)
            {
                AssetDatabase.StartAssetEditing();
            }

            try
            {
                File.WriteAllBytes(staged, bytes);
                if (previousPath != null)
                {
                    string error = AssetDatabase.MoveAsset(previousPath, assetPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        throw new XRaySourceException($"Cannot preserve the texture GUID while moving '{previousPath}' to '{assetPath}': {error}");
                    }

                    moved = true;
                }

                if (exists || moved)
                {
                    AssetDatabase.ReleaseCachedFileHandles();
                    File.Replace(staged, assetPath, null);
                }
                else
                {
                    File.Move(staged, assetPath);
                }

                installed = true;
                QueuePng(assetPath, role, marker, header, moved || invalidShape, anisotropy, mipBias);
            }
            finally
            {
                try
                {
                    if (File.Exists(staged))
                    {
                        File.Delete(staged);
                    }

                    if (moved && !installed)
                    {
                        string error = AssetDatabase.MoveAsset(assetPath, previousPath);
                        if (!string.IsNullOrEmpty(error))
                        {
                            Debug.LogError($"Cannot restore texture '{previousPath}' after PNG migration failed: {error}");
                        }
                    }
                }
                finally
                {
                    if (previousPath != null)
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                }
            }

            result.Status = exists || moved ? XRayResourceStatus.Updated : XRayResourceStatus.Converted;
            return result;
        }

        private void QueuePng(string path, XRayTextureRole role, XRayAssetMarker marker, DdsHeader header, bool reset, int? anisotropy, float? mipBias)
        {
            XRayTextureImportQueue.Enqueue(path, new XRayTextureImportRequest(role, marker, header) { ResetSettings = reset, Anisotropy = anisotropy, MipBias = mipBias });
            SchedulePngImport(path);
        }

        private void SchedulePngImport(string path)
        {
            if (!_pendingImports.Contains(path))
            {
                _pendingImports.Add(path);
            }
        }


        private string FindOwnedPng(string folder, List<string> candidates, string key)
        {
            string existingPath = null;
            foreach (string candidate in candidates)
            {
                string path = folder + "/" + candidate + ".png";
                if (XRayAssetMarker.ReadKey(path) != key)
                {
                    path = folder + "/" + candidate + "_dds_source.png";
                    if (XRayAssetMarker.ReadKey(path) != key)
                    {
                        continue;
                    }
                }

                if (_claimed == null || !_claimed.Contains(path))
                {
                    return path;
                }

                existingPath ??= path;
            }

            return existingPath;
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
                return EncodeVerifiedRgba(normal, _bumpHeader.Width, _bumpHeader.Height, true);
            }
        }

        public static byte[] EncodeVerifiedPng(byte[] source, DdsHeader header, bool linear)
        {
            byte[] rgba = DdsDecoder.DecodePngTopLevel(source, header);
            return EncodeVerifiedRgba(rgba, header.Width, header.Height, linear);
        }

        private static byte[] EncodeVerifiedRgba(byte[] topDownRgba, int width, int height, bool linear)
        {
            byte[] png = EncodePng(topDownRgba, width, height, linear);
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!decoded.LoadImage(png) || decoded.width != width || decoded.height != height)
                {
                    throw new FormatException("PNG round trip changed the decoded DDS dimensions.");
                }

                Color32[] pixels = decoded.GetPixels32();
                int stride = width * 4;
                for (int y = 0; y < height; y++)
                {
                    int input = y * stride;
                    int output = (height - 1 - y) * width;
                    for (int x = 0; x < width; x++, input += 4)
                    {
                        Color32 pixel = pixels[output + x];
                        if (pixel.r != topDownRgba[input] || pixel.g != topDownRgba[input + 1] || pixel.b != topDownRgba[input + 2] || pixel.a != topDownRgba[input + 3])
                        {
                            throw new FormatException($"PNG round trip changed decoded RGBA at ({x}, {y}).");
                        }
                    }
                }

                return png;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decoded);
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
