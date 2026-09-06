namespace Genesis.RoomScan
{
    /// <summary>
    /// Frozen public shape of the REV-B Sphere-Flower authority. CUT 1 fills the
    /// exact oracle and generated tables; CUT 0 intentionally has no production
    /// call site and changes no scanner, readout, storage or export behavior.
    /// </summary>
    public static class MerkabaSphereFlowerAuthority
    {
        public const float LatticeStep = MerkabaConstants.LatticeStep;
        public const int LevelCount = 6;
        public const int DirectedRelationCount = 26;
        public const int LineClassCount = 13;
        public const int NodeClassCount = 26;
        public const int StrandClassCount = 72;
        public const int PetalClassCount = 48;
        public const int MaximumSectorCount = 32;

        public enum Shell : byte
        {
            R1Core = 1,
            R2Shape = 2,
            R3Closure = 3
        }

        public static float LevelStep(int level)
        {
            if ((uint)level >= LevelCount)
                throw new System.ArgumentOutOfRangeException(nameof(level));
            return LatticeStep / (1 << level);
        }
    }
}
