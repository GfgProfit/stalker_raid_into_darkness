using System;

namespace RaidIntoDarkness.Player
{
    [Flags]
    public enum StalkerMoveFlags
    {
        None = 0,
        Forward = 1 << 0,
        Back = 1 << 1,
        LeftStrafe = 1 << 2,
        RightStrafe = 1 << 3,
        Crouch = 1 << 4,
        Accel = 1 << 5,
        Turn = 1 << 6,
        Jump = 1 << 7,
        Fall = 1 << 8,
        Landing = 1 << 9,
        Landing2 = 1 << 10,
        Climb = 1 << 11,
        Sprint = 1 << 12,
        LeftLookout = 1 << 13,
        RightLookout = 1 << 14,
        AnyMove = Forward | Back | LeftStrafe | RightStrafe
    }

    public static class StalkerMoveFlagsExtensions
    {
        public static bool Has(this StalkerMoveFlags flags, StalkerMoveFlags mask)
        {
            return (flags & mask) != 0;
        }

        public static bool IsAccelerated(this StalkerMoveFlags flags)
        {
            bool accelerated = (flags & StalkerMoveFlags.Accel) == 0;
            if (flags.Has(StalkerMoveFlags.Crouch | StalkerMoveFlags.Climb | StalkerMoveFlags.Jump | StalkerMoveFlags.Landing | StalkerMoveFlags.Landing2))
            {
                return accelerated;
            }

            return !flags.Has(StalkerMoveFlags.LeftLookout | StalkerMoveFlags.RightLookout) && accelerated;
        }
    }
}
