using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class SpawnMenuCategory
    {
        private readonly List<SpawnMenuCategory> _children = new();

        public SpawnMenuCategory(string title, SpawnMenuCategory parent, int order)
        {
            Title = title;
            Parent = parent;
            Order = order;
            Depth = parent != null ? parent.Depth + 1 : 0;
            Expanded = Depth <= 1;
        }

        public string Title { get; }
        public SpawnMenuCategory Parent { get; }
        public int Order { get; }
        public int Depth { get; }
        public IReadOnlyList<SpawnMenuCategory> Children => _children;
        public bool Expanded { get; set; }

        public SpawnMenuCategory GetOrAdd(string title, int order)
        {
            foreach (SpawnMenuCategory child in _children)
            {
                if (child.Title == title)
                {
                    return child;
                }
            }
            var category = new SpawnMenuCategory(title, this, order);
            _children.Add(category);
            return category;
        }

        public bool BelongsTo(SpawnMenuCategory parent)
        {
            for (SpawnMenuCategory category = this; category != null; category = category.Parent)
            {
                if (category == parent)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
