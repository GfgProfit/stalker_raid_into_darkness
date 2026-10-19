using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    [Serializable]
    public sealed class InventoryFilterTab
    {
        [SerializeField] private string _id;
        [SerializeField] private Button _button;
        [SerializeField] private List<string> _categories = new();
        [SerializeField] private List<string> _tags = new();

        public InventoryFilterTab()
        {
        }

        public InventoryFilterTab(string id, Button button, IEnumerable<string> categories, IEnumerable<string> tags = null)
        {
            _id = id;
            _button = button;
            _categories = categories == null ? new List<string>() : new List<string>(categories);
            _tags = tags == null ? new List<string>() : new List<string>(tags);
        }

        public string Id => _id ?? string.Empty;
        public Button Button => _button;
        public bool ShowsAll => (_categories == null || _categories.Count == 0) && (_tags == null || _tags.Count == 0);

        public bool Matches(ItemInstance item)
        {
            if (ShowsAll)
            {
                return true;
            }

            ItemDefinition definition = item?.Definition;

            if (definition == null)
            {
                return false;
            }

            for (int i = 0; i < _categories.Count; i++)
            {
                if (definition.IsCategory(_categories[i]))
                {
                    return true;
                }
            }

            for (int i = 0; i < _tags.Count; i++)
            {
                if (definition.HasTag(_tags[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
