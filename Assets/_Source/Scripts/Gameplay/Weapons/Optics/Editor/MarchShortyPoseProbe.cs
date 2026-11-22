using System;
using System.Collections.Generic;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer;

namespace RaidIntoDarkness.Weapons.Optics.Editor
{
    public static class MarchShortyPoseProbe
    {
        private const string PLAYER_PATH = "Assets/_Source/FirstPerson/FPS_Player.prefab";
        private const string WEAPON_PATH = "Assets/_Source/Items/Weapons/FirstPerson/wpn_aug_a3.asset";
        private const string PROFILE_PATH = "Assets/_Source/Settings/Player/StalkerMovementProfile.asset";
        private const string HANDS_PATH = "Assets/_Source/FirstPerson/Hands/Models/wpn_hand_01.ogf";
        private const string DEFINITION_PATH = ScopeOpticsPrefabSetup.MARCH_ITEM;
        private const string SCOPE_NAME = "march_f_shorty";
        private const float OCULAR_HALF_SIZE = 0.0158353858f;

        private static readonly Vector3 OCULAR_CENTER = new Vector3(0.0001934683f, 0.03417937f, -0.0187098235f);

        [Serializable]
        private sealed class PoseSample
        {
            [SerializeField] private float _time;
            [SerializeField] private Vector3 _ocularRelativeToEye;
            [SerializeField] private Vector3 _eyeInPhysicalPlaneFrame;
            [SerializeField] private Vector3 _eyeInTransformRotationFrame;
            [SerializeField] private Vector3 _opticalForwardRelativeToEye;
            [SerializeField] private Vector2 _worldHalfSize;
            [SerializeField] private float _planeBasisDot;
            [SerializeField] private float _axisDeviationDegrees;
            [SerializeField] private Vector3 _centeringAimOffset;
            [SerializeField] private Vector3 _ninetyMillimeterAimOffset;
            [SerializeField] private Vector3 _sharedMeshCenter;
            [SerializeField] private Vector3 _sharedMeshHalfSize;
            [SerializeField] private Vector3 _bakedMeshCenter;
            [SerializeField] private Vector3 _bakedMeshHalfSize;
            [SerializeField] private Vector3 _sharedOcularRelativeToEye;
            [SerializeField] private Vector3 _bakedOcularRelativeToEye;
            [SerializeField] private Vector3 _lensWorldBasisLengths;
            [SerializeField] private Vector3 _scopeWorldBasisLengths;

            public PoseSample(float time, Camera camera, Transform handsRoot, Transform scopeRoot, Transform lens, Bounds sharedBounds, Bounds bakedBounds)
            {
                _time = time;
                Vector3 ocular = scopeRoot.TransformPoint(OCULAR_CENTER);
                Vector3 right = scopeRoot.TransformVector(Vector3.right);
                Vector3 up = scopeRoot.TransformVector(Vector3.up);
                Vector3 forward = Vector3.Cross(right, up).normalized;
                Quaternion physicalRotation = Quaternion.LookRotation(forward, up.normalized);
                Vector3 eyeOffset = camera.transform.position - ocular;
                _ocularRelativeToEye = camera.transform.InverseTransformPoint(ocular);
                _eyeInPhysicalPlaneFrame = Quaternion.Inverse(physicalRotation) * eyeOffset;
                _eyeInTransformRotationFrame = Quaternion.Inverse(scopeRoot.rotation) * eyeOffset;
                _opticalForwardRelativeToEye = camera.transform.InverseTransformDirection(forward);
                _worldHalfSize = new Vector2(OCULAR_HALF_SIZE * right.magnitude, OCULAR_HALF_SIZE * up.magnitude);
                _planeBasisDot = Vector3.Dot(right.normalized, up.normalized);
                _axisDeviationDegrees = Vector3.Angle(camera.transform.forward, forward);
                float distance = -_eyeInPhysicalPlaneFrame.z;
                _centeringAimOffset = handsRoot.InverseTransformVector(camera.transform.position + forward * distance - ocular);
                _ninetyMillimeterAimOffset = handsRoot.InverseTransformVector(camera.transform.position + forward * 0.09f - ocular);
                _sharedMeshCenter = sharedBounds.center;
                _sharedMeshHalfSize = sharedBounds.extents;
                _bakedMeshCenter = bakedBounds.center;
                _bakedMeshHalfSize = bakedBounds.extents;
                _sharedOcularRelativeToEye = camera.transform.InverseTransformPoint(lens.TransformPoint(sharedBounds.center));
                _bakedOcularRelativeToEye = camera.transform.InverseTransformPoint(lens.TransformPoint(bakedBounds.center));
                _lensWorldBasisLengths = new Vector3(lens.TransformVector(Vector3.right).magnitude, lens.TransformVector(Vector3.up).magnitude, lens.TransformVector(Vector3.forward).magnitude);
                _scopeWorldBasisLengths = new Vector3(right.magnitude, up.magnitude, scopeRoot.TransformVector(Vector3.forward).magnitude);
            }
        }

        [Serializable]
        private sealed class PoseReport
        {
            [SerializeField] private string _weapon = WEAPON_PATH;
            [SerializeField] private string _hands = HANDS_PATH;
            [SerializeField] private string _lensPath;
            [SerializeField] private string _scopeRootPath;
            [SerializeField] private string _handsClip;
            [SerializeField] private string _weaponClip;
            [SerializeField] private float _hudScale;
            [SerializeField] private bool _playMode = false;
            [SerializeField] private bool _usedConfiguredOffset;
            [SerializeField] private Vector3 _aimOffsetPosition;
            [SerializeField] private List<PoseSample> _samples = new List<PoseSample>();

            public PoseReport(string lensPath, string scopeRootPath, FirstPersonWeaponMotion motion, float hudScale, bool usedConfiguredOffset, Vector3 aimOffsetPosition)
            {
                _lensPath = lensPath;
                _scopeRootPath = scopeRootPath;
                _handsClip = motion.Hands.name;
                _weaponClip = motion.Weapon != null ? motion.Weapon.name : string.Empty;
                _hudScale = hudScale;
                _usedConfiguredOffset = usedConfiguredOffset;
                _aimOffsetPosition = aimOffsetPosition;
            }

            public void Add(PoseSample sample)
            {
                _samples.Add(sample);
            }
        }

        public static string Inspect()
        {
            return InspectPose(false);
        }

        public static string InspectConfigured()
        {
            return InspectPose(true);
        }

        private static string InspectPose(bool useConfiguredOffset)
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException("MarchShortyPoseProbe requires Edit Mode.");
            }

            FirstPersonWeaponAsset asset = AssetDatabase.LoadAssetAtPath<FirstPersonWeaponAsset>(WEAPON_PATH);
            StalkerMovementProfile profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>(PROFILE_PATH);
            GameObject playerSource = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PATH);
            GameObject handsSource = AssetDatabase.LoadAssetAtPath<GameObject>(HANDS_PATH);
            if (asset == null || asset.HudModel == null || profile == null || playerSource == null || handsSource == null)
            {
                throw new InvalidOperationException("March Shorty pose probe requires the existing AUG, player, profile and hands assets.");
            }

            FirstPersonWeaponMotion motion = asset.Find(FirstPersonWeaponMotionKind.IdleAim, 0);
            if (motion == null || motion.Hands == null)
            {
                throw new InvalidOperationException("AUG requires its existing IdleAim hands motion.");
            }

            Vector3 aimOffsetPosition = useConfiguredOffset ? ReadConfiguredAimOffset(asset) : Vector3.zero;

            Scene preview = EditorSceneManager.NewPreviewScene();
            Mesh bakedLens = null;
            try
            {
                GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerSource, preview);
                StalkerCameraController cameraController = player.GetComponentInChildren<StalkerCameraController>(true);
                FirstPersonRig rig = player.GetComponentInChildren<FirstPersonRig>(true);
                if (cameraController == null || cameraController.Camera == null || rig == null)
                {
                    throw new InvalidOperationException("Player prefab lacks the existing camera or first person rig references.");
                }

                if (cameraController.gameObject.scene != preview || cameraController.Camera.gameObject.scene != preview || rig.gameObject.scene != preview)
                {
                    throw new InvalidOperationException("Pose probe references must belong to its isolated preview scene.");
                }

                Transform handsRoot = new SerializedObject(rig).FindProperty("_handsRoot").objectReferenceValue as Transform;
                if (handsRoot == null)
                {
                    throw new InvalidOperationException("Player first person rig requires Hands Root.");
                }

                if (handsRoot.gameObject.scene != preview)
                {
                    throw new InvalidOperationException("Pose probe Hands Root must belong to its isolated preview scene.");
                }

                float scale = Mathf.Tan(profile.Camera.FieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(profile.Camera.HudFieldOfView * profile.Camera.HudFieldOfViewScale * 0.5f * Mathf.Deg2Rad);
                handsRoot.localScale = new Vector3(scale, scale, 1f);
                Camera camera = cameraController.Camera;
                camera.enabled = false;
                camera.fieldOfView = profile.Camera.FieldOfView;
                GameObject hands = (GameObject)PrefabUtility.InstantiatePrefab(handsSource, preview);
                hands.transform.SetParent(handsRoot, false);
                Transform anchor = FindBone(hands.transform, FirstPersonHudAnimator.ITEM_ANCHOR_BONE);
                if (anchor == null)
                {
                    throw new InvalidOperationException("Existing hands model lacks lead_gun.");
                }

                GameObject weapon = (GameObject)PrefabUtility.InstantiatePrefab(asset.HudModel, preview);
                weapon.transform.SetParent(anchor, false);
                weapon.transform.localPosition = Vector3.zero;
                weapon.transform.localRotation = Quaternion.identity;
                Renderer lens = FindLens(weapon, out Mesh lensMesh);
                Transform scopeRoot = FindScopeRoot(weapon.transform, lens.transform);
                if (lens is SkinnedMeshRenderer)
                {
                    bakedLens = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                }

                Transform modelRoot = FindModelRoot(weapon);
                var report = new PoseReport(GetPath(weapon.transform, lens.transform), GetPath(weapon.transform, scopeRoot), motion, scale, useConfiguredOffset, aimOffsetPosition);
                foreach (float time in new[] { 0f, 0.25f, 0.5f, 0.75f })
                {
                    motion.Hands.SampleAnimation(hands, motion.Hands.length > 0f ? time % motion.Hands.length : 0f);
                    if (motion.Weapon != null)
                    {
                        motion.Weapon.SampleAnimation(weapon, motion.Weapon.length > 0f ? time % motion.Weapon.length : 0f);
                    }

                    if (modelRoot != null)
                    {
                        modelRoot.localPosition = Vector3.zero;
                        modelRoot.localRotation = Quaternion.identity;
                    }

                    Quaternion rotation = FirstPersonWeaponAsset.ToAimRotation(asset.AimOffsetRotation) * asset.HandsRotation;
                    Vector3 position = aimOffsetPosition + FirstPersonWeaponAsset.ToAimRotation(asset.AimOffsetRotation) * asset.HandsPosition;
                    XRayAnmAsset aim = asset.AimIdle != null ? asset.AimIdle : asset.AimStart != null ? asset.AimStart : asset.AimEnd;
                    if (aim != null)
                    {
                        float aimTime = aim == asset.AimIdle && aim.Duration > 0f ? time % aim.Duration : aim == asset.AimStart ? aim.Duration : 0f;
                        if (aim.Evaluate(aimTime, out Vector3 blendPosition, out Quaternion blendRotation))
                        {
                            position += rotation * blendPosition;
                            rotation *= blendRotation;
                        }
                    }

                    hands.transform.localPosition = position;
                    hands.transform.localRotation = rotation;
                    Bounds bounds = lensMesh.bounds;
                    if (lens is SkinnedMeshRenderer skinned)
                    {
                        skinned.BakeMesh(bakedLens, false);
                        bakedLens.RecalculateBounds();
                        bounds = bakedLens.bounds;
                    }

                    report.Add(new PoseSample(time, camera, handsRoot, scopeRoot, lens.transform, lensMesh.bounds, bounds));
                }

                return JsonUtility.ToJson(report);
            }
            finally
            {
                if (bakedLens != null)
                {
                    UnityEngine.Object.DestroyImmediate(bakedLens);
                }

                if (preview.IsValid())
                {
                    foreach (GameObject root in preview.GetRootGameObjects())
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                    }

                    EditorSceneManager.ClosePreviewScene(preview);
                }
            }
        }

        private static Renderer FindLens(GameObject weapon, out Mesh mesh)
        {
            foreach (Renderer renderer in weapon.GetComponentsInChildren<Renderer>(true))
            {
                Mesh candidate = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>() != null ? renderer.GetComponent<MeshFilter>().sharedMesh : null;
                bool shorty = candidate != null && candidate.name.IndexOf("shorty", StringComparison.OrdinalIgnoreCase) >= 0;
                if (shorty)
                {
                    for (Transform current = renderer.transform; current != null && current != weapon.transform; current = current.parent)
                    {
                        if (current.name == SCOPE_NAME)
                        {
                            mesh = candidate;
                            return renderer;
                        }
                    }
                }
            }

            throw new InvalidOperationException("AUG HUD lacks the authored March F Shorty ocular mesh.");
        }

        private static Vector3 ReadConfiguredAimOffset(FirstPersonWeaponAsset weapon)
        {
            UnityEngine.Object definition = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DEFINITION_PATH);
            if (definition == null)
            {
                throw new InvalidOperationException("March Shorty pose probe requires its existing item definition.");
            }

            var serialized = new SerializedObject(definition);
            SerializedProperty components = serialized.FindProperty("_components");
            if (components == null || !components.isArray)
            {
                throw new InvalidOperationException("March Shorty item definition requires its existing component array.");
            }

            for (int index = 0; index < components.arraySize; index++)
            {
                SerializedProperty component = components.GetArrayElementAtIndex(index);
                SerializedProperty section = component.FindPropertyRelative("_section");
                if (section != null && section.stringValue == SCOPE_NAME)
                {
                    SerializedProperty enabled = component.FindPropertyRelative("_overrideAimOffsetPosition");
                    SerializedProperty position = component.FindPropertyRelative("_aimOffsetPosition");
                    if (enabled == null || position == null)
                    {
                        throw new InvalidOperationException("March Shorty addon requires its existing aim override fields.");
                    }

                    return enabled.boolValue ? position.vector3Value : weapon.AimOffsetPosition;
                }
            }

            throw new InvalidOperationException("March Shorty item definition has no matching scope addon component.");
        }

        private static Transform FindModelRoot(GameObject weapon)
        {
            XRayModel model = weapon.GetComponent<XRayModel>();
            if (model != null)
            {
                foreach (Transform bone in model.Bones)
                {
                    if (bone != null && bone.parent == weapon.transform)
                    {
                        return bone;
                    }
                }
            }

            return null;
        }

        private static Transform FindScopeRoot(Transform weapon, Transform lens)
        {
            Transform result = null;
            for (Transform current = lens.parent; current != null && current != weapon; current = current.parent)
            {
                if (current.name == SCOPE_NAME)
                {
                    result = current;
                }
            }

            if (result == null)
            {
                throw new InvalidOperationException("March ocular requires its existing march_f_shorty wrapper root.");
            }

            return result;
        }

        private static Transform FindBone(Transform root, string name)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string GetPath(Transform root, Transform target)
        {
            string path = target.name;
            for (Transform current = target.parent; current != null && current != root; current = current.parent)
            {
                path = current.name + "/" + path;
            }

            return path;
        }
    }
}
