namespace XRay.Importer.Sources.Vfs
{
    public enum XRayAssetSourceKind
    {
        Overwrite,
        Mod,
        GameGamedata,
        Custom,
        BaseAnomaly
    }

    public sealed class XRayAssetSource
    {
        public XRayAssetSource(string name, XRayAssetSourceKind kind, int priority, string gamedataRoot, string containerFolder)
        {
            Name = name;
            Kind = kind;
            Priority = priority;
            GamedataRoot = gamedataRoot;
            ContainerFolder = containerFolder;
        }

        public string Name { get; }
        public XRayAssetSourceKind Kind { get; }
        public int Priority { get; }
        public string GamedataRoot { get; }
        public string ContainerFolder { get; }
        public string DisplayPriority => Kind == XRayAssetSourceKind.Mod ? Priority.ToString() : Kind.ToString();

        public string GetFullPath(string relativePath)
        {
            return XRayVirtualPath.ToFileSystem(GamedataRoot, relativePath);
        }
    }
}
