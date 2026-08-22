using System;
using Genesis.RoomScan.Prism;
using Unity.Collections;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Rehydrates a detached canonical chunk by bulk DMA upload into the existing
    /// GPU arenas. Serialized bytes are reinterpreted in one contiguous native copy;
    /// there is no per-film CPU decode, geometry traversal, CPU mesh, or GPU readback.
    /// Local IDs produced by <see cref="PrismChunkSnapshotStager"/> remain unchanged.
    /// </summary>
    public static class PrismGpuSnapshotRestore
    {
        private const int FilmInformationRecords = ContactFilmPool.InformationRecords;

        public static bool TryCreateMeshletCache(
            PrismCanonicalChunkSnapshot snapshot, Matrix4x4 worldFromChunk,
            out ContactMeshletBuffers meshlets, out string error)
        {
            meshlets = null;
            error = null;
            if (!PrismCanonicalChunkCodec.TryValidate(snapshot, out error))
                return false;
            try
            {
                meshlets = new ContactMeshletBuffers(
                    Math.Max(1, snapshot.MeshletVertexCount),
                    Math.Max(1, snapshot.MeshletIndexCount),
                    Math.Max(1, snapshot.MeshletDescriptorCount));
                ContactMeshletGenerationBuffers target = meshlets.Published;
                Upload<ContactMeshletVertexGpu>(target.Vertices,
                    snapshot.MeshletVertices);
                Upload<uint>(target.Indices, snapshot.MeshletIndices);
                Upload<ContactMeshletDescriptorGpu>(target.Descriptors,
                    snapshot.MeshletDescriptors);
                ConfigureMeshletGeneration(target, snapshot);
                target.Generation = Math.Max(1u, snapshot.MeshletGeneration);
                meshlets.SetChunkTransform(worldFromChunk);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                               exception is InvalidOperationException ||
                                               exception is OverflowException)
            {
                meshlets?.Dispose();
                meshlets = null;
                error = "PRISM meshlet cache rehydration failed: " +
                    exception.Message;
                return false;
            }
        }

        public static bool TryRestoreInPlace(PrismCanonicalChunkSnapshot snapshot,
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ContactDisplacementPool displacement, ContactMeshletBuffers meshlets,
            Matrix4x4 worldFromChunk, out string error)
        {
            error = Validate(snapshot, films, boundaries, displacement, meshlets);
            if (error != null) return false;
            try
            {
                Upload<ContactFilmHeaderGpu>(films.Headers, snapshot.FilmHeaders);
                Upload<Vector4>(films.Information, snapshot.FilmInformation);
                Upload<ContactFilmSlotStateGpu>(films.SlotStates,
                    snapshot.FilmSlotStates);
                Upload<uint>(films.ActiveIndices, snapshot.ActiveFilmIndices);
                Upload<uint>(films.DirtyIndices, snapshot.DirtyFilmIndices);
                Upload<uint>(films.Allocator, snapshot.FilmAllocatorState);
                PressureManifoldPool manifolds = films.Manifolds;
                Upload<PressureManifoldHeaderGpu>(manifolds.Headers,
                    snapshot.PressureManifoldHeaders);
                Upload<FilmMembershipGpu>(manifolds.Memberships,
                    snapshot.FilmMemberships);
                Upload<uint>(manifolds.Allocator,
                    snapshot.ManifoldAllocatorState);
                Upload<uint>(manifolds.Current,
                    snapshot.CurrentManifoldState);
                Upload<SupportContourPageGpu>(manifolds.SupportContourPages,
                    snapshot.SupportContourPages);
                Upload<SupportContourSegmentGpu>(manifolds.SupportContours,
                    snapshot.SupportContours);
                Upload<SurfaceHalfEdgeGpu>(manifolds.HalfEdges,
                    snapshot.SurfaceHalfEdges);
                Upload<FrontierLoopGpu>(manifolds.FrontierLoops,
                    snapshot.FrontierLoops);
                Upload<ContinuationEvidenceGpu>(manifolds.ContinuationEvidence,
                    snapshot.ContinuationEvidence);
                Upload<ElasticChartStateGpu>(manifolds.ElasticStates,
                    snapshot.ElasticChartStates);
                Upload<Vector4>(manifolds.FilmTopologyRanges,
                    snapshot.FilmTopologyRanges);
                Upload<uint>(manifolds.AtlasAllocator,
                    snapshot.AtlasAllocatorState);
                Upload<CrossChunkTopologyPortalGpu>(manifolds.CrossChunkPortals,
                    snapshot.CrossChunkTopologyPortals);
                Upload<ContactBoundaryHeaderGpu>(boundaries.Headers,
                    snapshot.BoundaryHeaders);
                Upload<Vector4>(boundaries.Information,
                    snapshot.BoundaryInformation);
                Upload<BoundaryCurveTopologyGpu>(boundaries.Topology,
                    snapshot.BoundaryCurveTopology);

                int baseHeaderBytes = checked(snapshot.DisplacementBasePageCount *
                    DisplacementPageHeaderGpu.Stride);
                UploadRange<DisplacementPageHeaderGpu>(displacement.PageHeaders,
                    snapshot.DisplacementPageHeaders, 0, baseHeaderBytes, 0);
                UploadRange<DisplacementPageHeaderGpu>(displacement.PageHeaders,
                    snapshot.DisplacementPageHeaders, baseHeaderBytes,
                    snapshot.DisplacementPageHeaders.Length - baseHeaderBytes,
                    displacement.BasePageCapacity);
                Upload<DisplacementCellGpu>(displacement.BaseCells,
                    snapshot.DisplacementBaseCells);
                Upload<DisplacementCellGpu>(displacement.MicroCells,
                    snapshot.DisplacementMicroCells);
                Upload<uint>(displacement.BaseChildPages,
                    snapshot.DisplacementBaseChildren);
                Upload<uint>(displacement.MicroChildPages,
                    snapshot.DisplacementMicroChildren);
                Upload<ContactTopologyEvidenceGpu>(displacement.TopologyEvidence,
                    snapshot.TopologyEvidence);

                boundaries.Allocator.SetData(new[]
                {
                    (uint)snapshot.BoundaryCount, (uint)snapshot.BoundaryCount, 0u,
                    Math.Max(1u, snapshot.BoundaryGeneration)
                });
                displacement.Allocator.SetData(new[]
                {
                    (uint)snapshot.DisplacementBasePageCount,
                    (uint)snapshot.DisplacementMicroPageCount,
                    0u, 0u, Math.Max(1u, snapshot.DisplacementGeneration),
                    0u, 0u, 0u
                });

                meshlets.EnsureCapacity(Math.Max(1, snapshot.MeshletVertexCount),
                    Math.Max(1, snapshot.MeshletIndexCount),
                    Math.Max(1, snapshot.MeshletDescriptorCount),
                    Math.Max(1, snapshot.FilmCount));
                if (!meshlets.TryBeginBuild(out ContactMeshletGenerationBuffers target))
                {
                    error = "PRISM inactive meshlet generation is still fenced.";
                    return false;
                }
                Upload<ContactMeshletVertexGpu>(target.Vertices,
                    snapshot.MeshletVertices);
                Upload<uint>(target.Indices, snapshot.MeshletIndices);
                Upload<ContactMeshletDescriptorGpu>(target.Descriptors,
                    snapshot.MeshletDescriptors);
                ConfigureMeshletGeneration(target, snapshot);
                meshlets.Publish(Math.Max(1u, snapshot.MeshletGeneration));
                meshlets.SetChunkTransform(worldFromChunk);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                               exception is InvalidOperationException ||
                                               exception is OverflowException)
            {
                error = "PRISM GPU rehydration failed: " + exception.Message;
                return false;
            }
        }

        public static void ClearInPlace(ContactFilmPool films,
            ContactBoundaryPool boundaries, ContactDisplacementPool displacement,
            ContactMeshletBuffers meshlets, uint generation,
            Matrix4x4 worldFromChunk)
        {
            generation = Math.Max(1u, generation);
            films?.Allocator?.SetData(new[]
                { 0u, 0u, 0u, generation, 0u, 0u, 0u, 0u });
            if (films?.Manifolds != null)
            {
                films.Manifolds.Allocator.SetData(new[]
                {
                    0u, 0u, 0u, generation,
                    0u, 0u, 0u, generation
                });
                films.Manifolds.Current.SetData(new uint[4]);
                films.Manifolds.Diagnostics.SetData(new uint[
                    PressureManifoldPool.DiagnosticWords]);
            }
            boundaries?.Allocator?.SetData(new[] { 0u, 0u, 0u, generation });
            displacement?.Allocator?.SetData(new[]
                { 0u, 0u, 0u, 0u, generation, 0u, 0u, 0u });
            if (meshlets == null || meshlets.IsDisposed) return;
            if (!meshlets.TryBeginBuild(out ContactMeshletGenerationBuffers target))
                return;
            target.BuildCounters.SetData(new uint[8]);
            target.DrawArguments.SetData(new[] { 0u, 1u, 0u, 0u });
            target.BuildDispatchArguments.SetData(new[]
                { 0u, 1u, 1u, 0u, 1u, 1u });
            target.CullDispatchArguments.SetData(new[] { 0u, 1u, 1u });
            meshlets.Publish(generation);
            meshlets.SetChunkTransform(worldFromChunk);
        }

        private static string Validate(PrismCanonicalChunkSnapshot snapshot,
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ContactDisplacementPool displacement, ContactMeshletBuffers meshlets)
        {
            if (!PrismCanonicalChunkCodec.TryValidate(snapshot, out string codecError))
                return codecError;
            if (films == null || films.IsDisposed || boundaries == null ||
                boundaries.IsDisposed || displacement == null ||
                displacement.IsDisposed || meshlets == null || meshlets.IsDisposed)
                return "Live PRISM GPU arenas are required for rehydration.";
            if (snapshot.FilmCount > films.Capacity ||
                snapshot.BoundaryCount > boundaries.Capacity ||
                snapshot.DisplacementBasePageCount >
                    displacement.BasePageCapacity ||
                snapshot.DisplacementMicroPageCount >
                    displacement.MicroPageCapacity ||
                snapshot.ManifoldCount > films.Manifolds.ManifoldCapacity)
                return "PRISM chunk exceeds the configured resident GPU arena.";
            if (snapshot.SupportContourPageCount >
                    films.Manifolds.ContourPageCapacity ||
                snapshot.SupportContourSegmentCount >
                    films.Manifolds.ContourSegmentCapacity ||
                snapshot.SurfaceHalfEdgeCount >
                    films.Manifolds.HalfEdgeCapacity ||
                snapshot.FrontierLoopCount >
                    films.Manifolds.FrontierLoopCapacity ||
                snapshot.ContinuationEvidenceCount >
                    films.Manifolds.ContinuationEvidenceCapacity ||
                snapshot.CrossChunkPortalCount > films.Manifolds.PortalCapacity)
                return "PRISM topology atlas exceeds the resident GPU arena.";
            return null;
        }

        private static void Upload<T>(GraphicsBuffer destination, byte[] bytes)
            where T : struct => UploadRange<T>(destination, bytes, 0,
                bytes?.Length ?? 0, 0);

        private static void UploadRange<T>(GraphicsBuffer destination, byte[] bytes,
            int byteOffset, int byteCount, int destinationElement) where T : struct
        {
            if (byteCount == 0) return;
            if (bytes == null || byteOffset < 0 || byteCount < 0 ||
                byteOffset + byteCount > bytes.Length)
                throw new ArgumentException("PRISM upload range is invalid.");
            int typeSize = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();
            if (byteCount % typeSize != 0)
                throw new ArgumentException("PRISM upload is not stride aligned.");
            using var nativeBytes = new NativeArray<byte>(byteCount,
                Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(bytes, byteOffset, nativeBytes, 0, byteCount);
            NativeArray<T> values = nativeBytes.Reinterpret<T>(sizeof(byte));
            destination.SetData(values, 0, destinationElement, values.Length);
        }

        private static uint DivideRoundUp(uint value, uint divisor) =>
            value == 0u ? 0u : (value + divisor - 1u) / divisor;

        private static void ConfigureMeshletGeneration(
            ContactMeshletGenerationBuffers target,
            PrismCanonicalChunkSnapshot snapshot)
        {
            target.BuildCounters.SetData(new[]
            {
                (uint)snapshot.MeshletVertexCount,
                (uint)snapshot.MeshletIndexCount,
                0u,
                (uint)snapshot.MeshletDescriptorCount,
                (uint)snapshot.MeshletDescriptorCount,
                0u, 0u, 0u
            });
            target.DrawArguments.SetData(new[]
            {
                (uint)snapshot.MeshletIndexCount, 1u, 0u, 0u
            });
            target.BuildDispatchArguments.SetData(new[]
            {
                DivideRoundUp((uint)snapshot.FilmCount, 64u), 1u, 1u,
                0u, 1u, 1u
            });
            target.CullDispatchArguments.SetData(new[]
            {
                DivideRoundUp((uint)snapshot.MeshletDescriptorCount, 64u),
                1u, 1u
            });
        }
    }
}
