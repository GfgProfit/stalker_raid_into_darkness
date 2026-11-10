using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayLevelAnomalyCatalogTests
    {
        [Test]
        public void DefaultCatalogCoversObservedSpawnSectionsAndReportsMissingFamilies()
        {
            XRayLevelAnomalyCatalog catalog = XRayLevelAnomalyCatalog.LoadDefault();
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Validate(out string error), Is.True, error);
            Assert.That(catalog.Entries.Count, Is.EqualTo(66));
            AssertPrefab(catalog, "zone_mine_electric_average", "Electra");
            AssertPrefab(catalog, "zone_mine_thermal_average", "Zharka");
            AssertPrefab(catalog, "zone_mine_gravitational_weak", "Tramplin");
            AssertPrefab(catalog, "zone_mine_gravitational_average", "Vortex");
            AssertPrefab(catalog, "zone_mine_gravitational_strong", "Carousel");
            AssertPrefab(catalog, "zone_mine_chemical_average", "Kisel");
            AssertPrefab(catalog, "zone_mine_acidic_average", "Gazirovka");
            Assert.That(catalog.TryGet("zone_burning_fuzz1", out XRayLevelAnomalyEntry missing), Is.True);
            Assert.That(missing.Prefab, Is.Null);
            Assert.That(missing.DisplayName, Does.Contain("пух"));
            Assert.That(catalog.TryGet("zone_teleport", out XRayLevelAnomalyEntry teleport), Is.True);
            Assert.That(teleport.Prefab, Is.Null);
        }

        [Test]
        public void LookupRequiresExactSectionAndDoesNotSubstituteOtherFamilies()
        {
            XRayLevelAnomalyCatalog catalog = XRayLevelAnomalyCatalog.LoadDefault();
            Assert.That(catalog.TryGet("zone_mine_electric_average", out XRayLevelAnomalyEntry exact), Is.True);
            Assert.That(catalog.TryGet("ZONE_MINE_ELECTRIC_AVERAGE", out XRayLevelAnomalyEntry uppercase), Is.True);
            Assert.That(uppercase, Is.SameAs(exact));
            Assert.That(catalog.TryGet("zone_mine_electric_unimplemented", out XRayLevelAnomalyEntry missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(catalog.TryGet(null, out missing), Is.False);
        }

        [Test]
        public void SourcePrefabAssignmentsKeepActivationSphereOnPrefabRoot()
        {
            XRayLevelAnomalyCatalog catalog = XRayLevelAnomalyCatalog.LoadDefault();
            foreach (XRayLevelAnomalyEntry entry in catalog.Entries)
            {
                if (entry.Prefab == null)
                {
                    continue;
                }

                Assert.That(entry.Prefab.transform.parent, Is.Null, entry.Section);
                SphereCollider volume = entry.Prefab.GetComponent<SphereCollider>();
                Assert.That(volume, Is.Not.Null, entry.Section);
                Assert.That(volume.enabled && volume.isTrigger && volume.radius > 0f, Is.True, entry.Section);
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(entry.Prefab), Is.Zero, entry.Section);
                Assert.That(entry.DisplayName, Is.Not.EqualTo(entry.Section), entry.Section);
            }
        }

        [Test]
        public void MissingPrefabIsValidButDuplicateSectionsAreRejected()
        {
            XRayLevelAnomalyCatalog catalog = ScriptableObject.CreateInstance<XRayLevelAnomalyCatalog>();
            try
            {
                SetSections(catalog, "zone_mine_electric_average");
                Assert.That(catalog.Validate(out string error), Is.True, error);
                Assert.That(catalog.TryGet("zone_mine_electric_average", out XRayLevelAnomalyEntry entry), Is.True);
                Assert.That(entry.Prefab, Is.Null);
                SetSections(catalog, "zone_mine_electric_average", "ZONE_MINE_ELECTRIC_AVERAGE");
                Assert.That(catalog.Validate(out error), Is.False);
                Assert.That(error, Does.Contain("повторяется"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void SceneObjectsCannotBecomeImportPrefabAssignments()
        {
            XRayLevelAnomalyCatalog catalog = ScriptableObject.CreateInstance<XRayLevelAnomalyCatalog>();
            var sceneObject = new GameObject("SceneAnomaly");
            try
            {
                SetSections(catalog, "zone_mine_electric_average");
                var data = new SerializedObject(catalog);
                data.FindProperty("_entries").GetArrayElementAtIndex(0).FindPropertyRelative("_prefab").objectReferenceValue = sceneObject;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(catalog.Validate(out string error), Is.False);
                Assert.That(error, Does.Contain("prefab asset"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sceneObject);
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        private static void AssertPrefab(XRayLevelAnomalyCatalog catalog, string section, string family)
        {
            Assert.That(catalog.TryGet(section, out XRayLevelAnomalyEntry entry), Is.True, section);
            Assert.That(entry.Prefab, Is.Not.Null, section);
            Assert.That(AssetDatabase.GetAssetPath(entry.Prefab), Is.EqualTo("Assets/_Source/Anomalies/" + family + "/" + family + ".prefab"), section);
        }

        private static void SetSections(XRayLevelAnomalyCatalog catalog, params string[] sections)
        {
            var data = new SerializedObject(catalog);
            SerializedProperty entries = data.FindProperty("_entries");
            entries.arraySize = sections.Length;
            for (int i = 0; i < sections.Length; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("_section").stringValue = sections[i];
                entry.FindPropertyRelative("_displayName").stringValue = sections[i];
                entry.FindPropertyRelative("_prefab").objectReferenceValue = null;
            }

            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
