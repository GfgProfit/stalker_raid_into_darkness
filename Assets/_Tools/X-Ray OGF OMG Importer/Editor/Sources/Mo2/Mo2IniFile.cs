using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XRay.Importer.Sources.Mo2
{
    public sealed class Mo2IniFile
    {
        private const string GENERAL_SECTION = "General";
        private const string BYTE_ARRAY_PREFIX = "@ByteArray(";

        private readonly Dictionary<string, Dictionary<string, string>> _sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static Mo2IniFile Load(string path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        public static Mo2IniFile Parse(string text)
        {
            var ini = new Mo2IniFile();
            Dictionary<string, string> current = ini.GetOrAddSection(GENERAL_SECTION);
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                {
                    continue;
                }

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    current = ini.GetOrAddSection(DecodeKey(line.Substring(1, line.Length - 2)));
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = DecodeKey(line.Substring(0, separator).Trim());
                current[key] = DecodeValue(line.Substring(separator + 1).Trim());
            }

            return ini;
        }

        public string GetValue(string section, string key)
        {
            if (_sections.TryGetValue(section, out Dictionary<string, string> values) && values.TryGetValue(key, out string value))
            {
                return value;
            }

            return null;
        }

        public static string DecodeValue(string raw)
        {
            string unescaped = Unescape(raw);
            if (unescaped.StartsWith(BYTE_ARRAY_PREFIX, StringComparison.Ordinal) && unescaped.EndsWith(")", StringComparison.Ordinal))
            {
                string inner = unescaped.Substring(BYTE_ARRAY_PREFIX.Length, unescaped.Length - BYTE_ARRAY_PREFIX.Length - 1);
                return DecodeLatin1AsUtf8(inner);
            }

            return unescaped;
        }

        private static string DecodeKey(string raw)
        {
            if (raw.IndexOf('%') < 0)
            {
                return raw;
            }

            var bytes = new List<byte>();
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '%' && i + 2 < raw.Length && IsHex(raw[i + 1]) && IsHex(raw[i + 2]))
                {
                    bytes.Add((byte)(HexValue(raw[i + 1]) * 16 + HexValue(raw[i + 2])));
                    i += 2;
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(raw[i].ToString()));
                }
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static string Unescape(string raw)
        {
            var builder = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (ch == '"')
                {
                    continue;
                }

                if (ch != '\\' || i + 1 >= raw.Length)
                {
                    builder.Append(ch);
                    continue;
                }

                char next = raw[++i];
                switch (next)
                {
                    case 'a':
                        builder.Append('\a');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'v':
                        builder.Append('\v');
                        break;
                    case 'x':
                        int value = 0;
                        int digits = 0;
                        while (i + 1 < raw.Length && IsHex(raw[i + 1]))
                        {
                            value = value * 16 + HexValue(raw[++i]);
                            digits++;
                        }

                        builder.Append(digits == 0 ? 'x' : (char)value);

                        break;
                    default:
                        if (next >= '0' && next <= '7')
                        {
                            int octal = next - '0';
                            while (i + 1 < raw.Length && raw[i + 1] >= '0' && raw[i + 1] <= '7')
                            {
                                octal = octal * 8 + (raw[++i] - '0');
                            }

                            builder.Append((char)octal);
                        }
                        else
                        {
                            builder.Append(next);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        private static string DecodeLatin1AsUtf8(string text)
        {
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 0xFF)
                {
                    return text;
                }

                bytes[i] = (byte)text[i];
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private static bool IsHex(char ch)
        {
            return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
        }

        private static int HexValue(char ch)
        {
            if (ch <= '9')
            {
                return ch - '0';
            }

            return (ch >= 'a' ? ch - 'a' : ch - 'A') + 10;
        }

        private Dictionary<string, string> GetOrAddSection(string name)
        {
            if (!_sections.TryGetValue(name, out Dictionary<string, string> values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections.Add(name, values);
            }

            return values;
        }
    }
}
