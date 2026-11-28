using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    internal sealed class EquipmentHudPlayback
    {
        private readonly StalkerPlayerController _player;
        private readonly FirstPersonHudAnimator _animator;

        public EquipmentHudPlayback(StalkerPlayerController player, FirstPersonHudAnimator animator)
        {
            _player = player;
            _animator = animator;
        }

        public bool AdvanceFade(EquipmentAnimationStep step, float deltaTime)
        {
            StalkerScreenFade fade = _player != null ? _player.ScreenFade : null;
            if (step.FadeStarted == false)
            {
                step.FadeStarted = true;
                step.Time = 0.0f;
                if (fade != null)
                {
                    fade.Play();
                }

                return false;
            }

            step.Time += deltaTime;
            if (step.FadeStopped == false)
            {
                step.FadeStopped = true;
                if (fade != null)
                {
                    fade.Stop();
                }

                return false;
            }

            return step.Time > EquipmentAnimationQueue.UNEQUIP_FADE_DELAY;
        }

        public bool AdvanceAnimation(EquipmentAnimationStep step, float deltaTime)
        {
            FirstPersonHudAnimator animator = _animator;
            if (animator == null)
            {
                return true;
            }

            if (step.Started == false)
            {
                if (animator.Rig != null && animator.Rig.HandsBusy)
                {
                    animator.Rig.RequestHands(animator);
                    return false;
                }

                step.Started = true;
                step.Time = 0.0f;
                return animator.TryPlay(step.Motion, step.Speed) == false;
            }

            step.Time += deltaTime;
            bool playing = animator.IsPlaying && animator.Current == step.Motion;
            if (step.FadeAtEnd)
            {
                TickEquipFade(step, animator.Duration, playing);
            }

            return playing == false;
        }

        private void TickEquipFade(EquipmentAnimationStep step, float duration, bool playing)
        {
            StalkerScreenFade fade = _player != null ? _player.ScreenFade : null;
            if (fade == null)
            {
                return;
            }

            if (step.FadeStarted == false && (step.Time > duration - EquipmentAnimationQueue.EQUIP_FADE_START || playing == false))
            {
                step.FadeStarted = true;
                fade.Play();
            }

            if (step.FadeStarted && step.FadeStopped == false && (step.Time > duration - EquipmentAnimationQueue.EQUIP_FADE_STOP || playing == false))
            {
                step.FadeStopped = true;
                fade.Stop();
            }
        }
    }
}
