using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    public sealed partial class MerkabaGrid
    {
        private const uint DualPacketBlock = 1u;
        private const uint DualPacketMasks = 2u;
        private const uint DualPacketChunk = 4u;
        private const uint DualPacketLeaf = 8u;
        private const uint DualPacketBlockOnly = 32u;
        private const uint DualPacketChunkOnly = 64u;
        private uint _observationAcknowledgedLoadCursor;
        private bool _dualCapacityBackpressure;
        private int _dualCapacitySampleGpuGeneration;
        private uint _dualCapacitySampleObservation;
        private uint _dualCapacitySampleChunkCount;
        private int _dualCapacitySweepGpuGeneration;
        private uint _dualCapacitySweepObservation;
        private uint _dualCapacitySweepResidency;
        private uint _dualCapacitySweepDurableGeneration;
        private uint _dualCapacitySweepChunkCount;
        private uint _dualCapacitySweepCursor;
        private uint _dualCapacitySweepRemaining;
        private uint _dualCapacityQuantumGeneration;

        // These are actual dependency changes, not GPU submission ordinals.
        // A deferred load acknowledgement has not freed its queue slots yet.
        // Preserve the last published cursor until SetData has been issued by
        // the existing storage pump; its serialized ordering precedes retry.
        internal void CaptureObservationDependencyState(out uint loadCursor,
            out uint durableGeneration)
        {
            if (!_loadAcknowledgePending)
                _observationAcknowledgedLoadCursor = _loadRequestCursor;
            loadCursor = _observationAcknowledgedLoadCursor;
            durableGeneration = _dualDurableGeneration;
        }

        // Called from the existing counter callback; it adds no readback and
        // does not equate submission/completion with released reference space.
        private void ObserveObservationDualCapacity(NativeArray<uint> values)
        {
            uint observation = values[CounterObservationToken];
            _dualCapacitySampleGpuGeneration = _gpuGeneration;
            _dualCapacitySampleObservation = observation;
            _dualCapacitySampleChunkCount = Math.Min(values[CounterChunkCount],
                (uint)MerkabaSpatial.ChunkCapacity);
            _dualCapacityBackpressure = observation != 0u &&
                observation == _issuedObservationToken &&
                values[CounterObservationCompleted] == 0u &&
                values[CounterObservationFailure] == 0u &&
                values[CounterStorageBackpressure] != 0u;
        }

        // Called by the existing storage pump before its telemetry throttle.
        // Already-known finite reclaim work can drain without camera input or
        // another timer. A complete unsuccessful sweep waits for real changes.
        private void PumpObservationDualCapacity()
        {
            if (!_dualCapacityBackpressure || _storageReplacementPending ||
                _dualCapacitySampleGpuGeneration != _gpuGeneration ||
                _dualCapacitySampleObservation != _issuedObservationToken ||
                _dualCapacitySampleObservation == _completedObservationToken ||
                !DualMutationSubmissionAllowed || HasUnresolvedStorageRequests ||
                _dualDurableGeneration == 0u || _dualCapacitySampleChunkCount == 0u)
                return;

            bool newSweepOwner = _dualCapacitySweepGpuGeneration != _gpuGeneration ||
                _dualCapacitySweepObservation != _dualCapacitySampleObservation;
            if (newSweepOwner)
            {
                _dualCapacitySweepGpuGeneration = _gpuGeneration;
                _dualCapacitySweepObservation = _dualCapacitySampleObservation;
                _dualCapacitySweepRemaining = 0u;
                _dualCapacityQuantumGeneration = 0u;
            }
            PollDualRetirement();
            if (_dualCapacityQuantumGeneration > _dualRetiredGeneration) return;

            if (_dualCapacitySweepRemaining == 0u)
            {
                if (!newSweepOwner &&
                    _dualCapacitySweepResidency == _residencyEpoch &&
                    _dualCapacitySweepDurableGeneration == _dualDurableGeneration &&
                    _dualCapacitySweepChunkCount == _dualCapacitySampleChunkCount)
                    return;
                _dualCapacitySweepResidency = _residencyEpoch;
                _dualCapacitySweepDurableGeneration = _dualDurableGeneration;
                _dualCapacitySweepChunkCount = _dualCapacitySampleChunkCount;
                _dualCapacitySweepCursor = 0u;
                _dualCapacitySweepRemaining = _dualCapacitySweepChunkCount;
            }

            if (!ReclaimObservationDualReferences(_dualCapacitySweepCursor)) return;
            _dualCapacityQuantumGeneration = _dualPublishedGeneration;
            uint consumed = Math.Min(_dualCapacitySweepRemaining, 256u);
            _dualCapacitySweepCursor += consumed;
            _dualCapacitySweepRemaining -= consumed;
            // Cursor consumption is scheduling only. The sole GPU residency
            // wake-up comes from COMMITTED M8DualMakeChunkCold, never this scan.
        }

        private void BeginDualWritebackReadback()
        {
            if (_storageReplacementPending || !GpuSubmissionAllowed ||
                _writebackReadbackPending || _writebackStorageTask != null ||
                _writebackCompletionPending) return;
            int generation = _gpuGeneration;
            uint sourceGeneration = _flushAllDirty && _flushTilesDrained
                ? _drainSourceGeneration : 0u;
            if (!GatherDualWritebackBatch(sourceGeneration)) return;
            _writebackReadbackPending = true;
            _writebackIsDual = true;
            // The second control row is a drain receipt only when count is
            // zero; nonempty packet layout still starts at byte 16.
            AsyncGPUReadback.Request(_m8WritebackStaging, 32, 0, control =>
            {
                if (generation != _gpuGeneration) return;
                if (_storageReplacementPending)
                {
                    _writebackReadbackPending = false;
                    return;
                }
                if (control.hasError)
                {
                    FailDualReadback(new IOException("Dual writeback control read failed."));
                    return;
                }
                NativeArray<Raw16> controls = control.GetData<Raw16>();
                Raw16 header = controls[0];
                if (header.X > StreamBatchCapacity || header.W != 0u)
                {
                    FailDualReadback(new InvalidDataException(
                        "Dirty dual node is unresolved; no partial packet was appended."));
                    return;
                }
                if (header.X == 0u)
                {
                    _writebackReadbackPending = false;
                    _writebackIsDual = false;
                    if (header.Y != 0u)
                    {
                        FailDualReadback(new InvalidDataException("Dual dirty hierarchy is inconsistent."));
                        return;
                    }
                    if (_flushAllDirty && _flushTilesDrained)
                    {
                        Raw16 receipt = controls[1];
                        if (receipt.Y != 0u)
                        {
                            // A queue selection is not a whole-source proof.
                            // Drain every remaining dirty tile, retaining HOT
                            // residency, before accepting this source cut.
                            _flushTilesDrained = false;
                            _nextStreamPoll = 0f;
                            return;
                        }
                        if (header.Z != sourceGeneration ||
                            sourceGeneration != _drainSourceGeneration ||
                            _writebackStorageTask != null ||
                            _writebackCompletionPending)
                        {
                            FailDualReadback(new InvalidDataException(
                                "GPU drain receipt does not match the held source cut."));
                            return;
                        }
                        _drainedDualGeneration = header.Z;
                        _drainedOccupiedKernelCount = receipt.X;
                        _drainReceiptValid = true;
                        if (_flushTotalTiles < 0) _flushTotalTiles = _flushCompletedTiles;
                        ReportFlushProgress(true);
                        _flushAllDirty = false;
                        TaskCompletionSource<bool> completion = _flushCompletion;
                        _flushCompletion = null;
                        _flushProgress = null;
                        completion?.TrySetResult(true);
                    }
                    return;
                }
                int count = (int)header.X;
                AsyncGPUReadback.Request(_m8WritebackStaging, count * 17 * 16, 16, packet =>
                {
                    if (generation != _gpuGeneration) return;
                    _writebackReadbackPending = false;
                    if (_storageReplacementPending) return;
                    if (packet.hasError)
                    {
                        FailDualReadback(new IOException("Dual writeback packet read failed."));
                        return;
                    }
                    try
                    {
                        NativeArray<Raw16> raw = packet.GetData<Raw16>();
                        var nodes = new List<MerkabaTileSnapshot>(count);
                        for (int item = 0; item < count; item++)
                        {
                            Raw16 address = raw[item * 17];
                            var tile = new MerkabaTileSnapshot
                            {
                                Address = new MerkabaTileAddress(new int3(
                                    unchecked((int)address.X), unchecked((int)address.Y),
                                    unchecked((int)address.Z)), address.W)
                            };
                            tile.Sidecars = DecodeDualWritebackPacket(raw, item * 17 + 1, tile.Address);
                            nodes.Add(tile);
                        }
                        List<MerkabaAppendRecord> records = CollectWritebackRecords(nodes);
                        _dualWritebackBytes = (ulong)(count * 17 * 16);
                        _writebackBatchCount = count;
                        _writeIoStartedAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        EnsureStorage();
                        _writebackStorageTask = _ssdStore.AppendObservationBatchAsync(
                            Array.Empty<MerkabaTileSnapshot>(), records);
                    }
                    catch (Exception failure) { FailDualReadback(failure); }
                });
            });
        }

        private void FailDualReadback(Exception failure)
        {
            _writebackReadbackPending = false;
            _writebackIsDual = true;
            _writebackBatchCount = 0;
            _writebackStorageTask = Task.FromException(failure);
        }

        private static void EncodeDualLoadPacket(MerkabaTileSnapshot tile,
            KernelState[] destination, int first,
            MerkabaRecordKind scope = MerkabaRecordKind.DualLeaf)
        {
            byte[] packet = new byte[MerkabaDualGpuLayout.StoragePacketBytes];
            uint flags = 0u;
            foreach (MerkabaAppendRecord record in tile.Sidecars)
            {
                int offset;
                uint flag;
                switch (record.Kind)
                {
                    case MerkabaRecordKind.DualBlock:
                        offset = 16; flag = DualPacketBlock; break;
                    case MerkabaRecordKind.DualBlockChildren:
                        offset = 32; flag = DualPacketMasks; break;
                    case MerkabaRecordKind.DualChunk:
                        offset = 160; flag = DualPacketChunk; break;
                    case MerkabaRecordKind.DualLeaf:
                        offset = 192; flag = DualPacketLeaf; break;
                    default:
                        continue;
                }
                if ((flags & flag) != 0u)
                    throw new InvalidDataException("Duplicate dual load packet record.");
                MerkabaSphereFlowerReplayIndex.ValidateRecord(record);
                ValidateDualPacketAddress(tile.Address, record);
                Buffer.BlockCopy(record.Payload, 0, packet, offset, record.Payload.Length);
                flags |= flag;
            }
            if (scope == MerkabaRecordKind.DualBlock) flags |= DualPacketBlockOnly;
            else if (scope == MerkabaRecordKind.DualChunk) flags |= DualPacketChunkOnly;
            ValidateDualPacketShape(tile.Address, flags, packet);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(packet, 0, flags);
            for (int row = 0; row < MerkabaDualGpuLayout.StoragePacketRecords; row++)
            {
                int offset = row * 16;
                destination[first + row] = new KernelState
                {
                    OccupancyEvidence = MerkabaSphereFlowerPersistenceAbi.ReadInt32(packet, offset),
                    PackedColor = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(packet, offset + 4),
                    ColorConfidence = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(packet, offset + 8),
                    Flags = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(packet, offset + 12)
                };
            }
        }

        private static MerkabaAppendRecord[] DecodeDualWritebackPacket(
            NativeArray<Raw16> source, int first, MerkabaTileAddress tile)
        {
            byte[] packet = new byte[MerkabaDualGpuLayout.StoragePacketBytes];
            for (int row = 0; row < MerkabaDualGpuLayout.StoragePacketRecords; row++)
            {
                Raw16 value = source[first + row];
                int offset = row * 16;
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(packet, offset, value.X);
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(packet, offset + 4, value.Y);
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(packet, offset + 8, value.Z);
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(packet, offset + 12, value.W);
            }
            uint flags = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(packet, 0);
            if ((flags & ~(DualPacketBlock | DualPacketMasks | DualPacketChunk |
                           DualPacketLeaf | DualPacketBlockOnly | DualPacketChunkOnly)) != 0u)
                throw new InvalidDataException(
                    "Dual writeback requires a complete resident generation snapshot.");
            ValidateDualPacketShape(tile, flags, packet);
            var records = new List<MerkabaAppendRecord>(4);
            if ((flags & DualPacketBlock) != 0u)
                records.Add(DualStorageRecord(MerkabaRecordKind.DualBlock,
                    tile, packet, 16, MerkabaDualBlockMeta.ByteSize));
            if ((flags & DualPacketMasks) != 0u)
                records.Add(DualStorageRecord(MerkabaRecordKind.DualBlockChildren,
                    tile, packet, 32, MerkabaDualBlockChildren.ByteSize));
            if ((flags & DualPacketChunk) != 0u)
                records.Add(DualStorageRecord(MerkabaRecordKind.DualChunk,
                    tile, packet, 160, MerkabaDualChunkPayload.ByteSize));
            if ((flags & DualPacketLeaf) != 0u)
                records.Add(DualStorageRecord(MerkabaRecordKind.DualLeaf,
                    tile, packet, 192, MerkabaDualLeaf.ByteSize));
            return records.ToArray();
        }

        private static void ValidateDualPacketShape(MerkabaTileAddress tile,
            uint flags, byte[] packet)
        {
            bool block = (flags & DualPacketBlock) != 0u;
            bool masks = (flags & DualPacketMasks) != 0u;
            bool chunk = (flags & DualPacketChunk) != 0u;
            bool leaf = (flags & DualPacketLeaf) != 0u;
            bool blockOnly = (flags & DualPacketBlockOnly) != 0u;
            bool chunkOnly = (flags & DualPacketChunkOnly) != 0u;
            if (blockOnly && chunkOnly)
                throw new InvalidDataException("A dual packet has two residency scopes.");
            uint blockState = block
                ? MerkabaSphereFlowerPersistenceAbi.ReadUInt32(packet, 16) & 3u : 0u;
            if (masks != (blockState == 2u) || (!block && flags != 0u))
                throw new InvalidDataException("Dual packet block closure is incomplete.");
            if (blockOnly)
            {
                if (chunk || leaf)
                    throw new InvalidDataException("Block-only restore contains descendants.");
                return;
            }
            uint chunkState = blockState;
            if (masks)
            {
                uint word = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(packet,
                    32 + (tile.ChunkLocal >> 4) * 4);
                chunkState = (word >> ((tile.ChunkLocal & 15) * 2)) & 3u;
            }
            if (chunk != (chunkState == 2u))
                throw new InvalidDataException("Dual packet chunk closure is incomplete.");
            if (chunkOnly)
            {
                if (leaf) throw new InvalidDataException("Chunk-only restore contains a leaf.");
                return;
            }
            bool mixedLeaf = false;
            if (chunk)
            {
                uint mixed = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(packet,
                    168 + (tile.TileLocal >> 5) * 4);
                mixedLeaf = (mixed & (1u << (tile.TileLocal & 31))) != 0u;
            }
            if (leaf != mixedLeaf)
                throw new InvalidDataException("Dual packet MIXED leaf is absent.");
        }

        private static MerkabaAppendRecord DualStorageRecord(MerkabaRecordKind kind,
            MerkabaTileAddress tile, byte[] packet, int offset, int bytes)
        {
            byte[] payload = new byte[bytes];
            Buffer.BlockCopy(packet, offset, payload, 0, bytes);
            byte[] address;
            if (kind == MerkabaRecordKind.DualBlock ||
                kind == MerkabaRecordKind.DualBlockChildren)
            {
                address = new byte[MerkabaSphereFlowerPersistenceAbi.BlockAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteBlockAddress(address, tile.BlockCoord);
            }
            else if (kind == MerkabaRecordKind.DualChunk)
            {
                address = new byte[MerkabaSphereFlowerPersistenceAbi.ChunkAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteChunkAddress(address,
                    tile.BlockCoord, tile.ChunkLocal);
            }
            else
            {
                address = new byte[MerkabaSphereFlowerPersistenceAbi.TileAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(address, tile);
            }
            var record = new MerkabaAppendRecord(kind, address, payload);
            MerkabaSphereFlowerReplayIndex.ValidateRecord(record);
            return record;
        }

        private static void ValidateDualPacketAddress(MerkabaTileAddress tile,
            MerkabaAppendRecord record)
        {
            bool valid;
            if (record.Kind == MerkabaRecordKind.DualBlock ||
                record.Kind == MerkabaRecordKind.DualBlockChildren)
                valid = math.all(MerkabaSphereFlowerPersistenceAbi.ReadBlockAddress(
                    record.Address) == tile.BlockCoord);
            else if (record.Kind == MerkabaRecordKind.DualChunk)
            {
                MerkabaSphereFlowerPersistenceAbi.ReadChunkAddress(record.Address,
                    out int3 block, out int local);
                valid = math.all(block == tile.BlockCoord) && local == tile.ChunkLocal;
            }
            else valid = MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(
                record.Address).Equals(tile);
            if (!valid) throw new InvalidDataException("Dual packet address is not its M8 owner.");
        }

        private static List<MerkabaAppendRecord> CollectWritebackRecords(
            IReadOnlyList<MerkabaTileSnapshot> tiles)
        {
            var records = new List<MerkabaAppendRecord>(tiles.Count * 4);
            foreach (MerkabaTileSnapshot tile in tiles)
            foreach (MerkabaAppendRecord record in tile.Sidecars)
            {
                MerkabaSphereFlowerReplayIndex.ValidateRecord(record);
                records.Add(record);
            }
            // A uniform ancestor supersedes old descendants; append every
            // parent before its children independent of eviction lane order.
            // Fine identity additionally includes its owner and local key;
            // different Flower records must never collapse to a dual address.
            records.Sort(CompareWritebackRecordIdentity);
            int unique = 0;
            for (int index = 0; index < records.Count; index++)
            {
                MerkabaAppendRecord record = records[index];
                if (unique != 0 && CompareWritebackRecordIdentity(
                        records[unique - 1], record) == 0)
                {
                    if (!records[unique - 1].Payload.AsSpan().SequenceEqual(record.Payload))
                        throw new InvalidDataException(
                            "One persistent identity changed inside a frozen writeback batch.");
                    continue;
                }
                records[unique++] = record;
            }
            if (unique < records.Count) records.RemoveRange(unique, records.Count - unique);
            return records;
        }

        private static int CompareWritebackRecordIdentity(MerkabaAppendRecord left,
            MerkabaAppendRecord right)
        {
            int order = left.Kind.CompareTo(right.Kind);
            if (order != 0) return order;
            order = left.Address.AsSpan().SequenceCompareTo(right.Address);
            if (order != 0) return order;
            switch (left.Kind)
            {
                case MerkabaRecordKind.FlowerOwnerEpoch:
                case MerkabaRecordKind.FlowerDetail:
                case MerkabaRecordKind.FlowerSkinMetricRun:
                case MerkabaRecordKind.ThreadRun:
                case MerkabaRecordKind.Tombstone:
                    order = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(left.Payload, 0)
                        .CompareTo(MerkabaSphereFlowerPersistenceAbi.ReadUInt32(right.Payload, 0));
                    if (order == 0 && left.Kind == MerkabaRecordKind.Tombstone)
                        order = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(left.Payload, 4)
                            .CompareTo(MerkabaSphereFlowerPersistenceAbi.ReadUInt32(right.Payload, 4));
                    return order;
                default:
                    return 0; // Group/program identity is already in its exact address.
            }
        }

        private async Task RegisterDualStorageNodesAsync(MerkabaDualStorageNode[] nodes,
            IProgress<OperationWorkProgress> progress)
        {
            for (int offset = 0; offset < nodes.Length;)
            {
                if (!GpuSubmissionAllowed)
                    throw new InvalidOperationException("Dual restore was interrupted by GPU quiesce.");
                MerkabaRecordKind kind = nodes[offset].Kind;
                int count = 1;
                while (count < StreamBatchCapacity && offset + count < nodes.Length &&
                       nodes[offset + count].Kind == kind) count++;
                var batch = new MerkabaDualStorageNode[count];
                var addresses = new MerkabaTileAddress[count];
                Array.Copy(nodes, offset, batch, 0, count);
                for (int item = 0; item < count; item++) addresses[item] = batch[item].Address;
                UploadLoadAddresses(addresses);
                RegisterLoadedTileAddresses(count, (int)kind);
                if (kind != MerkabaRecordKind.DualLeaf)
                {
                    MerkabaTileSnapshot[] snapshots = await _ssdStore.ReadDualNodesAsync(batch);
                    if (!GpuSubmissionAllowed)
                        throw new InvalidOperationException("Dual restore was interrupted by GPU quiesce.");
                    var staging = new KernelState[count * MerkabaDualGpuLayout.LoadTileRecords];
                    for (int item = 0; item < count; item++)
                        EncodeDualLoadPacket(snapshots[item], staging,
                            item * MerkabaDualGpuLayout.LoadTileRecords +
                            MerkabaSpatial.KernelsPerTile, kind);
                    _m8LoadStagingStates.SetData(staging);
                    while (!InstallLoadedTiles(count))
                    {
                        if (!GpuSubmissionAllowed)
                            throw new InvalidOperationException("Dual restore was interrupted by GPU quiesce.");
                        await Task.Yield();
                    }
                    await RequireDualRestoreReadyAsync(count);
                }
                // A MIXED leaf is registered COLD; its actual 64 bytes are
                // loaded only when normal M8 SCAN/WARM residency requests it.
                // No HOT slot or fabricated positive kernel is created here.
                offset += count;
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.ApplyingState, offset, nodes.Length,
                    $"Registered {offset}/{nodes.Length} sparse dual nodes"));
                await Task.Yield();
            }
        }

        private Task RequireDualRestoreReadyAsync(int count)
        {
            int generation = _gpuGeneration;
            var result = new TaskCompletionSource<bool>();
            AsyncGPUReadback.Request(_m8LoadStagingAddresses, count * 16, 0, request =>
            {
                if (generation != _gpuGeneration || !GpuSubmissionAllowed)
                    result.TrySetException(new InvalidOperationException(
                        "GPU world changed during sparse dual restore."));
                else if (request.hasError)
                    result.TrySetException(new IOException("Sparse dual restore status read failed."));
                else
                {
                    var status = request.GetData<Raw16>();
                    for (int item = 0; item < count; item++)
                    {
                        if ((status[item].W & 0x80000000u) != 0u) continue;
                        result.TrySetException(new InvalidDataException(
                            "Sparse dual restore could not publish a complete resident ancestor."));
                        return;
                    }
                    result.TrySetResult(true);
                }
            });
            return result.Task;
        }
    }
}
