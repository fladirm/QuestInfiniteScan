using System;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    public enum SigmaExactBackendGateStatus
    {
        GpuResident = 0,
        Disposed = 1
    }

    /// <summary>
    /// GPU-resident exact-backend witness. Canonical mutation kernels consume this
    /// one-word buffer directly; no CPU readback or optimistic device assumption is
    /// needed in the live path. Zero means fail closed.
    /// </summary>
    public sealed class SigmaExactBackendGate : IDisposable
    {
        private const string FixtureResource = "SigmaPrism/SigmaOperatorFixture";
        private readonly GraphicsBuffer _gate;
        private bool _disposed;

        private SigmaExactBackendGate(GraphicsBuffer gate)
        {
            _gate = gate;
        }

        /// <summary>
        /// The authoritative result intentionally has no CPU mirror. Every exact
        /// mutation kernel consumes the witness buffer and fails closed on zero.
        /// </summary>
        public SigmaExactBackendGateStatus DiagnosticStatus =>
            _disposed ? SigmaExactBackendGateStatus.Disposed :
                SigmaExactBackendGateStatus.GpuResident;

        public GraphicsBuffer Buffer
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SigmaExactBackendGate));
                return _gate;
            }
        }

        public static SigmaExactBackendGate Dispatch()
        {
            ComputeShader fixture = Resources.Load<ComputeShader>(FixtureResource);
            if (fixture == null)
                throw new InvalidOperationException(
                    "The exact Sigma backend fixture resource is missing.");
            int kernel = fixture.FindProfiledKernel("CanonicalSelfTest");
            var gate = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1,
                sizeof(uint));
            gate.SetData(new uint[] { 0u });
            fixture.SetBuffer(kernel, "_CapabilityGate", gate);
            fixture.Dispatch(kernel, 1, 1, 1);
            Logger.Info("Exact S16 GPU witness dispatched; mutation kernels " +
                        "consume it directly and fail closed on zero.");
            return new SigmaExactBackendGate(gate);
        }

        public void Bind(ComputeShader shader, int kernel,
            string propertyName = "_SigmaExactBackendGate")
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            shader.SetBuffer(kernel, propertyName, Buffer);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _gate.Dispose();
        }
    }
}
