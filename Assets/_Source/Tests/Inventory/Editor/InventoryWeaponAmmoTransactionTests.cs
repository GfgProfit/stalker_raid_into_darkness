using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class InventoryWeaponAmmoTransactionTests
    {
        private const string BACKPACK = "Backpack";
        private const string EQUIPMENT = "Equipment";
        private const string STANDARD_SECTION = "standard";
        private const string ALTERNATE_SECTION = "alternate";
        private const int MAGAZINE_CAPACITY = 8;
        private const int BOX_LIMIT = 4;

        private ItemDatabase _database;
        private ItemDefinition _weaponDefinition;
        private ItemDefinition _standardAmmo;
        private ItemDefinition _alternateAmmo;
        private InventoryModel _model;
        private InventoryWeaponAmmoSource _source;

        [SetUp]
        public void SetUp()
        {
            _weaponDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
            _weaponDefinition.Configure("weapon", "Weapon", Vector2Int.one, 2f, 1, false);
            _weaponDefinition.AddComponent(new WeaponStatsComponent());
            using (var serialized = new SerializedObject(_weaponDefinition))
            {
                SerializedProperty stats = serialized.FindProperty("_components").GetArrayElementAtIndex(0);
                stats.FindPropertyRelative("_magazineSize").intValue = MAGAZINE_CAPACITY;
                SerializedProperty ammoClasses = stats.FindPropertyRelative("_ammoClasses");
                ammoClasses.arraySize = 2;
                ammoClasses.GetArrayElementAtIndex(0).stringValue = STANDARD_SECTION;
                ammoClasses.GetArrayElementAtIndex(1).stringValue = ALTERNATE_SECTION;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            _standardAmmo = CreateAmmo(STANDARD_SECTION);
            _alternateAmmo = CreateAmmo(ALTERNATE_SECTION);
            _database = ScriptableObject.CreateInstance<ItemDatabase>();
            _database.SetDefinitions(new[] { _weaponDefinition, _standardAmmo, _alternateAmmo });
        }

        [TearDown]
        public void TearDown()
        {
            _source?.Dispose();
            Object.DestroyImmediate(_database);
            Object.DestroyImmediate(_weaponDefinition);
            Object.DestroyImmediate(_standardAmmo);
            Object.DestroyImmediate(_alternateAmmo);
        }

        [Test]
        public void ReloadConsumesMultipleStacksBeforePublishingAnyChange()
        {
            CreateInventory(5);
            ItemInstance weapon = AddWeapon(EQUIPMENT, 0);
            ItemInstance first = AddAmmo(_standardAmmo, 2, 0);
            ItemInstance second = AddAmmo(_standardAmmo, 4, 1);
            ItemInstance remainder = AddAmmo(_standardAmmo, 4, 2);
            int modelChanges = 0;
            int ammoChanges = 0;
            _model.Changed += _ =>
            {
                modelChanges++;
                Assert.That(_source.GetMagazine(weapon.InstanceId), Is.EqualTo(7));
                Assert.That(_source.GetReserve(weapon.InstanceId), Is.EqualTo(3));
                Assert.That(_model.ContainsItem(first), Is.False);
                Assert.That(_model.ContainsItem(second), Is.False);
            };
            _source.Changed += () =>
            {
                ammoChanges++;
                Assert.That(_source.GetMagazine(weapon.InstanceId), Is.EqualTo(7));
                Assert.That(_source.GetReserve(weapon.InstanceId), Is.EqualTo(3));
            };

            _source.Reload(weapon.InstanceId, 7);

            Assert.That(modelChanges, Is.EqualTo(4));
            Assert.That(ammoChanges, Is.GreaterThan(0));
            Assert.That(remainder.Amount, Is.EqualTo(3));
            Assert.That(_source.GetMagazine(weapon.InstanceId), Is.EqualTo(7));
        }

        [Test]
        public void SwitchCommitsAllReturnsAndTheNewSectionBeforePublishing()
        {
            CreateInventory(5);
            ItemInstance weapon = AddWeapon(EQUIPMENT, MAGAZINE_CAPACITY);
            ItemInstance topUp = AddAmmo(_standardAmmo, 2, 0);
            AddAmmo(_alternateAmmo, 1, 1);
            int modelChanges = 0;
            int ammoChanges = 0;
            _model.Changed += _ =>
            {
                modelChanges++;
                Assert.That(_source.GetAmmoSection(weapon.InstanceId), Is.EqualTo(ALTERNATE_SECTION));
                Assert.That(_source.GetMagazine(weapon.InstanceId), Is.Zero);
                Assert.That(CountAmmo(_standardAmmo), Is.EqualTo(10));
            };
            _source.Changed += () =>
            {
                ammoChanges++;
                Assert.That(_source.GetAmmoSection(weapon.InstanceId), Is.EqualTo(ALTERNATE_SECTION));
                Assert.That(CountAmmo(_standardAmmo), Is.EqualTo(10));
            };

            Assert.That(_source.TrySwitchAmmo(weapon.InstanceId), Is.True);

            Assert.That(modelChanges, Is.EqualTo(4));
            Assert.That(ammoChanges, Is.GreaterThan(0));
            Assert.That(topUp.Amount, Is.EqualTo(BOX_LIMIT));
            InventoryContainer backpack = _model.GetContainer(BACKPACK);
            Assert.That(backpack.GetItemAt(new Vector2Int(2, 0)).Amount, Is.EqualTo(BOX_LIMIT));
            Assert.That(backpack.GetItemAt(new Vector2Int(3, 0)).Amount, Is.EqualTo(2));
        }

        [Test]
        public void SwitchWithNoSpaceForFinalBoxKeepsMagazineAndGridUntouched()
        {
            CreateInventory(3);
            ItemInstance weapon = AddWeapon(EQUIPMENT, MAGAZINE_CAPACITY);
            ItemInstance topUp = AddAmmo(_standardAmmo, 2, 0);
            AddAmmo(_alternateAmmo, 1, 1);
            int notifications = 0;
            _model.Changed += _ => notifications++;
            _source.Changed += () => notifications++;

            Assert.That(_source.TrySwitchAmmo(weapon.InstanceId), Is.False);

            Assert.That(_source.GetAmmoSection(weapon.InstanceId), Is.EqualTo(STANDARD_SECTION));
            Assert.That(_source.GetMagazine(weapon.InstanceId), Is.EqualTo(MAGAZINE_CAPACITY));
            Assert.That(topUp.Amount, Is.EqualTo(2));
            Assert.That(CountAmmo(_standardAmmo), Is.EqualTo(2));
            Assert.That(_model.GetContainer(BACKPACK).GetItemAt(new Vector2Int(2, 0)), Is.Null);
            Assert.That(notifications, Is.Zero);
        }

        [Test]
        public void ReloadObserverCanSwitchAmmoUsingTheCommittedMagazine()
        {
            CreateInventory(5);
            ItemInstance weapon = AddWeapon(EQUIPMENT, 0);
            AddAmmo(_standardAmmo, 2, 0);
            AddAmmo(_standardAmmo, 4, 1);
            AddAmmo(_alternateAmmo, 1, 2);
            bool attempted = false;
            _source.Changed += () =>
            {
                if (attempted)
                {
                    return;
                }

                attempted = true;
                Assert.That(_source.GetMagazine(weapon.InstanceId), Is.EqualTo(6));
                Assert.That(_source.GetReserve(weapon.InstanceId), Is.Zero);
                Assert.That(_source.TrySwitchAmmo(weapon.InstanceId), Is.True);
            };

            _source.Reload(weapon.InstanceId, MAGAZINE_CAPACITY);

            Assert.That(attempted, Is.True);
            Assert.That(_source.GetAmmoSection(weapon.InstanceId), Is.EqualTo(ALTERNATE_SECTION));
            Assert.That(_source.GetMagazine(weapon.InstanceId), Is.Zero);
            Assert.That(CountAmmo(_standardAmmo), Is.EqualTo(6));
            Assert.That(CountAmmo(_alternateAmmo), Is.EqualTo(1));
        }

        private static ItemDefinition CreateAmmo(string section)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            definition.Configure(section, "Ammo", Vector2Int.one, 0.1f, BOX_LIMIT, false);
            definition.AddComponent(new AmmoStatsComponent());
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("_components").GetArrayElementAtIndex(0).FindPropertyRelative("_section").stringValue = section;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private void CreateInventory(int columns)
        {
            _model = new InventoryModel(_database);
            _model.AddContainer(new GridContainer(BACKPACK, columns, 1));
            var slot = new SlotDefinition(SlotId.Primary, "Primary", new RectInt(0, 0, 1, 1), SlotMode.Single, SlotRule.Create());
            _model.AddContainer(new SlottedContainer(EQUIPMENT, new[] { slot }, 1, 1, true));
            _source = new InventoryWeaponAmmoSource(_model, BACKPACK);
        }

        private ItemInstance AddWeapon(string containerId, int magazine)
        {
            ItemInstance weapon = _model.CreateItem(_weaponDefinition);
            weapon.SetState(InventoryWeaponAmmoSource.AMMO_SECTION_STATE, STANDARD_SECTION);
            weapon.SetState(InventoryWeaponAmmoSource.MAGAZINE_STATE, magazine.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.That(_model.TryAdd(weapon, containerId, Vector2Int.zero, false).Success, Is.True);
            return weapon;
        }

        private ItemInstance AddAmmo(ItemDefinition definition, int amount, int column)
        {
            ItemInstance ammo = _model.CreateItem(definition, amount);
            Assert.That(_model.TryAdd(ammo, BACKPACK, new Vector2Int(column, 0), false).Success, Is.True);
            return ammo;
        }

        private int CountAmmo(ItemDefinition definition)
        {
            int count = 0;
            foreach (ItemInstance item in _model.GetContainer(BACKPACK).Items)
            {
                if (item.Definition == definition)
                {
                    count += item.Amount;
                }
            }

            return count;
        }
    }
}
