using Genesis.RoomScan;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSphereFlowerBootstrapTests
    {
        [Test]
        public void FrozenSchema_MatchesRevCCountsAndExistingLattice()
        {
            Assert.That(MerkabaSphereFlowerAuthority.LatticeStep,
                Is.EqualTo(MerkabaConstants.LatticeStep));
            Assert.That(MerkabaSphereFlowerAuthority.LevelCount, Is.EqualTo(6));
            Assert.That(MerkabaSphereFlowerAuthority.GeometryLevelCount,
                Is.EqualTo(3));
            Assert.That(MerkabaSphereFlowerAuthority.DirectedRelationCount,
                Is.EqualTo(26));
            Assert.That(MerkabaSphereFlowerAuthority.LineClassCount,
                Is.EqualTo(13));
            Assert.That(MerkabaSphereFlowerAuthority.NodeClassCount,
                Is.EqualTo(26));
            Assert.That(MerkabaSphereFlowerAuthority.StrandClassCount,
                Is.EqualTo(72));
            Assert.That(MerkabaSphereFlowerAuthority.PetalClassCount,
                Is.EqualTo(48));
            Assert.That(MerkabaSphereFlowerAuthority.MaximumSectorCount,
                Is.EqualTo(32));
        }

        [Test]
        public void LevelStep_IsExactDyadicPrefix()
        {
            for (int level = 0;
                 level < MerkabaSphereFlowerAuthority.LevelCount; level++)
            {
                float expected = MerkabaConstants.LatticeStep / (1 << level);
                Assert.That(MerkabaSphereFlowerAuthority.LevelStep(level),
                    Is.EqualTo(expected));
            }
        }

        [Test]
        public void LevelStep_RejectsValuesOutsideL0ThroughL5()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                MerkabaSphereFlowerAuthority.LevelStep(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                MerkabaSphereFlowerAuthority.LevelStep(6));
        }

        [Test]
        public void WorldLoopEvaluation_StopsAtTerminalL2Carrier()
        {
            var junction = new MerkabaSphereFlowerAuthority.Long3(1, 0, 0);
            for (int level = 0;
                 level < MerkabaSphereFlowerAuthority.GeometryLevelCount; level++)
                Assert.DoesNotThrow(() =>
                    MerkabaSphereFlowerAuthority.EvaluateLoop(level,
                        junction, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                MerkabaSphereFlowerAuthority.EvaluateLoop(3, junction, 0));
        }
    }
}
