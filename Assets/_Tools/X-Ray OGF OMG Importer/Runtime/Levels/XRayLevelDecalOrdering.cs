using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    public sealed class XRayLevelDecalOrdering : MonoBehaviour
    {
        [SerializeField] private MeshRenderer[] _renderers;

        private int[] _indices;
        private int[] _originalPriorities;
        private float[] _distances;

        public void Initialize(MeshRenderer[] renderers)
        {
            Restore();
            _renderers = renderers;
            Prepare();
        }

        public void Order(Camera camera)
        {
            if (camera == null || _renderers == null)
            {
                return;
            }

            if (_indices == null || _indices.Length != _renderers.Length)
            {
                Prepare();
            }

            Vector3 eye = camera.transform.position;
            for (int i = 0; i < _renderers.Length; i++)
            {
                _distances[i] = _renderers[i] != null ? (eye - _renderers[i].bounds.center).sqrMagnitude : -1.0f;
                _indices[i] = i;
            }

            for (int i = 1; i < _indices.Length; i++)
            {
                int index = _indices[i];
                int j = i - 1;
                while (j >= 0 && _distances[_indices[j]] < _distances[index])
                {
                    _indices[j + 1] = _indices[j];
                    j--;
                }

                _indices[j + 1] = index;
            }

            for (int i = 0; i < _indices.Length; i++)
            {
                MeshRenderer renderer = _renderers[_indices[i]];
                if (renderer != null && renderer.rendererPriority != i)
                {
                    renderer.rendererPriority = i;
                }
            }
        }

        private void OnEnable()
        {
            Prepare();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            Restore();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            Order(camera);
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!Application.isPlaying)
            {
                Restore();
            }
        }

        private void Prepare()
        {
            int count = _renderers?.Length ?? 0;
            _indices = new int[count];
            _originalPriorities = new int[count];
            _distances = new float[count];
            for (int i = 0; i < count; i++)
            {
                _originalPriorities[i] = _renderers[i] != null ? _renderers[i].rendererPriority : 0;
            }
        }

        private void Restore()
        {
            if (_renderers == null || _originalPriorities == null)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].rendererPriority = _originalPriorities[i];
                }
            }
        }
    }
}
