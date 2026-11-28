using System.Collections.Generic;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    [InitializeOnLoad]
    internal static class BulletTrajectoryGizmos
    {
        private const string MENU = "Tools/Weapons/Bullet Trajectory Gizmos";
        private const string ENABLED_KEY = "RaidIntoDarkness.BulletTrajectoryGizmos.Enabled";
        private const int MAX_SEGMENTS = 2048;
        private const double LIFETIME_SECONDS = 5d;
        private const float MUZZLE_RADIUS = 0.025f;
        private const float IMPACT_RADIUS = 0.075f;
        private const float DIRECTION_LENGTH = 0.25f;

        private static readonly Queue<(Vector3 Start, Vector3 End, Color Color, float MarkerRadius, double ExpiresAt)> _segments = new Queue<(Vector3, Vector3, Color, float, double)>(MAX_SEGMENTS);

        private static bool _enabled;

        static BulletTrajectoryGizmos()
        {
            SetEnabled(EditorPrefs.GetBool(ENABLED_KEY, true));
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem(MENU)]
        private static void Toggle()
        {
            SetEnabled(!_enabled);
            EditorPrefs.SetBool(ENABLED_KEY, _enabled);
            SceneView.RepaintAll();
        }

        [MenuItem(MENU, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(MENU, _enabled);
            return true;
        }

        private static void SetEnabled(bool enabled)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            if (enabled)
            {
                BallisticProjectile.EditorShotLaunched += RecordMuzzle;
                BallisticProjectile.EditorSegmentSimulated += RecordSegment;
            }
            else
            {
                BallisticProjectile.EditorShotLaunched -= RecordMuzzle;
                BallisticProjectile.EditorSegmentSimulated -= RecordSegment;
                _segments.Clear();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state is PlayModeStateChange.EnteredPlayMode or PlayModeStateChange.ExitingPlayMode)
            {
                _segments.Clear();
            }
        }

        private static void RecordMuzzle(Ray ray)
        {
            AddSegment(ray.GetPoint(DIRECTION_LENGTH), ray.origin, Color.cyan, MUZZLE_RADIUS);
        }

        private static void RecordSegment(Vector3 start, Vector3 end, bool impact)
        {
            AddSegment(start, end, impact ? Color.red : Color.green, impact ? IMPACT_RADIUS : 0f);
        }

        private static void AddSegment(Vector3 start, Vector3 end, Color color, float markerRadius)
        {
            double now = EditorApplication.timeSinceStartup;
            RemoveExpiredSegments(now);
            if (_segments.Count == MAX_SEGMENTS)
            {
                _segments.Dequeue();
            }

            _segments.Enqueue((start, end, color, markerRadius, now + LIFETIME_SECONDS));
        }

        private static void RemoveExpiredSegments(double now)
        {
            while (_segments.TryPeek(out var segment) && segment.ExpiresAt <= now)
            {
                _segments.Dequeue();
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.InSelectionHierarchy)]
        private static void Draw(FirstPersonWeaponController controller, GizmoType gizmoType)
        {
            Camera camera = Camera.current;
            if (!_enabled || !EditorApplication.isPlaying || !controller.isActiveAndEnabled || camera == null || camera.cameraType is not (CameraType.Game or CameraType.SceneView) || camera.cameraType == CameraType.Game && camera.targetTexture != null)
            {
                return;
            }

            RemoveExpiredSegments(EditorApplication.timeSinceStartup);
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.identity;
            try
            {
                foreach (var segment in _segments)
                {
                    Gizmos.color = segment.Color;
                    Gizmos.DrawLine(segment.Start, segment.End);
                    if (segment.MarkerRadius > 0f)
                    {
                        Gizmos.DrawWireSphere(segment.End, segment.MarkerRadius);
                    }
                }
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
            }
        }
    }
}
