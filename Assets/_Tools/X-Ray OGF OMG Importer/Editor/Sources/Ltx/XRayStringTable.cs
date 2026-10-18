using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using XRay.Importer.Core.IO;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Sources.Ltx
{
    public sealed class XRayStringTable
    {
        private static readonly Regex ENTRY = new Regex("<string\\s+id\\s*=\\s*\"([^\"]+)\"\\s*>.*?<text>(.*?)</text>", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex WHITESPACE = new Regex("\\s+", RegexOptions.Compiled);
        private static readonly Regex UTF8_DECLARATION = new Regex("^\\s*<\\?xml[^>]*encoding\\s*=\\s*\"utf-8\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Count => _strings.Count;
        public int FileCount { get; private set; }

        public static XRayStringTable Load(XRayAssetIndex index, string language)
        {
            var table = new XRayStringTable();
            string prefix = "configs/text/" + language.ToLowerInvariant() + "/";
            var files = new List<XRayIndexedFile>();
            foreach (XRayIndexedFile file in index.Files)
            {
                if (file.VirtualPath.StartsWith(prefix, StringComparison.Ordinal) && file.VirtualPath.EndsWith(".xml", StringComparison.Ordinal))
                {
                    files.Add(file);
                }
            }

            files.Sort((a, b) => string.CompareOrdinal(a.VirtualPath, b.VirtualPath));
            foreach (XRayIndexedFile file in files)
            {
                byte[] data;
                try
                {
                    data = File.ReadAllBytes(index.GetFullPath(file.Winner));
                }
                catch (IOException)
                {
                    continue;
                }

                table.FileCount++;
                string text = Cp1251.Decode(data, 0, data.Length);
                if (UTF8_DECLARATION.IsMatch(text))
                {
                    text = Encoding.UTF8.GetString(data);
                }

                foreach (Match match in ENTRY.Matches(text))
                {
                    string id = match.Groups[1].Value.ToLowerInvariant();
                    if (!table._strings.ContainsKey(id))
                    {
                        table._strings.Add(id, WHITESPACE.Replace(match.Groups[2].Value, " ").Trim());
                    }
                }
            }

            return table;
        }

        public string Translate(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return id;
            }

            return _strings.TryGetValue(id.Trim().ToLowerInvariant(), out string value) ? value : id;
        }
    }
}
