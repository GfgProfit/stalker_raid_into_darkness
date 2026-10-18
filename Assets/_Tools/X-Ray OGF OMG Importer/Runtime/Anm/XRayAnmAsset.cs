using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Core;
using XRay.Importer.Core.Anm;

namespace XRay.Importer
{
    [Serializable]
    public sealed class XRayAnmKeyData
    {
        [SerializeField] private float _value;
        [SerializeField] private float _time;
        [SerializeField] private int _shape;
        [SerializeField] private float _tension;
        [SerializeField] private float _continuity;
        [SerializeField] private float _bias;
        [SerializeField] private float _param0;
        [SerializeField] private float _param1;
        [SerializeField] private float _param2;
        [SerializeField] private float _param3;

        public XRayAnmKeyData(XRayAnmKey key)
        {
            _value = (float)key.Value;
            _time = (float)key.Time;
            _shape = (int)key.Shape;
            _tension = (float)key.Tension;
            _continuity = (float)key.Continuity;
            _bias = (float)key.Bias;
            _param0 = (float)key.Param0;
            _param1 = (float)key.Param1;
            _param2 = (float)key.Param2;
            _param3 = (float)key.Param3;
        }

        public XRayAnmKey ToKey()
        {
            return new XRayAnmKey { Value = _value, Time = _time, Shape = (XRayAnmShape)_shape, Tension = _tension, Continuity = _continuity, Bias = _bias, Param0 = _param0, Param1 = _param1, Param2 = _param2, Param3 = _param3 };
        }
    }

    [Serializable]
    public sealed class XRayAnmChannelData
    {
        [SerializeField] private int _preBehavior;
        [SerializeField] private int _postBehavior;
        [SerializeField] private int _keyCount;
        [SerializeField] private string _shapes;
        [SerializeField] private List<XRayAnmKeyData> _keys = new List<XRayAnmKeyData>();

        public XRayAnmChannelData(XRayAnmEnvelope envelope, bool preserveKeys)
        {
            _preBehavior = (int)envelope.PreBehavior;
            _postBehavior = (int)envelope.PostBehavior;
            _keyCount = envelope.Keys.Count;
            var shapes = new SortedSet<string>();
            foreach (XRayAnmKey key in envelope.Keys)
            {
                shapes.Add(key.Shape.ToString());
                if (preserveKeys)
                {
                    _keys.Add(new XRayAnmKeyData(key));
                }
            }

            _shapes = string.Join(", ", shapes);
        }

        public XRayAnmBehavior PreBehavior => (XRayAnmBehavior)_preBehavior;
        public XRayAnmBehavior PostBehavior => (XRayAnmBehavior)_postBehavior;
        public int KeyCount => _keyCount;
        public string Shapes => _shapes;
        public bool HasKeys => _keys.Count == _keyCount;

        public XRayAnmEnvelope ToEnvelope()
        {
            var envelope = new XRayAnmEnvelope { PreBehavior = PreBehavior, PostBehavior = PostBehavior };
            foreach (XRayAnmKeyData key in _keys)
            {
                envelope.Keys.Add(key.ToKey());
            }

            return envelope;
        }
    }

    public sealed class XRayAnmAsset : ScriptableObject
    {
        [SerializeField] private string _sourceFile;
        [SerializeField] private string _motionName;
        [SerializeField] private int _version;
        [SerializeField] private float _fps;
        [SerializeField] private int _frameStart;
        [SerializeField] private int _frameEnd;
        [SerializeField] private float _scale = 1.0f;
        [SerializeField] private XRayAnmSemantics _semantics = XRayAnmSemantics.Engine;
        [SerializeField] private AnimationClip _clip;
        [SerializeField] private List<XRayAnmChannelData> _channels = new List<XRayAnmChannelData>();
        [SerializeField] private List<string> _warnings = new List<string>();

        private XRayAnmMotion _motion;

        public string SourceFile => _sourceFile;
        public string MotionName => _motionName;
        public int Version => _version;
        public float Fps => _fps;
        public int FrameStart => _frameStart;
        public int FrameEnd => _frameEnd;
        public float Scale => _scale;
        public XRayAnmSemantics Semantics => _semantics;
        public AnimationClip Clip => _clip;
        public IReadOnlyList<XRayAnmChannelData> Channels => _channels;
        public IReadOnlyList<string> Warnings => _warnings;
        public float Duration => _fps > 0.0f ? (_frameEnd - _frameStart) / _fps : 0.0f;
        public float StartTime => _fps > 0.0f ? _frameStart / _fps : 0.0f;
        public bool HasCurves => _channels.Count == XRayAnmFormat.CHANNEL_COUNT && _channels.TrueForAll(channel => channel.HasKeys);

        public void SetImportData(string sourceFile, XRayAnmMotion motion, float scale, XRayAnmSemantics semantics, bool preserveCurves, AnimationClip clip)
        {
            _sourceFile = sourceFile;
            _motionName = motion.Name;
            _version = motion.Version;
            _fps = (float)motion.Fps;
            _frameStart = motion.FrameStart;
            _frameEnd = motion.FrameEnd;
            _scale = scale;
            _semantics = semantics;
            _clip = clip;
            _channels = new List<XRayAnmChannelData>();
            foreach (XRayAnmEnvelope envelope in motion.FileEnvelopes)
            {
                _channels.Add(new XRayAnmChannelData(envelope, preserveCurves));
            }

            _warnings = new List<string>(motion.Warnings);
            _motion = null;
        }

        public XRayAnmMotion GetMotion()
        {
            if (_motion != null || !HasCurves)
            {
                return _motion;
            }

            var motion = new XRayAnmMotion { Name = _motionName, Version = (ushort)_version, Fps = _fps, FrameStart = _frameStart, FrameEnd = _frameEnd };
            for (int i = 0; i < _channels.Count; i++)
            {
                motion.FileEnvelopes[i] = _channels[i].ToEnvelope();
            }

            _motion = motion;
            return _motion;
        }

        public bool Evaluate(float clipTime, out Vector3 position, out Quaternion rotation)
        {
            XRayAnmMotion motion = GetMotion();
            if (motion == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            motion.Evaluate(motion.StartTime + clipTime, _semantics, out XVector3 nativePosition, out XVector3 rotationHpb);
            XQuaternion nativeRotation = XRayAnmTransform.ToRotation(rotationHpb);
            position = new Vector3((float)nativePosition.X, (float)nativePosition.Y, (float)nativePosition.Z) * _scale;
            rotation = new Quaternion((float)nativeRotation.X, (float)nativeRotation.Y, (float)nativeRotation.Z, (float)nativeRotation.W);
            return true;
        }

        private void OnValidate()
        {
            _motion = null;
        }
    }
}
