using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace XRay.Importer.Sources.Vfs
{
    public sealed class XRayIndexCandidate
    {
        public XRayIndexCandidate(int sourceIndex, string relativePath) : this(sourceIndex, relativePath, -1, 0)
        {
        }

        public XRayIndexCandidate(int sourceIndex, string relativePath, long size, long modifiedTicks)
        {
            SourceIndex = sourceIndex;
            RelativePath = relativePath;
            Size = size;
            ModifiedTicks = modifiedTicks;
        }

        public int SourceIndex { get; }
        public string RelativePath { get; }
        public long Size { get; }
        public long ModifiedTicks { get; }
    }

    public sealed class XRayIndexedFile
    {
        public XRayIndexedFile(string virtualPath)
        {
            VirtualPath = virtualPath;
        }

        public string VirtualPath { get; }
        public List<XRayIndexCandidate> Candidates { get; } = new List<XRayIndexCandidate>();
        public bool IsAmbiguous { get; internal set; }
        public XRayIndexCandidate Winner => Candidates[0];
        public bool HasConflict => Candidates.Count > 1;
    }

    public sealed class XRayAssetIndexStatistics
    {
        public int SourceCount { get; internal set; }
        public int ActiveModCount { get; internal set; }
        public int ContributingSourceCount { get; internal set; }
        public int FileCount { get; internal set; }
        public int ConflictCount { get; internal set; }
        public int AmbiguousCount { get; internal set; }
        public int ArchiveSourceCount { get; internal set; }
        public int LoadedArchiveCount { get; internal set; }
        public int ArchiveEntryCount { get; internal set; }
        public long BuildMilliseconds { get; internal set; }
        public long BuiltAtUtcTicks { get; internal set; }
    }

    public sealed class XRayAssetIndex
    {
        private const int FORMAT_VERSION = 2;
        private const string MAGIC = "XRAYIDX";
        private const string GAMEDATA_FOLDER = "gamedata";
        private const int ARCHIVE_SEARCH_DEPTH = 3;
        private const int CANCEL_CHECK_INTERVAL = 512;

        private readonly Dictionary<string, XRayIndexedFile> _files = new Dictionary<string, XRayIndexedFile>(StringComparer.Ordinal);
        private readonly Dictionary<int, XRayArchive> _archives = new Dictionary<int, XRayArchive>();
        private XRayFsGame _fsGame;

        public string Fingerprint { get; private set; }
        public string ProfileName { get; private set; }
        public string FsGamePath { get; private set; }
        public string ExtractionFolder { get; set; }
        public XRayAssetIndexStatistics Statistics { get; } = new XRayAssetIndexStatistics();
        public List<XRayAssetSource> Sources { get; } = new List<XRayAssetSource>();
        public List<string> ArchiveSources { get; } = new List<string>();
        public List<string> UnreadableArchives { get; } = new List<string>();
        public List<string> ShadowedArchives { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public int Count => _files.Count;
        public IEnumerable<XRayIndexedFile> Files => _files.Values;

        public bool TryGet(string virtualPath, out XRayIndexedFile file)
        {
            return _files.TryGetValue(XRayVirtualPath.Normalize(virtualPath), out file);
        }

        public XRayAssetSource GetSource(XRayIndexCandidate candidate)
        {
            return Sources[candidate.SourceIndex];
        }

        public string GetFullPath(XRayIndexCandidate candidate)
        {
            XRayAssetSource source = Sources[candidate.SourceIndex];
            return source.IsArchive ? Materialize(candidate) : source.GetFullPath(candidate.RelativePath);
        }

        public string Describe(XRayIndexCandidate candidate)
        {
            return Sources[candidate.SourceIndex].Describe(candidate.RelativePath);
        }

        public string Materialize(XRayIndexCandidate candidate)
        {
            XRayAssetSource source = Sources[candidate.SourceIndex];
            if (!source.IsArchive)
            {
                return source.GetFullPath(candidate.RelativePath);
            }

            XRayArchive archive = GetArchive(candidate);
            if (!archive.TryGet(candidate.RelativePath, out XRayArchiveEntry entry))
            {
                throw new XRaySourceException($"'{candidate.RelativePath}' is no longer present in archive '{archive.Path}'. Rebuild the virtual index.");
            }

            string root = string.IsNullOrEmpty(ExtractionFolder) ? Path.Combine(Path.GetTempPath(), "XRayArchiveCache") : ExtractionFolder;
            string folder = Path.Combine(root, XRayFileSystem.Hash(archive.Path + "|" + archive.Length + "|" + archive.ModifiedTicks));
            string target = XRayVirtualPath.ToFileSystem(folder, entry.VirtualPath);
            var existing = new FileInfo(target);
            if (existing.Exists && existing.Length == entry.SizeReal)
            {
                using (FileStream stream = File.OpenRead(target))
                {
                    if (XRayCompression.Crc32(stream) == entry.Crc)
                    {
                        return target;
                    }
                }
            }

            byte[] data = archive.Read(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temporary = target + ".tmp";
            File.WriteAllBytes(temporary, data);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(temporary, target);
            File.SetLastWriteTimeUtc(target, new DateTime(archive.ModifiedTicks, DateTimeKind.Utc));
            return target;
        }

        public Stream OpenRead(XRayIndexCandidate candidate)
        {
            XRayAssetSource source = Sources[candidate.SourceIndex];
            if (!source.IsArchive)
            {
                string path = source.GetFullPath(candidate.RelativePath);
                if (!File.Exists(path))
                {
                    throw new XRaySourceException($"'{path}' ({source.Name}) was removed after indexing. Rebuild the virtual index.");
                }

                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            }

            XRayArchive archive = GetArchive(candidate);
            if (!archive.TryGet(candidate.RelativePath, out XRayArchiveEntry entry))
            {
                throw new XRaySourceException($"'{candidate.RelativePath}' is no longer present in archive '{archive.Path}'. Rebuild the virtual index.");
            }

            return archive.OpenWindow(entry);
        }

        public byte[] ReadAllBytes(XRayIndexCandidate candidate)
        {
            XRayAssetSource source = Sources[candidate.SourceIndex];
            if (!source.IsArchive)
            {
                using (Stream stream = OpenRead(candidate))
                {
                    var data = new byte[stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int count = stream.Read(data, read, data.Length - read);
                        if (count <= 0)
                        {
                            throw new XRaySourceException($"'{source.GetFullPath(candidate.RelativePath)}' is truncated.");
                        }

                        read += count;
                    }

                    return data;
                }
            }

            XRayArchive archive = GetArchive(candidate);
            if (!archive.TryGet(candidate.RelativePath, out XRayArchiveEntry entry))
            {
                throw new XRaySourceException($"'{candidate.RelativePath}' is no longer present in archive '{archive.Path}'. Rebuild the virtual index.");
            }

            return archive.Read(entry);
        }

        public XRayArchive GetArchive(XRayIndexCandidate candidate)
        {
            XRayAssetSource source = Sources[candidate.SourceIndex];
            XRayArchive archive;
            lock (_archives)
            {
                if (!_archives.TryGetValue(candidate.SourceIndex, out archive))
                {
                    if (_fsGame == null)
                    {
                        _fsGame = LoadFsGame(FsGamePath);
                    }

                    archive = XRayArchive.Open(source.GamedataRoot, _fsGame);
                    _archives[candidate.SourceIndex] = archive;
                }
            }

            if (candidate.ModifiedTicks != 0 && archive.ModifiedTicks != candidate.ModifiedTicks)
            {
                throw new XRaySourceException($"Archive '{archive.Path}' changed after indexing. Rebuild the virtual index.");
            }

            return archive;
        }

        public static XRayAssetIndex Build(XRaySourcePlan plan)
        {
            return Build(plan, CancellationToken.None, null);
        }

        public static XRayAssetIndex Build(XRaySourcePlan plan, CancellationToken cancellation, Action<float, string> progress)
        {
            var stopwatch = Stopwatch.StartNew();
            var index = new XRayAssetIndex { Fingerprint = plan.Fingerprint, ProfileName = plan.ProfileName, FsGamePath = plan.FsGamePath, _fsGame = plan.FsGame };
            index.Sources.AddRange(plan.Sources);
            index.Warnings.AddRange(plan.Warnings);
            index.ShadowedArchives.AddRange(plan.ShadowedArchives);
            bool indexArchives = plan.Configuration.Mode == XRaySourceMode.Mo2Effective;
            var contributing = new HashSet<int>();
            for (int sourceIndex = 0; sourceIndex < plan.Sources.Count; sourceIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                XRayAssetSource source = plan.Sources[sourceIndex];
                progress?.Invoke((float)sourceIndex / Math.Max(1, plan.Sources.Count), source.Name);
                if (source.IsArchive)
                {
                    index.AddArchive(sourceIndex, source, plan, contributing);
                    continue;
                }

                foreach (string scope in plan.Configuration.IndexScope)
                {
                    if (Path.HasExtension(scope))
                    {
                        string file = XRayFileSystem.FindChildFile(source.GamedataRoot, scope);
                        if (file != null)
                        {
                            var info = new FileInfo(file);
                            if (index.Add(sourceIndex, Path.GetFileName(file), info.Length, info.LastWriteTimeUtc.Ticks))
                            {
                                contributing.Add(sourceIndex);
                            }
                        }

                        continue;
                    }

                    string directory = XRayFileSystem.FindChildDirectory(source.GamedataRoot, scope);
                    if (directory == null)
                    {
                        continue;
                    }

                    int rootLength = source.GamedataRoot.TrimEnd('\\', '/').Length + 1;
                    int counter = 0;
                    foreach (FileInfo file in XRayFileSystem.EnumerateFileInfosSafe(directory, plan.SkipDirectories, index.Warnings))
                    {
                        if (++counter % CANCEL_CHECK_INTERVAL == 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                        }

                        if (HasSkippedSuffix(file.FullName, plan.SkipFileSuffixes))
                        {
                            continue;
                        }

                        if (indexArchives && XRayFileSystem.IsArchive(file.FullName))
                        {
                            index.Warnings.Add($"'{file.FullName}' ({source.Name}) is an archive inside gamedata; the engine loads it while scanning gamedata, the importer does not index its content.");
                        }

                        if (index.Add(sourceIndex, file.FullName.Substring(rootLength), file.Length, file.LastWriteTimeUtc.Ticks))
                        {
                            contributing.Add(sourceIndex);
                        }
                    }
                }

                if (!indexArchives)
                {
                    int archives = CountSourceArchives(source);
                    if (archives > 0)
                    {
                        index.ArchiveSources.Add($"{source.Name} ({archives} archive files)");
                    }
                }
            }

            index.Statistics.SourceCount = plan.Sources.Count;
            index.Statistics.ActiveModCount = plan.ActiveModCount;
            index.Statistics.ContributingSourceCount = contributing.Count;
            index.Statistics.ArchiveSourceCount = indexArchives ? CountArchiveSources(index.Sources) : index.ArchiveSources.Count;
            index.CountFiles();
            index.Statistics.BuildMilliseconds = stopwatch.ElapsedMilliseconds;
            index.Statistics.BuiltAtUtcTicks = DateTime.UtcNow.Ticks;
            progress?.Invoke(1.0f, "Done");
            return index;
        }

        public void Save(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = path + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
            {
                writer.Write(MAGIC);
                writer.Write(FORMAT_VERSION);
                writer.Write(Fingerprint ?? string.Empty);
                writer.Write(ProfileName ?? string.Empty);
                writer.Write(FsGamePath ?? string.Empty);
                writer.Write(Statistics.ActiveModCount);
                writer.Write(Statistics.ContributingSourceCount);
                writer.Write(Statistics.LoadedArchiveCount);
                writer.Write(Statistics.ArchiveEntryCount);
                writer.Write(Statistics.ArchiveSourceCount);
                writer.Write(Statistics.BuildMilliseconds);
                writer.Write(Statistics.BuiltAtUtcTicks);
                WriteStrings(writer, Warnings);
                WriteStrings(writer, ArchiveSources);
                WriteStrings(writer, UnreadableArchives);
                WriteStrings(writer, ShadowedArchives);
                writer.Write(Sources.Count);
                foreach (XRayAssetSource source in Sources)
                {
                    writer.Write(source.Name);
                    writer.Write((int)source.Kind);
                    writer.Write(source.Priority);
                    writer.Write(source.GamedataRoot);
                    writer.Write(source.ContainerFolder ?? string.Empty);
                    writer.Write(source.Origin ?? string.Empty);
                }

                writer.Write(_files.Count);
                foreach (XRayIndexedFile file in _files.Values)
                {
                    writer.Write(file.VirtualPath);
                    writer.Write(file.IsAmbiguous);
                    writer.Write(file.Candidates.Count);
                    foreach (XRayIndexCandidate candidate in file.Candidates)
                    {
                        writer.Write(candidate.SourceIndex);
                        writer.Write(candidate.RelativePath);
                        writer.Write(candidate.Size);
                        writer.Write(candidate.ModifiedTicks);
                    }
                }
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        public static XRayAssetIndex Load(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using (var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8))
                {
                    if (reader.ReadString() != MAGIC || reader.ReadInt32() != FORMAT_VERSION)
                    {
                        return null;
                    }

                    var index = new XRayAssetIndex { Fingerprint = reader.ReadString(), ProfileName = reader.ReadString(), FsGamePath = NullIfEmpty(reader.ReadString()) };
                    index.Statistics.ActiveModCount = reader.ReadInt32();
                    index.Statistics.ContributingSourceCount = reader.ReadInt32();
                    index.Statistics.LoadedArchiveCount = reader.ReadInt32();
                    index.Statistics.ArchiveEntryCount = reader.ReadInt32();
                    index.Statistics.ArchiveSourceCount = reader.ReadInt32();
                    index.Statistics.BuildMilliseconds = reader.ReadInt64();
                    index.Statistics.BuiltAtUtcTicks = reader.ReadInt64();
                    ReadStrings(reader, index.Warnings);
                    ReadStrings(reader, index.ArchiveSources);
                    ReadStrings(reader, index.UnreadableArchives);
                    ReadStrings(reader, index.ShadowedArchives);
                    int sourceCount = reader.ReadInt32();
                    for (int i = 0; i < sourceCount; i++)
                    {
                        string name = reader.ReadString();
                        var kind = (XRayAssetSourceKind)reader.ReadInt32();
                        int priority = reader.ReadInt32();
                        string root = reader.ReadString();
                        string container = reader.ReadString();
                        string origin = reader.ReadString();
                        index.Sources.Add(new XRayAssetSource(name, kind, priority, root, container, origin));
                    }

                    int fileCount = reader.ReadInt32();
                    for (int i = 0; i < fileCount; i++)
                    {
                        var file = new XRayIndexedFile(reader.ReadString()) { IsAmbiguous = reader.ReadBoolean() };
                        int candidateCount = reader.ReadInt32();
                        for (int c = 0; c < candidateCount; c++)
                        {
                            file.Candidates.Add(new XRayIndexCandidate(reader.ReadInt32(), reader.ReadString(), reader.ReadInt64(), reader.ReadInt64()));
                        }

                        index._files[file.VirtualPath] = file;
                    }

                    index.Statistics.SourceCount = index.Sources.Count;
                    index.CountFiles();
                    return index;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is EndOfStreamException || exception is FormatException)
            {
                return null;
            }
        }

        private void AddArchive(int sourceIndex, XRayAssetSource source, XRaySourcePlan plan, HashSet<int> contributing)
        {
            XRayArchive archive;
            try
            {
                archive = XRayArchive.Open(source.GamedataRoot, plan.FsGame);
            }
            catch (Exception exception) when (exception is XRaySourceException || exception is IOException || exception is UnauthorizedAccessException)
            {
                UnreadableArchives.Add($"{source.Name}: {exception.Message}");
                Warnings.Add($"Archive '{source.GamedataRoot}' ({source.Origin}) cannot be read and is not indexed: {exception.Message}");
                return;
            }

            _archives[sourceIndex] = archive;
            Warnings.AddRange(archive.Warnings);
            if (!archive.AutoLoad)
            {
                Warnings.Add($"Archive '{source.GamedataRoot}' has auto_load = false; the engine does not load it without -auto_load_arch, so it is not indexed.");
                return;
            }

            Statistics.LoadedArchiveCount++;
            foreach (XRayArchiveEntry entry in archive.Entries)
            {
                if (!InScope(entry.VirtualPath, plan.Configuration.IndexScope))
                {
                    continue;
                }

                Statistics.ArchiveEntryCount++;
                if (Add(sourceIndex, entry.VirtualPath, entry.SizeReal, archive.ModifiedTicks))
                {
                    contributing.Add(sourceIndex);
                }
            }
        }

        private void CountFiles()
        {
            Statistics.FileCount = _files.Count;
            Statistics.ConflictCount = 0;
            Statistics.AmbiguousCount = 0;
            foreach (XRayIndexedFile file in _files.Values)
            {
                if (file.HasConflict)
                {
                    Statistics.ConflictCount++;
                }

                if (file.IsAmbiguous)
                {
                    Statistics.AmbiguousCount++;
                }
            }
        }

        private bool Add(int sourceIndex, string relativePath, long size, long modifiedTicks)
        {
            string key = XRayVirtualPath.Normalize(relativePath);
            if (!_files.TryGetValue(key, out XRayIndexedFile file))
            {
                file = new XRayIndexedFile(key);
                _files.Add(key, file);
            }

            XRayIndexCandidate last = file.Candidates.Count > 0 ? file.Candidates[file.Candidates.Count - 1] : null;
            if (last != null && last.SourceIndex == sourceIndex)
            {
                file.IsAmbiguous = true;
                if (string.CompareOrdinal(relativePath, last.RelativePath) < 0)
                {
                    file.Candidates[file.Candidates.Count - 1] = new XRayIndexCandidate(sourceIndex, relativePath, size, modifiedTicks);
                }

                return false;
            }

            file.Candidates.Add(new XRayIndexCandidate(sourceIndex, relativePath, size, modifiedTicks));
            return true;
        }

        private static bool InScope(string virtualPath, List<string> scopes)
        {
            foreach (string scope in scopes)
            {
                string normalized = XRayVirtualPath.Normalize(scope);
                if (virtualPath == normalized || (virtualPath.Length > normalized.Length && virtualPath[normalized.Length] == '/' && virtualPath.StartsWith(normalized, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static XRayFsGame LoadFsGame(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return XRayFsGame.CreateDefault();
            }

            return XRayFsGame.Parse(File.ReadAllBytes(path), path);
        }

        private static int CountArchiveSources(List<XRayAssetSource> sources)
        {
            int count = 0;
            foreach (XRayAssetSource source in sources)
            {
                if (source.IsArchive)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountSourceArchives(XRayAssetSource source)
        {
            if (source.Kind == XRayAssetSourceKind.BaseAnomaly || source.Kind == XRayAssetSourceKind.Custom)
            {
                return CountArchivesShallow(source.GamedataRoot);
            }

            if (string.IsNullOrEmpty(source.ContainerFolder) || !Directory.Exists(source.ContainerFolder) || source.Kind == XRayAssetSourceKind.GameGamedata)
            {
                return 0;
            }

            int count = CountArchivesShallow(source.ContainerFolder);
            foreach (string child in Directory.GetDirectories(source.ContainerFolder))
            {
                if (!string.Equals(Path.GetFileName(child), GAMEDATA_FOLDER, StringComparison.OrdinalIgnoreCase))
                {
                    count += XRayFileSystem.CountArchives(child, ARCHIVE_SEARCH_DEPTH - 1);
                }
            }

            return count;
        }

        private static int CountArchivesShallow(string folder)
        {
            return XRayFileSystem.CountArchives(folder, 0);
        }

        private static bool HasSkippedSuffix(string file, List<string> suffixes)
        {
            foreach (string suffix in suffixes)
            {
                if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static void WriteStrings(BinaryWriter writer, List<string> values)
        {
            writer.Write(values.Count);
            foreach (string value in values)
            {
                writer.Write(value ?? string.Empty);
            }
        }

        private static void ReadStrings(BinaryReader reader, List<string> values)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                values.Add(reader.ReadString());
            }
        }
    }
}
