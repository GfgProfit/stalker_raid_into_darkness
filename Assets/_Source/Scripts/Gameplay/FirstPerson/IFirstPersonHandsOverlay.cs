using UnityEngine;

namespace RaidIntoDarkness.FirstPerson
{
    public interface IFirstPersonHandsOverlay
    {
        bool TryPlayOverlay(AnimationClip clip, float speed);
        void StopOverlay();
    }
}
