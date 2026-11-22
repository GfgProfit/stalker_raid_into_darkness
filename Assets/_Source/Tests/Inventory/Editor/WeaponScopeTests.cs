using System.Collections.Generic;
using Inventory;
using Inventory.IconGeneration;
using Inventory.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class WeaponScopeTests
    {
        private Scene _preview;
        private GameObject _root;
        private InventoryWindow _window;
        private InventoryModel _model;
        private InventoryWeaponAttachments _attachments;
        private ItemDefinition _weapon;
        private ItemDefinition _first;
        private ItemDefinition _second;

        [SetUp]
        public void SetUp()
        {
            _weapon = Resources.Load<ItemDefinition>("Items/Weapons/wpn_aug_a3/wpn_aug_a3_definition");
            _first = LoadScope("leupold");
            _second = LoadScope("ac10632");
            _preview = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("WeaponScopeTests", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(_root, _preview);
            _window = _root.AddComponent<InventoryWindow>();
            var definitions = new List<ItemDefinition>(_weapon.GetComponent<WeaponStatsComponent>().CompatibleScopes) { _weapon, LoadSilencer() };
            _model = new InventoryModel(new Resolver(definitions));
            _model.AddContainer(new GridContainer("bag", 24, 8));
            _model.AddContainer(new GridContainer("loot", 8, 8));
            _window.ConfigureContainers("bag", "loot", "equipment", "Player", 100f);
            _window.Bind(_model, null);
            _attachments = new InventoryWeaponAttachments(_window);
        }

        [TearDown]
        public void TearDown()
        {
            _attachments?.Dispose();
            Object.DestroyImmediate(_root);
            EditorSceneManager.ClosePreviewScene(_preview);
        }

        [Test]
        public void InstallPreviewReplaceAndDetachPreserveIdentityStateWeightAndSilencer()
        {
            ItemInstance weapon = Add(_weapon, "bag");
            ItemInstance first = Add(_first, "loot");
            ItemInstance second = Add(_second, "loot");
            ItemInstance silencer = Add(LoadSilencer(), "bag");
            first.SetState("condition", "0.63");
            second.SetState("condition", "0.42");
            weapon.SetState(InventoryWeaponAmmoSource.MAGAZINE_STATE, "17");
            Assert.That(_attachments.Install(silencer, weapon).Success, Is.True);
            Vector2Int size = weapon.GetSize(false);
            float total = _model.GetContainer("bag").TotalWeight + _model.GetContainer("loot").TotalWeight;
            Assert.That(_attachments.Preview(first, weapon).Success, Is.True);
            Assert.That(_attachments.GetScope(weapon), Is.Null);
            Assert.That(_model.ContainsItem(first), Is.True);
            Assert.That(_attachments.Apply(first, weapon).Success, Is.True);
            Assert.That(_attachments.GetScope(weapon), Is.SameAs(_first));
            Assert.That(_model.ContainsItem(first), Is.False);
            Assert.That(weapon.GetSize(false), Is.EqualTo(size));
            Assert.That(_attachments.Install(second, weapon).Success, Is.True);
            Assert.That(_attachments.GetScope(weapon), Is.SameAs(_second));
            Assert.That(_model.GetItem(first.InstanceId).GetState("condition"), Is.EqualTo("0.63"));
            Assert.That(weapon.VisualVariant, Does.Not.Contain(_first.DefinitionId));
            Assert.That(weapon.VisualVariant, Does.Contain(_second.DefinitionId));
            Assert.That(weapon.VisualVariant, Does.Contain(silencer.DefinitionId));
            Assert.That(_attachments.DetachScope(weapon, false).Success, Is.True);
            Assert.That(_attachments.GetScope(weapon), Is.SameAs(_second));
            Assert.That(_attachments.DetachScope(weapon).Success, Is.True);
            Assert.That(_model.GetItem(second.InstanceId).GetState("condition"), Is.EqualTo("0.42"));
            Assert.That(_attachments.GetScope(weapon), Is.Null);
            Assert.That(_attachments.GetSilencer(weapon), Is.SameAs(silencer.Definition));
            Assert.That(weapon.GetState(InventoryWeaponAmmoSource.MAGAZINE_STATE), Is.EqualTo("17"));
            Assert.That(_model.GetContainer("bag").TotalWeight + _model.GetContainer("loot").TotalWeight, Is.EqualTo(total).Within(0.0001f));
        }

        [Test]
        public void ReplacementAndDetachFailAtomicallyWhenReturnedScopeHasNoSpace()
        {
            _model.AddContainer(new GridContainer("tight", _weapon.Width, _weapon.Height));
            _model.AddContainer(new GridContainer("small", _second.Width, _second.Height));
            ItemInstance weapon = Add(_weapon, "tight");
            ItemInstance first = Add(_first, "loot");
            ItemInstance second = Add(_second, "small");
            Assert.That(_attachments.Install(first, weapon).Success, Is.True);
            string original = JsonUtility.ToJson(InventorySerializer.CaptureItem(weapon));
            Assert.That(_attachments.Install(second, weapon).Success, Is.False);
            Assert.That(_attachments.DetachScope(weapon).Success, Is.False);
            Assert.That(_model.ContainsItem(second), Is.True);
            Assert.That(_model.GetItem(first.InstanceId), Is.Null);
            Assert.That(JsonUtility.ToJson(InventorySerializer.CaptureItem(weapon)), Is.EqualTo(original));
        }

        [Test]
        public void CompatibilityWhitelistRejectsOtherMountsAndNonScopeItems()
        {
            ItemInstance weapon = Add(_weapon, "bag");
            ItemInstance unsupported = Add(LoadScope("1p29"), "loot");
            ItemInstance ammunition = Add(_weapon, "loot");
            Assert.That(_attachments.Handles(unsupported, weapon), Is.False);
            Assert.That(_attachments.Install(unsupported, weapon).Success, Is.False);
            Assert.That(_attachments.Handles(ammunition, weapon), Is.False);
            Assert.That(_model.ContainsItem(unsupported), Is.True);
        }

        [Test]
        public void ContextActionsInstallReplaceAndReturnScopesFromEquippedWeapon()
        {
            _attachments.Dispose();
            var slot = new SlotDefinition("Primary", "Primary", new RectInt(0, 0, 5, 2), SlotMode.Single, SlotRule.Create(new[] { _weapon.Category }));
            _model.AddContainer(new SlottedContainer("equipment", new[] { slot }, 5, 2, true));
            _attachments = new InventoryWeaponAttachments(_window);
            ItemInstance weapon = Add(_weapon, "bag");
            ItemInstance first = Add(_first, "loot");
            ItemInstance second = Add(_second, "loot");
            Assert.That(_model.TryEquip(weapon.InstanceId, "equipment").Success, Is.True);
            IInventoryItemAction install = _window.Actions.Find(InventoryWeaponAttachments.ATTACH_ACTION + "Primary");
            var context = new InventoryActionContext(_window, first, null);
            Assert.That(install.IsVisible(context), Is.True);
            Assert.That(install.IsEnabled(context), Is.True);
            install.Execute(context);
            Assert.That(_attachments.GetScope(weapon), Is.SameAs(_first));
            context = new InventoryActionContext(_window, second, null);
            Assert.That(install.IsVisible(context), Is.True);
            Assert.That(install.IsEnabled(context), Is.True);
            install.Execute(context);
            Assert.That(_attachments.GetScope(weapon), Is.SameAs(_second));
            IInventoryItemAction detach = _window.Actions.Find(InventoryWeaponAttachments.DETACH_SCOPE_ACTION);
            var weaponContext = new InventoryActionContext(_window, weapon, null);
            Assert.That(detach.GetLabel(weaponContext), Is.EqualTo("Снять прицел"));
            Assert.That(detach.IsEnabled(weaponContext), Is.True);
            detach.Execute(weaponContext);
            Assert.That(_attachments.GetScope(weapon), Is.Null);
            Assert.That(_model.GetItem(second.InstanceId).ContainerId, Is.EqualTo("bag"));
            Assert.That(_model.GetItem(first.InstanceId).ContainerId, Is.EqualTo("loot"));
        }

        [Test]
        public void CmoScopeIconIsUsedForInstallAndDetachWhileSilencerKeepsItsIcon()
        {
            InventoryTheme theme = AssetDatabase.LoadAssetAtPath<InventoryTheme>("Assets/_Source/UI/Inventory/GRA/GraInventoryTheme.asset");
            _window.ConfigurePresentation(theme, null, "{0}", "{0}", Color.white);
            var style = new ContextMenuOverhaulStyle(_window, null);
            ItemInstance scope = Add(_first, "loot");
            ItemInstance weapon = Add(_weapon, "bag");
            ItemInstance silencer = Add(LoadSilencer(), "bag");
            var install = new InventoryDelegateAction(InventoryWeaponAttachments.ATTACH_ACTION + "Primary", 20, null, null);
            Sprite expected = theme.Icon("ui_cmo_scope");
            Assert.That(expected, Is.Not.Null);
            Assert.That(expected.name, Is.EqualTo("ui_cmo_scope"));
            Assert.That(AssetDatabase.GetAssetPath(expected), Is.EqualTo("Assets/_Source/UI/Inventory/GRA/ContextMenu/cmo_icons.png"));
            Assert.That(style.Resolve(new InventoryActionContext(_window, scope, null), install).Icon, Is.SameAs(expected));
            Assert.That(style.Resolve(new InventoryActionContext(_window, weapon, null), _window.Actions.Find(InventoryWeaponAttachments.DETACH_SCOPE_ACTION)).Icon, Is.SameAs(expected));
            Assert.That(style.Resolve(new InventoryActionContext(_window, silencer, null), install).Icon, Is.SameAs(theme.Icon("ui_cmo_silencer")));
        }

        [Test]
        public void ScopeSurvivesSaveRestoreWithOriginalItemState()
        {
            ItemInstance weapon = Add(_weapon, "bag");
            ItemInstance scope = Add(_first, "loot");
            scope.SetState("condition", "0.57");
            Assert.That(_attachments.Install(scope, weapon).Success, Is.True);
            var restored = new InventoryModel(_model.Resolver);
            restored.AddContainer(new GridContainer("bag", 24, 8));
            restored.AddContainer(new GridContainer("loot", 8, 8));
            Assert.That(InventorySerializer.Restore(restored, InventorySerializer.Capture(_model)).IsClean, Is.True);
            ItemInstance loaded = restored.GetItem(weapon.InstanceId);
            Assert.That(_weapon.GetComponent<WeaponStatsComponent>().GetScope(loaded, restored.Resolver), Is.SameAs(_first));
            Assert.That(loaded.GetState(InventoryWeaponAttachments.SCOPE_ITEM_STATE), Does.Contain(scope.InstanceId));
            Assert.That(loaded.GetState(InventoryWeaponAttachments.SCOPE_ITEM_STATE), Does.Contain("0.57"));
            Assert.That(loaded.TotalWeight, Is.EqualTo(weapon.TotalWeight).Within(0.0001f));
        }

        [Test]
        public void AllCompatibleScopesAreExclusiveInHudWorldAndIconVisuals()
        {
            GameObject[] prefabs = { _weapon.VisualPrefab, _weapon.GetComponent<WeaponHudComponent>().HudModel, _weapon.GetComponent<WorldItemComponent>().Prefab.gameObject };
            WeaponStatsComponent stats = _weapon.GetComponent<WeaponStatsComponent>();
            Assert.That(stats.CompatibleScopes.Count, Is.EqualTo(41));

            foreach (GameObject prefab in prefabs)
            {
                GameObject instance = Object.Instantiate(prefab, _root.transform);
                GameObject standard = instance.GetComponentInChildren<WeaponScopeVisuals>(true).DefaultScope;
                Assert.That(standard, Is.Not.Null, prefab.name);
                Assert.That(standard.name, Is.EqualTo("def_sight"));

                foreach (ItemDefinition scope in stats.CompatibleScopes)
                {
                    ItemIconVisualConfigurators.Apply(instance, _weapon, new[] { scope.DefinitionId });
                    int active = 0;

                    foreach (ItemIconVariantPart part in instance.GetComponentsInChildren<ItemIconVariantPart>(true))
                    {
                        if (part.Group != WeaponScopeVisuals.SCOPE_GROUP)
                        {
                            continue;
                        }

                        Assert.That(part.gameObject.activeSelf, Is.EqualTo(part.Part == scope), prefab.name + ": " + scope.name);
                        Assert.That(part.GetComponentsInChildren<WorldItem>(true), Is.Empty);
                        Assert.That(part.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                        Assert.That(part.GetComponentsInChildren<Collider>(true), Is.Empty);
                        active += part.gameObject.activeSelf ? 1 : 0;
                    }

                    Assert.That(active, Is.EqualTo(1));
                    Assert.That(standard.activeSelf, Is.False, prefab.name + ": " + scope.name);
                    Assert.That(scope.GetComponent<WeaponAddonComponent>().OverrideAimOffsetPosition, Is.True);
                    if (scope.name != "march_f_shorty")
                    {
                        Assert.That(scope.GetComponent<WeaponAddonComponent>().AimOffsetPosition, Is.EqualTo(Vector3.zero));
                    }
                }

                ItemIconVisualConfigurators.Apply(instance, _weapon, new string[0]);
                Assert.That(standard.activeSelf, Is.True, prefab.name);

                foreach (ItemIconVariantPart part in instance.GetComponentsInChildren<ItemIconVariantPart>(true))
                {
                    if (part.Group == WeaponScopeVisuals.SCOPE_GROUP)
                    {
                        Assert.That(part.gameObject.activeSelf, Is.False);
                    }
                }

                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void OptionalDefaultScopeHidesRestoresAndDoesNotToggleWhenUnassigned()
        {
            GameObject instance = Object.Instantiate(_weapon.VisualPrefab, _root.transform);
            var standard = new GameObject("DefaultScope");
            standard.transform.SetParent(instance.transform, false);
            WeaponScopeVisuals visuals = instance.GetComponent<WeaponScopeVisuals>();
            using var serialized = new SerializedObject(visuals);
            serialized.FindProperty("_defaultScope").objectReferenceValue = standard;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ItemIconVisualConfigurators.Apply(instance, _weapon, new[] { _first.DefinitionId });
            Assert.That(standard.activeSelf, Is.False);
            ItemIconVisualConfigurators.Apply(instance, _weapon, new string[0]);
            Assert.That(standard.activeSelf, Is.True);
            serialized.FindProperty("_defaultScope").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ItemIconVisualConfigurators.Apply(instance, _weapon, new[] { _first.DefinitionId });
            Assert.That(standard.activeSelf, Is.True);
            standard.SetActive(false);
            ItemIconVisualConfigurators.Apply(instance, _weapon, new string[0]);
            Assert.That(standard.activeSelf, Is.False);
        }

        [Test]
        public void InstallingTheAssignedDefaultScopeKeepsItVisible()
        {
            GameObject instance = Object.Instantiate(_weapon.VisualPrefab, _root.transform);
            ItemIconVariantPart standard = null;

            foreach (ItemIconVariantPart part in instance.GetComponentsInChildren<ItemIconVariantPart>(true))
            {
                if (part.Part == _first)
                {
                    standard = part;
                }
            }

            Assert.That(standard, Is.Not.Null);
            using var serialized = new SerializedObject(instance.GetComponent<WeaponScopeVisuals>());
            serialized.FindProperty("_defaultScope").objectReferenceValue = standard.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ItemIconVisualConfigurators.Apply(instance, _weapon, new[] { _first.DefinitionId });
            Assert.That(standard.gameObject.activeSelf, Is.True);
            ItemIconVisualConfigurators.Apply(instance, _weapon, new[] { _second.DefinitionId });
            Assert.That(standard.gameObject.activeSelf, Is.False);
            ItemIconVisualConfigurators.Apply(instance, _weapon, new string[0]);
            Assert.That(standard.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void IconVariantsContainEveryScopeWithAndWithoutSilencer()
        {
            List<string[]> variants = ItemIconVariantEnumeration.EnumerateVariants(_weapon);
            Assert.That(variants.Count, Is.EqualTo(84));
            string silencer = LoadSilencer().DefinitionId;

            foreach (ItemDefinition scope in _weapon.GetComponent<WeaponStatsComponent>().CompatibleScopes)
            {
                Assert.That(variants.Exists(v => v.Length == 1 && v[0] == scope.DefinitionId), Is.True, scope.name);
                Assert.That(variants.Exists(v => v.Length == 2 && System.Array.IndexOf(v, scope.DefinitionId) >= 0 && System.Array.IndexOf(v, silencer) >= 0), Is.True, scope.name);
            }
        }

        private ItemInstance Add(ItemDefinition definition, string container)
        {
            ItemInstance item = _model.CreateItem(definition);
            Assert.That(_model.TryAddAuto(item, container).Success, Is.True, definition.name);
            return item;
        }

        private static ItemDefinition LoadScope(string name) => Resources.Load<ItemDefinition>("Items/Addons/Scopes/" + name + "/" + name + "_definition");

        private static ItemDefinition LoadSilencer() => Resources.Load<ItemDefinition>("Items/Addons/Silencers/wpn_sil_nato/wpn_sil_nato_definition");

        private sealed class Resolver : IItemDefinitionResolver
        {
            private readonly Dictionary<string, ItemDefinition> _definitions = new();

            public Resolver(IEnumerable<ItemDefinition> definitions)
            {
                foreach (ItemDefinition definition in definitions)
                {
                    _definitions[definition.DefinitionId] = definition;
                }
            }

            public bool TryResolve(string definitionId, out ItemDefinition definition) => _definitions.TryGetValue(definitionId, out definition);
        }
    }
}
