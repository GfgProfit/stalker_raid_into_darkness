using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson
{
    internal sealed class FirstPersonContactGeometry
    {
        private const int COLLIDER_CAPACITY = 48;
        private const int FORWARD_PROBE_COUNT = 4;
        private const int FORWARD_HIT_CAPACITY = 16;
        private const int LEFT_HAND_CALIBRATION_BONE = 2;
        private const float MIN_FRONT_ALIGNMENT = 0.55f;
        private const float MAX_WALL_UP_ALIGNMENT = 0.7f;
        private const float SIDE_DEPTH_SCALE = 2.0f;

        private readonly FirstPersonContactCatalog _catalog;
        private readonly IReadOnlyDictionary<Collider, float> _ignoredColliders;
        private readonly Collider[] _nearby = new Collider[COLLIDER_CAPACITY];
        private readonly RaycastHit[] _forwardHits = new RaycastHit[FORWARD_HIT_CAPACITY];
        private readonly FirstPersonContactBox[] _forwardBoxes = new FirstPersonContactBox[FORWARD_PROBE_COUNT];
        private readonly float[] _forwardDepths = new float[FORWARD_PROBE_COUNT];
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<FirstPersonContactBox> _boxes = new List<FirstPersonContactBox>();
        private readonly List<FirstPersonContactBox> _active = new List<FirstPersonContactBox>();
        private readonly HashSet<string> _missing = new HashSet<string>();

        private BoxCollider _query;

        public FirstPersonContactGeometry(FirstPersonContactCatalog catalog, IReadOnlyDictionary<Collider, float> ignoredColliders)
        {
            _catalog = catalog;
            _ignoredColliders = ignoredColliders;
        }

        public void Bind(GameObject hands, GameObject weapon)
        {
            _renderers.Clear();
            _boxes.Clear();
            _active.Clear();
            if (hands == null || weapon == null || !hands.TryGetComponent(out XRayModel handModel))
            {
                return;
            }

            Transform leftHand = handModel.FindBone("l_hand");
            string handKey = FirstPersonContactCatalog.HandsKey(handModel);
            if (leftHand != null && _catalog.TryGet(handKey, out FirstPersonCollisionShapeData[] arms))
            {
                foreach (FirstPersonCollisionShapeData arm in arms)
                {
                    if (arm.Bone == LEFT_HAND_CALIBRATION_BONE)
                    {
                        _boxes.Add(new FirstPersonContactBox(leftHand, arm.Bounds, null));
                    }
                }
            }
            else
            {
                ReportMissing(handKey, hands);
            }

            weapon.GetComponentsInChildren(true, _renderers);
            foreach (Renderer renderer in _renderers)
            {
                XRayModel owner = renderer.GetComponentInParent<XRayModel>(true);
                if (owner == handModel)
                {
                    owner = null;
                }

                var skin = renderer as SkinnedMeshRenderer;
                Mesh mesh = skin != null ? skin.sharedMesh : renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                string key = FirstPersonContactCatalog.MeshKey(owner, mesh);
                if (!_catalog.TryGet(key, out FirstPersonCollisionShapeData[] shapes))
                {
                    ReportMissing(key, renderer);
                    continue;
                }

                Transform[] bones = skin != null ? skin.bones : null;
                foreach (FirstPersonCollisionShapeData shape in shapes)
                {
                    Transform bone = shape.Bone >= 0 && bones != null && shape.Bone < bones.Length ? bones[shape.Bone] : renderer.transform;
                    if (bone != null)
                    {
                        _boxes.Add(new FirstPersonContactBox(bone, shape.Bounds, renderer));
                    }
                }
            }
        }

        public bool TryFindObstacle(PhysicsScene scene, int mask, Transform actor, Vector3 origin, Vector3 forward, float length, float lead, out float distance)
        {
            distance = length + lead;
            _active.Clear();
            Bounds combined = default;
            foreach (FirstPersonContactBox box in _boxes)
            {
                if (!box.Capture())
                {
                    continue;
                }

                if (_active.Count == 0)
                {
                    combined = box.Bounds;
                }
                else
                {
                    combined.Encapsulate(box.Bounds);
                }

                _active.Add(box);
            }

            if (_active.Count == 0)
            {
                return false;
            }

            Vector3 sweep = forward * lead;
            combined.Encapsulate(new Bounds(combined.center + sweep, combined.size));
            int count = scene.OverlapBox(combined.center, combined.extents, _nearby, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            int nearbyCount = count;
            for (int i = count - 1; i >= 0; i--)
            {
                if (IsObstacle(_nearby[i], actor) == false)
                {
                    _nearby[i] = _nearby[--count];
                }
            }

            bool contact = false;
            if (count > 0)
            {
                contact = FindPenetration(actor, origin, forward, length, count, ref distance);
                if (!contact)
                {
                    float gap = FindForwardDistance(scene, mask, actor, origin, forward, sweep, lead, count);
                    distance = length + gap;
                    contact = gap < lead;
                }
            }

            System.Array.Clear(_nearby, 0, nearbyCount);
            return contact;
        }

        private bool FindPenetration(Transform actor, Vector3 origin, Vector3 forward, float length, int count, ref float distance)
        {
            bool contact = false;
            foreach (FirstPersonContactBox box in _active)
            {
                bool queryReady = false;
                for (int i = 0; i < count; i++)
                {
                    Collider obstacle = _nearby[i];
                    if (!box.Bounds.Intersects(obstacle.bounds))
                    {
                        continue;
                    }

                    if (!queryReady)
                    {
                        EnsureQuery(actor);
                        _query.size = box.Extents * 2.0f;
                        queryReady = true;
                    }

                    Transform target = obstacle.transform;
                    if (Physics.ComputePenetration(_query, box.Center, box.Rotation, obstacle, target.position, target.rotation, out Vector3 normal, out float depth) && depth > 0.0f)
                    {
                        float obstacleDistance = length - depth * SIDE_DEPTH_SCALE;
                        if (!box.IsHand && IsInFront(normal, forward))
                        {
                            Vector3 surface = box.SupportAgainst(normal) + normal * depth;
                            obstacleDistance = Mathf.Min(obstacleDistance, Vector3.Dot(surface - origin, forward));
                        }

                        distance = Mathf.Min(distance, Mathf.Clamp(obstacleDistance, 0.0f, length));
                        contact = true;
                    }
                }
            }

            return contact;
        }

        public void Dispose()
        {
            Bind(null, null);
            _missing.Clear();
            if (_query != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_query.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(_query.gameObject);
                }

                _query = null;
            }
        }

        private float FindForwardDistance(PhysicsScene scene, int mask, Transform actor, Vector3 origin, Vector3 forward, Vector3 sweep, float lead, int nearbyCount)
        {
            System.Array.Clear(_forwardBoxes, 0, _forwardBoxes.Length);
            foreach (FirstPersonContactBox box in _active)
            {
                if (!IsForwardCandidate(box.Bounds, sweep, nearbyCount))
                {
                    continue;
                }

                float depth = Vector3.Dot(box.SupportAgainst(-forward) - origin, forward);
                for (int i = 0; i < _forwardBoxes.Length; i++)
                {
                    if (_forwardBoxes[i] != null && depth <= _forwardDepths[i])
                    {
                        continue;
                    }

                    for (int j = _forwardBoxes.Length - 1; j > i; j--)
                    {
                        _forwardBoxes[j] = _forwardBoxes[j - 1];
                        _forwardDepths[j] = _forwardDepths[j - 1];
                    }

                    _forwardBoxes[i] = box;
                    _forwardDepths[i] = depth;
                    break;
                }
            }

            float distance = lead;
            foreach (FirstPersonContactBox box in _forwardBoxes)
            {
                if (box == null)
                {
                    break;
                }

                int hitCount = scene.BoxCast(box.Center, box.Extents, forward, _forwardHits, box.Rotation, distance, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = _forwardHits[i];
                    if (hit.distance > 0.0f && IsObstacle(hit.collider, actor) && IsInFront(hit.normal, forward))
                    {
                        distance = Mathf.Min(distance, hit.distance);
                    }
                }
            }

            return distance;
        }

        private bool IsObstacle(Collider collider, Transform actor)
        {
            return collider != null && (_ignoredColliders.TryGetValue(collider, out float until) == false || until <= Time.time) && collider.transform.IsChildOf(actor) == false;
        }

        private static bool IsInFront(Vector3 normal, Vector3 forward)
        {
            return Mathf.Abs(normal.y) <= MAX_WALL_UP_ALIGNMENT && Vector3.Dot(normal, forward) <= -MIN_FRONT_ALIGNMENT;
        }

        private bool IsForwardCandidate(Bounds bounds, Vector3 sweep, int nearbyCount)
        {
            Bounds detection = bounds;
            detection.Encapsulate(new Bounds(bounds.center + sweep, bounds.size));
            for (int i = 0; i < nearbyCount; i++)
            {
                if (detection.Intersects(_nearby[i].bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureQuery(Transform actor)
        {
            if (_query != null)
            {
                return;
            }

            var query = new GameObject("First Person Collision Query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            SceneManager.MoveGameObjectToScene(query, actor.gameObject.scene);
            query.transform.position = Vector3.down * 10000.0f;
            _query = query.AddComponent<BoxCollider>();
            _query.isTrigger = true;
            _query.excludeLayers = ~0;
            _query.layerOverridePriority = int.MaxValue;
        }

        private void ReportMissing(string key, Object context)
        {
            if (_missing.Add(key))
            {
                Debug.LogWarning($"First-person collision calibration missing for '{key}'. Rebuild the contact catalog.", context);
            }
        }
    }
}
