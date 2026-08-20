using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    internal readonly struct GpuSurfaceNetsMemoryPlan
    {
        internal readonly int TotalVoxels;
        internal readonly int MaximumVertices;
        internal readonly int MaximumIndices;
        internal readonly long CoordMapBytes;
        internal readonly long VertexBytes;
        internal readonly long IndexBytes;
        internal readonly long SmoothBufferBytes;
        internal readonly long TemporalTextureBytes;

        internal GpuSurfaceNetsMemoryPlan(int totalVoxels, int maximumVertices,
            int maximumIndices, long coordMapBytes, long vertexBytes, long indexBytes,
            long smoothBufferBytes, long temporalTextureBytes)
        {
            TotalVoxels = totalVoxels;
            MaximumVertices = maximumVertices;
            MaximumIndices = maximumIndices;
            CoordMapBytes = coordMapBytes;
            VertexBytes = vertexBytes;
            IndexBytes = indexBytes;
            SmoothBufferBytes = smoothBufferBytes;
            TemporalTextureBytes = temporalTextureBytes;
        }

        internal long MaximumStorageBufferBytes => Math.Max(CoordMapBytes,
            Math.Max(VertexBytes, Math.Max(IndexBytes, SmoothBufferBytes)));
        internal long TotalBytes => CoordMapBytes + VertexBytes + IndexBytes +
                                    SmoothBufferBytes * 2L + TemporalTextureBytes +
                                    40L;
    }

    internal class GPUSurfaceNets : IDisposable
    {
        private readonly ComputeShader _compute;

        // Kernel indices
        private readonly int _kClearCounters;
        private readonly int _kClassifyAndEmit;
        private readonly int _kBuildVertexDispatchArgs;
        private readonly int _kInitSmooth;
        private readonly int _kSmoothVertices;
        private readonly int _kApplySmooth;
        private readonly int _kTemporalBlend;
        private readonly int _kGenerateIndices;
        private readonly int _kBuildIndirectArgs;
        private readonly int _kInitTemporal;

        // GPU buffers
        private GraphicsBuffer _coordVertMap;
        private GraphicsBuffer _vertices;
        private GraphicsBuffer _indices;
        private GraphicsBuffer _counters;
        private GraphicsBuffer _dispatchArgs;
        private GraphicsBuffer _drawIndirectArgs;
        private GraphicsBuffer _smoothPosA;
        private GraphicsBuffer _smoothPosB;

        // Temporal state is a storage image, not a storage buffer. The Quest-reported
        // 128 MiB maxStorageBufferRange therefore does not apply to this texture.
        private RenderTexture _temporalState;

        // Sizing
        private int3 _voxCount;
        private int _totalVoxels;
        private int _maxVertices;
        private int _maxIndices;
        private bool _temporalInitialized;

        public float MinMeshWeight { get; set; } = 0.08f;
        public int SmoothIterations { get; set; } = 1;
        public float SmoothLambda { get; set; } = 0.33f;
        public float SmoothBeta { get; set; } = 0.5f;
        public float TemporalAlphaMax { get; set; } = 0.85f;
        public float TemporalAlphaMin { get; set; } = 0.1f;
        public float TemporalDecayRate { get; set; } = 0.15f;
        public float ConvergenceThreshold { get; set; } = 0.005f;
        public float TemporalDeadzone { get; set; } = 0.001f;

        public GraphicsBuffer VertexBuffer => _vertices;
        public GraphicsBuffer IndexBuffer => _indices;
        public GraphicsBuffer DrawIndirectArgs => _drawIndirectArgs;
        public GraphicsBuffer CountersBuffer => _counters;

        private static readonly int ID_TsdfVolume = Shader.PropertyToID("_TsdfVolume");
        private static readonly int ID_ColorVolume = Shader.PropertyToID("_ColorVolume");
        private static readonly int ID_VoxCount = Shader.PropertyToID("_VoxCount");
        private static readonly int ID_VoxSize = Shader.PropertyToID("_VoxSize");
        private static readonly int ID_MinWeight = Shader.PropertyToID("_MinWeight");
        private static readonly int ID_TotalVoxels = Shader.PropertyToID("_TotalVoxels");
        private static readonly int ID_MaxVertices = Shader.PropertyToID("_MaxVertices");
        private static readonly int ID_SmoothLambda = Shader.PropertyToID("_SmoothLambda");
        private static readonly int ID_SmoothBeta = Shader.PropertyToID("_SmoothBeta");
        private static readonly int ID_TemporalAlphaMax = Shader.PropertyToID("_TemporalAlphaMax");
        private static readonly int ID_TemporalAlphaMin = Shader.PropertyToID("_TemporalAlphaMin");
        private static readonly int ID_TemporalDecayRate = Shader.PropertyToID("_TemporalDecayRate");
        private static readonly int ID_ConvergeThreshold = Shader.PropertyToID("_ConvergeThreshold");
        private static readonly int ID_TemporalDeadzone = Shader.PropertyToID("_TemporalDeadzone");

        private static readonly int ID_CoordVertMap = Shader.PropertyToID("_CoordVertMap");
        private static readonly int ID_Vertices = Shader.PropertyToID("_Vertices");
        private static readonly int ID_Indices = Shader.PropertyToID("_Indices");
        private static readonly int ID_Counters = Shader.PropertyToID("_Counters");
        private static readonly int ID_DispatchArgs = Shader.PropertyToID("_DispatchArgs");
        private static readonly int ID_DrawIndirectArgs = Shader.PropertyToID("_DrawIndirectArgs");
        private static readonly int ID_SmoothPosA = Shader.PropertyToID("_SmoothPosA");
        private static readonly int ID_SmoothPosB = Shader.PropertyToID("_SmoothPosB");
        private static readonly int ID_TemporalState = Shader.PropertyToID("_TemporalState");

        private const int VertexStride = 32;
        private const int Float3Stride = 12;

        public GPUSurfaceNets(ComputeShader compute)
        {
            _compute = compute;

            _kClearCounters = compute.FindKernel("ClearCounters");
            _kClassifyAndEmit = compute.FindKernel("ClassifyAndEmit");
            _kBuildVertexDispatchArgs = compute.FindKernel("BuildVertexDispatchArgs");
            _kInitSmooth = compute.FindKernel("InitSmooth");
            _kSmoothVertices = compute.FindKernel("SmoothVertices");
            _kApplySmooth = compute.FindKernel("ApplySmooth");
            _kTemporalBlend = compute.FindKernel("TemporalBlend");
            _kGenerateIndices = compute.FindKernel("GenerateIndices");
            _kBuildIndirectArgs = compute.FindKernel("BuildIndirectArgs");
            _kInitTemporal = compute.FindKernel("InitTemporal");
        }

        public void EnsureBuffers(int3 voxCount, float vertexBudgetPercent = 0.05f)
        {
            if (!TryCreateMemoryPlan(voxCount, vertexBudgetPercent,
                    out GpuSurfaceNetsMemoryPlan plan))
                throw new ArgumentOutOfRangeException(nameof(voxCount),
                    "Surface Nets dimensions or vertex budget overflow GPU buffer counts.");
            if (plan.MaximumStorageBufferBytes >
                VolumeIntegrator.MaximumStorageBufferRangeBytes)
                throw new InvalidOperationException(
                    $"Surface Nets storage buffer plan exceeds Quest Vulkan limit: " +
                    $"largest={plan.MaximumStorageBufferBytes}, limit=" +
                    $"{VolumeIntegrator.MaximumStorageBufferRangeBytes} bytes.");

            if (_totalVoxels == plan.TotalVoxels &&
                _maxVertices == plan.MaximumVertices && _coordVertMap != null)
                return;

            Dispose();

            _voxCount = voxCount;
            _totalVoxels = plan.TotalVoxels;
            _maxVertices = plan.MaximumVertices;
            _maxIndices = plan.MaximumIndices;

            const GraphicsBuffer.Target structuredIndirect =
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments;

            _coordVertMap = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                _totalVoxels, 4);
            _vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxVertices, VertexStride);
            _indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxIndices, 4);
            _counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 4);
            _dispatchArgs = new GraphicsBuffer(structuredIndirect, 3, 4);
            _drawIndirectArgs = new GraphicsBuffer(structuredIndirect, 5, 4);
            _smoothPosA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxVertices, Float3Stride);
            _smoothPosB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxVertices, Float3Stride);

            // 256^3 x RGBA32Float = 256 MiB. This is legal on the measured Quest device
            // (1 GiB maxMemoryAllocationSize) and retains the upstream float precision.
            _temporalState = new RenderTexture(voxCount.x, voxCount.y, 0, GraphicsFormat.R32G32B32A32_SFloat)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = voxCount.z,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _temporalState.Create();

            _temporalInitialized = false;

            Logger.Info($"[GPUSurfaceNets] Allocated buffers: vox={voxCount}, " +
                      $"maxVerts={_maxVertices}, maxIdx={_maxIndices}, " +
                      $"maxStorageBuffer={plan.MaximumStorageBufferBytes / (1024 * 1024)}MiB, " +
                      $"temporalTexture={plan.TemporalTextureBytes / (1024 * 1024)}MiB, " +
                      $"totalGPU={plan.TotalBytes / (1024 * 1024)}MiB");
        }

        internal static bool TryCreateMemoryPlan(int3 voxCount,
            float vertexBudgetPercent, out GpuSurfaceNetsMemoryPlan plan)
        {
            plan = default;
            if (voxCount.x <= 0 || voxCount.y <= 0 || voxCount.z <= 0 ||
                float.IsNaN(vertexBudgetPercent) ||
                float.IsInfinity(vertexBudgetPercent) || vertexBudgetPercent <= 0f)
                return false;
            try
            {
                long voxelLong = checked((long)voxCount.x * voxCount.y * voxCount.z);
                if (voxelLong > int.MaxValue)
                    return false;
                long vertexLong = Math.Max(1024L,
                    (long)(voxelLong * (double)vertexBudgetPercent));
                long indexLong = checked(vertexLong * 18L);
                if (vertexLong > int.MaxValue || indexLong > int.MaxValue)
                    return false;

                long coordBytes = checked(voxelLong * 4L);
                long vertexBytes = checked(vertexLong * VertexStride);
                long indexBytes = checked(indexLong * 4L);
                long smoothBytes = checked(vertexLong * Float3Stride);
                long temporalBytes = checked(voxelLong * 16L);
                plan = new GpuSurfaceNetsMemoryPlan((int)voxelLong, (int)vertexLong,
                    (int)indexLong, coordBytes, vertexBytes, indexBytes, smoothBytes,
                    temporalBytes);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        public void InitTemporalState()
        {
            if (_temporalInitialized || _temporalState == null) return;

            _compute.SetTexture(_kInitTemporal, ID_TemporalState, _temporalState);
            _compute.SetInts(ID_VoxCount, _voxCount.x, _voxCount.y, _voxCount.z);
            int gx = CeilDiv(_voxCount.x, 4);
            int gy = CeilDiv(_voxCount.y, 4);
            int gz = CeilDiv(_voxCount.z, 4);
            _compute.Dispatch(_kInitTemporal, gx, gy, gz);
            _temporalInitialized = true;
        }

        /// <summary>
        /// Clears history keyed by local voxel coordinate. A reused TSDF buffer may represent
        /// a different chunk at the same coordinates, so carrying history across a rollover
        /// would blend unrelated walls and recreate stale gray geometry.
        /// </summary>
        public void ResetTemporalState()
        {
            if (_temporalState == null)
                return;
            _temporalInitialized = false;
            InitTemporalState();
        }

        public void Extract(RenderTexture tsdfVolume, RenderTexture colorVolume, float voxelSize)
        {
            if (_coordVertMap == null)
                throw new InvalidOperationException("Call EnsureBuffers before Extract");

            if (!_temporalInitialized && TemporalAlphaMax < 1f)
                InitTemporalState();

            SetGlobalParams(voxelSize);
            BindAllBuffers();

            _compute.SetTexture(_kClassifyAndEmit, ID_TsdfVolume, tsdfVolume);
            _compute.SetTexture(_kClassifyAndEmit, ID_ColorVolume, colorVolume);
            _compute.SetTexture(_kGenerateIndices, ID_TsdfVolume, tsdfVolume);

            // 1. Clear counters
            _compute.Dispatch(_kClearCounters, 1, 1, 1);

            // 2. Classify & emit vertices
            int gx = CeilDiv(_voxCount.x, 4);
            int gy = CeilDiv(_voxCount.y, 4);
            int gz = CeilDiv(_voxCount.z, 4);
            _compute.Dispatch(_kClassifyAndEmit, gx, gy, gz);

            // 3. Build dispatch args from vertex count
            _compute.Dispatch(_kBuildVertexDispatchArgs, 1, 1, 1);

            // 4. Smoothing (optional)
            if (SmoothIterations > 0)
            {
                _compute.DispatchIndirect(_kInitSmooth, _dispatchArgs);

                for (int iter = 0; iter < SmoothIterations; iter++)
                {
                    if (iter % 2 == 0)
                    {
                        _compute.SetBuffer(_kSmoothVertices, ID_SmoothPosA, _smoothPosA);
                        _compute.SetBuffer(_kSmoothVertices, ID_SmoothPosB, _smoothPosB);
                    }
                    else
                    {
                        _compute.SetBuffer(_kSmoothVertices, ID_SmoothPosA, _smoothPosB);
                        _compute.SetBuffer(_kSmoothVertices, ID_SmoothPosB, _smoothPosA);
                    }
                    _compute.DispatchIndirect(_kSmoothVertices, _dispatchArgs);
                }

                if (SmoothIterations % 2 == 0)
                    _compute.SetBuffer(_kApplySmooth, ID_SmoothPosA, _smoothPosA);
                else
                    _compute.SetBuffer(_kApplySmooth, ID_SmoothPosA, _smoothPosB);

                _compute.DispatchIndirect(_kApplySmooth, _dispatchArgs);
            }

            // 5. Temporal blend (optional)
            if (TemporalAlphaMax < 1f)
            {
                _compute.SetTexture(_kTemporalBlend, ID_TemporalState, _temporalState);
                _compute.DispatchIndirect(_kTemporalBlend, _dispatchArgs);
            }

            // 6. Generate indices
            _compute.DispatchIndirect(_kGenerateIndices, _dispatchArgs);

            // 7. Build draw indirect args
            _compute.Dispatch(_kBuildIndirectArgs, 1, 1, 1);
        }

        public Bounds GetVolumeBounds(float voxelSize)
        {
            float3 halfExtent = (float3)_voxCount * voxelSize * 0.5f;
            return new Bounds(Vector3.zero, (Vector3)(halfExtent * 2));
        }

        private void SetGlobalParams(float voxelSize)
        {
            _compute.SetInts(ID_VoxCount, _voxCount.x, _voxCount.y, _voxCount.z);
            _compute.SetFloat(ID_VoxSize, voxelSize);
            _compute.SetFloat(ID_MinWeight, MinMeshWeight);
            _compute.SetInt(ID_TotalVoxels, _totalVoxels);
            _compute.SetInt(ID_MaxVertices, _maxVertices);
            _compute.SetFloat(ID_SmoothLambda, SmoothLambda);
            _compute.SetFloat(ID_SmoothBeta, SmoothBeta);
            _compute.SetFloat(ID_TemporalAlphaMax, TemporalAlphaMax);
            _compute.SetFloat(ID_TemporalAlphaMin, TemporalAlphaMin);
            _compute.SetFloat(ID_TemporalDecayRate, TemporalDecayRate);
            _compute.SetFloat(ID_ConvergeThreshold, ConvergenceThreshold);
            _compute.SetFloat(ID_TemporalDeadzone, TemporalDeadzone);
        }

        private void BindAllBuffers()
        {
            BindBuffer(_kClearCounters, ID_Counters, _counters);

            BindBuffer(_kClassifyAndEmit, ID_CoordVertMap, _coordVertMap);
            BindBuffer(_kClassifyAndEmit, ID_Vertices, _vertices);
            BindBuffer(_kClassifyAndEmit, ID_Counters, _counters);

            BindBuffer(_kBuildVertexDispatchArgs, ID_Counters, _counters);
            BindBuffer(_kBuildVertexDispatchArgs, ID_DispatchArgs, _dispatchArgs);

            BindBuffer(_kInitSmooth, ID_Vertices, _vertices);
            BindBuffer(_kInitSmooth, ID_SmoothPosA, _smoothPosA);
            BindBuffer(_kInitSmooth, ID_Counters, _counters);

            BindBuffer(_kSmoothVertices, ID_Vertices, _vertices);
            BindBuffer(_kSmoothVertices, ID_CoordVertMap, _coordVertMap);
            BindBuffer(_kSmoothVertices, ID_SmoothPosA, _smoothPosA);
            BindBuffer(_kSmoothVertices, ID_SmoothPosB, _smoothPosB);
            BindBuffer(_kSmoothVertices, ID_Counters, _counters);

            BindBuffer(_kApplySmooth, ID_Vertices, _vertices);
            BindBuffer(_kApplySmooth, ID_SmoothPosA, _smoothPosA);
            BindBuffer(_kApplySmooth, ID_Counters, _counters);

            BindBuffer(_kTemporalBlend, ID_Vertices, _vertices);
            BindBuffer(_kTemporalBlend, ID_Counters, _counters);

            BindBuffer(_kGenerateIndices, ID_Vertices, _vertices);
            BindBuffer(_kGenerateIndices, ID_CoordVertMap, _coordVertMap);
            BindBuffer(_kGenerateIndices, ID_Indices, _indices);
            BindBuffer(_kGenerateIndices, ID_Counters, _counters);

            BindBuffer(_kBuildIndirectArgs, ID_Counters, _counters);
            BindBuffer(_kBuildIndirectArgs, ID_DrawIndirectArgs, _drawIndirectArgs);
        }

        private void BindBuffer(int kernel, int nameID, GraphicsBuffer buffer)
        {
            _compute.SetBuffer(kernel, nameID, buffer);
        }

        public void Dispose()
        {
            _coordVertMap?.Release();
            _vertices?.Release();
            _indices?.Release();
            _counters?.Release();
            _dispatchArgs?.Release();
            _drawIndirectArgs?.Release();
            _smoothPosA?.Release();
            _smoothPosB?.Release();

            if (_temporalState != null)
            {
                _temporalState.Release();
                UnityEngine.Object.Destroy(_temporalState);
            }

            _coordVertMap = null;
            _vertices = null;
            _indices = null;
            _counters = null;
            _dispatchArgs = null;
            _drawIndirectArgs = null;
            _smoothPosA = null;
            _smoothPosB = null;
            _temporalState = null;

            _totalVoxels = 0;
            _temporalInitialized = false;
        }

        private static int CeilDiv(int a, int b) => (a + b - 1) / b;
    }
}
