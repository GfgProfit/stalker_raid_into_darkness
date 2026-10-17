using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XRay.Importer.Sources
{
    public static class XRayAssetNaming
    {
        private const int MAX_NUMBERED_CANDIDATES = 64;

        public static List<string> CreateCandidates(string virtualPath, string suffix)
        {
            string[] parts = (virtualPath ?? string.Empty).Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var segments = new List<string>(parts);
            if (segments.Count > 0 && string.Equals(segments[0], "textures", StringComparison.OrdinalIgnoreCase))
            {
                segments.RemoveAt(0);
            }

            if (segments.Count == 0)
            {
                segments.Add("xray_default");
            }

            segments[segments.Count - 1] = Path.GetFileNameWithoutExtension(segments[segments.Count - 1]);
            var candidates = new List<string>();
            for (int take = 1; take <= segments.Count; take++)
            {
                AddUnique(candidates, Sanitize(string.Join("_", segments.GetRange(segments.Count - take, take)) + suffix));
            }

            string longest = candidates[candidates.Count - 1];
            for (int i = 2; i < MAX_NUMBERED_CANDIDATES; i++)
            {
                candidates.Add(longest + "_" + i);
            }

            return candidates;
        }

        public static string Place(string folder, List<string> candidates, string extension, string key, Func<string, string> readKey, ICollection<string> claimed)
        {
            foreach (string candidate in candidates)
            {
                string path = folder + "/" + candidate + extension;
                if (claimed != null && claimed.Contains(path))
                {
                    continue;
                }

                string existingKey = readKey(path);
                if (existingKey == null || existingKey == key)
                {
                    claimed?.Add(path);
                    return path;
                }
            }

            throw new XRaySourceException($"No free asset name for '{key}' in '{folder}'.");
        }

        public static string Sanitize(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char ch in name)
            {
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 || ch == '#' || ch == ' ' ? '_' : ch);
            }

            return builder.ToString();
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }
    }
}
