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
            uint slot = back[MerkabaGrid.RenderTileListBase];
            int record = Header + (int)slot * RecordWords;
            uint patches = back[record];
            Assert.That(patches, Is.GreaterThan(0u));
            Assert.That(back[1], Is.EqualTo(patches));
            // A page carries both the shared vertices and the indices of one
            // tile, so the chain length is whichever of the two needs more.
            Assert.That(back[record + 1], Is.GreaterThan(0u));
            Assert.That(back[record + 1], Is.LessThanOrEqualTo(
                (patches + (uint)MerkabaGrid.RenderPageIndices - 1u) /
                (uint)MerkabaGrid.RenderPageIndices +
                (uint)MerkabaGrid.RenderTileMaxPages));
            Assert.That(Control(MerkabaGrid.RenderControlFreePages),
                Is.EqualTo((uint)MerkabaGrid.RenderPageCapacity -
                    back[record + 1]));
        }

        [Test]
        public void UnchangedWorldRebuildsNothingAndKeepsPublishedRecords()
        {
            InstallWallTiles(new int3(0, 0, 0), new int3(0, 0, 8));
            Build();
            uint[] first = Index(_front);
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
                    MerkabaGrid.RenderTileListBase + list] * RecordWords;
                for (int word = 0; word < RecordWords; word++)
                    Assert.That(second[record + word],
                        Is.EqualTo(first[record + word]), $"word {word}");
            }
        }

        [Test]
        public void ChangedTileRebuildsOnlyItselfAndResidentHaloNeighbours()
        {
            InstallWallTiles(new int3(0, 0, 0), new int3(0, 0, 8),
                new int3(800, 0, 0));
            Build();
            uint changed = SlotOf(new int3(0, 0, 0));
            MarkRenderDirty(changed);
            Build();
            // The changed tile and its one resident neighbour, never the
            // distant tile: cost follows the change, not the world.
            Assert.That(Counter(MerkabaGrid.CounterRenderRebuildTiles),
                Is.EqualTo(2u));
            Assert.That(Index(_front)[0], Is.EqualTo(3u));
        }

        [Test]
        public void GrowingTheWorldBuildsOnlyTheNewTile()
        {
            InstallWallTiles(new int3(0, 0, 0));
            Build();
            InstallWallTiles(new int3(800, 0, 0));
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
            uint free = Control(MerkabaGrid.RenderControlFreePages);
            uint slot = SlotOf(new int3(0, 0, 0));
            MarkRenderDirty(slot);
            Build();
            uint retired = Control(MerkabaGrid.RenderControlRetireCount);
            Assert.That(retired, Is.GreaterThan(0u));
            Assert.That(Control(MerkabaGrid.RenderControlFreePages),
                Is.EqualTo(free - retired), "old pages still drawn by FRONT");
            Build();
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
            MarkRenderDirty(SlotOf(new int3(0, 0, 0)));
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
            InstallWallTiles(new int3(0, 0, 0), new int3(800, 0, 0));
            Build();
            uint slot = SlotOf(new int3(800, 0, 0));
            SetTileRef(RefIndexOf(slot), RefCold);
            Build();
            uint[] back = Index(_front);
            Assert.That(back[0], Is.EqualTo(1u));
            Assert.That(back[Header + (int)slot * RecordWords + 2], Is.Zero);
            Assert.That(Control(MerkabaGrid.RenderControlRetireCount),
                Is.GreaterThan(0u));
        }

        [Test]
        public void UnresolvedHaloKeepsThePreviousTileRecord()
        {
            InstallWallTiles(new int3(0, 0, 0), new int3(0, 0, 8));
            Build();
            uint target = SlotOf(new int3(0, 0, 0));
            uint neighbour = SlotOf(new int3(0, 0, 8));
            int record = Header + (int)target * RecordWords;
            uint[] before = Index(_front);
            SetTileRef(RefIndexOf(neighbour), RefLoading);
            MarkRenderDirty(target);
            Build();
            uint[] after = Index(_front);
            Assert.That(Counter(MerkabaGrid.CounterRenderPendingTiles),
                Is.EqualTo(1u));
            for (int word = 0; word < RecordWords; word++)
                Assert.That(after[record + word], Is.EqualTo(before[record + word]));
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

            int cull = _readout.FindKernel("CullRenderTiles");
            int prepare = _readout.FindKernel("PrepareVisibleIndices");
            int emit = _readout.FindKernel("EmitVisibleIndices");
            _grid.M8CullControl.SetData(new uint[] { 0u, 0u, 1u, 1u });
            var planes = new Vector4[12];
            for (int plane = 0; plane < 12; plane++)
                planes[plane] = new Vector4(0f, 0f, 0f, 1f);
            _readout.SetVectorArray("_M8CullGridPlanes", planes);
            _readout.SetVector("_M8CameraGridMeters", Vector3.zero);
            MerkabaReadoutCoverage.WriteGridMetric(Matrix4x4.identity,
                out Vector3 diagonal, out Vector3 cross);
            _readout.SetVector("_M8GridMetricDiagonal", diagonal);
            _readout.SetVector("_M8GridMetricCross", cross);
            _readout.SetFloat("_M8RenderDistance", 13f);
            _readout.SetInt("_M8FrontTileCount", (int)front[0]);
            _readout.SetBuffer(cull, "_M8RenderIndexFrontRead",
                _grid.GetM8RenderIndex(_front));
            _readout.SetBuffer(cull, "_M8RenderPageQueues",
                _grid.GetM8RenderPageQueues(_front));
            _readout.SetBuffer(cull, "_M8VisiblePages", _grid.M8VisiblePages);
            _readout.SetBuffer(cull, "_M8CullControl", _grid.M8CullControl);
            _readout.SetBuffer(prepare, "_M8CullControl", _grid.M8CullControl);
            _readout.SetBuffer(prepare, "_M8RenderDrawArgs",
                _grid.M8RenderDrawArgs);
            _readout.SetBuffer(emit, "_M8VisiblePagesRead", _grid.M8VisiblePages);
            _readout.SetBuffer(emit, "_M8CullControlRead", _grid.M8CullControl);
            _readout.SetBuffer(emit, "_M8VisibleIndices",
                _grid.GetM8RenderIndices(_front));
            _readout.SetBuffer(emit, "_M8RenderIndicesRead",
                _grid.GetM8PublishedIndices(_front));
            _readout.Dispatch(cull, 1, 1, 1);
            _readout.Dispatch(prepare, 1, 1, 1);
            _readout.DispatchIndirect(emit, _grid.M8CullControl, sizeof(uint));

            var control = new uint[4];
            _grid.M8CullControl.GetData(control);
            Assert.That(control[0], Is.EqualTo(nearPages), "far tile culled");
            var drawArgs = new uint[5];
            _grid.M8RenderDrawArgs.GetData(drawArgs);
            Assert.That(drawArgs[0], Is.EqualTo(nearPages *
                (uint)MerkabaGrid.RenderPageIndices));
            var pages = new uint[nearPages];
            _grid.M8VisiblePages.GetData(pages, 0, 0, (int)nearPages);
            var indices = new uint[MerkabaGrid.RenderPageIndices];
            _grid.GetM8RenderIndices(_front).GetData(indices, 0, 0,
                indices.Length);
            uint firstPage = pages[0] & 0xffffu;
            uint pageVertex = firstPage * (uint)MerkabaGrid.RenderPageVertices;
            // Published indices address shared vertices of the same tile.
            Assert.That(indices[0], Is.GreaterThanOrEqualTo(pageVertex));
            Assert.That(indices[0], Is.LessThan((uint)
                MerkabaGrid.ReadoutVertexCapacity));
            uint used = ((pages[0] >> 16) & 0xffu) + 1u;
            Assert.That(used, Is.GreaterThan(0u));
        }

        private U4 Build(bool publish = true)
        {
            int back = 1 - _front;
            var command = new CommandBuffer { name = "readout-gpu-test" };
            try
            {
                _renderer.RecordBuild(command, back, ++_revision, _published,
                    true, Vector3.zero);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                command.Release();
            }
            var completion = new U4[2];
            _grid.M8AttemptCompletion.GetData(completion);
            Assert.That(completion[1].X, Is.EqualTo(_revision));
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

        private void MarkRenderDirty(uint slot)
        {
            var runtime = new U4[1];
            _grid.M8TileRecords.GetData(runtime, 0, (int)slot * 2 + 1, 1);
            runtime[0].W |= 1u;
            _grid.M8TileRecords.SetData(runtime, 0, (int)slot * 2 + 1, 1);
        }

        private uint[] Index(int slot)
        {
            var values = new uint[MerkabaGrid.RenderIndexCount];
            _grid.GetM8RenderIndex(slot).GetData(values);
            return values;
        }

        private string Diagnostics() =>
            $"rebuild={Counter(MerkabaGrid.CounterRenderRebuildTiles)} " +

            $"retire={Control(MerkabaGrid.RenderControlRetireCount)} " +
            $"alloc={Control(MerkabaGrid.RenderControlAllocCount)} " +
            $"reclaim={Control(MerkabaGrid.RenderControlReclaimCount)} " +

            $"pending={Counter(MerkabaGrid.CounterRenderPendingTiles)}";

        private uint Control(int index) => ControlOf(_front, index);

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
