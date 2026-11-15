using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public static class AnomalyVisual09Events
    {
        private const int EVENT_CAPACITY = 262144;
        private const int FRAME_CAPACITY = 131072;
        private const int BINDING_CAPACITY = 65536;

        private static Event[] _events;
        private static Anchor[] _anchors;
        private static Component[] _components;
        private static int[] _owners;
        private static Dictionary<Component, int> _bindings;
        private static int _count;
        private static int _frameCount;
        private static int _bindingCount;
        private static int _lastFrame = -1;
        private static long _timestamp;
        private static int _unanchored;
        private static bool _overflow;

        public static bool Enabled { get; private set; }
        public static bool Configured { get; private set; }

        public static void Initialize(string[] args)
        {
            Enabled = Array.IndexOf(args, "-anomaly-events") >= 0;
            Configured = Enabled;
            if (!Enabled)
            {
                return;
            }
            if (AnomalyVisual08Target.Mode != "N")
            {
                throw new InvalidOperationException("Event recording requires Normal mode.");
            }
            _events = new Event[EVENT_CAPACITY];
            _anchors = new Anchor[FRAME_CAPACITY];
            _components = new Component[BINDING_CAPACITY];
            _owners = new int[BINDING_CAPACITY];
            _bindings = new Dictionary<Component, int>(BINDING_CAPACITY);
        }

        public static void Frame(long timestamp, int frame)
        {
            if (!Enabled)
            {
                return;
            }
            _lastFrame = frame;
            _timestamp = timestamp;
            if (_frameCount == FRAME_CAPACITY)
            {
                _overflow = true;
                return;
            }
            _anchors[_frameCount++] = new Anchor { Timestamp = timestamp, Frame = frame, SimulationTime = Time.timeAsDouble };
        }

        public static void RegisterTarget(AnomalyVisual08Target target)
        {
            if (!Enabled || target == null)
            {
                return;
            }
            int owner = _bindings.TryGetValue(target.transform, out int prior) ? _owners[prior] : 0;
            if (owner == 0)
            {
                if (target.Family != "Gazirovka")
                {
                    return;
                }
                owner = _bindingCount + 1;
            }
            Register(target.gameObject, owner);
        }

        public static void RegisterClone(Component source, GameObject clone)
        {
            if (!Enabled)
            {
                return;
            }
            int index = Resolve(source);
            if (index >= 0 && _owners[index] != 0)
            {
                Register(clone, _owners[index]);
            }
        }

        private static void Register(GameObject root, int owner)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                if (_bindings.TryGetValue(component, out int index))
                {
                    _owners[index] = owner;
                    continue;
                }
                if (_bindingCount == BINDING_CAPACITY)
                {
                    _overflow = true;
                    return;
                }
                _components[_bindingCount] = component;
                _owners[_bindingCount] = owner;
                _bindings.Add(component, _bindingCount++);
            }
        }

        private static int Resolve(Component component)
        {
            if (component == null)
            {
                return -1;
            }
            if (!_bindings.TryGetValue(component, out int index))
            {
                var target = component.GetComponentInParent<AnomalyVisual08Target>();
                if (target != null)
                {
                    RegisterTarget(target);
                }
                if (!_bindings.TryGetValue(component, out index))
                {
                    if (_bindingCount == BINDING_CAPACITY)
                    {
                        _overflow = true;
                        return -1;
                    }
                    index = _bindingCount++;
                    _components[index] = component;
                    _bindings.Add(component, index);
                }
            }
            return index;
        }

        public static void Record(Component source, int kind, int count = 0, double value = 0.0, Component subject = null)
        {
            if (!Enabled)
            {
                return;
            }
            int index = Resolve(source);
            if (index < 0 || _owners[index] == 0)
            {
                return;
            }
            if (_count == EVENT_CAPACITY)
            {
                _overflow = true;
                return;
            }
            int frame = Time.frameCount;
            long timestamp = frame == _lastFrame ? _timestamp : 0;
            _unanchored += timestamp == 0 ? 1 : 0;
            _events[_count++] = new Event { Timestamp = timestamp, Frame = frame, Kind = kind, Owner = _owners[index], Source = index + 1, Subject = subject != null ? Resolve(subject) + 1 : 0, Count = count, Value = value, SimulationTime = Time.timeAsDouble };
        }

        public static void LightAssignment(Light light, bool value)
        {
            if (Enabled && light != null && light.enabled != value)
            {
                Record(light, 8, value ? 1 : 0);
            }
        }

        public static void Save(string output)
        {
            if (!Enabled)
            {
                return;
            }
            Enabled = false;
            using (var writer = new StreamWriter(Path.Combine(output, "events.csv")))
            {
                writer.WriteLine("event,frame,update_timestamp,simulation_time,kind,owner,source,subject,count,value");
                for (int i = 0; i < _count; i++)
                {
                    Event item = _events[i];
                    writer.WriteLine(FormattableString.Invariant($"{i},{item.Frame},{item.Timestamp},{item.SimulationTime:R},{item.Kind},{item.Owner},{item.Source},{item.Subject},{item.Count},{item.Value:R}"));
                }
            }
            using (var writer = new StreamWriter(Path.Combine(output, "event-anchors.csv")))
            {
                writer.WriteLine("frame,update_timestamp,simulation_time");
                for (int i = 0; i < _frameCount; i++)
                {
                    Anchor item = _anchors[i];
                    writer.WriteLine(FormattableString.Invariant($"{item.Frame},{item.Timestamp},{item.SimulationTime:R}"));
                }
            }
            using (var writer = new StreamWriter(Path.Combine(output, "event-bindings.csv")))
            {
                writer.WriteLine("binding,owner,type,path");
                for (int i = 0; i < _bindingCount; i++)
                {
                    Component component = _components[i];
                    string path = component != null ? component.name : "destroyed";
                    string type = component != null ? component.GetType().FullName : "destroyed";
                    writer.WriteLine((i + 1).ToString(CultureInfo.InvariantCulture) + "," + _owners[i].ToString(CultureInfo.InvariantCulture) + "," + type + ",\"" + path.Replace("\"", "\"\"") + "\"");
                }
            }
            File.WriteAllText(Path.Combine(output, "event-coverage.json"), JsonUtility.ToJson(new Coverage { events = _count, anchors = _frameCount, bindings = _bindingCount, overflow = _overflow, unanchoredEvents = _unanchored, nativeUnityBirthsObserved = false, timestampIsUpdateAnchor = true }, true));
        }

        private struct Event
        {
            public long Timestamp;
            public int Frame;
            public int Kind;
            public int Owner;
            public int Source;
            public int Subject;
            public int Count;
            public double Value;
            public double SimulationTime;
        }

        private struct Anchor
        {
            public long Timestamp;
            public int Frame;
            public double SimulationTime;
        }

        [Serializable]
        private sealed class Coverage
        {
            public int events;
            public int anchors;
            public int bindings;
            public bool overflow;
            public int unanchoredEvents;
            public bool nativeUnityBirthsObserved;
            public bool timestampIsUpdateAnchor;
        }
    }
}
