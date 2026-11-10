using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public static class AnomalyParticleDynamicsMath
    {
        public static Vector3 GenerateVelocity(XRayParticleDomain domain, uint seed, Matrix4x4 parent, bool allowRotate, out UnityEngine.Random.State particleRandomState)
        {
            UnityEngine.Random.State previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(unchecked((int)seed));
                Vector3 velocity = domain.Generate();
                particleRandomState = UnityEngine.Random.state;
                return allowRotate ? parent.MultiplyVector(velocity) : velocity;
            }
            finally
            {
                UnityEngine.Random.state = previous;
            }
        }

        public static bool Apply(ref Vector3 position, ref Vector3 velocity, AnomalyParticleMotionAction action, float deltaTime, float actionTime)
        {
            switch (action.Type)
            {
                case XRayParticleActionType.Move:
                    position += velocity * deltaTime;
                    break;
                case XRayParticleActionType.Gravity:
                    velocity += action.Vector0 * deltaTime;
                    break;
                case XRayParticleActionType.TargetVelocity:
                    velocity += (action.Vector0 - velocity) * (action.Parameter0 * deltaTime);
                    break;
                case XRayParticleActionType.Damping:
                    Damping(ref velocity, action, deltaTime);
                    break;
                case XRayParticleActionType.OrbitPoint:
                    Orbit(ref velocity, action.Vector0 - position, action, deltaTime);
                    break;
                case XRayParticleActionType.OrbitLine:
                    Vector3 fromLine = position - action.Vector0;
                    Orbit(ref velocity, action.Vector1 * Vector3.Dot(fromLine, action.Vector1) - fromLine, action, deltaTime);
                    break;
                case XRayParticleActionType.Vortex:
                    Vortex(ref position, action, deltaTime);
                    break;
                case XRayParticleActionType.Scatter:
                    Scatter(ref velocity, position - action.Vector0, action, deltaTime);
                    break;
                case XRayParticleActionType.RandomAccel:
                    velocity += action.Domain.Generate() * deltaTime;
                    break;
                case XRayParticleActionType.RandomDisplace:
                    position += action.Domain.Generate() * deltaTime;
                    break;
                case XRayParticleActionType.RandomVelocity:
                    velocity = action.Domain.Generate();
                    break;
                case XRayParticleActionType.SpeedLimit:
                    LimitSpeed(ref velocity, action.Parameter0, action.Parameter1);
                    break;
                case XRayParticleActionType.Turbulence:
                    Turbulence(ref velocity, position, action, actionTime);
                    break;
                case XRayParticleActionType.Sink:
                    return Contains(action.Domain, position) != (action.Parameter0 != 0f);
            }
            return true;
        }

        public static bool Contains(XRayParticleDomain domain, Vector3 position)
        {
            switch (domain.Type)
            {
                case XRayParticleDomainType.Plane:
                    return Vector3.Dot(position - domain.P1, domain.P2) >= 0f;
                case XRayParticleDomainType.Box:
                    Vector3 minimum = Vector3.Min(domain.P1, domain.P2);
                    Vector3 maximum = Vector3.Max(domain.P1, domain.P2);
                    return position.x >= minimum.x && position.x <= maximum.x && position.y >= minimum.y && position.y <= maximum.y && position.z >= minimum.z && position.z <= maximum.z;
                case XRayParticleDomainType.Sphere:
                    float squared = (position - domain.P1).sqrMagnitude;
                    return squared <= domain.Radius1 * domain.Radius1 && squared >= domain.Radius2 * domain.Radius2;
                case XRayParticleDomainType.Point:
                    return position == domain.P1;
                default:
                    return false;
            }
        }

        private static void Damping(ref Vector3 velocity, AnomalyParticleMotionAction action, float deltaTime)
        {
            float low = action.DampingBoundsSquared ? action.Parameter0 : action.Parameter0 * action.Parameter0;
            float high = action.DampingBoundsSquared ? action.Parameter1 : action.Parameter1 * action.Parameter1;
            float squared = velocity.sqrMagnitude;
            if (squared >= low && squared <= high)
            {
                velocity = Vector3.Scale(velocity, Vector3.one - (Vector3.one - action.Vector0) * deltaTime);
            }
        }

        private static void Orbit(ref Vector3 velocity, Vector3 direction, AnomalyParticleMotionAction action, float deltaTime)
        {
            float squared = direction.sqrMagnitude;
            if (squared >= action.Parameter2 * action.Parameter2)
            {
                return;
            }
            float denominator = Mathf.Sqrt(squared) + squared + action.Parameter0;
            if (denominator > 0f)
            {
                velocity += direction * (action.Parameter1 * deltaTime / denominator);
            }
        }

        private static void Vortex(ref Vector3 position, AnomalyParticleMotionAction action, float deltaTime)
        {
            Vector3 offset = position - action.Vector0;
            float squared = offset.sqrMagnitude;
            if (squared <= 0f || squared > action.Parameter2 * action.Parameter2)
            {
                return;
            }
            float radius = Mathf.Sqrt(squared);
            Vector3 normalized = offset / radius;
            Vector3 parallel = action.Vector1 * Vector3.Dot(normalized, action.Vector1);
            Vector3 perpendicular = normalized - parallel;
            float denominator = squared + action.Parameter0;
            if (denominator <= 0f)
            {
                return;
            }
            float angle = action.Parameter1 * deltaTime / denominator;
            position = action.Vector0 + (perpendicular * Mathf.Cos(angle) + Vector3.Cross(action.Vector1, perpendicular) * Mathf.Sin(angle) + parallel) * radius;
        }

        private static void Scatter(ref Vector3 velocity, Vector3 direction, AnomalyParticleMotionAction action, float deltaTime)
        {
            float squared = direction.sqrMagnitude;
            if (squared <= 0f || squared >= action.Parameter2 * action.Parameter2)
            {
                return;
            }
            float denominator = squared + action.Parameter0;
            if (denominator > 0f)
            {
                velocity += direction / Mathf.Sqrt(squared) * (action.Parameter1 * deltaTime / denominator);
            }
        }

        private static void LimitSpeed(ref Vector3 velocity, float minimum, float maximum)
        {
            float squared = velocity.sqrMagnitude;
            if (squared < minimum * minimum && squared > 0f)
            {
                velocity *= minimum / Mathf.Sqrt(squared);
            }
            else if (squared > maximum * maximum)
            {
                velocity *= maximum / Mathf.Sqrt(squared);
            }
        }

        private static void Turbulence(ref Vector3 velocity, Vector3 position, AnomalyParticleMotionAction action, float actionTime)
        {
            float originalSpeed = velocity.magnitude;
            if (originalSpeed <= 0f)
            {
                return;
            }
            Vector3 sample = position + action.Vector0 * actionTime;
            float center = AnomalyParticleNoise.Fractal(sample, action.Parameter1, action.Octaves);
            float epsilon = action.Parameter0;
            Vector3 perturbation = new Vector3(AnomalyParticleNoise.Fractal(sample + Vector3.right * epsilon, action.Parameter1, action.Octaves) - center, AnomalyParticleNoise.Fractal(sample + Vector3.up * epsilon, action.Parameter1, action.Octaves) - center, AnomalyParticleNoise.Fractal(sample + Vector3.forward * epsilon, action.Parameter1, action.Octaves) - center);
            velocity += perturbation * action.Parameter2;
            float speed = velocity.magnitude;
            if (speed > 0f)
            {
                velocity *= originalSpeed / speed;
            }
        }
    }
}
