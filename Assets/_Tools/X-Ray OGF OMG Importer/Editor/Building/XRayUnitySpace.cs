using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Core;
using XRay.Importer.Core.Skeleton;

namespace XRay.Importer.Editor
{
    public static class XRayUnitySpace
    {
        public static Vector3 Position(XVector3 value, float scale)
        {
            return new Vector3((float)value.X * scale, (float)value.Y * scale, (float)value.Z * scale);
        }

        public static Vector3 Direction(XVector3 value)
        {
            return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
        }

        public static Quaternion Rotation(XQuaternion value)
        {
            return new Quaternion((float)value.X, (float)value.Y, (float)value.Z, (float)value.W);
        }
    }

    public sealed class XRayRigPaths
    {
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>();

        public XRayRigPaths(XRaySkeleton skeleton, List<string> warnings)
        {
            ObjectNames = new string[skeleton.Count];
            var used = new HashSet<string>();
            for (int i = 0; i < skeleton.Count; i++)
            {
                string objectName = skeleton.Names[i].Replace('/', '_');
                if (!used.Add(objectName))
                {
                    objectName = objectName + "_" + i;
                    used.Add(objectName);
                }

                if (objectName != skeleton.Names[i] && warnings != null)
                {
                    warnings.Add($"Bone '{skeleton.Names[i]}' is named '{objectName}' in Unity ('/' is the animation path separator).");
                }

                ObjectNames[i] = objectName;
            }

            for (int i = 0; i < skeleton.Count; i++)
            {
                var parts = new List<string>();
                for (int current = i; current >= 0; current = skeleton.Parents[current])
                {
                    parts.Add(ObjectNames[current]);
                }

                parts.Reverse();
                _paths[skeleton.Names[i]] = string.Join("/", parts);
            }
        }

        public string[] ObjectNames { get; }

        public bool TryGetPath(string boneName, out string path)
        {
            return _paths.TryGetValue(boneName, out path);
        }
    }
}
