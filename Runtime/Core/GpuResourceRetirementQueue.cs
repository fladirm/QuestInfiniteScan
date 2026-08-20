using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Keeps dynamically replaced Unity GPU objects alive until commands submitted before
    /// their retirement have passed a Vulkan fence. This is intentionally small and is used
    /// only for rollover-time resources; fixed mapper buffers still live for the scan session.
    /// </summary>
    internal sealed class GpuResourceRetirementQueue
    {
        private readonly struct Entry
        {
            internal readonly UnityEngine.Object Resource;
            internal readonly GraphicsFence Fence;

            internal Entry(UnityEngine.Object resource, GraphicsFence fence)
            {
                Resource = resource;
                Fence = fence;
            }
        }

        private readonly List<Entry> _entries = new();

        internal int PendingCount => _entries.Count;

        internal void RetireAfterCurrentGpuWork(UnityEngine.Object resource)
        {
            if (resource == null)
                return;
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType ==
                GraphicsDeviceType.Null)
            {
                DestroyOwned(resource);
                return;
            }

            try
            {
                GraphicsFence fence = Graphics.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                _entries.Add(new Entry(resource, fence));
            }
            catch (Exception exception)
            {
                // A platform without fence support retains Unity's normal deferred-destroy
                // behaviour. Quest Vulkan takes the fenced path.
                Logger.Warning("GPU retirement fence unavailable: " + exception.Message);
                DestroyOwned(resource);
            }
        }

        internal void DrainCompleted()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                bool passed;
                try
                {
                    passed = _entries[i].Fence.passed;
                }
                catch (Exception exception)
                {
                    Logger.Warning("GPU retirement fence query failed: " + exception.Message);
                    passed = true;
                }
                if (!passed)
                    continue;
                DestroyOwned(_entries[i].Resource);
                _entries.RemoveAt(i);
            }
        }

        private static void DestroyOwned(UnityEngine.Object resource)
        {
            if (resource == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(resource);
            else
                UnityEngine.Object.DestroyImmediate(resource);
        }
    }
}
