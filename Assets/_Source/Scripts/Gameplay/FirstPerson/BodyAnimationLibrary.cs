using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson
{
    public enum BodyAnimationTable
    {
        NonCombat,
        SlowedDown,
        Basic
    }

    [Serializable]
    public sealed class BodyAnimationBinding
    {
        [SerializeField] private int _flags;
        [SerializeField] private string _motion;
        [SerializeField] private AnimationClip _clip;
        [SerializeField] private string _sourceFile;

        public BodyAnimationBinding(int flags, string motion, AnimationClip clip, string sourceFile)
        {
            _flags = flags;
            _motion = motion;
            _clip = clip;
            _sourceFile = sourceFile;
        }

        public int Flags => _flags;
        public string Motion => _motion;
        public AnimationClip Clip => _clip;
        public string SourceFile => _sourceFile;
    }

    [Serializable]
    public sealed class BodyIdleAnimation
    {
        [SerializeField] private BodyAnimationBinding _binding;
        [SerializeField] private int _durationMilliseconds;

        public BodyIdleAnimation(BodyAnimationBinding binding, int durationMilliseconds)
        {
            _binding = binding;
            _durationMilliseconds = durationMilliseconds;
        }

        public BodyAnimationBinding Binding => _binding;
        public int DurationMilliseconds => _durationMilliseconds;
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/First Person/Body Animation Library", fileName = "BodyAnimationLibrary")]
    public sealed class BodyAnimationLibrary : ScriptableObject
    {
        [SerializeField] private string _sourceScript;
        [SerializeField] private List<XRayMotionLibrary> _motionLibraries = new List<XRayMotionLibrary>();
        [SerializeField] private List<BodyAnimationBinding> _nonCombat = new List<BodyAnimationBinding>();
        [SerializeField] private List<BodyAnimationBinding> _slowedDown = new List<BodyAnimationBinding>();
        [SerializeField] private List<BodyAnimationBinding> _basic = new List<BodyAnimationBinding>();
        [SerializeField] private BodyAnimationBinding _basicFallback;
        [SerializeField] private BodyAnimationBinding _ladderIdle;
        [SerializeField] private BodyAnimationBinding _ladderUp;
        [SerializeField] private BodyAnimationBinding _ladderDown;
        [SerializeField] private BodyAnimationBinding _turnLeft;
        [SerializeField] private BodyAnimationBinding _turnRight;
        [SerializeField] private List<BodyIdleAnimation> _idles = new List<BodyIdleAnimation>();
        [Tooltip("Flags subtracted from the actor moving state before the table lookup (swm_legs DISABLED_FLAGS).")]
        [SerializeField] private int _disabledFlags;
        [SerializeField] private bool _useCombatState;
        [SerializeField] private bool _useSlowDownState;
        [SerializeField] private bool _enableIdleAnimations;

        public string SourceScript => _sourceScript;
        public IReadOnlyList<XRayMotionLibrary> MotionLibraries => _motionLibraries;
        public IReadOnlyList<BodyAnimationBinding> NonCombat => _nonCombat;
        public IReadOnlyList<BodyAnimationBinding> SlowedDown => _slowedDown;
        public IReadOnlyList<BodyAnimationBinding> Basic => _basic;
        public BodyAnimationBinding BasicFallback => _basicFallback;
        public BodyAnimationBinding LadderIdle => _ladderIdle;
        public BodyAnimationBinding LadderUp => _ladderUp;
        public BodyAnimationBinding LadderDown => _ladderDown;
        public BodyAnimationBinding TurnLeft => _turnLeft;
        public BodyAnimationBinding TurnRight => _turnRight;
        public IReadOnlyList<BodyIdleAnimation> Idles => _idles;
        public int DisabledFlags => _disabledFlags;
        public bool UseCombatState => _useCombatState;
        public bool UseSlowDownState => _useSlowDownState;
        public bool EnableIdleAnimations => _enableIdleAnimations;

        public IReadOnlyList<BodyAnimationBinding> GetTable(BodyAnimationTable table)
        {
            switch (table)
            {
                case BodyAnimationTable.NonCombat:
                    return _nonCombat;
                case BodyAnimationTable.SlowedDown:
                    return _slowedDown;
                default:
                    return _basic;
            }
        }

        public bool TryGetBinding(BodyAnimationTable table, int flags, out BodyAnimationBinding binding)
        {
            IReadOnlyList<BodyAnimationBinding> bindings = GetTable(table);
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Flags == flags)
                {
                    binding = bindings[i];
                    return true;
                }
            }

            binding = null;
            return false;
        }

        public BodyAnimationBinding Resolve(int movingState, bool isCalm, bool isSlowedDown)
        {
            int flags = movingState & ~_disabledFlags;
            if (_useCombatState && isCalm && TryGetBinding(BodyAnimationTable.NonCombat, flags, out BodyAnimationBinding nonCombat))
            {
                return nonCombat;
            }

            if (_useSlowDownState && isSlowedDown && TryGetBinding(BodyAnimationTable.SlowedDown, flags, out BodyAnimationBinding slowedDown))
            {
                return slowedDown;
            }

            return TryGetBinding(BodyAnimationTable.Basic, flags, out BodyAnimationBinding basic) ? basic : _basicFallback;
        }

        public void SetImportData(string sourceScript, List<XRayMotionLibrary> motionLibraries, List<BodyAnimationBinding> nonCombat, List<BodyAnimationBinding> slowedDown, List<BodyAnimationBinding> basic, BodyAnimationBinding basicFallback, BodyAnimationBinding ladderIdle, BodyAnimationBinding ladderUp, BodyAnimationBinding ladderDown, BodyAnimationBinding turnLeft, BodyAnimationBinding turnRight, List<BodyIdleAnimation> idles, int disabledFlags, bool useCombatState, bool useSlowDownState, bool enableIdleAnimations)
        {
            _sourceScript = sourceScript;
            _motionLibraries = motionLibraries;
            _nonCombat = nonCombat;
            _slowedDown = slowedDown;
            _basic = basic;
            _basicFallback = basicFallback;
            _ladderIdle = ladderIdle;
            _ladderUp = ladderUp;
            _ladderDown = ladderDown;
            _turnLeft = turnLeft;
            _turnRight = turnRight;
            _idles = idles;
            _disabledFlags = disabledFlags;
            _useCombatState = useCombatState;
            _useSlowDownState = useSlowDownState;
            _enableIdleAnimations = enableIdleAnimations;
        }
    }
}
