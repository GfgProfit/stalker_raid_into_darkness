using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryGeneratedElement : MonoBehaviour
    {
        [SerializeField] private InventoryGeneratedRole _role;

        public InventoryGeneratedRole Role
        {
            get => _role;
            set => _role = value;
        }
    }
}
