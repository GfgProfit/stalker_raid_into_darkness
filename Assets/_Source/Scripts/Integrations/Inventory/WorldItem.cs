using Inventory;
using Inventory.IconGeneration;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WorldItem : MonoBehaviour
    {
        [SerializeField] private ItemDefinition _definition;
        [SerializeField] [Min(1)] private int _amount = 1;

        private ItemInstance _instance;

        public ItemDefinition Definition => _definition;
        public int Amount => Mathf.Max(1, _amount);
        public ItemInstance Instance => _instance;

        public void Configure(ItemInstance item)
        {
            _definition = item.Definition;
            _amount = item.Amount;
            _instance = item;
            ItemIconVisualConfigurators.Apply(gameObject, item.Definition, item.VisualVariant);
        }

        public void Configure(ItemDefinition definition, int amount)
        {
            _definition = definition;
            _amount = Mathf.Max(1, amount);
            _instance = null;
            ItemIconVisualConfigurators.Apply(gameObject, definition, null);
        }
    }
}
