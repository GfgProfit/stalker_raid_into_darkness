using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public sealed class MonolithOriginalTbnSkinning : MonoBehaviour
    {
        private static readonly int _boneRowsId = Shader.PropertyToID("_MonolithBoneRows");
        private static readonly int _boneCountId = Shader.PropertyToID("_MonolithBoneCount");
        private static readonly int _activeId = Shader.PropertyToID("_MonolithSkinningActive");
        private static GraphicsBuffer _fallbackBuffer;
        private static bool _shuttingDown;

        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private Transform[] _bones;
        private Matrix4x4[] _bindposes;
        private Matrix4x4[] _previous;
        private Vector4[] _rows;
        private GraphicsBuffer _buffer;
        private MaterialPropertyBlock _properties;
        private bool _rejected;
        private bool _uploaded;

        public int BufferBytes => _rows != null ? _rows.Length * 16 : 0;
        public int UploadCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeFallback()
        {
            if (_shuttingDown || _fallbackBuffer != null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            _fallbackBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);
            _fallbackBuffer.SetData(new[] { new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0) });
            Shader.SetGlobalBuffer(_boneRowsId, _fallbackBuffer);
            Application.quitting -= ReleaseSharedResources;
            Application.quitting += ReleaseSharedResources;
        }

        public static void ReleaseSharedResources()
        {
            _shuttingDown = true;
            if (_fallbackBuffer != null) _fallbackBuffer.Release();
            _fallbackBuffer = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedResources()
        {
            ReleaseSharedResources();
            _shuttingDown = false;
        }

        public bool RefreshSkinning()
        {
            if (_renderer == null) _renderer = GetComponent<SkinnedMeshRenderer>();
            if (_mesh != _renderer.sharedMesh)
            {
                ReleaseBuffer();
                _mesh = _renderer.sharedMesh;
                _rejected = false;
            }
            if (_mesh == null || _rejected) return false;
            if (_buffer == null && !InitializeBuffer()) return false;
            bool changed = !_uploaded;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null)
                {
                    Reject("A required bone was destroyed");
                    return false;
                }
                Matrix4x4 matrix = _bones[i].localToWorldMatrix * _bindposes[i];
                if (_previous[i].Equals(matrix) && _uploaded) continue;
                _previous[i] = matrix;
                _rows[i * 3] = matrix.GetRow(0);
                _rows[i * 3 + 1] = matrix.GetRow(1);
                _rows[i * 3 + 2] = matrix.GetRow(2);
                changed = true;
            }
            if (changed)
            {
                _buffer.SetData(_rows);
                UploadCount++;
                _uploaded = true;
            }
            _renderer.GetPropertyBlock(_properties);
            _properties.SetBuffer(_boneRowsId, _buffer);
            _properties.SetFloat(_boneCountId, _bones.Length);
            _properties.SetFloat(_activeId, 1);
            _renderer.SetPropertyBlock(_properties);
            return true;
        }

        private bool InitializeBuffer()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            if (!_mesh.isReadable || _mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1) != 2 || _mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord2) != 2 || _mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord3) != 2)
            {
                Reject("A readable full Original TBN vertex stream is required");
                return false;
            }
            _bones = _renderer.bones;
            _bindposes = _mesh.bindposes;
            if (!_mesh.isReadable || _bones.Length == 0 || _bones.Length != _bindposes.Length)
            {
                Reject("Use the isolated readable 1L mesh with unchanged renderer bones and bindposes");
                return false;
            }
            BoneWeight[] weights = _mesh.boneWeights;
            Color[] colors = _mesh.colors;
            if (weights.Length != _mesh.vertexCount || colors.Length != weights.Length)
            {
                Reject("Original TBN attributes and bone weights are missing");
                return false;
            }
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight weight = weights[i];
                if (weight.weight0 != 1 || weight.weight1 != 0 || weight.weight2 != 0 || weight.weight3 != 0 || weight.boneIndex0 < 0 || weight.boneIndex0 >= _bones.Length || colors[i].a != weight.boneIndex0 + 2)
                {
                    Reject("Only exact 1L weights and the matching COLOR.a bone index are supported");
                    return false;
                }
            }
            _previous = new Matrix4x4[_bones.Length];
            _rows = new Vector4[_bones.Length * 3];
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _rows.Length, 16);
            if (_properties == null) _properties = new MaterialPropertyBlock();
            _uploaded = false;
            return true;
        }

        private void Reject(string reason)
        {
            _rejected = true;
            ReleaseBuffer();
            Debug.LogWarning(nameof(MonolithOriginalTbnSkinning) + ": " + reason + "; using Legacy TBN", this);
        }

        private void OnEnable()
        {
            _rejected = false;
            InitializeFallback();
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        private void LateUpdate()
        {
            RefreshSkinning();
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_renderer == null) _renderer = GetComponent<SkinnedMeshRenderer>();
            if (_renderer.enabled && (camera.cullingMask & (1 << gameObject.layer)) != 0) RefreshSkinning();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            ReleaseBuffer();
        }

        private void ReleaseBuffer()
        {
            if (_renderer != null)
            {
                if (_properties == null) _properties = new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(_properties);
                _properties.SetFloat(_activeId, 0);
                _properties.SetFloat(_boneCountId, 0);
                InitializeFallback();
                _properties.SetBuffer(_boneRowsId, _fallbackBuffer);
                _renderer.SetPropertyBlock(_properties);
            }
            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }
            _rows = null;
            _uploaded = false;
        }
    }
}
