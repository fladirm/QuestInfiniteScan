using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaGpuCompletionStatus : byte
    {
        Pending = 0,
        Complete = 1,
        Faulted = 2
    }

    /// <summary>
    /// One non-blocking graphics-queue completion point. Canonical work never runs
    /// on Unity's optional async-compute queue, so the fence type must not require
    /// async-compute capability. Polling failure is terminal and never means that a
    /// resource can be recycled.
    /// </summary>
    internal readonly struct SigmaGpuCompletionTicket
    {
        private readonly GraphicsFence _fence;
        private readonly bool _valid;

        internal SigmaGpuCompletionTicket(GraphicsFence fence)
        {
            _fence = fence;
            _valid = true;
        }

        internal SigmaGpuCompletionStatus Poll(out string error)
        {
            if (!_valid)
            {
                error = "The GPU completion ticket was not initialized.";
                return SigmaGpuCompletionStatus.Faulted;
            }

            try
            {
                error = null;
                return _fence.passed
                    ? SigmaGpuCompletionStatus.Complete
                    : SigmaGpuCompletionStatus.Pending;
            }
            catch (Exception exception)
            {
                error = $"GPU completion polling failed: {exception.Message}";
                return SigmaGpuCompletionStatus.Faulted;
            }
        }
    }

    internal static class SigmaGpuCompletion
    {
        internal static void RequireSupported()
        {
            if (!SystemInfo.supportsGraphicsFence)
                throw new InvalidOperationException(
                    "Sigma-PRISM-16 requires non-blocking graphics fences.");
        }

        internal static SigmaGpuCompletionTicket InsertAfterGraphicsWork()
        {
            RequireSupported();
            return new SigmaGpuCompletionTicket(Graphics.CreateGraphicsFence(
                GraphicsFenceType.CPUSynchronisation,
                SynchronisationStageFlags.AllGPUOperations));
        }

        internal static SigmaGpuCompletionTicket RecordAfterAllWork(
            CommandBuffer command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            RequireSupported();
            return new SigmaGpuCompletionTicket(command.CreateGraphicsFence(
                GraphicsFenceType.CPUSynchronisation,
                SynchronisationStageFlags.AllGPUOperations));
        }
    }
}
