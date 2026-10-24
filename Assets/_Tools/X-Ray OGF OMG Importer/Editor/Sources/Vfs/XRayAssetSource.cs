namespace XRay.Importer.Sources.Vfs
{
    public enum XRayAssetSourceKind
    {
        Overwrite,
        Mod,
        GameGamedata,
        Custom,
        BaseAnomaly,
        LevelGamedata,
        Archive
    }

    public sealed class XRayAssetSource
    {
        public XRayAssetSource(string name, XRayAssetSourceKind kind, int priority, string gamedataRoot, string containerFolder) : this(name, kind, priority, gamedataRoot, containerFolder, name)
        {
        }

        public XRayAssetSource(string name, XRayAssetSourceKind kind, int priority, string gamedataRoot, string containerFolder, string origin)
        {
            Name = name;
            Kind = kind;
            Priority = priority;
            GamedataRoot = gamedataRoot;
            ContainerFolder = containerFolder;
            Origin = string.IsNullOrEmpty(origin) ? name : origin;
        }

        public string Name { get; }
        public XRayAssetSourceKind Kind { get; }
        public int Priority { get; }
        public string GamedataRoot { get; }
        public string ContainerFolder { get; }
        public string Origin { get; }
        public bool IsArchive => Kind == XRayAssetSourceKind.Archive;
        public string DisplayPriority => Kind == XRayAssetSourceKind.Mod ? Priority.ToString() : Kind == XRayAssetSourceKind.Archive ? "Archive #" + Priority : Kind.ToString();

        public string GetFullPath(string relativePath)
        {
            return XRayVirtualPath.ToFileSystem(GamedataRoot, relativePath);
        }

        public string Describe(string relativePath)
        {
            return IsArchive ? GamedataRoot + " :: " + XRayVirtualPath.ToDisplay(relativePath) : GetFullPath(relativePath);
        }
    }
}
