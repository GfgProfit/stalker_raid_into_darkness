using System.Collections.Generic;

namespace XRay.Importer.Sources.Mo2
{
    public enum Mo2ModState
    {
        Enabled,
        Disabled,
        Unmanaged
    }

    public sealed class Mo2ModEntry
    {
        public string Name { get; internal set; }
        public Mo2ModState State { get; internal set; }
        public bool IsSeparator { get; internal set; }
        public int Priority { get; internal set; }
        public int LineNumber { get; internal set; }
        public bool IsActive => State == Mo2ModState.Enabled && !IsSeparator;
    }

    public sealed class Mo2ModList
    {
        public List<Mo2ModEntry> Entries { get; } = new List<Mo2ModEntry>();
        public List<string> Warnings { get; } = new List<string>();

        public List<Mo2ModEntry> GetActiveModsByPriorityDescending()
        {
            var active = new List<Mo2ModEntry>();
            foreach (Mo2ModEntry entry in Entries)
            {
                if (entry.IsActive)
                {
                    active.Add(entry);
                }
            }

            active.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return active;
        }
    }
}
