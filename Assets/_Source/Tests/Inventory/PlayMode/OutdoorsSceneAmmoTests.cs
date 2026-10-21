using System.Collections;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using NUnit.Framework;
using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class OutdoorsSceneAmmoTests : InputTestFixture
    {
        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        [UnityTest]
        public IEnumerator ImportedAmmoHasHdSpritesMaterialsAndGraParameters()
        {
            SceneManager.LoadScene("OutdoorsScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            GameObject group = GameObject.Find("WorldItems/Ammo");
            Assert.That(group, Is.Not.Null);
            WorldItem[] items = group.GetComponentsInChildren<WorldItem>();
            Assert.That(items.Length, Is.EqualTo(45));
            var sections = new HashSet<string>();

            foreach (WorldItem item in items)
            {
                ItemDefinition definition = item.Definition;
                AmmoStatsComponent stats = definition.GetComponent<AmmoStatsComponent>();
                Assert.That(stats, Is.Not.Null, definition.name);
                Assert.That(sections.Add(stats.Section), Is.True, stats.Section);
                Assert.That(stats.Section, Does.Not.EndWith("_bad").And.Not.EndWith("_verybad"));
                Assert.That(item.Amount, Is.EqualTo(stats.BoxSize), stats.Section);
                Assert.That(definition.Weight * stats.BoxSize, Is.EqualTo(stats.BoxWeight).Within(0.00001f), stats.Section);
                Assert.That(definition.Size, Is.EqualTo(new Vector2Int(2, 1)), stats.Section);
                Assert.That(definition.MaxStack, Is.EqualTo(stats.BoxSize), stats.Section);
                Assert.That(definition.IconOverride, Is.Not.Null, stats.Section);
                Assert.That(definition.IconOverride.rect.size, Is.EqualTo(new Vector2(200f, 100f)), stats.Section);
                Assert.That(definition.IconOverride.texture.name, Is.EqualTo("ui_raid_hd_ammo"), stats.Section);
                Assert.That(definition.CanGenerateIcon, Is.False, stats.Section);
                Assert.That(stats.HasTooltipParameters, Is.True, stats.Section);
                Assert.That(definition.Description, Does.Contain("<color=#5D0074>").And.Contain("повреждение тканей:"), stats.Section);
                Assert.That(definition.Description, Does.Not.Contain("%c["), stats.Section);
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
                        Assert.That(material.shader.name, Is.EqualTo("HDRP/Lit"), stats.Section);
                        Assert.That(material.GetTexture("_BaseColorMap"), Is.Not.Null, stats.Section);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator AmmoPickupTransferSplitTooltipDropAndSurfaceSoundsWork()
        {
            SceneManager.LoadScene("OutdoorsScene", LoadSceneMode.Single);
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
            Assert.That(item.TotalWeight, Is.EqualTo(0.16f * 7f / 15f).Within(0.00001f));
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
            Assert.That(split.Item.TotalWeight, Is.EqualTo(0.16f * 3f / 15f).Within(0.00001f));
            window.Tooltip.Show(split.Item, new Vector2(500f, 500f), null);
            Assert.That(window.Tooltip.Title, Is.EqualTo(definition.DisplayName));
            Assert.That(window.Tooltip.Description, Does.Contain("пробивает броню класса:").And.Contain("(11)").And.Contain("(22)"));
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
    }
}
