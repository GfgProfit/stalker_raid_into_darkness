using System.Collections;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class OutdoorsSceneAmmoTests : OutdoorsSceneInputTestFixture
    {
        private InventoryItemView _hoveredView;

        protected override string SceneName => "TEST_SCENE";

        public override void Setup()
        {
            base.Setup();
            _hoveredView = null;
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator AmmoStacksMergeBeyondBoxSizeAndShowWhiteTypeLabels()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            WorldItemInteractor interactor = Object.FindAnyObjectByType<WorldItemInteractor>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance first = AddItem(controller, "ammo_5.56x45_ss190", window.PrimaryContainerId);
            ItemDefinition definition = first.Definition;
            Assert.That(interactor.TryPickUp(interactor.Spawn(definition, 15)), Is.True);
            Assert.That(interactor.TryPickUp(interactor.Spawn(definition, 1000)), Is.True);
            Assert.That(first.Amount, Is.EqualTo(1016));
            Assert.That(first.TotalWeight, Is.EqualTo(definition.Weight * 1016).Within(0.0001f));
            ItemInstance ep = AddItem(controller, "ammo_5.56x45_ap", window.PrimaryContainerId);
            Assert.That(ep.Amount, Is.EqualTo(1));
            Assert.That(model.TryTransfer(first.InstanceId, window.SecondaryContainerId).Success, Is.True);
            ItemInstance more = model.CreateItem(definition, 500);
            Assert.That(model.TryAddAuto(more, window.SecondaryContainerId).Success, Is.True);
            Assert.That(first.Amount, Is.EqualTo(1516));
            InventoryResult split = model.TrySplitAuto(first.InstanceId, 30, window.PrimaryContainerId);
            Assert.That(split.Success, Is.True);
            Assert.That(split.Item.Amount, Is.EqualTo(30));
            Assert.That(model.TryTransfer(split.Item.InstanceId, window.SecondaryContainerId).Success, Is.True);
            Assert.That(first.Amount, Is.EqualTo(1516));
            Assert.That(model.GetItem(split.Item.InstanceId), Is.Null);
            controller.Open(true);
            yield return null;
            yield return null;
            InventoryItemView view = window.FindGrid(first.ContainerId).GetItemView(first);
            TMP_Text label = view.transform.Find("Overlay/Name").GetComponent<TMP_Text>();
            label.ForceMeshUpdate();
            Assert.That(label.GetParsedText(), Is.EqualTo(definition.ShortName));
            Assert.That(label.alignment, Is.EqualTo(TextAlignmentOptions.BottomRight));
            Assert.That(label.textInfo.characterCount, Is.EqualTo(definition.ShortName.Length));
            Assert.That(label.textInfo.characterInfo[0].pointSize, Is.EqualTo(16f).Within(0.001f));
            Assert.That(label.textInfo.characterInfo[0].color, Is.EqualTo(new Color32(255, 255, 255, 255)));
            AssertNameBackground(view, definition.ShortName);
            InventorySaveData saved = InventorySerializer.Capture(model, new[] { window.PrimaryContainerId, window.SecondaryContainerId });
            var restored = new InventoryModel(model.Resolver);
            InventoryContainer backpack = model.GetContainer(window.PrimaryContainerId);
            InventoryContainer stash = model.GetContainer(window.SecondaryContainerId);
            restored.AddContainer(new GridContainer(backpack.Id, backpack.Width, backpack.Height));
            restored.AddContainer(new GridContainer(stash.Id, stash.Width, stash.Height));
            Assert.That(InventorySerializer.Restore(restored, saved).IsClean, Is.True);
            Assert.That(restored.GetItem(first.InstanceId).Amount, Is.EqualTo(1516));
            controller.Close();
        }

        [UnityTest]
        public IEnumerator InventoryCellsShowStoredShortNamesAndKeepTheKnifeSlotUnlabeled()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            string bag = window.PrimaryContainerId;
            string stash = window.SecondaryContainerId;
            ItemInstance pmm = AddItem(controller, "ammo_9x18_pmm", bag);
            ItemInstance magnum = AddItem(controller, "ammo_44mag", bag);
            ItemInstance addon = AddItem(controller, "sil_pbs1", stash);
            ItemInstance backpack = AddItem(controller, "itm_backpack", bag);
            ItemInstance patch = AddItem(controller, "dolg_patch", bag);
            ItemInstance grenade = AddItem(controller, "grenade_rgd5", bag);
            ItemInstance outfit = AddItem(controller, "dolg_outfit", bag);
            ItemInstance knife = AddItem(controller, "wpn_knife_m9", bag);
            controller.Open(true);
            yield return null;
            yield return null;

            AssertNameBackground(window.FindGrid(bag).GetItemView(pmm), "ПММ");

            foreach (ItemInstance item in new[] { magnum, addon, backpack, patch, grenade, outfit, knife })
            {
                InventoryItemView view = window.FindGrid(item.ContainerId).GetItemView(item);
                TMP_Text label = view.transform.Find("Overlay/Name").GetComponent<TMP_Text>();
                label.ForceMeshUpdate();
                Assert.That(label.isActiveAndEnabled, Is.True, item.Definition.DisplayName);
                Assert.That(label.text, Does.Contain(item.Definition.ShortName), item.Definition.DisplayName);
                Assert.That(label.GetParsedText(), Is.Not.Empty, item.Definition.DisplayName);
            }

            Assert.That(model.TryEquip(knife.InstanceId, window.EquipmentContainerId).Success, Is.True);
            Assert.That(knife.Location.SlotId, Is.EqualTo(FirstPersonWeaponBinder.KNIFE_SLOT));
            InventoryItemView equipped = window.FindGrid(knife.ContainerId).GetItemView(knife);
            Assert.That(equipped.transform.Find("Overlay/Name").GetComponent<TMP_Text>().enabled, Is.False);
            controller.Close();
        }

        [UnityTest]
        public IEnumerator DraggingSplitAmmoStackOntoMatchingAmmoMergesInBackpack()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance destination = AddItem(controller, "ammo_5.56x45_ss190", window.PrimaryContainerId, 30);
            InventoryResult split = model.TrySplitAuto(destination.InstanceId, 12);
            Assert.That(split.Success, Is.True);
            ItemInstance source = split.Item;
            ItemInstance incompatible = AddItem(controller, "ammo_5.56x45_ap", window.PrimaryContainerId);
            controller.Open();
            yield return null;
            yield return null;

            InventoryGridViewBase grid = window.FindGrid(window.PrimaryContainerId);
            InventoryItemView sourceView = grid.GetItemView(source);
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = ScreenPoint(sourceView) };
            ExecuteEvents.Execute(sourceView.gameObject, pointer, ExecuteEvents.beginDragHandler);
            pointer.position = ScreenPoint(grid.GetItemView(incompatible));
            ExecuteEvents.Execute(sourceView.gameObject, pointer, ExecuteEvents.dragHandler);
            Assert.That(window.Drag.Kind, Is.EqualTo(InventoryDropKind.None));
            ExecuteEvents.Execute(sourceView.gameObject, pointer, ExecuteEvents.endDragHandler);
            Assert.That(source.Amount, Is.EqualTo(12));
            Assert.That(incompatible.Amount, Is.EqualTo(1));

            pointer.position = ScreenPoint(sourceView);
            ExecuteEvents.Execute(sourceView.gameObject, pointer, ExecuteEvents.beginDragHandler);
            pointer.position = ScreenPoint(grid.GetItemView(destination));
            ExecuteEvents.Execute(sourceView.gameObject, pointer, ExecuteEvents.dragHandler);
            Assert.That(window.Drag.Kind, Is.EqualTo(InventoryDropKind.Merge));
            Assert.That(window.Drag.TargetItem, Is.SameAs(destination));
            Assert.That(destination.Amount, Is.EqualTo(18));
            ItemLocation destinationLocation = destination.Location;
            ExecuteEvents.Execute(sourceView.gameObject, pointer, ExecuteEvents.endDragHandler);
            Assert.That(model.GetItem(source.InstanceId), Is.Null);
            Assert.That(destination.Amount, Is.EqualTo(30));
            Assert.That(destination.Location, Is.EqualTo(destinationLocation));
            Assert.That(grid.GetItemView(source), Is.Null);
            controller.Close();
        }

        [UnityTest]
        public IEnumerator SplitDialogTracksStackAmountAndClosesWhenUnavailable()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            string backpack = window.PrimaryContainerId;
            ItemInstance source = AddItem(controller, "ammo_5.56x45_ss190", backpack, 12);
            ItemInstance destination = AddItem(controller, "ammo_5.56x45_ss190", backpack, 20);
            ItemInstance other = AddItem(controller, "ammo_5.56x45_ap", backpack, 4);
            ItemInstance refill = AddItem(controller, "ammo_5.56x45_ss190", backpack, 2);
            controller.Open();
            yield return null;

            OpenSplit(source);
            window.SplitDialog.SetAmount(11);
            Assert.That(model.TryRemove(other.InstanceId).Success, Is.True);
            Assert.That(window.SplitDialog.IsVisible, Is.True);
            Assert.That(window.SplitDialog.SelectedAmount, Is.EqualTo(11));
            Assert.That(model.TryRemove(source.InstanceId, 3).Success, Is.True);
            Assert.That(window.SplitDialog.IsVisible, Is.True);
            Assert.That(window.SplitDialog.SelectedAmount, Is.EqualTo(8));
            TMP_Text amountLabel = window.SplitDialog.transform.Find("Panel/Amount").GetComponent<TMP_Text>();
            Assert.That(amountLabel.text, Is.EqualTo("8"));
            Assert.That(model.TryMerge(refill.InstanceId, source.InstanceId).Success, Is.True);
            Assert.That(window.SplitDialog.SelectedAmount, Is.EqualTo(8));
            window.SplitDialog.SetAmount(20);
            Assert.That(window.SplitDialog.SelectedAmount, Is.EqualTo(10));
            Assert.That(model.TryMerge(source.InstanceId, destination.InstanceId).Success, Is.True);
            Assert.That(window.SplitDialog.IsVisible, Is.False);

            OpenSplit(destination);
            Assert.That(model.TryTransfer(destination.InstanceId, window.SecondaryContainerId, mergeStacks: false).Success, Is.True);
            Assert.That(window.SplitDialog.IsVisible, Is.False);
            Assert.That(model.ContainsItem(destination), Is.True);
            Assert.That(model.TryTransfer(destination.InstanceId, backpack, mergeStacks: false).Success, Is.True);
            OpenSplit(destination);
            window.SplitDialog.SetAmount(5);
            window.SplitDialog.Confirm();
            Assert.That(window.SplitDialog.IsVisible, Is.False);
            Assert.That(destination.Amount, Is.EqualTo(26));

            OpenSplit(destination);
            Assert.That(model.TryRemove(destination.InstanceId).Success, Is.True);
            Assert.That(window.SplitDialog.IsVisible, Is.False);
            ItemInstance remaining = AddItem(controller, "ammo_5.56x45_ss190", backpack, 8);
            OpenSplit(remaining);
            Assert.That(model.TryRemove(remaining.InstanceId, 7).Success, Is.True);
            Assert.That(remaining.Amount, Is.EqualTo(1));
            Assert.That(window.SplitDialog.IsVisible, Is.False);
            ItemInstance cleared = AddItem(controller, "ammo_5.56x45_ss190", backpack, 8);
            OpenSplit(cleared);
            model.Clear();
            Assert.That(window.SplitDialog.IsVisible, Is.False);
            controller.Close();

            void OpenSplit(ItemInstance item)
            {
                window.OpenContextMenu(item, window.FindGrid(item.ContainerId), new Vector2(400f, 400f), null);
                Assert.That(window.ContextMenu.Invoke(window.Label(InventoryBuiltInActions.SPLIT, "Split")), Is.True);
                Assert.That(window.SplitDialog.IsVisible, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator WeaponAmmoHoverHighlightsBothInventoriesAndEquipment()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            ItemInstance weapon = AddItem(controller, "wpn_aug_a3", window.PrimaryContainerId);
            ItemInstance leftWeapon = AddItem(controller, "wpn_aug_a3", window.SecondaryContainerId);
            ItemInstance equippedWeapon = AddItem(controller, "wpn_aug_a3", window.PrimaryContainerId);
            Assert.That(model.TryEquip(equippedWeapon.InstanceId, window.EquipmentContainerId).Success, Is.True);
            ItemInstance outfit = AddItem(controller, "dolg_outfit", window.PrimaryContainerId);
            Assert.That(model.TryEquip(outfit.InstanceId, window.EquipmentContainerId).Success, Is.True);
            ItemInstance helmet = AddItem(controller, "helm_battle", window.PrimaryContainerId);
            Assert.That(model.TryEquip(helmet.InstanceId, window.EquipmentContainerId).Success, Is.True);
            ItemInstance fmj = AddItem(controller, "ammo_5.56x45_ss190", window.PrimaryContainerId);
            ItemInstance ep = AddItem(controller, "ammo_5.56x45_ap", window.SecondaryContainerId);
            ItemInstance incompatible = AddItem(controller, "ammo_9x18_fmj", window.SecondaryContainerId);
            controller.Sorting.SetFavorite(ep.Definition, true);
            controller.Open(true);
            yield return null;
            yield return null;
            InventoryItemView weaponView = window.FindGrid(weapon.ContainerId).GetItemView(weapon);
            InventoryItemView leftWeaponView = window.FindGrid(leftWeapon.ContainerId).GetItemView(leftWeapon);
            InventoryItemView equippedView = window.FindGrid(equippedWeapon.ContainerId).GetItemView(equippedWeapon);
            InventoryItemView fmjView = window.FindGrid(fmj.ContainerId).GetItemView(fmj);
            InventoryItemView epView = window.FindGrid(ep.ContainerId).GetItemView(ep);
            InventoryItemView incompatibleView = window.FindGrid(incompatible.ContainerId).GetItemView(incompatible);
            Color weaponColor = weaponView.Background.color;
            Color leftWeaponColor = leftWeaponView.Background.color;
            Color equippedColor = equippedView.Background.color;
            Color fmjColor = fmjView.Background.color;
            Color epColor = epView.Background.color;
            Color incompatibleColor = incompatibleView.Background.color;
            Assert.That(equippedView.Background.enabled, Is.False);
            InventoryEquipmentPanel equipment = window.FindGrid(equippedWeapon.ContainerId) as InventoryEquipmentPanel;
            Assert.That(equipment, Is.Not.Null);
            InventoryEquipmentSlot weaponSlot = equipment.GetSlotView(equippedWeapon.Location.SlotId);
            Image slotHighlight = equippedView.Background;
            AssertNameBackground(weaponView, weapon.Definition.ShortName);
            AssertNameBackground(leftWeaponView, leftWeapon.Definition.ShortName);
            AssertNameBackground(equippedView, equippedWeapon.Definition.ShortName);
            InventoryItemView outfitView = window.FindGrid(outfit.ContainerId).GetItemView(outfit);
            InventoryItemView helmetView = window.FindGrid(helmet.ContainerId).GetItemView(helmet);
            AssertNameBackground(outfitView, outfit.Definition.ShortName, true);
            AssertNameBackground(helmetView, helmet.Definition.ShortName, true);
            TMP_Text equippedName = equippedView.transform.Find("Overlay/Name").GetComponent<TMP_Text>();
            Assert.That(equippedName.alignment, Is.EqualTo(TextAlignmentOptions.Top));
            Assert.That(equippedName.canvasRenderer.absoluteDepth, Is.GreaterThan(weaponSlot.Icon.canvasRenderer.absoluteDepth));

            foreach (InventoryItemView slotView in new[] { equippedView, outfitView, helmetView })
            {
                TMP_Text info = slotView.transform.Find("Overlay/Info").GetComponent<TMP_Text>();
                Assert.That(info.isActiveAndEnabled, Is.True);
                Assert.That(info.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
                Assert.That(info.text, Is.EqualTo(window.Theme.FormatItemWeight(slotView.Item.TotalWeight)));
            }

            Image community = outfitView.transform.Find("Overlay/CommunityIcon").GetComponent<Image>();
            Image classBackground = outfitView.transform.Find("Overlay/NameBackground").GetComponent<Image>();
            Assert.That(community.isActiveAndEnabled, Is.True);
            Assert.That(community.sprite.name, Is.EqualTo("ui_dyn_eq_dolg"));
            Assert.That(community.rectTransform.sizeDelta.x, Is.EqualTo(24f).Within(0.001f));
            Assert.That(community.rectTransform.sizeDelta.y, Is.EqualTo(22.8f).Within(0.001f));
            Assert.That(community.rectTransform.anchoredPosition.y, Is.EqualTo(outfitView.RectTransform.rect.height + classBackground.rectTransform.anchoredPosition.y + 5f).Within(0.01f));
            Assert.That(helmetView.transform.Find("Overlay/CommunityIcon").GetComponent<Image>().enabled, Is.False);
            Assert.That(slotHighlight.transform.parent, Is.SameAs(weaponSlot.Icon.transform.parent));
            Assert.That(slotHighlight.transform.GetSiblingIndex(), Is.LessThan(weaponSlot.Icon.transform.GetSiblingIndex()));

            foreach (InventoryItemView source in new[] { weaponView, leftWeaponView })
            {
                yield return Hover(source);
                Assert.That(window.HoveredItem, Is.SameAs(source.Item));
                Assert.That(fmjView.Background.color, Is.Not.EqualTo(fmjColor));
                Assert.That(epView.Background.color, Is.Not.EqualTo(epColor));
                Assert.That(incompatibleView.Background.color, Is.EqualTo(incompatibleColor));
                Assert.That(window.ItemDecorator(ep).Badge, Is.SameAs(controller.Sorting.Decorate(ep).Badge));
            }

            foreach (InventoryItemView source in new[] { epView, fmjView })
            {
                yield return Hover(source);
                Assert.That(window.HoveredItem, Is.SameAs(source.Item));
                Assert.That(weaponView.Background.color, Is.Not.EqualTo(weaponColor));
                Assert.That(leftWeaponView.Background.color, Is.Not.EqualTo(leftWeaponColor));
                Assert.That(equippedView.Background.color, Is.Not.EqualTo(equippedColor));
                Assert.That(equippedView.Background.isActiveAndEnabled, Is.True);
                Assert.That(equippedView.Background.canvasRenderer.GetInheritedAlpha(), Is.GreaterThan(0f));
                Assert.That(equippedView.Background.rectTransform.rect.size.sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(equippedView.Background.transform.GetSiblingIndex(), Is.LessThan(weaponSlot.Icon.transform.GetSiblingIndex()));
                Assert.That(equippedView.transform.Find("Content").GetComponent<Image>().enabled, Is.False);

                string capture = System.Environment.GetEnvironmentVariable("GRA_AMMO_HIGHLIGHT_CAPTURE_PATH");

                if (source == epView && string.IsNullOrEmpty(capture) == false)
                {
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(capture);
                    yield return null;
                }
            }

            yield return Hover(incompatibleView);
            Assert.That(weaponView.Background.color, Is.EqualTo(weaponColor));
            Assert.That(leftWeaponView.Background.color, Is.EqualTo(leftWeaponColor));
            Assert.That(equippedView.Background.color, Is.EqualTo(equippedColor));
            Assert.That(equippedView.Background.enabled, Is.False);
            yield return Hover(weaponView);
            Assert.That(window.BeginDrag(weapon, weaponView.Grid, 1, Vector2.zero, null), Is.True);
            yield return null;
            yield return null;
            Assert.That(fmjView.Background.color, Is.EqualTo(fmjColor));
            Assert.That(epView.Background.color, Is.EqualTo(epColor));
            window.CancelDrag();
            yield return Hover(epView);
            Assert.That(weaponView.Background.color, Is.Not.EqualTo(weaponColor));
            Assert.That(model.TryRemove(ep.InstanceId).Success, Is.True);
            yield return null;
            yield return null;
            Assert.That(weaponView.Background.color, Is.EqualTo(weaponColor));
            Assert.That(leftWeaponView.Background.color, Is.EqualTo(leftWeaponColor));
            yield return Hover(weaponView);
            controller.Close();
            yield return null;
            yield return null;
            Assert.That(fmjView.Background.color, Is.EqualTo(fmjColor));
            controller.Sorting.SetFavorite(ep.Definition, false);
            Assert.That(model.TryRemove(equippedWeapon.InstanceId).Success, Is.True);
            yield return null;
            yield return null;
            Assert.That(slotHighlight.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator LauncherAmmoHoverUpdatesWhenGp25IsInstalledOrDetached()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            InventoryWindow window = controller.Window;
            InventoryModel model = window.Model;
            ItemInstance weapon = AddItem(controller, "wpn_ak74", window.PrimaryContainerId);
            ItemInstance bareWeapon = AddItem(controller, "wpn_ak74", window.PrimaryContainerId);
            ItemInstance launcher = AddItem(controller, "wpn_addon_grenade_launcher_definition", window.PrimaryContainerId);
            ItemInstance grenade = AddItem(controller, "ammo_vog-25", window.SecondaryContainerId);
            ItemInstance incompatibleGrenade = AddItem(controller, "ammo_vog-25p", window.SecondaryContainerId);
            ItemInstance rifleAmmo = AddItem(controller, "ammo_5.45x39_fmj", window.SecondaryContainerId);
            Assert.That(model.TryEquip(weapon.InstanceId, window.EquipmentContainerId).Success, Is.True);

            controller.Open(true);
            yield return null;
            yield return null;
            InventoryItemView weaponView = window.FindGrid(weapon.ContainerId).GetItemView(weapon);
            InventoryItemView bareWeaponView = window.FindGrid(bareWeapon.ContainerId).GetItemView(bareWeapon);
            InventoryItemView grenadeView = window.FindGrid(grenade.ContainerId).GetItemView(grenade);
            InventoryItemView incompatibleView = window.FindGrid(incompatibleGrenade.ContainerId).GetItemView(incompatibleGrenade);
            InventoryItemView rifleAmmoView = window.FindGrid(rifleAmmo.ContainerId).GetItemView(rifleAmmo);
            Color grenadeColor = grenadeView.Background.color;
            Color incompatibleColor = incompatibleView.Background.color;
            Color rifleAmmoColor = rifleAmmoView.Background.color;
            Color bareWeaponColor = bareWeaponView.Background.color;

            yield return Hover(weaponView);
            Assert.That(grenadeView.Background.color, Is.EqualTo(grenadeColor));
            Assert.That(rifleAmmoView.Background.color, Is.Not.EqualTo(rifleAmmoColor));
            Assert.That(controller.WeaponAttachments.Install(launcher, weapon).Success, Is.True);
            yield return null;
            yield return null;
            Assert.That(window.HoveredItem, Is.SameAs(weapon));
            Assert.That(grenadeView.Background.color, Is.Not.EqualTo(grenadeColor));
            Assert.That(incompatibleView.Background.color, Is.EqualTo(incompatibleColor));

            yield return Hover(grenadeView);
            Assert.That(weaponView.Background.enabled, Is.True);
            Assert.That(bareWeaponView.Background.color, Is.EqualTo(bareWeaponColor));
            yield return Hover(incompatibleView);
            Assert.That(weaponView.Background.enabled, Is.False);

            yield return Hover(weaponView);
            Assert.That(controller.WeaponAttachments.DetachGrenadeLauncher(weapon).Success, Is.True);
            yield return null;
            yield return null;
            Assert.That(window.HoveredItem, Is.SameAs(weapon));
            Assert.That(grenadeView.Background.color, Is.EqualTo(grenadeColor));
            Assert.That(rifleAmmoView.Background.color, Is.Not.EqualTo(rifleAmmoColor));
            controller.Close();
        }

        [UnityTest]
        public IEnumerator ImportedIwpAmmoHasModelsAndConfigs()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            GameObject group = GameObject.Find("WorldItems/Ammo");
            Assert.That(group, Is.Not.Null);
            WorldItem[] items = group.GetComponentsInChildren<WorldItem>();
            var sections = new HashSet<string>();

            foreach (WorldItem item in items)
            {
                ItemDefinition definition = item.Definition;
                AmmoStatsComponent stats = definition.GetComponent<AmmoStatsComponent>();
                Assert.That(stats, Is.Not.Null, definition.name);
                sections.Add(stats.Section);
                Assert.That(stats.Section, Does.Not.EndWith("_bad").And.Not.EndWith("_verybad"));
                Assert.That(item.Amount, Is.EqualTo(stats.BoxSize), stats.Section);
                Assert.That(definition.Weight * stats.BoxSize, Is.EqualTo(stats.BoxWeight).Within(0.00001f), stats.Section);
                Assert.That(definition.HasStackLimit, Is.False, stats.Section);
                Assert.That(definition.MaxStack, Is.EqualTo(int.MaxValue), stats.Section);
                Assert.That(definition.IconOverride, Is.Not.Null, stats.Section);
                Rect iconRect = definition.IconOverride.rect;
                Assert.That(iconRect.width, Is.EqualTo(definition.Width * 100), stats.Section);
                Assert.That(iconRect.height, Is.EqualTo(definition.Height * 100), stats.Section);
                Assert.That(definition.Description, Is.Not.Empty, stats.Section);
                Assert.That(definition.GetComponent<AmmoBallisticsComponent>()?.Data.IsConfigured, Is.True, stats.Section);
                Assert.That(definition.GetComponent<WorldItemComponent>()?.Prefab, Is.Not.Null, stats.Section);
                Assert.That(item.GetComponent<Collider>(), Is.Not.Null, stats.Section);
                Assert.That(item.GetComponent<WorldItemImpactSound>(), Is.Not.Null, stats.Section);
                Assert.That(item.GetComponent<AudioSource>().spatialBlend, Is.EqualTo(1f), stats.Section);
                ItemTakeSoundComponent sounds = definition.GetComponent<ItemTakeSoundComponent>();
                Assert.That(sounds, Is.Not.Null, stats.Section);
                Assert.That(sounds.Clips, Is.Not.Empty.And.All.Not.Null, stats.Section);
                Renderer[] renderers = definition.VisualPrefab.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty, stats.Section);

                foreach (Renderer renderer in renderers)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, stats.Section);
                        Assert.That(material.shader.name, Is.EqualTo("HDRP/MonolithLit"), stats.Section);
                        Assert.That(material.GetTexture("_BaseColorMap"), Is.Not.Null, stats.Section);
                    }
                }
            }

            Assert.That(sections.Count, Is.EqualTo(37));
        }

        [UnityTest]
        public IEnumerator AmmoPickupTransferSplitTooltipDropAndSurfaceSoundsWork()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            InventoryWindowController controller = Object.FindAnyObjectByType<InventoryWindowController>();
            WorldItemInteractor interactor = Object.FindAnyObjectByType<WorldItemInteractor>();
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Inventory.Model;
            WorldItem box = GameObject.Find("WorldItems/Ammo/ammo_9x18_fmj").GetComponent<WorldItem>();
            ItemDefinition definition = box.Definition;
            box.Configure(definition, 7);
            int taken = controller.ItemSounds.PlayedCount;
            Assert.That(interactor.TryPickUp(box), Is.True);
            Assert.That(controller.ItemSounds.PlayedCount, Is.EqualTo(taken + 1));
            Assert.That(controller.ItemSounds.LastVolume, Is.EqualTo(InventoryItemSounds.TAKE_VOLUME));
            ItemInstance item = null;

            foreach (ItemInstance candidate in model.GetContainer(window.PrimaryContainerId).Items)
            {
                if (candidate.Definition == definition)
                {
                    item = candidate;
                }
            }

            Assert.That(item, Is.Not.Null);
            Assert.That(item.Amount, Is.EqualTo(7));
            Assert.That(item.TotalWeight, Is.EqualTo(definition.Weight * 7f).Within(0.00001f));
            controller.Open(true);
            yield return new WaitForSeconds(0.05f);
            Assert.That(model.TryTransfer(item.InstanceId, window.SecondaryContainerId).Success, Is.True);
            Assert.That(controller.ItemSounds.LastVolume, Is.EqualTo(InventoryItemSounds.PUT_IN_BOX_VOLUME));
            yield return new WaitForSeconds(0.05f);
            Assert.That(model.TryTransfer(item.InstanceId, window.PrimaryContainerId).Success, Is.True);
            Assert.That(controller.ItemSounds.LastVolume, Is.EqualTo(InventoryItemSounds.TAKE_VOLUME));
            InventoryResult split = model.TrySplitAuto(item.InstanceId, 3, window.SecondaryContainerId);
            Assert.That(split.Success, Is.True);
            Assert.That(item.Amount, Is.EqualTo(4));
            Assert.That(split.Item.Amount, Is.EqualTo(3));
            Assert.That(split.Item.TotalWeight, Is.EqualTo(definition.Weight * 3f).Within(0.00001f));
            window.Tooltip.Show(split.Item, new Vector2(500f, 500f), null);
            Assert.That(window.Tooltip.Title, Is.EqualTo(definition.DisplayName));
            Assert.That(window.Tooltip.Description, Is.EqualTo(definition.Description));
            string capture = System.Environment.GetEnvironmentVariable("GRA_AMMO_CAPTURE_PATH");

            if (string.IsNullOrEmpty(capture) == false)
            {
                ScreenCapture.CaptureScreenshot(capture);
            }

            yield return new WaitForSeconds(0.6f);
            var uiSounds = new List<InventorySound>();
            window.SoundPlayed += uiSounds.Add;
            window.DropItemNow(split.Item);
            window.SoundPlayed -= uiSounds.Add;
            Assert.That(uiSounds, Does.Contain(InventorySound.Drop));
            Assert.That(window.Theme.Sounds.Get(InventorySound.Drop), Is.Not.Null);
            yield return null;
            WorldItem dropped = null;

            foreach (WorldItem candidate in Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            {
                if (candidate.Definition == definition && candidate.Amount == 3)
                {
                    dropped = candidate;
                }
            }

            Assert.That(dropped, Is.Not.Null);
            WorldItemImpactSound impact = dropped.GetComponent<WorldItemImpactSound>();
            GameObject surfaceObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surfaceObject.transform.position = dropped.transform.position + Vector3.down * 10f;
            StalkerSurface surface = surfaceObject.AddComponent<StalkerSurface>();

            foreach (StalkerSurfaceType type in new[] { StalkerSurfaceType.Earth, StalkerSurfaceType.Concrete, StalkerSurfaceType.Metal, StalkerSurfaceType.Wood, StalkerSurfaceType.Water })
            {
                surface.Type = type;
                Assert.That(impact.TryPlay(surfaceObject.GetComponent<Collider>(), dropped.transform.position, Vector3.up, Vector3.down * 100f), Is.True, type.ToString());
                Assert.That(impact.LastSurface, Is.EqualTo(type));
                Assert.That(impact.LastClip, Is.Not.Null);
            }

            Object.Destroy(surfaceObject);
            controller.Close();
        }

        private static void AssertNameBackground(InventoryItemView view, string expected, bool compareSource = false)
        {
            TMP_Text label = view.transform.Find("Overlay/Name").GetComponent<TMP_Text>();
            Image background = view.transform.Find("Overlay/NameBackground").GetComponent<Image>();
            label.ForceMeshUpdate();
            Assert.That(compareSource ? label.text : label.GetParsedText(), Is.EqualTo(expected));
            Assert.That(label.isActiveAndEnabled, Is.True);
            Assert.That(label.canvasRenderer.GetInheritedAlpha(), Is.GreaterThan(0f));
            Assert.That(background.isActiveAndEnabled, Is.True);
            Assert.That(background.color, Is.EqualTo(view.Grid.Window.Theme.ItemInfoBackground));
            Assert.That(background.raycastTarget, Is.False);
            Assert.That(background.transform.GetSiblingIndex(), Is.LessThan(label.transform.GetSiblingIndex()));
            Rect area = background.rectTransform.rect;
            Assert.That(area.width, Is.GreaterThan(0f));
            Assert.That(area.height, Is.GreaterThan(0f));

            for (int i = 0; i < label.textInfo.characterCount; i++)
            {
                TMP_CharacterInfo character = label.textInfo.characterInfo[i];

                if (character.isVisible == false)
                {
                    continue;
                }

                Vector3 min = background.rectTransform.InverseTransformPoint(label.rectTransform.TransformPoint(character.bottomLeft));
                Vector3 max = background.rectTransform.InverseTransformPoint(label.rectTransform.TransformPoint(character.topRight));
                Assert.That(min.x, Is.GreaterThanOrEqualTo(area.xMin - 0.01f));
                Assert.That(min.y, Is.GreaterThanOrEqualTo(area.yMin - 0.01f));
                Assert.That(max.x, Is.LessThanOrEqualTo(area.xMax + 0.01f));
                Assert.That(max.y, Is.LessThanOrEqualTo(area.yMax + 0.01f));
            }
        }

        private static ItemInstance AddItem(InventoryWindowController controller, string section, string containerId, int amount = 1)
        {
            foreach (ItemDefinition definition in controller.Inventory.Database.Definitions)
            {
                if (definition == null)
                {
                    continue;
                }

                string definitionSection = definition.GetComponent<AmmoStatsComponent>()?.Section ?? definition.GetComponent<WeaponAddonComponent>()?.Section ?? DefinitionSection(definition);

                if (definitionSection == section || definition.name == section || definition.name == section + "_definition")
                {
                    ItemInstance item = controller.Inventory.Model.CreateItem(definition, amount);
                    Assert.That(controller.Inventory.Model.TryAddAuto(item, containerId, false).Success, Is.True, section);
                    return item;
                }
            }

            Assert.Fail("Missing item definition: " + section);
            return null;
        }

        private static Vector2 ScreenPoint(InventoryItemView view)
        {
            RectTransform rect = view.RectTransform;
            Canvas canvas = view.GetComponentInParent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        }

        private IEnumerator Hover(InventoryItemView view)
        {
            Vector2 position = ScreenPoint(view);
            Set(Mouse.current.position, position);
            var pointer = new PointerEventData(EventSystem.current) { position = position };

            if (_hoveredView != null)
            {
                ExecuteEvents.Execute(_hoveredView.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            }

            _hoveredView = view;
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            yield return null;
            yield return null;
        }
    }
}
