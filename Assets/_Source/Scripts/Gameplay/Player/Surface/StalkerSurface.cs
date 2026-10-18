using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerSurface : MonoBehaviour
    {
        [SerializeField] private StalkerSurfaceType _type = StalkerSurfaceType.Earth;

        public StalkerSurfaceType Type
        {
            get => _type;
            set => _type = value;
        }
    }
}
