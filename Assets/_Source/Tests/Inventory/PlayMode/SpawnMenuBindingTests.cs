using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.Infrastructure;
using RaidIntoDarkness.Inventory.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class SpawnMenuBindingTests : OutdoorsSceneInputTestFixture
    {
        private const BindingFlags FIELDS = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string OLD_ITEM = "helm_hardhat";
        private const string NEW_ITEM = "helm_resp";

        [UnityTest]
        public IEnumerator OpenMenuClearsOldCatalogOnControllerUnbindAndUsesNewCatalogAfterRebind()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindAnyObjectByType<InventoryWindowController>();
            PlayerInventoryRuntime oldRuntime = controller.Runtime();
            InventoryWindow window = controller.Window;
            SpawnMenuView menu = ReadField<SpawnMenuView>(controller, "_spawnMenu");
            TMP_InputField search = ReadField<TMP_InputField>(menu, "_search");
            TMP_Text catalogMessage = ReadField<TMP_Text>(menu, "_catalogMessage");
            GameObject details = ReadField<GameObject>(menu, "_details");
            TMP_Text itemName = ReadField<TMP_Text>(menu, "_itemName");
            TMP_Text status = ReadField<TMP_Text>(menu, "_status");
            Button grant = ReadField<Button>(menu, "_grant");
            InventoryTooltip tooltip = ReadField<InventoryTooltip>(menu, "_tooltip");
            ItemDefinition oldDefinition = FindDefinition(oldRuntime.Database, OLD_ITEM);
            ItemDefinition newDefinition = FindDefinition(oldRuntime.Database, NEW_ITEM);

            yield return Tap(keyboard.escapeKey);
            Assert.That(Time.timeScale, Is.Zero);
            yield return Tap(keyboard.f7Key);
            Assert.That(menu.isActiveAndEnabled, Is.True);
            search.text = oldDefinition.DisplayName;
            yield return WaitForCells(menu);
            SpawnMenuItemCell oldCell = menu.GetComponentsInChildren<SpawnMenuItemCell>().Single();
            var row = menu.GetComponentsInChildren<SpawnMenuCategoryRow>().First();
            Button oldToggle = ReadField<Button>(row, "_toggle");
            Button oldSelect = ReadField<Button>(row, "_select");
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = 1, clickCount = 1 };
            Click(oldCell, pointer);
            Assert.That(details.activeInHierarchy, Is.True);
            Assert.That(itemName.text, Is.EqualTo(oldDefinition.DisplayName));
            oldCell.OnPointerEnter(pointer);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(tooltip.IsVisible, Is.True);
            EventSystem.current.SetSelectedGameObject(oldToggle.gameObject);
            oldCell.OnPointerDown(pointer);

            int oldAmount = Count(oldRuntime, oldDefinition);
            controller.Configure(window, null, controller.Player, null, null, menu);
            Assert.That(menu.isActiveAndEnabled, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            pointer.clickCount = 2;
            oldCell.OnPointerEnter(pointer);
            oldCell.OnPointerClick(pointer);
            Click(oldCell, pointer);
            grant.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.DoesNotThrow(() => oldToggle.onClick.Invoke());
            Assert.DoesNotThrow(() => oldSelect.onClick.Invoke());
            Assert.That(menu.GetComponentsInChildren<SpawnMenuCategoryRow>(), Is.Empty);
            Assert.That(menu.GetComponentsInChildren<SpawnMenuItemCell>(), Is.Empty);
            Assert.That(details.activeInHierarchy, Is.False);
            Assert.That(itemName.text, Is.Empty);
            Assert.That(status.text, Is.Empty);
            Assert.That(tooltip.IsVisible, Is.False);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
            Assert.That(catalogMessage.text, Is.EqualTo("Инвентарь недоступен"));
            Assert.That(Count(oldRuntime, oldDefinition), Is.EqualTo(oldAmount));
            Assert.DoesNotThrow(() => menu.Unbind());

            var database = ScriptableObject.CreateInstance<ItemDatabase>();
            var sourceObject = new GameObject("Spawn menu replacement inventory");
            try
            {
                database.SetDefinitions(new[] { newDefinition });
                var runtime = sourceObject.AddComponent<PlayerInventoryRuntime>();
                typeof(PlayerInventoryRuntime).GetField("_database", FIELDS).SetValue(runtime, database);
                runtime.Configure(ReadField<InventoryGridSettings>(oldRuntime, "_backpack"), ReadField<InventoryGridSettings>(oldRuntime, "_stash"), oldRuntime.EquipmentContainerId, null, ReadField<InventorySlotLayout>(oldRuntime, "_equipmentLayout"), null, null, null, null);

                yield return Tap(keyboard.f7Key);
                Assert.That(menu.gameObject.activeSelf, Is.False);
                Assert.That(Time.timeScale, Is.Zero);
                yield return Tap(keyboard.escapeKey);
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                controller.Configure(window, runtime, controller.Player, null, null, menu);
                controller.Open();
                Assert.That(window.Model, Is.SameAs(runtime.Model));
                controller.Close();
                yield return null;
                yield return Tap(keyboard.escapeKey);
                Assert.That(Time.timeScale, Is.Zero);
                yield return Tap(keyboard.f7Key);
                Assert.That(menu.isActiveAndEnabled, Is.True);

                search.text = oldDefinition.DisplayName;
                yield return WaitForCatalog(catalogMessage);
                Assert.That(menu.GetComponentsInChildren<SpawnMenuItemCell>(), Is.Empty);
                search.text = newDefinition.DisplayName;
                yield return WaitForCells(menu);
                SpawnMenuItemCell newCell = menu.GetComponentsInChildren<SpawnMenuItemCell>().Single();
                Assert.That(newCell, Is.SameAs(oldCell));
                pointer.clickCount = 1;
                Click(newCell, pointer);
                Assert.That(itemName.text, Is.EqualTo(newDefinition.DisplayName));
                Assert.That(Count(runtime, newDefinition), Is.Zero);
                pointer.clickCount = 2;
                Click(newCell, pointer);
                Assert.That(Count(runtime, newDefinition), Is.EqualTo(1));
                grant.onClick.Invoke();
                Assert.That(Count(runtime, newDefinition), Is.EqualTo(2));
                Assert.That(Count(oldRuntime, oldDefinition), Is.EqualTo(oldAmount));

                yield return Tap(keyboard.f7Key);
                Assert.That(menu.gameObject.activeSelf, Is.False);
                yield return Tap(keyboard.f7Key);
                yield return WaitForCells(menu);
                Assert.That(details.activeInHierarchy, Is.True);
                Assert.That(itemName.text, Is.EqualTo(newDefinition.DisplayName));
                yield return Tap(keyboard.f7Key);
                Assert.That(menu.gameObject.activeSelf, Is.False);
                yield return Tap(keyboard.escapeKey);
                Assert.That(Time.timeScale, Is.EqualTo(1f));
            }
            finally
            {
                controller.Configure(window, oldRuntime, controller.Player, null, null, menu);
                Object.Destroy(sourceObject);
                Object.Destroy(database);
            }
        }

        private IEnumerator Tap(KeyControl key)
        {
            Press(key, queueEventOnly: true);
            yield return null;
            Release(key, queueEventOnly: true);
            yield return null;
        }

        private static IEnumerator WaitForCells(SpawnMenuView menu)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 15d;
            while (menu.GetComponentInChildren<SpawnMenuItemCell>() == null && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            Assert.That(menu.GetComponentInChildren<SpawnMenuItemCell>(), Is.Not.Null, "The spawn catalog did not present any cells.");
        }

        private static IEnumerator WaitForCatalog(TMP_Text message)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 15d;
            while (message.text != "Предметы не найдены" && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            Assert.That(message.text, Is.EqualTo("Предметы не найдены"));
        }

        private static void Click(SpawnMenuItemCell cell, PointerEventData pointer)
        {
            cell.OnPointerDown(pointer);
            cell.OnPointerClick(pointer);
        }

        private static int Count(PlayerInventoryRuntime runtime, ItemDefinition definition) => runtime.Model.GetContainer(runtime.PrimaryContainerId).Items.Where(item => item.Definition == definition).Sum(item => item.Amount);

        private static T ReadField<T>(object owner, string name) => (T)owner.GetType().GetField(name, FIELDS).GetValue(owner);
    }
}
