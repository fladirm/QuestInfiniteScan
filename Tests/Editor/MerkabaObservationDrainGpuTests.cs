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
        public void FrozenDepthObservation_NonzeroR2IsBitIdenticalAcrossQuantaAndBackpressure()
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True,
                "A missing GPU cannot satisfy the production drain proof.");

            uint[] eager;
            using (var world = new FrozenWorld())
            {
                eager = world.Drain(1336, false);
                AssertNonzeroR2(eager);
            }
            foreach (int quantum in new[] { 1, 7, 64 })
            {
                using var world = new FrozenWorld();
                uint[] split = world.Drain(quantum, true);
                AssertNonzeroR2(split);
                CollectionAssert.AreEqual(eager, split,
                    $"The same frozen pixels must commit identical owner/key/Lower/Upper/epoch words; quantum={quantum}.");
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
            private const int PhaseTaskCount = 1336;
            private const int TouchedTileCountCounter = 15; // M8_COUNTER_TOUCHED_TILE_COUNT
            private static readonly int3 FirstOwner = new(3, 4, 3);
            private static readonly int3 SecondOwner = new(4, 3, 3);
            private readonly List<ComputeBuffer> _buffers = new();
            private readonly ComputeShader _shader;
            private readonly int _kernel;
            private readonly ComputeBuffer _counters, _details, _tileRecords, _states;
            private readonly Texture2D _depth, _normals;
            private readonly uint4[] _canonicalStates;
            private uint _publishingGeneration;

            internal FrozenWorld()
            {
                _shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaIntegration.compute"));
                Assert.That(_shader, Is.Not.Null);
                _kernel = _shader.FindKernel("DrainObservationRefinement");

                // An ordinary orthographic projection: the two pixel centres
                // lie on the fixed relation endpoints plus the measured normal
                // offset. N.d=0, so both endpoints observe the same plane.
                Vector3 normal = new Vector3(1, 1, 1).normalized;
                Vector3 direction = new(1, -1, 0);
                Vector3 right = direction.normalized;
                Vector3 up = Vector3.Cross(normal, right);
                Vector3 center = (Vector3)(float3)FirstOwner * A.LatticeStep +
                    direction * (A.LatticeStep * 0.5f);
                const float measuredOffset = 0.006f;
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
                _shader.SetTexture(_kernel, "gsDepthTex", _depth);
                _shader.SetTexture(_kernel, "gsDepthNormalTex", _normals);
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
                _shader.SetFloat("_MerkabaMaxUpdateDistance", 2f);
                _shader.SetInt("_MerkabaExclusionCount", 0);
                _shader.SetInt("_M8FineRefineActive", 0);
                _shader.SetInt("_M8ObservationToken", (int)ObservationToken);
                _shader.SetInt("_M8ObservationHotSlotCount", 1);

                var records = new List<uint4>(16);
                for (int pixel = 0; pixel < 2; pixel++)
                {
                    Vector4 h = viewInverse * (projection.inverse *
                        new Vector4(pixel == 0 ? -0.5f : 0.5f, 0, depth * 2f - 1f, 1));
                    float3 world = new(h.x / h.w, h.y / h.w, h.z / h.w);
                    int3 first = (int3)math.floor(world / A.LatticeStep);
                    for (int ordinal = 0; ordinal < 8; ordinal++)
                    {
                        int3 owner = A.OverlapOwner(first, ordinal);
                        Assert.That(math.all(owner >= 0 & owner < 8), Is.True,
                            "Every emitted observation owner must fit this one-tile fixture.");
                        records.Add(new uint4(Local(owner), (uint)pixel, 0, 0));
                    }
                }
                _shader.SetInt("_M8ObservationRecordCapacity", records.Count);
                Bind("_M8ObservationRecords", Upload(records.ToArray(), 16));
                Bind("_M8ObservationTileBinsRead", Upload(new[]
                    { new uint4(ObservationToken, (uint)records.Count, 0, (uint)records.Count) }, 16));
                Bind("_M8TouchedTileQueueRead", Upload(new uint[] { 0 }, 4));

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
                Bind("_M8ChunkTileRefsRead", Upload(tiles, 4));
                _tileRecords = Upload(new[] { new uint4(0, 0, 2, 0),
                    new uint4(ObservationToken, 0, 0, SlotGeneration) }, 16);
                Bind("_M8TileRecords", _tileRecords);
                Bind("_M8TileRecordsRead", _tileRecords);
                var bits = new uint4[16];
                _canonicalStates = new uint4[512];
                foreach (int3 owner in new[] { FirstOwner, SecondOwner })
                {
                    uint local = Local(owner);
                    uint flags = KernelState.SetSurfacePlane(MerkabaConstants.OccupiedFlag,
                        (float3)normal, 0f);
                    _canonicalStates[local] = new uint4((uint)MerkabaConstants.OccupiedOnThreshold,
                        0xff808080u, 1, flags);
                    bits[local >> 5].x |= 1u << (int)(local & 31u);
                }
                _states = Upload(_canonicalStates, 16);
                Bind("_M8KernelStates0Read", _states);
                var emptyBank = Upload(new uint4[512], 16);
                for (int bank = 1; bank < 4; bank++) Bind($"_M8KernelStates{bank}Read", emptyBank);
                Bind("_M8TileBits", Upload(bits, 16));
                var counters = new uint[MerkabaGrid.CounterCount];
                counters[MerkabaGrid.CounterBlockCount] = 1;
                counters[MerkabaGrid.CounterChunkCount] = 1;
                counters[MerkabaGrid.CounterHotTileCount] = 1;
                counters[MerkabaGrid.CounterOccupiedKernelCount] = 2;
                counters[MerkabaGrid.CounterObservationToken] = ObservationToken;
                counters[TouchedTileCountCounter] = 1;
                _counters = Upload(counters, 4);
                Bind("_M8Counters", _counters);

                _details = Raw(MerkabaFlowerGpuLayout.DetailBufferBytes);
                _details.SetData(new uint[MerkabaFlowerGpuLayout.DetailArenaControl / 4]);
                InitializeArena(_details, MerkabaFlowerGpuLayout.DetailArenaControl);
                Bind("_M8FlowerDetailPages", _details);
                var threads = Raw(MerkabaFlowerGpuLayout.ThreadPersistentBufferBytes);
                InitializeArena(threads, MerkabaFlowerGpuLayout.ThreadArenaControl);
                Bind("_M8ThreadAtlasPages", threads);
            }

            internal uint[] Drain(int quantum, bool backpressure)
            {
                if (backpressure)
                {
                    // Hold the actual allocator lease, not a fabricated
                    // reduction result. A real nonzero candidate must stall.
                    _details.SetData(new uint[] { 1 }, 0,
                        MerkabaFlowerGpuLayout.DetailArenaControl / 4, 1);
                    uint[] counters = Dispatch(PhaseTaskCount);
                    Assert.That(counters[MerkabaGrid.CounterRefinementPendingTiles], Is.EqualTo(1u));
                    Assert.That(counters[MerkabaGrid.CounterRefinementBackpressure], Is.GreaterThan(0u));
                    Assert.That(CanonicalRecords(), Is.Empty);
                    Assert.That(ReadDirectory()[3], Is.LessThan((uint)PhaseTaskCount));
                    // GetData above has retired this dispatch. Releasing the
                    // lease supplies progress without a new image or token.
                    _details.SetData(new uint[] { 0 }, 0,
                        MerkabaFlowerGpuLayout.DetailArenaControl / 4, 1);
                }
                bool complete = false;
                for (int attempt = 0; attempt < PhaseTaskCount * 4; attempt++)
                {
                    uint[] counters = Dispatch(quantum);
                    uint[] directory = ReadDirectory();
                    Assert.That(directory[1], Is.EqualTo(SlotGeneration));
                    Assert.That(directory[2], Is.EqualTo(ObservationToken));
                    Assert.That(counters[MerkabaGrid.CounterObservationToken], Is.EqualTo(ObservationToken));
                    Assert.That(counters[MerkabaGrid.CounterObservationFailure], Is.Zero);
                    if (directory[3] == PhaseTaskCount &&
                        counters[MerkabaGrid.CounterRefinementPendingTiles] == 0)
                    { complete = true; break; }
                }
                Assert.That(complete, Is.True, "The frozen observation must drain without another camera input.");
                uint[] records = CanonicalRecords();
                Dispatch(quantum);
                CollectionAssert.AreEqual(records, CanonicalRecords(), "A completed cursor must be idempotent.");
                var states = new uint4[512]; _states.GetData(states);
                CollectionAssert.AreEqual(_canonicalStates, states, "Fine drain cannot move the canonical R1 plane.");
                var metadata = new uint4[2]; _tileRecords.GetData(metadata);
                Assert.That(metadata[1].x, Is.EqualTo(ObservationToken),
                    "Fine quanta must retain the once-per-observation R1 stamp.");
                return records;
            }

            private uint[] Dispatch(int quantum)
            {
                // Only per-quantum telemetry is reset. Canonical state,
                // source pixels, bins, matrices and token remain immutable.
                _counters.SetData(new uint[4], 0, MerkabaGrid.CounterRefinementPendingTiles, 4);
                _shader.SetInt("_M8RefinementQuantum", quantum);
                _shader.SetInt("_M8DualRetiredGeneration", (int)_publishingGeneration);
                _shader.SetInt("_M8DualPublishingGeneration", (int)++_publishingGeneration);
                _shader.Dispatch(_kernel, 1, 1, 1);
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
            private void Bind(string name, ComputeBuffer buffer) => _shader.SetBuffer(_kernel, name, buffer);
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
                var words = new uint[9]; words[8] = MerkabaFlowerGpuLayout.PersistentOrder + 1u;
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
                UnityEngine.Object.DestroyImmediate(_shader);
            }
        }
    }
}
