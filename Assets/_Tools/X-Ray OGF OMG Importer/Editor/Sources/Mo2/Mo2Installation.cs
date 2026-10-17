using System.Collections.Generic;

namespace XRay.Importer.Sources.Mo2
{
    public sealed class Mo2Installation
    {
        public string InstanceFolder { get; internal set; }
        public string IniPath { get; internal set; }
        public bool IsPortable { get; internal set; }
        public string GameName { get; internal set; }
        public string GamePath { get; internal set; }
        public string BaseDirectory { get; internal set; }
        public string ModsDirectory { get; internal set; }
        public string ProfilesDirectory { get; internal set; }
        public string OverwriteDirectory { get; internal set; }
        public string SelectedProfile { get; internal set; }
        public List<string> SkipFileSuffixes { get; } = new List<string>();
        public List<string> SkipDirectories { get; } = new List<string>();
        public List<string> Profiles { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }
}
