using System;
using FinalScan.Platform.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace FinalScan.Render
{
    /// <summary>Opaque-bucket geometry (Shaders/FinalScanSurfel.shader header).</summary>
    public enum SurfelOpaqueGeometry
    {
        /// <summary>6-vertex quad, analytic rim discard + alpha-to-coverage. Native writes vertexCountPerInstance = 6.</summary>
        QuadDiscard = 0,
        /// <summary>K-triangle fan (native writes 3K, 24 = 8-gon), no discard, no A2C: Adreno LRZ stays enabled (FS_POLYGON).</summary>
        PolygonFan = 1,
    }

    /// <summary>
    /// C05 surfel renderer (contract §13.1/§13.4): owns the Unity GraphicsBuffers the native CULL pass fills (draw
    /// records, 32 B each, and TWO back-to-back FsIndirectDrawArgs = 32 B), registers them with the plugin once, and
    /// issues the per-frame indirect draws from <see cref="FinalScanRenderFeature"/>:
    ///
    ///   draw 1  args offset 0   opaque leaf bucket      pass 0 SurfelOpaque    (XRAY: pass 2)
    ///   draw 2  args offset 16  aggregate bucket        pass 1 SurfelAggregate (XRAY: pass 2)
    ///
    /// Buckets are contiguous in the record buffer (aggregates start at opaqueArgs.instanceCount / 2); both args have
    /// startInstance = 0 and instanceCount = visible * 2 for single-pass instanced stereo (the instance multiplier Unity
    /// applies to non-indirect draws cannot touch a GPU-written argument buffer; donor lesson f4970df). The args buffer
    /// is also bound as StructuredBuffer&lt;uint&gt; so the shader reads vertexCountPerInstance and the bucket base itself.
    /// The native LOD cut may update every second frame; nothing here assumes the args change per frame.
    ///
    /// No CPU readback, no fences: the renderer reads whatever the last FRAME_BEGIN cull published (§11).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfelRenderer : MonoBehaviour
    {
        public const string ShaderName = "FinalScan/Surfel";
        public const int PassOpaque = 0;
        public const int PassAggregate = 1;
        public const int PassXRay = 2;
        public const int ArgsSlotCount = 2;                                  // opaque, aggregate
        public const int ArgsUintCount = ArgsSlotCount * SurfelDrawAbi.IndirectArgsCount;
        public const int AggregateArgsByteOffset = SurfelDrawAbi.IndirectArgsCount * sizeof(uint);   // 16
        public const uint PolygonFanVertexCount = 24;                        // 8-gon fan

        /// <summary>Instance used by the render feature (one per scene).</summary>
        public static SurfelRenderer Current { get; private set; }

        [Tooltip("FinalScan/Surfel. Assigned by the editor setup so the shader ships in the APK (Shader.Find alone would be stripped).")]
        [SerializeField] Shader surfelShader;
        [Tooltip("FsDrawRecord slots in the draw record buffer; must match FsHostConfig.drawRecordCapacity.")]
        [SerializeField] int drawRecordCapacity = 1 << 20;
        [Tooltip("§13.4 screen-work budget (leaf surfels per frame) passed with FsRender_SetLodPolicy.")]
        [SerializeField] uint screenWorkBudget = 200000;
        [SerializeField] RenderMode mode = RenderMode.Scan;
        [SerializeField] SurfelOpaqueGeometry opaqueGeometry = SurfelOpaqueGeometry.PolygonFan;
        [Tooltip("-1: aggregate bucket starts right after the opaque bucket (derived from the args on the GPU). >= 0: fixed record index.")]
        [SerializeField] int aggregateRecordBase = -1;
        [Range(0.05f, 1f)] [SerializeField] float xrayOpacity = 0.5f;
        [Tooltip("XRAY: depth test between surfels (true) or draw everything (false, pure radar).")]
        [SerializeField] bool xrayDepthTestBetweenSurfels = true;
        [Tooltip("Write SV_Depth from the analytic ray/plane intersection (FS_ANALYTIC_DEPTH). Off: the polygon is coplanar with the surfel so rasterized depth already is the plane depth and early-Z stays enabled. Measure on device (§13.2).")]
        [SerializeField] bool analyticDepth;
        [SerializeField] bool useMainLightDirection = true;
        [SerializeField] Vector3 fallbackLightDirection = new Vector3(0.3f, 0.8f, 0.5f);

        static readonly int DrawRecordsId = Shader.PropertyToID("_DrawRecords");
        static readonly int DrawArgsId = Shader.PropertyToID("_DrawArgs");
        static readonly int ModeId = Shader.PropertyToID("_Mode");
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int LightDirId = Shader.PropertyToID("_LightDir");
        static readonly int ZTestId = Shader.PropertyToID("_XRayZTest");
        static readonly int OpaqueA2CId = Shader.PropertyToID("_OpaqueA2C");
        static readonly int ArgsSlotId = Shader.PropertyToID("_ArgsSlot");
        static readonly int AggregateBaseId = Shader.PropertyToID("_AggregateRecordBase");
        const string AnalyticDepthKeyword = "FS_ANALYTIC_DEPTH";
        const string PolygonKeyword = "FS_POLYGON";

        GraphicsBuffer _drawRecords;
        GraphicsBuffer _indirectArgs;
        Material _material;
        MaterialPropertyBlock _propsOpaque, _propsAggregate;
        bool _registered;
        int _registerAttempts;
        float _nextRegisterTime;
        string _lastError;

        public uint ScreenWorkBudget { get => screenWorkBudget; set => screenWorkBudget = Math.Max(1000u, value); }
        public RenderMode Mode { get => mode; set => mode = value; }
        public SurfelOpaqueGeometry OpaqueGeometry { get => opaqueGeometry; set => opaqueGeometry = value; }
        /// <summary>vertexCountPerInstance the native cull is expected to write for the opaque bucket with the current geometry.</summary>
        public uint ExpectedOpaqueVertexCount => opaqueGeometry == SurfelOpaqueGeometry.PolygonFan ? PolygonFanVertexCount : SurfelDrawAbi.VerticesPerSurfel;
        public float XRayOpacity { get => xrayOpacity; set => xrayOpacity = Mathf.Clamp(value, 0.05f, 1f); }
        public bool XRayDepthTestBetweenSurfels { get => xrayDepthTestBetweenSurfels; set => xrayDepthTestBetweenSurfels = value; }
        public bool AnalyticDepth { get => analyticDepth; set => analyticDepth = value; }
        public int DrawRecordCapacity => drawRecordCapacity;
        public bool BuffersReady => _drawRecords != null && _indirectArgs != null && _material != null;
        public bool Registered => _registered;
        public int RegisterAttempts => _registerAttempts;
        public string LastError => _lastError;
        public long DrawsIssued { get; private set; }
        public GraphicsBuffer DrawRecordBuffer => _drawRecords;
        public GraphicsBuffer IndirectArgsBuffer => _indirectArgs;

        void OnEnable()
        {
            if (Current != null && Current != this) Debug.LogWarning("[FinalScan] more than one SurfelRenderer; the newest wins.");
            Current = this;
            CreateResources();
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
            ReleaseResources();
        }

        /// <summary>Initial args: both buckets empty (instanceCount 0 draws nothing) with the vertex count the shader expects.</summary>
        public static uint[] InitialArgs(uint opaqueVertexCount)
        {
            return new[] { opaqueVertexCount, 0u, 0u, 0u, SurfelDrawAbi.VerticesPerSurfel, 0u, 0u, 0u };
        }

        void CreateResources()
        {
            if (drawRecordCapacity < 1024) drawRecordCapacity = 1024;
            if (_drawRecords == null)
                _drawRecords = new GraphicsBuffer(GraphicsBuffer.Target.Structured, drawRecordCapacity, SurfelDrawAbi.DrawRecordStride) { name = "FS-DrawRecords" };
            if (_indirectArgs == null)
            {
                _indirectArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, ArgsUintCount, sizeof(uint)) { name = "FS-IndirectArgs" };
                _indirectArgs.SetData(InitialArgs(ExpectedOpaqueVertexCount));
            }
            if (_material == null)
            {
                Shader shader = surfelShader != null ? surfelShader : Shader.Find(ShaderName);
                if (shader == null) { _lastError = "shader " + ShaderName + " missing"; Debug.LogError("[FinalScan] " + _lastError); return; }
                _material = new Material(shader) { name = "FS-Surfel", hideFlags = HideFlags.HideAndDontSave };
            }
            _propsOpaque ??= new MaterialPropertyBlock();
            _propsAggregate ??= new MaterialPropertyBlock();
            foreach (var p in new[] { _propsOpaque, _propsAggregate })
            {
                p.SetBuffer(DrawRecordsId, _drawRecords);
                p.SetBuffer(DrawArgsId, _indirectArgs);
            }
            _propsOpaque.SetInt(ArgsSlotId, 0);
            _propsAggregate.SetInt(ArgsSlotId, 1);
            _registered = false;
            _nextRegisterTime = 0f;
        }

        void ReleaseResources()
        {
            _drawRecords?.Release(); _drawRecords = null;
            _indirectArgs?.Release(); _indirectArgs = null;
            if (_material != null) { Destroy(_material); _material = null; }
            _registered = false;
        }

        void Update()
        {
            if (!BuffersReady) return;
            if (!_registered && Time.unscaledTime >= _nextRegisterTime) TryRegister();
            UpdateMaterial();
        }

        /// <summary>
        /// FsRender_RegisterBuffers with the native VkBuffer handles (GetNativeBufferPtr). Called once per buffer
        /// generation; the plugin imports lazily on its render thread. Retried every second until FS_OK.
        /// </summary>
        public bool TryRegister()
        {
            if (_registered || !BuffersReady) return _registered;
            _nextRegisterTime = Time.unscaledTime + 1f;
            if (!FinalScanHostNative.Available) return false;
            if (FinalScanHostNative.Status == FinalScanHostNative.HostStatus.Uninitialized) return false;
            _registerAttempts++;
            try
            {
                IntPtr records = _drawRecords.GetNativeBufferPtr();
                IntPtr args = _indirectArgs.GetNativeBufferPtr();
                if (records == IntPtr.Zero || args == IntPtr.Zero) { _lastError = "native buffer ptr null"; return false; }
                int rc = FinalScanHostNative.RegisterRenderBuffers(records, (uint)((long)drawRecordCapacity * SurfelDrawAbi.DrawRecordStride), args);
                _registered = rc == FinalScanHostNative.ResultOk;
                if (!_registered) _lastError = "FsRender_RegisterBuffers rc=" + rc;
                else Debug.Log($"[FinalScan] render buffers registered: {drawRecordCapacity} records x {SurfelDrawAbi.DrawRecordStride} B, args {ArgsUintCount * 4} B (2 buckets), opaque vertexCount {ExpectedOpaqueVertexCount}");
            }
            catch (Exception e) { _lastError = e.GetType().Name + ": " + e.Message; }
            return _registered;
        }

        void UpdateMaterial()
        {
            Vector3 l = fallbackLightDirection;
            if (useMainLightDirection && RenderSettings.sun != null) l = -RenderSettings.sun.transform.forward;
            if (l.sqrMagnitude < 1e-6f) l = Vector3.up;
            _material.SetVector(LightDirId, l.normalized);
            _material.SetInt(ModeId, (int)mode);
            _material.SetFloat(OpacityId, mode == RenderMode.XRay ? xrayOpacity : 1f);
            _material.SetInt(ZTestId, (int)(xrayDepthTestBetweenSurfels ? CompareFunction.LessEqual : CompareFunction.Always));
            _material.SetInt(AggregateBaseId, aggregateRecordBase);
            bool polygon = opaqueGeometry == SurfelOpaqueGeometry.PolygonFan;
            _material.SetInt(OpaqueA2CId, polygon ? 0 : 1);
            if (polygon) _material.EnableKeyword(PolygonKeyword); else _material.DisableKeyword(PolygonKeyword);
            if (analyticDepth) _material.EnableKeyword(AnalyticDepthKeyword); else _material.DisableKeyword(AnalyticDepthKeyword);
        }

        /// <summary>The two indirect draws of the frame (opaque bucket, then aggregates). Called by FinalScanRenderFeature.SurfelDrawPass.</summary>
        public void Draw(RasterCommandBuffer cmd)
        {
            if (!BuffersReady) return;
            bool xray = mode == RenderMode.XRay;
            cmd.DrawProceduralIndirect(Matrix4x4.identity, _material, xray ? PassXRay : PassOpaque, MeshTopology.Triangles, _indirectArgs, 0, _propsOpaque);
            cmd.DrawProceduralIndirect(Matrix4x4.identity, _material, xray ? PassXRay : PassAggregate, MeshTopology.Triangles, _indirectArgs, AggregateArgsByteOffset, _propsAggregate);
            DrawsIssued++;
        }
    }
}
