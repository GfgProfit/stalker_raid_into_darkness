namespace XRay.Importer.Sources.Textures
{
    public enum XRayBumpMode
    {
        Reserved = 0,
        None = 1,
        Use = 2,
        UseParallax = 3
    }

    public enum XRayThmTextureType
    {
        Image = 0,
        CubeMap = 1,
        BumpMap = 2,
        NormalMap = 3,
        Terrain = 4
    }

    public sealed class XRayTextureDescription
    {
        public const uint FLAG_DIFFUSE_DETAIL = 1u << 23;
        public const uint FLAG_HAS_ALPHA = 1u << 25;
        public const uint FLAG_BUMP_DETAIL = 1u << 26;

        public string Origin { get; internal set; }
        public string BumpName { get; internal set; }
        public XRayBumpMode BumpMode { get; internal set; } = XRayBumpMode.None;
        public string DetailName { get; internal set; }
        public double DetailScale { get; internal set; }
        public double Material { get; internal set; }
        public uint MaterialId { get; internal set; } = 1;
        public double MaterialWeight { get; internal set; }
        public bool HasMaterial { get; internal set; }
        public double VirtualHeight { get; internal set; } = 0.05;
        public uint Flags { get; internal set; }
        public uint PixelFormat { get; internal set; }
        public XRayThmTextureType TextureType { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public bool HasBump => (BumpMode == XRayBumpMode.Use || BumpMode == XRayBumpMode.UseParallax) && !string.IsNullOrEmpty(BumpName);
        public bool HasAlpha => (Flags & FLAG_HAS_ALPHA) != 0;
    }
}
