using System;
using System.Collections.Generic;
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
    /// One non-blocking GPU completion point. The ticket records whether its fence
    /// is a cross-queue dependency; polling failure is terminal and never means
    /// that a resource can be recycled.
    /// </summary>
    internal readonly struct SigmaGpuCompletionTicket
    {
        private readonly GraphicsFence _fence;
        private readonly SigmaNativeVulkanExecutor.SigmaNativeVulkanJob
            _nativeJob;
        private readonly bool _valid;
        private readonly bool _crossQueue;

        internal SigmaGpuCompletionTicket(GraphicsFence fence,
            bool crossQueue = false)
        {
            _fence = fence;
            _nativeJob = null;
            _valid = true;
            _crossQueue = crossQueue;
        }

        internal SigmaGpuCompletionTicket(
            SigmaNativeVulkanExecutor.SigmaNativeVulkanJob nativeJob)
        {
            _fence = default;
            _nativeJob = nativeJob ?? throw new ArgumentNullException(
                nameof(nativeJob));
            _valid = true;
            _crossQueue = false;
        }

        internal bool IsValid => _valid;
        internal bool IsNative => _valid && _nativeJob != null;
        internal bool IsCrossQueue => _valid && _crossQueue;
        internal GraphicsFence Fence => _valid && _nativeJob == null
            ? _fence
            : throw new InvalidOperationException(
                "The GPU completion ticket is not a Unity graphics fence.");

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
                if (_nativeJob != null)
                    return _nativeJob.Poll(out error);
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

        internal static void RequireBackgroundComputeSupported()
        {
            RequireSupported();
            if (!SystemInfo.supportsAsyncCompute)
                throw new InvalidOperationException(
                    "N4.2R Quest requires Vulkan background async compute; " +
                    "graphics-queue fallback is forbidden.");
            if (!SystemInfo.supportsAsyncGPUReadback)
                throw new InvalidOperationException(
                    "N4.2R Quest requires asynchronous GPU readback.");
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

        internal static SigmaGpuCompletionTicket RecordCrossQueueFence(
            CommandBuffer command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            RequireBackgroundComputeSupported();
            return new SigmaGpuCompletionTicket(command.CreateGraphicsFence(
                GraphicsFenceType.AsyncQueueSynchronisation,
                SynchronisationStageFlags.AllGPUOperations), true);
        }

        internal static void WaitOnCrossQueueFence(CommandBuffer command,
            SigmaGpuCompletionTicket ticket)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (!ticket.IsCrossQueue)
                throw new InvalidOperationException(
                    "Only an async-queue synchronization fence may cross from " +
                    "the graphics prepass to native background compute.");
            command.WaitOnAsyncGraphicsFence(ticket.Fence);
        }
    }

    /// <summary>
    /// Main-thread, non-blocking retirement for GPU-owned resources whose normal
    /// component owner can disappear before the graphics queue reaches its fence.
    /// A failed fence is quarantined: unproven resources are intentionally kept
    /// alive instead of being recycled or destroyed under live GPU work.
    /// </summary>
    internal static class SigmaGpuRetirement
    {
        private sealed class Entry
        {
            internal SigmaGpuCompletionTicket Ticket;
            internal Action Release;
            internal string Label;
        }

        private static readonly List<Entry> Pending = new();
        private static readonly List<Entry> Quarantined = new();
        private static bool _subscribed;

        internal static int PendingCount => Pending.Count;
        internal static int QuarantinedCount => Quarantined.Count;

        internal static void Retire(SigmaGpuCompletionTicket ticket,
            Action release, string label)
        {
            if (release == null)
                return;
            var entry = new Entry
            {
                Ticket = ticket,
                Release = release,
                Label = string.IsNullOrWhiteSpace(label)
                    ? "Sigma GPU resources"
                    : label
            };
            SigmaGpuCompletionStatus status = ticket.Poll(out string error);
            if (status == SigmaGpuCompletionStatus.Complete)
            {
                ReleaseOnce(entry);
                return;
            }
            if (status == SigmaGpuCompletionStatus.Faulted)
            {
                Quarantine(entry, error);
                return;
            }
            Pending.Add(entry);
            EnsurePump();
        }

        internal static void Quarantine(Action release, string label,
            string error)
        {
            if (release == null)
                return;
            Quarantine(new Entry
            {
                Release = release,
                Label = string.IsNullOrWhiteSpace(label)
                    ? "Sigma GPU resources"
                    : label
            }, error);
        }

        internal static void Poll()
        {
            for (int index = Pending.Count - 1; index >= 0; --index)
            {
                Entry entry = Pending[index];
                SigmaGpuCompletionStatus status = entry.Ticket.Poll(
                    out string error);
                if (status == SigmaGpuCompletionStatus.Pending)
                    continue;
                Pending.RemoveAt(index);
                if (status == SigmaGpuCompletionStatus.Complete)
                    ReleaseOnce(entry);
                else
                    Quarantine(entry, error);
            }
            if (Pending.Count == 0)
                StopPump();
        }

        private static void EnsurePump()
        {
            if (_subscribed)
                return;
            RenderPipelineManager.endFrameRendering += EndFrameRendering;
            _subscribed = true;
        }

        private static void StopPump()
        {
            if (!_subscribed)
                return;
            RenderPipelineManager.endFrameRendering -= EndFrameRendering;
            _subscribed = false;
        }

        private static void EndFrameRendering(ScriptableRenderContext context,
            Camera[] cameras) => Poll();

        private static void ReleaseOnce(Entry entry)
        {
            Action release = entry.Release;
            entry.Release = null;
            try
            {
                release?.Invoke();
            }
            catch (Exception exception)
            {
                Logger.Error($"{entry.Label}: deferred GPU resource release " +
                             $"failed: {exception.Message}");
            }
        }

        private static void Quarantine(Entry entry, string error)
        {
            Quarantined.Add(entry);
            Logger.Error($"{entry.Label}: GPU completion is unprovable; " +
                         "resources were quarantined and will not be reused. " +
                         (string.IsNullOrWhiteSpace(error) ? string.Empty : error));
        }
    }
}
