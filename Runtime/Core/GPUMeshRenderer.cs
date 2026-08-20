using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Renders the GPU Surface Nets mesh via Graphics.RenderPrimitivesIndirect.
    /// Replaces per-chunk MeshFilter+MeshRenderer with a single indirect draw call.
    /// </summary>
    internal class GPUMeshRenderer : MonoBehaviour
    {
        [SerializeField] private Material gpuMeshMaterial;

        private GPUSurfaceNets _surfaceNets;
        private MaterialPropertyBlock _props;
        private bool _ready;
        private Bounds _localBounds;
        private Matrix4x4 _worldFromVolume = Matrix4x4.identity;

        private static readonly int ID_SurfaceVerts = Shader.PropertyToID("_SurfaceVerts");
        private static readonly int ID_SurfaceIndices = Shader.PropertyToID("_SurfaceIndices");
        private static readonly int ID_WorldFromVolume = Shader.PropertyToID("gsWorldFromVolume");
        private static readonly int ID_VolumeFromWorld = Shader.PropertyToID("gsVolumeFromWorld");

        private bool _renderVisible = true;

        /// <summary>
        /// Toggle rendering without disabling the component (which destroys state).
        /// </summary>
        public bool RenderVisible
        {
            get => _renderVisible;
            set => _renderVisible = value;
        }

        public Material GpuMeshMaterial
        {
            get => gpuMeshMaterial;
            set => gpuMeshMaterial = value;
        }

        internal void Initialize(GPUSurfaceNets surfaceNets, Bounds volumeBounds)
        {
            _surfaceNets = surfaceNets;
            _localBounds = volumeBounds;
            _props = new MaterialPropertyBlock();
            _ready = true;
        }

        public void UpdateBounds(Bounds bounds)
        {
            _localBounds = bounds;
        }

        public void SetWorldFromVolume(Matrix4x4 worldFromVolume)
        {
            _worldFromVolume = worldFromVolume;
        }

        private void LateUpdate()
        {
            if (!_ready || !_renderVisible || _surfaceNets == null || gpuMeshMaterial == null)
                return;

            var vertBuf = _surfaceNets.VertexBuffer;
            var idxBuf = _surfaceNets.IndexBuffer;
            var argsBuf = _surfaceNets.DrawIndirectArgs;

            if (vertBuf == null || idxBuf == null || argsBuf == null)
                return;

            _props.SetBuffer(ID_SurfaceVerts, vertBuf);
            _props.SetBuffer(ID_SurfaceIndices, idxBuf);
            _props.SetMatrix(ID_WorldFromVolume, _worldFromVolume);
            _props.SetMatrix(ID_VolumeFromWorld, _worldFromVolume.inverse);

            var rp = new RenderParams(gpuMeshMaterial)
            {
                worldBounds = TransformBounds(_localBounds, _worldFromVolume),
                matProps = _props,
                receiveShadows = false,
                shadowCastingMode = ShadowCastingMode.Off,
                layer = gameObject.layer
            };

            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, argsBuf, 1);
        }

        private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
        {
            Vector3 localExtents = localBounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(localExtents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, localExtents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, localExtents.z));
            var worldExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(matrix.MultiplyPoint3x4(localBounds.center), worldExtents * 2f);
        }

        private void OnDisable()
        {
            _ready = false;
        }
    }
}
