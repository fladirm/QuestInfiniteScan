using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using A = Genesis.RoomScan.MerkabaSphereFlowerAuthority;

namespace Genesis.RoomScan.Tests
{
    /// <summary>
    /// Actual production R2 drain, starting after the coarse observation commit.
    /// The input is frozen depth/normal pixels, not uploaded roots or residuals.
    /// This does not claim stereo acquisition, RGB/V or complete scan parity.
    /// </summary>
    public sealed class MerkabaObservationDrainGpuTests
    {
        [Test, Timeout(120000)]
        public void FrozenDepthObservation_NonzeroR2CompletesWithoutCursorAfterLocalBackpressure()
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True,
                "A missing GPU cannot satisfy the production drain proof.");

            uint[] eager;
            using (var world = new FrozenWorld())
            {
                eager = world.Drain(false);
                AssertNonzeroR2(eager);
            }
            foreach (bool backpressure in new[] { false, true })
            {
                using var world = new FrozenWorld();
                uint[] split = world.Drain(backpressure);
                AssertNonzeroR2(split);
                CollectionAssert.AreEqual(eager, split,
                    "The same frozen pixels must commit identical owner/key/Lower/Upper/epoch words.");
            }
        }

        private static void AssertNonzeroR2(uint[] records)
        {
            int count = 0;
            // Each entry is the implicit owner identity followed by the exact
            // four words of its canonical 16-byte FlowerDetailRecord.
            for (int i = 0; i < records.Length; i += 5)
            {
                Assert.That(MerkabaFlowerDetailKey.TryDecode(records[i + 1], out var key), Is.True);
                int lower = unchecked((int)records[i + 2]);
                int upper = unchecked((int)records[i + 3]);
                Assert.That(lower, Is.LessThanOrEqualTo(upper));
                Assert.That(records[i + 4], Is.EqualTo(1u));
                if (key.Kind == MerkabaFlowerDetailKind.R2Phase &&
                    (upper < 0 || lower > 0)) count++;
            }
            Assert.That(count, Is.GreaterThan(0),
                "Two empty outputs are not an eager/drain proof: actual measurement reduction must commit nonzero R2.");
        }

        private sealed class FrozenWorld : IDisposable
        {
            private const uint ObservationToken = 37u;
            private const uint SlotGeneration = 1u;
            private static readonly int3 FirstOwner = new(3, 4, 3);
            private static readonly int3 SecondOwner = new(4, 3, 3);
            private readonly List<ComputeBuffer> _buffers = new();
            private readonly ComputeShader _shader;
            private readonly int[] _geometryKernels;
            private readonly int _prepareOwnersKernel, _resolveKernel,
                _skinRgbKernel, _skinVKernel, _finalizeKernel;
            private readonly ComputeBuffer _counters, _details, _tileRecords, _states, _signals;
            // What a completed snapshot's retirement clears, so the harness can
            // stand in for the acquisition side of the NEXT snapshot exactly as
            // FlowerCommit and EmitObservationBins do in production.
            private ComputeBuffer _bins, _bits;
            private uint4[] _binsSeed, _bitsSeed;
            private readonly Texture2D _depth, _normals, _rgb;
            private readonly uint4[] _canonicalStates;
            private uint _publishingGeneration;

            internal FrozenWorld()
            {
                _shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaIntegration.compute"));
                Assert.That(_shader, Is.Not.Null);
                _geometryKernels = new[] { _shader.FindKernel("IntegrateFlowerRoot"),
                    _shader.FindKernel("IntegrateFlowerL1"), _shader.FindKernel("IntegrateFlowerL2") };
                _prepareOwnersKernel = _shader.FindKernel("PrepareFlowerOwners");
                _resolveKernel = _shader.FindKernel("ResolveFlowerCarriers");
                _skinRgbKernel = _shader.FindKernel("DrainFlowerSkinRgb");
                _skinVKernel = _shader.FindKernel("DrainFlowerSkinV");
                _finalizeKernel = _shader.FindKernel("FinalizeObservation");
                var flowerTables = MerkabaGrid.CreateFlowerTableBuffer();
                _buffers.Add(flowerTables);
                Bind("_M8FlowerTables", flowerTables);

                // An ordinary orthographic projection: the two pixel centres
                // lie on the fixed relation endpoints plus the measured normal
                // offset. N.d=0, so both endpoints observe the same plane.
                Vector3 normal = new Vector3(1, 1, 1).normalized;
                Vector3 direction = new(1, -1, 0);
                Vector3 right = direction.normalized;
                Vector3 up = Vector3.Cross(normal, right);
                Vector3 center = (Vector3)(float3)FirstOwner * A.LatticeStep +
                    direction * (A.LatticeStep * 0.5f);
                // Both independent planes and the outward residual synthesis
                // fit strictly inside the same generated R2 sector. The old
                // coarse=0 / measured=0.006 pair had nonzero tau but its full
                // synthesized enclosure crossed a sector and was not storable.
                // These are ordinary offset-code centres, not uploaded roots;
                // mandatory quantization and calibrated errors are unchanged.
                const float coarseOffset = -32f * A.LatticeStep / 127f;
                const float measuredOffset = -56f * A.LatticeStep / 127f;
                Matrix4x4 viewInverse = Matrix4x4.identity;
                viewInverse.SetColumn(0, new Vector4(right.x, right.y, right.z, 0));
                viewInverse.SetColumn(1, new Vector4(up.x, up.y, up.z, 0));
                viewInverse.SetColumn(2, new Vector4(normal.x, normal.y, normal.z, 0));
                Vector3 eye = center + normal * (1f + measuredOffset);
                viewInverse.SetColumn(3, new Vector4(eye.x, eye.y, eye.z, 1));
                float halfWidth = A.LatticeStep * Mathf.Sqrt(2f);
                Matrix4x4 projection = Matrix4x4.Ortho(-halfWidth, halfWidth,
                    -A.LatticeStep, A.LatticeStep, 0.1f, 10f);
                Vector4 clip = projection * new Vector4(0, 0, -1, 1);
                float depth = (clip.z / clip.w) * 0.5f + 0.5f;
                Assert.That(depth, Is.InRange(float.Epsilon, 1f - float.Epsilon));
                _depth = MakeTexture(TextureFormat.RFloat,
                    new Color(depth, 0, 0, 0), new Color(depth, 0, 0, 0));
                _normals = MakeTexture(TextureFormat.RGBAFloat,
                    new Color(normal.x, normal.y, normal.z, 1),
                    new Color(normal.x, normal.y, normal.z, 1));
                _rgb = MakeTexture(TextureFormat.RGBAFloat,
                    new Color(128f / 255f, 128f / 255f, 128f / 255f, 1),
                    new Color(128f / 255f, 128f / 255f, 128f / 255f, 1));
                foreach (int kernel in _geometryKernels)
                {
                    _shader.SetTexture(kernel, "gsDepthTex", _depth);
                    _shader.SetTexture(kernel, "gsDepthNormalTex", _normals);
                }
                _shader.SetTexture(_resolveKernel, "gsDepthTex", _depth);
                _shader.SetTexture(_resolveKernel, "gsDepthNormalTex", _normals);
                _shader.SetTexture(_skinRgbKernel, "gsDepthTex", _depth);
                _shader.SetTexture(_skinRgbKernel, "gsDepthNormalTex", _normals);
                _shader.SetTexture(_skinVKernel, "gsDepthTex", _depth);
                _shader.SetTexture(_skinVKernel, "gsDepthNormalTex", _normals);
                _shader.SetInts("gsDepthTexSize", 2, 1);
                _shader.SetMatrixArray("gsDepthProj", new[] { projection, projection });
                _shader.SetMatrixArray("gsDepthProjInv", new[] { projection.inverse, projection.inverse });
                _shader.SetMatrixArray("gsDepthView", new[] { viewInverse.inverse, viewInverse.inverse });
                _shader.SetMatrixArray("gsDepthViewInv", new[] { viewInverse, viewInverse });
                _shader.SetMatrix("_MerkabaGridToWorld", Matrix4x4.identity);
                _shader.SetMatrix("_MerkabaWorldToGrid", Matrix4x4.identity);
                // Ideal calibrated measurement; the production kernel still
                // adds the mandatory normal/offset quantization enclosures.
                _shader.SetVector("_M8PlaneErrorBounds", new Vector4(0, 0, 0, 1));
                _shader.SetVectorArray("_M8DepthErrorBounds", new[]
                    { new Vector4(0, 0, 0, 1), new Vector4(0, 0, 0, 1) });
                _shader.SetVectorArray("_M8RgbErrorBounds", new[]
                    { new Vector4(0, 0, 0, 1), new Vector4(0, 0, 0, 1) });
                Matrix4x4 rgbRotation = Matrix4x4.identity;
                rgbRotation.SetColumn(0, new Vector4(right.x, right.y, right.z, 0));
                rgbRotation.SetColumn(1, new Vector4(up.x, up.y, up.z, 0));
                rgbRotation.SetColumn(2, new Vector4(-normal.x, -normal.y, -normal.z, 0));
                foreach (string eyeName in new[] { "Left", "Right" })
                {
                    foreach (int kernel in _geometryKernels)
                        _shader.SetTexture(kernel, "_MerkabaCameraRgb" + eyeName, _rgb);
                    _shader.SetTexture(_resolveKernel, "_MerkabaCameraRgb" + eyeName, _rgb);
                    _shader.SetTexture(_skinRgbKernel, "_MerkabaCameraRgb" + eyeName, _rgb);
                    _shader.SetTexture(_skinVKernel, "_MerkabaCameraRgb" + eyeName, _rgb);
                    _shader.SetVector("_MerkabaCameraPosition" + eyeName, eye);
                    _shader.SetMatrix("_MerkabaCameraInverseRotation" + eyeName, rgbRotation.inverse);
                    _shader.SetVector("_MerkabaCameraFocalLength" + eyeName,
                        new Vector4(1f / halfWidth, 0.5f / A.LatticeStep, 0, 0));
                    _shader.SetVector("_MerkabaCameraPrincipalPoint" + eyeName,
                        new Vector4(1, 0.5f, 0, 0));
                    _shader.SetVector("_MerkabaCameraSensorResolution" + eyeName,
                        new Vector4(2, 1, 0, 0));
                    _shader.SetVector("_MerkabaCameraCurrentResolution" + eyeName,
                        new Vector4(2, 1, 0, 0));
                }
                // Both captured rows really have height one. They cannot
                // certify a complete RGB bilinear cell: the production skin
                // stage must consume that ambiguity, not invent RGB detail or
                // hold this stationary phase fixture forever.
                _shader.SetFloat("_MerkabaMaxUpdateDistance", 2f);
                _shader.SetInt("_MerkabaExclusionCount", 0);
                _shader.SetInt("_M8FineRefineActive", 0);
                _shader.SetInt("_M8ObservationToken", (int)ObservationToken);
                _shader.SetInt("_M8ObservationHotSlotCount", 1);
                _shader.SetInts("_M8ScanCenterBlock", 0, 0, 0);
                _shader.SetInt("_M8ScanBlockRadius", 2);
                _shader.SetInt("_M8ScanBlockSide", 5);

                var records = new List<uint4>(16);
                var measuredPlanes = new uint[2];
                for (int pixel = 0; pixel < 2; pixel++)
                {
                    Vector4 h = viewInverse * (projection.inverse *
                        new Vector4(pixel == 0 ? -0.5f : 0.5f, 0, depth * 2f - 1f, 1));
                    float3 world = new(h.x / h.w, h.y / h.w, h.z / h.w);
                    int3 first = (int3)math.floor(world / A.LatticeStep);
                    int3 endpoint = pixel == 0 ? FirstOwner : SecondOwner;
                    bool endpointIncluded = false;
                    for (int ordinal = 0; ordinal < 8; ordinal++)
                    {
                        int3 owner = A.OverlapOwner(first, ordinal);
                        Assert.That(math.all(owner >= 0 & owner < 8), Is.True,
                            "Every emitted observation owner must fit this one-tile fixture.");
                        endpointIncluded |= math.all(owner == endpoint);
                        records.Add(new uint4(Local(owner), (uint)pixel, 0, 0));
                    }
                    Assert.That(endpointIncluded, Is.True,
                        "The actual reprojected pixel must belong to its fixed relation endpoint.");
                    // The same camera/depth world sample supplies the CPU
                    // representability precondition. No derived root is sent
                    // to the GPU: it independently reduces the frozen records.
                    float3 n = (float3)normal;
                    float3 squared = n * n;
                    n /= math.sqrt((squared.x + squared.y) + squared.z);
                    float3 terms = (world - (float3)endpoint * A.LatticeStep) * n;
                    measuredPlanes[pixel] = KernelState.SetSurfacePlane(
                        MerkabaConstants.OccupiedFlag, n, (terms.x + terms.y) + terms.z);
                }
                uint coarsePlane = KernelState.SetSurfacePlane(MerkabaConstants.OccupiedFlag,
                    (float3)normal, coarseOffset);
                float normalError = math.asfloat(math.asuint(A.FloatInterval.Enclose(
                    2.0 * Math.Sqrt(18.0) / 1023.0).Upper) + 1u);
                float offsetError = math.asfloat(math.asuint(A.FloatInterval.Enclose(
                    (double)A.LatticeStep / (2.0 * 127.0)).Upper) + 1u);
                int3 relation = SecondOwner - FirstOwner;
                Assert.That(A.CarrierRootProof(FirstOwner, coarsePlane, 0, relation, 4, false,
                    normalError, offsetError, out var predicted), Is.EqualTo(A.ProofClassification.Certain));
                Assert.That(A.EvaluateCarrierRelation(FirstOwner + SecondOwner, 4, false,
                    measuredPlanes[0], measuredPlanes[1], normalError, offsetError,
                    out var observed, out _), Is.EqualTo(A.ProofClassification.Certain));
                var residual = A.AnalyzePhaseResidual(predicted, observed);
                Assert.That(residual.Classification, Is.EqualTo(A.PhaseResidualClassification.CertainNonzero),
                    "The actual frozen camera input must prove a representable nonzero residual before GPU execution.");
                Assert.That(A.RotatePhaseEvidence(predicted,
                    A.DecodePhaseInterval(residual.Lower, residual.Upper), out var synthesis),
                    Is.EqualTo(A.ProofClassification.Certain));
                Assert.That(A.CloseSharedPhaseRoot(synthesis, observed, out _),
                    Is.EqualTo(A.ProofClassification.Certain));
                _shader.SetInt("_M8ObservationRecordCapacity", records.Count);
                var observationRecords = Upload(records.ToArray(), 16);
                Bind("_M8ObservationRecords", observationRecords);
                Bind("_M8ObservationRecordsRead", observationRecords);
                _binsSeed = new[]
                    { new uint4(ObservationToken, (uint)records.Count, 0, (uint)records.Count) };
                var observationBins = Upload(_binsSeed, 16);
                _bins = observationBins;
                Bind("_M8ObservationTileBinsRead", observationBins);
                Bind("_M8ObservationTileBins", observationBins);
                Bind("_M8TouchedTileQueueRead", Upload(new uint[] { 0 }, 4));
                Bind("_M8PendingNewTileRefsRead", Upload(new uint[] { 0 }, 4));
                Bind("_M8ClaimQueue", Upload(new uint2[MerkabaSpatial.ClaimRecordCount], 8));
                Bind("_M8ObservationDispatchArgs", Upload(new uint[] { 1, 1, 1 }, 4));
                Bind("_M8AttemptCompletion", Upload(new uint4[1], 16));

                // Exactly the existing Block -> Chunk -> Tile coordinate ABI.
                var hash = new uint4[MerkabaSpatial.HashEntryCount];
                uint2 buckets = MerkabaSpatial.BucketSearchOrder(int3.zero);
                hash[buckets.x * 4u] = new uint4(0, 0, 0, 1);
                Bind("_M8HashEntriesRead", Upload(hash, 16));
                var owners = new uint4[MerkabaSpatial.OwnerChunkOffset + 1];
                Bind("_M8OwnerRecordsRead", Upload(owners, 16));
                var chunks = new uint[512]; chunks[0] = 1;
                Bind("_M8BlockChunkRefsRead", Upload(chunks, 4));
                var tiles = new uint[64]; tiles[0] = 1;
                var tileRefs = Upload(tiles, 4);
                Bind("_M8ChunkTileRefsRead", tileRefs);
                Bind("_M8ChunkTileRefs", tileRefs);
                var halo = Upload(new uint[27], 4);
                Bind("_M8TileHalo", halo);
                Bind("_M8TileHaloRead", halo);
                // The actual root-stage R3 reader and skin stage share the
                // production dual hierarchy. Zero block metadata means the
                // canonical unmaterialized FULL state; no leaf is invented.
                foreach ((string name, int bytes) in new[]
                {
                    ("_M8DualBlockStateRead", MerkabaDualGpuLayout.BlockBufferBytes),
                    ("_M8DualChunkStateRead", MerkabaDualGpuLayout.ChunkBufferBytes),
                    ("_M8DualLeavesRead", MerkabaDualGpuLayout.LeafBufferBytes)
                })
                {
                    var dual = Raw(bytes);
                    dual.SetData(new uint[bytes / sizeof(uint)]);
                    Bind(name, dual);
                }
                _tileRecords = Upload(new[] { new uint4(0, 0, 2, 0),
                    new uint4(ObservationToken, 0, 0, SlotGeneration) }, 16);
                Bind("_M8TileRecords", _tileRecords);
                Bind("_M8TileRecordsRead", _tileRecords);
                var bits = new uint4[16];
                _canonicalStates = new uint4[512];
                foreach (int3 owner in new[] { FirstOwner, SecondOwner })
                {
                    uint local = Local(owner);
                    _canonicalStates[local] = new uint4((uint)MerkabaConstants.OccupiedOnThreshold,
                        0xff808080u, 1, coarsePlane);
                    bits[local >> 5].x |= 1u << (int)(local & 31u);
                }
                _states = Upload(_canonicalStates, 16);
                Bind("_M8KernelStates0Read", _states);
                var emptyBank = Upload(new uint4[512], 16);
                for (int bank = 1; bank < 4; bank++) Bind($"_M8KernelStates{bank}Read", emptyBank);
                _bitsSeed = bits;
                _bits = Upload(bits, 16);
                Bind("_M8TileBits", _bits);
                var counters = new uint[MerkabaGrid.CounterCount];
                counters[MerkabaGrid.CounterBlockCount] = 1;
                counters[MerkabaGrid.CounterChunkCount] = 1;
                counters[MerkabaGrid.CounterHotTileCount] = 1;
                counters[MerkabaGrid.CounterOccupiedKernelCount] = 2;
                counters[MerkabaGrid.CounterObservationToken] = ObservationToken;
                counters[MerkabaGrid.CounterTouchedTileCount] = 1;
                _counters = Upload(counters, 4);
                Bind("_M8Counters", _counters);

                _details = Raw(MerkabaFlowerGpuLayout.DetailBufferBytes);
                _details.SetData(new uint[MerkabaFlowerGpuLayout.DetailArenaControl / 4]);
                InitializeArena(_details, MerkabaFlowerGpuLayout.DetailArenaControl);
                Bind("_M8FlowerDetailPages", _details);
                // The signal passes read the resolver's receipt through the
                // read-only view, exactly as production binds it in
                // BindFlowerPages. Without it those two kernels read unbound
                // memory and the drain never reaches completion.
                Bind("_M8FlowerDetailPagesRead", _details);
                var threads = Raw(MerkabaFlowerGpuLayout.ThreadPersistentBufferBytes);
                InitializeArena(threads, MerkabaFlowerGpuLayout.ThreadArenaControl);
                Bind("_M8ThreadAtlasPages", threads);
                Bind("_M8ThreadAtlasPagesRead", threads);
                _signals = new ComputeBuffer(MerkabaFlowerGpuLayout.SignalBufferBytes / 4, 4,
                    ComputeBufferType.Raw | ComputeBufferType.IndirectArguments);
                _buffers.Add(_signals);
                Bind("_M8FlowerSignalItems", _signals);
                Bind("_M8FlowerSignalItemsRead", _signals);
                var pages = Raw(MerkabaFlowerGpuLayout.PageDirectoryBytes);
                pages.SetData(new uint[MerkabaFlowerGpuLayout.PageDirectoryBytes / 4]);
                Bind("_M8FlowerPageDirectory", pages);
            }

            internal uint[] Drain(bool backpressure)
            {
                if (backpressure)
                {
                    _details.SetData(new uint[] { 1 }, 0,
                        MerkabaFlowerGpuLayout.DetailArenaControl / 4, 1);
                    uint[] blocked = Snapshot();
                    Assert.That(blocked[MerkabaGrid.CounterRefinementBackpressure], Is.GreaterThan(0u));
                    Assert.That(blocked[MerkabaGrid.CounterObservationCompleted], Is.Not.Zero);
                    Assert.That(CanonicalRecords(), Is.Empty);
                    _details.SetData(new uint[] { 0 }, 0,
                        MerkabaFlowerGpuLayout.DetailArenaControl / 4, 1);
                }
                uint[] counters = Snapshot();
                Assert.That(counters[MerkabaGrid.CounterObservationFailure], Is.Zero);
                Assert.That(counters[MerkabaGrid.CounterObservationCompleted], Is.Not.Zero);
                Assert.That(counters[MerkabaGrid.CounterRefinementPendingTiles], Is.Zero,
                    "Resident reached work must finish in this snapshot, without a saved cursor.");
                uint[] directory = ReadDirectory();
                Assert.That(directory[1], Is.EqualTo(SlotGeneration));
                Assert.That(directory[2], Is.Zero);
                Assert.That(directory[3], Is.Zero, "The tile directory cannot retain a program counter.");
                uint[] records = CanonicalRecords();
                Snapshot();
                CollectionAssert.AreEqual(records, CanonicalRecords(),
                    "Re-observing identical evidence must be idempotent.");
                var states = new uint4[512]; _states.GetData(states);
                CollectionAssert.AreEqual(_canonicalStates, states,
                    "Fine integration cannot move the canonical R1 plane.");
                var metadata = new uint4[2]; _tileRecords.GetData(metadata);
                Assert.That(metadata[1].x, Is.Zero,
                    "Completed observation retirement must release its R1 stamp.");
                return records;
            }

            // One bounded synchronous scan transaction, in the same order the
            // native schedule embeds: commit, then root, L1, L2 and skin with
            // fixed dispatch barriers, then finalize. What the harness
            // does before it is what the acquisition side does in production -
            // stamp the touched tile and emit its bin - because a released
            // snapshot's retirement clears exactly those.
            private uint[] Snapshot()
            {
                _bins.SetData(_binsSeed);
                _bits.SetData(_bitsSeed);
                _tileRecords.SetData(new[] { new uint4(0, 0, 2, 0),
                    new uint4(ObservationToken, 0, 0, SlotGeneration) });
                // Exactly what ReduceDepthCertificate/ResetObservationBins zero
                // for a snapshot. Canonical state, source pixels, the frozen
                // records, matrices and the token remain immutable.
                _counters.SetData(new uint[4], 0, MerkabaGrid.CounterRefinementPendingTiles, 4);
                _counters.SetData(new uint[1], 0, MerkabaGrid.CounterObservationCompleted, 1);
                _counters.SetData(new uint[1], 0, MerkabaGrid.CounterObservationChangeMask, 1);
                _counters.SetData(new[] { 1u }, 0, MerkabaGrid.CounterTouchedTileCount, 1);
                _shader.SetInt("_M8DualRetiredGeneration", (int)_publishingGeneration);
                _shader.SetInt("_M8DualPublishingGeneration", (int)++_publishingGeneration);
                _shader.SetInt("_M8AttemptToken", (int)_publishingGeneration);
                _signals.SetData(MerkabaFlowerGpuLayout.CreateSignalInitialHeader());
                _shader.Dispatch(_prepareOwnersKernel, 1, 1, 1);
                foreach (int kernel in _geometryKernels)
                    _shader.DispatchIndirect(kernel, _signals, MerkabaFlowerGpuLayout.MeasuredOwnerDispatchOffset);
                _shader.DispatchIndirect(_resolveKernel, _signals, MerkabaFlowerGpuLayout.MeasuredOwnerDispatchOffset);
                _shader.DispatchIndirect(_skinRgbKernel, _signals, MerkabaFlowerGpuLayout.SignalDispatchOffset);
                _shader.DispatchIndirect(_skinVKernel, _signals, MerkabaFlowerGpuLayout.SignalDispatchOffset);
                _shader.Dispatch(_finalizeKernel, 1, 1, 1);
                var counters = new uint[MerkabaGrid.CounterCount];
                _counters.GetData(counters); // Test-only readback; also the true retirement boundary.
                return counters;
            }

            private uint[] CanonicalRecords()
            {
                uint[] directory = ReadDirectory();
                if (directory[0] == 0u) return Array.Empty<uint>();
                uint[] owners = ReadWords(_details, (int)directory[0], 512);
                var result = new List<uint>();
                for (uint local = 0; local < owners.Length; local++)
                {
                    if (owners[local] == 0u) continue;
                    uint[] owner = ReadWords(_details, (int)owners[local], 4);
                    Assert.That(owner[0], Is.EqualTo(local));
                    Assert.That(owner[1], Is.EqualTo(1u));
                    if (owner[3] == 0u) continue;
                    uint[] records = ReadWords(_details, (int)owner[2], checked((int)owner[3] * 4));
                    for (int i = 0; i < records.Length; i += 4)
                    {
                        if (i != 0) Assert.That(records[i], Is.GreaterThan(records[i - 4]));
                        result.Add(local);
                        for (int word = 0; word < 4; word++) result.Add(records[i + word]);
                    }
                }
                return result.ToArray();
            }

            private uint[] ReadDirectory() => ReadWords(_details, MerkabaFlowerGpuLayout.TileDirectoryBase, 4);
            private static uint Local(int3 owner) => (uint)(owner.x + 8 * (owner.y + 8 * owner.z));
            private void Bind(string name, ComputeBuffer buffer)
            {
                foreach (int kernel in _geometryKernels) _shader.SetBuffer(kernel, name, buffer);
                _shader.SetBuffer(_prepareOwnersKernel, name, buffer);
                _shader.SetBuffer(_resolveKernel, name, buffer);
                _shader.SetBuffer(_skinRgbKernel, name, buffer);
                _shader.SetBuffer(_skinVKernel, name, buffer);
                _shader.SetBuffer(_finalizeKernel, name, buffer);
            }
            private ComputeBuffer Upload<T>(T[] values, int stride) where T : struct
            {
                var buffer = new ComputeBuffer(values.Length, stride);
                _buffers.Add(buffer); buffer.SetData(values); return buffer;
            }
            private ComputeBuffer Raw(int bytes)
            {
                var buffer = new ComputeBuffer(bytes / 4, 4, ComputeBufferType.Raw);
                _buffers.Add(buffer); return buffer;
            }
            private static void InitializeArena(ComputeBuffer buffer, int control)
            {
                uint[] words = MerkabaFlowerGpuLayout.CreateArenaInitialWords(MerkabaFlowerGpuLayout.PersistentOrder);
                buffer.SetData(words, 0, control / 4, words.Length);
            }
            private static uint[] ReadWords(ComputeBuffer buffer, int byteOffset, int count)
            {
                Assert.That(byteOffset & 3, Is.Zero);
                Assert.That(byteOffset / 4 + count, Is.LessThanOrEqualTo(buffer.count * buffer.stride / 4));
                var words = new uint[count]; buffer.GetData(words, 0, byteOffset / 4, count); return words;
            }
            private static Texture2D MakeTexture(TextureFormat format, params Color[] values)
            {
                var texture = new Texture2D(2, 1, format, false, true)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels(values); texture.Apply(false, false); return texture;
            }
            public void Dispose()
            {
                foreach (ComputeBuffer buffer in _buffers) buffer.Dispose();
                UnityEngine.Object.DestroyImmediate(_depth);
                UnityEngine.Object.DestroyImmediate(_normals);
                UnityEngine.Object.DestroyImmediate(_rgb);
                UnityEngine.Object.DestroyImmediate(_shader);
            }
        }
    }
}
