using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaDurableStoreTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct UInt2
        {
            public uint X;
            public uint Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

        [Test]
        public void DirectoryOpenFlagsMatchAndroidArmAndLinuxAbis()
        {
            Assert.That(SigmaDurableStore.AndroidDirectoryOpenFlag,
                Is.EqualTo(0x4000));
            Assert.That(SigmaDurableStore.LinuxDirectoryOpenFlag,
                Is.EqualTo(0x10000));
#if UNITY_EDITOR_LINUX
            Assert.That(SigmaDurableStore.CurrentDirectoryOpenFlag,
                Is.EqualTo(SigmaDurableStore.LinuxDirectoryOpenFlag));
#endif
        }

        [Test]
        public void RootHeadAndPageRecordAreCanonicalAndFingerprintPinned()
        {
            Assert.That(SigmaDurableSchema.ProgramFingerprint,
                Is.EqualTo(SigmaGeneratedMerkabaProgram.ProgramFingerprint));
            Assert.That(SigmaDurableSchema.DefaultFingerprint,
                Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.DefaultRepresentationProofFingerprint));

            SigmaDurableHash pageRoot = Hash(1);
            SigmaDurableHash supportRoot = Hash(2);
            SigmaDurableHash certificateRoot = Hash(3);
            SigmaDurableHash frontierRoot = Hash(4);
            var root = new SigmaDurableRootObject(91UL, pageRoot,
                supportRoot, certificateRoot, frontierRoot);
            byte[] bytes = root.Encode();
            SigmaDurableRootObject decoded = SigmaDurableRootObject.Decode(bytes);
            CollectionAssert.AreEqual(bytes, decoded.Encode());
            Assert.That(decoded.Revision, Is.EqualTo(91UL));
            Assert.That(decoded.SparsePageMapRootHash, Is.EqualTo(pageRoot));
            Assert.That(decoded.QuerySupportRootHash, Is.EqualTo(supportRoot));
            Assert.That(decoded.CertificateManifestRootHash,
                Is.EqualTo(certificateRoot));
            Assert.That(decoded.UnresolvedFrontierRootHash,
                Is.EqualTo(frontierRoot));

            SigmaDurableHash rootHash = SigmaDurableHash.Compute(bytes);
            byte[] head = SigmaDurableHead.Encode(rootHash);
            Assert.That(SigmaDurableHead.Decode(head), Is.EqualTo(rootHash));
            CollectionAssert.AreEqual(head,
                SigmaDurableHead.Encode(SigmaDurableHead.Decode(head)));

            byte[] wrongProgram = (byte[])bytes.Clone();
            wrongProgram[16 + SigmaDurableHash.ByteCount] ^= 0x80;
            Assert.Throws<InvalidDataException>(() =>
                SigmaDurableRootObject.Decode(wrongProgram));
        }

        [Test]
        public void SparseCowMapUsesFullSignedKeyAndOnlyOneBoundedPath()
        {
            var store = new MemoryObjectStore();
            SigmaDurableHash root = SigmaDurableHash.Zero;
            var coordinates = new[]
            {
                new SigmaCarrierPageCoordinate(long.MinValue, long.MaxValue),
                new SigmaCarrierPageCoordinate(-1, 0),
                new SigmaCarrierPageCoordinate(0, -1),
                new SigmaCarrierPageCoordinate(long.MaxValue, long.MinValue),
            };

            for (int index = 0; index < coordinates.Length; ++index)
            {
                byte[] value = MakeRecord(coordinates[index],
                    (uint)(index + 1)).Encode();
                SigmaSparseUpdateResult update = SigmaSparseMerkleMap.Set(store,
                    root, SigmaSparseValueKind.PageRecord, coordinates[index], value);
                Assert.That(update.Changed, Is.True);
                Assert.That(update.NodesCreated, Is.LessThanOrEqualTo(33));
                root = update.Root;
            }

            for (int index = 0; index < coordinates.Length; ++index)
            {
                Assert.That(SigmaSparseMerkleMap.TryGet(store, root,
                    SigmaSparseValueKind.PageRecord, coordinates[index],
                    out byte[] value), Is.True);
                SigmaDurablePageRecord decoded =
                    SigmaDurablePageRecord.Decode(value);
                Assert.That(decoded.Coordinate, Is.EqualTo(coordinates[index]));
                Assert.That(decoded.StateGeneration, Is.EqualTo((uint)index + 1u));
            }

            SigmaCarrierPageCoordinate unchangedCoordinate = coordinates[2];
            byte[] unchanged = MakeRecord(unchangedCoordinate, 3u).Encode();
            int before = store.CreatedCount;
            SigmaSparseUpdateResult noChange = SigmaSparseMerkleMap.Set(store,
                root, SigmaSparseValueKind.PageRecord, unchangedCoordinate,
                unchanged);
            Assert.That(noChange.Changed, Is.False);
            Assert.That(noChange.NodesCreated, Is.Zero);
            Assert.That(store.CreatedCount, Is.EqualTo(before));
            Assert.That(noChange.Root, Is.EqualTo(root));

            store.ResetReadCount();
            byte[] changed = MakeRecord(unchangedCoordinate, 44u).Encode();
            SigmaSparseUpdateResult changedUpdate = SigmaSparseMerkleMap.Set(
                store, root, SigmaSparseValueKind.PageRecord,
                unchangedCoordinate, changed);
            Assert.That(changedUpdate.Changed, Is.True);
            Assert.That(changedUpdate.NodesCreated, Is.EqualTo(33));
            Assert.That(store.ReadCount, Is.EqualTo(33),
                "A changed leaf must read its COW path exactly once.");
            root = changedUpdate.Root;

            Assert.That(SigmaSparseMerkleMap.TryGet(store, root,
                SigmaSparseValueKind.PageRecord,
                new SigmaCarrierPageCoordinate(1, 1), out _), Is.False);
            Assert.That(SigmaSparseMerkleMap.VerifyReachable(store, root,
                SigmaSparseValueKind.PageRecord), Is.GreaterThan(0));
        }

        [Test]
        public void SparseCowBatchWritesSharedPrefixesOnceAndMatchesExactRoot()
        {
            var updates = new SigmaSparseMapUpdate[3];
            for (int index = 0; index < updates.Length; ++index)
            {
                var coordinate = new SigmaCarrierPageCoordinate(100 + index, 0);
                updates[index] = new SigmaSparseMapUpdate(coordinate,
                    MakeRecord(coordinate, (uint)index + 1u).Encode());
            }

            var sequentialStore = new MemoryObjectStore();
            SigmaDurableHash sequentialRoot = SigmaDurableHash.Zero;
            for (int index = 0; index < updates.Length; ++index)
            {
                SigmaSparseMapUpdate update = updates[index];
                sequentialRoot = SigmaSparseMerkleMap.Set(sequentialStore,
                    sequentialRoot, SigmaSparseValueKind.PageRecord,
                    update.Coordinate, update.CanonicalValue).Root;
            }

            var batchStore = new MemoryObjectStore();
            SigmaSparseUpdateResult batch = SigmaSparseMerkleMap.SetBatch(
                batchStore, SigmaDurableHash.Zero,
                SigmaSparseValueKind.PageRecord, new[]
                {
                    updates[2], updates[0], updates[1],
                });
            Assert.That(batch.Changed, Is.True);
            Assert.That(batch.Root, Is.EqualTo(sequentialRoot));
            Assert.That(batch.NodesCreated,
                Is.LessThan(sequentialStore.CreatedCount));
            for (int index = 0; index < updates.Length; ++index)
            {
                Assert.That(SigmaSparseMerkleMap.TryGet(batchStore, batch.Root,
                    SigmaSparseValueKind.PageRecord,
                    updates[index].Coordinate, out byte[] value), Is.True);
                CollectionAssert.AreEqual(updates[index].CanonicalValue, value);
            }
        }

        [Test]
        public void DeliberateImmutableObjectHashCollisionFailsClosedOnFullBytes()
        {
            SigmaDurableHash forced = Hash(99);
            var store = new MemoryObjectStore(_ => forced);
            Assert.That(store.Put(new byte[] { 1, 2, 3 },
                SigmaDurableObjectKind.PageBlob, out bool created),
                Is.EqualTo(forced));
            Assert.That(created, Is.True);
            Assert.Throws<InvalidDataException>(() =>
                store.Put(new byte[] { 1, 2, 4 },
                    SigmaDurableObjectKind.PageBlob, out _));
            Assert.That(store.Put(new byte[] { 1, 2, 3 },
                SigmaDurableObjectKind.PageBlob, out created),
                Is.EqualTo(forced));
            Assert.That(created, Is.False);
        }

        [Test]
        public void PageRecordValidatesCompleteCanonicalBlobAndFullLogicalKey()
        {
            SigmaDecodedPage page = MakeSmallRepresentedPage(
                new SigmaCarrierPageCoordinate(-17, 23));
            byte[] blob = SigmaCarrierCodec.EncodePage(page);
            SigmaDurableHash blobHash = SigmaDurableHash.Compute(blob);
            var support = new SigmaQuerySupportReceipt(7u,
                SigmaQuerySupportFlags.Verified |
                    SigmaQuerySupportFlags.MayContribute,
                SigmaDurableHash.Compute(new byte[] { 9, 8, 7 }));
            SigmaDurablePageRecord record = SigmaDurablePageRecord.FromPage(page,
                13u, blobHash, support);
            byte[] recordBytes = record.Encode();
            SigmaDurablePageRecord decoded =
                SigmaDurablePageRecord.Decode(recordBytes);
            CollectionAssert.AreEqual(recordBytes, decoded.Encode());
            decoded.ValidatePageBytes(blob);

            byte[] corrupt = (byte[])blob.Clone();
            corrupt[corrupt.Length - 1] ^= 1;
            Assert.Throws<InvalidDataException>(() =>
                decoded.ValidatePageBytes(corrupt));

            var wrongKey = new SigmaDurablePageRecord(
                new SigmaCarrierPageCoordinate(-16, 23), page.Generation,
                page.GaugeGeneration, page.CertificateGeneration, 13u,
                page.Revision, blobHash, support);
            Assert.Throws<InvalidDataException>(() =>
                wrongKey.ValidatePageBytes(blob));
        }

        [Test]
        public void RehydrateMetadataKeepsStateAndPageGenerationsDistinct()
        {
            SigmaDecodedPage page = MakeOneSamplePage(
                new SigmaCarrierPageCoordinate(-17, 23), 7u, 11u, 13u,
                17u, 19L);
            SigmaCarrierPageMetadataGpu metadata =
                SigmaCarrierPageMetadataGpu.FromPage(page, 29u);

            Assert.That(metadata.Generation, Is.EqualTo(29u),
                "The resident page selector uses the page generation.");
            Assert.That(metadata.StateGeneration, Is.EqualTo(7u),
                "Gauge/certificate-only revisions must preserve S16 generation.");
            Assert.That(metadata.GaugeGeneration,
                Is.EqualTo(page.GaugeGeneration));
            Assert.That(metadata.CertificateGeneration,
                Is.EqualTo(page.CertificateGeneration));
        }

        [Test]
        public void GpuStagedBlocksAssembleExactCanonicalMixedPageBytes()
        {
            SigmaDecodedPage page = MakeFullMixedRepresentedPage();
            byte[] cpuBytes = SigmaCarrierCodec.EncodePage(page);
            SigmaEncodedPageHeader header =
                SigmaCarrierCodec.ReadPageHeader(cpuBytes);
            Assert.That(header.Coordinate, Is.EqualTo(page.Coordinate));
            Assert.That(header.Generation, Is.EqualTo(page.Generation));
            Assert.That(header.Revision, Is.EqualTo(page.Revision));
            Assert.That(header.ActiveSampleCount,
                Is.EqualTo((uint)SigmaDecodedPage.SampleCount));
            Assert.That(header.RepresentationCount,
                Is.EqualTo((uint)SigmaDecodedPage.SampleCount));
            SigmaEncodedBlock[] staged = EncodeBlocksOnGpu(page.CopySamples());
            byte[] stagedBytes = SigmaCarrierCodec.EncodePageFromBlocks(page, staged);
            CollectionAssert.AreEqual(cpuBytes, stagedBytes);
            uint[] flatRepresentation = FlattenRepresentation(page);
            SigmaCarrierCodec.ValidateStagedRepresentationAndCoverage(header,
                flatRepresentation, 0, staged);
            byte[] metadataOnlyBytes =
                SigmaCarrierCodec.EncodePageFromStagedBlocks(header,
                    flatRepresentation, 0, staged);
            CollectionAssert.AreEqual(cpuBytes, metadataOnlyBytes);
            SigmaDecodedPage decoded = SigmaCarrierCodec.DecodePage(stagedBytes);
            CollectionAssert.AreEqual(page.CopySamples(), decoded.CopySamples());
            CollectionAssert.AreEqual(stagedBytes,
                SigmaCarrierCodec.EncodePage(decoded));

            var modes = new HashSet<SigmaBlockMode>();
            for (int block = 0; block < staged.Length; ++block)
                modes.Add(staged[block].Mode);
            CollectionAssert.AreEquivalent(new[]
            {
                SigmaBlockMode.Null, SigmaBlockMode.Constant,
                SigmaBlockMode.Affine, SigmaBlockMode.Delta,
                SigmaBlockMode.Raw,
            }, modes);
            SigmaCarrierRepresentationRecord[] representation =
                decoded.CopyRepresentation();
            Assert.That(representation[0].Words[4], Is.EqualTo(0u));
            Assert.That(representation[1].Words[4], Is.EqualTo(1u));
            Assert.That(representation[2].Words[4], Is.EqualTo(2u));

            int modeOffset = checked(256 + SigmaDecodedPage.SampleCount *
                (sizeof(ushort) +
                    SigmaCarrierRepresentationRecord.WordCount * sizeof(uint)));
            byte[] badFingerprint = (byte[])cpuBytes.Clone();
            badFingerprint[60] ^= 0x80;
            Assert.Throws<InvalidDataException>(() =>
                SigmaCarrierCodec.ReadPageHeader(badFingerprint));
            byte[] badMode = (byte[])cpuBytes.Clone();
            badMode[modeOffset] = byte.MaxValue;
            Assert.Throws<InvalidDataException>(() =>
                SigmaCarrierCodec.ReadPageHeader(badMode));
            byte[] badOffset = (byte[])cpuBytes.Clone();
            int offsetTable = modeOffset + SigmaDecodedPage.BlockCount;
            badOffset[offsetTable] = 1;
            Assert.Throws<InvalidDataException>(() =>
                SigmaCarrierCodec.ReadPageHeader(badOffset));
            byte[] truncated = new byte[cpuBytes.Length - 1];
            Buffer.BlockCopy(cpuBytes, 0, truncated, 0, truncated.Length);
            Assert.Throws<InvalidDataException>(() =>
                SigmaCarrierCodec.ReadPageHeader(truncated));
        }

        [Test]
        public void MetadataOnlyGpuStagePreservesExactPageWithoutDecodedPage()
        {
            SigmaDecodedPage oracle = MakeSmallRepresentedPage(
                new SigmaCarrierPageCoordinate(-31, 47));
            SigmaEncodedPageHeader header =
                SigmaEncodedPageHeader.FromPage(oracle);
            SigmaEncodedBlock[] blocks = new SigmaEncodedBlock[
                SigmaDecodedPage.BlockCount];
            for (int block = 0; block < blocks.Length; ++block)
                blocks[block] = SigmaCarrierCodec.EncodeBlock(
                    oracle.CopyBlock(block));
            uint[] representation = FlattenRepresentation(oracle);
            SigmaCarrierCodec.ValidateStagedRepresentationAndCoverage(header,
                representation, 0, blocks);
            byte[] oracleBytes = SigmaCarrierCodec.EncodePage(oracle);
            byte[] stagedBytes = SigmaCarrierCodec.EncodePageFromStagedBlocks(
                header, representation, 0, blocks);
            CollectionAssert.AreEqual(oracleBytes, stagedBytes);

            byte[] supportBytes = SigmaQuerySupportSummary.FromHeader(header,
                29u, SigmaQuerySupportFlags.Verified |
                    SigmaQuerySupportFlags.MayContribute).Encode();
            var support = new SigmaQuerySupportReceipt(29u,
                SigmaQuerySupportFlags.Verified |
                    SigmaQuerySupportFlags.MayContribute,
                SigmaDurableHash.Compute(supportBytes));
            SigmaDurablePageUpdate update =
                SigmaDurablePageUpdate.FromDirectCodecVerifiedStage(header,
                    29u, support, stagedBytes, supportBytes);
            Assert.That(update.Page, Is.Null);
            Assert.That(update.Coordinate, Is.EqualTo(oracle.Coordinate));
            Assert.That(update.StagedCanonicalPageBytes,
                Is.SameAs(stagedBytes));
            Assert.That(update.QuerySupportSummaryBytes,
                Is.SameAs(supportBytes));

            uint[] badRepresentation = (uint[])representation.Clone();
            badRepresentation[6] ^= 1u;
            Assert.Throws<InvalidDataException>(() =>
                SigmaCarrierCodec.ValidateStagedRepresentationAndCoverage(
                    header, badRepresentation, 0, blocks));

            SigmaEncodedBlock[] badCoverage =
                (SigmaEncodedBlock[])blocks.Clone();
            var nonzero = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int sample = 0; sample < nonzero.Length; ++sample)
                nonzero[sample] = State(1L);
            badCoverage[badCoverage.Length - 1] =
                SigmaCarrierCodec.EncodeBlock(nonzero);
            Assert.Throws<InvalidDataException>(() =>
                SigmaCarrierCodec.ValidateStagedRepresentationAndCoverage(
                    header, representation, 0, badCoverage));
        }

        [Test]
        public void RestartCachesVerifiedHeadInventoryAndNativeExtent()
        {
            string path = TemporaryDirectory();
            try
            {
                SigmaDurablePageUpdate first = DurableUpdate(
                    new SigmaCarrierPageCoordinate(0, 0), 1u, 17L);
                SigmaDurablePageUpdate second = DurableUpdate(
                    new SigmaCarrierPageCoordinate(2, 0), 1u, 19L);
                var store = new SigmaDurableStore(path);
                store.Commit(new SigmaDurableCommitRequest(1UL,
                    new[] { first, second }));

                long expectedPageBytes =
                    SigmaCarrierCodec.EncodePage(first.Page).LongLength +
                    SigmaCarrierCodec.EncodePage(second.Page).LongLength;
                var restarted = new SigmaDurableStore(path);
                Assert.That(restarted.HeadInventoryPageCount, Is.EqualTo(2));
                Assert.That(restarted.HeadInventoryPageBytes,
                    Is.EqualTo(expectedPageBytes));
                using SigmaDurableRootLease root = restarted.PinHead();
                Assert.That(restarted.ComputeLogicalExtent(root),
                    Is.EqualTo(2u * SigmaCarrier.SamplesPerPage + 1u));
                Assert.That(restarted.EnumeratePageRecords(root).Count,
                    Is.EqualTo(2));
            }
            finally
            {
                DeleteDirectory(path);
            }
        }

        [Test]
        public void MissingOrUnverifiedSupportIsAlwaysConservative()
        {
            Assert.That(new SigmaQuerySupportReceipt(0u,
                SigmaQuerySupportFlags.None, SigmaDurableHash.Zero)
                .MayContribute, Is.True);
            Assert.That(new SigmaQuerySupportReceipt(1u,
                SigmaQuerySupportFlags.Verified, Hash(1))
                .MayContribute, Is.False);
            Assert.That(new SigmaQuerySupportReceipt(1u,
                SigmaQuerySupportFlags.Verified |
                    SigmaQuerySupportFlags.MayContribute, Hash(1))
                .MayContribute, Is.True);
        }

        [Test]
        public void QuerySupportSummaryPinsFullPageKeyAndAllFingerprints()
        {
            SigmaDecodedPage page = MakeOneSamplePage(
                new SigmaCarrierPageCoordinate(-91, 203), 7u, 11u, 13u,
                17u, 19L);
            const SigmaQuerySupportFlags flags =
                SigmaQuerySupportFlags.Verified |
                SigmaQuerySupportFlags.MayContribute;
            byte[] bytes = SigmaQuerySupportSummary.FromPage(page,
                page.Generation, flags)
                .Encode();
            var receipt = new SigmaQuerySupportReceipt(page.Generation, flags,
                SigmaDurableHash.Compute(bytes));
            SigmaQuerySupportSummary.Decode(bytes).Validate(page, receipt);

            var wrongGenerationReceipt = new SigmaQuerySupportReceipt(
                page.Generation + 1u, flags, SigmaDurableHash.Compute(bytes));
            Assert.Throws<InvalidDataException>(() =>
                SigmaQuerySupportSummary.Decode(bytes)
                    .Validate(page, wrongGenerationReceipt));

            var wrongFlagsReceipt = new SigmaQuerySupportReceipt(
                page.Generation, SigmaQuerySupportFlags.Verified,
                SigmaDurableHash.Compute(bytes));
            Assert.Throws<InvalidDataException>(() =>
                SigmaQuerySupportSummary.Decode(bytes)
                    .Validate(page, wrongFlagsReceipt));

            byte[] wrongCoordinate = (byte[])bytes.Clone();
            wrongCoordinate[8] ^= 1;
            Assert.Throws<InvalidDataException>(() =>
                SigmaQuerySupportSummary.Decode(wrongCoordinate)
                    .Validate(page, receipt));

            byte[] wrongFingerprint = (byte[])bytes.Clone();
            wrongFingerprint[40] ^= 1;
            Assert.Throws<InvalidDataException>(() =>
                SigmaQuerySupportSummary.Decode(wrongFingerprint)
                    .Validate(page, receipt));

            byte[] wrongExtent = (byte[])bytes.Clone();
            wrongExtent[32] ^= 1;
            Assert.Throws<InvalidDataException>(() =>
                SigmaQuerySupportSummary.Decode(wrongExtent)
                    .Validate(page, receipt));
        }

        [Test]
        public void GeneratedQuerySupportPlanPinsPredictionWitnessAndFailClosedLaw()
        {
            Assert.That(SigmaGeneratedQuerySupport.QueryId,
                Is.EqualTo("PREDICTION_SUPPORT"));
            Assert.That(SigmaGeneratedQuerySupport.LocalContribution,
                Is.EqualTo("SENSOR_FORWARD_WITNESS"));
            Assert.That(SigmaGeneratedQuerySupport.ProgramFingerprint,
                Is.EqualTo(SigmaDurableSchema.ProgramFingerprint));
            Assert.That(SigmaGeneratedQuerySupport.FalseNegativeAllowance,
                Is.Zero);
            Assert.That(SigmaGeneratedQuerySupport.MissingStaleCorruptMustInclude,
                Is.True);
            Assert.That(SigmaQuerySupportPlan.Fingerprint.ToString(),
                Is.EqualTo(SigmaGeneratedQuerySupport.PlanFingerprint));
        }

        [Test]
        public void QuerySupportHullIsExactAcrossPageAndStagedCodecSpellings()
        {
            SigmaDecodedPage page = GeometryPage(
                new SigmaCarrierPageCoordinate(-7, 11), 23u,
                (-2L, 3L, 4L), (5L, -6L, 7L));
            const SigmaQuerySupportFlags flags =
                SigmaQuerySupportFlags.Verified |
                SigmaQuerySupportFlags.MayContribute;
            SigmaQuerySupportSummary fromPage = SigmaQuerySupportSummary.Decode(
                SigmaQuerySupportSummary.FromPage(page, 23u, flags).Encode());
            var blocks = new SigmaEncodedBlock[SigmaDecodedPage.BlockCount];
            for (int block = 0; block < blocks.Length; ++block)
                blocks[block] = SigmaCarrierCodec.EncodeBlock(
                    page.CopyBlock(block));
            SigmaQuerySupportSummary fromBlocks =
                SigmaQuerySupportSummary.Decode(
                    SigmaQuerySupportSummary.FromStagedBlocks(
                        SigmaEncodedPageHeader.FromPage(page), 23u, flags,
                        blocks).Encode());

            Assert.That(fromPage.HasExactProjectiveBounds, Is.True);
            Assert.That(fromBlocks.HasExactProjectiveBounds, Is.True);
            Assert.That(fromBlocks.ProjectiveBounds,
                Is.EqualTo(fromPage.ProjectiveBounds));
            Assert.That(fromPage.ProjectiveBounds, Is.EqualTo(
                new SigmaQ48Bounds3(
                    SigmaNumericDomain.FromInteger(-2),
                    SigmaNumericDomain.FromInteger(-6),
                    SigmaNumericDomain.FromInteger(4),
                    SigmaNumericDomain.FromInteger(5),
                    SigmaNumericDomain.FromInteger(3),
                    SigmaNumericDomain.FromInteger(7))));
        }

        [Test]
        public void SparseSupportIndexIsIncrementalCollisionSafeAndConservative()
        {
            var index = new SigmaQuerySupportIndex();
            const SigmaQuerySupportFlags flags =
                SigmaQuerySupportFlags.Verified |
                SigmaQuerySupportFlags.MayContribute;
            for (uint item = 1u; item <= 256u; ++item)
            {
                var coordinate = new SigmaCarrierPageCoordinate(item, 0L);
                SigmaDecodedPage page = GeometryPage(coordinate, item,
                    ((long)item * 2L, 0L, 1L));
                index.Upsert(MakeRecord(coordinate, item),
                    SigmaQuerySupportSummary.FromPage(page, item, flags));
            }
            var unknownCoordinate = new SigmaCarrierPageCoordinate(-1, -1);
            index.Upsert(MakeRecord(unknownCoordinate, 1u), null);

            var query = new SigmaQ48Bounds3(
                SigmaNumericDomain.FromInteger(199),
                SigmaNumericDomain.FromInteger(-1),
                SigmaNumericDomain.FromInteger(0),
                SigmaNumericDomain.FromInteger(201),
                SigmaNumericDomain.FromInteger(1),
                SigmaNumericDomain.FromInteger(2));
            IReadOnlyList<SigmaResidencyKey> selected = index.Query(query);
            Assert.That(selected, Has.Count.EqualTo(2));
            Assert.That(selected[0].Coordinate, Is.EqualTo(unknownCoordinate),
                "Missing/stale/corrupt summary is always selected.");
            Assert.That(selected[1].Coordinate,
                Is.EqualTo(new SigmaCarrierPageCoordinate(100, 0)));

            SigmaCarrierPageCoordinate moving = selected[1].Coordinate;
            SigmaDecodedPage moved = GeometryPage(moving, 100u,
                (900L, 0L, 1L));
            SigmaQuerySupportSummary movedSummary =
                SigmaQuerySupportSummary.FromPage(moved, 100u, flags);
            for (int repeat = 0; repeat < 10000; ++repeat)
                index.Upsert(MakeRecord(moving, 100u), movedSummary);
            Assert.That(index.Count, Is.EqualTo(257));
            Assert.That(index.Query(query), Has.Count.EqualTo(1),
                "The old spatial leaf must not survive an incremental update.");

            var disjoint = new SigmaQ48Bounds3(
                SigmaNumericDomain.FromInteger(-1000),
                SigmaNumericDomain.FromInteger(-1000),
                SigmaNumericDomain.FromInteger(-1000),
                SigmaNumericDomain.FromInteger(-999),
                SigmaNumericDomain.FromInteger(-999),
                SigmaNumericDomain.FromInteger(-999));
            Assert.That(index.Query(disjoint), Has.Count.EqualTo(1));
            Assert.That(index.LastVisitedNodes, Is.LessThan(index.Count),
                "A disjoint sparse query must prune the bounded tree.");
        }

        [Test]
        public void RestartRebuildsColdSupportIndexWithoutDecodingWholePages()
        {
            string path = TemporaryDirectory();
            try
            {
                var store = new SigmaDurableStore(path);
                SigmaDurablePageUpdate near = DurableGeometryUpdate(
                    new SigmaCarrierPageCoordinate(1, 0), 1u,
                    (0L, 0L, 2L));
                SigmaDurablePageUpdate far = DurableGeometryUpdate(
                    new SigmaCarrierPageCoordinate(2, 0), 1u,
                    (100L, 100L, 100L));
                store.Commit(new SigmaDurableCommitRequest(1UL,
                    new[] { near, far }));

                var restarted = new SigmaDurableStore(path);
                Assert.That(restarted.QuerySupportPageCount, Is.EqualTo(2));
                Assert.That(restarted.QuerySupportUnboundedCount, Is.Zero);
                IReadOnlyList<SigmaResidencyKey> selected =
                    restarted.SelectPredictionSupport(new SigmaQ48Bounds3(
                        SigmaNumericDomain.FromInteger(-1),
                        SigmaNumericDomain.FromInteger(-1),
                        SigmaNumericDomain.FromInteger(1),
                        SigmaNumericDomain.FromInteger(1),
                        SigmaNumericDomain.FromInteger(1),
                        SigmaNumericDomain.FromInteger(3)));
                Assert.That(selected, Has.Count.EqualTo(1));
                Assert.That(selected[0].Coordinate,
                    Is.EqualTo(near.Coordinate));
            }
            finally
            {
                DeleteDirectory(path);
            }
        }

        [Test]
        public void DirtyOnlyTransactionWritesBoundedCowAndNoChangeWritesNothing()
        {
            string path = TemporaryDirectory();
            try
            {
                var store = new SigmaDurableStore(path);
                SigmaDurablePageUpdate first = DurableUpdate(
                    new SigmaCarrierPageCoordinate(-4, 9), 1u, 17L);
                SigmaDurableCommitResult committed = store.Commit(
                    new SigmaDurableCommitRequest(1UL, new[] { first },
                        new byte[] { 1, 2 }, new byte[] { 3, 4 }));
                Assert.That(committed.Statistics.PageBlobs, Is.EqualTo(1));
                Assert.That(committed.Statistics.PageMapNodes,
                    Is.InRange(1, 33));
                Assert.That(committed.Statistics.SupportMapNodes,
                    Is.InRange(1, 33));
                Assert.That(committed.Statistics.HeadSwapped, Is.True);
                Assert.That(store.TryGetPageRecord(first.Page.Coordinate,
                    out SigmaDurablePageRecord record), Is.True);
                Assert.That(record.Revision, Is.EqualTo(1u));
                Assert.That(store.HeadRecordIndexPageCount, Is.EqualTo(1));
                Assert.That(store.HeadRecordIndexLookups, Is.EqualTo(1L));

                SigmaDurableCommitResult noChange = store.Commit(
                    new SigmaDurableCommitRequest(2UL, new[] { first },
                        new byte[] { 1, 2 }, new byte[] { 3, 4 }));
                Assert.That(noChange.RootObjectHash,
                    Is.EqualTo(committed.RootObjectHash));
                Assert.That(noChange.Statistics.PageBlobs, Is.Zero);
                Assert.That(noChange.Statistics.PageMapNodes, Is.Zero);
                Assert.That(noChange.Statistics.SupportMapNodes, Is.Zero);
                Assert.That(noChange.Statistics.ImmutableObjects, Is.Zero);
                Assert.That(noChange.Statistics.HeadSwapped, Is.False);

                SigmaDurablePageUpdate second = DurableUpdate(
                    new SigmaCarrierPageCoordinate(300, -700), 2u, 31L);
                SigmaDurableCommitResult twoPages = store.Commit(
                    new SigmaDurableCommitRequest(2UL, new[] { second }));
                Assert.That(twoPages.Statistics.PageBlobs, Is.EqualTo(1));
                Assert.That(twoPages.Statistics.PageMapNodes,
                    Is.InRange(1, 33));
                Assert.That(twoPages.Statistics.SupportMapNodes,
                    Is.InRange(1, 33));
                Assert.That(store.TryGetPageRecord(first.Page.Coordinate,
                    out _), Is.True, "Unrelated COW branch was retained.");
                Assert.That(store.TryGetPageRecord(second.Page.Coordinate,
                    out _), Is.True);
                Assert.That(store.HeadRecordIndexPageCount, Is.EqualTo(2),
                    "A dirty commit updates the selected-HEAD index without " +
                    "rebuilding the complete durable map.");
                Assert.That(store.HeadRecordIndexLookups, Is.EqualTo(3L));

                var restarted = new SigmaDurableStore(path);
                Assert.That(restarted.HeadHash,
                    Is.EqualTo(twoPages.RootObjectHash));
                Assert.That(restarted.Head.Revision, Is.EqualTo(2UL));
                Assert.That(restarted.TryGetPageRecord(first.Page.Coordinate,
                    out _), Is.True);
                Assert.That(restarted.TryGetPageRecord(second.Page.Coordinate,
                    out _), Is.True);
                Assert.That(restarted.HeadRecordIndexPageCount, Is.EqualTo(2));
                Assert.That(restarted.HeadRecordIndexLookups, Is.EqualTo(2L));
            }
            finally
            {
                DeleteDirectory(path);
            }
        }

        [Test]
        public void UnresolvedFrontierAdvancesHeadAtSelectedRevisionOnly()
        {
            string path = TemporaryDirectory();
            try
            {
                var store = new SigmaDurableStore(path);
                SigmaDurablePageUpdate page = DurableUpdate(
                    new SigmaCarrierPageCoordinate(7, -3), 1u, 37L);
                SigmaDurableCommitResult baseline = store.Commit(
                    new SigmaDurableCommitRequest(1UL, new[] { page },
                        new byte[] { 1, 2, 3 }, new byte[] { 4 }));
                byte[] frontier = { 9, 8, 7, 6, 5 };

                SigmaDurableCommitResult updated = store.Commit(
                    new SigmaDurableCommitRequest(1UL,
                        Array.Empty<SigmaDurablePageUpdate>(), null,
                        frontier));

                Assert.That(updated.Statistics.HeadSwapped, Is.True);
                Assert.That(updated.Statistics.PageBlobs, Is.Zero);
                Assert.That(updated.Statistics.PageMapNodes, Is.Zero);
                Assert.That(updated.Statistics.SupportMapNodes, Is.Zero);
                Assert.That(updated.RootObject.Revision, Is.EqualTo(1UL));
                Assert.That(updated.RootObject.SparsePageMapRootHash,
                    Is.EqualTo(baseline.RootObject.SparsePageMapRootHash));
                Assert.That(updated.RootObject.QuerySupportRootHash,
                    Is.EqualTo(baseline.RootObject.QuerySupportRootHash));
                Assert.That(updated.RootObject.CertificateManifestRootHash,
                    Is.EqualTo(
                        baseline.RootObject.CertificateManifestRootHash));
                Assert.That(updated.RootObject.UnresolvedFrontierRootHash,
                    Is.Not.EqualTo(
                        baseline.RootObject.UnresolvedFrontierRootHash));
                Assert.That(store.HeadRecordIndexPageCount, Is.EqualTo(1),
                    "A frontier-only HEAD swap preserves the exact page index.");
                using (SigmaDurableRootLease current = store.PinHead())
                {
                    Assert.That(store.TryGetPageRecord(current,
                        page.Page.Coordinate, out SigmaDurablePageRecord currentRecord),
                        Is.True);
                    Assert.That(currentRecord.PageGeneration,
                        Is.EqualTo(page.PageGeneration));
                }

                var restarted = new SigmaDurableStore(path);
                using SigmaDurableRootLease selected = restarted.PinHead();
                Assert.That(restarted.TryReadUnresolvedFrontier(selected,
                    out byte[] restored), Is.True);
                CollectionAssert.AreEqual(frontier, restored);
                Assert.That(restarted.TryGetPageRecord(selected,
                    page.Page.Coordinate, out SigmaDurablePageRecord record),
                    Is.True);
                Assert.That(record.PageGeneration,
                    Is.EqualTo(page.PageGeneration));

                Assert.Throws<InvalidOperationException>(() => store.Commit(
                    new SigmaDurableCommitRequest(1UL,
                        Array.Empty<SigmaDurablePageUpdate>(),
                        new byte[] { 99 }, frontier)));
            }
            finally
            {
                DeleteDirectory(path);
            }
        }

        [Test]
        public void EveryCrashBoundaryRecoversOnlyPriorOrCompleteNextRoot()
        {
            foreach (SigmaDurableFailurePoint target in
                (SigmaDurableFailurePoint[])Enum.GetValues(
                    typeof(SigmaDurableFailurePoint)))
            {
                string path = TemporaryDirectory();
                try
                {
                    var baseline = new SigmaDurableStore(path);
                    baseline.Commit(new SigmaDurableCommitRequest(1UL,
                        new[] { DurableUpdate(
                            new SigmaCarrierPageCoordinate(1, 2), 1u, 5L) },
                        new byte[] { 1 }, new byte[] { 2 }));
                    bool injected = false;
                    var crashing = new SigmaDurableStore(path,
                        (point, kind) =>
                        {
                            if (!injected && point == target)
                            {
                                injected = true;
                                throw new IOException("Injected " + point);
                            }
                        });
                    Assert.Throws<IOException>(() => crashing.Commit(
                        new SigmaDurableCommitRequest(2UL,
                            new[] { DurableUpdate(
                                new SigmaCarrierPageCoordinate(1, 2),
                                2u, 9L) }, new byte[] { 3 },
                            new byte[] { 4 })));
                    Assert.That(injected, Is.True, target.ToString());

                    var restarted = new SigmaDurableStore(path);
                    bool afterSwap = target ==
                            SigmaDurableFailurePoint.AfterHeadSwap ||
                        target == SigmaDurableFailurePoint.HeadDirectoryFlushed;
                    Assert.That(restarted.Head.Revision,
                        Is.EqualTo(afterSwap ? 2UL : 1UL), target.ToString());
                    Assert.That(restarted.TryGetPageRecord(
                        new SigmaCarrierPageCoordinate(1, 2),
                        out SigmaDurablePageRecord record), Is.True);
                    Assert.That(record.Revision,
                        Is.EqualTo(afterSwap ? 2u : 1u), target.ToString());
                }
                finally
                {
                    DeleteDirectory(path);
                }
            }
        }

        [Test]
        public void ProductionObjectStoreRejectsDeliberatePageBlobCollision()
        {
            string path = TemporaryDirectory();
            try
            {
                SigmaDurableHash forced = Hash(78);
                var store = new SigmaDurableStore(path, hash: _ => forced);
                store.Put(new byte[] { 1, 2, 3 },
                    SigmaDurableObjectKind.PageBlob, out _);
                Assert.Throws<InvalidDataException>(() => store.Put(
                    new byte[] { 1, 2, 4 }, SigmaDurableObjectKind.PageBlob,
                    out _));
            }
            finally
            {
                DeleteDirectory(path);
            }
        }

        [Test]
        public void GaugeOnlyAndMultiPageRevisionsPublishAtomicallyAtHeadLast()
        {
            string path = TemporaryDirectory();
            try
            {
                var store = new SigmaDurableStore(path);
                SigmaDurablePageUpdate left = DurableUpdate(
                    new SigmaCarrierPageCoordinate(-1, -1), 1u, 11L);
                SigmaDurablePageUpdate right = DurableUpdate(
                    new SigmaCarrierPageCoordinate(1, 1), 1u, 13L);
                store.Commit(new SigmaDurableCommitRequest(1UL,
                    new[] { left, right }, new byte[] { 1 }, new byte[] { 2 }));

                SigmaDecodedPage gaugePage = MakeOneSamplePage(
                    left.Page.Coordinate, 1u, 2u, 2u, 2u, 11L);
                byte[] summary = SigmaQuerySupportSummary.FromPage(gaugePage,
                    2u,
                    SigmaQuerySupportFlags.Verified |
                    SigmaQuerySupportFlags.MayContribute).Encode();
                var gaugeUpdate = new SigmaDurablePageUpdate(gaugePage, 2u,
                    new SigmaQuerySupportReceipt(2u,
                        SigmaQuerySupportFlags.Verified |
                            SigmaQuerySupportFlags.MayContribute,
                        SigmaDurableHash.Compute(summary)), null, summary);
                SigmaDurableCommitResult gaugeCommit = store.Commit(
                    new SigmaDurableCommitRequest(2UL,
                        new[] { gaugeUpdate }, new byte[] { 7 },
                        new byte[] { 8 }));
                Assert.That(gaugeCommit.Statistics.HeadSwapped, Is.True);
                Assert.That(store.Head.Revision, Is.EqualTo(2UL));
                Assert.That(store.TryGetPageRecord(left.Page.Coordinate,
                    out SigmaDurablePageRecord gaugeRecord), Is.True);
                Assert.That(gaugeRecord.GaugeGeneration, Is.EqualTo(2u));
                Assert.That(store.TryGetPageRecord(right.Page.Coordinate,
                    out SigmaDurablePageRecord unchanged), Is.True);
                Assert.That(unchanged.Revision, Is.EqualTo(1u));
            }
            finally
            {
                DeleteDirectory(path);
            }
        }

        [Test]
        public void PhysicalUpdateOrderCannotChangeCanonicalDurableRoot()
        {
            string leftPath = TemporaryDirectory();
            string rightPath = TemporaryDirectory();
            try
            {
                SigmaDurablePageUpdate[] canonical =
                {
                    DurableUpdate(new SigmaCarrierPageCoordinate(-19, 7),
                        1u, 11L),
                    DurableUpdate(new SigmaCarrierPageCoordinate(0, 0),
                        1u, 13L),
                    DurableUpdate(new SigmaCarrierPageCoordinate(31, -5),
                        1u, 17L),
                    DurableUpdate(new SigmaCarrierPageCoordinate(long.MaxValue,
                        long.MinValue), 1u, 19L),
                };
                SigmaDurablePageUpdate[] permuted =
                {
                    canonical[3], canonical[1], canonical[0], canonical[2],
                };
                byte[] certificate = { 1, 3, 3, 7 };
                byte[] frontier = { 2, 4, 6, 8 };

                var left = new SigmaDurableStore(leftPath);
                var right = new SigmaDurableStore(rightPath);
                SigmaDurableCommitResult leftCommit = left.Commit(
                    new SigmaDurableCommitRequest(1UL, canonical,
                        certificate, frontier));
                SigmaDurableCommitResult rightCommit = right.Commit(
                    new SigmaDurableCommitRequest(1UL, permuted,
                        certificate, frontier));

                Assert.That(rightCommit.RootObjectHash,
                    Is.EqualTo(leftCommit.RootObjectHash));
                CollectionAssert.AreEqual(leftCommit.RootObject.Encode(),
                    rightCommit.RootObject.Encode());
                foreach (SigmaDurablePageUpdate update in canonical)
                {
                    Assert.That(left.TryGetPageRecord(update.Page.Coordinate,
                        out SigmaDurablePageRecord leftRecord), Is.True);
                    Assert.That(right.TryGetPageRecord(update.Page.Coordinate,
                        out SigmaDurablePageRecord rightRecord), Is.True);
                    CollectionAssert.AreEqual(leftRecord.Encode(),
                        rightRecord.Encode());
                }
            }
            finally
            {
                DeleteDirectory(leftPath);
                DeleteDirectory(rightPath);
            }
        }

        [Test]
        public void TenThousandNoChangeRevisitsCreateNoDurableGrowth()
        {
            string path = TemporaryDirectory();
            try
            {
                var store = new SigmaDurableStore(path);
                SigmaDurablePageUpdate update = DurableUpdate(
                    new SigmaCarrierPageCoordinate(4, 0), 1u, 23L);
                SigmaDurableCommitResult baseline = store.Commit(
                    new SigmaDurableCommitRequest(1UL, new[] { update }));
                long bytesBefore = DirectoryBytes(path);
                int filesBefore = Directory.GetFiles(path, "*",
                    SearchOption.AllDirectories).Length;

                for (int revisit = 0; revisit < 10000; ++revisit)
                {
                    SigmaDurableCommitResult result = store.Commit(
                        new SigmaDurableCommitRequest(
                            checked((ulong)revisit + 2UL),
                            Array.Empty<SigmaDurablePageUpdate>()));
                    Assert.That(result.RootObjectHash,
                        Is.EqualTo(baseline.RootObjectHash));
                    Assert.That(result.Statistics.PageBlobs, Is.Zero);
                    Assert.That(result.Statistics.PageMapNodes, Is.Zero);
                    Assert.That(result.Statistics.SupportMapNodes, Is.Zero);
                    Assert.That(result.Statistics.ImmutableObjects, Is.Zero);
                    Assert.That(result.Statistics.HeadSwapped, Is.False);
                }

                Assert.That(Directory.GetFiles(path, "*",
                    SearchOption.AllDirectories).Length, Is.EqualTo(filesBefore));
                Assert.That(DirectoryBytes(path), Is.EqualTo(bytesBefore));
                Assert.That(store.HeadHash, Is.EqualTo(baseline.RootObjectHash));
            }
            finally
            {
                DeleteDirectory(path);
            }
        }

        [Test]
        public void ExplicitEmptyHeadPublishesLastAndKeepsPinnedPriorRootValid()
        {
            string path = TemporaryDirectory();
            try
            {
                var store = new SigmaDurableStore(path);
                SigmaDurablePageUpdate update = DurableUpdate(
                    new SigmaCarrierPageCoordinate(8, 0), 1u, 29L);
                store.Commit(new SigmaDurableCommitRequest(1UL,
                    new[] { update }));
                using SigmaDurableRootLease prior = store.PinHead();

                SigmaDurableCommitResult empty = store.SelectEmpty(2UL);
                Assert.That(empty.RootObject.Revision, Is.EqualTo(2UL));
                Assert.That(empty.RootObject.SparsePageMapRootHash.IsZero,
                    Is.True);
                using (SigmaDurableRootLease selected = store.PinHead())
                    Assert.That(store.EnumeratePageRecords(selected), Is.Empty);
                Assert.That(store.HeadRecordIndexPageCount, Is.Zero);
                Assert.That(store.TryGetPageRecord(prior, update.Page.Coordinate,
                    out SigmaDurablePageRecord oldRecord), Is.True);
                Assert.That(oldRecord.Revision, Is.EqualTo(1u));

                var restarted = new SigmaDurableStore(path);
                Assert.That(restarted.Head.Revision, Is.EqualTo(2UL));
                Assert.That(restarted.TryGetPageRecord(update.Page.Coordinate,
                    out _), Is.False);
            }
            finally
            {
                DeleteDirectory(path);
            }
        }

        private static SigmaDurablePageRecord MakeRecord(
            SigmaCarrierPageCoordinate coordinate, uint generation)
        {
            var support = new SigmaQuerySupportReceipt(generation,
                SigmaQuerySupportFlags.Verified |
                    SigmaQuerySupportFlags.MayContribute,
                Hash((byte)(generation + 32u)));
            return new SigmaDurablePageRecord(coordinate, generation,
                generation, generation, generation, generation,
                Hash((byte)generation), support);
        }

        private static SigmaDurablePageUpdate DurableUpdate(
            SigmaCarrierPageCoordinate coordinate, uint revision,
            long stateValue)
        {
            SigmaDecodedPage page = MakeOneSamplePage(coordinate, revision,
                revision, revision, revision, stateValue);
            byte[] summary = SigmaQuerySupportSummary.FromPage(page,
                revision,
                SigmaQuerySupportFlags.Verified |
                    SigmaQuerySupportFlags.MayContribute).Encode();
            var receipt = new SigmaQuerySupportReceipt(revision,
                SigmaQuerySupportFlags.Verified |
                    SigmaQuerySupportFlags.MayContribute,
                SigmaDurableHash.Compute(summary));
            return new SigmaDurablePageUpdate(page, revision, receipt,
                SigmaCarrierCodec.EncodePage(page), summary);
        }

        private static SigmaDurablePageUpdate DurableGeometryUpdate(
            SigmaCarrierPageCoordinate coordinate, uint revision,
            params (long X, long Y, long Z)[] positions)
        {
            SigmaDecodedPage page = GeometryPage(coordinate, revision,
                positions);
            const SigmaQuerySupportFlags flags =
                SigmaQuerySupportFlags.Verified |
                SigmaQuerySupportFlags.MayContribute;
            byte[] summary = SigmaQuerySupportSummary.FromPage(page,
                revision, flags).Encode();
            return new SigmaDurablePageUpdate(page, revision,
                new SigmaQuerySupportReceipt(revision, flags,
                    SigmaDurableHash.Compute(summary)),
                SigmaCarrierCodec.EncodePage(page), summary);
        }

        private static SigmaDecodedPage GeometryPage(
            SigmaCarrierPageCoordinate coordinate, uint revision,
            params (long X, long Y, long Z)[] positions)
        {
            if (positions == null || positions.Length == 0)
                throw new ArgumentOutOfRangeException(nameof(positions));
            var samples = new SigmaS16[SigmaDecodedPage.SampleCount];
            var representation = new SigmaCarrierRepresentationRecord[
                positions.Length];
            for (int index = 0; index < positions.Length; ++index)
            {
                samples[index] = SigmaGeometryReadout.LiftFixture(
                    SigmaNumericDomain.One,
                    SigmaNumericDomain.FromInteger(positions[index].X),
                    SigmaNumericDomain.FromInteger(positions[index].Y),
                    SigmaNumericDomain.FromInteger(positions[index].Z));
                representation[index] = Representation(index, 0);
            }
            return new SigmaDecodedPage(coordinate, revision, revision, 0UL,
                (uint)positions.Length, revision, revision, 3u,
                (uint)positions.Length, representation, samples);
        }

        private static SigmaDecodedPage MakeOneSamplePage(
            SigmaCarrierPageCoordinate coordinate, uint stateGeneration,
            uint revision, uint gaugeGeneration,
            uint certificateGeneration, long stateValue)
        {
            var samples = new SigmaS16[SigmaDecodedPage.SampleCount];
            samples[0] = State(stateValue);
            return new SigmaDecodedPage(coordinate, stateGeneration, revision,
                0UL, 1u, gaugeGeneration, certificateGeneration, 3u, 1u,
                new[] { Representation(0, 0) }, samples);
        }

        private static SigmaDecodedPage MakeSmallRepresentedPage(
            SigmaCarrierPageCoordinate coordinate)
        {
            var samples = new SigmaS16[SigmaDecodedPage.SampleCount];
            var representation = new SigmaCarrierRepresentationRecord[3];
            for (int sample = 0; sample < representation.Length; ++sample)
            {
                samples[sample] = State(sample + 1L);
                representation[sample] = Representation(sample, sample);
            }
            return new SigmaDecodedPage(coordinate, 3u, 11u, 19UL, 3u,
                4u, 5u, 3u, 3u, representation, samples);
        }

        private static SigmaDecodedPage MakeFullMixedRepresentedPage()
        {
            SigmaS16[] samples = MixedSamples();
            var representation = new SigmaCarrierRepresentationRecord[
                SigmaDecodedPage.SampleCount];
            for (int sample = 0; sample < representation.Length; ++sample)
                representation[sample] = Representation(sample, sample % 3);
            return new SigmaDecodedPage(new SigmaCarrierPageCoordinate(-2, 5),
                7u, 13u, 123UL, SigmaDecodedPage.SampleCount, 8u, 9u, 3u,
                SigmaDecodedPage.SampleCount, representation, samples);
        }

        private static SigmaCarrierRepresentationRecord Representation(
            int sample, int level)
        {
            var words = new uint[SigmaCarrierRepresentationRecord.WordCount];
            words[0] = (uint)sample + 1u;
            words[4] = (uint)level;
            words[5] = (uint)(SigmaNativeGaugeCellFlags.Active |
                SigmaNativeGaugeCellFlags.Normalized);
            words[6] = Convert.ToUInt32(
                SigmaGeneratedFrame.ChiFingerprint.Substring(0, 8), 16);
            words[7] = Convert.ToUInt32(
                SigmaGeneratedFrame.KappaFingerprint.Substring(0, 8), 16);
            words[8] = (uint)(SigmaNativeCertificateFlags.Valid |
                SigmaNativeCertificateFlags.Minimized);
            words[9] = Convert.ToUInt32(
                SigmaGeneratedFrame.CertificateFingerprint.Substring(0, 8), 16);
            words[10] = 1u;
            return new SigmaCarrierRepresentationRecord(sample, words);
        }

        private static uint[] FlattenRepresentation(SigmaDecodedPage page)
        {
            SigmaCarrierRepresentationRecord[] records =
                page.CopyRepresentation();
            var words = new uint[checked(records.Length *
                SigmaCarrierRepresentationRecord.WordCount)];
            for (int index = 0; index < records.Length; ++index)
                Array.Copy(records[index].Words, 0, words,
                    index * SigmaCarrierRepresentationRecord.WordCount,
                    SigmaCarrierRepresentationRecord.WordCount);
            return words;
        }

        private static SigmaS16[] MixedSamples()
        {
            var page = new SigmaS16[SigmaDecodedPage.SampleCount];
            SigmaS16[][] blocks =
            {
                NullBlock(), ConstantBlock(), AffineBlock(), DeltaBlock(), RawBlock(),
            };
            for (int block = 0; block < SigmaDecodedPage.BlockCount; ++block)
                StoreBlock(page, block, blocks[block % blocks.Length]);
            return page;
        }

        private static SigmaEncodedBlock[] EncodeBlocksOnGpu(SigmaS16[] samples)
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaCarrierCodec");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("EncodePageBlocks");
            UInt2[] packed = Pack(samples);
            var descriptors = new UInt4[SigmaDecodedPage.BlockCount];
            var payloadWords = new uint[
                SigmaDecodedPage.BlockCount * SigmaCarrierCodec.RawBlockBytes /
                sizeof(uint)];
            using SigmaExactBackendGate gate = SigmaExactBackendGate.Dispatch();
            using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                packed.Length, Marshal.SizeOf<UInt2>());
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                packed.Length, Marshal.SizeOf<UInt2>());
            using var descriptorBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, descriptors.Length,
                Marshal.SizeOf<UInt4>());
            using var payload = new GraphicsBuffer(GraphicsBuffer.Target.Raw,
                payloadWords.Length, sizeof(uint));
            input.SetData(packed); payload.SetData(payloadWords);
            gate.Bind(shader, kernel);
            shader.SetInt("_InputPageSlot", 0);
            shader.SetInt("_OutputPageSlot", 0);
            shader.SetInt("_BlockCount", SigmaDecodedPage.BlockCount);
            shader.SetBuffer(kernel, "_DecodedInput", input);
            shader.SetBuffer(kernel, "_DecodedOutput", output);
            shader.SetBuffer(kernel, "_BlockDescriptors", descriptorBuffer);
            shader.SetBuffer(kernel, "_CodecPayload", payload);
            shader.Dispatch(kernel, SigmaDecodedPage.BlockCount, 1, 1);
            descriptorBuffer.GetData(descriptors);
            payload.GetData(payloadWords);

            byte[] payloadBytes = WordsToLittleEndian(payloadWords);
            var result = new SigmaEncodedBlock[SigmaDecodedPage.BlockCount];
            for (int block = 0; block < result.Length; ++block)
            {
                UInt4 descriptor = descriptors[block];
                Assert.That(descriptor.W & 1u, Is.EqualTo(1u));
                var bytes = new byte[checked((int)descriptor.Y)];
                Buffer.BlockCopy(payloadBytes, checked((int)descriptor.Z), bytes,
                    0, bytes.Length);
                result[block] = new SigmaEncodedBlock(
                    (SigmaBlockMode)descriptor.X, bytes);
            }
            return result;
        }

        private static SigmaS16[] NullBlock() =>
            new SigmaS16[SigmaDecodedPage.SamplesPerBlock];

        private static SigmaS16[] ConstantBlock()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            var lanes = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = lane % 2 == 0 ? long.MaxValue - lane :
                    long.MinValue + lane;
            SigmaS16 value = SigmaS16.FromArray(lanes);
            for (int index = 0; index < block.Length; ++index) block[index] = value;
            return block;
        }

        private static SigmaS16[] AffineBlock()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int v = 0; v < 8; ++v)
                for (int u = 0; u < 8; ++u)
                {
                    var lanes = new long[SigmaS16.LaneCount];
                    for (int lane = 0; lane < lanes.Length; ++lane)
                    {
                        long scale = lane + 1L;
                        lanes[lane] = checked(scale * 100L * SigmaNumericDomain.One +
                            u * scale * 10L * SigmaNumericDomain.One -
                            v * scale * 5L * SigmaNumericDomain.One);
                    }
                    block[v * 8 + u] = SigmaS16.FromArray(lanes);
                }
            return block;
        }

        private static SigmaS16[] DeltaBlock()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int index = 0; index < block.Length; ++index)
                block[index] = State((index * index + 3 * index + 1) % 19 - 9);
            return block;
        }

        private static SigmaS16[] RawBlock()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            ulong value = 0x9e3779b97f4a7c15UL;
            for (int sample = 0; sample < block.Length; ++sample)
            {
                var lanes = new long[SigmaS16.LaneCount];
                for (int lane = 0; lane < lanes.Length; ++lane)
                {
                    value ^= value << 7; value ^= value >> 9; value ^= value << 8;
                    lanes[lane] = unchecked((long)value);
                }
                block[sample] = SigmaS16.FromArray(lanes);
            }
            return block;
        }

        private static SigmaS16 State(long value)
        {
            var lanes = new long[SigmaS16.LaneCount];
            lanes[0] = value;
            return SigmaS16.FromArray(lanes);
        }

        private static void StoreBlock(SigmaS16[] page, int blockIndex,
            SigmaS16[] block)
        {
            int blockX = blockIndex & 7;
            int blockY = blockIndex >> 3;
            for (int v = 0; v < 8; ++v)
                for (int u = 0; u < 8; ++u)
                    page[(blockY * 8 + v) * 64 + blockX * 8 + u] =
                        block[v * 8 + u];
        }

        private static UInt2[] Pack(SigmaS16[] samples)
        {
            var result = new UInt2[samples.Length * SigmaS16.LaneCount];
            for (int sample = 0; sample < samples.Length; ++sample)
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    long value = samples[sample][lane];
                    result[sample * SigmaS16.LaneCount + lane] = new UInt2
                    {
                        X = unchecked((uint)value),
                        Y = unchecked((uint)(value >> 32)),
                    };
                }
            return result;
        }

        private static byte[] WordsToLittleEndian(uint[] words)
        {
            var bytes = new byte[words.Length * sizeof(uint)];
            for (int index = 0; index < words.Length; ++index)
            {
                uint value = words[index]; int offset = index * sizeof(uint);
                bytes[offset] = (byte)value;
                bytes[offset + 1] = (byte)(value >> 8);
                bytes[offset + 2] = (byte)(value >> 16);
                bytes[offset + 3] = (byte)(value >> 24);
            }
            return bytes;
        }

        private static SigmaDurableHash Hash(byte seed)
        {
            var bytes = new byte[SigmaDurableHash.ByteCount];
            for (int index = 0; index < bytes.Length; ++index)
                bytes[index] = (byte)(seed + index);
            return SigmaDurableHash.FromBytes(bytes);
        }

        private static long DirectoryBytes(string path)
        {
            long total = 0L;
            foreach (string file in Directory.GetFiles(path, "*",
                SearchOption.AllDirectories))
                total = checked(total + new FileInfo(file).Length);
            return total;
        }

        private static string TemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "sigma-n5r-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }

        private sealed class MemoryObjectStore : ISigmaDurableObjectStore
        {
            private readonly Dictionary<SigmaDurableHash, byte[]> _objects = new();
            private readonly Func<byte[], SigmaDurableHash> _hash;

            internal MemoryObjectStore(Func<byte[], SigmaDurableHash> hash = null)
            {
                _hash = hash ?? SigmaDurableHash.Compute;
            }

            internal int CreatedCount { get; private set; }
            internal int ReadCount { get; private set; }
            internal void ResetReadCount() => ReadCount = 0;

            public bool TryRead(SigmaDurableHash hash, out byte[] bytes)
            {
                ++ReadCount;
                if (_objects.TryGetValue(hash, out byte[] stored))
                {
                    if (_hash(stored) != hash)
                        throw new InvalidDataException(
                            "In-memory immutable object failed content-address validation.");
                    bytes = (byte[])stored.Clone();
                    return true;
                }
                bytes = null;
                return false;
            }

            public SigmaDurableHash Put(byte[] bytes,
                SigmaDurableObjectKind kind, out bool created)
            {
                SigmaDurableHash hash = _hash(bytes);
                if (_objects.TryGetValue(hash, out byte[] existing))
                {
                    if (!Equal(existing, bytes))
                        throw new InvalidDataException(
                            "Immutable object hash collision with unequal bytes.");
                    created = false;
                    return hash;
                }
                _objects.Add(hash, (byte[])bytes.Clone());
                ++CreatedCount;
                created = true;
                return hash;
            }

            private static bool Equal(byte[] left, byte[] right)
            {
                if (left.Length != right.Length) return false;
                for (int index = 0; index < left.Length; ++index)
                    if (left[index] != right[index]) return false;
                return true;
            }
        }
    }
}
