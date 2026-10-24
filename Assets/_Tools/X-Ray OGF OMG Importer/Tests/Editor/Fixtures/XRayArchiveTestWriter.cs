using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class XRayArchiveTestEntry
    {
        public XRayArchiveTestEntry(string name, byte[] data, bool lzo)
        {
            Name = name;
            Data = data;
            Lzo = lzo;
        }

        public string Name { get; }
        public byte[] Data { get; }
        public bool Lzo { get; }
    }

    public static class XRayArchiveTestWriter
    {
        public const string DEFAULT_ENTRY_POINT = "$fs_root$\\gamedata\\";

        public static byte[] Write(params XRayArchiveTestEntry[] entries)
        {
            return Write(DEFAULT_ENTRY_POINT, true, true, entries);
        }

        public static byte[] Write(string entryPoint, bool autoLoad, bool header, params XRayArchiveTestEntry[] entries)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                if (header)
                {
                    byte[] text = Encoding.ASCII.GetBytes($"[header]\r\nauto_load = {(autoLoad ? "true" : "false")}\r\nentry_point = {entryPoint}\r\n");
                    writer.Write(XRayArchive.CHUNK_HEADER);
                    writer.Write(text.Length);
                    writer.Write(text);
                }

                var payloads = new List<byte[]>();
                int dataSize = 0;
                foreach (XRayArchiveTestEntry entry in entries)
                {
                    byte[] payload = entry.Lzo ? Lzo(entry.Data) : entry.Data;
                    payloads.Add(payload);
                    dataSize += payload.Length;
                }

                writer.Write(0u);
                writer.Write(dataSize);
                long dataStart = stream.Position;
                foreach (byte[] payload in payloads)
                {
                    writer.Write(payload);
                }

                using (var table = new MemoryStream())
                using (var tableWriter = new BinaryWriter(table))
                {
                    long offset = dataStart;
                    for (int i = 0; i < entries.Length; i++)
                    {
                        byte[] name = Encoding.ASCII.GetBytes(entries[i].Name);
                        tableWriter.Write((ushort)(16 + name.Length));
                        tableWriter.Write(entries[i].Data.Length);
                        tableWriter.Write(payloads[i].Length);
                        tableWriter.Write(XRayCompression.Crc32(entries[i].Data, 0, entries[i].Data.Length));
                        tableWriter.Write(name);
                        tableWriter.Write((uint)offset);
                        offset += payloads[i].Length;
                    }

                    writer.Write(XRayArchive.CHUNK_FILES);
                    writer.Write((int)table.Length);
                    writer.Write(table.ToArray());
                }

                return stream.ToArray();
            }
        }

        public static byte[] Lzo(byte[] data)
        {
            if (data.Length < 4 || data.Length > 238)
            {
                throw new ArgumentException("The literal-only LZO writer supports 4..238 bytes.");
            }

            var output = new byte[data.Length + 4];
            output[0] = (byte)(17 + data.Length);
            Buffer.BlockCopy(data, 0, output, 1, data.Length);
            output[data.Length + 1] = 0x11;
            return output;
        }
    }
}
