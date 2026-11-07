using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerTestAreaBuilder
    {
        public const string MATERIAL_FOLDER = "Assets/_Source/Settings/Player/TestArea";

        private const string HDRP_LIT = "HDRP/Lit";
        private const float GROUND_SIZE = 120.0f;

        public static readonly Vector3 SpawnPosition = new Vector3(0.0f, 0.02f, -6.0f);

        public static GameObject Build(bool createGround)
        {
            Material blocks = GetMaterial("TestBlocks", new Color(0.62f, 0.6f, 0.55f));
            var area = new GameObject(StalkerPlayerSetup.TEST_AREA);
            if (createGround)
            {
                Box(area, "Ground", new Vector3(0.0f, -0.25f, 0.0f), new Vector3(GROUND_SIZE, 0.5f, GROUND_SIZE), Quaternion.identity, GetMaterial("TestGround", new Color(0.42f, 0.42f, 0.4f)));
            }

            var slopes = Group(area, "Slopes");
            float[] angles = { 20.0f, 35.0f, 44.0f, 50.0f };
            for (int i = 0; i < angles.Length; i++)
            {
                float length = 6.0f;
                float height = Mathf.Sin(angles[i] * Mathf.Deg2Rad) * length * 0.5f;
                float run = Mathf.Cos(angles[i] * Mathf.Deg2Rad) * length * 0.5f;
                Box(slopes, $"Slope_{angles[i]:0}deg", new Vector3(8.0f + i * 4.0f, height - 0.2f, 6.0f + run), new Vector3(3.0f, 0.4f, length), Quaternion.Euler(-angles[i], 0.0f, 0.0f), blocks);
            }

            var steps = Group(area, "Stairs_0.15m");
            for (int i = 0; i < 10; i++)
            {
                float top = 0.15f * (i + 1);
                Box(steps, $"Step_{i + 1}", new Vector3(-8.0f, top * 0.5f, 4.0f + i * 0.3f), new Vector3(2.5f, top, 0.3f), Quaternion.identity, blocks);
            }

            var ledges = Group(area, "Ledges");
            float[] heights = { 0.3f, 0.5f, 0.65f, 0.8f, 1.0f };
            for (int i = 0; i < heights.Length; i++)
            {
                Box(ledges, "Ledge_" + heights[i].ToString("0.00", CultureInfo.InvariantCulture) + "m", new Vector3(-14.0f - i * 3.0f, heights[i] * 0.5f, 6.0f), new Vector3(2.0f, heights[i], 2.0f), Quaternion.identity, blocks);
            }

            var tunnels = Group(area, "LowCeilings");
            Tunnel(tunnels, "Ceiling_1.45m_Crouch", new Vector3(0.0f, 0.0f, 10.0f), 1.45f, blocks);
            Tunnel(tunnels, "Ceiling_0.75m_LowCrouch", new Vector3(4.0f, 0.0f, 10.0f), 0.75f, blocks);
            return area;
        }

        private static void Tunnel(GameObject parent, string name, Vector3 origin, float clearance, Material material)
        {
            var tunnel = Group(parent, name);
            tunnel.transform.localPosition = origin;
            const float LENGTH = 6.0f;
            const float WIDTH = 2.0f;
            const float THICKNESS = 0.2f;
            Box(tunnel, "Roof", new Vector3(0.0f, clearance + THICKNESS * 0.5f, LENGTH * 0.5f), new Vector3(WIDTH + THICKNESS * 2.0f, THICKNESS, LENGTH), Quaternion.identity, material);
            Box(tunnel, "WallLeft", new Vector3(-(WIDTH + THICKNESS) * 0.5f, clearance * 0.5f, LENGTH * 0.5f), new Vector3(THICKNESS, clearance, LENGTH), Quaternion.identity, material);
            Box(tunnel, "WallRight", new Vector3((WIDTH + THICKNESS) * 0.5f, clearance * 0.5f, LENGTH * 0.5f), new Vector3(THICKNESS, clearance, LENGTH), Quaternion.identity, material);
        }

        private static GameObject Group(GameObject parent, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent.transform, false);
            return group;
        }

        private static void Box(GameObject parent, string name, Vector3 position, Vector3 size, Quaternion rotation, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform, false);
            box.transform.localPosition = position;
            box.transform.localRotation = rotation;
            box.transform.localScale = size;
            box.isStatic = true;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material GetMaterial(string name, Color color)
        {
            string path = MATERIAL_FOLDER + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find(HDRP_LIT);
            if (shader == null)
            {
                Debug.LogWarning($"[Player] Shader '{HDRP_LIT}' not found; the test area uses the default material.");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(MATERIAL_FOLDER))
            {
                Directory.CreateDirectory(Path.GetFullPath(MATERIAL_FOLDER));
                AssetDatabase.Refresh();
            }

            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, null, null, null, true);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
