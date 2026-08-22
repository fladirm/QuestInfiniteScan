using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    public enum SigmaExactBackendGateStatus
    {
        Pending = 0,
        Passed = 1,
        Failed = 2,
        ReadbackError = 3,
        Disposed = 4
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
        private int _diagnosticStatus;
        private bool _disposed;

        private SigmaExactBackendGate(GraphicsBuffer gate)
        {
            _gate = gate;
            _diagnosticStatus = (int)SigmaExactBackendGateStatus.Pending;
        }

        /// <summary>
        /// Asynchronous operator-UX diagnostic only. Canonical mutation still reads
        /// the GPU gate buffer directly and never trusts this CPU mirror.
        /// </summary>
        public SigmaExactBackendGateStatus DiagnosticStatus =>
            (SigmaExactBackendGateStatus)Volatile.Read(ref _diagnosticStatus);

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
            int kernel = fixture.FindKernel("CanonicalSelfTest");
            var gate = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1,
                sizeof(uint));
            gate.SetData(new uint[] { 0u });
            fixture.SetBuffer(kernel, "_CapabilityGate", gate);
            fixture.Dispatch(kernel, 1, 1, 1);
            var result = new SigmaExactBackendGate(gate);
            result.RequestDiagnosticStatus();
            return result;
        }

        private void RequestDiagnosticStatus()
        {
            AsyncGPUReadback.Request(_gate, request =>
            {
                if (_disposed)
                    return;

                SigmaExactBackendGateStatus status;
                if (request.hasError)
                    status = SigmaExactBackendGateStatus.ReadbackError;
                else
                {
                    var values = request.GetData<uint>();
                    status = values.Length == 1 && values[0] == 1u
                        ? SigmaExactBackendGateStatus.Passed
                        : SigmaExactBackendGateStatus.Failed;
                }

                Interlocked.Exchange(ref _diagnosticStatus, (int)status);
                if (status == SigmaExactBackendGateStatus.Passed)
                    Logger.Info("Exact S16 GPU mutation gate passed.");
                else
                    Logger.Error("Exact S16 GPU mutation gate diagnostic: " + status);
            });
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
            Interlocked.Exchange(ref _diagnosticStatus,
                (int)SigmaExactBackendGateStatus.Disposed);
            _gate.Dispose();
        }
    }
}
