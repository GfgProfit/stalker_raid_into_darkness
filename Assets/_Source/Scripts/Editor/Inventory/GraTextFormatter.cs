using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RaidIntoDarkness.Player.Editor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class GraTextFormatter
    {
        public const string COLOR_DEFS = "configs/ui/color_defs.xml";

        private const string DEFAULT_COLOR = "default";
        private const string BASE_COLOR = "ui_gray_1";

        private static readonly Regex COLOR_TAG = new Regex("%c\\[([^\\]]*)\\]", RegexOptions.Compiled);
        private static readonly Regex COLOR_DEF = new Regex("<color\\s+name=\"([^\"]+)\"\\s+r=\"(\\d+)\"\\s+g=\"(\\d+)\"\\s+b=\"(\\d+)\"", RegexOptions.Compiled);
        private static readonly Regex RICH_TAG = new Regex("<[^>]+>", RegexOptions.Compiled);

        public static Dictionary<string, Color32> LoadColors(StalkerXRaySources sources)
        {
            var colors = new Dictionary<string, Color32>(StringComparer.OrdinalIgnoreCase);
            byte[] data = sources.ReadBytes(COLOR_DEFS);

            if (data == null)
            {
                Debug.LogWarning($"[GraText] '{COLOR_DEFS}' is missing; description colors are dropped.");
                return colors;
            }

            foreach (Match match in COLOR_DEF.Matches(Encoding.UTF8.GetString(data)))
            {
                colors[match.Groups[1].Value] = new Color32(byte.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), byte.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture), byte.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture), 255);
            }

            return colors;
        }

        public static string Format(string text, IReadOnlyDictionary<string, Color32> colors)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string clean = COLOR_TAG.Replace(text, match => ToRichColor(match.Groups[1].Value, colors)).Replace("\\n", "\n").Replace("&amp;", "&").Replace("\r", string.Empty).Replace("\t", string.Empty);
            var lines = new List<string>();
            string pendingTags = string.Empty;
            bool pendingBlank = false;

            foreach (string line in clean.Split('\n'))
            {
                string trimmed = line.Trim();

                if (RICH_TAG.Replace(trimmed, string.Empty).Trim().Length == 0)
                {
                    pendingTags += trimmed;
                    pendingBlank = lines.Count > 0;
                    continue;
                }

                if (pendingBlank)
                {
                    lines.Add(string.Empty);
                }

                lines.Add(pendingTags + trimmed);
                pendingTags = string.Empty;
                pendingBlank = false;
            }

            return string.Join("\n", lines);
        }

        private static string ToRichColor(string value, IReadOnlyDictionary<string, Color32> colors)
        {
            string[] parts = value.Split(',');

            if (parts.Length == 4 && byte.TryParse(parts[1].Trim(), out byte r) && byte.TryParse(parts[2].Trim(), out byte g) && byte.TryParse(parts[3].Trim(), out byte b))
            {
                return "<color=#" + ColorUtility.ToHtmlStringRGB(new Color32(r, g, b, 255)) + ">";
            }

            string name = string.Equals(value, DEFAULT_COLOR, StringComparison.OrdinalIgnoreCase) ? BASE_COLOR : value;
            return colors.TryGetValue(name, out Color32 color) ? "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" : string.Empty;
        }
    }
}
