using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Core;
using XRay.Importer.Core.Spawn;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayLevelAnomalyPlacementTests
    {
        private Scene _scene;
        private GameObject _root;
        private XRayLevelAnomalyCatalog _catalog;
        private string _fixtureFolder;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("Level");
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _catalog = XRayLevelAnomalyCatalog.LoadDefault();
            Assert.That(_catalog, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(_scene);
            if (!string.IsNullOrEmpty(_fixtureFolder))
            {
                AssetDatabase.DeleteAsset(_fixtureFolder);
                _fixtureFolder = null;
            }
        }

        [Test]
        public void SphereUsesLocalSpawnPoseAndBoundsWithoutChangingTheSourcePrefab()
        {
            XRayLevelSpawnData data = Data();
            XRaySpawnAnomaly anomaly = Anomaly("zone_mine_acidic_average", 1);
            Set(anomaly, "Position", new XVector3(12, 4, -7));
            Set(anomaly, "Angles", new XVector3(0, Math.PI * 0.5, 0));
            anomaly.Shapes.Add(Sphere(new XVector3(0.25, 1.5, -0.75), 7.25));
            data.Anomalies.Add(anomaly);
            data.Anomalies.Add(Anomaly("zone_mine_electric_average", 2, "other"));
            Assert.That(_catalog.TryGet(anomaly.Section, out XRayLevelAnomalyEntry entry), Is.True);
            float originalRadius = entry.Prefab.GetComponent<SphereCollider>().radius;
            XRayLevelAnomalyPlacementResult result = Apply(data);
            Assert.That(result.SourceCount, Is.EqualTo(1));
            Assert.That(result.ImportedCount, Is.EqualTo(1));
            Transform instance = result.Group.transform.GetChild(0);
            Assert.That(instance.localPosition, Is.EqualTo(new Vector3(12, 4, -7)));
            Assert.That(Vector3.Distance(instance.forward, Vector3.left), Is.LessThan(0.0001f));
            SphereCollider volume = instance.GetComponent<SphereCollider>();
            Assert.That(volume.radius, Is.EqualTo(7.25f));
            Assert.That(volume.center, Is.EqualTo(new Vector3(0.25f, 1.5f, -0.75f)));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject), Is.SameAs(entry.Prefab));
            foreach (MonoBehaviour component in instance.GetComponents<MonoBehaviour>())
            {
                SerializedProperty randomize = new SerializedObject(component).FindProperty("_randomizeRadius");
                if (randomize != null)
                {
                    Assert.That(randomize.boolValue, Is.False);
                }
            }

            Assert.That(entry.Prefab.GetComponent<SphereCollider>().radius, Is.EqualTo(originalRadius));
            Assert.That(result.Group.SpawnAssetPath, Is.EqualTo("Assets/shared_all.spawn"));
            Assert.That(result.Group.SourceSha256, Is.EqualTo("source-hash"));
        }

        [Test]
        public void BoxUsesFullSideLengthsAndKeepsVisualChildrenAtTheirSourceWorldPoses()
        {
            XRayLevelSpawnData data = Data();
            XRaySpawnAnomaly anomaly = Anomaly("zone_mine_electric_average", 5);
            Set(anomaly, "Position", new XVector3(11, -3, 9));
            Set(anomaly, "Angles", new XVector3(0.3, 0.5, -0.2));
            XMatrix4 matrix = XMatrix4.FromRows(0, 0, 8, 1, 0, 6, 0, 2, -4, 0, 0, 3, 0, 0, 0, 1);
            var shape = new XRaySpawnShape();
            Set(shape, "Type", XRaySpawnShapeType.Box);
            Set(shape, "Center", new XVector3(1, 2, 3));
            Set(shape, "Box", matrix);
            anomaly.Shapes.Add(shape);
            data.Anomalies.Add(anomaly);
            Assert.That(_catalog.TryGet(anomaly.Section, out XRayLevelAnomalyEntry entry), Is.True);
            XRayLevelAnomalyPlacementResult result = Apply(data);
            Transform instance = result.Group.transform.GetChild(0);
            BoxCollider box = instance.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null);
            Assert.That(box.isTrigger, Is.True);
            Assert.That(box.size, Is.EqualTo(new Vector3(4, 6, 8)));
            Assert.That(instance.GetComponent<SphereCollider>().excludeLayers.value, Is.EqualTo(-1));
            Assert.That(instance.GetComponent<Rigidbody>() == null, Is.EqualTo(entry.Prefab.GetComponent<Rigidbody>() == null));
            Quaternion sourceRotation = XRayLevelAnomalyPlacement.SourceRotation(anomaly.Angles);
            Vector3 position = new Vector3(11, -3, 9);
            for (int i = 0; i < entry.Prefab.transform.childCount; i++)
            {
                Transform sourceChild = entry.Prefab.transform.GetChild(i);
                Transform child = instance.GetChild(i);
                Assert.That(Vector3.Distance(child.position, position + sourceRotation * sourceChild.localPosition), Is.LessThan(0.0001f), child.name);
                Assert.That(Quaternion.Angle(child.rotation, sourceRotation * sourceChild.localRotation), Is.LessThan(0.01f), child.name);
                Assert.That(child.localScale, Is.EqualTo(sourceChild.localScale), child.name);
            }

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 sourceCorner = new Vector3(1 + z * 4, 2 + y * 3, 3 - x * 2);
                        Vector3 colliderCorner = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, new Vector3(x, y, z)));
                        Assert.That(Vector3.Distance(colliderCorner, position + sourceRotation * sourceCorner), Is.LessThan(0.0001f));
                    }
                }
            }

            Assert.That(entry.Prefab.GetComponent<BoxCollider>(), Is.Null);
        }

        [Test]
        public void ReimportReplacesOnlyOwnedGroupAndPreservesUnrelatedObjects()
        {
            var manual = new GameObject(XRayLevelAnomalyPlacement.GROUP_NAME);
            manual.transform.SetParent(_root.transform, false);
            var geometry = new GameObject("Geometry");
            geometry.transform.SetParent(_root.transform, false);
            XRayLevelSpawnData data = Data();
            XRaySpawnAnomaly anomaly = Anomaly("zone_mine_electric_average", 8);
            anomaly.Shapes.Add(Sphere(XVector3.Zero, 3));
            data.Anomalies.Add(anomaly);
            XRayLevelAnomalyPlacementResult first = Apply(data);
            XRayLevelAnomalyPlacementResult second = Apply(data);
            Assert.That(first.Group == null, Is.True);
            Assert.That(second.Group.transform.childCount, Is.EqualTo(1));
            Assert.That(_root.GetComponentsInChildren<XRayLevelAnomalyGroup>(true).Length, Is.EqualTo(1));
            Assert.That(manual != null && geometry != null, Is.True);
            Assert.That(_root.transform.childCount, Is.EqualTo(3));
        }

        [Test]
        public void MissingUnknownAndUnsupportedSectionsAreReportedWithCounts()
        {
            XRayLevelSpawnData data = Data();
            data.Anomalies.Add(Anomaly("zone_burning_fuzz1", 1));
            data.Anomalies.Add(Anomaly("zone_burning_fuzz1", 2));
            data.Anomalies.Add(Anomaly("zone_new_from_another_pack", 3));
            XRaySpawnAnomaly composite = Anomaly("zone_mine_electric_average", 4);
            composite.Shapes.Add(Sphere(XVector3.Zero, 1));
            composite.Shapes.Add(Sphere(XVector3.Zero, 2));
            data.Anomalies.Add(composite);
            XRayLevelAnomalyPlacementResult result = Apply(data);
            Assert.That(result.SourceCount, Is.EqualTo(4));
            Assert.That(result.ImportedCount, Is.Zero);
            Assert.That(result.Missing.Count, Is.EqualTo(2));
            Assert.That(string.Join(" ", result.Missing), Does.Contain("Жгучий пух"));
            Assert.That(string.Join(" ", result.Missing), Does.Contain(": 2."));
            Assert.That(string.Join(" ", result.Missing), Does.Contain("zone_new_from_another_pack"));
            Assert.That(result.Unsupported.Count, Is.EqualTo(1));
            Assert.That(result.Unsupported[0], Does.Contain("составной объём"));
            Assert.That(result.Group.Missing.Count, Is.EqualTo(2));
        }

        [Test]
        public void InvalidCatalogAndMissingLevelCannotReplacePreviousImport()
        {
            XRayLevelSpawnData data = Data();
            XRaySpawnAnomaly anomaly = Anomaly("zone_mine_electric_average", 1);
            anomaly.Shapes.Add(Sphere(XVector3.Zero, 2));
            data.Anomalies.Add(anomaly);
            XRayLevelAnomalyPlacementResult first = Apply(data);
            Assert.Throws<InvalidOperationException>(() => XRayLevelAnomalyPlacement.Apply(_root, "Level", "source", "origin", "hash", data, null));
            Assert.That(first.Group != null, Is.True);
            Assert.Throws<InvalidOperationException>(() => XRayLevelAnomalyPlacement.Apply(_root, "missing", "source", "origin", "hash", data, _catalog));
            Assert.That(first.Group != null, Is.True);
            Assert.That(_root.transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void InvalidPrefabCannotDestroyPreviousImportedGroup()
        {
            XRayLevelSpawnData data = Data();
            XRaySpawnAnomaly anomaly = Anomaly("zone_mine_electric_average", 1);
            anomaly.Shapes.Add(Sphere(XVector3.Zero, 2));
            data.Anomalies.Add(anomaly);
            XRayLevelAnomalyPlacementResult first = Apply(data);
            XRayLevelAnomalyCatalog catalog = UnityEngine.Object.Instantiate(_catalog);
            try
            {
                var serialized = new SerializedObject(catalog);
                SerializedProperty entries = serialized.FindProperty("_entries");
                for (int i = 0; i < entries.arraySize; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    if (entry.FindPropertyRelative("_section").stringValue == anomaly.Section)
                    {
                        entry.FindPropertyRelative("_prefab").objectReferenceValue = _root;
                    }
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<InvalidOperationException>(() => XRayLevelAnomalyPlacement.Apply(_root, "Level", "source", "origin", "hash", data, catalog));
                Assert.That(first.Group != null, Is.True);
                Assert.That(_root.transform.childCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void SavedPrefabRetainsSourceBoundsNestedLinksAndGeometryAfterRepeatedAnomalyOnlyUpdates()
        {
            _fixtureFolder = "Assets/_Source/Tests/AnomalyPlacementFixture_" + Guid.NewGuid().ToString("N");
            string folderGuid = AssetDatabase.CreateFolder("Assets/_Source/Tests", System.IO.Path.GetFileName(_fixtureFolder));
            Assert.That(folderGuid, Is.Not.Empty);
            string path = _fixtureFolder + "/Level.prefab";
            var geometry = new GameObject("ManualGeometry");
            geometry.transform.SetParent(_root.transform, false);
            geometry.transform.localPosition = new Vector3(3, 2, 1);
            geometry.transform.localRotation = Quaternion.Euler(13, 29, 47);
            geometry.transform.localScale = new Vector3(2, 3, 4);
            XRayLevelSpawnData data = Data();
            XRaySpawnAnomaly sphere = Anomaly("zone_mine_acidic_average", 1);
            sphere.Shapes.Add(Sphere(new XVector3(1, 2, 3), 2));
            data.Anomalies.Add(sphere);
            XRaySpawnAnomaly box = Anomaly("zone_mine_electric_average", 2);
            Set(box, "Position", new XVector3(17, -2, 11));
            var boxShape = new XRaySpawnShape();
            Set(boxShape, "Type", XRaySpawnShapeType.Box);
            Set(boxShape, "Center", new XVector3(1, 2, 3));
            Set(boxShape, "Box", XMatrix4.FromRows(0, 0, 8, 1, 0, 6, 0, 2, -4, 0, 0, 3, 0, 0, 0, 1));
            box.Shapes.Add(boxShape);
            data.Anomalies.Add(box);
            Apply(data);
            Assert.That(PrefabUtility.SaveAsPrefabAsset(_root, path), Is.Not.Null);
            Set(sphere.Shapes[0], "Radius", 5.5);
            Set(sphere.Shapes[0], "Center", new XVector3(-1.25, 4.5, 2.75));
            var plan = new XRayLevelAnomalyImportPlan();
            Set(plan, "LevelName", "Level");
            Set(plan, "SpawnAssetPath", "Assets/shared_all.spawn");
            Set(plan, "Origin", "MO2 winner");
            Set(plan, "Sha256", "updated-source-hash");
            Set(plan, "Data", data);
            Set(plan, "Catalog", _catalog);
            Assert.That(XRayLevelAnomalyImporter.UpdatePrefab(path, plan).ImportedCount, Is.EqualTo(2));
            Assert.That(XRayLevelAnomalyImporter.UpdatePrefab(path, plan).ImportedCount, Is.EqualTo(2));
            GameObject loaded = PrefabUtility.LoadPrefabContents(path);
            try
            {
                XRayLevelAnomalyGroup[] groups = loaded.GetComponentsInChildren<XRayLevelAnomalyGroup>(true);
                Assert.That(groups.Length, Is.EqualTo(1));
                Assert.That(groups[0].ImportedCount, Is.EqualTo(2));
                Assert.That(groups[0].SourceSha256, Is.EqualTo("updated-source-hash"));
                Transform savedGeometry = loaded.transform.Find("ManualGeometry");
                Assert.That(savedGeometry, Is.Not.Null);
                Assert.That(savedGeometry.localPosition, Is.EqualTo(geometry.transform.localPosition));
                Assert.That(savedGeometry.localScale, Is.EqualTo(geometry.transform.localScale));
                Assert.That(Quaternion.Angle(savedGeometry.localRotation, geometry.transform.localRotation), Is.LessThan(0.01f));
                Assert.That(groups[0].transform.childCount, Is.EqualTo(2));
                GameObject savedSphere = groups[0].transform.GetChild(0).gameObject;
                Assert.That(_catalog.TryGet(sphere.Section, out XRayLevelAnomalyEntry sphereEntry), Is.True);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(savedSphere), Is.SameAs(sphereEntry.Prefab));
                SphereCollider sourceSphere = savedSphere.GetComponent<SphereCollider>();
                Assert.That(sourceSphere.radius, Is.EqualTo(5.5f));
                Assert.That(sourceSphere.center, Is.EqualTo(new Vector3(-1.25f, 4.5f, 2.75f)));
                foreach (MonoBehaviour component in savedSphere.GetComponents<MonoBehaviour>())
                {
                    SerializedProperty randomize = new SerializedObject(component).FindProperty("_randomizeRadius");
                    if (randomize != null)
                    {
                        Assert.That(randomize.boolValue, Is.False);
                    }
                }

                GameObject savedBox = groups[0].transform.GetChild(1).gameObject;
                Assert.That(_catalog.TryGet(box.Section, out XRayLevelAnomalyEntry boxEntry), Is.True);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(savedBox), Is.SameAs(boxEntry.Prefab));
                Assert.That(savedBox.transform.localPosition, Is.EqualTo(new Vector3(17, -2, 11)));
                Assert.That(savedBox.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(4, 6, 8)));
                Assert.That(savedBox.GetComponent<BoxCollider>().isTrigger, Is.True);
                Assert.That(savedBox.GetComponent<SphereCollider>().excludeLayers.value, Is.EqualTo(-1));
                Assert.That(Vector3.Distance(savedBox.transform.TransformPoint(savedBox.GetComponent<BoxCollider>().center), new Vector3(18, 0, 14)), Is.LessThan(0.0001f));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(loaded);
            }
        }

        private XRayLevelAnomalyPlacementResult Apply(XRayLevelSpawnData data)
        {
            return XRayLevelAnomalyPlacement.Apply(_root, "Level", "Assets/shared_all.spawn", "MO2 winner", "source-hash", data, _catalog);
        }

        private static XRayLevelSpawnData Data()
        {
            var data = new XRayLevelSpawnData();
            var level = new XRaySpawnLevel();
            Set(level, "Name", "Level");
            Set(level, "Offset", new XVector3(10000, 20000, 30000));
            data.Levels.Add(level);
            return data;
        }

        private static XRaySpawnAnomaly Anomaly(string section, ushort id, string level = "Level")
        {
            var anomaly = new XRaySpawnAnomaly();
            Set(anomaly, "Section", section);
            Set(anomaly, "Name", "source_" + id);
            Set(anomaly, "SpawnId", id);
            Set(anomaly, "LevelName", level);
            return anomaly;
        }

        private static XRaySpawnShape Sphere(XVector3 center, double radius)
        {
            var shape = new XRaySpawnShape();
            Set(shape, "Type", XRaySpawnShapeType.Sphere);
            Set(shape, "Center", center);
            Set(shape, "Radius", radius);
            return shape;
        }

        private static void Set(object value, string property, object data)
        {
            value.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance).SetValue(value, data);
        }
    }
}
