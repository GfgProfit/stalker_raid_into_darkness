using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Inventory;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Ltx;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class GraEquipmentIconSetup
    {
        public const string ATLAS_PATH = "Assets/_Source/Items/Icons/gra_equipment_factions.png";

        private const string DEFINITIONS_FOLDER = "Assets/_Source/Items/Definitions/Armor";
        private const string CONFIG_PATH = "configs/plugins/eq_icons_settings.ltx";
        private const string XML_PATH = "configs/ui/textures_descr/ui_dyn_eq_icons.xml";
        private const string TEXTURE_PATH = "textures/ui/equipment_icons.dds";

        [MenuItem("Tools/Inventory/Import GRA Equipment Faction Icons")]
        public static void ImportFromMenu()
        {
            Debug.Log(Import());
        }

        public static string Import()
        {
            StalkerXRaySources sources = StalkerXRaySources.Open(new[] { "configs", "textures/ui" }, out string error);

            if (sources == null)
            {
                throw new InvalidOperationException(error);
            }

            XRayLtxDatabase config = XRayLtxDatabase.LoadFile(sources.Index, CONFIG_PATH);
            XDocument xml = XDocument.Load(sources.GetFullPath(XML_PATH));
            byte[] data = sources.ReadBytes(TEXTURE_PATH);
            DdsHeader header = DdsHeader.Parse(data);

            if (DdsDecoder.CanDecode(header) == false)
            {
                throw new InvalidOperationException("Unsupported GRA equipment icon atlas: " + header.Description);
            }

            var textureRects = new Dictionary<string, Rect>(StringComparer.Ordinal);

            foreach (XElement texture in xml.Descendants("texture"))
            {
                int height = (int)texture.Attribute("height");
                textureRects[(string)texture.Attribute("id")] = new Rect((int)texture.Attribute("x"), header.Height - (int)texture.Attribute("y") - height, (int)texture.Attribute("width"), height);
            }

            var definitions = new List<ItemDefinition>();
            var communities = new Dictionary<string, (string Icon, Vector2 Size)>(StringComparer.Ordinal);

            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { DEFINITIONS_FOLDER }))
            {
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                EquipmentStatsComponent stats = definition.GetComponent<EquipmentStatsComponent>();

                if (stats == null || stats.Community.Length == 0)
                {
                    continue;
                }

                string[] fields = (config.GetValue("comm_to_texture", stats.Community) ?? string.Empty).Split(',');

                if (fields.Length != 3 || textureRects.ContainsKey(fields[0].Trim()) == false || StalkerXRaySources.TryFloat(fields[1], out float width) == false || StalkerXRaySources.TryFloat(fields[2], out float height) == false)
                {
                    throw new InvalidOperationException("Missing GRA faction icon: " + stats.Section + "/" + stats.Community);
                }

                communities[stats.Community] = (fields[0].Trim(), new Vector2(width, height));
                definitions.Add(definition);
            }

            var rects = new List<KeyValuePair<string, Rect>>();

            foreach (var entry in communities.Values)
            {
                rects.Add(new KeyValuePair<string, Rect>(entry.Icon, textureRects[entry.Icon]));
            }

            File.WriteAllBytes(Path.GetFullPath(ATLAS_PATH), GraWeaponAddonSetup.EncodePng(DdsDecoder.DecodeTopLevel(data, header), header.Width, header.Height));
            AssetDatabase.ImportAsset(ATLAS_PATH, ImportAssetOptions.ForceSynchronousImport);
            GraWeaponAddonSetup.ConfigureSprites(ATLAS_PATH, Math.Max(header.Width, header.Height), rects);
            var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ATLAS_PATH))
            {
                if (asset is Sprite sprite)
                {
                    sprites[sprite.name] = sprite;
                }
            }

            foreach (ItemDefinition definition in definitions)
            {
                var entry = communities[definition.GetComponent<EquipmentStatsComponent>().Community];
                var serialized = new SerializedObject(definition);
                SerializedProperty components = serialized.FindProperty("_components");

                for (int i = 0; i < components.arraySize; i++)
                {
                    SerializedProperty component = components.GetArrayElementAtIndex(i);

                    if (component.managedReferenceValue is EquipmentStatsComponent)
                    {
                        component.FindPropertyRelative("_communityIcon").objectReferenceValue = sprites[entry.Icon];
                        component.FindPropertyRelative("_communityIconSize").vector2Value = entry.Size;
                    }
                }

                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(definition);
            }

            AssetDatabase.SaveAssets();
            return $"Imported {sprites.Count} GRA faction sprites and assigned {definitions.Count} outfits. Source: {sources.GetSourceName(TEXTURE_PATH)}.";
        }
    }
}
