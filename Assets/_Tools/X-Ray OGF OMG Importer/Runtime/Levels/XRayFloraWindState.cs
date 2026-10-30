using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public enum XRayFloraWindProfile
    {
        Disabled,
        AnomalySsfx,
        TrueStalkerXRay
    }

    [Serializable]
    public struct XRayFloraWindWeather
    {
        [SerializeField] private float _directionRadians;
        [SerializeField] private float _velocity;
        [SerializeField] private float _treeAmplitudeIntensity;

        public XRayFloraWindWeather(float directionRadians, float velocity, float treeAmplitudeIntensity)
        {
            _directionRadians = directionRadians;
            _velocity = velocity;
            _treeAmplitudeIntensity = treeAmplitudeIntensity;
        }

        public float DirectionRadians => _directionRadians;
        public float Velocity => _velocity;
        public float TreeAmplitudeIntensity => _treeAmplitudeIntensity;
    }

    public sealed class XRayFloraWindState
    {
        public const string ACTIVE_PROPERTY = "_MonolithWindActive";
        public const string PARAMS_PROPERTY = "_MonolithWindParams";
        public const string TREES_SETUP_PROPERTY = "_MonolithWindTreesSetup";
        public const string ANIM_PROPERTY = "_MonolithWindAnim";
        public const string ANIM_PREV_PROPERTY = "_MonolithWindAnimPrev";
        public const string TREE_WIND_PROPERTY = "_MonolithTreeWind";
        public const string TREE_WAVE_PROPERTY = "_MonolithTreeWave";
        public const string TREE_WIND_PREV_PROPERTY = "_MonolithTreeWindPrev";
        public const string TREE_WAVE_PREV_PROPERTY = "_MonolithTreeWavePrev";
        public const float ENGINE_HALF_PI = 1.5707964f;
        public const float ENGINE_MAX_DELTA = 0.1f;
        public const float MIN_ANIM_VELOCITY = 200.0f;

        private static readonly int ACTIVE_ID = Shader.PropertyToID(ACTIVE_PROPERTY);
        private static readonly int PARAMS_ID = Shader.PropertyToID(PARAMS_PROPERTY);
        private static readonly int TREES_SETUP_ID = Shader.PropertyToID(TREES_SETUP_PROPERTY);
        private static readonly int ANIM_ID = Shader.PropertyToID(ANIM_PROPERTY);
        private static readonly int ANIM_PREV_ID = Shader.PropertyToID(ANIM_PREV_PROPERTY);
        private static readonly int TREE_WIND_ID = Shader.PropertyToID(TREE_WIND_PROPERTY);
        private static readonly int TREE_WAVE_ID = Shader.PropertyToID(TREE_WAVE_PROPERTY);
        private static readonly int TREE_WIND_PREV_ID = Shader.PropertyToID(TREE_WIND_PREV_PROPERTY);
        private static readonly int TREE_WAVE_PREV_ID = Shader.PropertyToID(TREE_WAVE_PREV_PROPERTY);

        public XRayFloraWindProfile Profile { get; set; }
        public Vector4 TreesSetup { get; set; } = new Vector4(11.0f, 0.15f, 0.5f, 0.15f);
        public float TreeRotationPeriod { get; set; } = 10.0f;
        public float TreeWaveSpeed { get; set; } = 1.0f;
        public float TreeAmplitude { get; set; } = 0.005f;
        public Vector3 TreeWaveScale { get; set; } = new Vector3(0.1f, 0.01f, 0.11f);
        public bool TreeAmplitudeFromWeather { get; set; }
        public XRayFloraWindWeather Weather { get; private set; }
        public Vector4 Anim { get; private set; }
        public Vector4 AnimPrev { get; private set; }
        public Vector4 TreeWind { get; private set; }
        public Vector4 TreeWave { get; private set; }
        public Vector4 TreeWindPrev { get; private set; }
        public Vector4 TreeWavePrev { get; private set; }
        public double Time { get; private set; }
        public bool Initialized { get; private set; }
        public int Ticks { get; private set; }

        public static float EngineDelta(float deltaTime, bool paused)
        {
            if (paused || deltaTime <= 0.0f)
            {
                return 0.0f;
            }

            return Mathf.Min(deltaTime, ENGINE_MAX_DELTA);
        }

        public void Reset(XRayFloraWindWeather weather)
        {
            Weather = weather;
            Anim = Vector4.zero;
            Time = 0.0;
            CalculateTree();
            AnimPrev = Anim;
            TreeWindPrev = TreeWind;
            TreeWavePrev = TreeWave;
            Ticks = 0;
            Initialized = true;
        }

        public void Reinitialize()
        {
            AnimPrev = Anim;
            TreeWindPrev = TreeWind;
            TreeWavePrev = TreeWave;
        }

        public void Advance(float deltaTime, XRayFloraWindWeather weather)
        {
            if (!Initialized)
            {
                Reset(weather);
                return;
            }

            Weather = weather;
            AnimPrev = Anim;
            TreeWindPrev = TreeWind;
            TreeWavePrev = TreeWave;
            float velocity = Mathf.Max(Mathf.Max(weather.Velocity, TreesSetup.w * 1000.0f), MIN_ANIM_VELOCITY) * 0.001f;
            float direction = -weather.DirectionRadians + ENGINE_HALF_PI;
            Vector4 anim = Anim;
            anim.x += velocity * Mathf.Cos(direction) * deltaTime;
            anim.y += velocity * Mathf.Sin(direction) * deltaTime;
            anim.z += Mathf.Clamp(velocity * 1.33f, 0.0f, 1.0f) * deltaTime;
            anim.w += 1.0f * deltaTime;
            Anim = anim;
            Time += deltaTime;
            CalculateTree();
            Ticks++;
        }

        public Vector4 WindParams()
        {
            return new Vector4(Weather.DirectionRadians, Weather.Velocity, 0.0f, 0.0f);
        }

        public float MaxHorizontalDisplacement(float treeHeight, float maxRigidity)
        {
            float height = Mathf.Max(0.0f, treeHeight);
            if (Profile == XRayFloraWindProfile.AnomalySsfx)
            {
                float speed = Mathf.Max(TreesSetup.w, Mathf.Clamp01(Weather.Velocity * 0.001f));
                float sqrtSpeed = Mathf.Clamp01(Mathf.Sqrt(speed * 1.66f));
                float bendSpeed = Mathf.Clamp01(sqrtSpeed * 1.5f);
                float trunk = bendSpeed * 0.006f * height * height * Mathf.Abs(1.5f * TreesSetup.z) * bendSpeed;
                float branch = (2.5f * Mathf.Sqrt(2.0f) + 1.0f) * speed;
                return trunk + branch;
            }

            if (Profile == XRayFloraWindProfile.TrueStalkerXRay)
            {
                float amplitude = Mathf.Max(new Vector3(TreeWind.x, TreeWind.y, TreeWind.z).magnitude, new Vector3(TreeWindPrev.x, TreeWindPrev.y, TreeWindPrev.z).magnitude);
                return amplitude * height * 2.0f * Mathf.Max(0.0f, maxRigidity);
            }

            return 0.0f;
        }

        public float MaxVerticalDisplacement(float treeHeight)
        {
            if (Profile != XRayFloraWindProfile.AnomalySsfx)
            {
                return 0.0f;
            }

            float speed = Mathf.Max(TreesSetup.w, Mathf.Clamp01(Weather.Velocity * 0.001f));
            return Mathf.Clamp01(Mathf.Max(0.0f, treeHeight) * 0.1f) * speed;
        }

        public void Apply()
        {
            Shader.SetGlobalFloat(ACTIVE_ID, Initialized ? (float)Profile : 0.0f);
            Shader.SetGlobalVector(PARAMS_ID, WindParams());
            Shader.SetGlobalVector(TREES_SETUP_ID, TreesSetup);
            Shader.SetGlobalVector(ANIM_ID, Anim);
            Shader.SetGlobalVector(ANIM_PREV_ID, AnimPrev);
            Shader.SetGlobalVector(TREE_WIND_ID, TreeWind);
            Shader.SetGlobalVector(TREE_WAVE_ID, TreeWave);
            Shader.SetGlobalVector(TREE_WIND_PREV_ID, TreeWindPrev);
            Shader.SetGlobalVector(TREE_WAVE_PREV_ID, TreeWavePrev);
        }

        public static void Clear()
        {
            Shader.SetGlobalFloat(ACTIVE_ID, 0.0f);
        }

        private void CalculateTree()
        {
            float rotation = (float)(Mathf.PI * 2.0 * Time / TreeRotationPeriod);
            var direction = new Vector3(Mathf.Sin(rotation), 0.0f, Mathf.Cos(rotation));
            direction.Normalize();
            float amplitude = TreeAmplitudeFromWeather ? Weather.TreeAmplitudeIntensity : TreeAmplitude;
            TreeWind = new Vector4(direction.x * amplitude, 0.0f, direction.z * amplitude, 0.0f);
            TreeWave = new Vector4(TreeWaveScale.x, TreeWaveScale.y, TreeWaveScale.z, (float)(Time * TreeWaveSpeed)) / (Mathf.PI * 2.0f);
        }
    }
}
