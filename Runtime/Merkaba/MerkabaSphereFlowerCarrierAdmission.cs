using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        /// <summary>
        /// Signs of all directed radical planes incident to one loop node.
        /// Bits address the existing 26 Node entries, not new plane IDs.
        /// Incident bits outside Positive/Negative are identically zero on
        /// this loop. This is provenance, not a petal-admission decision.
        /// </summary>
        public readonly struct RadicalSectorPattern
        {
            public readonly uint Incident;
            public readonly uint Positive;
            public readonly uint Negative;

            internal RadicalSectorPattern(uint incident, uint positive,
                uint negative)
            {
                Incident = incident;
                Positive = positive;
                Negative = negative;
            }
        }

#if UNITY_EDITOR
        // Code generation only. Runtime classification reads the emitted
        // finite table after the root interval is CERTAIN in a strict sector.
        // No side of a flag is inferred from its sector ordinal here.
        public static RadicalSectorPattern[] BuildRadicalSectorPatterns()
        {
            var result = new RadicalSectorPattern[2 * SectorBoundariesValue.Length];
            var assigned = new bool[result.Length];
            for (int nodeIndex = 0; nodeIndex < NodesValue.Length; nodeIndex++)
            {
                NodeRule node = NodesValue[nodeIndex];
                LineRule line = LinesValue[node.LineClass];
                int3 representative = CanonicalLinesValue[node.LineClass];
                List<ExactBoundaryPoint> boundaries = BuildLineBoundaryPoints(
                    PetalsValue, NodesValue, representative);
                if (boundaries.Count != line.SectorCount)
                    throw new InvalidOperationException(
                        "Radical provenance differs from the generated sector partition.");

                uint incident = 0u;
                ulong petals = NodeIncidentPetalsValue[nodeIndex];
                for (int petal = 0; petal < PetalsValue.Length; petal++)
                {
                    if ((petals & (1UL << petal)) == 0u) continue;
                    for (int site = 0; site < 3; site++)
                        incident |= 1u << PetalsValue[petal].Node(site);
                }

                // The opposite endpoint has the same canonical loop basis,
                // but its directed radical plane is relative to its own
                // centre. Translate that equation before comparing cuts.
                for (int planeNode = 0; planeNode < NodesValue.Length; planeNode++)
                {
                    if ((incident & (1u << planeNode)) == 0u) continue;
                    int3 q = NodesValue[planeNode].Direction;
                    Rational translatedOffset = new(Dot(q, q) +
                        Dot(q, representative - node.Direction), 2);
                    var cuts = new List<ExactBoundaryPoint>(2);
                    BuildBoundaryPoints(representative, q, translatedOffset, cuts);
                    foreach (ExactBoundaryPoint cut in cuts)
                    {
                        bool represented = false;
                        foreach (ExactBoundaryPoint boundary in boundaries)
                            represented |= ExactBoundaryPoint.SquaredDistanceSign(
                                cut, boundary) == 0;
                        if (!represented)
                            throw new InvalidOperationException(
                                $"Directed radical plane {planeNode} crosses the interior " +
                                $"of a generated sector at loop node {nodeIndex}.");
                    }
                }

                for (int sector = 0; sector < boundaries.Count; sector++)
                {
                    ExactBoundaryPoint start = boundaries[sector];
                    uint positive = 0u, negative = 0u;
                    for (int planeNode = 0; planeNode < NodesValue.Length; planeNode++)
                    {
                        uint bit = 1u << planeNode;
                        if ((incident & bit) == 0u) continue;
                        int3 q = NodesValue[planeNode].Direction;
                        Rational alpha = new(Dot(q, node.Direction) - Dot(q, q), 2);
                        LinearSurd radial = start.RadialDot(representative, q);
                        int sign = new LinearSurd(radial.A + alpha,
                            radial.B, radial.Radicand).Sign;
                        if (sign == 0)
                        {
                            // dX/dtheta = r x (X-r/2) / |r|. The positive
                            // denominator cannot change the exact sign.
                            sign = start.RadialDot(representative,
                                Cross(q, representative)).Sign;
                            // At a tangent cut P=P'=0, P''=alpha. If it also
                            // vanishes, this radical equation is identically
                            // zero on the loop (for example q=d itself).
                            if (sign == 0) sign = alpha.Sign;
                        }
                        if (sign > 0) positive |= bit;
                        else if (sign < 0) negative |= bit;
                    }

                    int index = 2 * (line.SectorOffset + sector) +
                        (node.Orientation < 0 ? 1 : 0);
                    if (assigned[index] || (positive & negative) != 0u ||
                        ((positive | negative) & ~incident) != 0u)
                        throw new InvalidOperationException(
                            "Directed radical sector provenance is not unique.");
                    result[index] = new RadicalSectorPattern(incident, positive, negative);
                    assigned[index] = true;
                }
            }
            foreach (bool present in assigned)
                if (!present) throw new InvalidOperationException(
                    "A directed loop has no generated radical provenance.");
            return result;
        }
#endif
    }
}
