#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    public enum GrenadeFragmentResult
    {
        Miss,
        Cover,
        Target
    }

    public readonly struct GrenadeFragmentTrace
    {
        public GrenadeFragmentTrace(Vector3 end, GrenadeFragmentResult result)
        {
            End = end;
            Result = result;
        }

        public Vector3 End { get; }
        public GrenadeFragmentResult Result { get; }
    }

    public sealed class GrenadeShotDebug
    {
        private readonly List<Vector3> _flown = new List<Vector3>(256);
        private readonly List<GrenadeFragmentTrace> _fragments = new List<GrenadeFragmentTrace>();

        public static bool Enabled { get; set; }
        public static GrenadeShotDebug Latest { get; private set; }

        public string AmmoName { get; }
        public GrenadeAmmoData Ammo { get; }
        public Ray Muzzle { get; }
        public Vector3 AimPoint { get; }
        public bool AimHasSurface { get; }
        public bool IsHandThrow { get; }
        public IReadOnlyList<Vector3> Planned { get; }
        public IReadOnlyList<Vector3> Flown => _flown;
        public IReadOnlyList<GrenadeFragmentTrace> Fragments => _fragments;
        public Vector3? ArmingPoint { get; private set; }
        public Vector3? BouncePoint { get; private set; }
        public Vector3 Position { get; private set; }
        public Vector3 Normal { get; private set; }
        public float Elapsed { get; private set; }
        public float Travelled { get; private set; }
        public bool Finished { get; private set; }
        public bool Detonated { get; private set; }
        public bool SurfaceHit { get; private set; }
        public bool HasSurfaceNormal => SurfaceHit || Finished && BouncePoint.HasValue;
        public int DamagedEntities { get; private set; }

        internal GrenadeShotDebug(string ammoName, GrenadeAmmoData ammo, Ray muzzle, Vector3 aimPoint, bool aimHasSurface, Vector3[] planned, bool isHandThrow = false)
        {
            AmmoName = ammoName;
            Ammo = ammo;
            Muzzle = muzzle;
            AimPoint = aimPoint;
            AimHasSurface = aimHasSurface;
            IsHandThrow = isHandThrow;
            Planned = planned;
            Position = muzzle.origin;
            ArmingPoint = ammo.ArmingDistance <= 0f && ammo.ArmingSeconds <= 0f ? muzzle.origin : null;
            _flown.Add(muzzle.origin);
            Latest = this;
        }

        public static void Clear()
        {
            Latest = null;
        }

        internal void Advance(Vector3 from, Vector3 to, float elapsed, float travelled, float stepDistance, bool record)
        {
            if (!ArmingPoint.HasValue && travelled >= Ammo.ArmingDistance && elapsed >= Ammo.ArmingSeconds)
            {
                float distanceFraction = stepDistance > 0f ? 1f - (travelled - Ammo.ArmingDistance) / stepDistance : 0f;
                float timeFraction = elapsed > Elapsed ? (Ammo.ArmingSeconds - Elapsed) / (elapsed - Elapsed) : 0f;
                ArmingPoint = Vector3.Lerp(from, to, Mathf.Max(distanceFraction, timeFraction));
            }

            Position = to;
            Elapsed = elapsed;
            Travelled = travelled;
            if (record && _flown.Count < 1024)
            {
                _flown.Add(to);
            }
        }

        internal void Finish(Vector3 point, Vector3 normal, bool detonated, bool surfaceHit)
        {
            Position = point;
            Normal = normal;
            Finished = true;
            Detonated = detonated;
            SurfaceHit = surfaceHit;
            _flown.Add(point);
        }

        internal void MarkBounce(Vector3 point)
        {
            BouncePoint = point;
            _flown.Add(point);
        }

        internal void AddFragment(Vector3 end, GrenadeFragmentResult result)
        {
            _fragments.Add(new GrenadeFragmentTrace(end, result));
        }

        internal void AddDamagedEntity()
        {
            DamagedEntities++;
        }
    }
}
#endif
