using RaidIntoDarkness.Inventory.Domain;
using System;
using RaidIntoDarkness.FirstPerson;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    internal sealed class EquipmentAnimationStep
    {
        public EquipmentAnimationStepKind Kind { get; set; }
        public Action Move { get; set; }
        public FirstPersonHudMotion Motion { get; set; }
        public float Speed { get; set; }
        public bool FadeAtEnd { get; set; }
        public bool Started { get; set; }
        public bool FadeStarted { get; set; }
        public bool FadeStopped { get; set; }
        public bool Completed { get; set; }
        public float Time { get; set; }
        public ItemInstance Item { get; set; }
    }
}
