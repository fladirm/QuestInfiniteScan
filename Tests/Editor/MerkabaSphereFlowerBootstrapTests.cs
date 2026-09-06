using Genesis.RoomScan;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSphereFlowerBootstrapTests
    {
        [Test]
        public void FrozenSchema_MatchesRevBCountsAndExistingLattice()
        {
            Assert.That(MerkabaSphereFlowerAuthority.LatticeStep,
                Is.EqualTo(MerkabaConstants.LatticeStep));
            Assert.That(MerkabaSphereFlowerAuthority.LevelCount, Is.EqualTo(6));
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
    }
}
