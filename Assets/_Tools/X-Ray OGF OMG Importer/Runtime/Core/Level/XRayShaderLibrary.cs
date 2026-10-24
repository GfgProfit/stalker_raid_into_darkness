using System.Collections.Generic;
using System.Text;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Level
{
    public sealed class XRayBlender
    {
        public string Name { get; internal set; }
        public string ClassId { get; internal set; }
        public ushort Version { get; internal set; }
        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public bool PropertiesComplete { get; internal set; }

        public bool GetBool(string name, bool fallback)
        {
            return Properties.TryGetValue(name, out string value) ? value == "1" : fallback;
        }

        public int GetInt(string name, int fallback)
        {
            return Properties.TryGetValue(name, out string value) && int.TryParse(value, out int result) ? result : fallback;
        }

        public string GetString(string name)
        {
            return Properties.TryGetValue(name, out string value) ? value : null;
        }
    }

    public sealed class XRayShaderLibrary
    {
        public const uint CHUNK_BLENDERS = 2;
        public const int DESCRIPTION_SIZE = 176;
        public const int STRING64 = 64;
        public const uint PID_MARKER = 0;
        public const uint PID_MATRIX = 1;
        public const uint PID_CONSTANT = 2;
        public const uint PID_TEXTURE = 3;
        public const uint PID_INTEGER = 4;
        public const uint PID_FLOAT = 5;
        public const uint PID_BOOL = 6;
        public const uint PID_TOKEN = 7;
        public const uint PID_CLSID = 8;
        public const uint PID_OBJECT = 9;
        public const uint PID_STRING = 10;

        private readonly Dictionary<string, XRayBlender> _blenders = new Dictionary<string, XRayBlender>();

        public int Count => _blenders.Count;

        public bool TryGet(string name, out XRayBlender blender)
        {
            return _blenders.TryGetValue(Normalize(name), out blender);
        }

        public static XRayShaderLibrary Read(byte[] data, string context)
        {
            Dictionary<uint, XRayChunk> chunks = XRayChunks.ReadMap(new XRayReader(data, context));
            var library = new XRayShaderLibrary();
            if (!chunks.TryGetValue(CHUNK_BLENDERS, out XRayChunk blenders))
            {
                throw new XRayFormatException($"{context}: blender chunk 2 is missing.");
            }

            foreach (XRayChunk chunk in XRayChunks.ReadAll(blenders.CreateReader(context + " BLENDERS")))
            {
                if (chunk.Length < DESCRIPTION_SIZE)
                {
                    continue;
                }

                var blender = new XRayBlender();
                var builder = new StringBuilder(8);
                for (int i = 7; i >= 0; i--)
                {
                    builder.Append((char)chunk.Buffer[chunk.Offset + i]);
                }

                blender.ClassId = builder.ToString().Trim();
                blender.Name = ReadFixed(chunk.Buffer, chunk.Offset + 8, 128);
                blender.Version = (ushort)(chunk.Buffer[chunk.Offset + 172] | (chunk.Buffer[chunk.Offset + 173] << 8));
                ReadProperties(new XRayReader(chunk.Buffer, chunk.Offset + DESCRIPTION_SIZE, chunk.Length - DESCRIPTION_SIZE, context), blender);
                library._blenders[Normalize(blender.Name)] = blender;
            }

            return library;
        }

        private static void ReadProperties(XRayReader reader, XRayBlender blender)
        {
            string section = string.Empty;
            while (reader.Remaining >= 5)
            {
                uint type = reader.ReadUInt32();
                string name = reader.ReadStringZ();
                string key = section.Length > 0 ? section + "/" + name : name;
                switch (type)
                {
                    case PID_MARKER:
                        section = name;
                        break;
                    case PID_MATRIX:
                    case PID_CONSTANT:
                    case PID_TEXTURE:
                    case PID_OBJECT:
                    case PID_STRING:
                        if (reader.Remaining < STRING64)
                        {
                            return;
                        }

                        blender.Properties[key] = ReadFixed(reader.Buffer, reader.AbsolutePosition, STRING64);
                        reader.Skip(STRING64);
                        break;
                    case PID_INTEGER:
                        blender.Properties[key] = reader.ReadInt32().ToString();
                        reader.Skip(8);
                        break;
                    case PID_FLOAT:
                        blender.Properties[key] = reader.ReadSingle().ToString(System.Globalization.CultureInfo.InvariantCulture);
                        reader.Skip(8);
                        break;
                    case PID_BOOL:
                        blender.Properties[key] = reader.ReadUInt32() != 0 ? "1" : "0";
                        break;
                    case PID_TOKEN:
                        uint selected = reader.ReadUInt32();
                        uint count = reader.ReadUInt32();
                        if (count > 64 || reader.Remaining < count * (4 + STRING64))
                        {
                            return;
                        }

                        string selectedName = selected.ToString();
                        for (int i = 0; i < count; i++)
                        {
                            uint id = reader.ReadUInt32();
                            string token = ReadFixed(reader.Buffer, reader.AbsolutePosition, STRING64);
                            reader.Skip(STRING64);
                            if (id == selected)
                            {
                                selectedName = token;
                            }
                        }

                        blender.Properties[key] = selectedName;
                        break;
                    case PID_CLSID:
                        reader.Skip(8);
                        break;
                    default:
                        return;
                }
            }

            blender.PropertiesComplete = true;
        }

        private static string ReadFixed(byte[] buffer, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && end < buffer.Length && buffer[end] != 0)
            {
                end++;
            }

            return Cp1251.Decode(buffer, offset, end - offset);
        }

        private static string Normalize(string name)
        {
            return (name ?? string.Empty).Trim().Replace('/', '\\').ToLowerInvariant();
        }
    }
}
