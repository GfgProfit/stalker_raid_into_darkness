using System;
using UnityEngine;

namespace Rvm.CleanReleaseRoute07
{
    [Serializable]
    internal sealed class PunctualShadowDistanceTrialReport
    {
        public int schemaVersion;
        public string mode;
        public bool restored;
        public int frame;
        public float distance;
        public int discoveryPasses;
        public int worldDiscoveryPasses;
        public int tracked;
        public int eligible;
        public int originalNone;
        public int missingHdData;
        public int restrictedOriginalValues;
        public int directionalsExcluded;
        public int otherTypesExcluded;
        public int currentlyDimmed;
        public int removedDestroyed;
        public int maximumTracked;
        public bool cameraAvailable;
        public string cameraEntityId;
        public Vector3 cameraPosition;
        public int poseCount;
        public PunctualShadowDistancePoseRecord[] poses;
        public PunctualShadowDistanceLightRecord[] lights;
        public string scope;
    }
}
