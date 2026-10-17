using System;
using System.Collections.Generic;
using System.Globalization;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Sources.Textures
{
    public sealed class XRayTexturesLtx
    {
        private const string SECTION_ASSOCIATION = "association";
        private const string SECTION_SPECIFICATION = "specification";

        private readonly Dictionary<string, XRayTextureDescription> _descriptions = new Dictionary<string, XRayTextureDescription>(StringComparer.Ordinal);

        public string Origin { get; private set; }
        public int Count => _descriptions.Count;

        public static XRayTexturesLtx Parse(byte[] data, string origin)
        {
            var ltx = new XRayTexturesLtx { Origin = origin };
            string text = Cp1251.Decode(data, 0, data.Length);
            string section = null;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    section = end > 0 ? line.Substring(1, end - 1).Trim().ToLowerInvariant() : null;
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0 || section == null)
                {
                    continue;
                }

                string key = NormalizeName(line.Substring(0, separator));
                string value = line.Substring(separator + 1).Trim();
                if (section == SECTION_SPECIFICATION)
                {
                    ltx.ParseSpecification(key, value);
                }
                else if (section == SECTION_ASSOCIATION)
                {
                    ltx.ParseAssociation(key, value);
                }
            }

            return ltx;
        }

        public bool TryGet(string textureName, out XRayTextureDescription description)
        {
            return _descriptions.TryGetValue(NormalizeName(textureName), out description);
        }

        public static string NormalizeName(string name)
        {
            return (name ?? string.Empty).Trim().Replace('/', '\\').ToLowerInvariant();
        }

        private void ParseSpecification(string key, string value)
        {
            XRayTextureDescription description = GetOrAdd(key);
            string bump = ExtractBracket(value, "bump_mode");
            if (bump != null)
            {
                if (bump.StartsWith("use_parallax:", StringComparison.OrdinalIgnoreCase))
                {
                    description.BumpMode = XRayBumpMode.UseParallax;
                    description.BumpName = bump.Substring("use_parallax:".Length).Trim();
                }
                else if (bump.StartsWith("use:", StringComparison.OrdinalIgnoreCase))
                {
                    description.BumpMode = XRayBumpMode.Use;
                    description.BumpName = bump.Substring("use:".Length).Trim();
                }
                else
                {
                    description.BumpMode = XRayBumpMode.None;
                }
            }

            string material = ExtractBracket(value, "material");
            if (material != null && double.TryParse(material, NumberStyles.Float, CultureInfo.InvariantCulture, out double materialValue))
            {
                description.Material = materialValue;
            }
        }

        private void ParseAssociation(string key, string value)
        {
            XRayTextureDescription description = GetOrAdd(key);
            string[] parts = value.Split(',');
            if (parts.Length == 0 || parts[0].Trim().Length == 0)
            {
                return;
            }

            description.DetailName = parts[0].Trim();
            if (parts.Length > 1 && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale))
            {
                description.DetailScale = scale;
            }
        }

        private XRayTextureDescription GetOrAdd(string key)
        {
            if (!_descriptions.TryGetValue(key, out XRayTextureDescription description))
            {
                description = new XRayTextureDescription { Origin = Origin };
                _descriptions.Add(key, description);
            }

            return description;
        }

        private static string ExtractBracket(string value, string name)
        {
            int start = value.IndexOf(name + "[", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            start += name.Length + 1;
            int end = value.IndexOf(']', start);
            return end < 0 ? null : value.Substring(start, end - start).Trim();
        }

        private static string StripComment(string line)
        {
            int comment = line.IndexOf(';');
            return comment >= 0 ? line.Substring(0, comment) : line;
        }
    }
}
