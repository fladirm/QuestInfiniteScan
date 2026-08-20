using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.World
{
    public sealed class ChunkGpuSnapshot
    {
        public ChunkVolumeSnapshot Volume { get; set; }
        public ChunkLiveMeshSnapshot LiveMesh { get; set; }
    }

    /// <summary>
    /// Enqueues all GPU readbacks behind one final Surface Nets extraction. Requests copy only
    /// the active portion of the mesh buffers; the much larger capacity tail never crosses the
    /// GPU/CPU boundary. Unity callbacks copy NativeArray data before the callback returns.
    /// </summary>
    internal static class ChunkGpuSnapshotCapture
    {
        internal const int SnapshotTimeoutMilliseconds = 15_000;

        public static async Task<ChunkGpuSnapshot> CaptureAsync(VolumeIntegrator volume,
            MeshExtractor meshExtractor)
        {
            if (volume == null || volume.Volume == null || volume.ColorVolume == null)
                throw new InvalidOperationException("Allocated TSDF and color volumes are required.");

            // Queue one final extraction first. Every following readback is ordered after it on
            // the graphics queue, so volume and live mesh describe the same finalized source.
            meshExtractor?.Extract();

            int3 voxelCount = volume.VoxelCount;
            int integrationCount = volume.IntegrationCount;
            float voxelSize = volume.VoxelSize;
            RigidPoseData worldFromVolume = volume.WorldFromVolume;
            int voxelTotal = checked(voxelCount.x * voxelCount.y * voxelCount.z);
            var stopwatch = Stopwatch.StartNew();
            Task<NativeArray<byte>> tsdfTask = ReadTexture3DNativeAsync(volume.Volume,
                checked(voxelTotal * 2));
            Task<NativeArray<byte>> colorTask = ReadTexture3DNativeAsync(volume.ColorVolume,
                checked(voxelTotal * 4));
            Task<ChunkLiveMeshSnapshot> meshTask = CaptureLiveMeshAsync(
                meshExtractor?.GpuSurfaceNets, voxelSize, voxelCount);

            NativeArray<byte> tsdfNative = default;
            NativeArray<byte> colorNative = default;
            bool cleanupDelegated = false;
            try
            {
                Task allReadbacks = Task.WhenAll(tsdfTask, colorTask, meshTask);
                Task completed = await Task.WhenAny(allReadbacks,
                    Task.Delay(SnapshotTimeoutMilliseconds));
                if (!ReferenceEquals(completed, allReadbacks))
                {
                    cleanupDelegated = true;
                    DisposeNativeWhenComplete(tsdfTask);
                    DisposeNativeWhenComplete(colorTask);
                    ObserveWhenComplete(meshTask);
                    throw new TimeoutException($"GPU snapshot did not complete within " +
                        $"{SnapshotTimeoutMilliseconds / 1000} seconds. The scan remains " +
                        "recoverable, but its current chunk was not published.");
                }
                await allReadbacks;
                tsdfNative = tsdfTask.Result;
                colorNative = colorTask.Result;
                long gpuMilliseconds = stopwatch.ElapsedMilliseconds;

                byte[] tsdfBytes = null;
                byte[] colorBytes = null;
                // RequestIntoNativeArray makes the GPU callback O(1). The old callback
                // allocated and copied 96 MB slice-by-slice on Unity's render thread,
                // producing a visible ~1 s freeze. Persistent native buffers remain valid
                // while this bulk copy runs on a worker.
                await Task.Run(() =>
                {
                    tsdfBytes = new byte[tsdfNative.Length];
                    colorBytes = new byte[colorNative.Length];
                    tsdfNative.CopyTo(tsdfBytes);
                    colorNative.CopyTo(colorBytes);
                });
                long managedCopyMilliseconds = stopwatch.ElapsedMilliseconds - gpuMilliseconds;
                Logger.Info($"Chunk snapshot readback: GPU={gpuMilliseconds}ms, " +
                            $"worker-copy={managedCopyMilliseconds}ms");

                return new ChunkGpuSnapshot
                {
                    Volume = new ChunkVolumeSnapshot
                    {
                        VoxelCount = new Vector3Int(voxelCount.x, voxelCount.y, voxelCount.z),
                        VoxelSize = voxelSize,
                        IntegrationCount = integrationCount,
                        WorldFromVolume = worldFromVolume,
                        TsdfBytes = tsdfBytes,
                        ColorBytes = colorBytes
                    },
                    LiveMesh = meshTask.Result
                };
            }
            finally
            {
                if (!cleanupDelegated)
                {
                    if (!tsdfNative.IsCreated &&
                        tsdfTask.Status == TaskStatus.RanToCompletion)
                        tsdfNative = tsdfTask.Result;
                    if (!colorNative.IsCreated &&
                        colorTask.Status == TaskStatus.RanToCompletion)
                        colorNative = colorTask.Result;
                    if (tsdfNative.IsCreated)
                        tsdfNative.Dispose();
                    if (colorNative.IsCreated)
                        colorNative.Dispose();
                }
            }
        }

        private static void DisposeNativeWhenComplete(Task<NativeArray<byte>> task)
        {
            _ = task.ContinueWith(completed =>
            {
                if (completed.Status == TaskStatus.RanToCompletion)
                {
                    NativeArray<byte> data = completed.Result;
                    if (data.IsCreated)
                        data.Dispose();
                }
                else
                {
                    _ = completed.Exception;
                }
            }, TaskScheduler.Default);
        }

        private static void ObserveWhenComplete(Task task)
        {
            _ = task.ContinueWith(completed =>
            {
                if (completed.IsFaulted)
                    _ = completed.Exception;
            }, TaskScheduler.Default);
        }

        private static async Task<ChunkLiveMeshSnapshot> CaptureLiveMeshAsync(
            GPUSurfaceNets surfaceNets, float voxelSize, int3 voxelCount)
        {
            if (surfaceNets?.CountersBuffer == null || surfaceNets.VertexBuffer == null ||
                surfaceNets.IndexBuffer == null)
                return null;

            byte[] counters = await ReadBufferAsync(surfaceNets.CountersBuffer, 8);
            if (counters == null || counters.Length < 8)
                throw new InvalidOperationException("Surface Nets counter readback was incomplete.");
            int vertexCount = BitConverter.ToInt32(counters, 0);
            int indexCount = BitConverter.ToInt32(counters, 4);
            if (vertexCount <= 0 || indexCount <= 0)
                return null;
            if (vertexCount > surfaceNets.VertexBuffer.count ||
                indexCount > surfaceNets.IndexBuffer.count || indexCount % 3 != 0)
                throw new InvalidOperationException(
                    $"Surface Nets counters are invalid: {vertexCount} vertices, " +
                    $"{indexCount} indices.");

            int vertexBytes = checked(vertexCount * ChunkLiveMeshSnapshot.VertexStride);
            int indexBytes = checked(indexCount * sizeof(uint));
            Task<byte[]> verticesTask = ReadBufferAsync(surfaceNets.VertexBuffer, vertexBytes);
            Task<byte[]> indicesTask = ReadBufferAsync(surfaceNets.IndexBuffer, indexBytes);
            await Task.WhenAll(verticesTask, indicesTask);

            Vector3 extents = new Vector3(voxelCount.x, voxelCount.y, voxelCount.z) *
                              (voxelSize * 0.5f);
            return new ChunkLiveMeshSnapshot
            {
                VertexCount = vertexCount,
                IndexCount = indexCount,
                LocalBounds = new BoundsData(Vector3.zero, extents),
                VertexBytes = verticesTask.Result,
                IndexBytes = indicesTask.Result
            };
        }

        private static Task<byte[]> ReadBufferAsync(GraphicsBuffer buffer, int byteCount)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (byteCount <= 0 || byteCount > (long)buffer.count * buffer.stride)
                throw new ArgumentOutOfRangeException(nameof(byteCount));

            var completion = new TaskCompletionSource<byte[]>();
            AsyncGPUReadback.Request(buffer, byteCount, 0, request =>
            {
                if (request.hasError)
                {
                    completion.TrySetException(new InvalidOperationException(
                        "GPU buffer readback failed."));
                    return;
                }
                try
                {
                    NativeArray<byte> native = request.GetData<byte>();
                    if (native.Length != byteCount)
                        throw new InvalidOperationException(
                            $"GPU buffer returned {native.Length} bytes, expected {byteCount}.");
                    var managed = new byte[byteCount];
                    NativeArray<byte>.Copy(native, managed, byteCount);
                    completion.TrySetResult(managed);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return completion.Task;
        }

        private static Task<NativeArray<byte>> ReadTexture3DNativeAsync(RenderTexture texture,
            int expectedByteCount)
        {
            if (texture == null || texture.dimension != TextureDimension.Tex3D ||
                texture.volumeDepth <= 0)
                throw new ArgumentException("A created 3D render texture is required.",
                    nameof(texture));
            var completion = new TaskCompletionSource<NativeArray<byte>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var destination = new NativeArray<byte>(expectedByteCount, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                AsyncGPUReadback.RequestIntoNativeArray(ref destination, texture, 0, request =>
                {
                    if (request.hasError)
                    {
                        if (destination.IsCreated)
                            destination.Dispose();
                        completion.TrySetException(new InvalidOperationException(
                            "GPU 3D-texture readback failed."));
                        return;
                    }
                    completion.TrySetResult(destination);
                });
            }
            catch
            {
                if (destination.IsCreated)
                    destination.Dispose();
                throw;
            }
            return completion.Task;
        }
    }
}
