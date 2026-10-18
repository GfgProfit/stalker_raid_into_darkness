using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public enum StalkerMovementMode
    {
        Standing,
        Walking,
        Running,
        Sprinting,
        Crouching,
        LowCrouching,
        Airborne,
        Landing,
        Ladder
    }

    public readonly struct StalkerMovementState
    {
        public StalkerMovementState(StalkerMoveFlags flags, StalkerEnvironment environment, Vector3 velocity, Vector3 localMove, int box, bool slowedDown, float actualSpeed, float maxSpeed)
        {
            Flags = flags;
            Environment = environment;
            Velocity = velocity;
            LocalMove = localMove;
            Box = box;
            IsSlowedDown = slowedDown;
            ActualSpeed = actualSpeed;
            MaxSpeed = maxSpeed;
        }

        public StalkerMoveFlags Flags { get; }
        public StalkerEnvironment Environment { get; }
        public Vector3 Velocity { get; }
        public Vector3 LocalMove { get; }
        public int Box { get; }
        public bool IsSlowedDown { get; }
        public float ActualSpeed { get; }
        public float MaxSpeed { get; }
        public Vector3 HorizontalVelocity => new Vector3(Velocity.x, 0.0f, Velocity.z);
        public float VerticalVelocity => Velocity.y;
        public bool IsGrounded => Environment == StalkerEnvironment.OnGround;
        public bool IsCrouching => Flags.Has(StalkerMoveFlags.Crouch);
        public bool IsLowCrouching => Flags.Has(StalkerMoveFlags.Crouch) && Flags.Has(StalkerMoveFlags.Accel);
        public bool IsSprinting => Flags.Has(StalkerMoveFlags.Sprint);
        public bool IsClimbing => Flags.Has(StalkerMoveFlags.Climb);
        public bool IsJumping => Flags.Has(StalkerMoveFlags.Jump | StalkerMoveFlags.Fall);
        public bool IsMoving => Flags.Has(StalkerMoveFlags.AnyMove);

        public StalkerMovementMode Mode
        {
            get
            {
                if (IsClimbing)
                {
                    return StalkerMovementMode.Ladder;
                }

                if (Flags.Has(StalkerMoveFlags.Landing | StalkerMoveFlags.Landing2))
                {
                    return StalkerMovementMode.Landing;
                }

                if (IsJumping || Environment == StalkerEnvironment.InAir)
                {
                    return StalkerMovementMode.Airborne;
                }

                if (IsCrouching)
                {
                    return IsLowCrouching ? StalkerMovementMode.LowCrouching : StalkerMovementMode.Crouching;
                }

                if (!IsMoving)
                {
                    return StalkerMovementMode.Standing;
                }

                if (IsSprinting)
                {
                    return StalkerMovementMode.Sprinting;
                }

                return Flags.IsAccelerated() ? StalkerMovementMode.Running : StalkerMovementMode.Walking;
            }
        }
    }
}
