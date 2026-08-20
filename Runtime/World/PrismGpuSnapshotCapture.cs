using System;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.Prism;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Background-only canonical GPU snapshot capture. Counts and payloads use ordered
    /// AsyncGPUReadback requests; scanning never calls GetData, WaitForCompletion, or
    /// constructs a CPU mesh on the realtime path.
    /// </summary>
    public static class PrismGpuSnapshotCapture
    {
        public static async Task<PrismCanonicalChunkSnapshot> CaptureAsync(
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ulong calibrationEpoch, CancellationToken cancellationToken = default)
        {
            if (films == null || films.IsDisposed)
                throw new ArgumentException("A live ContactFilm pool is required.",
                    nameof(films));
            if (boundaries == null || boundaries.IsDisposed)
                throw new ArgumentException("A live ContactBoundary pool is required.",
                    nameof(boundaries));
            cancellationToken.ThrowIfCancellationRequested();

            Task<byte[]> filmAllocatorTask = RequestBytes(films.Allocator,
                sizeof(uint) * 4, cancellationToken);
            Task<byte[]> boundaryAllocatorTask = RequestBytes(boundaries.Allocator,
                sizeof(uint) * 4, cancellationToken);
            await Task.WhenAll(filmAllocatorTask, boundaryAllocatorTask);
            byte[] filmAllocator = filmAllocatorTask.Result;
            byte[] boundaryAllocator = boundaryAllocatorTask.Result;
            int filmCount = checked((int)Math.Min(BitConverter.ToUInt32(filmAllocator, 0),
                (uint)films.Capacity));
            int boundaryCount = checked((int)Math.Min(
                BitConverter.ToUInt32(boundaryAllocator, 0),
                (uint)boundaries.Capacity));
            uint filmGeneration = BitConverter.ToUInt32(filmAllocator, 12);
            uint boundaryGeneration = BitConverter.ToUInt32(boundaryAllocator, 12);

            Task<byte[]> filmHeaders = RequestBytes(films.Headers,
                checked(filmCount * ContactFilmHeaderGpu.Stride), cancellationToken);
            Task<byte[]> filmInformation = RequestBytes(films.Information,
                checked(filmCount * 9 * sizeof(float) * 4), cancellationToken);
            Task<byte[]> boundaryHeaders = RequestBytes(boundaries.Headers,
                checked(boundaryCount * ContactBoundaryHeaderGpu.Stride),
                cancellationToken);
            Task<byte[]> boundaryInformation = RequestBytes(boundaries.Information,
                checked(boundaryCount *
                    ContactBoundaryPool.InformationRecordsPerBoundary *
                    sizeof(float) * 4), cancellationToken);
            await Task.WhenAll(filmHeaders, filmInformation, boundaryHeaders,
                boundaryInformation);
            cancellationToken.ThrowIfCancellationRequested();

            return new PrismCanonicalChunkSnapshot
            {
                FilmCount = filmCount,
                BoundaryCount = boundaryCount,
                FilmGeneration = filmGeneration,
                BoundaryGeneration = boundaryGeneration,
                CalibrationEpoch = calibrationEpoch,
                FilmHeaders = filmHeaders.Result,
                FilmInformation = filmInformation.Result,
                BoundaryHeaders = boundaryHeaders.Result,
                BoundaryInformation = boundaryInformation.Result
            };
        }

        private static Task<byte[]> RequestBytes(GraphicsBuffer buffer, int byteCount,
            CancellationToken cancellationToken)
        {
            if (byteCount == 0) return Task.FromResult(Array.Empty<byte>());
            var completion = new TaskCompletionSource<byte[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
                registration = cancellationToken.Register(() =>
                    completion.TrySetCanceled(cancellationToken));
            try
            {
                AsyncGPUReadback.Request(buffer, byteCount, 0, request =>
                {
                    registration.Dispose();
                    if (request.hasError)
                    {
                        completion.TrySetException(new InvalidOperationException(
                            "Asynchronous PRISM GPU readback failed."));
                        return;
                    }
                    try { completion.TrySetResult(request.GetData<byte>().ToArray()); }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                });
            }
            catch (Exception exception)
            {
                registration.Dispose();
                completion.TrySetException(exception);
            }
            return completion.Task;
        }
    }
}
