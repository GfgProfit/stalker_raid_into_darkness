using System.Text;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerPlayerDebugOverlay : MonoBehaviour
    {
        private const float REFRESH_INTERVAL = 0.1f;

        [SerializeField] private StalkerPlayerController _controller;
        [SerializeField] private StalkerPlayerInput _input;
        [SerializeField] private bool _visible = true;
        [SerializeField] private bool _showProfile;

        private readonly StringBuilder _builder = new StringBuilder(2048);
        private string _text = string.Empty;
        private float _refresh;
        private GUIStyle _style;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        private void Update()
        {
            if (_input != null && _input.ToggleOverlayPressed)
            {
                _visible = !_visible;
            }

            _refresh -= Time.unscaledDeltaTime;
            if (_visible && _refresh <= 0.0f && _controller != null && _controller.Motor != null && _controller.Motor.Physics != null)
            {
                _refresh = REFRESH_INTERVAL;
                _text = Build();
            }
        }

        private void OnGUI()
        {
            if (!_visible || string.IsNullOrEmpty(_text))
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = false };
            }

            GUI.Box(new Rect(10.0f, 10.0f, 460.0f, _showProfile ? 560.0f : 330.0f), _text, _style);
        }

        private string Build()
        {
            StalkerMovementMotor motor = _controller.Motor;
            StalkerMovementState state = motor.State;
            StalkerActorCondition condition = motor.Condition;
            StalkerVisibleBody body = _controller.VisibleBody;
            StalkerBodyAnimationPlayer player = _controller.BodyPlayer;
            Vector3 horizontal = state.HorizontalVelocity;
            _builder.Clear();
            _builder.Append("F3 overlay, F4 combat, PgUp/PgDn outfit, Esc cursor\n");
            _builder.Append("State: ").Append(state.Mode).Append("  Env: ").Append(state.Environment).Append('\n');
            _builder.Append("mstate_real: ").Append((int)state.Flags).Append(" (").Append(state.Flags).Append(")\n");
            _builder.Append("Stance box: ").Append(state.Box).Append("  Camera height: ").Append(motor.Stance.CameraHeight.ToString("0.000")).Append('\n');
            _builder.Append("Actual speed: ").Append(state.ActualSpeed.ToString("0.000")).Append(" m/s  Max: ").Append(state.MaxSpeed.ToString("0.000")).Append('\n');
            _builder.Append("Horizontal: ").Append(horizontal.magnitude.ToString("0.000")).Append(" m/s  Vertical: ").Append(state.VerticalVelocity.ToString("0.000")).Append('\n');
            _builder.Append("Grounded: ").Append(motor.Physics.GoodGround).Append("  LoseControl: ").Append(motor.Physics.LoseControl).Append("  Jumping: ").Append(motor.Physics.Jumping).Append('\n');
            _builder.Append("Sprint: ").Append(state.IsSprinting).Append("  Slowed: ").Append(state.IsSlowedDown).Append("  Last contact: ").Append(motor.LastContactSpeed.ToString("0.00")).Append('\n');
            _builder.Append("Power: ").Append(condition.Power.ToString("0.0000")).Append(" / ").Append(condition.MaxPower.ToString("0.0000")).Append("  Limping: ").Append(condition.IsLimping()).Append("  CantSprint: ").Append(condition.IsCantSprint()).Append('\n');
            _builder.Append("run_coef: ").Append(condition.RunCoef.ToString("0.000")).Append("  run_back: ").Append(condition.RunBackCoef.ToString("0.000")).Append("  sprint: ").Append(condition.SprintKoef.ToString("0.000")).Append('\n');
            _builder.Append("Combat: ").Append(_controller.Combat).Append("  Table: ").Append(body != null ? body.ActiveTable.ToString() : "-").Append('\n');
            _builder.Append("Body motion: ").Append(player != null ? player.CurrentMotion : "-").Append("  offset: ").Append(body != null ? body.ForwardOffset.ToString("0.00") : "-").Append('\n');
            _builder.Append("Blend: ").Append(player != null ? player.DescribeBlend() : "-").Append('\n');
            _builder.Append("Outfit: ").Append(_controller.OutfitSection).Append('\n');
            if (_showProfile)
            {
                StalkerMovementProfile profile = _controller.Profile;
                StalkerActorValues actor = profile.Actor;
                StalkerEngineValues engine = profile.Engine;
                float walk = actor.WalkAccel / engine.MaxVelocityDivider;
                _builder.Append("\n[actor] walk_accel ").Append(actor.WalkAccel).Append(" -> walk ").Append(walk.ToString("0.000")).Append(" m/s (|accel| / 10)\n");
                _builder.Append("run_coef ").Append(actor.RunCoef).Append(" -> run ").Append((walk * condition.RunCoef).ToString("0.000")).Append(" m/s\n");
                _builder.Append("sprint_koef ").Append(actor.SprintKoef).Append(" -> sprint ").Append((walk * condition.RunCoef * condition.SprintKoef).ToString("0.000")).Append(" m/s\n");
                _builder.Append("crouch_coef ").Append(actor.CrouchCoef).Append(" -> crouch run ").Append((walk * condition.RunCoef * actor.CrouchCoef).ToString("0.000")).Append(", low ").Append((walk * actor.CrouchCoef).ToString("0.000")).Append(" m/s\n");
                _builder.Append("jump_speed ").Append(actor.JumpSpeed).Append(" -> apex ").Append((actor.JumpSpeed * actor.JumpSpeed / (2.0f * engine.Gravity)).ToString("0.000")).Append(" m (g ").Append(engine.Gravity).Append(")\n");
                _builder.Append("ground accel ").Append((engine.PullForce * engine.ActorFriction * engine.GroundFriction).ToString("0.00")).Append(" m/s^2, stop ").Append((engine.ActorFriction * engine.GroundFriction * engine.ActorFriction * engine.GroundFriction * engine.Gravity).ToString("0.00")).Append(" m/s^2\n");
                _builder.Append("air_control_param ").Append(actor.AirControlParam).Append(", camera_height_factor ").Append(actor.CameraHeightFactor).Append('\n');
            }

            return _builder.ToString();
        }
    }
}
