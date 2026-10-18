using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using XRay.Importer.Core.IO;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Sources.Ltx
{
    public sealed class XRayLtxSection
    {
        public XRayLtxSection(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public string Origin { get; internal set; }
        public string DefinedIn { get; internal set; }
        public List<string> Parents { get; } = new List<string>();
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public HashSet<string> Deleted { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Appended { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Removed { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    }

    public sealed class XRayLtxDatabase
    {
        private const int MAX_DEPTH = 64;
        private const string CONFIGS_PREFIX = "configs/";
        private const string MOD_PREFIX = "mod_system_";

        private static readonly Regex SECTION = new Regex("^(!!|!|@)?\\[([^\\]]+)\\]\\s*(?::\\s*(.*))?$", RegexOptions.Compiled);
        private static readonly Regex INCLUDE = new Regex("^#include\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly XRayAssetIndex _index;
        private readonly Dictionary<string, XRayLtxSection> _sections = new Dictionary<string, XRayLtxSection>(StringComparer.Ordinal);
        private readonly HashSet<string> _loaded = new HashSet<string>(StringComparer.Ordinal);

        private XRayLtxDatabase(XRayAssetIndex index)
        {
            _index = index;
        }

        public IReadOnlyDictionary<string, XRayLtxSection> Sections => _sections;
        public List<string> Warnings { get; } = new List<string>();
        public int ModFileCount { get; private set; }

        public static XRayLtxDatabase LoadFile(XRayAssetIndex index, string virtualPath)
        {
            var database = new XRayLtxDatabase(index);
            database.Load(virtualPath, false);
            return database;
        }

        public static XRayLtxDatabase LoadSystem(XRayAssetIndex index)
        {
            var database = new XRayLtxDatabase(index);
            database.Load("configs/system.ltx", false);
            var mods = new List<string>();
            foreach (XRayIndexedFile file in index.Files)
            {
                string name = Path.GetFileName(file.VirtualPath);
                if (file.VirtualPath.StartsWith(CONFIGS_PREFIX, StringComparison.Ordinal) && name.StartsWith(MOD_PREFIX, StringComparison.Ordinal) && name.EndsWith(".ltx", StringComparison.Ordinal))
                {
                    mods.Add(file.VirtualPath);
                }
            }

            mods.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));
            foreach (string mod in mods)
            {
                database.Load(mod, true);
            }

            database.ModFileCount = mods.Count;
            return database;
        }

        public bool HasSection(string name)
        {
            return _sections.ContainsKey(name.ToLowerInvariant());
        }

        public string GetOrigin(string name)
        {
            return _sections.TryGetValue(name.ToLowerInvariant(), out XRayLtxSection section) ? section.Origin : null;
        }

        public string GetValue(string sectionName, string key)
        {
            return GetValue(sectionName.ToLowerInvariant(), key.ToLowerInvariant(), 0);
        }

        public List<string> GetList(string sectionName, string key)
        {
            return GetList(sectionName.ToLowerInvariant(), key.ToLowerInvariant(), 0);
        }

        public static string Unquote(string value)
        {
            if (value == null)
            {
                return null;
            }

            string trimmed = value.Trim();
            return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"' ? trimmed.Substring(1, trimmed.Length - 2) : trimmed;
        }

        private string GetValue(string sectionName, string key, int depth)
        {
            if (depth > MAX_DEPTH || !_sections.TryGetValue(sectionName, out XRayLtxSection section))
            {
                return null;
            }

            if (section.Values.TryGetValue(key, out string own))
            {
                return own;
            }

            if (section.Deleted.Contains(key))
            {
                return null;
            }

            string inherited = null;
            foreach (string parent in section.Parents)
            {
                string value = GetValue(parent, key, depth + 1);
                if (value != null)
                {
                    inherited = value;
                }
            }

            return inherited;
        }

        private List<string> GetList(string sectionName, string key, int depth)
        {
            var items = new List<string>();
            if (depth > MAX_DEPTH || !_sections.TryGetValue(sectionName, out XRayLtxSection section))
            {
                return items;
            }

            if (section.Values.TryGetValue(key, out string own))
            {
                items.AddRange(SplitList(own));
            }
            else if (!section.Deleted.Contains(key))
            {
                foreach (string parent in section.Parents)
                {
                    items.AddRange(GetList(parent, key, depth + 1));
                }
            }

            if (section.Appended.TryGetValue(key, out List<string> appended))
            {
                items.AddRange(appended);
            }

            var removed = section.Removed.TryGetValue(key, out List<string> list) ? new HashSet<string>(list) : new HashSet<string>();
            var seen = new HashSet<string>();
            items.RemoveAll(item => removed.Contains(item) || !seen.Add(item));
            return items;
        }

        private void Load(string virtualPath, bool dltx)
        {
            string key = XRayVirtualPath.Normalize(virtualPath);
            if (!_loaded.Add(key))
            {
                return;
            }

            if (!_index.TryGet(key, out XRayIndexedFile file))
            {
                Warnings.Add($"Missing include '{key}'.");
                return;
            }

            string fullPath = _index.GetFullPath(file.Winner);
            string origin = _index.GetSource(file.Winner).Name + " :: " + key;
            byte[] data;
            try
            {
                data = File.ReadAllBytes(fullPath);
            }
            catch (IOException exception)
            {
                Warnings.Add($"Cannot read '{fullPath}': {exception.Message}");
                return;
            }

            string text = Cp1251.Decode(data, 0, data.Length);
            int slash = key.LastIndexOf('/');
            string directory = slash >= 0 ? key.Substring(0, slash) : string.Empty;
            XRayLtxSection current = null;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                Match include = INCLUDE.Match(line);
                if (include.Success)
                {
                    LoadInclude(directory, include.Groups[1].Value, dltx);
                    continue;
                }

                Match header = SECTION.Match(line);
                if (header.Success)
                {
                    current = ApplyHeader(header, key, origin);
                    continue;
                }

                if (current != null)
                {
                    ApplyLine(current, line);
                }
            }
        }

        private void LoadInclude(string directory, string pattern, bool dltx)
        {
            string combined = XRayVirtualPath.Normalize(directory + "/" + pattern);
            while (combined.Contains("/../"))
            {
                combined = Regex.Replace(combined, "/[^/]+/\\.\\./", "/");
            }

            if (combined.IndexOf('*') < 0 && combined.IndexOf('?') < 0)
            {
                Load(combined, dltx);
                return;
            }

            var regex = new Regex("^" + Regex.Escape(combined).Replace("\\*", "[^/]*").Replace("\\?", "[^/]") + "$");
            var matches = new List<string>();
            foreach (XRayIndexedFile file in _index.Files)
            {
                if (regex.IsMatch(file.VirtualPath))
                {
                    matches.Add(file.VirtualPath);
                }
            }

            matches.Sort(StringComparer.Ordinal);
            foreach (string match in matches)
            {
                Load(match, dltx);
            }
        }

        private XRayLtxSection ApplyHeader(Match header, string path, string origin)
        {
            string prefix = header.Groups[1].Value;
            string name = header.Groups[2].Value.Trim().ToLowerInvariant();
            if (prefix == "!!")
            {
                _sections.Remove(name);
                return null;
            }

            if (!_sections.TryGetValue(name, out XRayLtxSection section))
            {
                section = new XRayLtxSection(name);
                section.DefinedIn = path;
                _sections.Add(name, section);
            }

            section.Origin = origin;
            if (header.Groups[3].Success && header.Groups[3].Value.Trim().Length > 0)
            {
                section.Parents.Clear();
                section.Parents.AddRange(SplitList(header.Groups[3].Value));
            }

            return section;
        }

        private static void ApplyLine(XRayLtxSection section, string line)
        {
            char op = line[0];
            string body = op == '!' || op == '<' || op == '>' ? line.Substring(1) : line;
            int separator = body.IndexOf('=');
            string key = (separator >= 0 ? body.Substring(0, separator) : body).Trim().ToLowerInvariant();
            string value = separator >= 0 ? body.Substring(separator + 1).Trim() : string.Empty;
            if (key.Length == 0)
            {
                return;
            }

            switch (op)
            {
                case '!':
                    section.Values.Remove(key);
                    section.Deleted.Add(key);
                    break;
                case '>':
                    GetOrAdd(section.Appended, key).AddRange(SplitList(value));
                    break;
                case '<':
                    GetOrAdd(section.Removed, key).AddRange(SplitList(value));
                    break;
                default:
                    section.Values[key] = value;
                    section.Appended.Remove(key);
                    section.Deleted.Remove(key);
                    break;
            }
        }

        private static List<string> GetOrAdd(Dictionary<string, List<string>> map, string key)
        {
            if (!map.TryGetValue(key, out List<string> list))
            {
                list = new List<string>();
                map.Add(key, list);
            }

            return list;
        }

        private static IEnumerable<string> SplitList(string value)
        {
            foreach (string item in value.Split(','))
            {
                string trimmed = item.Trim().ToLowerInvariant();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }

        private static string StripComment(string line)
        {
            var builder = new StringBuilder(line.Length);
            bool quoted = false;
            foreach (char ch in line)
            {
                if (ch == '"')
                {
                    quoted = !quoted;
                }
                else if (ch == ';' && !quoted)
                {
                    break;
                }

                builder.Append(ch);
            }

            return builder.ToString().TrimEnd('\r');
        }
    }
}
