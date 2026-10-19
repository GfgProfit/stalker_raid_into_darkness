using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WorldItem : MonoBehaviour
    {
        [SerializeField] private ItemDefinition _definition;
        [SerializeField] [Min(1)] private int _amount = 1;

        public ItemDefinition Definition => _definition;
        public int Amount => Mathf.Max(1, _amount);

        public void Configure(ItemDefinition definition, int amount)
        {
            _definition = definition;
            _amount = Mathf.Max(1, amount);
        }
    }
}
