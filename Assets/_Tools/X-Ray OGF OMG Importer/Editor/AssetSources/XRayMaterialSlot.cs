using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Ogf;

namespace XRay.Importer.Editor
{
    public sealed class XRayMaterialSlot
    {
        public string Key { get; private set; }
        public string Texture { get; private set; }
        public string Shader { get; private set; }
        public List<int> MeshIndices { get; } = new List<int>();

        public static string MakeKey(string texture, string shader)
        {
            return (texture ?? string.Empty) + "|" + (shader ?? string.Empty);
        }

        public static AssetImporter.SourceAssetIdentifier MakeIdentifier(string key)
        {
            return new AssetImporter.SourceAssetIdentifier(typeof(Material), key);
        }

        public static List<XRayMaterialSlot> Collect(OgfModel model)
        {
            var slots = new List<XRayMaterialSlot>();
            var byKey = new Dictionary<string, XRayMaterialSlot>();
            List<OgfVisual> visuals = model.GetMeshVisuals();
            for (int i = 0; i < visuals.Count; i++)
            {
                string key = MakeKey(visuals[i].Texture, visuals[i].Shader);
                if (!byKey.TryGetValue(key, out XRayMaterialSlot slot))
                {
                    slot = new XRayMaterialSlot { Key = key, Texture = visuals[i].Texture, Shader = visuals[i].Shader };
                    byKey.Add(key, slot);
                    slots.Add(slot);
                }

                slot.MeshIndices.Add(i);
            }

            return slots;
        }
    }
}
