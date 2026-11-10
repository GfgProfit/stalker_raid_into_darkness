using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [Serializable]
    public sealed class StalkerActorValues
    {
        [SerializeField] private float _walkAccel = 11.5f;
        [SerializeField] private float _jumpSpeed = 6.85f;
        [SerializeField] private float _runCoef = 2.41f;
        [SerializeField] private float _runBackCoef = 1.33f;
        [SerializeField] private float _walkBackCoef = 0.61f;
        [SerializeField] private float _sprintKoef = 2.08f;
        [SerializeField] private float _crouchCoef = 0.45f;
        [SerializeField] private float _climbCoef = 0.5f;
        [SerializeField] private float _walkStrafeCoef = 1.0f;
        [SerializeField] private float _runStrafeCoef = 1.0f;
        [SerializeField] private float _sprintStrafeCoef = 0.8f;
        [SerializeField] private float _airControlParam = 0.02f;
        [SerializeField] private float _cameraHeightFactor = 0.92f;
        [SerializeField] private Vector3 _box0Center = new Vector3(0.0f, 0.9f, 0.0f);
        [SerializeField] private Vector3 _box0Size = new Vector3(0.3f, 0.9f, 0.3f);
        [SerializeField] private Vector3 _box1Center = new Vector3(0.0f, 0.67f, 0.0f);
        [SerializeField] private Vector3 _box1Size = new Vector3(0.35f, 0.67f, 0.3f);
        [SerializeField] private Vector3 _box2Center = new Vector3(0.0f, 0.22f, 0.0f);
        [SerializeField] private Vector3 _box2Size = new Vector3(0.35f, 0.22f, 0.35f);
        [SerializeField] private float _mass = 94.0f;
        [SerializeField] private float _crashSpeedMin = 15.0f;
        [SerializeField] private float _crashSpeedMax = 41.0f;
        [SerializeField] private float _maxItemMass = 50.0f;
        [SerializeField] private string _material = "creatures\\actor";

        public float WalkAccel => _walkAccel;
        public float JumpSpeed => _jumpSpeed;
        public float RunCoef => _runCoef;
        public float RunBackCoef => _runBackCoef;
        public float WalkBackCoef => _walkBackCoef;
        public float SprintKoef => _sprintKoef;
        public float CrouchCoef => _crouchCoef;
        public float ClimbCoef => _climbCoef;
        public float WalkStrafeCoef => _walkStrafeCoef;
        public float RunStrafeCoef => _runStrafeCoef;
        public float SprintStrafeCoef => _sprintStrafeCoef;
        public float AirControlParam => _airControlParam;
        public float CameraHeightFactor => _cameraHeightFactor;
        public float Mass => _mass;
        public float CrashSpeedMin => _crashSpeedMin;
        public float CrashSpeedMax => _crashSpeedMax;
        public float MaxItemMass => _maxItemMass;
        public string Material => _material;

        public Vector3 GetBoxCenter(int box)
        {
            return box == 2 ? _box2Center : box == 1 ? _box1Center : _box0Center;
        }

        public Vector3 GetBoxSize(int box)
        {
            return box == 2 ? _box2Size : box == 1 ? _box1Size : _box0Size;
        }
    }

    [Serializable]
    public sealed class StalkerEngineValues
    {
        [Tooltip("PHSimpleCharacter pull_force: ground control acceleration before friction (m/s^2).")]
        [SerializeField] private float _pullForce = 25.0f;
        [Tooltip("PHSimpleCharacter::PhTune side velocity damping (N per m/s).")]
        [SerializeField] private float _sideDamping = 500.0f;
        [Tooltip("PHSimpleCharacter::PhTune extra down acceleration while controlled without contacts (m/s^2).")]
        [SerializeField] private float _groundStickAcceleration = 50.0f;
        [Tooltip("default_world_gravity = 2 * 9.81 unless ph_gravity is changed.")]
        [SerializeField] private float _gravity = 19.62f;
        [Tooltip("1 / ph_frequency.")]
        [SerializeField] private float _fixedStep = 0.01f;
        [Tooltip("CPHMovementControl::Calculate: SetMaximumVelocity(|accel| / 10).")]
        [SerializeField] private float _maxVelocityDivider = 10.0f;
        [SerializeField] private float _loseControlDistance = 0.5f;
        [SerializeField] private float _jumpIncreaseVelocityRate = 1.2f;
        [SerializeField] private float _jumpDirectionalVelocity = 0.2f;
        [SerializeField] private float _jumpAirForce = 1000.0f;
        [SerializeField] private float _jumpAirBrakeForce = 3000.0f;
        [SerializeField] private float _jumpAirDistance = 0.3f;
        [SerializeField] private float _sprintAirStrafeInput = 0.5f;
        [SerializeField] private float _landingContactSpeed = 4.0f;
        [SerializeField] private float _landingTime1 = 0.1f;
        [SerializeField] private float _landingTime2 = 0.3f;
        [SerializeField] private float _jumpTime = 0.3f;
        [SerializeField] private float _jumpGroundTime = 0.1f;
        [SerializeField] private float _fallTime = 0.2f;
        [Tooltip("g_cl_ValidateMState: below this actual velocity the move flags are cleared.")]
        [SerializeField] private float _stuckVelocity = 0.2f;
        [Tooltip("gamemtl.xr fPHFriction of the actor material.")]
        [SerializeField] private float _actorFriction = 0.8f;
        [Tooltip("gamemtl.xr fPHFriction of the ground (materials\\earth).")]
        [SerializeField] private float _groundFriction = 1.0f;
        [SerializeField] private float _cameraIntoCollisionShift = 0.05f;
        [SerializeField] private float _cameraHeightInterpolationSpeed = 4.0f;
        [SerializeField] private float _climbPullMultiplier = 2.0f;
        [SerializeField] private float _ladderLookAngleCosine = 0.9238795f;
        [SerializeField] private float _ladderLookUpSine = 0.34202014f;
        [SerializeField] private float _ladderJumpVelocity = 3.0f;

        public float PullForce => _pullForce;
        public float SideDamping => _sideDamping;
        public float GroundStickAcceleration => _groundStickAcceleration;
        public float Gravity => _gravity;
        public float FixedStep => _fixedStep;
        public float MaxVelocityDivider => _maxVelocityDivider;
        public float LoseControlDistance => _loseControlDistance;
        public float JumpIncreaseVelocityRate => _jumpIncreaseVelocityRate;
        public float JumpDirectionalVelocity => _jumpDirectionalVelocity;
        public float JumpAirForce => _jumpAirForce;
        public float JumpAirBrakeForce => _jumpAirBrakeForce;
        public float JumpAirDistance => _jumpAirDistance;
        public float SprintAirStrafeInput => _sprintAirStrafeInput;
        public float LandingContactSpeed => _landingContactSpeed;
        public float LandingTime1 => _landingTime1;
        public float LandingTime2 => _landingTime2;
        public float JumpTime => _jumpTime;
        public float JumpGroundTime => _jumpGroundTime;
        public float FallTime => _fallTime;
        public float StuckVelocity => _stuckVelocity;
        public float ActorFriction => _actorFriction;
        public float GroundFriction => _groundFriction;
        public float CameraIntoCollisionShift => _cameraIntoCollisionShift;
        public float CameraHeightInterpolationSpeed => _cameraHeightInterpolationSpeed;
        public float ClimbPullMultiplier => _climbPullMultiplier;
        public float LadderLookAngleCosine => _ladderLookAngleCosine;
        public float LadderLookUpSine => _ladderLookUpSine;
        public float LadderJumpVelocity => _ladderJumpVelocity;
    }

    [Serializable]
    public sealed class StalkerConditionValues
    {
        [SerializeField] private float _jumpPower = 0.018f;
        [SerializeField] private float _standPower = -0.005f;
        [SerializeField] private float _walkPower = 0.0002f;
        [SerializeField] private float _jumpWeightPower = 0.2f;
        [SerializeField] private float _walkWeightPower = 0.0044f;
        [SerializeField] private float _overweightWalkK = 2.5f;
        [SerializeField] private float _overweightJumpK = 1.03f;
        [SerializeField] private float _accelK = 1.3f;
        [SerializeField] private float _sprintK = 4.0f;
        [SerializeField] private float _limpingHealthBegin = 0.09f;
        [SerializeField] private float _limpingHealthEnd = 0.12f;
        [SerializeField] private float _limpingPowerBegin = 0.18f;
        [SerializeField] private float _limpingPowerEnd = 0.29f;
        [SerializeField] private float _cantWalkPowerBegin = 0.0015f;
        [SerializeField] private float _cantWalkPowerEnd = 0.007f;
        [SerializeField] private float _cantSprintPowerBegin = 0.08f;
        [SerializeField] private float _cantSprintPowerEnd = 0.1f;
        [SerializeField] private float _maxPowerLeakSpeed = 0.000001f;
        [SerializeField] private float _maxWalkWeight = 70.0f;
        [SerializeField] private float _satietyPowerV = 0.00325f;
        [Tooltip("[alife] time_factor: condition deltas use game time.")]
        [SerializeField] private float _timeFactor = 6.0f;
        [SerializeField] private float _powerLossBias = 0.4f;
        [SerializeField] private float _powerLossFactor = 0.5f;
        [SerializeField] private bool _progressiveStaminaCost;

        public float JumpPower => _jumpPower;
        public float StandPower => _standPower;
        public float WalkPower => _walkPower;
        public float JumpWeightPower => _jumpWeightPower;
        public float WalkWeightPower => _walkWeightPower;
        public float OverweightWalkK => _overweightWalkK;
        public float OverweightJumpK => _overweightJumpK;
        public float AccelK => _accelK;
        public float SprintK => _sprintK;
        public float LimpingHealthBegin => _limpingHealthBegin;
        public float LimpingHealthEnd => _limpingHealthEnd;
        public float LimpingPowerBegin => _limpingPowerBegin;
        public float LimpingPowerEnd => _limpingPowerEnd;
        public float CantWalkPowerBegin => _cantWalkPowerBegin;
        public float CantWalkPowerEnd => _cantWalkPowerEnd;
        public float CantSprintPowerBegin => _cantSprintPowerBegin;
        public float CantSprintPowerEnd => _cantSprintPowerEnd;
        public float MaxPowerLeakSpeed => _maxPowerLeakSpeed;
        public float MaxWalkWeight => _maxWalkWeight;
        public float SatietyPowerV => _satietyPowerV;
        public float TimeFactor => _timeFactor;
        public float PowerLossBias => _powerLossBias;
        public float PowerLossFactor => _powerLossFactor;
        public bool ProgressiveStaminaCost => _progressiveStaminaCost;
    }

    [Serializable]
    public sealed class StalkerStaminaModValues
    {
        [SerializeField] private bool _enableStaminaSpeedModifier = true;
        [SerializeField] private float _staminaSpeedModifierModifier = 0.6f;
        [SerializeField] private float _powerSpeedMax = 0.55f;
        [SerializeField] private float _powerSpeedMin = -0.5f;
        [SerializeField] private bool _enableUphillSpeedModifier = true;
        [SerializeField] private float _uphillSpeedModifierModifier = 0.6f;
        [SerializeField] private float _uphillSpeedCoeff = 2.3f;
        [SerializeField] private float _uphillSpeedDownhillReduction = 0.45f;
        [SerializeField] private bool _enableUphillDrain = true;
        [SerializeField] private float _uphillDrainModifier = 0.6f;
        [SerializeField] private float _uphillDrainCoeff = 0.033f;
        [SerializeField] private float _uphillDrainDownhillReduction = 0.43f;
        [SerializeField] private bool _enableMaterialDrain = true;
        [SerializeField] private float _materialDrainModifier = 0.8f;
        [SerializeField] private float _updateInterval = 0.06f;
        [SerializeField] private float _speedCoefMin = 1.0f;
        [SerializeField] private float _speedCoefMax = 10.0f;

        public bool EnableStaminaSpeedModifier => _enableStaminaSpeedModifier;
        public float StaminaSpeedModifierModifier => _staminaSpeedModifierModifier;
        public float PowerSpeedMax => _powerSpeedMax;
        public float PowerSpeedMin => _powerSpeedMin;
        public bool EnableUphillSpeedModifier => _enableUphillSpeedModifier;
        public float UphillSpeedModifierModifier => _uphillSpeedModifierModifier;
        public float UphillSpeedCoeff => _uphillSpeedCoeff;
        public float UphillSpeedDownhillReduction => _uphillSpeedDownhillReduction;
        public bool EnableUphillDrain => _enableUphillDrain;
        public float UphillDrainModifier => _uphillDrainModifier;
        public float UphillDrainCoeff => _uphillDrainCoeff;
        public float UphillDrainDownhillReduction => _uphillDrainDownhillReduction;
        public bool EnableMaterialDrain => _enableMaterialDrain;
        public float MaterialDrainModifier => _materialDrainModifier;
        public float UpdateInterval => _updateInterval;
        public float SpeedCoefMin => _speedCoefMin;
        public float SpeedCoefMax => _speedCoefMax;
    }

    [Serializable]
    public sealed class StalkerBodyShift
    {
        [SerializeField] private string _motion;
        [SerializeField] private float _forward;
        [SerializeField] private float _right;

        public StalkerBodyShift(string motion, float forward, float right)
        {
            _motion = motion;
            _forward = forward;
            _right = right;
        }

        public string Motion => _motion;
        public float Forward => _forward;
        public float Right => _right;
    }

    [Serializable]
    public sealed class StalkerVisibleBodyValues
    {
        [SerializeField] private float _legsOffset = -0.65f;
        [SerializeField] private float _legsOffsetNonCombat = -0.5f;
        [SerializeField] private float _legsOffsetSide;
        [SerializeField] private bool _useSpecialNonCombatShift;
        [SerializeField] private bool _hideLegsLowCrouch = true;
        [SerializeField] private float _height;
        [SerializeField] private float _heightLadder = -0.1f;
        [SerializeField] private float _headToBodyDistance = 1.7f;
        [SerializeField] private float _fallShiftStand = 1.5f;
        [SerializeField] private float _fallShiftCrouch = 1.23f;
        [SerializeField] private float _fallShiftLowCrouch = 0.95f;
        [SerializeField] private float _lookDownThreshold = -0.65f;
        [SerializeField] private float _turnThreshold = 0.2f;
        [SerializeField] private float _turnThresholdLookDown = 0.08f;
        [SerializeField] private float _turnDuration = 0.5f;
        [SerializeField] private bool _enableIdleAnimations = true;
        [SerializeField] private float _idleCooldownMin = 6.0f;
        [SerializeField] private float _idleCooldownMax = 30.0f;
        [Tooltip("is_slowed_down: run coefficient threshold (jog_to_walk_thresold).")]
        [SerializeField] private float _slowedRunCoef = 1.4f;
        [Tooltip("is_slowed_down: sprint coefficient threshold (sprint_to_jog_thresold).")]
        [SerializeField] private float _slowedSprintKoef = 1.8f;
        [SerializeField] private List<StalkerBodyShift> _motionShifts = new List<StalkerBodyShift>();
        [SerializeField] private List<StalkerBodyShift> _lowCrouchShifts = new List<StalkerBodyShift>();

        public float LegsOffset => _legsOffset;
        public float LegsOffsetNonCombat => _legsOffsetNonCombat;
        public float LegsOffsetSide => _legsOffsetSide;
        public bool UseSpecialNonCombatShift => _useSpecialNonCombatShift;
        public bool HideLegsLowCrouch => _hideLegsLowCrouch;
        public float Height => _height;
        public float HeightLadder => _heightLadder;
        public float HeadToBodyDistance => _headToBodyDistance;
        public float FallShiftStand => _fallShiftStand;
        public float FallShiftCrouch => _fallShiftCrouch;
        public float FallShiftLowCrouch => _fallShiftLowCrouch;
        public float LookDownThreshold => _lookDownThreshold;
        public float TurnThreshold => _turnThreshold;
        public float TurnThresholdLookDown => _turnThresholdLookDown;
        public float TurnDuration => _turnDuration;
        public bool EnableIdleAnimations => _enableIdleAnimations;
        public float IdleCooldownMin => _idleCooldownMin;
        public float IdleCooldownMax => _idleCooldownMax;
        public float SlowedRunCoef => _slowedRunCoef;
        public float SlowedSprintKoef => _slowedSprintKoef;
        public IReadOnlyList<StalkerBodyShift> MotionShifts => _motionShifts;
        public IReadOnlyList<StalkerBodyShift> LowCrouchShifts => _lowCrouchShifts;

        public bool TryGetShift(IReadOnlyList<StalkerBodyShift> shifts, string motion, out StalkerBodyShift shift)
        {
            for (int i = 0; i < shifts.Count; i++)
            {
                if (shifts[i].Motion == motion)
                {
                    shift = shifts[i];
                    return true;
                }
            }

            shift = null;
            return false;
        }
    }

    [Serializable]
    public sealed class StalkerUnityAdaptation
    {
        [SerializeField] private float _skinWidth = 0.02f;
        [Tooltip("CharacterController step for small steps; higher ledges use the X-Ray climb (ValidateWalkOnMesh).")]
        [SerializeField] private float _stepOffset = 0.3f;
        [Tooltip("b_good_graund: ground normal.y > M_SQRT1_2.")]
        [SerializeField] private float _slopeLimit = 45.0f;
        [Tooltip("Uncontrolled ground velocity below this is zeroed (stands for CPHDisablingTranslational).")]
        [SerializeField] private float _sleepVelocity = 0.15f;
        [SerializeField] private bool _enableLedgeClimb = true;
        [Tooltip("ValidateWalkOnMesh: probe box above the feet, 0.05 + 2 * radius.")]
        [SerializeField] private float _ledgeProbeShift = 0.05f;
        [Tooltip("ValidateWalkOnMesh CHWON_ACCLEL_SHIFT.")]
        [SerializeField] private float _ledgeProbeForward = 0.4f;
        [Tooltip("ValidateWalkOnMesh CHWON_CALL_FB_HIGHT.")]
        [SerializeField] private float _ledgeForbidHeight = 1.5f;
        [Tooltip("CHWON climb force multiplier (control force * 4).")]
        [SerializeField] private float _ledgeClimbMultiplier = 4.0f;
        [SerializeField] private LayerMask _collisionMask = ~0;

        public float SkinWidth => _skinWidth;
        public float StepOffset => _stepOffset;
        public float SlopeLimit => _slopeLimit;
        public float SleepVelocity => _sleepVelocity;
        public bool EnableLedgeClimb => _enableLedgeClimb;
        public float LedgeProbeShift => _ledgeProbeShift;
        public float LedgeProbeForward => _ledgeProbeForward;
        public float LedgeForbidHeight => _ledgeForbidHeight;
        public float LedgeClimbMultiplier => _ledgeClimbMultiplier;
        public LayerMask CollisionMask => _collisionMask;
    }

    [Serializable]
    public sealed class StalkerCameraSettings
    {
        [Tooltip("Console 'fov': X-Ray passes it as the vertical field of view.")]
        [SerializeField] private float _fieldOfView = 95.0f;
        [Tooltip("Console 'hud_fov': hands and HUD items use a vertical field of view of hud_fov * 83 degrees, independent of 'fov'.")]
        [SerializeField] [Range(0.1f, 1.0f)] private float _hudFieldOfView = 0.45f;
        [Tooltip("r_dsgraph_render_hud: build_projection(deg2rad(psHUD_FOV * 83)).")]
        [SerializeField] private float _hudFieldOfViewScale = 83.0f;
        [SerializeField] private float _nearClip = 0.05f;
        [Tooltip("Console 'mouse_sens'.")]
        [SerializeField] private float _mouseSensitivity = 0.061f;
        [Tooltip("Console 'mouse_sens_vertical'.")]
        [SerializeField] private float _mouseSensitivityVertical = 1.0f;
        [SerializeField] private bool _invertMouse;
        [Tooltip("CActor::IR_OnMouseMove: radians per mouse count = sens / 50.")]
        [SerializeField] private float _mouseScaleDivider = 50.0f;
        [Tooltip("CActor::IR_OnMouseMove: vertical movement is scaled by 3/4.")]
        [SerializeField] private float _verticalScale = 0.75f;
        [Tooltip("[actor_firsteye_cam] lim_pitch, radians.")]
        [SerializeField] private Vector2 _pitchLimits = new Vector2(-1.5f, 1.5f);
        [Tooltip("[actor_ladder_cam] lim_yaw, radians relative to the ladder.")]
        [SerializeField] private Vector2 _ladderYawLimits = new Vector2(-1.0f, 1.0f);

        public float FieldOfView => _fieldOfView;
        public float HudFieldOfView => _hudFieldOfView;
        public float HudFieldOfViewScale => _hudFieldOfViewScale;
        public float NearClip => _nearClip;
        public float MouseSensitivity => _mouseSensitivity;
        public float MouseSensitivityVertical => _mouseSensitivityVertical;
        public bool InvertMouse => _invertMouse;
        public float MouseScaleDivider => _mouseScaleDivider;
        public float VerticalScale => _verticalScale;
        public Vector2 PitchLimits => _pitchLimits;
        public Vector2 LadderYawLimits => _ladderYawLimits;
    }

    [Serializable]
    public sealed class StalkerInputSettings
    {
        [Tooltip("Off: sprint while the key is held.")]
        [SerializeField] private bool _sprintToggle;
        [Tooltip("Console 'g_crouch_toggle'.")]
        [SerializeField] private bool _crouchToggle;
        [Tooltip("Console 'g_walk_toggle'.")]
        [SerializeField] private bool _walkToggle;

        public bool SprintToggle => _sprintToggle;
        public bool CrouchToggle => _crouchToggle;
        public bool WalkToggle => _walkToggle;
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/Player/Stalker Movement Profile", fileName = "StalkerMovementProfile")]
    public sealed class StalkerMovementProfile : ScriptableObject
    {
        [Header("Original X-Ray Values")]
        [SerializeField] private StalkerActorValues _actor = new StalkerActorValues();
        [SerializeField] private StalkerEngineValues _engine = new StalkerEngineValues();
        [SerializeField] private StalkerConditionValues _condition = new StalkerConditionValues();
        [SerializeField] private StalkerStaminaModValues _staminaMod = new StalkerStaminaModValues();
        [SerializeField] private StalkerVisibleBodyValues _visibleBody = new StalkerVisibleBodyValues();
        [SerializeField] private StalkerInputSettings _input = new StalkerInputSettings();
        [Header("Unity Adaptation")]
        [SerializeField] private StalkerUnityAdaptation _unity = new StalkerUnityAdaptation();
        [Header("Camera Settings")]
        [SerializeField] private StalkerCameraSettings _camera = new StalkerCameraSettings();
        [Header("Source")]
        [TextArea(3, 20)]
        [SerializeField] private string _sourceReport;

        public StalkerActorValues Actor => _actor;
        public StalkerEngineValues Engine => _engine;
        public StalkerConditionValues Condition => _condition;
        public StalkerStaminaModValues StaminaMod => _staminaMod;
        public StalkerVisibleBodyValues VisibleBody => _visibleBody;
        public StalkerInputSettings Input => _input;
        public StalkerUnityAdaptation Unity => _unity;
        public StalkerCameraSettings Camera => _camera;
        public string SourceReport => _sourceReport;
    }
}
