using System;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Player
{
    [Serializable]
    public sealed class StalkerBobbingValues
    {
        [Tooltip("[bobbing_effector] walk_amplitude (meters and radians).")]
        [SerializeField] private float _walkAmplitude = 0.005f;
        [Tooltip("[bobbing_effector] run_amplitude.")]
        [SerializeField] private float _runAmplitude = 0.0075f;
        [Tooltip("[bobbing_effector] limp_amplitude.")]
        [SerializeField] private float _limpAmplitude = 0.011f;
        [Tooltip("[bobbing_effector] walk_speed (radians per second).")]
        [SerializeField] private float _walkSpeed = 6.26f;
        [Tooltip("[bobbing_effector] run_speed.")]
        [SerializeField] private float _runSpeed = 6.74f;
        [Tooltip("[bobbing_effector] limp_speed.")]
        [SerializeField] private float _limpSpeed = 4.6f;
        [Tooltip("EffectorBobbing.cpp CROUCH_FACTOR.")]
        [SerializeField] private float _crouchFactor = 0.75f;
        [Tooltip("EffectorBobbing.cpp SPEED_REMINDER: fade in/out rate per second.")]
        [SerializeField] private float _speedReminder = 5.0f;
        [Tooltip("Console 'head_bob_factor' (g_head_bob_factor).")]
        [SerializeField] private float _headBobFactor = 1.0f;
        [Tooltip("Unity adaptation: max extra phase speed (rad/s) used to re-lock the bob onto the body step cycle after a clip or direction change, instead of jumping.")]
        [SerializeField] private float _phaseCorrection = 4.0f;
        [Tooltip("Unity adaptation: time constant (s) for blending between walk, run and limp amplitudes. X-Ray switches them instantly. 0 restores the instant switch.")]
        [SerializeField] private float _amplitudeBlendTime = 0.1f;

        public float WalkAmplitude => _walkAmplitude;
        public float RunAmplitude => _runAmplitude;
        public float LimpAmplitude => _limpAmplitude;
        public float WalkSpeed => _walkSpeed;
        public float RunSpeed => _runSpeed;
        public float LimpSpeed => _limpSpeed;
        public float CrouchFactor => _crouchFactor;
        public float SpeedReminder => _speedReminder;
        public float HeadBobFactor => _headBobFactor;
        public float PhaseCorrection => _phaseCorrection;
        public float AmplitudeBlendTime => _amplitudeBlendTime;
    }

    [Serializable]
    public sealed class StalkerFallEffectValues
    {
        [Tooltip("EffectorFall.cpp FALL_SPEED: phase per second.")]
        [SerializeField] private float _speed = 3.5f;
        [Tooltip("EffectorFall.cpp FALL_MAXDIST (meters).")]
        [SerializeField] private float _maxDistance = 0.15f;
        [Tooltip("gcontact_Power = contact speed / ph_crash_speed_max ([actor]).")]
        [SerializeField] private float _crashSpeedMax = 41.0f;

        public float Speed => _speed;
        public float MaxDistance => _maxDistance;
        public float CrashSpeedMax => _crashSpeedMax;
    }

    [Serializable]
    public sealed class StalkerAnmEffectValues
    {
        [Tooltip("camera_reanim_project.script actor_on_footstep, IsMoveState('mcSprint').")]
        [SerializeField] private XRayAnmAsset _sprintStep;
        [Tooltip("camera_reanim_project.script actor_on_footstep, default.")]
        [SerializeField] private XRayAnmAsset _moveStep;
        [Tooltip("camera_reanim_project.script actor_on_footstep, db.actor.power below the threshold.")]
        [SerializeField] private XRayAnmAsset _tiredStep;
        [SerializeField] private float _tiredPower = 0.25f;
        [SerializeField] private XRayAnmAsset _jump;
        [SerializeField] private XRayAnmAsset[] _landing = new XRayAnmAsset[0];
        [Tooltip("camera_reanim_project.script actor_on_land: fall_speed threshold.")]
        [SerializeField] private float _landingSpeed = 5.0f;
        [Tooltip("camera_reanim_project.script tg_anim1_delay (seconds).")]
        [SerializeField] private float _landingCooldown = 1.0f;
        [SerializeField] private XRayAnmAsset _crouch;
        [SerializeField] private XRayAnmAsset _stand;
        [Tooltip("Actor_Movement.cpp: anims\\camera_effects\\actor_move\\strafe_left.anm.")]
        [SerializeField] private XRayAnmAsset _strafeLeft;
        [SerializeField] private XRayAnmAsset _strafeRight;
        [Tooltip("Actor_Movement.cpp: CAnimatorCamLerpEffectorConst factor = cam_eff_factor / max_scale.")]
        [SerializeField] private float _moveEffectMaxScale = 70.0f;

        public XRayAnmAsset SprintStep => _sprintStep;
        public XRayAnmAsset MoveStep => _moveStep;
        public XRayAnmAsset TiredStep => _tiredStep;
        public float TiredPower => _tiredPower;
        public XRayAnmAsset Jump => _jump;
        public XRayAnmAsset[] Landing => _landing;
        public float LandingSpeed => _landingSpeed;
        public float LandingCooldown => _landingCooldown;
        public XRayAnmAsset Crouch => _crouch;
        public XRayAnmAsset Stand => _stand;
        public XRayAnmAsset StrafeLeft => _strafeLeft;
        public XRayAnmAsset StrafeRight => _strafeRight;
        public float MoveEffectMaxScale => _moveEffectMaxScale;
    }

    public enum StalkerBobPhaseSource
    {
        EngineTime,
        BodyAnimation
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/Player/Camera Effects Profile", fileName = "StalkerCameraEffectsProfile")]
    public sealed class StalkerCameraEffectsProfile : ScriptableObject
    {
        [Header("Original X-Ray Values")]
        [SerializeField] private StalkerBobbingValues _bobbing = new StalkerBobbingValues();
        [SerializeField] private StalkerFallEffectValues _fall = new StalkerFallEffectValues();
        [SerializeField] private StalkerAnmEffectValues _anm = new StalkerAnmEffectValues();
        [Header("Unity Adaptation")]
        [SerializeField] private bool _enabled = true;
        [SerializeField] private bool _bobbingEnabled = true;
        [SerializeField] private bool _fallEnabled = true;
        [SerializeField] private bool _anmEffectsEnabled = true;
        [Tooltip("ReanimationProject MCM crpmcm/moving.")]
        [SerializeField] private bool _stepEffectsEnabled = true;
        [Tooltip("ReanimationProject MCM crpmcm/jump_land.")]
        [SerializeField] private bool _jumpLandEffectsEnabled = true;
        [Tooltip("ReanimationProject MCM crpmcm/crouch.")]
        [SerializeField] private bool _crouchEffectsEnabled = true;
        [SerializeField] private bool _moveEffectsEnabled = true;
        [Tooltip("Unity adaptation: bobbing fades out without ground contact. X-Ray keeps it in the air while mcAnyMove is set.")]
        [SerializeField] private bool _bobRequiresGround = true;
        [Tooltip("Scales every effect; 1 keeps the original values.")]
        [SerializeField] private float _intensity = 1.0f;
        [SerializeField] private float _bobbingScale = 1.0f;
        [SerializeField] private float _landingScale = 1.0f;
        [SerializeField] private float _jumpScale = 1.0f;
        [Tooltip("Unity adaptation: an ANM effect restarted before it ended fades out over this time instead of snapping to the new one. X-Ray replaces it instantly.")]
        [SerializeField] private float _anmRestartFade = 0.12f;
        [Tooltip("EngineTime keeps the global timer of CEffectorBobbing; BodyAnimation drives the phase from the body locomotion cycle.")]
        [SerializeField] private StalkerBobPhaseSource _bobPhaseSource = StalkerBobPhaseSource.BodyAnimation;
        [TextArea(3, 20)]
        [SerializeField] private string _sourceReport;

        public StalkerBobbingValues Bobbing => _bobbing;
        public StalkerFallEffectValues Fall => _fall;
        public StalkerAnmEffectValues Anm => _anm;
        public bool Enabled => _enabled;
        public bool BobbingEnabled => _bobbingEnabled;
        public bool FallEnabled => _fallEnabled;
        public bool AnmEffectsEnabled => _anmEffectsEnabled;
        public bool StepEffectsEnabled => _anmEffectsEnabled && _stepEffectsEnabled;
        public bool JumpLandEffectsEnabled => _anmEffectsEnabled && _jumpLandEffectsEnabled;
        public bool CrouchEffectsEnabled => _anmEffectsEnabled && _crouchEffectsEnabled;
        public bool MoveEffectsEnabled => _moveEffectsEnabled;
        public bool BobRequiresGround => _bobRequiresGround;
        public float Intensity => _intensity;
        public float BobbingScale => _bobbingScale;
        public float LandingScale => _landingScale;
        public float JumpScale => _jumpScale;
        public float AnmRestartFade => _anmRestartFade;
        public StalkerBobPhaseSource BobPhaseSource => _bobPhaseSource;
        public string SourceReport => _sourceReport;
    }
}
