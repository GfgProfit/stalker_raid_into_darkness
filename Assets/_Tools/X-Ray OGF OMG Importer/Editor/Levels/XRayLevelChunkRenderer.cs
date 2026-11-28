using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelChunkRenderer
    {
        public int Sector { get; set; }
        public string Name { get; set; }
        public Mesh Mesh { get; set; }
        public Material[] Materials { get; set; }
        public bool CastShadows { get; set; }
    }
}
