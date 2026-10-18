using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerLadder : MonoBehaviour
    {
        [SerializeField] private float _height = 3.0f;
        [SerializeField] private float _width = 0.6f;
        [Tooltip("Distance from the ladder plane at which the actor counts as touching it (getting_on_dist).")]
        [SerializeField] private float _touchDistance = 0.3f;

        public Vector3 Bottom => transform.position;
        public Vector3 Top => transform.position + transform.up * _height;
        public Vector3 Axis => transform.up;
        public Vector3 Normal => transform.forward;
        public float Height => _height;

        public float DistanceToPlane(Vector3 point)
        {
            return Vector3.Dot(point - transform.position, Normal);
        }

        public Vector3 DirectionToPlane(Vector3 point)
        {
            Vector3 direction = -Normal * Mathf.Sign(DistanceToPlane(point));
            direction.y = 0.0f;
            return direction.normalized;
        }

        public bool BeforeLadder(Vector3 point, float tolerance)
        {
            return DistanceToPlane(point) > -tolerance;
        }

        public bool InTouch(Vector3 feet, float footRadius)
        {
            Vector3 local = transform.InverseTransformPoint(feet);
            float distance = DistanceToPlane(feet);
            return Mathf.Abs(local.x) <= _width * 0.5f + footRadius && local.y >= -footRadius - _touchDistance && local.y <= _height + footRadius && Mathf.Abs(distance) <= footRadius + _touchDistance;
        }

        public float AxisDistanceToTop(Vector3 feet)
        {
            return Vector3.Dot(Top - feet, Axis);
        }

        public float AxisDistanceToBottom(Vector3 feet)
        {
            return Vector3.Dot(feet - Bottom, Axis);
        }

        public Vector3 ToAxis(Vector3 feet, float footRadius)
        {
            Vector3 local = transform.InverseTransformPoint(feet);
            return transform.TransformVector(new Vector3(-local.x, 0.0f, footRadius - local.z));
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(Bottom, Top);
            Gizmos.DrawLine(Bottom, Bottom + Normal * 0.5f);
        }
    }
}
