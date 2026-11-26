using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson
{
    public sealed class FirstPersonContactCatalog
    {
        public const int VERSION = 1;
        public const string RESOURCE_PATH = "FirstPerson/CollisionCalibration";

        private readonly Dictionary<string, FirstPersonCollisionShapeData[]> _entries = new Dictionary<string, FirstPersonCollisionShapeData[]>(StringComparer.Ordinal);

        internal FirstPersonContactCatalog(byte[] data)
        {
            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != VERSION)
            {
                throw new InvalidDataException("Unsupported first-person collision calibration version.");
            }

            int count = reader.ReadInt32();
            if (count < 0 || count > 8192)
            {
                throw new InvalidDataException("Invalid first-person collision calibration entry count.");
            }

            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                int shapeCount = reader.ReadInt32();
                if (shapeCount < 1 || shapeCount > 256)
                {
                    throw new InvalidDataException($"Invalid calibrated shape count for '{key}'.");
                }

                var shapes = new FirstPersonCollisionShapeData[shapeCount];
                for (int j = 0; j < shapeCount; j++)
                {
                    int bone = reader.ReadInt32();
                    Vector3 center = ReadVector(reader);
                    Vector3 size = ReadVector(reader);
                    shapes[j] = new FirstPersonCollisionShapeData(bone, new Bounds(center, size));
                }

                _entries.Add(key, shapes);
            }

            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Unexpected data after the first-person collision calibration entries.");
            }
        }

        internal static FirstPersonContactCatalog Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(RESOURCE_PATH);
            return asset != null ? new FirstPersonContactCatalog(asset.bytes) : null;
        }

        public static string HandsKey(XRayModel hands)
        {
            string name = hands.name;
            const string CLONE_SUFFIX = "(Clone)";
            if (name.EndsWith(CLONE_SUFFIX, StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - CLONE_SUFFIX.Length);
            }

            return $"hands|{hands.SourceFile}|{name}";
        }

        public static string MeshKey(XRayModel model, Mesh mesh)
        {
            Bounds bounds = mesh.bounds;
            Vector3 center = bounds.center;
            Vector3 size = bounds.size;
            return FormattableString.Invariant($"mesh|{model?.SourceFile}|{mesh.name}|{mesh.vertexCount}|{mesh.subMeshCount}|{center.x:R},{center.y:R},{center.z:R}|{size.x:R},{size.y:R},{size.z:R}");
        }

        internal bool TryGet(string key, out FirstPersonCollisionShapeData[] shapes) => _entries.TryGetValue(key, out shapes);

        private static Vector3 ReadVector(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}
