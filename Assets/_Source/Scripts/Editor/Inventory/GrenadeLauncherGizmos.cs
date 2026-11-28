using System.Collections.Generic;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaidIntoDarkness.Inventory.Editor
{
    [InitializeOnLoad]
    internal static class GrenadeLauncherGizmos
    {
        private const string MENU_ROOT = "Tools/Weapons/Grenade Launcher Gizmos/";
        private const string ENABLED_MENU = MENU_ROOT + "Enabled";
        private const string FRAGMENTS_MENU = MENU_ROOT + "Show Fragments";
        private const string CLEAR_MENU = MENU_ROOT + "Clear Last Shot";
        private const string ENABLED_KEY = "RaidIntoDarkness.GrenadeGizmos.Enabled";
        private const string FRAGMENTS_KEY = "RaidIntoDarkness.GrenadeGizmos.Fragments";

        private static readonly Color MUZZLE_COLOR = new Color(0.32f, 0.74f, 1f);
        private static readonly Color PLANNED_COLOR = new Color(1f, 0.82f, 0.35f, 0.8f);
        private static readonly Color FLOWN_COLOR = new Color(0.34f, 1f, 0.78f);
        private static readonly Color BOUNCE_COLOR = new Color(0.78f, 0.56f, 1f);
        private static readonly Color BLAST_COLOR = new Color(1f, 0.45f, 0.19f, 0.85f);
        private static readonly Color DUD_COLOR = new Color(0.65f, 0.72f, 0.84f);
        private static readonly Color MISS_COLOR = new Color(0.58f, 0.67f, 0.86f, 0.18f);
        private static readonly Color COVER_COLOR = new Color(1f, 0.68f, 0.33f, 0.60f);
        private static readonly Color TARGET_COLOR = new Color(1f, 0.30f, 0.45f, 0.95f);

        private static bool _showFragments;
        private static GUIStyle _panel;
        private static GUIStyle _caption;

        static GrenadeLauncherGizmos()
        {
            GrenadeShotDebug.Enabled = EditorPrefs.GetBool(ENABLED_KEY);
            _showFragments = EditorPrefs.GetBool(FRAGMENTS_KEY, true);
            SceneView.duringSceneGui += DrawScene;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem(ENABLED_MENU)]
        private static void ToggleEnabled()
        {
            GrenadeShotDebug.Enabled = !GrenadeShotDebug.Enabled;
            EditorPrefs.SetBool(ENABLED_KEY, GrenadeShotDebug.Enabled);
            SceneView.RepaintAll();
        }

        [MenuItem(ENABLED_MENU, true)]
        private static bool ValidateEnabled()
        {
            Menu.SetChecked(ENABLED_MENU, GrenadeShotDebug.Enabled);
            return true;
        }

        [MenuItem(FRAGMENTS_MENU)]
        private static void ToggleFragments()
        {
            _showFragments = !_showFragments;
            EditorPrefs.SetBool(FRAGMENTS_KEY, _showFragments);
            SceneView.RepaintAll();
        }

        [MenuItem(FRAGMENTS_MENU, true)]
        private static bool ValidateFragments()
        {
            Menu.SetChecked(FRAGMENTS_MENU, _showFragments);
            return GrenadeShotDebug.Enabled;
        }

        [MenuItem(CLEAR_MENU)]
        private static void Clear()
        {
            GrenadeShotDebug.Clear();
            SceneView.RepaintAll();
        }

        [MenuItem(CLEAR_MENU, true)]
        private static bool ValidateClear() => GrenadeShotDebug.Latest != null;

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state is PlayModeStateChange.ExitingPlayMode or PlayModeStateChange.EnteredPlayMode)
            {
                Clear();
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy)]
        private static void DrawMuzzle(FirstPersonWeaponView view, GizmoType gizmoType)
        {
            if (!GrenadeShotDebug.Enabled || view.GrenadeMuzzle == null)
            {
                return;
            }

            Transform muzzle = view.GrenadeMuzzle;
            using (new Handles.DrawingScope(MUZZLE_COLOR))
            {
                Handles.ArrowHandleCap(0, muzzle.position, Quaternion.LookRotation(muzzle.TransformVector(Vector3.forward)), 0.35f, EventType.Repaint);
                Handles.Label(muzzle.position, "Дульный срез");
            }
        }

        private static void DrawScene(SceneView view)
        {
            GrenadeShotDebug shot = GrenadeShotDebug.Latest;
            if (!GrenadeShotDebug.Enabled || !view.drawGizmos || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (shot == null)
            {
                if (EditorApplication.isPlaying)
                {
                    DrawReadout(view, null);
                }

                return;
            }

            CompareFunction previousDepth = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            try
            {
                using (new Handles.DrawingScope(PLANNED_COLOR))
                {
                    DrawPath(shot.Planned, true);
                    if (shot.AimHasSurface)
                    {
                        DrawPoint(shot.AimPoint, 0.14f);
                    }
                }

                using (new Handles.DrawingScope(MUZZLE_COLOR))
                {
                    Handles.ArrowHandleCap(0, shot.Muzzle.origin, Quaternion.LookRotation(shot.Muzzle.direction), 0.45f, EventType.Repaint);
                    DrawPoint(shot.Muzzle.origin, 0.08f);
                }

                using (new Handles.DrawingScope(FLOWN_COLOR))
                {
                    DrawPath(shot.Flown, false);
                    if (shot.ArmingPoint.HasValue)
                    {
                        DrawPoint(shot.ArmingPoint.Value, 0.1f);
                        Handles.Label(shot.ArmingPoint.Value, "Взведение");
                    }
                }

                if (shot.BouncePoint.HasValue)
                {
                    using (new Handles.DrawingScope(BOUNCE_COLOR))
                    {
                        DrawPoint(shot.BouncePoint.Value, 0.13f);
                        Handles.Label(shot.BouncePoint.Value, "Подскок");
                    }
                }

                if (shot.Finished)
                {
                    DrawImpact(shot);
                }
                else
                {
                    using (new Handles.DrawingScope(FLOWN_COLOR))
                    {
                        DrawPoint(shot.Position, 0.1f);
                    }
                }

                DrawReadout(view, shot);
            }
            finally
            {
                Handles.zTest = previousDepth;
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.InSelectionHierarchy)]
        private static void DrawGameShot(FirstPersonWeaponController controller, GizmoType gizmoType)
        {
            Camera camera = Camera.current;
            if (!GrenadeShotDebug.Enabled || !EditorApplication.isPlaying || !controller.isActiveAndEnabled || camera == null || camera.cameraType != CameraType.Game || camera.targetTexture != null)
            {
                return;
            }

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.identity;
            try
            {
                GrenadeShotDebug shot = GrenadeShotDebug.Latest;
                if (shot == null)
                {
                    if (controller.TryGetHandGrenadeThrowRay(out Ray hand))
                    {
                        DrawGameMuzzle(hand);
                    }
                    else if (controller.ActiveWeapon?.IsStandaloneLauncher == true && controller.TryGetMuzzleRay(out Ray launcher))
                    {
                        DrawGameMuzzle(launcher);
                    }
                    else if (controller.IsGrenadeMode && controller.Model != null && controller.Model.TryGetComponent(out FirstPersonWeaponView view) && view.GrenadeMuzzle != null)
                    {
                        Transform muzzle = view.GrenadeMuzzle;
                        DrawGameMuzzle(new Ray(muzzle.position, muzzle.TransformVector(Vector3.forward).normalized));
                    }

                    return;
                }

                Gizmos.color = PLANNED_COLOR;
                DrawGamePath(shot.Planned, 2);
                if (shot.AimHasSurface)
                {
                    Gizmos.DrawWireSphere(shot.AimPoint, 0.07f);
                }

                DrawGameMuzzle(shot.Muzzle);
                Gizmos.color = FLOWN_COLOR;
                DrawGamePath(shot.Flown, 1);
                if (shot.ArmingPoint.HasValue)
                {
                    Gizmos.DrawWireSphere(shot.ArmingPoint.Value, 0.06f);
                }

                if (shot.BouncePoint.HasValue)
                {
                    Gizmos.color = BOUNCE_COLOR;
                    Gizmos.DrawWireSphere(shot.BouncePoint.Value, 0.065f);
                }

                Gizmos.color = shot.Finished ? shot.Detonated ? BLAST_COLOR : DUD_COLOR : FLOWN_COLOR;
                Gizmos.DrawWireSphere(shot.Position, 0.09f);
                if (shot.HasSurfaceNormal)
                {
                    Gizmos.DrawLine(shot.Position, shot.Position + shot.Normal * 0.7f);
                }

                if (shot.Detonated && shot.Ammo.BlastRadius > 0f)
                {
                    Gizmos.DrawWireSphere(shot.Position, shot.Ammo.BlastRadius);
                }

                if (_showFragments)
                {
                    foreach (GrenadeFragmentTrace fragment in shot.Fragments)
                    {
                        Gizmos.color = GetFragmentColor(fragment.Result);
                        Gizmos.DrawLine(shot.Position, fragment.End);
                    }
                }
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
            }
        }

        private static void DrawGameMuzzle(Ray muzzle)
        {
            Gizmos.color = MUZZLE_COLOR;
            Gizmos.DrawWireSphere(muzzle.origin, 0.04f);
            Gizmos.DrawRay(muzzle.origin, muzzle.direction * 0.45f);
        }

        private static void DrawGamePath(IReadOnlyList<Vector3> points, int stride)
        {
            for (int index = 1; index < points.Count; index += stride)
            {
                Gizmos.DrawLine(points[index - 1], points[index]);
            }
        }

        private static void DrawPath(IReadOnlyList<Vector3> points, bool dotted)
        {
            for (int index = 1; index < points.Count; index++)
            {
                if (dotted)
                {
                    Handles.DrawDottedLine(points[index - 1], points[index], 5f);
                }
                else
                {
                    Handles.DrawLine(points[index - 1], points[index], 2.75f);
                }
            }
        }

        private static void DrawImpact(GrenadeShotDebug shot)
        {
            using (new Handles.DrawingScope(shot.Detonated ? BLAST_COLOR : DUD_COLOR))
            {
                DrawPoint(shot.Position, 0.18f);
                if (shot.HasSurfaceNormal)
                {
                    Handles.ArrowHandleCap(0, shot.Position, Quaternion.FromToRotation(Vector3.forward, shot.Normal), 0.7f, EventType.Repaint);
                }

                if (shot.Detonated && shot.Ammo.BlastRadius > 0f)
                {
                    Quaternion surface = Quaternion.FromToRotation(Vector3.up, shot.Normal);
                    Handles.DrawWireDisc(shot.Position, shot.Normal, shot.Ammo.BlastRadius, 2f);
                    Handles.DrawWireDisc(shot.Position, surface * Vector3.forward, shot.Ammo.BlastRadius, 1f);
                    Handles.DrawWireDisc(shot.Position, surface * Vector3.right, shot.Ammo.BlastRadius, 1f);
                    using (new Handles.DrawingScope(new Color(BLAST_COLOR.r, BLAST_COLOR.g, BLAST_COLOR.b, 0.07f)))
                    {
                        Handles.DrawSolidDisc(shot.Position, shot.Normal, shot.Ammo.BlastRadius);
                    }
                }
            }

            if (_showFragments)
            {
                foreach (GrenadeFragmentTrace fragment in shot.Fragments)
                {
                    using (new Handles.DrawingScope(GetFragmentColor(fragment.Result)))
                    {
                        Handles.DrawLine(shot.Position, fragment.End, fragment.Result == GrenadeFragmentResult.Target ? 2f : 1f);
                        if (fragment.Result != GrenadeFragmentResult.Miss)
                        {
                            DrawPoint(fragment.End, 0.04f);
                        }
                    }
                }
            }
        }

        private static Color GetFragmentColor(GrenadeFragmentResult result)
        {
            return result switch
            {
                GrenadeFragmentResult.Target => TARGET_COLOR,
                GrenadeFragmentResult.Cover => COVER_COLOR,
                _ => MISS_COLOR
            };
        }

        private static void DrawPoint(Vector3 point, float size)
        {
            Handles.SphereHandleCap(0, point, Quaternion.identity, size, EventType.Repaint);
        }

        private static void DrawReadout(SceneView view, GrenadeShotDebug shot)
        {
            Handles.BeginGUI();
            try
            {
                _panel ??= new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 8, 8)
                };
                _caption ??= new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
                Rect panel = new Rect(12f, Mathf.Max(70f, view.position.height - 166f), Mathf.Min(356f, view.position.width - 24f), shot != null ? 126f : 45f);
                GUI.Box(panel, GUIContent.none, _panel);
                Rect line = new Rect(panel.x + 12f, panel.y + 7f, panel.width - 24f, 18f);
                if (shot == null)
                {
                    GUI.Label(line, "Гранаты · ожидание выстрела или броска", EditorStyles.boldLabel);
                    return;
                }

                string state = GetShotState(shot);
                GUI.Label(line, $"{shot.AmmoName}  ·  {state}", EditorStyles.boldLabel);
                line.y += 19f;
                GUI.Label(line, $"Путь {shot.Travelled:F2} м  ·  {shot.Elapsed:F2} с  ·  Таймер {shot.Ammo.SelfDestructSeconds:F1} с", _caption);
                line.y += 17f;
                string arming = shot.IsHandThrow ? $"{shot.Ammo.ArmingSeconds:F2} с" : $"{shot.Ammo.ArmingDistance:F1} м";
                GUI.Label(line, $"Взведение {arming}  ·  Радиус {shot.Ammo.BlastRadius:F1} м  ·  Целей {shot.DamagedEntities}", _caption);
                line.y += 17f;
                GUI.Label(line, $"Взрыв до {shot.Ammo.BlastDamage:F0} HP  ·  Осколки {shot.Fragments.Count}/{shot.Ammo.FragmentCount} по {shot.Ammo.FragmentDamage:F0} HP", _caption);
                line.y += 20f;
                GUI.Label(line, "Жёлтый — расчёт   ·   Зелёный — полёт   ·   Оранжевый — укрытие", _caption);
                line.y += 16f;
                GUI.Label(line, "Розовый — попадание осколка   ·   Серый — свободный луч", _caption);
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        private static string GetShotState(GrenadeShotDebug shot)
        {
            if (!shot.Finished)
            {
                if (shot.Ammo.FliesStraight)
                {
                    return "Полёт по оси ствола";
                }

                return shot.BouncePoint.HasValue ? shot.IsHandThrow ? "После отскока" : "Подскок" : "В полёте";
            }

            if (!shot.Detonated)
            {
                return "Без взрыва";
            }

            if (shot.IsHandThrow)
            {
                return shot.Ammo.ExplodeOnImpact && shot.SurfaceHit ? "Взрыв при попадании" : "Взрыв по таймеру";
            }

            if (shot.SurfaceHit)
            {
                return "Взрыв при попадании";
            }

            return shot.BouncePoint.HasValue ? "Взрыв после подскока" : "Самоликвидация";
        }
    }
}
