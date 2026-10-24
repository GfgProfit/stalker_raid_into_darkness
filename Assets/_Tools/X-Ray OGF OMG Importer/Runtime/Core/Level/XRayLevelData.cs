using System.Collections.Generic;

namespace XRay.Importer.Core.Level
{
    public sealed class XRayLevelShader
    {
        public XRayLevelShader(int index, string raw)
        {
            Index = index;
            Raw = raw ?? string.Empty;
            int separator = Raw.IndexOf('/');
            EngineShader = separator >= 0 ? Raw.Substring(0, separator) : Raw;
            string textures = separator >= 0 ? Raw.Substring(separator + 1) : string.Empty;
            string[] parts = textures.Length > 0 ? textures.Split(',') : new string[0];
            Texture = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            var lightmaps = new List<string>();
            for (int i = 1; i < parts.Length; i++)
            {
                lightmaps.Add(parts[i].Trim());
            }

            Lightmaps = lightmaps;
        }

        public int Index { get; }
        public string Raw { get; }
        public string EngineShader { get; }
        public string Texture { get; }
        public IReadOnlyList<string> Lightmaps { get; }
        public bool IsEmpty => Raw.Length == 0;
    }

    public readonly struct XRayGeometryRange
    {
        public XRayGeometryRange(int vertexBuffer, int vertexOffset, int vertexCount, int indexBuffer, int indexOffset, int indexCount)
        {
            VertexBuffer = vertexBuffer;
            VertexOffset = vertexOffset;
            VertexCount = vertexCount;
            IndexBuffer = indexBuffer;
            IndexOffset = indexOffset;
            IndexCount = indexCount;
        }

        public int VertexBuffer { get; }
        public int VertexOffset { get; }
        public int VertexCount { get; }
        public int IndexBuffer { get; }
        public int IndexOffset { get; }
        public int IndexCount { get; }
    }

    public sealed class XRayTreeDefinition
    {
        public float[] Matrix { get; } = new float[16];
        public float[] ScaleRgb { get; } = new float[3];
        public float ScaleHemi { get; set; }
        public float ScaleSun { get; set; }
        public float[] BiasRgb { get; } = new float[3];
        public float BiasHemi { get; set; }
        public float BiasSun { get; set; }
    }

    public readonly struct XRayLodVertex
    {
        public XRayLodVertex(XVector3 position, float u, float v, uint rgbHemi, byte sun)
        {
            Position = position;
            U = u;
            V = v;
            RgbHemi = rgbHemi;
            Sun = sun;
        }

        public XVector3 Position { get; }
        public float U { get; }
        public float V { get; }
        public uint RgbHemi { get; }
        public byte Sun { get; }
    }

    public sealed class XRayLevelVisual
    {
        public int Index { get; internal set; }
        public byte ModelType { get; internal set; }
        public int ShaderId { get; internal set; }
        public float[] BoundsMin { get; } = new float[3];
        public float[] BoundsMax { get; } = new float[3];
        public float[] SphereCenter { get; } = new float[3];
        public float SphereRadius { get; internal set; }
        public bool HasGeometry { get; internal set; }
        public XRayGeometryRange Geometry { get; internal set; }
        public int SwiOffset { get; internal set; }
        public int SwiTriangles { get; internal set; } = -1;
        public int SwiVertices { get; internal set; } = -1;
        public int SwiContainer { get; internal set; } = -1;
        public bool HasFastPath { get; internal set; }
        public XRayTreeDefinition Tree { get; internal set; }
        public List<int> Children { get; } = new List<int>();
        public int LodFaceCount { get; internal set; }
        public XRayLodVertex[] LodVertices { get; internal set; }
        public int Parent { get; internal set; } = -1;
        public int Sector { get; internal set; } = -1;
        public bool IsTree => ModelType == XRayLevelFormat.MT_TREE_ST || ModelType == XRayLevelFormat.MT_TREE_PM;
        public bool IsGeometry => ModelType == XRayLevelFormat.MT_NORMAL || ModelType == XRayLevelFormat.MT_PROGRESSIVE || IsTree;
    }

    public sealed class XRayLevelSector
    {
        public int Index { get; internal set; }
        public int RootVisual { get; internal set; } = -1;
        public List<int> Portals { get; } = new List<int>();
    }

    public sealed class XRayLevelPortal
    {
        public int Index { get; internal set; }
        public int SectorFront { get; internal set; }
        public int SectorBack { get; internal set; }
        public List<XVector3> Vertices { get; } = new List<XVector3>();
    }

    public sealed class XRayLevelLight
    {
        public int Index { get; internal set; }
        public int Controller { get; internal set; }
        public int Type { get; internal set; }
        public float[] Diffuse { get; } = new float[4];
        public float[] Position { get; } = new float[3];
        public float[] Direction { get; } = new float[3];
        public float Range { get; internal set; }
        public float Falloff { get; internal set; }
        public float Attenuation0 { get; internal set; }
        public float Attenuation1 { get; internal set; }
        public float Attenuation2 { get; internal set; }
        public float Theta { get; internal set; }
        public float Phi { get; internal set; }
    }

    public sealed class XRayLevelGlow
    {
        public float[] Position { get; } = new float[3];
        public float Radius { get; internal set; }
        public int Shader { get; internal set; }
    }

    public sealed class XRayLevelData
    {
        public ushort Version { get; internal set; }
        public ushort Quality { get; internal set; }
        public List<XRayLevelShader> Shaders { get; } = new List<XRayLevelShader>();
        public List<XRayLevelVisual> Visuals { get; } = new List<XRayLevelVisual>();
        public List<XRayLevelSector> Sectors { get; } = new List<XRayLevelSector>();
        public List<XRayLevelPortal> Portals { get; } = new List<XRayLevelPortal>();
        public List<XRayLevelLight> Lights { get; } = new List<XRayLevelLight>();
        public List<XRayLevelGlow> Glows { get; } = new List<XRayLevelGlow>();
        public List<string> Warnings { get; } = new List<string>();
        public List<uint> UnknownChunks { get; } = new List<uint>();
    }
}
