using UnityEngine;

namespace RaidIntoDarkness.FirstPerson
{
    internal readonly struct FirstPersonCollisionShapeData
    {
        public FirstPersonCollisionShapeData(int bone, Bounds bounds)
        {
            Bone = bone;
            Bounds = bounds;
        }

        public int Bone { get; }
        public Bounds Bounds { get; }
    }
}
