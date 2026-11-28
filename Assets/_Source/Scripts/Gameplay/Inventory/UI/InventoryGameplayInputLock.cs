using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryGameplayInputLock
    {
        private int _restoreFrame = -1;

        public bool IsBlocked { get; private set; }

        public void ScheduleRestore() => _restoreFrame = Time.frameCount;

        public void Tick(StalkerPlayerController player)
        {
            if (_restoreFrame >= 0 && Time.frameCount > _restoreFrame)
            {
                Restore(player);
            }
        }

        public void KeepCursorReleased(StalkerPlayerController player, bool inventoryOpen)
        {
            if (inventoryOpen && IsBlocked && Cursor.lockState != CursorLockMode.None)
            {
                ReleaseCursor(player);
            }
        }

        public void Block(StalkerPlayerController player)
        {
            _restoreFrame = -1;
            IsBlocked = true;

            if (player != null)
            {
                player.GameplayInputBlocked = true;
            }

            ReleaseCursor(player);
        }

        public void Restore(StalkerPlayerController player)
        {
            _restoreFrame = -1;

            if (IsBlocked == false)
            {
                return;
            }

            IsBlocked = false;

            if (player != null)
            {
                player.GameplayInputBlocked = false;

                if (player.CameraController != null)
                {
                    player.CameraController.SetCursorLocked(true);
                }
            }
        }

        public void ReleaseCursor(StalkerPlayerController player)
        {
            if (player != null && player.CameraController != null)
            {
                player.CameraController.SetCursorLocked(false);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}
