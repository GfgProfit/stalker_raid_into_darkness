using System.Collections.Generic;
using XRay.Importer.Core.Omf;

namespace XRay.Importer.Core.Ogf
{
    public sealed class OgfVisual
    {
        public byte FormatVersion { get; internal set; }
        public byte ModelType { get; internal set; }
        public OgfModelKind Kind { get; internal set; }
        public ushort ShaderId { get; internal set; }
        public string Name { get; internal set; }
        public string Texture { get; internal set; }
        public string Shader { get; internal set; }
        public uint VertexFormat { get; internal set; }
        public XVector3[] Positions { get; internal set; }
        public XVector3[] Normals { get; internal set; }
        public double[] U { get; internal set; }
        public double[] V { get; internal set; }
        public OgfVertexWeights[] Weights { get; internal set; }
        public int[] Indices { get; internal set; }
        public List<OgfVisual> Children { get; } = new List<OgfVisual>();
        public bool IsSkinned => Weights != null;
        public int VertexCount => Positions == null ? 0 : Positions.Length;
        public int TriangleCount => Indices == null ? 0 : Indices.Length / 3;
    }

    public struct OgfVertexWeights
    {
        public int Count { get; internal set; }
        public int Bone0 { get; internal set; }
        public int Bone1 { get; internal set; }
        public int Bone2 { get; internal set; }
        public int Bone3 { get; internal set; }
        public double Weight0 { get; internal set; }
        public double Weight1 { get; internal set; }
        public double Weight2 { get; internal set; }
        public double Weight3 { get; internal set; }

        public int GetBone(int index)
        {
            switch (index)
            {
                case 0: return Bone0;
                case 1: return Bone1;
                case 2: return Bone2;
                default: return Bone3;
            }
        }

        public double GetWeight(int index)
        {
            switch (index)
            {
                case 0: return Weight0;
                case 1: return Weight1;
                case 2: return Weight2;
                default: return Weight3;
            }
        }
    }

    public sealed class OgfBone
    {
        public int Index { get; internal set; }
        public string Name { get; internal set; }
        public string ParentName { get; internal set; }
        public int ParentIndex { get; internal set; } = -1;
        public XVector3 BindRotation { get; internal set; }
        public XVector3 BindTranslation { get; internal set; }
        public uint IkVersion { get; internal set; }
        public string GameMaterial { get; internal set; }
        public ushort ShapeType { get; internal set; }
        public ushort ShapeFlags { get; internal set; }
        public uint JointType { get; internal set; }
        public uint IkFlags { get; internal set; }
        public double Mass { get; internal set; }
        public XVector3 MassCenter { get; internal set; }
        public double Friction { get; internal set; }
    }

    public sealed class OgfDescription
    {
        public string SourceFile { get; internal set; }
        public string BuildName { get; internal set; }
        public uint BuildTime { get; internal set; }
        public string CreatorName { get; internal set; }
        public uint CreateTime { get; internal set; }
        public string ModifierName { get; internal set; }
        public uint ModifyTime { get; internal set; }
    }

    public sealed class OgfModel
    {
        public OgfVisual Root { get; internal set; }
        public List<OgfBone> Bones { get; } = new List<OgfBone>();
        public List<string> MotionRefs { get; } = new List<string>();
        public string UserData { get; internal set; }
        public string LodReference { get; internal set; }
        public OgfDescription Description { get; internal set; }
        public OmfData EmbeddedMotions { get; internal set; }
        public List<string> Warnings { get; } = new List<string>();
        public bool HasSkeleton => Bones.Count > 0;

        public int FindBone(string name)
        {
            for (int i = 0; i < Bones.Count; i++)
            {
                if (Bones[i].Name == name)
                {
                    return i;
                }
            }

            return -1;
        }

        public List<OgfVisual> GetMeshVisuals()
        {
            var ordered = new List<OgfVisual>();
            CollectMeshes(Root, ordered);
            return ordered;
        }

        private static void CollectMeshes(OgfVisual visual, List<OgfVisual> output)
        {
            if (visual.Positions != null && visual.Indices != null)
            {
                output.Add(visual);
            }

            foreach (OgfVisual child in visual.Children)
            {
                CollectMeshes(child, output);
            }
        }
    }
}
