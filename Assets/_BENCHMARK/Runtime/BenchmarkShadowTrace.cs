using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rvm.Benchmark
{
    [Serializable]
    public struct BenchmarkTraceContext
    {
        public string runId;
        public string phase;
        public int position;
        public bool transition;
    }

    public sealed class BenchmarkShadowTrace : IDisposable
    {
        private const int FRAME_CAPACITY = 32;
        private const int ASSERTION_CAPACITY = 8;
        private const int LIGHT_CAPACITY = 512;

        private readonly Camera _camera;
        private readonly Func<BenchmarkTraceContext> _context;
        private readonly LightCache[] _lights;
        private readonly LightCatalog[] _catalog;
        private readonly TraceFrame[] _frames = new TraceFrame[FRAME_CAPACITY];
        private readonly List<AssertionEvidence> _assertions = new List<AssertionEvidence>(ASSERTION_CAPACITY);
        private readonly LightState[] _previous;
        private readonly int _discoveredLights;

        private Type _observerType;
        private EventInfo _observerEvent;
        private PropertyInfo _observerEnabled;
        private Delegate _observerCallback;
        private IRequestBuffer _requestBuffer;
        private string _observerStatus = "unavailable: package observer type not loaded";
        private bool _observerWasEnabled;
        private int _cursor = -1;
        private int _frameCount;
        private int _assertionCount;
        private bool _disposed;

        public BenchmarkShadowTrace(Camera camera, Func<BenchmarkTraceContext> context)
        {
            if (camera == null || context == null)
            {
                throw new ArgumentNullException(camera == null ? nameof(camera) : nameof(context));
            }
            _camera = camera;
            _context = context;
            var found = new List<Light>();
            foreach (var root in camera.gameObject.scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<Light>(true));
            }
            found.Sort((first, second) => string.CompareOrdinal(first.GetEntityId().ToString(), second.GetEntityId().ToString()));
            _discoveredLights = found.Count;
            int count = Math.Min(found.Count, LIGHT_CAPACITY);
            _lights = new LightCache[count];
            _catalog = new LightCatalog[count];
            _previous = new LightState[count];
            for (int i = 0; i < count; i++)
            {
                _lights[i] = new LightCache(found[i]);
                _catalog[i] = new LightCatalog { slot = i, instanceId = found[i].GetEntityId().ToString(), path = HierarchyPath(found[i].transform), scene = found[i].gameObject.scene.path, additionalDataType = _lights[i].AdditionalType };
            }
            for (int i = 0; i < _frames.Length; i++)
            {
                _frames[i] = new TraceFrame { begin = new LightState[count], end = new LightState[count] };
            }
            ConnectObserver();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            Application.logMessageReceived += OnLog;
        }

        public void Capture(string stage)
        {
            if (_disposed || _camera == null)
            {
                return;
            }
            if (_cursor < 0 || _frames[_cursor].observedUnityFrame != Time.frameCount)
            {
                _cursor = (_cursor + 1) % FRAME_CAPACITY;
                _frameCount++;
                var fresh = _frames[_cursor];
                fresh.observedUnityFrame = Time.frameCount;
                fresh.context = _context();
                fresh.realtimeSeconds = Time.realtimeSinceStartupAsDouble;
                fresh.qpc = BenchmarkClock.GetTimestamp();
                fresh.cameraId = _camera.GetEntityId().ToString();
                fresh.cameraPosition = _camera.transform.position;
                fresh.cameraRotation = _camera.transform.rotation;
                fresh.beginSeen = false;
                fresh.endSeen = false;
                fresh.beginStage = null;
                fresh.endStage = null;
                fresh.lastStage = stage;
            }
            var frame = _frames[_cursor];
            bool beginning = stage == "beginCameraRendering" || !frame.beginSeen;
            var destination = beginning ? frame.begin : frame.end;
            for (int i = 0; i < _lights.Length; i++)
            {
                var state = _lights[i].Read();
                state.changed = _frameCount == 1 || !state.SameState(_previous[i]);
                destination[i] = state;
                _previous[i] = state;
            }
            if (beginning)
            {
                frame.beginSeen = true;
                frame.beginStage = stage;
                frame.beginDeveloperConsoleVisible = Debug.developerConsoleVisible;
            }
            else
            {
                frame.endSeen = true;
                frame.endStage = stage;
                frame.endDeveloperConsoleVisible = Debug.developerConsoleVisible;
            }
            frame.lastStage = stage;
        }

        public void Save(string path)
        {
            var document = new TraceDocument { utc = DateTime.UtcNow.ToString("O"), cameraName = _camera == null ? "destroyed" : _camera.name, sourceFramePolicy = "Unity frames are observation frames. Submit assertion may originate from an earlier request; no source frame is inferred. Light inventory is cached once and capped; no new-object discovery runs per frame. Catalog instanceId and cameraId preserve exact Unity EntityId strings; HDRP entityIndex/dataIndex are separate integer indices.", qpcFrequency = BenchmarkClock.Frequency, capturedFrames = _frameCount, discoveredLights = _discoveredLights, cachedLights = _lights.Length, assertionsObserved = _assertionCount, observerStatus = _observerStatus, catalog = _catalog, latestFrames = CopyHistory(), assertions = _assertions };
            BenchmarkOutput.WriteJson(path, document);
            _requestBuffer?.Save(Path.ChangeExtension(path, null) + ".requests.json");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            Application.logMessageReceived -= OnLog;
            if (_observerEvent != null && _observerCallback != null)
            {
                _observerEvent.RemoveEventHandler(null, _observerCallback);
                _observerEnabled.SetValue(null, _observerWasEnabled);
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
            {
                Capture("beginCameraRendering");
            }
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
            {
                Capture("endCameraRendering");
            }
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (message == null || message.IndexOf("Light index must be a valid shadow casting light", StringComparison.Ordinal) < 0)
            {
                return;
            }
            _assertionCount++;
            if (_assertions.Count == ASSERTION_CAPACITY)
            {
                return;
            }
            Capture("assert-observation");
            _requestBuffer?.FreezeAssertion(Time.frameCount);
            var states = new LightState[_lights.Length];
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = _lights[i].Read();
            }
            _assertions.Add(new AssertionEvidence { observedUnityFrame = Time.frameCount, sourceUnityFrame = -1, realtimeSeconds = Time.realtimeSinceStartupAsDouble, qpc = BenchmarkClock.GetTimestamp(), context = _context(), developerConsoleVisible = Debug.developerConsoleVisible, message = message, stack = stack, lightStates = states, history = CopyHistory() });
        }

        private TraceFrame[] CopyHistory()
        {
            int count = Math.Min(_frameCount, FRAME_CAPACITY);
            var result = new TraceFrame[count];
            int start = (_cursor - count + 1 + FRAME_CAPACITY) % FRAME_CAPACITY;
            for (int i = 0; i < count; i++)
            {
                var source = _frames[(start + i) % FRAME_CAPACITY];
                result[i] = new TraceFrame { observedUnityFrame = source.observedUnityFrame, context = source.context, realtimeSeconds = source.realtimeSeconds, qpc = source.qpc, cameraId = source.cameraId, cameraPosition = source.cameraPosition, cameraRotation = source.cameraRotation, beginSeen = source.beginSeen, endSeen = source.endSeen, beginStage = source.beginStage, endStage = source.endStage, beginDeveloperConsoleVisible = source.beginDeveloperConsoleVisible, endDeveloperConsoleVisible = source.endDeveloperConsoleVisible, lastStage = source.lastStage, begin = (LightState[])source.begin.Clone(), end = (LightState[])source.end.Clone() };
            }
            return result;
        }

        private void ConnectObserver()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                _observerType = assembly.GetType("UnityEngine.Rendering.HighDefinition.HDShadowRequestDiagnostics");
                if (_observerType != null)
                {
                    break;
                }
            }
            if (_observerType == null)
            {
                return;
            }
            _observerEvent = _observerType.GetEvent("Observed", BindingFlags.Public | BindingFlags.Static);
            _observerEnabled = _observerType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Static);
            if (_observerEvent == null || _observerEnabled == null || !_observerEvent.EventHandlerType.IsGenericType)
            {
                _observerStatus = "unavailable: package observer API does not match the expected contract";
                _observerEvent = null;
                return;
            }
            try
            {
                Type observation = _observerEvent.EventHandlerType.GetGenericArguments()[0];
                _requestBuffer = (IRequestBuffer)Activator.CreateInstance(typeof(RequestBuffer<>).MakeGenericType(observation));
                var method = typeof(BenchmarkShadowTrace).GetMethod(nameof(OnShadowRequest), BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(observation);
                _observerCallback = Delegate.CreateDelegate(_observerEvent.EventHandlerType, this, method);
                _observerWasEnabled = (bool)_observerEnabled.GetValue(null);
                _observerEvent.AddEventHandler(null, _observerCallback);
                _observerEnabled.SetValue(null, true);
                _observerStatus = "enabled: package request observations retained in a typed rolling buffer; mismatches retained separately";
            }
            catch (Exception exception)
            {
                _observerStatus = "unavailable: " + exception.GetType().Name + ": " + exception.Message;
                if (_observerCallback != null)
                {
                    _observerEvent.RemoveEventHandler(null, _observerCallback);
                    _observerEnabled.SetValue(null, _observerWasEnabled);
                }
                _observerEvent = null;
                _observerCallback = null;
                _requestBuffer = null;
            }
        }

        private void OnShadowRequest<T>(T observation)
        {
            ((RequestBuffer<T>)_requestBuffer).Add(observation);
        }

        private interface IRequestBuffer
        {
            void Save(string path);
            void FreezeAssertion(int observedUnityFrame);
        }

        private sealed class RequestBuffer<T> : IRequestBuffer
        {
            private const int REQUEST_CAPACITY = 2048;
            private const int MISMATCH_CAPACITY = 128;

            private readonly T[] _requests = new T[REQUEST_CAPACITY];
            private readonly List<T> _mismatches = new List<T>(MISMATCH_CAPACITY);
            private readonly List<RequestAssertion<T>> _assertions = new List<RequestAssertion<T>>(ASSERTION_CAPACITY);
            private readonly Func<T, bool> _mismatch;

            private int _count;
            private int _mismatchCount;

            public RequestBuffer()
            {
                var item = Expression.Parameter(typeof(T), "item");
                var mapped = Expression.Field(item, "mappedLightIndex");
                Expression mismatch = Expression.OrElse(Expression.Not(Expression.Field(item, "ownerSetMatches")), Expression.Not(Expression.Field(item, "mapFound")));
                mismatch = Expression.OrElse(mismatch, Expression.LessThan(mapped, Expression.Constant(0)));
                mismatch = Expression.OrElse(mismatch, Expression.GreaterThanOrEqual(mapped, Expression.Field(item, "cullingLightCount")));
                mismatch = Expression.OrElse(mismatch, Expression.NotEqual(mapped, Expression.Field(item, "expectedVisibleLightIndex")));
                mismatch = Expression.OrElse(mismatch, Expression.NotEqual(Expression.Field(item, "expectedLightInstanceId"), Expression.Field(item, "cullingLightInstanceId")));
                mismatch = Expression.OrElse(mismatch, Expression.NotEqual(Expression.Field(item, "expectedDataIndex"), Expression.Field(item, "requestDataIndex")));
                _mismatch = Expression.Lambda<Func<T, bool>>(mismatch, item).Compile();
            }

            public void Add(T observation)
            {
                _requests[_count % REQUEST_CAPACITY] = observation;
                _count++;
                if (_mismatch(observation))
                {
                    _mismatchCount++;
                    if (_mismatches.Count < MISMATCH_CAPACITY)
                    {
                        _mismatches.Add(observation);
                    }
                }
            }

            public void Save(string path)
            {
                BenchmarkOutput.WriteJson(path, new RequestDocument<T> { observations = _count, mismatches = _mismatchCount, recent = CopyRecent(), firstMismatches = _mismatches.ToArray(), assertionSnapshots = _assertions.ToArray() });
            }

            public void FreezeAssertion(int observedUnityFrame)
            {
                if (_assertions.Count < ASSERTION_CAPACITY)
                {
                    _assertions.Add(new RequestAssertion<T> { observedUnityFrame = observedUnityFrame, sourceUnityFrame = -1, requestsObserved = _count, recent = CopyRecent() });
                }
            }

            private T[] CopyRecent()
            {
                int count = Math.Min(_count, REQUEST_CAPACITY);
                var recent = new T[count];
                int first = Math.Max(0, _count - REQUEST_CAPACITY);
                for (int i = 0; i < count; i++)
                {
                    recent[i] = _requests[(first + i) % REQUEST_CAPACITY];
                }
                return recent;
            }
        }

        [Serializable]
        private sealed class RequestAssertion<T>
        {
            public int observedUnityFrame;
            public int sourceUnityFrame;
            public int requestsObserved;
            public T[] recent;
        }

        [Serializable]
        private sealed class RequestDocument<T>
        {
            public int version = 1;
            public string sourceFramePolicy = "Frames identify package request observation/renderer-list creation, not a proven source frame for a later Submit assertion. Mismatch is diagnostic consistency evidence, not an automatic causal conclusion.";
            public int observations;
            public int mismatches;
            public T[] recent;
            public T[] firstMismatches;
            public RequestAssertion<T>[] assertionSnapshots;
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private sealed class LightCache
        {
            private const BindingFlags MEMBERS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            private readonly Light _light;
            private readonly Component _additional;
            private readonly FieldInfo _entity;
            private readonly FieldInfo _entityIndex;
            private readonly FieldInfo _shadowIndex;
            private readonly PropertyInfo _updateMode;
            private readonly FieldInfo _databaseInstance;
            private readonly MethodInfo _dataIndex;
            private readonly object[] _lightArgument;

            public string AdditionalType => _additional == null ? "unavailable" : _additional.GetType().FullName;

            public LightCache(Light light)
            {
                _light = light;
                _lightArgument = new object[] { light };
                foreach (var component in light.GetComponents<Component>())
                {
                    if (component != null && component.GetType().FullName == "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData")
                    {
                        _additional = component;
                        break;
                    }
                }
                if (_additional == null)
                {
                    return;
                }
                var type = _additional.GetType();
                _entity = type.GetField("lightEntity", MEMBERS);
                _entityIndex = _entity?.FieldType.GetField("entityIndex", MEMBERS);
                _shadowIndex = type.GetField("shadowIndex", MEMBERS);
                _updateMode = type.GetProperty("shadowUpdateMode", MEMBERS);
                var database = type.Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDLightRenderDatabase");
                _databaseInstance = database?.GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic);
                _dataIndex = database?.GetMethod("FindEntityDataIndex", MEMBERS, null, new[] { typeof(Light).MakeByRefType() }, null);
            }

            public LightState Read()
            {
                var result = new LightState { exists = _light != null, entityIndex = -1, dataIndex = -1, shadowIndex = -1, updateMode = "unavailable" };
                if (_light == null)
                {
                    return result;
                }
                result.enabled = _light.enabled;
                result.active = _light.gameObject.activeInHierarchy;
                result.type = _light.type.ToString();
                result.shadows = _light.shadows.ToString();
                result.range = _light.range;
                result.intensity = _light.intensity;
                result.color = _light.color;
                result.position = _light.transform.position;
                result.rotation = _light.transform.rotation;
                result.cullingMask = _light.cullingMask;
                result.shadowStrength = _light.shadowStrength;
                if (_additional == null)
                {
                    return result;
                }
                try
                {
                    var entity = _entity?.GetValue(_additional);
                    result.entityIndex = entity != null && _entityIndex != null ? (int)_entityIndex.GetValue(entity) : -1;
                    result.shadowIndex = _shadowIndex == null ? -1 : (int)_shadowIndex.GetValue(_additional);
                    result.updateMode = _updateMode?.GetValue(_additional)?.ToString() ?? "unavailable";
                    object database = _databaseInstance?.GetValue(null);
                    result.dataIndex = database != null && _dataIndex != null ? (int)_dataIndex.Invoke(database, _lightArgument) : -1;
                }
                catch (Exception exception)
                {
                    result.readFailure = exception.GetType().Name;
                }
                return result;
            }
        }

        [Serializable]
        private struct LightState
        {
            public bool exists;
            public bool enabled;
            public bool active;
            public bool changed;
            public string type;
            public string shadows;
            public float range;
            public float intensity;
            public Color color;
            public Vector3 position;
            public Quaternion rotation;
            public int cullingMask;
            public float shadowStrength;
            public int entityIndex;
            public int dataIndex;
            public int shadowIndex;
            public string updateMode;
            public string readFailure;

            public bool SameState(LightState other)
            {
                return exists == other.exists && enabled == other.enabled && active == other.active && type == other.type && shadows == other.shadows && range == other.range && intensity == other.intensity && color == other.color && position == other.position && rotation == other.rotation && cullingMask == other.cullingMask && shadowStrength == other.shadowStrength && entityIndex == other.entityIndex && dataIndex == other.dataIndex && shadowIndex == other.shadowIndex && updateMode == other.updateMode && readFailure == other.readFailure;
            }
        }

        [Serializable]
        private sealed class LightCatalog
        {
            public int slot;
            public string instanceId;
            public string path;
            public string scene;
            public string additionalDataType;
        }

        [Serializable]
        private sealed class TraceFrame
        {
            public int observedUnityFrame;
            public BenchmarkTraceContext context;
            public double realtimeSeconds;
            public long qpc;
            public string cameraId;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public bool beginSeen;
            public bool endSeen;
            public string beginStage;
            public string endStage;
            public bool beginDeveloperConsoleVisible;
            public bool endDeveloperConsoleVisible;
            public string lastStage;
            public LightState[] begin;
            public LightState[] end;
        }

        [Serializable]
        private sealed class AssertionEvidence
        {
            public int observedUnityFrame;
            public int sourceUnityFrame;
            public double realtimeSeconds;
            public long qpc;
            public BenchmarkTraceContext context;
            public bool developerConsoleVisible;
            public string message;
            public string stack;
            public LightState[] lightStates;
            public TraceFrame[] history;
        }

        [Serializable]
        private sealed class TraceDocument
        {
            public int version = 1;
            public string utc;
            public string cameraName;
            public string sourceFramePolicy;
            public long qpcFrequency;
            public int capturedFrames;
            public int discoveredLights;
            public int cachedLights;
            public int assertionsObserved;
            public string observerStatus;
            public LightCatalog[] catalog;
            public TraceFrame[] latestFrames;
            public List<AssertionEvidence> assertions;
        }
    }
}
