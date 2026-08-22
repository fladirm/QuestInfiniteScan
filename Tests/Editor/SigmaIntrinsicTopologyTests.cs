using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaIntrinsicTopologyTests
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
        public void GenerationPairCacheComputesOneTransitionAndInvalidatesEitherEnd()
        {
            SigmaS16 center = Scalar(1);
            SigmaS16 neighbour = Scalar(2);
            var cache = new SigmaTransitionCache(8);
            var firstKey = new SigmaTransitionKey(10UL, 3u, 11UL, 7u);
            SigmaTransitionSignature first = cache.GetOrCompute(firstKey,
                center, neighbour);
            SigmaTransitionSignature repeated = cache.GetOrCompute(firstKey,
                center, neighbour);
            SigmaTransitionSignature changed = cache.GetOrCompute(
                new SigmaTransitionKey(10UL, 3u, 11UL, 8u), center, neighbour);

            Assert.That(first.Transition, Is.EqualTo(repeated.Transition));
            Assert.That(first.AnnihilatorId, Is.EqualTo(repeated.AnnihilatorId));
            Assert.That(first.AnnihilatorError,
                Is.EqualTo(repeated.AnnihilatorError));
            Assert.That(cache.HitCount, Is.EqualTo(1UL));
            Assert.That(cache.MissCount, Is.EqualTo(2UL));
            Assert.That(changed.Transition, Is.EqualTo(first.Transition));
        }

        [Test]
        public void StableIndependentEvidencePromotesExactSingularity()
        {
            SigmaS16 center = Scalar(1);
            SigmaS16 singular = FindContactZeroDivisor();

            SigmaIntrinsicTopologySignature repeatedSameView =
                SigmaIntrinsicTopology.EvaluateCell(center, singular, center,
                    41u, 41u, true);
            SigmaIntrinsicTopologySignature independentViews =
                SigmaIntrinsicTopology.EvaluateCell(center, singular, center,
                    41u, 97u, true);
            SigmaIntrinsicTopologySignature exactWithoutResidual =
                SigmaIntrinsicTopology.EvaluateCell(center, singular, center,
                    41u, 97u, false);
            SigmaIntrinsicTopologySignature laterIndependentViews =
                SigmaIntrinsicTopology.EvaluateCell(center, singular, center,
                    113u, 211u, true);

            Assert.That(repeatedSameView.Classification,
                Is.EqualTo(SigmaTopologyClass.Unresolved));
            Assert.That(independentViews.Classification,
                Is.EqualTo(SigmaTopologyClass.Singular));
            Assert.That(independentViews.ExactAnnihilator, Is.True);
            Assert.That(independentViews.AnnihilatorError, Is.EqualTo(BigInteger.Zero));
            Assert.That(exactWithoutResidual.Classification,
                Is.EqualTo(SigmaTopologyClass.Unresolved),
                "an algebraic zero divisor alone cannot invent a physical discontinuity");
            Assert.That(laterIndependentViews.AnnihilatorId,
                Is.EqualTo(independentViews.AnnihilatorId));
            Assert.That(laterIndependentViews.Classification,
                Is.EqualTo(SigmaTopologyClass.Singular));
        }

        [Test]
        public void SyntheticH0SignaturesAreIntrinsicAndProximityIndependent()
        {
            SigmaIntrinsicTopologySignature wall =
                SigmaIntrinsicTopology.EvaluateCell(Scalar(1), Scalar(2),
                    Scalar(3), 5u, 9u, false);
            Assert.That(wall.Classification, Is.EqualTo(SigmaTopologyClass.Regular),
                "a supported scalar wall continuation must stay regular");

            SigmaS16 creaseState = FindContactZeroDivisor();
            SigmaIntrinsicTopologySignature crease =
                SigmaIntrinsicTopology.EvaluateCell(Scalar(1), creaseState,
                    Scalar(1), 5u, 9u, true);
            Assert.That(crease.Classification,
                Is.EqualTo(SigmaTopologyClass.Singular));

            SigmaIntrinsicTopologySignature doorway =
                SigmaIntrinsicTopology.EvaluateCell(Scalar(1),
                    SigmaS16Operators.NullState, Scalar(1), 5u, 9u, true);
            Assert.That(doorway.ContactNull, Is.True);
            Assert.That(doorway.Classification,
                Is.Not.EqualTo(SigmaTopologyClass.Regular));

            long mass = SigmaNumericDomain.FromInteger(8);
            SigmaS16 pipeCenter = SigmaGeometryReadout.LiftFixture(mass,
                SigmaNumericDomain.Quantize(0.100),
                SigmaNumericDomain.Quantize(0.000),
                SigmaNumericDomain.Quantize(0.700));
            SigmaS16 pipeRight = SigmaGeometryReadout.LiftFixture(mass,
                SigmaNumericDomain.Quantize(0.099),
                SigmaNumericDomain.Quantize(0.014),
                SigmaNumericDomain.Quantize(0.700));
            SigmaS16 pipeDown = SigmaGeometryReadout.LiftFixture(mass,
                SigmaNumericDomain.Quantize(0.100),
                SigmaNumericDomain.Quantize(0.000),
                SigmaNumericDomain.Quantize(0.714));
            SigmaIntrinsicTopologySignature pipe =
                SigmaIntrinsicTopology.EvaluateCell(pipeCenter, pipeRight,
                    pipeDown, 5u, 9u, false);
            Assert.That(pipe.Classification, Is.EqualTo(SigmaTopologyClass.Regular),
                "a locally smooth curved pipe immersion must remain regular");

            SigmaS16 plateFront = SigmaGeometryReadout.LiftFixture(mass,
                SigmaNumericDomain.Quantize(0.0),
                SigmaNumericDomain.Quantize(0.0),
                SigmaNumericDomain.Quantize(0.500));
            SigmaS16 plateBack = SigmaGeometryReadout.LiftFixture(mass,
                SigmaNumericDomain.Quantize(0.0),
                SigmaNumericDomain.Quantize(0.0),
                SigmaNumericDomain.Quantize(0.505));
            SigmaIntrinsicTopologySignature front =
                SigmaIntrinsicTopology.EvaluateCell(plateFront, plateFront,
                    plateFront, 5u, 9u, false);
            SigmaIntrinsicTopologySignature back =
                SigmaIntrinsicTopology.EvaluateCell(plateBack, plateBack,
                    plateBack, 5u, 9u, false);
            Assert.That(front.Classification, Is.EqualTo(SigmaTopologyClass.Regular));
            Assert.That(back.Classification, Is.EqualTo(SigmaTopologyClass.Regular));
            Assert.That(plateFront, Is.Not.EqualTo(plateBack),
                "5 mm-separated sides remain different carrier states; no 3D merge gate exists");
        }

        [Test]
        public void VulkanFixtureMatchesCpuFullCatalogAndIntegerGates()
        {
            string[] fixtureGuids = AssetDatabase.FindAssets(
                "SigmaTopologyFixture t:ComputeShader");
            Assert.That(fixtureGuids, Has.Length.EqualTo(1));
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                AssetDatabase.GUIDToAssetPath(fixtureGuids[0]));
            Assert.That(shader, Is.Not.Null);
            int buildKernel = shader.FindKernel("BuildFixtureTransition");
            int scanKernel = shader.FindKernel("ScanFixtureCatalog");
            int finalizeKernel = shader.FindKernel("FinalizeFixture");
            SigmaS16 center = Scalar(1);
            SigmaS16 right = FindContactZeroDivisor();
            SigmaS16 down = center;
            SigmaIntrinsicTopologySignature expected =
                SigmaIntrinsicTopology.EvaluateCell(center, right, down,
                    17u, 29u, true);

            UInt2[] packed = Pack(center, right, down);
            var result = new UInt4[6];
            using var states = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                packed.Length, Marshal.SizeOf<UInt2>());
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                result.Length, Marshal.SizeOf<UInt4>());
            using var transition = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, SigmaS16.LaneCount,
                Marshal.SizeOf<UInt2>());
            states.SetData(packed);
            output.SetData(result);
            shader.SetInt("_SingularShift",
                SigmaIntrinsicTopology.DefaultSingularShift);
            shader.SetInt("_AssociatorShift",
                SigmaIntrinsicTopology.DefaultAssociatorShift);
            shader.SetInt("_FixtureDiscontinuity", 1);
            shader.SetInt("_FixtureLeftKey", 17);
            shader.SetInt("_FixtureRightKey", 29);
            shader.SetBuffer(buildKernel, "_FixtureStates", states);
            shader.SetBuffer(buildKernel, "_FixtureTau", transition);
            shader.SetBuffer(buildKernel, "_FixtureResult", output);
            shader.SetBuffer(scanKernel, "_FixtureTau", transition);
            shader.SetBuffer(scanKernel, "_FixtureResult", output);
            shader.SetBuffer(finalizeKernel, "_FixtureStates", states);
            shader.SetBuffer(finalizeKernel, "_FixtureResult", output);
            shader.Dispatch(buildKernel, 1, 1, 1);
            shader.Dispatch(scanKernel, 1, 1, 1);
            shader.Dispatch(finalizeKernel, 1, 1, 1);
            output.GetData(result);

            Assert.That(result[0].X & 3u,
                Is.EqualTo((uint)expected.Classification));
            Assert.That((result[0].X >> 8) & 0xffu,
                Is.EqualTo((uint)expected.AnnihilatorId));
            Assert.That(ToBigInteger(result[2]),
                Is.EqualTo(expected.AnnihilatorError));
            Assert.That(ToBigInteger(result[3]),
                Is.EqualTo(expected.TransitionScale));
            Assert.That(ToBigInteger(result[4]),
                Is.EqualTo(expected.AssociatorError));
            Assert.That(ToBigInteger(result[5]),
                Is.EqualTo(expected.AssociatorScale));
            Assert.That(result[0].Y, Is.EqualTo(17u));
            Assert.That(result[0].Z, Is.EqualTo(29u));
        }

        private static SigmaS16 FindContactZeroDivisor()
        {
            for (int index = 0; index < 1344; ++index)
            {
                SigmaZeroDivisorEntry entry =
                    SigmaS16Operators.GetZeroDivisorEntry(index);
                SigmaS16 witness = entry.Witness.ToS16();
                if (!SigmaGeometryReadout.TryRead(witness, out _))
                    continue;
                Assert.That(SigmaS16Operators.RightSignedDyadAction(witness,
                    entry.Annihilator).IsZero, Is.True);
                return witness;
            }
            throw new InvalidOperationException(
                "Generated catalog has no contact-readable zero divisor fixture.");
        }

        private static SigmaS16 Scalar(int value) => SigmaS16.Basis(0,
            SigmaNumericDomain.FromInteger(value));

        private static UInt2[] Pack(params SigmaS16[] states)
        {
            var packed = new UInt2[states.Length * SigmaS16.LaneCount];
            for (int state = 0; state < states.Length; ++state)
            {
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    long raw = states[state][lane];
                    packed[state * SigmaS16.LaneCount + lane] = new UInt2
                    {
                        X = unchecked((uint)raw),
                        Y = unchecked((uint)(raw >> 32))
                    };
                }
            }
            return packed;
        }

        private static BigInteger ToBigInteger(UInt4 value) =>
            new BigInteger(value.X) +
            (new BigInteger(value.Y) << 32) +
            (new BigInteger(value.Z) << 64) +
            (new BigInteger(value.W) << 96);
    }
}
