using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class AnomalyContactEffects : MonoBehaviour
    {
        [SerializeField] private GameObject _template;
        [SerializeField] private int _rootIndex;
        [SerializeField] private int[] _stopEffectIndices;
        [SerializeField] private float[] _stopTimes;
        [SerializeField, Min(0f)] private float _groupDuration = 3f;
        [SerializeField] private bool _entranceEnabled;
        [SerializeField] private bool _entranceSmallOnly;
        [SerializeField, Min(0f)] private float _smallObjectRadius = 0.6f;

        private readonly List<ContactInstance> _instances = new List<ContactInstance>();

        public void PlayEntrance(Transform target)
        {
            if (CanPlay(target) == false || _entranceEnabled == false)
            {
                return;
            }

            SkinnedMeshRenderer renderer = FindLargestRenderer(target);
            if (_entranceSmallOnly && EstimateRadius(target, renderer) >= _smallObjectRadius)
            {
                return;
            }
            Transform anchor = ChooseAnchor(target, renderer);
            Vector3 direction = Vector3.up;
            CharacterController controller = target.GetComponent<CharacterController>();
            if (controller != null && controller.velocity.sqrMagnitude > 0.000001f)
            {
                direction = controller.velocity.normalized;
            }
            else
            {
                Rigidbody body = target.GetComponent<Rigidbody>();
                if (body != null && body.linearVelocity.sqrMagnitude > 0.000001f)
                {
                    direction = body.linearVelocity.normalized;
                }
            }
            Create(target, anchor, false, Quaternion.FromToRotation(Vector3.forward, direction));
        }

        public void PlayHit(Transform target)
        {
            if (CanPlay(target) == false)
            {
                return;
            }

            Transform anchor = ChooseAnchor(target, FindLargestRenderer(target));
            for (int index = _instances.Count - 1; index >= 0; index--)
            {
                ContactInstance instance = _instances[index];
                if (instance.Follow && instance.Target == target && instance.Anchor == anchor)
                {
                    if (instance.Playback.Tick(0f))
                    {
                        return;
                    }
                    Release(index);
                }
            }
            Create(target, anchor, true, Quaternion.FromToRotation(Vector3.forward, Vector3.up));
        }

        public void StopTarget(Transform target)
        {
            for (int index = _instances.Count - 1; index >= 0; index--)
            {
                if (_instances[index].Target == target)
                {
                    Release(index);
                }
            }
        }

        public void Clear()
        {
            for (int index = _instances.Count - 1; index >= 0; index--)
            {
                Release(index);
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        private void LateUpdate()
        {
            for (int index = _instances.Count - 1; index >= 0; index--)
            {
                ContactInstance instance = _instances[index];
                if (instance.Target == null || instance.Target.gameObject.activeInHierarchy == false || (instance.Follow && instance.Anchor == null) || instance.Playback.Root == null)
                {
                    Release(index);
                    continue;
                }
                Vector3 position = instance.Follow ? instance.Anchor.position : instance.Position;
                instance.Playback.Root.transform.SetPositionAndRotation(position, instance.Rotation);
                if (instance.Playback.Tick(Time.deltaTime) == false)
                {
                    Release(index);
                }
            }
        }

        private bool CanPlay(Transform target)
        {
            return isActiveAndEnabled && _template != null && target != null && target.gameObject.activeInHierarchy;
        }

        private void Create(Transform target, Transform anchor, bool follow, Quaternion rotation)
        {
            Vector3 position = anchor.position;
            GameObject root = Instantiate(_template, position, rotation);
            root.name = "AnomalyContactEffect";
            if (gameObject.scene.IsValid() && root.scene != gameObject.scene)
            {
                SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            }
            AnomalyParticlePlayback playback = new AnomalyParticlePlayback(root, _rootIndex, _stopEffectIndices, _stopTimes, _groupDuration);
            if (playback.IsValid == false || playback.Play() == false)
            {
                playback.Stop();
                Destroy(root);
                Debug.LogError("Anomaly contact template has invalid ParticleSystem bindings.", this);
                return;
            }
            _instances.Add(new ContactInstance(playback, target, anchor, position, rotation, follow));
        }

        private void Release(int index)
        {
            AnomalyParticlePlayback playback = _instances[index].Playback;
            playback.Stop();
            if (playback.Root != null)
            {
                Destroy(playback.Root);
            }
            _instances.RemoveAt(index);
        }

        private static SkinnedMeshRenderer FindLargestRenderer(Transform target)
        {
            SkinnedMeshRenderer selected = null;
            float largest = -1f;
            foreach (SkinnedMeshRenderer renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }
                float size = renderer.bounds.extents.sqrMagnitude;
                if (size > largest)
                {
                    largest = size;
                    selected = renderer;
                }
            }
            return selected;
        }

        private static float EstimateRadius(Transform target, SkinnedMeshRenderer renderer)
        {
            if (renderer != null && renderer.bounds.extents.sqrMagnitude > 0f)
            {
                return renderer.bounds.extents.magnitude;
            }

            Collider rootCollider = target.GetComponent<Collider>();
            if (rootCollider != null)
            {
                return rootCollider.bounds.extents.magnitude;
            }
            float radius = 0f;
            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                if (collider != null)
                {
                    radius = Mathf.Max(radius, collider.bounds.extents.magnitude);
                }
            }
            return radius;
        }

        private static Transform ChooseAnchor(Transform target, SkinnedMeshRenderer renderer)
        {
            if (renderer != null)
            {
                Transform[] bones = renderer.bones;
                if (bones.Length > 0)
                {
                    int first = Random.Range(0, bones.Length);
                    for (int index = 0; index < bones.Length; index++)
                    {
                        Transform bone = bones[(first + index) % bones.Length];
                        if (bone != null && bone.IsChildOf(target))
                        {
                            return bone;
                        }
                    }
                }
            }
            return target;
        }

        private sealed class ContactInstance
        {
            public AnomalyParticlePlayback Playback { get; }
            public Transform Target { get; }
            public Transform Anchor { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public bool Follow { get; }

            public ContactInstance(AnomalyParticlePlayback playback, Transform target, Transform anchor, Vector3 position, Quaternion rotation, bool follow)
            {
                Playback = playback;
                Target = target;
                Anchor = anchor;
                Position = position;
                Rotation = rotation;
                Follow = follow;
            }
        }
    }
}
