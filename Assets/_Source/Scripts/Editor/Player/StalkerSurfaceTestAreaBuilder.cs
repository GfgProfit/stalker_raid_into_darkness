using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerSurfaceTestAreaBuilder
    {
        public const string GROUP_NAME = "SurfaceTestPads";
        public const string ASSET_FOLDER = "Assets/_Source/Settings/Player/TestArea/Surfaces";
        public const float PAD_SIZE = 2.5f;
        public const float PAD_SPACING = 3.0f;
        public const float PAD_Z = -12.0f;
        public const float TERRAIN_Z = -22.0f;
        public const float TERRAIN_SIZE = 8.0f;

        private const string HDRP_LIT = "HDRP/Lit";
        private const float PAD_THICKNESS = 0.04f;
        private const float TERRAIN_LIFT = 0.02f;
        private const int TERRAIN_RESOLUTION = 33;
        private const int ALPHAMAP_RESOLUTION = 16;

        private readonly struct PadDefinition
        {
            public PadDefinition(StalkerSurfaceType type, bool physicsMaterial, Color color)
            {
                Type = type;
                PhysicsMaterial = physicsMaterial;
                Color = color;
            }

            public StalkerSurfaceType Type { get; }
            public bool PhysicsMaterial { get; }
            public Color Color { get; }
        }

        private static readonly PadDefinition[] PADS =
        {
            new PadDefinition(StalkerSurfaceType.Grass, false, new Color(0.25f, 0.42f, 0.18f)),
            new PadDefinition(StalkerSurfaceType.Gravel, false, new Color(0.5f, 0.48f, 0.44f)),
            new PadDefinition(StalkerSurfaceType.Asphalt, false, new Color(0.16f, 0.16f, 0.17f)),
            new PadDefinition(StalkerSurfaceType.Concrete, true, new Color(0.62f, 0.62f, 0.6f)),
            new PadDefinition(StalkerSurfaceType.Metal, true, new Color(0.35f, 0.38f, 0.42f)),
            new PadDefinition(StalkerSurfaceType.MetalPlate, false, new Color(0.45f, 0.4f, 0.35f)),
            new PadDefinition(StalkerSurfaceType.Wood, false, new Color(0.45f, 0.3f, 0.16f)),
            new PadDefinition(StalkerSurfaceType.WoodenBoard, false, new Color(0.55f, 0.4f, 0.24f)),
            new PadDefinition(StalkerSurfaceType.Tin, false, new Color(0.5f, 0.55f, 0.58f)),
            new PadDefinition(StalkerSurfaceType.Water, false, new Color(0.12f, 0.25f, 0.35f))
        };

        public static GameObject Build(Transform parent, StalkerSurfaceAudioLibrary library)
        {
            Transform existing = parent.Find(GROUP_NAME);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var group = new GameObject(GROUP_NAME);
            group.transform.SetParent(parent, false);
            var physicsMaterials = new List<StalkerPhysicsMaterialSurface>();
            for (int i = 0; i < PADS.Length; i++)
            {
                PadDefinition pad = PADS[i];
                float x = (i - (PADS.Length - 1) * 0.5f) * PAD_SPACING;
                float ground = GroundHeight(new Vector3(x, 0.0f, PAD_Z));
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Pad_" + pad.Type;
                box.transform.SetParent(group.transform, false);
                box.transform.position = new Vector3(x, ground, PAD_Z);
                box.transform.localScale = new Vector3(PAD_SIZE, PAD_THICKNESS, PAD_SIZE);
                box.isStatic = true;
                box.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("Pad_" + pad.Type, pad.Color);
                if (pad.PhysicsMaterial)
                {
                    PhysicsMaterial material = GetPhysicsMaterial(pad.Type);
                    box.GetComponent<Collider>().sharedMaterial = material;
                    physicsMaterials.Add(new StalkerPhysicsMaterialSurface(material, pad.Type));
                }
                else
                {
                    box.AddComponent<StalkerSurface>().Type = pad.Type;
                }
            }

            var layers = new List<StalkerTerrainLayerSurface>();
            BuildTerrain(group.transform, layers);
            if (library != null)
            {
                library.SetSurfaceMappings(physicsMaterials, layers);
                EditorUtility.SetDirty(library);
            }

            AssetDatabase.SaveAssets();
            return group;
        }

        private static void BuildTerrain(Transform parent, List<StalkerTerrainLayerSurface> layers)
        {
            TerrainLayer grass = GetTerrainLayer("Terrain_Grass", new Color(0.27f, 0.4f, 0.2f));
            TerrainLayer gravel = GetTerrainLayer("Terrain_Gravel", new Color(0.52f, 0.5f, 0.46f));
            layers.Add(new StalkerTerrainLayerSurface(grass, StalkerSurfaceType.Grass));
            layers.Add(new StalkerTerrainLayerSurface(gravel, StalkerSurfaceType.Gravel));
            string path = ASSET_FOLDER + "/SurfaceTestTerrain.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, path);
            }

            data.heightmapResolution = TERRAIN_RESOLUTION;
            data.alphamapResolution = ALPHAMAP_RESOLUTION;
            data.size = new Vector3(TERRAIN_SIZE, 1.0f, TERRAIN_SIZE);
            data.SetHeights(0, 0, new float[TERRAIN_RESOLUTION, TERRAIN_RESOLUTION]);
            data.terrainLayers = new[] { grass, gravel };
            var alphas = new float[ALPHAMAP_RESOLUTION, ALPHAMAP_RESOLUTION, 2];
            for (int z = 0; z < ALPHAMAP_RESOLUTION; z++)
            {
                for (int x = 0; x < ALPHAMAP_RESOLUTION; x++)
                {
                    bool left = x < ALPHAMAP_RESOLUTION / 2;
                    alphas[z, x, 0] = left ? 1.0f : 0.0f;
                    alphas[z, x, 1] = left ? 0.0f : 1.0f;
                }
            }

            data.SetAlphamaps(0, 0, alphas);
            EditorUtility.SetDirty(data);
            float ground = GroundHeight(new Vector3(0.0f, 0.0f, TERRAIN_Z));
            GameObject terrain = Terrain.CreateTerrainGameObject(data);
            terrain.name = "Terrain_GrassLeft_GravelRight";
            terrain.transform.SetParent(parent, false);
            terrain.transform.position = new Vector3(-TERRAIN_SIZE * 0.5f, ground + TERRAIN_LIFT, TERRAIN_Z - TERRAIN_SIZE * 0.5f);
            terrain.isStatic = true;
        }

        private static float GroundHeight(Vector3 position)
        {
            Physics.SyncTransforms();
            Vector3 origin = new Vector3(position.x, 50.0f, position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 100.0f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponentInParent<StalkerSurface>() == null && hit.collider.GetComponentInParent<CharacterController>() == null && hit.point.y < 1.0f && hit.point.y > best)
                {
                    best = hit.point.y;
                }
            }

            return float.IsNegativeInfinity(best) ? 0.0f : best;
        }

        private static PhysicsMaterial GetPhysicsMaterial(StalkerSurfaceType type)
        {
            string path = ASSET_FOLDER + "/Surface_" + type + ".asset";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                StalkerXRaySources.EnsureFolder(ASSET_FOLDER);
                material = new PhysicsMaterial("Surface_" + type);
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        private static Material GetMaterial(string name, Color color)
        {
            string path = ASSET_FOLDER + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find(HDRP_LIT);
            if (shader == null)
            {
                return null;
            }

            StalkerXRaySources.EnsureFolder(ASSET_FOLDER);
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, null, null, null, true);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static TerrainLayer GetTerrainLayer(string name, Color color)
        {
            string path = ASSET_FOLDER + "/" + name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer != null)
            {
                return layer;
            }

            StalkerXRaySources.EnsureFolder(ASSET_FOLDER);
            string texturePath = ASSET_FOLDER + "/" + name + ".png";
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels(pixels);
            File.WriteAllBytes(Path.GetFullPath(texturePath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            layer = new TerrainLayer { name = name, diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath), tileSize = new Vector2(2.0f, 2.0f) };
            AssetDatabase.CreateAsset(layer, path);
            return layer;
        }
    }
}
