using System;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    /// <summary>
    /// Executes the production MerkabaWorld storage kernels on the editor GPU.
    /// Persist and Evict are distinct residency operations.
    /// </summary>
    public sealed class MerkabaResidencyGpuTests
    {
        private const string Package = "Packages/com.genesis.roomscan/";
        private const uint RefCold = 0xfffffffeu;
        private const uint RefLoading = 0xfffffffdu;
        private const uint RefEvicting = 0xfffffffcu;
        private const int CounterHot = 2;
        private const int CounterWritebackCount = 20;
        private const int CounterFrameEpoch = 25;
        private const int CounterFreeTiles = 54;

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
        private uint _slot;

        [SetUp]
        public void CreateWorldWithOneHotTile()
        {
            _owner = new GameObject("m8-residency-gpu");
            _grid = _owner.AddComponent<MerkabaGrid>();
            typeof(MerkabaGrid).GetField("worldCompute",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_grid, AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    Package + "Runtime/Shaders/MerkabaWorld.compute"));
            _grid.EnsureGpuResources();

            _grid.M8LoadStagingAddresses.SetData(new[]
            {
                new MerkabaTileAddress(new int3(0, 0, 0), 0u)
            });
            _grid.RegisterLoadedTileAddresses(1);
            Assert.That(TileRef(), Is.EqualTo(RefCold));
            SetTileRef(RefLoading);

            var states = new KernelState[MerkabaSpatial.KernelsPerTile];
            states[3].Apply(MerkabaObservationKind.Surface, 1f,
                new Color32(10, 20, 30, 255));
            _grid.M8LoadStagingAddresses.SetData(new[]
            {
                new MerkabaTileAddress(new int3(0, 0, 0), 0u)
            });
            _grid.M8LoadStagingStates.SetData(states);
            _grid.InstallLoadedTiles(1);
            uint tileRef = TileRef();
            Assert.That(tileRef, Is.InRange(1u,
                (uint)MerkabaSpatial.PhysicalTileCapacity));
            _slot = tileRef - 1u;
        }

        [TearDown]
        public void ReleaseWorld()
        {
            _grid?.ReleaseOwnedResourcesAfterGpuRetirement();
            if (_owner != null) UnityEngine.Object.DestroyImmediate(_owner);
        }

        [Test]
        public void PersistWritesDirtyTileAndKeepsItHot()
        {
            SetRuntime(dirty: 1u, epoch: 0u);
            SetCounter(CounterFrameEpoch, 100u);
            uint hotBefore = Counter(CounterHot);
            // A focus far away must not matter to Persist.
            _grid.SetResidencyFocus(new Vector3(1000f, 1000f, 1000f), 0f);

            _grid.SelectWritebackBatch(true);
            Assert.That(Counter(CounterWritebackCount), Is.EqualTo(1u));
            Assert.That(TileRef(), Is.EqualTo(_slot + 1u), "stays HOT");
            Assert.That(Runtime().Y, Is.EqualTo(2u), "PERSISTING");
            var staged = new U4[MerkabaSpatial.KernelsPerTile + 1];
            _grid.M8WritebackStaging.GetData(staged, 0, 0, staged.Length);
            Assert.That(staged[0].W, Is.EqualTo(0u), "logical address");
            Assert.That(staged[1 + 3].W & MerkabaConstants.OccupiedFlag,
                Is.Not.Zero);

            _grid.AcknowledgeWritebackBatch(1, true);
            Assert.That(Runtime().Y, Is.EqualTo(0u), "clean");
            Assert.That(TileRef(), Is.EqualTo(_slot + 1u));
            Assert.That(Counter(CounterHot), Is.EqualTo(hotBefore));
            Assert.That(Counter(CounterWritebackCount), Is.Zero);
        }

        [Test]
        public void MutationDuringPersistKeepsTileDirty()
        {
            SetRuntime(dirty: 1u, epoch: 0u);
            _grid.SelectWritebackBatch(true);
            Assert.That(Runtime().Y, Is.EqualTo(2u));
            SetRuntime(dirty: 1u, epoch: 0u);
            _grid.AcknowledgeWritebackBatch(1, true);
            Assert.That(Runtime().Y, Is.EqualTo(1u));

            _grid.SelectWritebackBatch(true);
            _grid.FailWritebackBatch(1, true);
            Assert.That(Runtime().Y, Is.EqualTo(1u));
            Assert.That(TileRef(), Is.EqualTo(_slot + 1u));
        }

        [Test]
        public void EvictionNeverSelectsTilesInsideTheResidencyFocus()
        {
            SetRuntime(dirty: 1u, epoch: 0u);
            SetCounter(CounterFrameEpoch, 100u);
            _grid.SetResidencyFocus(new Vector3(0.1f, 0.1f, 0.1f), 13f);
            _grid.SelectWritebackBatch(false);
            Assert.That(Counter(CounterWritebackCount), Is.Zero);
            Assert.That(TileRef(), Is.EqualTo(_slot + 1u));

            // Recently touched by scan or readout work: still protected.
            SetRuntime(dirty: 1u, epoch: 98u);
            _grid.SetResidencyFocus(new Vector3(500f, 0f, 0f), 13f);
            _grid.SelectWritebackBatch(false);
            Assert.That(Counter(CounterWritebackCount), Is.Zero);
        }

        [Test]
        public void EvictionWritesBackThenFreesAndFailureReturnsHotDirty()
        {
            SetRuntime(dirty: 1u, epoch: 0u);
            SetCounter(CounterFrameEpoch, 100u);
            _grid.SetResidencyFocus(new Vector3(500f, 0f, 0f), 13f);

            _grid.SelectWritebackBatch(false);
            Assert.That(Counter(CounterWritebackCount), Is.EqualTo(1u));
            Assert.That(TileRef(), Is.EqualTo(RefEvicting));
            _grid.FailWritebackBatch(1, false);
            Assert.That(TileRef(), Is.EqualTo(_slot + 1u));
            Assert.That(Runtime().Y, Is.EqualTo(1u));

            uint hot = Counter(CounterHot);
            uint free = Counter(CounterFreeTiles);
            _grid.SelectWritebackBatch(false);
            Assert.That(TileRef(), Is.EqualTo(RefEvicting));
            _grid.AcknowledgeWritebackBatch(1, false);
            Assert.That(TileRef(), Is.EqualTo(RefCold));
            Assert.That(Counter(CounterHot), Is.EqualTo(hot - 1u));
            Assert.That(Counter(CounterFreeTiles), Is.EqualTo(free + 1u));
        }

        private uint TileRef()
        {
            var refs = new uint[1];
            _grid.M8ChunkTileRefs.GetData(refs, 0, 0, 1);
            return refs[0];
        }

        private void SetTileRef(uint value) =>
            _grid.M8ChunkTileRefs.SetData(new[] { value }, 0, 0, 1);

        private U4 Runtime()
        {
            var record = new U4[1];
            _grid.M8TileRecords.GetData(record, 0, (int)_slot * 2 + 1, 1);
            return record[0];
        }

        private void SetRuntime(uint dirty, uint epoch)
        {
            U4 record = Runtime();
            record.Y = dirty;
            record.Z = epoch;
            _grid.M8TileRecords.SetData(new[] { record }, 0,
                (int)_slot * 2 + 1, 1);
        }

        private uint Counter(int index)
        {
            var value = new uint[1];
            _grid.M8Counters.GetData(value, 0, index, 1);
            return value[0];
        }

        private void SetCounter(int index, uint value) =>
            _grid.M8Counters.SetData(new[] { value }, 0, index, 1);
    }
}
