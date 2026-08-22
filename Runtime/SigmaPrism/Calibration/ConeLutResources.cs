using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Immutable per-pixel local ray and normalized-ray Jacobian for one rectified
    /// sensor projection. W channels carry solid angle and angular half widths.
    /// </summary>
    public sealed class ConeProjectionLut
    {
        internal ConeProjectionLut(RigProjectionCalibration calibration,
            RenderTexture center, RenderTexture differentialX,
            RenderTexture differentialY, RenderTexture slopeBounds)
        {
            Calibration = calibration;
            CenterRaySolidAngle = center;
            DifferentialXHalfAngle = differentialX;
            DifferentialYHalfAngle = differentialY;
            SlopeBounds = slopeBounds;
        }

        public RigProjectionCalibration Calibration { get; }
        public RenderTexture CenterRaySolidAngle { get; }
        public RenderTexture DifferentialXHalfAngle { get; }
        public RenderTexture DifferentialYHalfAngle { get; }
        /// <summary>(slopeXLo, slopeXHi, slopeYLo, slopeYHi) at pixel edges.</summary>
        public RenderTexture SlopeBounds { get; }
    }

    /// <summary>Ref-counted immutable four-projection LUT epoch.</summary>
    public sealed class ConeLutLease : IDisposable
    {
        private RigConeLutSet _owner;

        internal ConeLutLease(RigConeLutSet owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        public RigCalibration Calibration => Owner.Calibration;
        public ConeProjectionLut RgbLeft => Owner.RgbLeft;
        public ConeProjectionLut RgbRight => Owner.RgbRight;
        public ConeProjectionLut DepthLeft => Owner.DepthLeft;
        public ConeProjectionLut DepthRight => Owner.DepthRight;
        public bool IsDisposed => _owner == null;

        public ConeProjectionLut Get(RigProjection projection) => Owner.Get(projection);

        public ConeLutLease Retain()
        {
            RigConeLutSet owner = Owner;
            owner.Retain();
            return new ConeLutLease(owner);
        }

        public void Dispose()
        {
            RigConeLutSet owner = _owner;
            if (owner == null)
                return;
            _owner = null;
            owner.Release();
        }

        private RigConeLutSet Owner => _owner ??
            throw new ObjectDisposedException(nameof(ConeLutLease));
    }

    internal sealed class RigConeLutSet
    {
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int FocalLengthId = Shader.PropertyToID("_FocalLength");
        private static readonly int PrincipalPointId = Shader.PropertyToID("_PrincipalPoint");
        private static readonly int CenterId = Shader.PropertyToID("_ConeCenter");
        private static readonly int DifferentialXId = Shader.PropertyToID("_ConeDifferentialX");
        private static readonly int DifferentialYId = Shader.PropertyToID("_ConeDifferentialY");
        private static readonly int SlopeBoundsId = Shader.PropertyToID("_ConeSlopeBounds");

        private readonly ComputeShader _shader;
        private readonly int _kernel;
        private int _references = 1;
        private bool _retired;
        private bool _destroyed;

        private RigConeLutSet(ComputeShader shader, RigCalibration calibration)
        {
            _shader = shader;
            _kernel = shader.FindKernel("BuildConeLut");
            Calibration = calibration;
            try
            {
                RgbLeft = Build(calibration.RgbLeft);
                RgbRight = Build(calibration.RgbRight);
                DepthLeft = Build(calibration.DepthLeft);
                DepthRight = Build(calibration.DepthRight);
            }
            catch
            {
                DestroyProjection(RgbLeft);
                DestroyProjection(RgbRight);
                DestroyProjection(DepthLeft);
                DestroyProjection(DepthRight);
                throw;
            }
        }

        internal RigCalibration Calibration { get; }
        internal ConeProjectionLut RgbLeft { get; }
        internal ConeProjectionLut RgbRight { get; }
        internal ConeProjectionLut DepthLeft { get; }
        internal ConeProjectionLut DepthRight { get; }

        internal static RigConeLutSet Create(ComputeShader shader,
            RigCalibration calibration)
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            if (calibration == null)
                throw new ArgumentNullException(nameof(calibration));
            return new RigConeLutSet(shader, calibration);
        }

        internal ConeLutLease Acquire()
        {
            Retain();
            return new ConeLutLease(this);
        }

        internal void Retire()
        {
            if (_retired)
                return;
            _retired = true;
            Release();
        }

        internal void Retain()
        {
            if (_destroyed)
                throw new ObjectDisposedException(nameof(RigConeLutSet));
            checked { _references++; }
        }

        internal void Release()
        {
            if (_references <= 0)
                return;
            _references--;
            if (_references == 0 && _retired)
                Destroy();
        }

        internal ConeProjectionLut Get(RigProjection projection) => projection switch
        {
            RigProjection.RgbLeft => RgbLeft,
            RigProjection.RgbRight => RgbRight,
            RigProjection.DepthLeft => DepthLeft,
            RigProjection.DepthRight => DepthRight,
            _ => throw new ArgumentOutOfRangeException(nameof(projection))
        };

        private ConeProjectionLut Build(RigProjectionCalibration projection)
        {
            Vector2Int resolution = projection.Resolution;
            RenderTexture center = CreateTexture($"{projection.Projection} Cone Center",
                resolution, GraphicsFormat.R32G32B32A32_SFloat);
            RenderTexture dx = CreateTexture($"{projection.Projection} Cone dX",
                resolution, GraphicsFormat.R16G16B16A16_SFloat);
            RenderTexture dy = CreateTexture($"{projection.Projection} Cone dY",
                resolution, GraphicsFormat.R16G16B16A16_SFloat);
            RenderTexture slopes = CreateTexture(
                $"{projection.Projection} Cone Slope Bounds", resolution,
                GraphicsFormat.R32G32B32A32_SFloat);
            try
            {
                _shader.SetInts(ResolutionId, resolution.x, resolution.y);
                _shader.SetVector(FocalLengthId, projection.Intrinsics.FocalLength);
                _shader.SetVector(PrincipalPointId, projection.Intrinsics.PrincipalPoint);
                _shader.SetTexture(_kernel, CenterId, center);
                _shader.SetTexture(_kernel, DifferentialXId, dx);
                _shader.SetTexture(_kernel, DifferentialYId, dy);
                _shader.SetTexture(_kernel, SlopeBoundsId, slopes);
                _shader.Dispatch(_kernel, CeilDiv(resolution.x, 8),
                    CeilDiv(resolution.y, 8), 1);
                return new ConeProjectionLut(projection, center, dx, dy, slopes);
            }
            catch
            {
                DestroyTexture(center);
                DestroyTexture(dx);
                DestroyTexture(dy);
                DestroyTexture(slopes);
                throw;
            }
        }

        private void Destroy()
        {
            if (_destroyed)
                return;
            _destroyed = true;
            DestroyProjection(RgbLeft);
            DestroyProjection(RgbRight);
            DestroyProjection(DepthLeft);
            DestroyProjection(DepthRight);
        }

        private static RenderTexture CreateTexture(string name, Vector2Int resolution,
            GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(resolution.x, resolution.y)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
            var texture = new RenderTexture(descriptor)
            {
                name = $"[Sigma-PRISM-16] {name}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create())
            {
                DestroyTexture(texture);
                throw new InvalidOperationException($"Unable to allocate {name} LUT.");
            }
            return texture;
        }

        private static void DestroyProjection(ConeProjectionLut projection)
        {
            if (projection == null)
                return;
            DestroyTexture(projection.CenterRaySolidAngle);
            DestroyTexture(projection.DifferentialXHalfAngle);
            DestroyTexture(projection.DifferentialYHalfAngle);
            DestroyTexture(projection.SlopeBounds);
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
