using System;
using System.Collections.Generic;
using System.Globalization;
using Inventory;
using RaidIntoDarkness.Integrations.Inventory;

namespace RaidIntoDarkness.UI
{
    internal enum SpawnMenuSort
    {
        Name,
        Weight,
        Price
    }

    internal sealed class SpawnMenuCatalog
    {
        private static readonly CompareInfo NAME_COMPARISON = CultureInfo.GetCultureInfo("ru-RU").CompareInfo;

        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        private readonly List<Entry> _entries = new();
        private int _nextCategoryOrder;

        public Category Root { get; } = new("Предметы", null, 0);
        public IReadOnlyList<Entry> Entries => _entries;
        public int MaxWidth { get; private set; } = 1;

        public void Add(ItemDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId) || !_ids.Add(definition.DefinitionId))
            {
                return;
            }

            WeaponStatsComponent weapon = definition.GetComponent<WeaponStatsComponent>();
            AmmoStatsComponent ammo = definition.GetComponent<AmmoStatsComponent>();
            WeaponAddonComponent addon = definition.GetComponent<WeaponAddonComponent>();
            var (group, subgroup) = Classify(definition, weapon, ammo, addon);
            var category = Root.GetOrAdd(group, ++_nextCategoryOrder);
            if (subgroup != null)
            {
                category = category.GetOrAdd(subgroup, ++_nextCategoryOrder);
            }

            int? price = ItemCostComponent.GetBaseCost(definition);
            _entries.Add(new Entry(definition, category, price, EquipmentStatsFormatter.SupportsCondition(definition), Math.Max(1, ammo?.BoxSize ?? 1)));
            MaxWidth = Math.Max(MaxWidth, definition.Width);
        }

        public void Filter(List<Entry> results, Category selected, string search, SpawnMenuSort sort, bool descending)
        {
            results.Clear();
            foreach (Entry entry in _entries)
            {
                if (entry.Category.BelongsTo(selected) && NAME_COMPARISON.IndexOf(entry.Definition.DisplayName, search, CompareOptions.IgnoreCase) >= 0)
                {
                    results.Add(entry);
                }
            }
            results.Sort((first, second) => Compare(first, second, sort, descending));
        }

        private static int Compare(Entry first, Entry second, SpawnMenuSort sort, bool descending)
        {
            int group = first.Group.CompareTo(second.Group);
            if (group != 0)
            {
                return group;
            }

            if (sort == SpawnMenuSort.Price && first.Price.HasValue != second.Price.HasValue)
            {
                return first.Price.HasValue ? -1 : 1;
            }
            int order = sort switch
            {
                SpawnMenuSort.Weight => first.Definition.Weight.CompareTo(second.Definition.Weight),
                SpawnMenuSort.Price => Nullable.Compare(first.Price, second.Price),
                _ => CompareNames(first, second)
            };
            if (order != 0)
            {
                return descending ? -Math.Sign(order) : order;
            }
            order = CompareNames(first, second);
            return order != 0 ? order : StringComparer.Ordinal.Compare(first.Definition.DefinitionId, second.Definition.DefinitionId);
        }

        private static int CompareNames(Entry first, Entry second) => NAME_COMPARISON.Compare(first.Definition.DisplayName, second.Definition.DisplayName, CompareOptions.IgnoreCase);

        private static (string, string) Classify(ItemDefinition definition, WeaponStatsComponent weapon, AmmoStatsComponent ammo, WeaponAddonComponent addon)
        {
            if (weapon != null || definition.IsCategory("Weapon"))
            {
                return ("Оружие", weapon?.WeaponClass.ToLowerInvariant() switch
                {
                    "pistol" => "Пистолеты",
                    "smg" => "Пистолеты-пулемёты",
                    "assault_rifle" => "Автоматы",
                    "shotgun" => "Дробовики",
                    "sniper_rifle" => "Снайперские винтовки",
                    "machine_gun" => "Пулемёты",
                    "knife" => "Холодное оружие",
                    "grenade" => "Ручные гранаты",
                    "grenade_launcher" => "Гранатомёты",
                    _ => definition.HasTag("Rifle") ? "Винтовки" : null
                });
            }
            if (addon != null || definition.IsCategory("Attachment"))
            {
                return ("Модули оружия", addon?.Kind switch
                {
                    WeaponAddonKind.Scope => "Прицелы",
                    WeaponAddonKind.Silencer => "Дульные устройства",
                    WeaponAddonKind.GrenadeLauncher => "Подствольные гранатомёты",
                    _ => null
                });
            }
            if (ammo != null || definition.IsCategory("Ammo"))
            {
                return ("Боеприпасы", IsLauncherRound(ammo?.Section) ? "Выстрелы для гранатомётов" : "Патроны");
            }

            return definition.Category.ToLowerInvariant() switch
            {
                "grenade" => ("Боеприпасы", "Гранаты"),
                "armor" => ("Броня и экипировка", "Костюмы"),
                "head" => ("Броня и экипировка", "Шлемы"),
                "backpack" => ("Броня и экипировка", "Рюкзаки"),
                "patch" => ("Броня и экипировка", "Нашивки"),
                "medicine" or "medical" => ("Медикаменты", null),
                "food" or "drink" => ("Еда и напитки", null),
                "artifact" or "artefact" => ("Артефакты", null),
                "detector" => ("Детекторы", null),
                "tool" or "repair" => ("Инструменты и ремонт", null),
                "mutantpart" => ("Части мутантов", null),
                "quest" => ("Квестовые предметы", null),
                _ => ("Разное", null)
            };
        }

        private static bool IsLauncherRound(string section) => section is "ammo_m209" or "ammo_m397a1" or "ammo_og-7b" or "ammo_vog-25" or "ammo_vog-25p";

        internal sealed class Category
        {
            private readonly List<Category> _children = new();

            public Category(string title, Category parent, int order)
            {
                Title = title;
                Parent = parent;
                Order = order;
                Depth = parent != null ? parent.Depth + 1 : 0;
                Expanded = Depth <= 1;
            }

            public string Title { get; }
            public Category Parent { get; }
            public int Order { get; }
            public int Depth { get; }
            public IReadOnlyList<Category> Children => _children;
            public bool Expanded { get; set; }

            public Category GetOrAdd(string title, int order)
            {
                foreach (Category child in _children)
                {
                    if (child.Title == title)
                    {
                        return child;
                    }
                }
                var category = new Category(title, this, order);
                _children.Add(category);
                return category;
            }

            public bool BelongsTo(Category parent)
            {
                for (Category category = this; category != null; category = category.Parent)
                {
                    if (category == parent)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        internal sealed class Entry
        {
            public Entry(ItemDefinition definition, Category category, int? price, bool supportsCondition, int packSize)
            {
                Definition = definition;
                Category = category;
                Group = (InventorySortingPlus.GetKindOrder(definition), category.Order);
                Price = price;
                SupportsCondition = supportsCondition;
                PackSize = packSize;
            }

            public ItemDefinition Definition { get; }
            public Category Category { get; }
            public (int Kind, int Category) Group { get; }
            public int? Price { get; }
            public bool SupportsCondition { get; }
            public int PackSize { get; }
            public int MaxQuantity => int.MaxValue / PackSize;
            public int GetUnitCount(int quantity) => quantity * PackSize;
            public int Quantity { get; set; } = 1;
            public int Condition { get; set; } = 100;
        }
    }
}
