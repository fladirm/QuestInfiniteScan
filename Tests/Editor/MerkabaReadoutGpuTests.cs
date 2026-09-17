using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    /// <summary>
    /// Executes the production readout publication on the editor GPU. A build
    /// costs the changed tiles and their halo, never the unchanged world.
    /// </summary>
    public sealed class MerkabaReadoutGpuTests
    {
        private const string Package = "Packages/com.genesis.roomscan/";
        private const uint RefCold = 0xfffffffeu;
        private const uint RefLoading = 0xfffffffdu;
        private const int RecordWords = MerkabaGrid.RenderRecordWords;
        private const int Header = MerkabaGrid.RenderIndexHeader;

        [StructLayout(LayoutKind.Sequential)]
        private struct U4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

        private GameObject _owner;
        private MerkabaGrid _grid;
        private MerkabaGridRenderer _renderer;
        private ComputeShader _readout;
        private ComputeShader _visibility;
        private int _front;
        private bool _published;
        private uint _revision;

        [SetUp]
        public void CreateWorld()
        {
            _owner = new GameObject("m8-readout-gpu");
            _grid = _owner.AddComponent<MerkabaGrid>();
            SetField(_grid, "worldCompute", Load<ComputeShader>(
                "Runtime/Shaders/MerkabaWorld.compute"));
            _grid.EnsureGpuResources();
            _renderer = _owner.AddComponent<MerkabaGridRenderer>();
            _readout = Load<ComputeShader>(
                "Runtime/Shaders/MerkabaReadout.compute");
            _visibility = Load<ComputeShader>(
                "Runtime/Resources/Merkaba/MerkabaVisibility.compute");
            SetField(_renderer, "readoutCompute", _readout);
            SetField(_renderer, "renderShader", Load<Shader>(
                "Runtime/Shaders/MerkabaGrid.shader"));
            typeof(MerkabaGridRenderer).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_renderer, null);
            _front = 0;
            _published = false;
        }

        [TearDown]
        public void ReleaseWorld()
        {
            // Wait for every queued dispatch of this test before its buffers
            // are released: the editor frees immediately, and a late write
            // into freed memory lands in the next test's fresh buffers.
            if (_grid != null && _grid.M8Counters != null)
                _grid.M8Counters.GetData(new uint[1], 0, 0, 1);
            _renderer?.ReleaseOwnedResourcesAfterGpuRetirement();
            _grid?.ReleaseOwnedResourcesAfterGpuRetirement();
            if (_owner != null) UnityEngine.Object.DestroyImmediate(_owner);
        }

        [Test]
        public void FirstBuildPublishesMeasuredWallIntoPages()
        {
            InstallWallTiles(new int3(0, 0, 0));
            U4 completion = Build();
            Assert.That(completion.Y, Is.EqualTo(MerkabaGrid.ReadoutPublishedStatus));
            Assert.That(completion.Z, Is.EqualTo(1u), "published tiles");
            uint[] back = Index(_front);
            Assert.That(back[0], Is.EqualTo(1u));
            uint slot = back[ListBase(back)];
            int record = Header + (int)slot * RecordWords;
            uint indexCount = back[record];
            var header = new U4[1];
            _grid.M8RenderScratchHeader.GetData(header, 0, 0, 1);
            Assert.That(indexCount, Is.EqualTo(64u * 6u),
                "one 25 mm membrane quad per measured MAIN; " +
                $"planeValid={Counter(30)} emittedPatches={Counter(31)} " +
                $"emittedVertices={Counter(97)} coldInCoverage={Counter(50)} " +
                $"ringInvariant={Counter(108)} overflow={Counter(23)} " +
                $"rebuild={Counter(100)} batchBase={Counter(106)} " +
                $"cursor={Counter(64)} header=({header[0].X},{header[0].Y}," +
                $"{header[0].Z},{header[0].W})");
            Assert.That(back[1], Is.EqualTo(indexCount));
            // Shared knots: an 8x8 sheet has 9x9 = 81 knot vertices (two
            // 64-vertex pages) and 64 * 6 = 384 indices (two 256-index pages).
            Assert.That(back[record + 1], Is.EqualTo(2u),
                "81 shared knots and 384 indices = two pages");
            Assert.That(Counter(97), Is.EqualTo(81u), "one vertex per knot");
            Assert.That(Control(MerkabaGrid.RenderControlFreePages),
                Is.EqualTo((uint)MerkabaGrid.RenderPageCapacity -
                    back[record + 1]));
        }

        [Test]
        public void UnchangedWorldRebuildsNothingAndKeepsPublishedRecords()
        {
            InstallWallTiles(new int3(0, 0, 0), new int3(0, 0, 8));
            // Two slots: each slot builds everything the first time it is
            // BACK; from then on an unchanged world rebuilds nothing and a
            // slot's records are byte-identical to its previous publication.
            Build();
            Build();
            uint[] first = Index(_front);
            Build();
            U4 completion = Build();
            Assert.That(completion.Y, Is.EqualTo(MerkabaGrid.ReadoutPublishedStatus));
            Assert.That(Counter(MerkabaGrid.CounterRenderRebuildTiles), Is.Zero);
            Assert.That(Control(MerkabaGrid.RenderControlAllocCount), Is.Zero);
            uint[] second = Index(_front);
            Assert.That(second[0], Is.EqualTo(first[0]));
            Assert.That(second[1], Is.EqualTo(first[1]));
            for (int list = 0; list < (int)first[0]; list++)
            {
                int record = Header + (int)first[
                    ListBase(first) + list] * RecordWords;
                for (int word = 0; word < RecordWords; word++)
                    Assert.That(second[record + word],
                        Is.EqualTo(first[record + word]), $"word {word}");
            }
        }

        [Test]
        public void ChangedTileRebuildsOnlyItself()
        {
            InstallWallTiles(new int3(0, 0, 0), new int3(0, 0, 8),
                new int3(0, 0, 24));
            Build();
            Build();
            // A canonical mutation moves the tile's version; the 26-neighbour
            // halo is the journal's job (SeedRenderMutationJournal), not the
            // version's. Cost follows the change, never the world.
            MarkTileDirty(new int3(0, 0, 0));
            Build();
            Assert.That(Counter(MerkabaGrid.CounterRenderRebuildTiles),
                Is.EqualTo(1u));
            Assert.That(Index(_front)[0], Is.EqualTo(3u));
        }

        [Test]
        public void GrowingTheWorldBuildsOnlyTheNewTile()
        {
            InstallWallTiles(new int3(0, 0, 0));
            Build();
            Build();
            // A tile outside the first tile's 26-neighbour halo: only the
            // new tile builds (an adjacent tile would also rebuild its
            // neighbour, whose boundary knots gain contributors).
            InstallWallTiles(new int3(0, 24, 0));
            Build();
            Assert.That(Counter(MerkabaGrid.CounterRenderRebuildTiles),
                Is.EqualTo(1u));
            Assert.That(Index(_front)[0], Is.EqualTo(2u));
        }

        [Test]
        public void RetiredPagesReturnOnlyAfterTheNextPublication()
        {
            InstallWallTiles(new int3(0, 0, 0));
            Build();
            Build();
            uint free = Control(MerkabaGrid.RenderControlFreePages);
            MarkTileDirty(new int3(0, 0, 0));
            // The slot that rebuilds the tile retires its old pages into its
            // own ring; they are free again only when that slot builds next.
            int rebuiltSlot = 1 - _front;
            string beforeChain = ChainDump(rebuiltSlot, new int3(0, 0, 0));
            Build();
            string afterChain = ChainDump(rebuiltSlot, new int3(0, 0, 0));
            uint retired = Retired();
            Assert.That(retired, Is.GreaterThan(0u),
                $"before: {beforeChain} after: {afterChain}");
            Assert.That(Control(MerkabaGrid.RenderControlFreePages),
                Is.EqualTo(free - retired), "old pages wait in the retire ring");
            Build();
            Build();
            Assert.That(Retired(), Is.Zero);
            Assert.That(Control(MerkabaGrid.RenderControlFreePages),
                Is.EqualTo(free), "reclaimed after publication " + Diagnostics());
        }

        [Test]
        public void RejectedBackKeepsItsPagesAndCompletesWithoutLeak()
        {
            InstallWallTiles(new int3(0, 0, 0));
            Build();
            uint free = Control(MerkabaGrid.RenderControlFreePages);
            int back = 1 - _front;
            MarkTileDirty(new int3(0, 0, 0));
            Build(publish: false);
            // The rejected BACK still references the pages it allocated.
            uint allocated = ControlOf(back, MerkabaGrid.RenderControlAllocCount);
            Assert.That(allocated, Is.GreaterThan(0u));
            Assert.That(ControlOf(back, MerkabaGrid.RenderControlFreePages),
                Is.EqualTo((uint)MerkabaGrid.RenderPageCapacity - allocated));
            // Its next build completes it: nothing is rebuilt, nothing leaks.
            Build();
            Assert.That(_front, Is.EqualTo(back));
            Assert.That(Counter(MerkabaGrid.CounterRenderRebuildTiles), Is.Zero);
            Assert.That(Control(MerkabaGrid.RenderControlFreePages),
                Is.EqualTo(free));
        }

        [Test]
        public void EvictedTileLeavesThePublicationAndRetiresItsPages()
        {
            // The second tile spans y = 6.0..6.2 m: inside the 6.4 m coverage
            // sphere from the origin, outside it once the head moved 0.5 m.
            // (A tile that is not resident INSIDE coverage is simply not
            // drawn yet; see TileWithNonResidentRingIsNotListedUntilInstalled.)
            InstallWallTiles(new int3(0, 0, 0), new int3(0, 240, 0));
            Build();
            Build();
            uint[] published = Index(_front);
            Assert.That(published[0], Is.EqualTo(2u));
            uint slot = SlotOf(new int3(0, 240, 0));
            SetTileRef(RefIndexOf(slot), RefCold);
            Build(cameraGridMeters: new Vector3(0f, -0.5f, 0f));
            uint[] back = Index(_front);
            Assert.That(back[0], Is.EqualTo(1u));
            Assert.That(back[Header + (int)slot * RecordWords + 2], Is.Zero);
            Assert.That(Retired(), Is.GreaterThan(0u));
        }

        // Replaces UnresolvedHaloKeepsThePreviousTileRecord (contract C9
        // amendment): a tile whose ring is not resident is not drawn yet and
        // never holds or fails the publication; the tile is listed and built
        // in the first transaction after its ring member is installed.
        [Test]
        public void TileWithNonResidentRingIsNotListedUntilInstalled()
        {
            InstallWallTiles(new int3(0, 0, 0), new int3(0, 0, 8));
            Build();
            uint target = SlotOf(new int3(0, 0, 0));
            uint neighbour = SlotOf(new int3(0, 0, 8));
            uint neighbourRef = RefIndexOf(neighbour);
            SetTileRef(neighbourRef, RefLoading);
            MarkTileDirty(new int3(0, 0, 0));
            U4 withoutRing = Build();
            Assert.That(withoutRing.Y,
                Is.EqualTo(MerkabaGrid.ReadoutPublishedStatus),
                "a non-resident ring never fails the publication");
            Assert.That(withoutRing.Z, Is.Zero, "neither tile is drawn");
            Assert.That(withoutRing.W & 0x7fffffffu, Is.EqualTo(2u),
                "both coverage tiles are reported as not drawn yet");
            Assert.That(Counter(MerkabaGrid.CounterReadoutRingInvariant),
                Is.Zero);

            SetTileRef(neighbourRef, neighbour + 1u);
            U4 installed = Build();
            Assert.That(installed.Y,
                Is.EqualTo(MerkabaGrid.ReadoutPublishedStatus));
            Assert.That(installed.Z, Is.EqualTo(2u));
            Assert.That(installed.W & 0x7fffffffu, Is.Zero);
            uint[] after = Index(_front);
            int record = Header + (int)target * RecordWords;
            Assert.That(after[record], Is.EqualTo(64u * 6u),
                "the target tile is rebuilt with its complete ring");
        }

        [Test]
        public void VisibilityEmitsIndicesOnlyForPagesOfVisibleTiles()
        {
            InstallWallTiles(new int3(0, 0, 0), new int3(4000, 0, 0));
            Build();
            uint[] front = Index(_front);
            int nearRecord = Header + (int)SlotOf(new int3(0, 0, 0)) * RecordWords;
            uint nearPatches = front[nearRecord];
            uint nearPages = front[nearRecord + 1];

            int cull = _visibility.FindKernel("CullRenderTiles");
            int prepare = _visibility.FindKernel("PrepareVisibleIndices");
            int emit = _visibility.FindKernel("EmitVisibleIndices");
            _grid.M8CullControl.SetData(new uint[] { 0u, 0u, 0u, 0u });
            var planes = new Vector4[12];
            for (int plane = 0; plane < 12; plane++)
                planes[plane] = new Vector4(0f, 0f, 0f, 1f);
            _visibility.SetVectorArray("_M8CullGridPlanes", planes);
            _visibility.SetVector("_M8CameraGridMeters", Vector3.zero);
            MerkabaReadoutCoverage.WriteGridMetric(Matrix4x4.identity,
                out Vector3 diagonal, out Vector3 cross);
            _visibility.SetVector("_M8GridMetricDiagonal", diagonal);
            _visibility.SetVector("_M8GridMetricCross", cross);
            _visibility.SetFloat("_M8RenderDistance", 13f);
            _visibility.SetInt("_M8FrontTileCount", (int)front[0]);
            _visibility.SetBuffer(cull, "_M8RenderIndexFrontRead",
                _grid.GetM8RenderIndex(_front));
            _visibility.SetBuffer(cull, "_M8RenderPageQueues",
                _grid.GetM8RenderPageQueues(_front));
            _visibility.SetBuffer(cull, "_M8VisiblePages", _grid.M8VisiblePages);
            _visibility.SetBuffer(cull, "_M8CullControl", _grid.M8CullControl);
            _visibility.SetBuffer(prepare, "_M8CullControl", _grid.M8CullControl);
            _visibility.SetBuffer(prepare, "_M8RenderDrawArgs",
                _grid.M8RenderDrawArgs);
            _visibility.SetBuffer(emit, "_M8VisiblePagesRead", _grid.M8VisiblePages);
            _visibility.SetBuffer(emit, "_M8CullControlRead", _grid.M8CullControl);
            _visibility.SetBuffer(emit, "_M8VisibleIndices",
                _grid.GetM8RenderIndices(_front));
            _visibility.SetBuffer(emit, "_M8RenderIndicesRead",
                _grid.GetM8PublishedIndices(_front));
            _visibility.Dispatch(cull, 1, 1, 1);
            _visibility.Dispatch(prepare, 1, 1, 1);
            _visibility.DispatchIndirect(emit, _grid.M8CullControl, sizeof(uint));

            var control = new uint[MerkabaGrid.CullControlWords];
            _grid.M8CullControl.GetData(control);
            uint indexPages = (nearPatches +
                (uint)MerkabaGrid.RenderPageIndices - 1u) /
                (uint)MerkabaGrid.RenderPageIndices;
            Assert.That(control[0], Is.EqualTo(indexPages), "far tile culled");
            var drawArgs = new uint[5];
            _grid.M8RenderDrawArgs.GetData(drawArgs);
            // The draw stream is exactly the published indices of the visible
            // tiles: no page padding, whole triangles only.
            Assert.That(drawArgs[0], Is.EqualTo(nearPatches),
                "draw count is exact live indices, never pageCount*256");
            Assert.That(drawArgs[0] % 3u, Is.Zero);
            var pages = new U4[indexPages];
            _grid.M8VisiblePages.GetData(pages, 0, 0, (int)indexPages);
            var indices = new uint[MerkabaGrid.RenderPageIndices];
            _grid.GetM8RenderIndices(_front).GetData(indices, 0, 0,
                indices.Length);
            uint firstPage = pages[0].X;
            uint pageVertex = firstPage * (uint)MerkabaGrid.RenderPageVertices;
            Assert.That(indices[0], Is.GreaterThanOrEqualTo(pageVertex));
            Assert.That(indices[0], Is.LessThan((uint)
                MerkabaGrid.ReadoutVertexCapacity));
            Assert.That(pages[0].Y, Is.GreaterThan(0u));
            Assert.That(pages[0].Y, Is.LessThanOrEqualTo(
                (uint)MerkabaGrid.RenderPageIndices));
            Assert.That(pages[0].Z, Is.Zero,
                "first visible tile owns one contiguous output range");
        }

        private U4 Build(bool publish = true, Vector3 cameraGridMeters = default)
        {
            int back = 1 - _front;
            var command = new CommandBuffer { name = "readout-gpu-test" };
            try
            {
                _renderer.RecordBuild(command, back, ++_revision,
                    cameraGridMeters);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                command.Release();
            }
            var completion = new U4[3];
            _grid.M8AttemptCompletion.GetData(completion, 0, 0, 3);
            Assert.That(completion[1].X, Is.EqualTo(_revision));
            if (completion[1].Y != MerkabaGrid.ReadoutPublishedStatus)
                Debug.Log($"readout-gpu-test revision={_revision} not published: " +
                    $"status={completion[1].Y} tiles={completion[1].Z} " +
                    $"flags={completion[1].W:X8} batch=({completion[2].Y}/" +
                    $"{completion[2].Z}) rebuild={Counter(100)} " +
                    $"coldInCoverage={Counter(50)} ringInvariant={Counter(108)} " +
                    $"capacity={Counter(109)} overflow={Counter(23)} " +
                    $"viewOverflow={Counter(104)} invalid={Counter(105)} " +
                    $"planeValid={Counter(30)} emitted={Counter(31)}");
            _published = publish &&
                completion[1].Y == MerkabaGrid.ReadoutPublishedStatus;
            if (_published) _front = back;
            return completion[1];
        }

        private void InstallWallTiles(params int3[] origins)
        {
            var addresses = new List<MerkabaTileAddress>();
            foreach (int3 origin in origins)
            {
                MerkabaSpatial.Address address = MerkabaSpatial.Encode(origin);
                addresses.Add(new MerkabaTileAddress(address.BlockCoord,
                    address.LocalAddress));
            }
            _grid.M8LoadStagingAddresses.SetData(addresses);
            _grid.RegisterLoadedTileAddresses(addresses.Count);
            var refs = new uint[_grid.M8ChunkTileRefs.count];
            _grid.M8ChunkTileRefs.GetData(refs);
            for (int index = 0; index < refs.Length; index++)
                if (refs[index] == RefCold) refs[index] = RefLoading;
            _grid.M8ChunkTileRefs.SetData(refs);

            var states = new KernelState[addresses.Count *
                MerkabaSpatial.KernelsPerTile];
            for (int tile = 0; tile < addresses.Count; tile++)
                for (int z = 0; z < 8; z++)
                    for (int y = 0; y < 8; y++)
                    {
                        ref KernelState state = ref states[
                            tile * MerkabaSpatial.KernelsPerTile +
                            (3 | (y << 3) | (z << 6))];
                        state.Apply(MerkabaObservationKind.Surface, 1f,
                            new Color32(90, 120, 150, 255));
                        state.Flags = KernelState.SetSurfacePlane(state.Flags,
                            new float3(1f, 0f, 0f), 0f);
                    }
            _grid.M8LoadStagingAddresses.SetData(addresses);
            _grid.M8LoadStagingStates.SetData(states);
            _grid.InstallLoadedTiles(addresses.Count);
        }

        /// <summary>
        /// A canonical mutation of the tile: its publication version moves,
        /// exactly as a journalled observation would move it.
        /// </summary>
        private void MarkTileDirty(int3 origin)
        {
            uint slot = SlotOf(origin);
            var version = new uint[1];
            _grid.M8RenderVersions.GetData(version, 0, (int)slot, 1);
            version[0]++;
            _grid.M8RenderVersions.SetData(version, 0, (int)slot, 1);
            var runtime = new U4[1];
            _grid.M8TileRecords.GetData(runtime, 0, (int)slot * 2 + 1, 1);
            runtime[0].W |= 1u;
            _grid.M8TileRecords.SetData(runtime, 0, (int)slot * 2 + 1, 1);
        }

        private uint SlotOf(int3 origin)
        {
            MerkabaSpatial.Address address = MerkabaSpatial.Encode(origin);
            var records = new U4[MerkabaSpatial.PhysicalTileCapacity * 2];
            _grid.M8TileRecords.GetData(records);
            var refs = new uint[_grid.M8ChunkTileRefs.count];
            _grid.M8ChunkTileRefs.GetData(refs);
            for (uint slot = 0; slot < MerkabaSpatial.PhysicalTileCapacity; slot++)
            {
                U4 meta = records[slot * 2];
                uint refIndex = meta.X * 64u + meta.Y;
                if (refIndex < refs.Length && refs[refIndex] == slot + 1u &&
                    meta.Y == (uint)address.TileLocal &&
                    OwnerMatches(meta.X, address))
                    return slot;
            }
            Assert.Fail($"tile {origin} is not HOT");
            return 0u;
        }

        private bool OwnerMatches(uint chunkIndex, MerkabaSpatial.Address address)
        {
            var owners = new U4[1];
            _grid.M8OwnerRecords.GetData(owners, 0,
                MerkabaSpatial.BlockCapacity + (int)chunkIndex, 1);
            if (owners[0].Y != (uint)address.ChunkLocal) return false;
            var block = new U4[1];
            _grid.M8OwnerRecords.GetData(block, 0, (int)owners[0].X, 1);
            return unchecked((int)block[0].X) == address.BlockCoord.x &&
                unchecked((int)block[0].Y) == address.BlockCoord.y &&
                unchecked((int)block[0].Z) == address.BlockCoord.z;
        }

        private uint RefIndexOf(uint slot)
        {
            var meta = new U4[1];
            _grid.M8TileRecords.GetData(meta, 0, (int)slot * 2, 1);
            return meta[0].X * 64u + meta[0].Y;
        }

        private void SetTileRef(uint refIndex, uint value) =>
            _grid.M8ChunkTileRefs.SetData(new[] { value }, 0, (int)refIndex, 1);

        private uint[] Index(int slot)
        {
            var values = new uint[MerkabaGrid.RenderIndexCount];
            _grid.GetM8RenderIndex(slot).GetData(values);
            return values;
        }

        private string ChainDump(int slot, int3 origin)
        {
            uint tile = SlotOf(origin);
            uint[] index = Index(slot);
            int record = Header + (int)tile * RecordWords;
            var queues = new uint[MerkabaGrid.RenderPageQueueCount];
            _grid.GetM8RenderPageQueues(slot).GetData(queues);
            const int capacity = MerkabaGrid.RenderPageCapacity;
            int link = 8 + 3 * capacity;
            int owner = 8 + 4 * capacity;
            int alloc = 8 + 2 * capacity;
            var text = new System.Text.StringBuilder();
            text.Append($"slot={slot} tile={tile} record=[");
            for (int word = 0; word < RecordWords; word++)
                text.Append(index[record + word]).Append(word + 1 < RecordWords ? "," : "]");
            text.Append($" free={queues[0]} retireCount={queues[1]} alloc={queues[2]} " +
                $"reclaim={queues[3]} head={queues[4]} tail={queues[5]} chain=");
            uint page = index[record + 6];
            for (int hop = 0; hop < 6 && page < capacity; hop++)
            {
                text.Append($"{page}(owner={queues[owner + page]})->");
                page = queues[link + (int)page];
            }
            text.Append(page == 0xffffffffu ? "END" : page.ToString());
            text.Append($" allocList={queues[alloc]},{queues[alloc + 1]},{queues[alloc + 2]}");
            text.Append($" freeTop={queues[8 + queues[0] - 1]},{queues[8 + queues[0] - 2]}");
            return text.ToString();
        }

        private string Diagnostics() =>
            $"rebuild={Counter(MerkabaGrid.CounterRenderRebuildTiles)} " +

            $"retire={Retired()} " +
            $"alloc={Control(MerkabaGrid.RenderControlAllocCount)} " +
            $"reclaim={Control(MerkabaGrid.RenderControlReclaimCount)} " +

            $"ringInvariant={Counter(MerkabaGrid.CounterReadoutRingInvariant)}";

        private uint Control(int index) => ControlOf(_front, index);

        // Pages waiting in the FRONT slot's retire ring.
        private uint Retired() =>
            ControlOf(_front, MerkabaGrid.RenderControlRetireTail) -
            ControlOf(_front, MerkabaGrid.RenderControlRetireHead);

        // The active tile-list region of a slot alternates between builds.
        private static int ListBase(uint[] index) =>
            MerkabaGrid.RenderTileListBase +
            (int)(index[RenderHeaderListRegion] & 1u) *
            MerkabaSpatial.PhysicalTileCapacity;

        private const int RenderHeaderListRegion = 3;

        private uint ControlOf(int slot, int index)
        {
            var value = new uint[1];
            _grid.GetM8RenderPageQueues(slot).GetData(value, 0, index, 1);
            return value[0];
        }

        private uint[] Snapshot(int slot)
        {
            var pages = new uint[MerkabaGrid.RenderControlWords];
            _grid.GetM8RenderPageQueues(slot).GetData(pages, 0, 0, pages.Length);
            var records = new uint[MerkabaGrid.RenderIndexCount];
            _grid.GetM8RenderIndex(slot).GetData(records);
            var indices = new uint[MerkabaGrid.RenderPageIndices * 64];
            _grid.GetM8PublishedIndices(slot).GetData(indices, 0, 0,
                indices.Length);
            var vertices = new uint[MerkabaGrid.RenderPageVertices * 64 * 4];
            _grid.GetM8RenderVertices(slot).GetData(vertices, 0, 0,
                vertices.Length);
            return pages.Concat(records).Concat(indices).Concat(vertices)
                .ToArray();
        }

        [Test]
        public void PublicationSlotsOwnDisjointStorage()
        {
            Assert.That(_grid.GetM8RenderMesh(0), Is.Not.SameAs(
                _grid.GetM8RenderMesh(1)));
            Assert.That(_grid.GetM8RenderVertices(0), Is.Not.SameAs(
                _grid.GetM8RenderVertices(1)));
            Assert.That(_grid.GetM8RenderIndices(0), Is.Not.SameAs(
                _grid.GetM8RenderIndices(1)));
            Assert.That(_grid.GetM8RenderPageQueues(0), Is.Not.SameAs(
                _grid.GetM8RenderPageQueues(1)));
            Assert.That(_grid.GetM8PublishedIndices(0), Is.Not.SameAs(
                _grid.GetM8PublishedIndices(1)));
            Assert.That(_grid.GetM8RenderIndex(0), Is.Not.SameAs(
                _grid.GetM8RenderIndex(1)));
        }

        [Test]
        public void FrontStorageIsUntouchedWhileBackBuilds()
        {
            InstallWallTiles(new int3(0, 0, 0));
            U4 first = Build();
            Assert.That(first.Y, Is.EqualTo(MerkabaGrid.ReadoutPublishedStatus));
            int front = _front;
            uint[] before = Snapshot(front);
            MarkTileDirty(new int3(0, 0, 0));
            U4 second = Build();
            Assert.That(second.Y, Is.EqualTo(MerkabaGrid.ReadoutPublishedStatus));
            Assert.That(_front, Is.Not.EqualTo(front), "BACK became FRONT");
            Assert.That(Snapshot(front), Is.EqualTo(before),
                "the old FRONT changed while BACK was built");
        }

        [Test]
        public void FailedBackNeverChangesFront()
        {
            InstallWallTiles(new int3(0, 0, 0));
            U4 first = Build();
            Assert.That(first.Y, Is.EqualTo(MerkabaGrid.ReadoutPublishedStatus));
            int front = _front;
            uint[] before = Snapshot(front);
            // Starve the BACK allocator: the next build cannot place its tile.
            int back = 1 - _front;
            _grid.GetM8RenderPageQueues(back).SetData(new uint[] { 0u }, 0,
                MerkabaGrid.RenderControlFreePages, 1);
            MarkTileDirty(new int3(0, 0, 0));
            U4 failed = Build(false);
            Assert.That(failed.Y, Is.Not.EqualTo(
                MerkabaGrid.ReadoutPublishedStatus));
            Assert.That(_front, Is.EqualTo(front));
            Assert.That(Snapshot(front), Is.EqualTo(before),
                "a failed BACK touched FRONT");
        }

        private uint Counter(int index)
        {
            var value = new uint[1];
            _grid.M8Counters.GetData(value, 0, index, 1);
            return value[0];
        }

        private static T Load<T>(string relative) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(Package + relative);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
    }
}
