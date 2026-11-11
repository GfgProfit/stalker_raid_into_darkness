using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Rvm.Benchmark
{
    public interface IBenchmarkWeatherState
    {
        double TimeSeconds { get; }
        bool Paused { get; }
        bool Busy { get; }
        string Profile { get; }
        void Seek(double seconds);
        void SetPaused(bool value);
    }

    public sealed class BenchmarkWorldState : IDisposable
    {
        private readonly IBenchmarkWeatherState[] _weather;
        private readonly double[] _times;
        private readonly bool[] _paused;
        private bool _disposed;

        public BenchmarkWorldState(IBenchmarkWeatherState[] weather, double hours, string expectedProfile)
        {
            if (weather == null || weather.Length == 0 || double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0 || hours >= 24)
            {
                throw new InvalidOperationException("Fixed weather/time requires an existing weather controller and a valid hour.");
            }
            _weather = weather;
            _times = new double[weather.Length];
            _paused = new bool[weather.Length];
            for (int i = 0; i < weather.Length; i++)
            {
                if (weather[i].Busy || string.IsNullOrEmpty(weather[i].Profile) || (!string.IsNullOrEmpty(expectedProfile) && weather[i].Profile != expectedProfile))
                {
                    throw new InvalidOperationException("Fixed weather/time requires a stable prepared profile, no transition/FX, and the expected profile when specified. No profile is replaced.");
                }
                _times[i] = weather[i].TimeSeconds;
                _paused[i] = weather[i].Paused;
            }
            try
            {
                for (int i = 0; i < weather.Length; i++)
                {
                    weather[i].SetPaused(true);
                    weather[i].Seek(hours * 3600);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static BenchmarkWorldState Acquire(Camera camera, BenchmarkScenario scenario)
        {
            if (!scenario.FixedWeatherTime)
            {
                return null;
            }
            var weather = new List<IBenchmarkWeatherState>();
            foreach (var root in camera.gameObject.scene.GetRootGameObjects())
            {
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component != null && component.isActiveAndEnabled && component.GetType().FullName == "XRay.Importer.Levels.XRayWeatherController")
                    {
                        weather.Add(new ReflectionWeather(component));
                    }
                }
            }
            return new BenchmarkWorldState(weather.ToArray(), scenario.FixedTimeHours, scenario.ExpectedWeatherProfile);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Exception failure = null;
            for (int i = 0; i < _weather.Length; i++)
            {
                try
                {
                    _weather[i].Seek(_times[i]);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                try
                {
                    _weather[i].SetPaused(_paused[i]);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }
            if (failure != null)
            {
                throw new InvalidOperationException("Weather/time restoration failed.", failure);
            }
        }

        private sealed class ReflectionWeather : IBenchmarkWeatherState
        {
            private readonly MonoBehaviour _component;
            private readonly Type _type;

            public double TimeSeconds => Convert.ToDouble(Get("GameTimeSeconds"));
            public bool Paused => (bool)Get("Paused");
            public bool Busy => (bool)Get("IsTransitioning") || Get("ActiveFx") is UnityEngine.Object fx && fx != null;
            public string Profile => Get("ActiveProfile") is UnityEngine.Object profile && profile != null ? profile.name : null;

            public ReflectionWeather(MonoBehaviour component)
            {
                _component = component;
                _type = component.GetType();
            }

            public void Seek(double seconds)
            {
                _type.GetMethod("Seek", new[] { typeof(double) }).Invoke(_component, new object[] { seconds });
            }

            public void SetPaused(bool value)
            {
                _type.GetMethod("SetPaused", new[] { typeof(bool) }).Invoke(_component, new object[] { value });
            }

            private object Get(string property)
            {
                PropertyInfo info = _type.GetProperty(property);
                if (info == null || _component == null)
                {
                    throw new InvalidOperationException("Weather API unavailable: " + property);
                }
                return info.GetValue(_component);
            }
        }
    }
}
