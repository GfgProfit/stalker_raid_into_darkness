using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    public sealed class FirstPersonSwmEntry
    {
        public FirstPersonSwmEntry(int flags, string motion)
        {
            Flags = flags;
            Motion = motion;
        }

        public int Flags { get; }
        public string Motion { get; }
    }

    // Reads the animation tables of swm_legs.script as data; the Lua code itself is never executed.
    public sealed class FirstPersonSwmScript
    {
        public const string NON_COMBAT_TABLE = "AnimTable_NonCombat";
        public const string SLOWED_DOWN_TABLE = "AnimTable_SlowedDown";
        public const string BASIC_TABLE = "AnimTable_Basic";
        public const int LADDER_FLAG = 2048;
        public const int FORWARD_FLAG = 1;
        public const int BACKWARD_FLAG = 2;

        private static readonly Regex NUMBER_ENTRY = new Regex("\\[\\s*([0-9+\\s]+)\\]\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex STRING_ENTRY = new Regex("\\[\\s*\"([^\"]+)\"\\s*\\]\\s*=\\s*([0-9]+)", RegexOptions.Compiled);
        private static readonly Regex BASIC_FALLBACK = new Regex("AnimTable_Basic\\[body_flag\\]\\s+or\\s+\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex TURN = new Regex("turnAnim\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        public Dictionary<string, List<FirstPersonSwmEntry>> Tables { get; } = new Dictionary<string, List<FirstPersonSwmEntry>>(StringComparer.Ordinal);
        public List<KeyValuePair<string, int>> Idles { get; } = new List<KeyValuePair<string, int>>();
        public int DisabledFlags { get; private set; }
        public string BasicFallback { get; private set; }
        public string TurnLeft { get; private set; }
        public string TurnRight { get; private set; }
        public List<string> Warnings { get; } = new List<string>();

        public static FirstPersonSwmScript Parse(string source)
        {
            var script = new FirstPersonSwmScript();
            string code = StripComments(source);
            foreach (string table in new[] { NON_COMBAT_TABLE, SLOWED_DOWN_TABLE, BASIC_TABLE })
            {
                string body = FindTable(code, "local " + table);
                var entries = new List<FirstPersonSwmEntry>();
                if (body == null)
                {
                    script.Warnings.Add($"Table '{table}' not found.");
                }
                else
                {
                    foreach (Match match in NUMBER_ENTRY.Matches(body))
                    {
                        AddUnique(script, table, entries, new FirstPersonSwmEntry(Sum(match.Groups[1].Value), match.Groups[2].Value));
                    }
                }

                script.Tables.Add(table, entries);
            }

            string idles = FindTable(code, "idles_time");
            if (idles == null)
            {
                script.Warnings.Add("Table 'idles_time' not found.");
            }
            else
            {
                foreach (Match match in STRING_ENTRY.Matches(idles))
                {
                    script.Idles.Add(new KeyValuePair<string, int>(match.Groups[1].Value, int.Parse(match.Groups[2].Value)));
                }
            }

            string disabled = FindTable(code, "local DISABLED_FLAGS");
            if (disabled == null)
            {
                script.Warnings.Add("Table 'DISABLED_FLAGS' not found.");
            }
            else
            {
                foreach (Match match in Regex.Matches(disabled, "=\\s*([0-9]+)"))
                {
                    script.DisabledFlags |= int.Parse(match.Groups[1].Value);
                }
            }

            Match fallback = BASIC_FALLBACK.Match(code);
            script.BasicFallback = fallback.Success ? fallback.Groups[1].Value : null;
            if (!fallback.Success)
            {
                script.Warnings.Add("Basic table fallback motion not found in get_anim_type.");
            }

            foreach (Match match in TURN.Matches(code))
            {
                string motion = match.Groups[1].Value;
                if (motion.EndsWith("_left", StringComparison.Ordinal))
                {
                    script.TurnLeft = motion;
                }
                else if (motion.EndsWith("_right", StringComparison.Ordinal))
                {
                    script.TurnRight = motion;
                }
            }

            return script;
        }

        public string Find(string table, int flags)
        {
            if (!Tables.TryGetValue(table, out List<FirstPersonSwmEntry> entries))
            {
                return null;
            }

            foreach (FirstPersonSwmEntry entry in entries)
            {
                if (entry.Flags == flags)
                {
                    return entry.Motion;
                }
            }

            return null;
        }

        private static void AddUnique(FirstPersonSwmScript script, string table, List<FirstPersonSwmEntry> entries, FirstPersonSwmEntry entry)
        {
            // Lua table constructors keep the last value assigned to a key.
            int existing = entries.FindIndex(item => item.Flags == entry.Flags);
            if (existing >= 0)
            {
                script.Warnings.Add($"{table}[{entry.Flags}] is assigned twice; '{entry.Motion}' overrides '{entries[existing].Motion}'.");
                entries[existing] = entry;
                return;
            }

            entries.Add(entry);
        }

        private static int Sum(string expression)
        {
            int sum = 0;
            foreach (string part in expression.Split('+'))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    sum += int.Parse(trimmed);
                }
            }

            return sum;
        }

        private static string FindTable(string code, string declaration)
        {
            Match match = Regex.Match(code, Regex.Escape(declaration) + "\\s*=\\s*\\{");
            if (!match.Success)
            {
                return null;
            }

            int start = match.Index + match.Length;
            int depth = 1;
            for (int i = start; i < code.Length; i++)
            {
                if (code[i] == '{')
                {
                    depth++;
                }
                else if (code[i] == '}' && --depth == 0)
                {
                    return code.Substring(start, i - start);
                }
            }

            return null;
        }

        private static string StripComments(string source)
        {
            var builder = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char ch = source[i];
                if (ch == '"' || ch == '\'')
                {
                    int end = i + 1;
                    while (end < source.Length && source[end] != ch && source[end] != '\n')
                    {
                        end += source[end] == '\\' ? 2 : 1;
                    }

                    end = Math.Min(end + 1, source.Length);
                    builder.Append(source, i, end - i);
                    i = end;
                    continue;
                }

                if (ch == '-' && i + 1 < source.Length && source[i + 1] == '-')
                {
                    if (i + 3 < source.Length && source[i + 2] == '[' && source[i + 3] == '[')
                    {
                        int close = source.IndexOf("]]", i + 4, StringComparison.Ordinal);
                        i = close < 0 ? source.Length : close + 2;
                        continue;
                    }

                    int newline = source.IndexOf('\n', i);
                    i = newline < 0 ? source.Length : newline;
                    continue;
                }

                builder.Append(ch);
                i++;
            }

            return builder.ToString();
        }
    }
}
