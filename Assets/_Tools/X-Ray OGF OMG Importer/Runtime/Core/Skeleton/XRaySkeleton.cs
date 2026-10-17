using System.Collections.Generic;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Ogf;

namespace XRay.Importer.Core.Skeleton
{
    public sealed class XRaySkeleton
    {
        public XRaySkeleton(IList<OgfBone> bones)
        {
            ValidateHierarchy(bones);
            int count = bones.Count;
            Names = new string[count];
            Parents = new int[count];
            LocalPositions = new XVector3[count];
            LocalRotations = new XQuaternion[count];
            RestLocal = new XMatrix4[count];
            RestWorld = new XMatrix4[count];
            var computed = new bool[count];

            for (int i = 0; i < count; i++)
            {
                OgfBone bone = bones[i];
                Names[i] = bone.Name;
                Parents[i] = bone.ParentIndex;
                LocalPositions[i] = bone.BindTranslation;
                LocalRotations[i] = XQuaternion.FromXRayBindEuler(bone.BindRotation);
                RestLocal[i] = XMatrix4.TranslationRotation(LocalPositions[i], LocalRotations[i]);
            }

            for (int i = 0; i < count; i++)
            {
                ComputeWorld(i, computed);
            }
        }

        public string[] Names { get; }
        public int[] Parents { get; }
        public XVector3[] LocalPositions { get; }
        public XQuaternion[] LocalRotations { get; }
        public XMatrix4[] RestLocal { get; }
        public XMatrix4[] RestWorld { get; }
        public int Count => Names.Length;

        public int IndexOf(string name)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                if (Names[i] == name)
                {
                    return i;
                }
            }

            return -1;
        }

        public string GetPath(int index)
        {
            var parts = new List<string>();
            for (int current = index; current >= 0; current = Parents[current])
            {
                parts.Add(Names[current]);
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        public static void ValidateHierarchy(IList<OgfBone> bones)
        {
            var seen = new HashSet<string>();
            foreach (OgfBone bone in bones)
            {
                if (string.IsNullOrEmpty(bone.Name))
                {
                    throw new XRayFormatException($"Bone #{bone.Index} has an empty name.");
                }

                if (!seen.Add(bone.Name))
                {
                    throw new XRayFormatException($"Duplicate bone name '{bone.Name}'.");
                }
            }

            for (int i = 0; i < bones.Count; i++)
            {
                int steps = 0;
                for (int current = bones[i].ParentIndex; current >= 0; current = bones[current].ParentIndex)
                {
                    if (++steps > bones.Count)
                    {
                        throw new XRayFormatException($"Bone hierarchy contains a cycle through '{bones[i].Name}'.");
                    }
                }
            }
        }

        private XMatrix4 ComputeWorld(int index, bool[] computed)
        {
            if (computed[index])
            {
                return RestWorld[index];
            }

            int parent = Parents[index];
            RestWorld[index] = parent >= 0 ? ComputeWorld(parent, computed) * RestLocal[index] : RestLocal[index];
            computed[index] = true;
            return RestWorld[index];
        }
    }
}
