using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    internal static class XRayLevelHomSource
    {
        internal const string FILE_NAME = "level.hom";
        internal const string CAPTURE_PATH = "/Occlusion/LevelHomSource.bytes";

        private const int MAX_SOURCE_BYTES = 16 * 1024 * 1024;
        private const string ARCHIVE_SEPARATOR = " :: ";

        internal static bool IsHomPath(string path)
        {
            return path != null && path.EndsWith("/" + FILE_NAME, StringComparison.OrdinalIgnoreCase);
        }

        internal static XRayHomTriangle[] Read(string levelFolder, XRayLevelImportRecord record, out string signature, out string diagnostic)
        {
            signature = null;
            diagnostic = null;
            if (record == null || !record.Completed || string.IsNullOrEmpty(record.LevelName)) return Array.Empty<XRayHomTriangle>();
            string path = "levels/" + record.LevelName.ToLowerInvariant() + "/" + FILE_NAME;
            XRayLevelSourceFile source = record.Source.Find(path);
            if (source == null) return Array.Empty<XRayHomTriangle>();
            if (string.IsNullOrEmpty(source.Location) || string.IsNullOrEmpty(source.ContentHash) || source.Size <= 0 || source.Size > MAX_SOURCE_BYTES)
            {
                diagnostic = "level.hom has incomplete or unsupported source provenance.";
                return Array.Empty<XRayHomTriangle>();
            }

            try
            {
                if (record.Source.SourceType == XRayLevelSourceManifest.SOURCE_TYPE_FOLDER && !IsOriginalFolderSource(record.Source.SourceFolder, record.LevelName, source.Location)) return Array.Empty<XRayHomTriangle>();
                string capture = levelFolder + CAPTURE_PATH;
                byte[] bytes = ReadVerifiedBytes(capture, source);
                bool backfill = bytes == null;
                if (backfill) bytes = source.Kind == XRayAssetSourceKind.Archive.ToString() ? ReadArchive(record.Source.FsGame, source, path) : ReadLoose(source);
                if (bytes == null || bytes.Length != source.Size)
                {
                    diagnostic = "level.hom no longer matches the imported level's source record.";
                    return Array.Empty<XRayHomTriangle>();
                }

                XRayHomTriangle[] triangles = XRayHom.Read(bytes, source.Location);
                if (backfill)
                {
                    XRayLevelPaths.EnsureFolder(Path.GetDirectoryName(capture).Replace('\\', '/'));
                    File.WriteAllBytes(capture, bytes);
                    AssetDatabase.ImportAsset(capture, ImportAssetOptions.ForceSynchronousImport);
                }
                signature = source.Location + "\n" + Hash(bytes);
                return triangles;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is XRayFormatException || exception is XRaySourceException)
            {
                diagnostic = "level.hom could not be used: " + exception.Message;
                return Array.Empty<XRayHomTriangle>();
            }
        }

        private static byte[] ReadArchive(string fsGame, XRayLevelSourceFile source, string path)
        {
            int separator = source.Location.LastIndexOf(ARCHIVE_SEPARATOR, StringComparison.Ordinal);
            if (separator <= 0 || !XRayVirtualPath.TryNormalizeSafe(source.Location.Substring(separator + ARCHIVE_SEPARATOR.Length), out string storedPath) || storedPath != path) return null;
            string archivePath = source.Location.Substring(0, separator);
            if (!XRayFileSystem.IsArchive(archivePath) || !File.Exists(archivePath)) return null;
            XRayFsGame layout = File.Exists(fsGame) ? XRayFsGame.Parse(File.ReadAllBytes(fsGame), fsGame) : XRayFsGame.CreateDefault();
            XRayArchive archive = XRayArchive.Open(archivePath, layout);
            if (!archive.TryGet(path, out XRayArchiveEntry entry) || entry.SizeReal != source.Size || entry.SizeCompressed <= 0 || entry.SizeCompressed > MAX_SOURCE_BYTES || source.ContentHash != "crc32:" + entry.Crc.ToString("x8")) return null;
            return archive.Read(entry);
        }

        private static byte[] ReadLoose(XRayLevelSourceFile source)
        {
            return string.Equals(Path.GetFileName(source.Location), FILE_NAME, StringComparison.OrdinalIgnoreCase) ? ReadVerifiedBytes(source.Location, source) : null;
        }

        private static bool IsOriginalFolderSource(string folder, string level, string location)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(location)) return false;
            string expected = Path.GetFullPath(Path.Combine(folder, "levels", level, FILE_NAME));
            return string.Equals(expected, Path.GetFullPath(location), StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] ReadVerifiedBytes(string path, XRayLevelSourceFile source)
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length != source.Size) return null;
            using var reader = new BinaryReader(stream);
            byte[] bytes = reader.ReadBytes((int)source.Size);
            if (bytes.Length != source.Size) return null;
            if (source.Kind == XRayAssetSourceKind.Archive.ToString()) return source.ContentHash == "crc32:" + XRayCompression.Crc32(bytes, 0, bytes.Length).ToString("x8") ? bytes : null;
            return string.Equals(source.ContentHash, Hash(bytes), StringComparison.OrdinalIgnoreCase) ? bytes : null;
        }

        private static string Hash(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return "sha256:" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
