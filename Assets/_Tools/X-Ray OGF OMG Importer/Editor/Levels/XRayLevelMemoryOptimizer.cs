using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelMemoryOptimizer
    {
        public static string Optimize(string level, string output, bool apply)
        {
            string scene = XRayLevelPaths.ScenePath(level);
            var canonical = new Dictionary<string, Texture>();
            var keys = new Dictionary<Texture, string>();
            int bindings = 0;
            int corrections = 0;
            int materials = 0;
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Path.Combine(output, "MaterialBackups"));
            using (var writer = new StreamWriter(Path.Combine(output, "MaterialBindings.csv")))
            {
                writer.WriteLine("material,property,before,after,applied");
                foreach (string path in AssetDatabase.GetDependencies(scene, true))
                {
                    if (!path.StartsWith("Assets/_Source/Levels/Materials/", StringComparison.Ordinal) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || !(XRayAssetMarker.ReadKey(path)?.StartsWith("level|", StringComparison.Ordinal) ?? false))
                    {
                        continue;
                    }

                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    bool changed = false;
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(property);
                        if (texture == null)
                        {
                            continue;
                        }

                        Texture replacement = texture;
                        if (property == "_MonolithBumpCorrection" && material.shader.name == MonolithMaterialConfigurator.SHADER_NAME)
                        {
                            replacement = null;
                            corrections++;
                        }
                        else
                        {
                            if (!keys.TryGetValue(texture, out string key))
                            {
                                string texturePath = AssetDatabase.GetAssetPath(texture);
                                XRayAssetMarker marker = XRayAssetMarker.Read(texturePath);
                                key = null;
                                if (marker != null && texture is Texture2D texture2D)
                                {
                                    string settings = Regex.Replace(EditorJsonUtility.ToJson(AssetImporter.GetAtPath(texturePath)), "\"(?:m_UserData|userData|m_Name)\"\\s*:\\s*\"(?:\\\\.|[^\"\\\\])*\"", "");
                                    using (SHA256 sha = SHA256.Create())
                                    {
                                        key = marker.Key + "|" + texture2D.graphicsFormat + "|" + texture2D.mipmapCount + "|" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(texturePath))) + "|" + settings;
                                    }
                                }

                                keys.Add(texture, key);
                            }

                            if (key != null)
                            {
                                if (canonical.TryGetValue(key, out Texture existing))
                                {
                                    replacement = existing;
                                }
                                else
                                {
                                    canonical.Add(key, texture);
                                }
                            }
                        }

                        if (replacement == texture)
                        {
                            continue;
                        }

                        writer.WriteLine(path + "," + property + "," + AssetDatabase.GetAssetPath(texture) + "," + AssetDatabase.GetAssetPath(replacement) + "," + apply);
                        bindings++;
                        if (apply)
                        {
                            if (!changed)
                            {
                                File.Copy(path, Path.Combine(output, "MaterialBackups", AssetDatabase.AssetPathToGUID(path) + ".mat"), false);
                                Undo.RecordObject(material, "Optimize X-Ray level texture references");
                            }

                            material.SetTexture(property, replacement);
                            EditorUtility.SetDirty(material);
                        }

                        changed = true;
                    }

                    if (changed)
                    {
                        materials++;
                        if (apply)
                        {
                            AssetDatabase.SaveAssetIfDirty(material);
                        }
                    }
                }
            }

            return $"materials={materials}; changedBindings={bindings}; reservedCorrections={corrections}; applied={apply}";
        }
    }
}
