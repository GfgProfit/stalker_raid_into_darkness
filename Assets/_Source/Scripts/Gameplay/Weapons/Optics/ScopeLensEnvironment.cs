using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    [DisallowMultipleComponent]
    public sealed class ScopeLensEnvironment : MonoBehaviour
    {
        [SerializeField] private bool _useBakedEnvironment;
        [SerializeField] private Cubemap _cubemap;

        public bool UseBakedEnvironment => _useBakedEnvironment;
        public Cubemap Cubemap => _cubemap;
    }
}
