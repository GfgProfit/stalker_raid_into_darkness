using System;
using System.Collections.Generic;
using System.IO;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Sources.Vfs
{
    public sealed class XRayArchiveEntry
    {
        public string Name { get; internal set; }
        public string VirtualPath { get; internal set; }
        public uint Offset { get; internal set; }
        public int SizeReal { get; internal set; }
        public int SizeCompressed { get; internal set; }
        public uint Crc { get; internal set; }
        public bool IsCompressed => SizeReal != SizeCompressed;
    }

    public sealed class XRayArchive
    {
        public const uint CHUNK_FILES = 1;
        public const uint CHUNK_HEADER = 666;
        public const uint COMPRESS_MARK = 0x80000000u;

        private readonly Dictionary<string, XRayArchiveEntry> _entries = new Dictionary<string, XRayArchiveEntry>(StringComparer.Ordinal);

        public string Path { get; private set; }
        public long Length { get; private set; }
        public long ModifiedTicks { get; private set; }
        public bool AutoLoad { get; private set; } = true;
        public string EntryPoint { get; private set; }
        public string EntryRoot { get; private set; }
        public int DirectoryCount { get; private set; }
        public int OutsideGamedataCount { get; private set; }
        public int UnsafeCount { get; private set; }
        public List<XRayArchiveEntry> Entries { get; } = new List<XRayArchiveEntry>();
        public List<string> Warnings { get; } = new List<string>();

        public static XRayArchive Open(string path, XRayFsGame fs)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new XRaySourceException($"Archive '{path}' does not exist.");
            }

            var archive = new XRayArchive { Path = info.FullName, Length = info.Length, ModifiedTicks = info.LastWriteTimeUtc.Ticks };
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 12))
            using (var reader = new BinaryReader(stream))
            {
                byte[] header = ReadChunk(stream, reader, CHUNK_HEADER, path);
                if (header == null)
                {
                    throw new XRaySourceException($"Archive '{path}' has no header chunk {CHUNK_HEADER}; the engine rejects archives without a header (unsupported legacy or encrypted format).");
                }

                archive.ReadHeader(Cp1251.Decode(header, 0, header.Length));
                byte[] files = ReadChunk(stream, reader, CHUNK_FILES, path);
                if (files == null)
                {
                    throw new XRaySourceException($"Archive '{path}' has no file table chunk {CHUNK_FILES}.");
                }

                archive.EntryRoot = (fs ?? XRayFsGame.CreateDefault()).ResolveEntryPoint(archive.EntryPoint);
                if (archive.EntryRoot == null)
                {
                    throw new XRaySourceException($"Archive '{path}' uses entry point '{archive.EntryPoint}' that cannot be resolved inside the game folder.");
                }

                archive.ReadTable(files);
            }

            return archive;
        }

        public bool TryGet(string virtualPath, out XRayArchiveEntry entry)
        {
            return _entries.TryGetValue(virtualPath, out entry);
        }

        public byte[] Read(XRayArchiveEntry entry)
        {
            if (entry.Offset + (long)entry.SizeCompressed > Length)
            {
                throw new XRaySourceException($"Archive '{Path}': entry '{entry.Name}' points past the end of the file.");
            }

            var raw = new byte[entry.SizeCompressed];
            using (var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
            {
                CheckUnchanged(stream);
                stream.Position = entry.Offset;
                int read = 0;
                while (read < raw.Length)
                {
                    int count = stream.Read(raw, read, raw.Length - read);
                    if (count <= 0)
                    {
                        throw new XRaySourceException($"Archive '{Path}': entry '{entry.Name}' is truncated.");
                    }

                    read += count;
                }
            }

            byte[] data = entry.IsCompressed ? XRayCompression.DecompressLzo1x(raw, 0, raw.Length, entry.SizeReal) : raw;
            uint crc = XRayCompression.Crc32(data, 0, data.Length);
            if (crc != entry.Crc)
            {
                throw new XRaySourceException($"Archive '{Path}': entry '{entry.Name}' failed the CRC check (expected {entry.Crc:x8}, got {crc:x8}). The archive is damaged; the resource is not replaced by another source.");
            }

            return data;
        }

        public Stream OpenWindow(XRayArchiveEntry entry)
        {
            if (entry.IsCompressed)
            {
                return new MemoryStream(Read(entry), false);
            }

            var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            CheckUnchanged(stream);
            return new XRayWindowStream(stream, entry.Offset, entry.SizeReal);
        }

        private void CheckUnchanged(FileStream stream)
        {
            if (stream.Length != Length)
            {
                stream.Dispose();
                throw new XRaySourceException($"Archive '{Path}' changed after indexing ({Length} -> {stream.Length} bytes). Rebuild the virtual index.");
            }
        }

        private void ReadHeader(string text)
        {
            bool inHeader = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inHeader = line.StartsWith("[header]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                int separator = line.IndexOf('=');
                if (!inHeader || separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim().Trim('"');
                if (string.Equals(key, "entry_point", StringComparison.OrdinalIgnoreCase))
                {
                    EntryPoint = value;
                }
                else if (string.Equals(key, "auto_load", StringComparison.OrdinalIgnoreCase))
                {
                    AutoLoad = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1" || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (EntryPoint == null)
            {
                throw new XRaySourceException($"Archive '{Path}' header has no entry_point.");
            }
        }

        private void ReadTable(byte[] table)
        {
            int position = 0;
            string prefix = EntryRoot.Length == 0 ? string.Empty : EntryRoot + "/";
            while (position < table.Length)
            {
                if (position + 2 > table.Length)
                {
                    throw new XRaySourceException($"Archive '{Path}': file table is truncated.");
                }

                int size = BitConverter.ToUInt16(table, position);
                position += 2;
                if (size < 16 || position + size > table.Length)
                {
                    throw new XRaySourceException($"Archive '{Path}': file table record at {position - 2} has an invalid size {size}.");
                }

                var entry = new XRayArchiveEntry
                {
                    SizeReal = BitConverter.ToInt32(table, position),
                    SizeCompressed = BitConverter.ToInt32(table, position + 4),
                    Crc = BitConverter.ToUInt32(table, position + 8),
                    Name = Cp1251.Decode(table, position + 12, size - 16),
                    Offset = BitConverter.ToUInt32(table, position + size - 4)
                };
                position += size;
                if (entry.Name.EndsWith("\\", StringComparison.Ordinal) || entry.Name.EndsWith("/", StringComparison.Ordinal))
                {
                    DirectoryCount++;
                    continue;
                }

                if (entry.SizeReal < 0 || entry.SizeCompressed < 0)
                {
                    throw new XRaySourceException($"Archive '{Path}': entry '{entry.Name}' has a negative size.");
                }

                if (!XRayVirtualPath.TryNormalizeSafe(prefix + entry.Name, out string full))
                {
                    UnsafeCount++;
                    Warnings.Add($"Archive '{Path}': entry '{entry.Name}' escapes the game folder and was ignored.");
                    continue;
                }

                if (!full.StartsWith("gamedata/", StringComparison.Ordinal))
                {
                    OutsideGamedataCount++;
                    continue;
                }

                entry.VirtualPath = full.Substring("gamedata/".Length);
                _entries[entry.VirtualPath] = entry;
            }

            Entries.AddRange(_entries.Values);
        }

        private static byte[] ReadChunk(FileStream stream, BinaryReader reader, uint id, string path)
        {
            stream.Position = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                uint type = reader.ReadUInt32();
                uint size = reader.ReadUInt32();
                if (stream.Position + size > stream.Length)
                {
                    throw new XRaySourceException($"Archive '{path}': chunk 0x{type:x} at {stream.Position - 8} is truncated (size {size}).");
                }

                if ((type & ~COMPRESS_MARK) != id)
                {
                    stream.Position += size;
                    continue;
                }

                byte[] data = reader.ReadBytes((int)size);
                return (type & COMPRESS_MARK) != 0 ? XRayCompression.DecompressLzhuf(data, 0, data.Length) : data;
            }

            return null;
        }
    }

    public sealed class XRayWindowStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public XRayWindowStream(Stream inner, long start, long length)
        {
            _inner = inner;
            _start = start;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Max(0, Math.Min(_length, value));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long remaining = _length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            _inner.Position = _start + _position;
            int read = _inner.Read(buffer, offset, (int)Math.Min(count, remaining));
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position = _position + offset;
                    break;
                default:
                    Position = _length + offset;
                    break;
            }

            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
