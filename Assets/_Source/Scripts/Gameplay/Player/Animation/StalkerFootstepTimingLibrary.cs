using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public enum StalkerFootSide
    {
        Left,
        Right
    }

    [Serializable]
    public sealed class StalkerFootContact
    {
        [SerializeField] private float _phase;
        [SerializeField] private StalkerFootSide _side;

        public StalkerFootContact(float phase, StalkerFootSide side)
        {
            _phase = phase;
            _side = side;
        }

        public float Phase => _phase;
        public StalkerFootSide Side => _side;
    }

    [Serializable]
    public sealed class StalkerBodyMotionSteps
    {
        [SerializeField] private string _motion;
        [SerializeField] private AnimationClip _clip;
        [SerializeField] private float _length;
        [SerializeField] private List<StalkerFootContact> _contacts = new List<StalkerFootContact>();

        public StalkerBodyMotionSteps(string motion, AnimationClip clip, float length, List<StalkerFootContact> contacts)
        {
            _motion = motion;
            _clip = clip;
            _length = length;
            _contacts = contacts;
        }

        public string Motion => _motion;
        public AnimationClip Clip => _clip;
        public float Length => _length;
        public IReadOnlyList<StalkerFootContact> Contacts => _contacts;
    }

    [Serializable]
    public sealed class StalkerLegsMotionSteps
    {
        [SerializeField] private string _motion;
        [SerializeField] private float _cycleTime;
        [SerializeField] private int _cycles = 1;
        [SerializeField] private float _leftTime;
        [SerializeField] private float _leftPower;
        [SerializeField] private float _rightTime;
        [SerializeField] private float _rightPower;
        [SerializeField] private bool _stopAtEnd;

        public StalkerLegsMotionSteps(string motion, float cycleTime, int cycles, float leftTime, float leftPower, float rightTime, float rightPower, bool stopAtEnd)
        {
            _stopAtEnd = stopAtEnd;
            _motion = motion;
            _cycleTime = cycleTime;
            _cycles = cycles;
            _leftTime = leftTime;
            _leftPower = leftPower;
            _rightTime = rightTime;
            _rightPower = rightPower;
        }

        public string Motion => _motion;
        public float CycleTime => _cycleTime;
        public int Cycles => _cycles;
        public float LeftTime => _leftTime;
        public float LeftPower => _leftPower;
        public float RightTime => _rightTime;
        public float RightPower => _rightPower;
        public bool StopAtEnd => _stopAtEnd;

        public float GetTime(StalkerFootSide side)
        {
            return side == StalkerFootSide.Left ? _leftTime : _rightTime;
        }

        public float GetPower(StalkerFootSide side)
        {
            return side == StalkerFootSide.Left ? _leftPower : _rightPower;
        }
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/Player/Footstep Timing Library", fileName = "StalkerFootstepTiming")]
    public sealed class StalkerFootstepTimingLibrary : ScriptableObject
    {
        [SerializeField] private List<StalkerBodyMotionSteps> _bodyMotions = new List<StalkerBodyMotionSteps>();
        [SerializeField] private List<StalkerLegsMotionSteps> _legsMotions = new List<StalkerLegsMotionSteps>();
        [TextArea(3, 20)]
        [SerializeField] private string _sourceReport;

        private Dictionary<AnimationClip, StalkerBodyMotionSteps> _bodyByClip;
        private Dictionary<string, StalkerLegsMotionSteps> _legsByName;

        public IReadOnlyList<StalkerBodyMotionSteps> BodyMotions => _bodyMotions;
        public IReadOnlyList<StalkerLegsMotionSteps> LegsMotions => _legsMotions;
        public string SourceReport => _sourceReport;

        public bool TryGetBody(AnimationClip clip, out StalkerBodyMotionSteps steps)
        {
            if (_bodyByClip == null)
            {
                _bodyByClip = new Dictionary<AnimationClip, StalkerBodyMotionSteps>();
                foreach (StalkerBodyMotionSteps item in _bodyMotions)
                {
                    if (item.Clip != null && !_bodyByClip.ContainsKey(item.Clip))
                    {
                        _bodyByClip.Add(item.Clip, item);
                    }
                }
            }

            steps = null;
            return clip != null && _bodyByClip.TryGetValue(clip, out steps) && steps.Contacts.Count > 0;
        }

        public bool TryGetLegs(string motion, out StalkerLegsMotionSteps steps)
        {
            if (_legsByName == null)
            {
                _legsByName = new Dictionary<string, StalkerLegsMotionSteps>(StringComparer.Ordinal);
                foreach (StalkerLegsMotionSteps item in _legsMotions)
                {
                    _legsByName[item.Motion] = item;
                }
            }

            steps = null;
            return motion != null && _legsByName.TryGetValue(motion, out steps);
        }

        public void SetImportData(List<StalkerBodyMotionSteps> bodyMotions, List<StalkerLegsMotionSteps> legsMotions, string sourceReport)
        {
            _bodyMotions = bodyMotions;
            _legsMotions = legsMotions;
            _sourceReport = sourceReport;
            _bodyByClip = null;
            _legsByName = null;
        }

        private void OnValidate()
        {
            _bodyByClip = null;
            _legsByName = null;
        }
    }
}
