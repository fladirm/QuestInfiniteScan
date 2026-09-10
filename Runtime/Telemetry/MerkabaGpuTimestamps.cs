using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    internal enum MerkabaGpuStage : byte
    {
        DepthPreprocess,
        SurfaceIntegration,
        DualIntegration,
        WorldQuery,
        FlowerClassify,
        FlowerCompact,
        FlowerPublish,
        FlowerCull,
        MerkabaDraw,
        Count
    }

    internal enum CaptureOwner : byte
    {
        Observation,
        FlowerPages,
        Draw,
        DepthSnapshotCopy,
        PcaObservationCopy,
        PcaHistoryCopy,
        Count
    }

    /// <summary>
    /// Periodic Vulkan timestamps recorded in the same command buffers as each
    /// measured compute dispatch and the actual URP Merkaba draw. Normal frames
    /// emit no plugin events or timing readbacks.
    /// </summary>
    internal static class MerkabaGpuTimestamps
    {
        internal const int RefineRadialBinCount = 3;
        internal const int RefineMetricCount = 8;
        internal const int RefineMetricValueCount =
            RefineRadialBinCount * RefineMetricCount;

        private const int MaximumTimedEntries = 4096;
        private const int SubmissionTimestampCount = 2;
        private const int OwnerStride = SubmissionTimestampCount +
            MaximumTimedEntries * 2;
        private const float InitialSampleDelaySeconds = 2f;
        private const float SampleIntervalSeconds = 5f;
        private const float UnavailableRetrySeconds = 30f;

        private enum CaptureState : byte
        {
            Idle,
            Recording,
            AwaitingResults
        }

        private sealed class TimingEntry
        {
            internal TimingEntry(MerkabaGpuStage stage, string name)
            {
                Stage = stage;
                Name = name;
            }

            internal MerkabaGpuStage Stage { get; }
            internal string Name { get; }
        }

        private class Aggregate
        {
            internal int Invocations;
            internal double TotalNanoseconds;
            internal double MaximumNanoseconds;
        }

        private sealed class SessionAggregate : Aggregate
        {
            internal int Samples;
        }

        private sealed class SampleMetrics
        {
            internal uint Revision;
            internal int PendingReadbacks;
            internal bool TimingComplete;
            internal bool ReadbackValid = true;
            internal bool Logged;
            internal bool MetricsCaptureRequested;
            internal bool RefineAvailable;
            internal uint BlockCount;
            internal uint ChunkCount;
            internal uint HotTiles;
            internal uint ColdTiles;
            internal uint HashCollisions;
            internal uint HashProbes;
            internal uint HashMaxProbe;
            internal uint BlockOverflow;
            internal uint ChunkOverflow;
            internal uint TileStarvation;
            internal uint HashFull;
            internal uint UnresolvedSurfaceTiles;
            internal uint SurfaceTilesAllocated;
            internal uint ScanColdMisses;
            internal uint DualQueryBlocks;
            internal uint ObservationChangeMask;
            internal uint DirtyTileCount;
            internal uint ThroughEvidenceDecrements;
            internal uint ThroughOccupiedToFree;
            internal uint UnresolvedObservationTiles;
            internal uint LoadRequests;
            internal uint WritebackTiles;
            internal uint FailedReads;
            internal uint FailedWrites;
            internal uint StorageBackpressure;
            internal uint DualStorageIntents;
            internal uint DualTouchPublication;
            internal uint ObservationFailure;
            internal uint FailedObservations;
            internal readonly uint[] RefineRadial =
                new uint[RefineMetricValueCount];
            internal float LoadBytesPerSecond;
            internal float WriteBytesPerSecond;
            internal float LoadLatencyP50Ms;
            internal float LoadLatencyP95Ms;
            internal float WriteLatencyP50Ms;
            internal float WriteLatencyP95Ms;
        }

        private static readonly string[] StageNames =
        {
            "DEPTH_PREPROCESS",
            "SURFACE_INTEGRATION",
            "DUAL_INTEGRATION",
            "M8_WORLD_QUERY",
            "FLOWER_CLASSIFY",
            "FLOWER_COMPACT",
            "FLOWER_PUBLISH",
            "FLOWER_CULL",
            "MERKABA_DRAW"
        };
        private static readonly Dictionary<(ulong Shader, int Kernel), TimingEntry>
            EntriesByKernel = new();
        private static readonly List<TimingEntry> EntrySequence =
            new(MaximumTimedEntries);
        private static readonly TimingEntry DrawEntry = new(
            MerkabaGpuStage.MerkabaDraw, "Flower.DrawIndexedIndirectCount");
        private static readonly TimingEntry PcaHistoryCopyEntry = new(
            MerkabaGpuStage.DepthPreprocess,
            "PassthroughCameraProvider.CopyOwnedHistory");
        private static readonly TimingEntry PcaObservationCopyEntry = new(
            MerkabaGpuStage.DepthPreprocess,
            "MerkabaIntegrator.CopyOwnedPcaObservation");
        private static readonly ulong[] TimestampPairs =
            new ulong[OwnerStride];
        private static readonly Dictionary<TimingEntry, SessionAggregate>
            SessionTotals = new();

        private static CaptureState _state;
        private static CaptureOwner _scheduledOwner;
        private static CaptureOwner _activeOwner;
        private static uint _revision;
        private static float _nextSampleTime;
        private static float _nextCounterLogTime;
        private static float _nextNativeRefineSampleTime;
        private static bool _nativeRefineReadbackPending;
        private static float _lastStorageSubmissionTime;
        private static bool _storageCapture;
        private static bool _submissionBegan;
        private static bool _submissionEnded;
        private static int _lastCoverageSeen;
#if !UNITY_EDITOR
        private static bool _unavailableWarningLogged;
#endif
        private static SampleMetrics _metrics;
#if UNITY_EDITOR
        private static bool _testAvailable;
#endif

        internal static bool IsOwnerRecording(CaptureOwner owner) =>
            _state == CaptureState.Recording && _activeOwner == owner;
        internal static uint CurrentRevision => _revision;
        internal static bool ShouldCaptureNativeRefineMetrics =>
            !_nativeRefineReadbackPending &&
            Time.unscaledTime >= _nextNativeRefineSampleTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            EntriesByKernel.Clear();
            EntrySequence.Clear();
            SessionTotals.Clear();
            _state = CaptureState.Idle;
            _scheduledOwner = CaptureOwner.Observation;
            _activeOwner = CaptureOwner.Count;
            _revision = 0u;
            _nextSampleTime = float.PositiveInfinity;
            _nextCounterLogTime = 0f;
            _nextNativeRefineSampleTime = 0f;
            _nativeRefineReadbackPending = false;
            _lastStorageSubmissionTime = float.NegativeInfinity;
            _storageCapture = false;
            _submissionBegan = false;
            _submissionEnded = false;
            _lastCoverageSeen = -1;
#if !UNITY_EDITOR
            _unavailableWarningLogged = false;
#endif
            _metrics = null;
#if UNITY_EDITOR
            _testAvailable = false;
#endif
        }

        internal static int FindProfiledKernel(this ComputeShader shader,
            string kernelName, MerkabaGpuStage stage)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (string.IsNullOrWhiteSpace(kernelName))
                throw new ArgumentException("Kernel name is required.",
                    nameof(kernelName));
            if ((uint)stage >= (uint)MerkabaGpuStage.Count)
                throw new ArgumentOutOfRangeException(nameof(stage));
            int kernel = shader.FindKernel(kernelName);
            RegisterKernel(shader, kernel, stage, kernelName);
            return kernel;
        }

        internal static void RegisterKernel(ComputeShader shader, int kernel,
            MerkabaGpuStage stage, string kernelName)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if ((uint)stage >= (uint)MerkabaGpuStage.Count)
                throw new ArgumentOutOfRangeException(nameof(stage));
            string name = shader.name + '.' + kernelName;
            var key = (EntityId.ToULong(shader.GetEntityId()), kernel);
            if (EntriesByKernel.TryGetValue(key, out TimingEntry existing))
            {
                if (existing.Name != name || existing.Stage != stage)
                    throw new InvalidOperationException(
                        $"GPU timing registration collision: {existing.Name} " +
                        $"versus {name}.");
                return;
            }
            EntriesByKernel.Add(key, new TimingEntry(stage, name));
        }

        internal static void NotifyScanStarted()
        {
            if (_state == CaptureState.Idle)
                _nextSampleTime = Time.unscaledTime + InitialSampleDelaySeconds;
        }

        internal static bool IsOwnerEligible(CaptureOwner owner)
        {
            return _state == CaptureState.Idle && owner == _scheduledOwner &&
                   Time.unscaledTime >= _nextSampleTime;
        }

        internal static bool TryAcquire(CaptureOwner owner, uint revision,
            CommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!TryAcquireState(owner, revision)) return false;
            RecordProfileBegin(command);
            return true;
        }

        internal static bool TryAcquire(CaptureOwner owner, uint revision,
            RasterCommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!TryAcquireState(owner, revision)) return false;
            RecordProfileBegin(command);
            return true;
        }

        internal static bool TryAcquireStorage(uint revision, CommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            _lastStorageSubmissionTime = Time.unscaledTime;
            // Native observations have their own query pool. Reuse that idle
            // managed owner slot for actual serialized storage batches, with
            // an explicit log scope; the native timestamp owner ABI is unchanged.
            if (!TryAcquireState(CaptureOwner.Observation, revision, true)) return false;
            RecordProfileBegin(command);
            return true;
        }

        private static bool TryAcquireState(CaptureOwner owner, uint revision,
            bool storage = false)
        {
            Poll();
#if !UNITY_EDITOR && UNITY_ANDROID
            // Android scanner dispatches are already timed by the native job
            // query pool. Waiting for its removed managed observation owner
            // would permanently starve current-view cull and real draw samples.
            if (_state == CaptureState.Idle && _scheduledOwner == CaptureOwner.Observation &&
                MerkabaNativeVulkanExecutor.IsAvailable && !storage &&
                Time.unscaledTime - _lastStorageSubmissionTime > SampleIntervalSeconds)
                AdvanceScheduledOwner();
#endif
            if ((uint)owner >= (uint)CaptureOwner.Count ||
                _state != CaptureState.Idle || revision == 0u ||
                owner != _scheduledOwner)
                return false;
#if UNITY_EDITOR
            if (!_testAvailable)
                return false;
#else
            if (Time.unscaledTime < _nextSampleTime)
                return false;
            if (!Native.TryArm(owner, revision))
            {
                if (!_unavailableWarningLogged)
                {
                    _unavailableWarningLogged = true;
                    Logger.Warning("Merkaba GPU kernel timestamps unavailable; " +
                                   "Vulkan query plugin was not loaded.");
                }
                _nextSampleTime = Time.unscaledTime + UnavailableRetrySeconds;
                return false;
            }
#endif
            EntrySequence.Clear();
            _revision = revision;
            _metrics = new SampleMetrics { Revision = revision };
            _submissionBegan = false;
            _submissionEnded = false;
            _activeOwner = owner;
            _storageCapture = storage;
            _state = CaptureState.Recording;
            return true;
        }

        private static void RecordProfileBegin(CommandBuffer command)
        {
            if (_state != CaptureState.Recording || _submissionBegan)
                return;
            if (command == null) throw new ArgumentNullException(nameof(command));
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionBegin));
#endif
            _submissionBegan = true;
        }

        private static void RecordProfileBegin(RasterCommandBuffer command)
        {
            if (_state != CaptureState.Recording || _submissionBegan)
                return;
            if (command == null) throw new ArgumentNullException(nameof(command));
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionBegin));
#endif
            _submissionBegan = true;
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, int x, int y, int z)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            ValidateDispatchDimensions(x, y, z);
            bool timed = Observe(shader, kernel);
            RecordDispatchEvent(command, timed, true);
            command.DispatchCompute(shader, kernel, x, y, z);
            RecordDispatchEvent(command, timed, false);
        }

        internal static void DrawProceduralIndirectProfiled(
            this RasterCommandBuffer command, Matrix4x4 matrix,
            Material material, int shaderPass, MeshTopology topology,
            ComputeBuffer arguments, int argumentsOffset)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            bool timed = Observe(DrawEntry);
            RecordDrawEvent(command, timed, true);
            command.DrawProceduralIndirect(matrix, material, shaderPass,
                topology, arguments, argumentsOffset);
            RecordDrawEvent(command, timed, false);
        }

        internal static void DrawProceduralIndirectProfiled(
            this RasterCommandBuffer command, GraphicsBuffer indexBuffer,
            Matrix4x4 matrix, Material material, int shaderPass,
            MeshTopology topology, ComputeBuffer arguments,
            int argumentsOffset)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (indexBuffer == null)
                throw new ArgumentNullException(nameof(indexBuffer));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            bool timed = Observe(DrawEntry);
            RecordDrawEvent(command, timed, true);
            command.DrawProceduralIndirect(indexBuffer, matrix, material,
                shaderPass, topology, arguments, argumentsOffset);
            RecordDrawEvent(command, timed, false);
        }

        internal static void DrawMeshInstancedIndirectProfiled(
            this RasterCommandBuffer command, Mesh mesh, int submeshIndex,
            Material material, int shaderPass, ComputeBuffer arguments,
            int argumentsOffset)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            bool timed = Observe(DrawEntry);
            RecordDrawEvent(command, timed, true);
            command.DrawMeshInstancedIndirect(mesh, submeshIndex, material,
                shaderPass, arguments, argumentsOffset);
            RecordDrawEvent(command, timed, false);
        }

        internal static void BlitPcaHistoryProfiled(this CommandBuffer command,
            Texture source, RenderTexture destination, bool timedSubmission)
        {
            BlitProfiled(command, source, destination, PcaHistoryCopyEntry,
                timedSubmission);
        }

        internal static void BlitPcaObservationProfiled(
            this CommandBuffer command, Texture source,
            RenderTexture destination, bool timedSubmission)
        {
            BlitProfiled(command, source, destination,
                PcaObservationCopyEntry, timedSubmission);
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, ComputeBuffer arguments,
            uint offset = 0u)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            bool timed = Observe(shader, kernel);
            RecordDispatchEvent(command, timed, true);
            command.DispatchCompute(shader, kernel, arguments, offset);
            RecordDispatchEvent(command, timed, false);
        }

        internal static void End(CaptureOwner owner, CommandBuffer command,
            bool acquired)
        {
            if (!acquired) return;
            RequireActiveOwner(owner);
            RecordProfileEnd(command);
        }

        internal static void End(CaptureOwner owner,
            RasterCommandBuffer command, bool acquired)
        {
            if (!acquired) return;
            RequireActiveOwner(owner);
            RecordProfileEnd(command);
        }

        private static void RecordProfileEnd(CommandBuffer command)
        {
            if (_state != CaptureState.Recording)
                return;
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!_submissionBegan)
            {
                CancelFrame();
                return;
            }
            if (_submissionEnded)
                throw new InvalidOperationException(
                    "GPU timing submission was ended more than once.");
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionEnd));
#endif
            _submissionEnded = true;
        }

        private static void RecordProfileEnd(RasterCommandBuffer command)
        {
            if (_state != CaptureState.Recording)
                return;
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!_submissionBegan)
            {
                CancelFrame();
                return;
            }
            if (_submissionEnded)
                throw new InvalidOperationException(
                    "GPU timing submission was ended more than once.");
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionEnd));
#endif
            _submissionEnded = true;
        }

        internal static void Complete(CaptureOwner owner, bool acquired,
            bool submitted)
        {
            if (!acquired) return;
            RequireActiveOwner(owner);
            if (!submitted)
            {
                CancelFrame();
                return;
            }
            if (!_submissionEnded)
                throw new InvalidOperationException(
                    "GPU timing submission completed without a matching End.");
            _state = CaptureState.AwaitingResults;
#if !UNITY_EDITOR
            _nextSampleTime = Time.unscaledTime + SampleIntervalSeconds;
#endif
        }

        private static void RequireActiveOwner(CaptureOwner owner)
        {
            if (_state != CaptureState.Recording || _activeOwner != owner)
                throw new InvalidOperationException(
                    $"GPU timing owner mismatch: active={_activeOwner}, caller={owner}.");
        }

        internal static void Poll()
        {
            if (_state != CaptureState.AwaitingResults)
                return;
#if UNITY_EDITOR
            return;
#else
            int status = Native.TryRead(_activeOwner, TimestampPairs,
                out CaptureOwner capturedOwner, out int entryCount,
                out double timestampPeriod, out int validBits,
                out ulong capturedRevision, out bool overflow);
            if (status == 0)
                return;
            bool valid = IsTimestampSampleValid(status, overflow,
                capturedOwner, _activeOwner, capturedRevision, _revision,
                entryCount, EntrySequence.Count);
            double submissionNanoseconds = 0.0;
            double entryNanoseconds = 0.0;
            if (valid)
                valid = TryValidateTimestampBounds(entryCount,
                    timestampPeriod, validBits, out submissionNanoseconds,
                    out entryNanoseconds);
            if (valid)
            {
                LogTimings(entryCount, timestampPeriod, validBits,
                    submissionNanoseconds, entryNanoseconds);
                AdvanceScheduledOwner();
            }
            else
                Logger.Warning($"Merkaba GPU timestamp sample invalid " +
                               $"owner={_activeOwner} " +
                               $"nativeOwner={capturedOwner} " +
                               $"revision={_revision} nativeRevision=" +
                               $"{capturedRevision} expectedEntries=" +
                               $"{EntrySequence.Count} actualEntries=" +
                               $"{entryCount} overflow={overflow}");
            if (_metrics != null)
            {
                _metrics.TimingComplete = true;
                _metrics.ReadbackValid &= valid;
                TryLogMetrics(_metrics);
            }
            FinishFrame();
#endif
        }

        private static void AdvanceScheduledOwner()
        {
            _scheduledOwner = (CaptureOwner)(((int)_scheduledOwner + 1) %
                (int)CaptureOwner.Count);
        }

        internal static void CaptureM8Metrics(MerkabaGrid grid)
        {
            SampleMetrics sample = RecordingMetrics();
            if (sample == null || grid == null || grid.M8Counters == null ||
                grid.GpuSubmissionSuspended || sample.MetricsCaptureRequested)
                return;
            sample.MetricsCaptureRequested = true;
            grid.CaptureStorageMetrics(out sample.LoadBytesPerSecond,
                out sample.WriteBytesPerSecond, out sample.LoadLatencyP50Ms,
                out sample.LoadLatencyP95Ms, out sample.WriteLatencyP50Ms,
                out sample.WriteLatencyP95Ms);
            sample.PendingReadbacks++;
            AsyncGPUReadback.Request(grid.M8Counters, request =>
            {
                if (request.hasError)
                    sample.ReadbackValid = false;
                else
                {
                    var values = request.GetData<uint>();
                    sample.BlockCount = values[MerkabaGrid.CounterBlockCount];
                    sample.ChunkCount = values[MerkabaGrid.CounterChunkCount];
                    sample.HotTiles = values[MerkabaGrid.CounterHotTileCount];
                    sample.ColdTiles = values[MerkabaGrid.CounterColdTileCount];
                    sample.HashCollisions = values[
                        MerkabaGrid.CounterHashCollisions];
                    sample.HashProbes = values[MerkabaGrid.CounterHashProbes];
                    sample.HashMaxProbe = values[
                        MerkabaGrid.CounterHashMaxProbe];
                    sample.BlockOverflow = values[
                        MerkabaGrid.CounterBlockOverflow];
                    sample.ChunkOverflow = values[
                        MerkabaGrid.CounterChunkOverflow];
                    sample.TileStarvation = values[
                        MerkabaGrid.CounterTileStarvation];
                    sample.UnresolvedSurfaceTiles = values[
                        MerkabaGrid.CounterUnresolvedSurfaceTiles];
                    sample.SurfaceTilesAllocated = values[
                        MerkabaGrid.CounterSurfaceTilesAllocated];
                    sample.ScanColdMisses = values[
                        MerkabaGrid.CounterScanColdMisses];
                    sample.LoadRequests = values[
                        MerkabaGrid.CounterLoadRequests];
                    sample.HashFull = values[MerkabaGrid.CounterHashFull];
                    sample.FailedReads = values[
                        MerkabaGrid.CounterFailedReads];
                    sample.FailedWrites = values[
                        MerkabaGrid.CounterFailedWrites];
                    sample.StorageBackpressure = values[
                        MerkabaGrid.CounterStorageBackpressure];
                    sample.DualStorageIntents = values[
                        MerkabaGrid.CounterDualStorageIntentCount];
                    sample.DualTouchPublication = values[
                        MerkabaGrid.CounterDualTouchPublication];
                    sample.DualQueryBlocks = values[
                        MerkabaGrid.CounterDualQueryBlocks];
                    sample.WritebackTiles = values[
                        MerkabaGrid.CounterWritebackTiles];
                    sample.ObservationFailure = values[
                        MerkabaGrid.CounterObservationFailure];
                    sample.FailedObservations = values[
                        MerkabaGrid.CounterFailedObservations];
                    sample.ObservationChangeMask = values[
                        MerkabaGrid.CounterObservationChangeMask];
                    sample.DirtyTileCount = values[
                        MerkabaGrid.CounterDirtyTileCount];
                    sample.ThroughEvidenceDecrements = values[
                        MerkabaGrid.CounterThroughEvidenceDecrements];
                    sample.ThroughOccupiedToFree = values[
                        MerkabaGrid.CounterThroughOccupiedToFree];
                    sample.UnresolvedObservationTiles = values[
                        MerkabaGrid.CounterUnresolvedObservationTiles];
                }
                sample.PendingReadbacks--;
                TryLogMetrics(sample);
            });

            DepthCapture depthCapture = DepthCapture.Instance;
            if (depthCapture != null && depthCapture.TryGetRefineMetrics(
                    sample.Revision, out ComputeBuffer refineMetrics,
                    out int refineMetricValueCount))
            {
                sample.PendingReadbacks++;
                AsyncGPUReadback.Request(refineMetrics,
                    refineMetricValueCount * sizeof(uint), 0, request =>
                    {
                        if (request.hasError)
                            sample.ReadbackValid = false;
                        else
                        {
                            sample.RefineAvailable = true;
                            var values = request.GetData<uint>();
                            for (int index = 0; index < values.Length; index++)
                            {
                                int metric = index % RefineMetricValueCount;
                                sample.RefineRadial[metric] += values[index];
                            }
                        }
                        sample.PendingReadbacks--;
                        TryLogMetrics(sample);
                    });
            }
        }

        // Called after native fence completion and Unity ownership reacquisition,
        // before the next observation can overwrite the per-WG counters. This
        // sampled readback never holds an observation or participates in admission.
        internal static void CaptureNativeRefineMetrics(ComputeBuffer buffer,
            int valueCount, uint observation, uint attempt, int depthVersion)
        {
            _nextNativeRefineSampleTime = Time.unscaledTime + SampleIntervalSeconds;
            if (_nativeRefineReadbackPending) return;
            string identity = $"observation={observation} attempt={attempt} " +
                $"depthVersion={depthVersion} source=native";
            if (buffer == null || valueCount <= 0 ||
                valueCount % RefineMetricValueCount != 0)
            {
                Logger.Warning($"Merkaba metrics-rgbd {identity} available=false reason=buffer");
                return;
            }
            _nativeRefineReadbackPending = true;
            try
            {
                AsyncGPUReadback.Request(buffer, checked(valueCount * sizeof(uint)), 0, request =>
                {
                    _nativeRefineReadbackPending = false;
                    if (request.hasError)
                    {
                        Logger.Warning($"Merkaba metrics-rgbd {identity} available=false reason=readback");
                        return;
                    }
                    var totals = new uint[RefineMetricValueCount];
                    var values = request.GetData<uint>();
                    for (int index = 0; index < values.Length; index++)
                        totals[index % RefineMetricValueCount] += values[index];
                    Logger.Info($"Merkaba metrics-rgbd {identity} available=true " +
                        $"center={FormatRefineBin(totals, 0)} " +
                        $"mid={FormatRefineBin(totals, 1)} " +
                        $"edge={FormatRefineBin(totals, 2)}");
                });
            }
            catch (Exception exception)
            {
                _nativeRefineReadbackPending = false;
                Logger.Warning($"Merkaba metrics-rgbd {identity} available=false reason={exception.Message}");
            }
        }

        // Consumes the existing storage telemetry snapshot. This method never
        // requests a GPU readback and never influences scan/publication/LOD.
        internal static void ObserveStorageCounterSample(Unity.Collections.NativeArray<uint> values)
        {
            if (values.Length < MerkabaGrid.CounterCount || Time.unscaledTime < _nextCounterLogTime)
                return;
            _nextCounterLogTime = Time.unscaledTime + SampleIntervalSeconds;
            uint observation = values[MerkabaGrid.CounterObservationToken];
            Logger.Info($"Merkaba metrics-flower observation={observation} " +
                $"compiledBatchActiveTriangles={values[MerkabaGrid.CounterReadoutEmittedTriangles]} " +
                $"compiledBatchIndexedVertexSlots={values[MerkabaGrid.CounterReadoutEmittedVertices]} " +
                $"unresolvedPageAttempts={values[MerkabaGrid.CounterReadoutUnresolved]} " +
                "scope=readout-batch-counters-not-current-view " +
                "visibleTriangles=NOT_AVAILABLE visibleVertices=NOT_AVAILABLE");
            Logger.Info($"Merkaba metrics-held-refinement observation={observation} " +
                $"completed={values[MerkabaGrid.CounterObservationCompleted]} " +
                $"pendingTiles={values[MerkabaGrid.CounterRefinementPendingTiles]} " +
                $"quantumProgress={values[MerkabaGrid.CounterRefinementWorkProgress]} " +
                $"backpressure={values[MerkabaGrid.CounterRefinementBackpressure]} " +
                $"unresolved={values[MerkabaGrid.CounterRefinementUnresolved]} " +
                $"stage={values[MerkabaGrid.CounterRefinementStage]} " +
                $"storageBackpressure={values[MerkabaGrid.CounterStorageBackpressure]} " +
                $"residencyEpoch={values[MerkabaGrid.CounterResidencyEpoch]} " +
                $"failure=0x{values[MerkabaGrid.CounterObservationFailure]:x} " +
                "source=existing-asynchronous-counter-snapshot");
            // The residency chain, in the order a measured endpoint travels it:
            // a candidate needs a tile, a tile needs a slot, a slot needs the
            // block/chunk above it, and only a touched tile reaches FlowerCommit.
            // Without these the first empty link is indistinguishable from the
            // last, and an empty readout looks the same at every stage.
            Logger.Info($"Merkaba metrics-residency observation={observation} " +
                $"hotTiles={values[MerkabaGrid.CounterHotTileCount]} " +
                $"coldTiles={values[MerkabaGrid.CounterColdTileCount]} " +
                $"freeTiles={values[MerkabaGrid.CounterFreeTileCount]} " +
                $"surfaceTilesAllocated={values[MerkabaGrid.CounterSurfaceTilesAllocated]} " +
                $"newTileQueue={values[MerkabaGrid.CounterNewTileQueueCount]} " +
                $"pendingNewTiles={values[MerkabaGrid.CounterPendingNewTileCount]} " +
                $"touchedTiles={values[MerkabaGrid.CounterTouchedTileCount]} " +
                $"dirtyTiles={values[MerkabaGrid.CounterDirtyTileCount]} " +
                $"occupiedKernels={values[MerkabaGrid.CounterOccupiedKernelCount]} " +
                $"unresolvedSurfaceTiles={values[MerkabaGrid.CounterUnresolvedSurfaceTiles]} " +
                $"unresolvedObservationTiles={values[MerkabaGrid.CounterUnresolvedObservationTiles]} " +
                $"tileStarvation={values[MerkabaGrid.CounterTileStarvation]} " +
                $"blockOverflow={values[MerkabaGrid.CounterBlockOverflow]} " +
                $"chunkOverflow={values[MerkabaGrid.CounterChunkOverflow]} " +
                $"hashFull={values[MerkabaGrid.CounterHashFull]} " +
                $"failedReads={values[MerkabaGrid.CounterFailedReads]} " +
                $"failedWrites={values[MerkabaGrid.CounterFailedWrites]} " +
                $"dualQueryBlocks={values[MerkabaGrid.CounterDualQueryBlocks]} " +
                $"changeMask=0x{values[MerkabaGrid.CounterObservationChangeMask]:x}");
        }

        internal static bool IsTimestampSampleValid(int status, bool overflow,
            CaptureOwner capturedOwner, CaptureOwner expectedOwner,
            ulong capturedRevision, ulong expectedRevision, int actualEntries,
            int expectedEntries) =>
            status > 0 && !overflow && capturedOwner == expectedOwner &&
            capturedRevision == expectedRevision &&
            actualEntries == expectedEntries;

        internal static bool IsEntryTotalWithinSubmission(
            double submissionNanoseconds, double entryNanoseconds) =>
            submissionNanoseconds >= 0.0 && entryNanoseconds >= 0.0 &&
            entryNanoseconds <= submissionNanoseconds * 1.10;

        internal static double ElapsedNanoseconds(ulong begin, ulong end,
            double timestampPeriod, int validBits)
        {
            if (timestampPeriod <= 0.0 || validBits <= 0 || validBits > 64)
                throw new ArgumentOutOfRangeException();
            ulong mask = validBits == 64
                ? ulong.MaxValue : (1UL << validBits) - 1UL;
            return ((end & mask) - (begin & mask) & mask) * timestampPeriod;
        }

        private static bool Observe(ComputeShader shader, int kernel)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (_state != CaptureState.Recording)
                return false;
            if (!EntriesByKernel.TryGetValue((
                    EntityId.ToULong(shader.GetEntityId()), kernel),
                    out TimingEntry entry))
                throw new InvalidOperationException(
                    $"GPU timing kernel {shader.name}#{kernel} was not registered.");
            return Observe(entry);
        }

        private static bool Observe(TimingEntry entry)
        {
            if (_state != CaptureState.Recording || !_submissionBegan)
                return false;
            if (EntrySequence.Count >= MaximumTimedEntries)
                throw new InvalidOperationException(
                    $"GPU timing capture exceeds {MaximumTimedEntries} entries.");
            EntrySequence.Add(entry);
            return true;
        }

        private static void RecordDispatchEvent(CommandBuffer command,
            bool timed, bool begin)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            if (timed)
                command.IssuePluginEvent(Native.RenderEvent,
                    Native.EventId(begin
                        ? Native.DispatchBegin : Native.DispatchEnd));
#endif
        }

        private static void BlitProfiled(CommandBuffer command,
            Texture source, RenderTexture destination, TimingEntry entry,
            bool timedSubmission)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            bool timed = timedSubmission && Observe(entry);
            RecordCopyEvent(command, timed, true);
            command.Blit(source, destination);
            RecordCopyEvent(command, timed, false);
        }

        private static void RecordCopyEvent(CommandBuffer command,
            bool timed, bool begin)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            if (timed)
                command.IssuePluginEvent(Native.RenderEvent,
                    Native.EventId(begin
                        ? Native.CopyBegin : Native.CopyEnd));
#endif
        }

        private static void RecordDrawEvent(RasterCommandBuffer command,
            bool timed, bool begin)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            if (timed)
                command.IssuePluginEvent(Native.RenderEvent,
                    Native.EventId(begin ? Native.DrawBegin : Native.DrawEnd));
#endif
        }

        private static void ValidateDispatchDimensions(int x, int y, int z)
        {
            const int maximum = 65535;
            if (x <= 0 || y <= 0 || z <= 0 ||
                x > maximum || y > maximum || z > maximum)
                throw new InvalidOperationException(
                    $"Illegal compute dispatch ({x},{y},{z}); each dimension " +
                    $"must be in [1,{maximum}].");
        }

        private static bool TryValidateTimestampBounds(int entryCount,
            double timestampPeriod, int validBits,
            out double submissionNanoseconds, out double entryNanoseconds)
        {
            submissionNanoseconds = ElapsedNanoseconds(TimestampPairs[0],
                TimestampPairs[1], timestampPeriod, validBits);
            entryNanoseconds = 0.0;
            for (int index = 0; index < entryCount; index++)
                entryNanoseconds += ElapsedNanoseconds(
                    TimestampPairs[SubmissionTimestampCount + index * 2],
                    TimestampPairs[SubmissionTimestampCount + index * 2 + 1],
                    timestampPeriod, validBits);
            return IsEntryTotalWithinSubmission(submissionNanoseconds,
                entryNanoseconds);
        }

        private static void LogTimings(int entryCount,
            double timestampPeriod, int validBits,
            double submissionNanoseconds, double entryNanoseconds)
        {
            var operationTotals = new Dictionary<TimingEntry, Aggregate>();
            var stageTotals = new Aggregate[(int)MerkabaGpuStage.Count];
            for (int stage = 0; stage < stageTotals.Length; stage++)
                stageTotals[stage] = new Aggregate();
            double checksum = 0.0;
            for (int index = 0; index < entryCount; index++)
            {
                double nanoseconds = ElapsedNanoseconds(
                    TimestampPairs[SubmissionTimestampCount + index * 2],
                    TimestampPairs[SubmissionTimestampCount + index * 2 + 1],
                    timestampPeriod, validBits);
                TimingEntry entry = EntrySequence[index];
                if (!operationTotals.TryGetValue(entry, out Aggregate operation))
                {
                    operation = new Aggregate();
                    operationTotals.Add(entry, operation);
                }
                Add(operation, nanoseconds);
                Add(stageTotals[(int)entry.Stage], nanoseconds);
                checksum += nanoseconds;
            }
            var ranked = new List<KeyValuePair<TimingEntry, Aggregate>>(
                operationTotals);
            ranked.Sort((left, right) => right.Value.TotalNanoseconds
                .CompareTo(left.Value.TotalNanoseconds));
            for (int index = 0; index < ranked.Count; index++)
            {
                TimingEntry entry = ranked[index].Key;
                Aggregate value = ranked[index].Value;
                UpdateSession(entry, value);
                Logger.Info($"Merkaba gpu-operation owner={(_storageCapture ? "Storage" : _activeOwner.ToString())} " +
                            $"revision={_revision} " +
                            $"rank={index + 1} stage=" +
                            $"{StageNames[(int)entry.Stage]} " +
                            $"name={entry.Name} invocations={value.Invocations} " +
                            $"total={value.TotalNanoseconds / 1000.0:F1}us " +
                            $"average={value.TotalNanoseconds / value.Invocations / 1000.0:F1}us " +
                            $"maximum={value.MaximumNanoseconds / 1000.0:F1}us");
            }
            for (int stage = 0; stage < stageTotals.Length; stage++)
            {
                Aggregate value = stageTotals[stage];
                Logger.Info($"Merkaba gpu-stage owner={(_storageCapture ? "Storage" : _activeOwner.ToString())} " +
                            $"revision={_revision} " +
                            $"stage={StageNames[stage]} " +
                            $"invocations={value.Invocations} " +
                            $"total={value.TotalNanoseconds / 1_000_000.0:F3}ms " +
                            $"maximum={value.MaximumNanoseconds / 1_000_000.0:F3}ms");
            }
            Logger.Info($"Merkaba gpu-sample owner={(_storageCapture ? "Storage" : _activeOwner.ToString())} " +
                        $"revision={_revision} " +
                        $"submissionGpuMs=" +
                        $"{submissionNanoseconds / 1_000_000.0:F3} " +
                        $"sumEntryGpuMs=" +
                        $"{entryNanoseconds / 1_000_000.0:F3} " +
                        $"entries={entryCount} operations={ranked.Count} " +
                        $"timestampPeriod={timestampPeriod:F6}ns " +
                        $"validBits={validBits}");
            LogSessionCoverage();
        }

        private static void UpdateSession(TimingEntry entry, Aggregate sample)
        {
            if (!SessionTotals.TryGetValue(entry, out SessionAggregate total))
            {
                total = new SessionAggregate();
                SessionTotals.Add(entry, total);
            }
            total.Samples++;
            total.Invocations += sample.Invocations;
            total.TotalNanoseconds += sample.TotalNanoseconds;
            total.MaximumNanoseconds = Math.Max(total.MaximumNanoseconds,
                sample.MaximumNanoseconds);
            Logger.Info($"Merkaba gpu-operation-session name={entry.Name} " +
                        $"samples={total.Samples} " +
                        $"invocations={total.Invocations} " +
                        $"average={total.TotalNanoseconds / total.Invocations / 1000.0:F1}us " +
                        $"maximum={total.MaximumNanoseconds / 1000.0:F1}us");
        }

        private static void LogSessionCoverage()
        {
            var registered = new HashSet<TimingEntry>(EntriesByKernel.Values)
            {
                DrawEntry,
                PcaHistoryCopyEntry,
                PcaObservationCopyEntry
            };
            var missing = new List<string>();
            foreach (TimingEntry entry in registered)
                if (!SessionTotals.ContainsKey(entry)) missing.Add(entry.Name);
            missing.Sort(StringComparer.Ordinal);
            int seen = registered.Count - missing.Count;
            Logger.Info($"Merkaba gpu-coverage registered={registered.Count} " +
                        $"seen={seen} " +
                        $"missing={missing.Count}");
            if (seen == _lastCoverageSeen) return;
            _lastCoverageSeen = seen;
            const int namesPerLine = 4;
            for (int start = 0; start < missing.Count; start += namesPerLine)
            {
                int count = Math.Min(namesPerLine, missing.Count - start);
                Logger.Info($"Merkaba gpu-coverage-missing names=" +
                    string.Join(",", missing.GetRange(start, count)));
            }
        }

        private static void Add(Aggregate aggregate, double nanoseconds)
        {
            aggregate.Invocations++;
            aggregate.TotalNanoseconds += nanoseconds;
            aggregate.MaximumNanoseconds = Math.Max(
                aggregate.MaximumNanoseconds, nanoseconds);
        }

        private static SampleMetrics RecordingMetrics() =>
            _state is CaptureState.Recording or CaptureState.AwaitingResults
                ? _metrics : null;

        private static void TryLogMetrics(SampleMetrics sample)
        {
            if (sample == null || sample.Logged ||
                !sample.MetricsCaptureRequested || !sample.TimingComplete ||
                sample.PendingReadbacks != 0)
                return;
            sample.Logged = true;
            Logger.Info($"Merkaba metrics-world revision={sample.Revision} " +
                        $"valid={sample.ReadbackValid} " +
                        $"m8Blocks={sample.BlockCount} " +
                        $"chunks={sample.ChunkCount} " +
                        $"hotTiles={sample.HotTiles} " +
                        $"coldTiles={sample.ColdTiles} " +
                        $"hashLoad={(double)sample.BlockCount / 32768.0:F4} " +
                        $"hashCollisions={sample.HashCollisions} " +
                        $"hashProbes={sample.HashProbes} " +
                        $"hashMaxProbe={sample.HashMaxProbe} " +
                        $"blockOverflow={sample.BlockOverflow} " +
                        $"chunkOverflow={sample.ChunkOverflow} " +
                        $"physicalTileStarvation={sample.TileStarvation} " +
                        $"hashFull={sample.HashFull}");
            Logger.Info($"Merkaba metrics-reconstruction revision=" +
                        $"{sample.Revision} valid={sample.ReadbackValid} " +
                        $"unresolvedSurfaceTiles={sample.UnresolvedSurfaceTiles} " +
                        $"surfaceTilesAllocated={sample.SurfaceTilesAllocated} " +
                        $"scanColdMisses={sample.ScanColdMisses}");
            Logger.Info($"Merkaba metrics-dual revision={sample.Revision} " +
                        $"valid={sample.ReadbackValid} " +
                        $"dualQueryBlocks={sample.DualQueryBlocks} " +
                        $"throughEvidenceDecrements={sample.ThroughEvidenceDecrements} " +
                        $"throughOccupiedToFreeTransitions={sample.ThroughOccupiedToFree} " +
                        $"unresolvedObservationTiles={sample.UnresolvedObservationTiles} " +
                        $"observationChangeMask={sample.ObservationChangeMask} " +
                        $"dirtyTileCount={sample.DirtyTileCount}");
            Logger.Info($"Merkaba metrics-rgbd revision={sample.Revision} " +
                        $"available={sample.RefineAvailable} valid={sample.ReadbackValid} " +
                        $"center={FormatRefineBin(sample.RefineRadial, 0)} " +
                        $"mid={FormatRefineBin(sample.RefineRadial, 1)} " +
                        $"edge={FormatRefineBin(sample.RefineRadial, 2)}");
            Logger.Info($"Merkaba metrics-storage revision={sample.Revision} " +
                        $"valid={sample.ReadbackValid} " +
                        $"loadRequests={sample.LoadRequests} " +
                        $"loadBytesPerSecond={sample.LoadBytesPerSecond:F0} " +
                        $"loadLatencyP50={sample.LoadLatencyP50Ms:F2}ms " +
                        $"loadLatencyP95={sample.LoadLatencyP95Ms:F2}ms " +
                        $"writebackTiles={sample.WritebackTiles} " +
                        $"writeBytesPerSecond={sample.WriteBytesPerSecond:F0} " +
                        $"writeLatencyP50={sample.WriteLatencyP50Ms:F2}ms " +
                        $"writeLatencyP95={sample.WriteLatencyP95Ms:F2}ms " +
                        $"storageBackpressure={sample.StorageBackpressure} " +
                        $"failedReads={sample.FailedReads} " +
                        $"failedWrites={sample.FailedWrites}");
            Logger.Info($"Merkaba metrics-observation-publication revision={sample.Revision} " +
                        $"valid={sample.ReadbackValid} " +
                        $"dualStorageIntents={sample.DualStorageIntents} " +
                        $"dualTouchPublication={sample.DualTouchPublication} " +
                        $"observationFailure=0x{sample.ObservationFailure:x} " +
                        $"failedObservations={sample.FailedObservations}");
        }

        internal static string FormatRefineBin(uint[] values, int radialBin)
        {
            int offset = radialBin * RefineMetricCount;
            return "[planeAccepted=" + values[offset] +
                   ",noOppositeSupport=" + values[offset + 1] +
                   ",noRgbCoverage=" + values[offset + 2] +
                   ",noChromaticSupport=" + values[offset + 3] +
                   ",noPhotometricSupport=" + values[offset + 4] +
                   ",measuredDepth=" + values[offset + 5] +
                   ",rgbCorrection=" + values[offset + 6] +
                   ",accepted=" + values[offset + 7] + "]";
        }

        private static void CancelFrame()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            Native.Cancel();
#endif
            FinishFrame();
        }

        private static void FinishFrame()
        {
            EntrySequence.Clear();
            _state = CaptureState.Idle;
            _revision = 0u;
            _submissionBegan = false;
            _submissionEnded = false;
            _activeOwner = CaptureOwner.Count;
            _storageCapture = false;
        }

#if UNITY_EDITOR
        internal static void SetAvailableForTests(bool available)
        {
            _testAvailable = available;
            if (!available)
            {
                _state = CaptureState.Idle;
                _scheduledOwner = CaptureOwner.Observation;
                _activeOwner = CaptureOwner.Count;
                _revision = 0u;
                _submissionBegan = false;
                _submissionEnded = false;
                EntrySequence.Clear();
                SessionTotals.Clear();
                _lastCoverageSeen = -1;
            }
        }

        internal static CaptureOwner ScheduledOwnerForTests => _scheduledOwner;

        internal static void SetScheduledOwnerForTests(CaptureOwner owner)
        {
            if ((uint)owner >= (uint)CaptureOwner.Count)
                throw new ArgumentOutOfRangeException(nameof(owner));
            if (_state != CaptureState.Idle)
                throw new InvalidOperationException(
                    "Cannot change the scheduled owner during a capture.");
            _scheduledOwner = owner;
        }

        internal static int SessionSampleCountForTests
        {
            get
            {
                int samples = 0;
                foreach (SessionAggregate aggregate in SessionTotals.Values)
                    samples += aggregate.Samples;
                return samples;
            }
        }

        internal static void ResolveSampleForTests(bool valid)
        {
            if (_state != CaptureState.AwaitingResults)
                throw new InvalidOperationException("No timing sample is awaiting results.");
            if (valid) AdvanceScheduledOwner();
            FinishFrame();
        }

        internal static MerkabaGpuStage[] RecordedStagesForTests()
        {
            var result = new MerkabaGpuStage[EntrySequence.Count];
            for (int index = 0; index < result.Length; index++)
                result[index] = EntrySequence[index].Stage;
            return result;
        }

        internal static int OwnerStrideForTests => OwnerStride;

        internal static int OwnerQueryBaseForTests(CaptureOwner owner)
        {
            if ((uint)owner >= (uint)CaptureOwner.Count)
                throw new ArgumentOutOfRangeException(nameof(owner));
            return (int)owner * OwnerStride;
        }
#endif

        private static class Native
        {
            internal const int SubmissionBegin = 0;
            internal const int DispatchBegin = 1;
            internal const int DispatchEnd = 2;
            internal const int CopyBegin = 3;
            internal const int CopyEnd = 4;
            internal const int DrawBegin = 5;
            internal const int DrawEnd = 6;
            internal const int SubmissionEnd = 7;
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "MerkabaVulkanTimestamps";

            [DllImport(Library, EntryPoint = "MerkabaTimestamp_IsAvailable")]
            private static extern int IsAvailableNative();
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_Arm")]
            private static extern int ArmNative(int owner, ulong revision);
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_Cancel")]
            private static extern void CancelNative();
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_GetRenderEventFunc")]
            private static extern IntPtr GetRenderEventFuncNative();
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_GetEventId")]
            private static extern int GetEventIdNative(int offset);
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_Read")]
            private static extern int ReadNative(int requestedOwner,
                [Out] ulong[] timestamps, int timestampCapacity,
                out int capturedOwner, out int entryCount,
                out double timestampPeriod, out int validBits,
                out ulong revision, out int overflow);

            internal static IntPtr RenderEvent => GetRenderEventFuncNative();
            internal static int EventId(int offset) => GetEventIdNative(offset);
            internal static bool TryArm(CaptureOwner owner, uint revision)
            {
                try
                {
                    return IsAvailableNative() != 0 &&
                           ArmNative((int)owner, revision) != 0;
                }
                catch (DllNotFoundException) { return false; }
                catch (EntryPointNotFoundException) { return false; }
            }

            internal static void Cancel()
            {
                try { CancelNative(); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }

            internal static int TryRead(CaptureOwner requestedOwner,
                ulong[] timestamps, out CaptureOwner capturedOwner,
                out int entryCount, out double timestampPeriod,
                out int validBits, out ulong revision, out bool overflow)
            {
                try
                {
                    int result = ReadNative((int)requestedOwner, timestamps,
                        timestamps.Length, out int ownerValue, out entryCount,
                        out timestampPeriod, out validBits, out revision,
                        out int overflowValue);
                    capturedOwner = (CaptureOwner)ownerValue;
                    overflow = overflowValue != 0;
                    return result;
                }
                catch (DllNotFoundException)
                {
                    capturedOwner = CaptureOwner.Count;
                    entryCount = validBits = 0;
                    timestampPeriod = 0.0;
                    revision = 0u;
                    overflow = false;
                    return -1;
                }
                catch (EntryPointNotFoundException)
                {
                    capturedOwner = CaptureOwner.Count;
                    entryCount = validBits = 0;
                    timestampPeriod = 0.0;
                    revision = 0u;
                    overflow = false;
                    return -1;
                }
            }
#else
            internal static IntPtr RenderEvent => IntPtr.Zero;
            internal static int EventId(int offset) => 0;
            internal static bool TryArm(CaptureOwner owner, uint revision) => false;
            internal static void Cancel() { }
            internal static int TryRead(CaptureOwner requestedOwner,
                ulong[] timestamps, out CaptureOwner capturedOwner,
                out int entryCount, out double timestampPeriod,
                out int validBits, out ulong revision, out bool overflow)
            {
                capturedOwner = CaptureOwner.Count;
                entryCount = validBits = 0;
                timestampPeriod = 0.0;
                revision = 0u;
                overflow = false;
                return -1;
            }
#endif
        }
    }
}
