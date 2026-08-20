using System;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.Prism;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Background-only canonical GPU snapshot capture. Every read is asynchronous;
    /// scanning never waits for completion or constructs CPU geometry. Q3-13's chunk
    /// stager supplies immutable compact pools to this class before durable publication.
    /// </summary>
    public static class PrismGpuSnapshotCapture
    {
        public static Task<PrismCanonicalChunkSnapshot> CaptureAsync(
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ContactDisplacementPool displacement, ContactMeshletBuffers meshlets,
            ulong calibrationEpoch, byte[] appearanceState = null,
            byte[] observationState = null, string[] keyframeReferences = null,
            CancellationToken cancellationToken = default) => CaptureCoreAsync(
                films, boundaries, displacement, meshlets?.Published,
                calibrationEpoch,
                appearanceState, observationState, keyframeReferences,
                cancellationToken);

        /// <summary>
        /// Captures one immutable generation produced by
        /// <see cref="PrismChunkSnapshotStager"/>. Unlike the public live-pool
        /// compatibility overload this path cannot observe a meshlet publication swap
        /// halfway through a detached revision.
        /// </summary>
        internal static Task<PrismCanonicalChunkSnapshot> CaptureStagedAsync(
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ContactDisplacementPool displacement,
            ContactMeshletGenerationBuffers meshlets, ulong calibrationEpoch,
            byte[] appearanceState = null, byte[] observationState = null,
            string[] keyframeReferences = null,
            CancellationToken cancellationToken = default) => CaptureCoreAsync(
                films, boundaries, displacement, meshlets, calibrationEpoch,
                appearanceState, observationState, keyframeReferences,
                cancellationToken);

        /// <summary>Compatibility overload for pre-Q3-11 fixtures without detail pools.</summary>
        public static Task<PrismCanonicalChunkSnapshot> CaptureAsync(
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ulong calibrationEpoch, CancellationToken cancellationToken = default) =>
            CaptureCoreAsync(films, boundaries, null, null, calibrationEpoch,
                null, null, null, cancellationToken);

        private static async Task<PrismCanonicalChunkSnapshot> CaptureCoreAsync(
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ContactDisplacementPool displacement,
            ContactMeshletGenerationBuffers published,
            ulong calibrationEpoch, byte[] appearanceState,
            byte[] observationState, string[] keyframeReferences,
            CancellationToken cancellationToken)
        {
            if (films == null || films.IsDisposed)
                throw new ArgumentException("A live ContactFilm pool is required.",
                    nameof(films));
            if (boundaries == null || boundaries.IsDisposed)
                throw new ArgumentException("A live ContactBoundary pool is required.",
                    nameof(boundaries));
            if (displacement != null && displacement.IsDisposed)
                throw new ArgumentException("The displacement pool is disposed.",
                    nameof(displacement));
            if (published != null && published.IsDisposed)
                throw new ArgumentException("The meshlet publication is disposed.",
                    nameof(published));
            cancellationToken.ThrowIfCancellationRequested();

            Task<byte[]> filmAllocatorTask = RequestBytes(films.Allocator,
                sizeof(uint) * 4, cancellationToken);
            Task<byte[]> boundaryAllocatorTask = RequestBytes(boundaries.Allocator,
                sizeof(uint) * 4, cancellationToken);
            Task<byte[]> displacementAllocatorTask = displacement == null
                ? Task.FromResult(new byte[sizeof(uint) * 8])
                : RequestBytes(displacement.Allocator, sizeof(uint) * 8,
                    cancellationToken);
            Task<byte[]> meshletCountersTask = published == null
                ? Task.FromResult(new byte[sizeof(uint) * 8])
                : RequestBytes(published.BuildCounters, sizeof(uint) * 8,
                    cancellationToken);
            await Task.WhenAll(filmAllocatorTask, boundaryAllocatorTask,
                displacementAllocatorTask, meshletCountersTask);
            cancellationToken.ThrowIfCancellationRequested();

            byte[] filmAllocator = filmAllocatorTask.Result;
            byte[] boundaryAllocator = boundaryAllocatorTask.Result;
            byte[] displacementAllocator = displacementAllocatorTask.Result;
            byte[] meshletCounters = meshletCountersTask.Result;
            int filmCount = Count(filmAllocator, 0, films.Capacity);
            int boundaryCount = Count(boundaryAllocator, 0, boundaries.Capacity);
            int basePageCount = displacement == null ? 0 :
                Count(displacementAllocator, 0, displacement.BasePageCapacity);
            int microPageCount = displacement == null ? 0 :
                Count(displacementAllocator, 4, displacement.MicroPageCapacity);
            int meshletVertexCount = published == null ? 0 :
                Count(meshletCounters, 0, published.VertexCapacity);
            int meshletIndexCount = published == null ? 0 :
                Count(meshletCounters, 4, published.IndexCapacity);
            int meshletDescriptorCount = published == null ? 0 :
                Count(meshletCounters, 16, published.DescriptorCapacity);

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

            Task<byte[]> basePageHeaders = displacement == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(displacement.PageHeaders,
                    checked(basePageCount * DisplacementPageHeaderGpu.Stride),
                    cancellationToken);
            Task<byte[]> microPageHeaders = displacement == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(displacement.PageHeaders,
                    checked(microPageCount * DisplacementPageHeaderGpu.Stride),
                    cancellationToken,
                    checked(displacement.BasePageCapacity *
                        DisplacementPageHeaderGpu.Stride));
            Task<byte[]> baseCells = displacement == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(displacement.BaseCells,
                    checked(basePageCount * ContactDisplacementPool.BaseCellsPerPage *
                        DisplacementCellGpu.Stride), cancellationToken);
            Task<byte[]> microCells = displacement == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(displacement.MicroCells,
                    checked(microPageCount * ContactDisplacementPool.MicroCellsPerPage *
                        DisplacementCellGpu.Stride), cancellationToken);
            Task<byte[]> baseChildren = displacement == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(displacement.BaseChildPages,
                    checked(basePageCount * ContactDisplacementPool.BaseCellsPerPage *
                        sizeof(uint)), cancellationToken);
            Task<byte[]> microChildren = displacement == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(displacement.MicroChildPages,
                    checked(microPageCount * ContactDisplacementPool.MicroCellsPerPage *
                        sizeof(uint)), cancellationToken);
            Task<byte[]> topologyEvidence = displacement == null
                ? Task.FromResult(new byte[checked(filmCount *
                    ContactTopologyEvidenceGpu.Stride)])
                : RequestBytes(displacement.TopologyEvidence,
                    checked(filmCount * ContactTopologyEvidenceGpu.Stride),
                    cancellationToken);
            Task<byte[]> meshletVertices = published == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(published.Vertices,
                    checked(meshletVertexCount * ContactMeshletVertexGpu.Stride),
                    cancellationToken);
            Task<byte[]> meshletIndices = published == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(published.Indices,
                    checked(meshletIndexCount * sizeof(uint)), cancellationToken);
            Task<byte[]> meshletDescriptors = published == null
                ? Task.FromResult(Array.Empty<byte>())
                : RequestBytes(published.Descriptors,
                    checked(meshletDescriptorCount *
                        ContactMeshletDescriptorGpu.Stride), cancellationToken);

            await Task.WhenAll(filmHeaders, filmInformation, boundaryHeaders,
                boundaryInformation, basePageHeaders, microPageHeaders, baseCells,
                microCells, baseChildren, microChildren, topologyEvidence,
                meshletVertices, meshletIndices, meshletDescriptors);
            cancellationToken.ThrowIfCancellationRequested();

            return new PrismCanonicalChunkSnapshot
            {
                FilmCount = filmCount,
                BoundaryCount = boundaryCount,
                DisplacementBasePageCount = basePageCount,
                DisplacementMicroPageCount = microPageCount,
                MeshletVertexCount = meshletVertexCount,
                MeshletIndexCount = meshletIndexCount,
                MeshletDescriptorCount = meshletDescriptorCount,
                FilmGeneration = BitConverter.ToUInt32(filmAllocator, 12),
                BoundaryGeneration = BitConverter.ToUInt32(boundaryAllocator, 12),
                DisplacementGeneration = displacement == null ? 1u :
                    Math.Max(1u, BitConverter.ToUInt32(displacementAllocator, 16)),
                MeshletGeneration = published?.Generation ?? 1u,
                CalibrationEpoch = calibrationEpoch,
                FilmHeaders = filmHeaders.Result,
                FilmInformation = filmInformation.Result,
                BoundaryHeaders = boundaryHeaders.Result,
                BoundaryInformation = boundaryInformation.Result,
                DisplacementPageHeaders = Concatenate(basePageHeaders.Result,
                    microPageHeaders.Result),
                DisplacementBaseCells = baseCells.Result,
                DisplacementMicroCells = microCells.Result,
                DisplacementBaseChildren = baseChildren.Result,
                DisplacementMicroChildren = microChildren.Result,
                TopologyEvidence = topologyEvidence.Result,
                DisplacementAllocator = displacementAllocator,
                MeshletVertices = meshletVertices.Result,
                MeshletIndices = meshletIndices.Result,
                MeshletDescriptors = meshletDescriptors.Result,
                AppearanceState = CloneOrEmpty(appearanceState),
                ObservationState = CloneOrEmpty(observationState),
                KeyframeReferences = keyframeReferences == null
                    ? Array.Empty<string>()
                    : (string[])keyframeReferences.Clone()
            };
        }

        private static int Count(byte[] bytes, int byteOffset, int capacity) =>
            checked((int)Math.Min(BitConverter.ToUInt32(bytes, byteOffset),
                (uint)capacity));

        private static byte[] CloneOrEmpty(byte[] bytes) => bytes == null
            ? Array.Empty<byte>()
            : (byte[])bytes.Clone();

        private static byte[] Concatenate(byte[] first, byte[] second)
        {
            var result = new byte[checked(first.Length + second.Length)];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private static Task<byte[]> RequestBytes(GraphicsBuffer buffer, int byteCount,
            CancellationToken cancellationToken, int byteOffset = 0)
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
                AsyncGPUReadback.Request(buffer, byteCount, byteOffset, request =>
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
