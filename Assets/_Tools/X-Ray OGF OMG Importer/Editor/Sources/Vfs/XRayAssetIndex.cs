using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace XRay.Importer.Sources.Vfs
{
    public sealed class XRayIndexCandidate
    {
        public XRayIndexCandidate(int sourceIndex, string relativePath)
        {
            SourceIndex = sourceIndex;
            RelativePath = relativePath;
        }

        public int SourceIndex { get; }
        public string RelativePath { get; }
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
        public long BuildMilliseconds { get; internal set; }
        public long BuiltAtUtcTicks { get; internal set; }
    }

    public sealed class XRayAssetIndex
    {
        private const int FORMAT_VERSION = 1;
        private const string MAGIC = "XRAYIDX";
        private const string GAMEDATA_FOLDER = "gamedata";
        private const int ARCHIVE_SEARCH_DEPTH = 3;

        private readonly Dictionary<string, XRayIndexedFile> _files = new Dictionary<string, XRayIndexedFile>(StringComparer.Ordinal);

        public string Fingerprint { get; private set; }
        public string ProfileName { get; private set; }
        public XRayAssetIndexStatistics Statistics { get; } = new XRayAssetIndexStatistics();
        public List<XRayAssetSource> Sources { get; } = new List<XRayAssetSource>();
        public List<string> ArchiveSources { get; } = new List<string>();
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
            return Sources[candidate.SourceIndex].GetFullPath(candidate.RelativePath);
        }

        public static XRayAssetIndex Build(XRaySourcePlan plan)
        {
            var stopwatch = Stopwatch.StartNew();
            var index = new XRayAssetIndex { Fingerprint = plan.Fingerprint, ProfileName = plan.ProfileName };
            index.Sources.AddRange(plan.Sources);
            index.Warnings.AddRange(plan.Warnings);
            var contributing = new HashSet<int>();
            for (int sourceIndex = 0; sourceIndex < plan.Sources.Count; sourceIndex++)
            {
                XRayAssetSource source = plan.Sources[sourceIndex];
                foreach (string scope in plan.Configuration.IndexScope)
                {
                    if (Path.HasExtension(scope))
                    {
                        string file = XRayFileSystem.FindChildFile(source.GamedataRoot, scope);
                        if (file != null && index.Add(sourceIndex, Path.GetFileName(file)))
                        {
                            contributing.Add(sourceIndex);
                        }

                        continue;
                    }

                    string directory = XRayFileSystem.FindChildDirectory(source.GamedataRoot, scope);
                    if (directory == null)
                    {
                        continue;
                    }

                    int rootLength = source.GamedataRoot.TrimEnd('\\', '/').Length + 1;
                    foreach (string file in XRayFileSystem.EnumerateFilesSafe(directory, plan.SkipDirectories, index.Warnings))
                    {
                        if (HasSkippedSuffix(file, plan.SkipFileSuffixes))
                        {
                            continue;
                        }

                        if (index.Add(sourceIndex, file.Substring(rootLength)))
                        {
                            contributing.Add(sourceIndex);
                        }
                    }
                }

                int archives = CountSourceArchives(source);
                if (archives > 0)
                {
                    index.ArchiveSources.Add($"{source.Name} ({archives} archive files)");
                }
            }

            index.Statistics.SourceCount = plan.Sources.Count;
            index.Statistics.ActiveModCount = plan.ActiveModCount;
            index.Statistics.ContributingSourceCount = contributing.Count;
            index.Statistics.ArchiveSourceCount = index.ArchiveSources.Count;
            index.Statistics.FileCount = index._files.Count;
            foreach (XRayIndexedFile file in index._files.Values)
            {
                if (file.HasConflict)
                {
                    index.Statistics.ConflictCount++;
                }

                if (file.IsAmbiguous)
                {
                    index.Statistics.AmbiguousCount++;
                }
            }

            index.Statistics.BuildMilliseconds = stopwatch.ElapsedMilliseconds;
            index.Statistics.BuiltAtUtcTicks = DateTime.UtcNow.Ticks;
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
                writer.Write(Statistics.ActiveModCount);
                writer.Write(Statistics.ContributingSourceCount);
                writer.Write(Statistics.BuildMilliseconds);
                writer.Write(Statistics.BuiltAtUtcTicks);
                WriteStrings(writer, Warnings);
                WriteStrings(writer, ArchiveSources);
                writer.Write(Sources.Count);
                foreach (XRayAssetSource source in Sources)
                {
                    writer.Write(source.Name);
                    writer.Write((int)source.Kind);
                    writer.Write(source.Priority);
                    writer.Write(source.GamedataRoot);
                    writer.Write(source.ContainerFolder ?? string.Empty);
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

                    var index = new XRayAssetIndex { Fingerprint = reader.ReadString(), ProfileName = reader.ReadString() };
                    index.Statistics.ActiveModCount = reader.ReadInt32();
                    index.Statistics.ContributingSourceCount = reader.ReadInt32();
                    index.Statistics.BuildMilliseconds = reader.ReadInt64();
                    index.Statistics.BuiltAtUtcTicks = reader.ReadInt64();
                    ReadStrings(reader, index.Warnings);
                    ReadStrings(reader, index.ArchiveSources);
                    int sourceCount = reader.ReadInt32();
                    for (int i = 0; i < sourceCount; i++)
                    {
                        string name = reader.ReadString();
                        var kind = (XRayAssetSourceKind)reader.ReadInt32();
                        int priority = reader.ReadInt32();
                        string root = reader.ReadString();
                        string container = reader.ReadString();
                        index.Sources.Add(new XRayAssetSource(name, kind, priority, root, container));
                    }

                    int fileCount = reader.ReadInt32();
                    for (int i = 0; i < fileCount; i++)
                    {
                        var file = new XRayIndexedFile(reader.ReadString()) { IsAmbiguous = reader.ReadBoolean() };
                        int candidateCount = reader.ReadInt32();
                        for (int c = 0; c < candidateCount; c++)
                        {
                            file.Candidates.Add(new XRayIndexCandidate(reader.ReadInt32(), reader.ReadString()));
                        }

                        index._files[file.VirtualPath] = file;
                        if (file.HasConflict)
                        {
                            index.Statistics.ConflictCount++;
                        }

                        if (file.IsAmbiguous)
                        {
                            index.Statistics.AmbiguousCount++;
                        }
                    }

                    index.Statistics.SourceCount = index.Sources.Count;
                    index.Statistics.FileCount = index._files.Count;
                    index.Statistics.ArchiveSourceCount = index.ArchiveSources.Count;
                    return index;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is EndOfStreamException || exception is FormatException)
            {
                return null;
            }
        }

        private bool Add(int sourceIndex, string relativePath)
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
                    file.Candidates[file.Candidates.Count - 1] = new XRayIndexCandidate(sourceIndex, relativePath);
                }

                return false;
            }

            file.Candidates.Add(new XRayIndexCandidate(sourceIndex, relativePath));
            return true;
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
