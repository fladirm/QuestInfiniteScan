using System;
using System.Collections.Generic;
using System.Numerics;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// The single CPU oracle for the REV-C M8 Dual Sphere-Flower algebra.
    /// Its finite tables are emitted verbatim to HLSL by the editor codegen.
    /// No method in this type searches world geometry or owns persistent state.
    /// </summary>
    public static partial class MerkabaSphereFlowerAuthority
    {
        public const float LatticeStep = MerkabaConstants.LatticeStep;
        public const int LevelCount = 6;
        public const int GeometryLevelCount = 3;
        public const int DirectedRelationCount = 26;
        public const int LineClassCount = 13;
        public const int NodeClassCount = 26;
        public const int StrandClassCount = 72;
        public const int PetalClassCount = 48;
        public const int MaximumSectorCount = 32;
        public const int ChildPetalCount = 4;
        public const int ChildPhaseParentContextCount = 5;
        public const int ChildPhaseKnotSiteCount = 6;
        public const int PhaseFamilyCount = StrandClassCount;
        public const int ChildPhaseEdgeCount = ChildPetalCount * 3;
        public const int TetraFrameCount = 8;

        private const double FloatUnitRoundoff = 1.0 / 16777216.0;

        public enum Shell : byte
        {
            R1Core = 1,
            R2Shape = 2,
            R3Closure = 3
        }

        public enum RootClassification : byte
        {
            Impossible = 0,
            CertainTangent = 1,
            CertainSecant = 2,
            CoplanarLoop = 3,
            Ambiguous = 4
        }

        public enum ProofClassification : byte
        {
            Impossible = 0,
            Certain = 1,
            Ambiguous = 2
        }

        internal enum PhaseResidualClassification : byte
        {
            Impossible = 0,
            ExactZero = 1,
            CertainNonzero = 2,
            Ambiguous = 3,
            Promote = 4
        }

        /// <summary>Transient certain-root claim, already transported to the
        /// canonical loop orientation. It is not a persisted branch identity.</summary>
        internal readonly struct PhaseRootEvidence
        {
            internal readonly MerkabaFlowerSymbolKey Symbol;
            internal readonly Interval2 Root;
            internal readonly ProofClassification Classification;

            internal PhaseRootEvidence(MerkabaFlowerSymbolKey symbol,
                Interval2 root, ProofClassification classification)
            {
                Symbol = symbol;
                Root = root;
                Classification = classification;
            }
        }

        internal readonly struct PhaseResidualResult
        {
            internal readonly PhaseResidualClassification Classification;
            internal readonly FloatInterval Residual;
            internal readonly Interval2 Synthesis;
            internal readonly int Lower;
            internal readonly int Upper;

            internal PhaseResidualResult(PhaseResidualClassification classification,
                FloatInterval residual = default, Interval2 synthesis = default,
                int lower = 0, int upper = 0)
            {
                Classification = classification;
                Residual = residual;
                Synthesis = synthesis;
                Lower = lower;
                Upper = upper;
            }
        }

        /// <summary>One already committed R2 innovation selected by generated
        /// ancestry incidence. It contains no V and no fitted plane.</summary>
        internal readonly struct PhaseTransportTerm
        {
            internal readonly int Lower;
            internal readonly int Upper;
            internal readonly sbyte Orientation;
            internal readonly byte AncestorLevel;

            internal PhaseTransportTerm(int lower, int upper,
                sbyte orientation, byte ancestorLevel)
            {
                Lower = lower;
                Upper = upper;
                Orientation = orientation;
                AncestorLevel = ancestorLevel;
            }
        }

        public readonly struct FloatInterval : IEquatable<FloatInterval>
        {
            public readonly float Lower;
            public readonly float Upper;

            public FloatInterval(float lower, float upper)
            {
                if (float.IsNaN(lower) || float.IsNaN(upper) || lower > upper)
                    throw new ArgumentOutOfRangeException(nameof(lower));
                Lower = lower;
                Upper = upper;
            }

            public bool IsSingleton => Lower == Upper;
            public bool ContainsZero => Lower <= 0f && Upper >= 0f;
            public float Midpoint => OrderedAdd(Lower,
                OrderedMultiply(OrderedSubtract(Upper, Lower), 0.5f));

            public static FloatInterval Singleton(float value) =>
                new(value, value);

            public static FloatInterval Enclose(double value)
            {
                if (double.IsNaN(value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                float rounded = (float)value;
                if (float.IsNegativeInfinity(rounded) ||
                    float.IsPositiveInfinity(rounded))
                    return new FloatInterval(rounded, rounded);
                float lower = (double)rounded <= value
                    ? rounded : PreviousFloat(rounded);
                float upper = (double)rounded >= value
                    ? rounded : NextFloat(rounded);
                return new FloatInterval(lower, upper);
            }

            public static FloatInterval FromCenterRadius(float center,
                double radius)
            {
                if (!(radius >= 0.0) || double.IsInfinity(radius))
                    throw new ArgumentOutOfRangeException(nameof(radius));
                FloatInterval lower = Enclose((double)center - radius);
                FloatInterval upper = Enclose((double)center + radius);
                return new FloatInterval(lower.Lower, upper.Upper);
            }

            // These are the production binary32 operations emitted as
            // M8FlowerI*. Enclose remains the independent exact-to-float
            // conversion used when generating algebraic constants.
            private bool IsExactZero =>
                ((math.asuint(Lower) | math.asuint(Upper)) & 0x7fffffffu) == 0u;

            public static FloatInterval Add(FloatInterval left,
                FloatInterval right)
            {
                if (left.IsExactZero) return right;
                if (right.IsExactZero) return left;
                return new FloatInterval(PreviousFloat(OrderedAdd(left.Lower, right.Lower)),
                    NextFloat(OrderedAdd(left.Upper, right.Upper)));
            }

            public static FloatInterval Subtract(FloatInterval left,
                FloatInterval right)
            {
                if (right.IsExactZero) return left;
                if (left.IsExactZero) return new FloatInterval(-right.Upper, -right.Lower);
                return new FloatInterval(PreviousFloat(OrderedSubtract(left.Lower, right.Upper)),
                    NextFloat(OrderedSubtract(left.Upper, right.Lower)));
            }

            public static FloatInterval Multiply(FloatInterval left,
                FloatInterval right)
            {
                if ((left.IsExactZero && float.IsFinite(right.Lower) && float.IsFinite(right.Upper)) ||
                    (right.IsExactZero && float.IsFinite(left.Lower) && float.IsFinite(left.Upper)))
                    return Singleton(0f);
                float p0 = OrderedMultiply(left.Lower, right.Lower);
                float p1 = OrderedMultiply(left.Lower, right.Upper);
                float p2 = OrderedMultiply(left.Upper, right.Lower);
                float p3 = OrderedMultiply(left.Upper, right.Upper);
                float minimum = Math.Min(Math.Min(p0, p1), Math.Min(p2, p3));
                float maximum = Math.Max(Math.Max(p0, p1), Math.Max(p2, p3));
                return new FloatInterval(PreviousFloat(minimum), NextFloat(maximum));
            }

            public static FloatInterval Divide(FloatInterval numerator,
                FloatInterval denominator)
            {
                if (denominator.ContainsZero)
                    throw new DivideByZeroException(
                        "An interval divisor may not contain zero.");
                if (denominator.Upper < 0f)
                {
                    numerator = new FloatInterval(-numerator.Upper, -numerator.Lower);
                    denominator = new FloatInterval(-denominator.Upper, -denominator.Lower);
                }
                if (!TryDividePositive(numerator, denominator, out FloatInterval result))
                    throw new ArgumentOutOfRangeException(nameof(denominator),
                        "A production interval quotient must have finite certified endpoints.");
                return result;
            }

            internal static bool TryDividePositive(FloatInterval numerator,
                FloatInterval denominator, out FloatInterval result)
            {
                result = default;
                if (!(denominator.Lower > 0f) || !float.IsFinite(denominator.Upper) ||
                    !float.IsFinite(numerator.Lower) || !float.IsFinite(numerator.Upper)) return false;
                if (!TryDivideEnclosed(numerator.Lower, numerator.Lower < 0f
                        ? denominator.Lower : denominator.Upper, out FloatInterval lower) ||
                    !TryDivideEnclosed(numerator.Upper, numerator.Upper < 0f
                        ? denominator.Upper : denominator.Lower, out FloatInterval upper)) return false;
                result = new FloatInterval(lower.Lower, upper.Upper);
                return true;
            }

            public static FloatInterval Square(FloatInterval value)
            {
                if (value.IsExactZero) return Singleton(0f);
                if (value.ContainsZero)
                {
                    float maximum = Math.Max(OrderedMultiply(value.Lower, value.Lower),
                        OrderedMultiply(value.Upper, value.Upper));
                    return new FloatInterval(0f, NextFloat(maximum));
                }
                return Multiply(value, value);
            }

            public static FloatInterval Sqrt(FloatInterval value)
            {
                if (!TrySqrt(value, out FloatInterval result))
                    throw new ArgumentOutOfRangeException(nameof(value));
                return result;
            }

            internal static bool TrySqrt(FloatInterval value, out FloatInterval result)
            {
                result = default;
                if (!(value.Lower >= 0f) || !float.IsFinite(value.Upper)) return false;
                float lower = M8FlowerRoundSqrt(value.Lower);
                float upper = M8FlowerRoundSqrt(value.Upper);
                float lo = Math.Max(0f, PreviousFloat(lower));
                float hi = NextFloat(upper);
                // A product of two binary32 values has at most 48 significant
                // bits and lies within binary64's exponent range. These exact
                // products implement the shader's dyadic certificate, not an
                // epsilon or an assumed accuracy of the sqrt hint.
                if (!float.IsFinite(hi) || (double)lo * lo > value.Lower ||
                    (double)hi * hi < value.Upper) return false;
                result = new FloatInterval(lo, hi);
                return true;
            }

            public static FloatInterval Abs(FloatInterval value)
            {
                if (value.Lower >= 0f) return value;
                if (value.Upper <= 0f)
                    return new FloatInterval(-value.Upper, -value.Lower);
                return new FloatInterval(0f,
                    Math.Max(-value.Lower, value.Upper));
            }

            public static bool TryIntersect(FloatInterval left,
                FloatInterval right, out FloatInterval intersection)
            {
                float lower = Math.Max(left.Lower, right.Lower);
                float upper = Math.Min(left.Upper, right.Upper);
                if (lower > upper)
                {
                    intersection = default;
                    return false;
                }
                intersection = new FloatInterval(lower, upper);
                return true;
            }

            public bool Equals(FloatInterval other) =>
                Lower.Equals(other.Lower) && Upper.Equals(other.Upper);

            public override bool Equals(object obj) =>
                obj is FloatInterval other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Lower, Upper);

            public override string ToString() => $"[{Lower:R}, {Upper:R}]";
        }

        public readonly struct Long3 : IEquatable<Long3>
        {
            public readonly long X;
            public readonly long Y;
            public readonly long Z;

            public Long3(long x, long y, long z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(Long3 other) =>
                X == other.X && Y == other.Y && Z == other.Z;

            public override bool Equals(object obj) =>
                obj is Long3 other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }

        public readonly struct Interval2
        {
            public readonly FloatInterval X;
            public readonly FloatInterval Y;

            public Interval2(FloatInterval x, FloatInterval y)
            {
                X = x;
                Y = y;
            }

            public static Interval2 Singleton(float2 value) => new(
                FloatInterval.Singleton(value.x),
                FloatInterval.Singleton(value.y));
        }

        public readonly struct Interval3
        {
            public readonly FloatInterval X;
            public readonly FloatInterval Y;
            public readonly FloatInterval Z;

            public Interval3(FloatInterval x, FloatInterval y,
                FloatInterval z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public FloatInterval this[int index] => index switch
            {
                0 => X,
                1 => Y,
                2 => Z,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            public static Interval3 Singleton(float3 value) => new(
                FloatInterval.Singleton(value.x),
                FloatInterval.Singleton(value.y),
                FloatInterval.Singleton(value.z));
        }

        public readonly struct LineRule
        {
            public readonly int3 Direction;
            public readonly Shell Shell;
            public readonly float3 UnitDirection;
            public readonly float3 E1;
            public readonly float3 E2;
            public readonly ushort SectorOffset;
            public readonly byte SectorCount;

            internal LineRule(int3 direction, Shell shell,
                float3 unitDirection, float3 e1, float3 e2,
                ushort sectorOffset, byte sectorCount)
            {
                Direction = direction;
                Shell = shell;
                UnitDirection = unitDirection;
                E1 = e1;
                E2 = e2;
                SectorOffset = sectorOffset;
                SectorCount = sectorCount;
            }
        }

        public readonly struct DirectionRule
        {
            public readonly int3 Direction;
            public readonly byte LineClass;
            public readonly sbyte Orientation;
            public readonly Shell Shell;

            internal DirectionRule(int3 direction, byte lineClass,
                sbyte orientation, Shell shell)
            {
                Direction = direction;
                LineClass = lineClass;
                Orientation = orientation;
                Shell = shell;
            }
        }

        public readonly struct NodeRule
        {
            public readonly int3 Direction;
            public readonly byte LineClass;
            public readonly sbyte Orientation;
            public readonly Shell Shell;

            internal NodeRule(DirectionRule direction)
            {
                Direction = direction.Direction;
                LineClass = direction.LineClass;
                Orientation = direction.Orientation;
                Shell = direction.Shell;
            }
        }

        public readonly struct StrandRule
        {
            public readonly byte Node0;
            public readonly byte Node1;
            public readonly byte IncidenceKind;
            public readonly byte Petal0;
            public readonly byte Petal1;

            internal StrandRule(byte node0, byte node1, byte incidenceKind,
                byte petal0, byte petal1)
            {
                Node0 = node0;
                Node1 = node1;
                IncidenceKind = incidenceKind;
                Petal0 = petal0;
                Petal1 = petal1;
            }
        }

        public readonly struct PetalRule
        {
            public readonly byte FaceNode;
            public readonly byte EdgeNode;
            public readonly byte CornerNode;
            public readonly byte Strand0;
            public readonly byte Strand1;
            public readonly byte Strand2;
            public readonly sbyte Orientation;
            public readonly sbyte StrandSign0;
            public readonly sbyte StrandSign1;
            public readonly sbyte StrandSign2;

            internal PetalRule(byte faceNode, byte edgeNode, byte cornerNode,
                byte strand0, byte strand1, byte strand2, sbyte orientation)
                : this(faceNode, edgeNode, cornerNode, strand0, strand1,
                    strand2, orientation,
                    BoundarySign(faceNode, edgeNode, orientation),
                    BoundarySign(edgeNode, cornerNode, orientation),
                    BoundarySign(cornerNode, faceNode, orientation))
            {
            }

            internal PetalRule(byte faceNode, byte edgeNode, byte cornerNode,
                byte strand0, byte strand1, byte strand2, sbyte orientation,
                sbyte strandSign0, sbyte strandSign1, sbyte strandSign2)
            {
                FaceNode = faceNode;
                EdgeNode = edgeNode;
                CornerNode = cornerNode;
                Strand0 = strand0;
                Strand1 = strand1;
                Strand2 = strand2;
                Orientation = orientation;
                StrandSign0 = strandSign0;
                StrandSign1 = strandSign1;
                StrandSign2 = strandSign2;
            }

            public byte Node(int index) => index switch
            {
                0 => FaceNode,
                1 => EdgeNode,
                2 => CornerNode,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            public byte Strand(int index) => index switch
            {
                0 => Strand0,
                1 => Strand1,
                2 => Strand2,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            public sbyte StrandSign(int index) => index switch
            {
                0 => StrandSign0,
                1 => StrandSign1,
                2 => StrandSign2,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            private static sbyte BoundarySign(byte from, byte to,
                sbyte orientation) => checked((sbyte)(orientation *
                (from < to ? 1 : -1)));
        }

        public readonly struct ChildPetalRule
        {
            // Barycentric coefficients over parent A/B/C. Each packed triplet
            // uses two bits per coefficient in A,B,C order.
            public readonly byte Vertex0;
            public readonly byte Vertex1;
            public readonly byte Vertex2;

            internal ChildPetalRule(byte vertex0, byte vertex1, byte vertex2)
            {
                Vertex0 = vertex0;
                Vertex1 = vertex1;
                Vertex2 = vertex2;
            }

            public byte Vertex(int index) => index switch
            {
                0 => Vertex0,
                1 => Vertex1,
                2 => Vertex2,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        /// <summary>The whole-Flower loop family of one existing strand.
        /// RootNode is the directed difference of its two anchors, not the
        /// R2 anchor of either incident petal. Fine paths only identify
        /// duplicate incidence within each of those two parent contexts.</summary>
        public readonly struct PhaseFamilyRule
        {
            public readonly byte RootNode;
            public readonly sbyte PhaseOrientation;
            public readonly byte FinePath0;
            public readonly byte FinePath1;
            public readonly ulong RootIncidentPetals;

            internal PhaseFamilyRule(byte rootNode, sbyte phaseOrientation,
                byte finePath0, byte finePath1, ulong rootIncidentPetals)
            {
                RootNode = rootNode;
                PhaseOrientation = phaseOrientation;
                FinePath0 = finePath0;
                FinePath1 = finePath1;
                RootIncidentPetals = rootIncidentPetals;
            }

            public uint Packed => (uint)RootNode |
                (PhaseOrientation < 0 ? 1u << 5 : 0u) |
                ((uint)FinePath0 << 6) | ((uint)FinePath1 << 8);
        }

        /// <summary>Exact child-edge substitution into AB/BC/AC families.
        /// Endpoint reversal does not reverse the canonical loop phase.</summary>
        public readonly struct ChildPhaseEdgeRule
        {
            public readonly byte Family;
            public readonly sbyte EndpointOrientation;
            public readonly sbyte PhaseOrientation;

            internal ChildPhaseEdgeRule(byte family, sbyte endpointOrientation,
                sbyte phaseOrientation)
            {
                Family = family;
                EndpointOrientation = endpointOrientation;
                PhaseOrientation = phaseOrientation;
            }

            public uint Packed => (uint)Family |
                (EndpointOrientation < 0 ? 1u << 2 : 0u) |
                (PhaseOrientation < 0 ? 1u << 3 : 0u);
        }


        public readonly struct SectorBoundary
        {
            public readonly float2 Unit;
            public readonly Interval2 Enclosure;

            internal SectorBoundary(float2 unit, Interval2 enclosure)
            {
                Unit = unit;
                Enclosure = enclosure;
            }
        }

        public readonly struct TetraFrameRule
        {
            public readonly int4 LineClasses;
            public readonly int4 Eta;
            public readonly sbyte Chirality;

            internal TetraFrameRule(int4 lineClasses, int4 eta,
                sbyte chirality)
            {
                LineClasses = lineClasses;
                Eta = eta;
                Chirality = chirality;
            }
        }

        public readonly struct LoopFrame
        {
            public readonly float3 Center;
            public readonly float SphereRadius;
            public readonly float Radius;
            public readonly float3 Direction;
            public readonly float3 E1;
            public readonly float3 E2;

            internal LoopFrame(float3 center, float sphereRadius, float radius,
                float3 direction, float3 e1, float3 e2)
            {
                Center = center;
                SphereRadius = sphereRadius;
                Radius = radius;
                Direction = direction;
                E1 = e1;
                E2 = e2;
            }
        }

        public readonly struct RootResult
        {
            public readonly RootClassification Classification;
            public readonly Interval2 Minus;
            public readonly Interval2 Plus;

            internal RootResult(RootClassification classification,
                Interval2 minus, Interval2 plus)
            {
                Classification = classification;
                Minus = minus;
                Plus = plus;
            }
        }

        private static readonly int3[] CanonicalLinesValue =
        {
            new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            new(1, 1, 0), new(1, -1, 0),
            new(1, 0, 1), new(1, 0, -1),
            new(0, 1, 1), new(0, 1, -1),
            new(1, 1, 1), new(1, 1, -1),
            new(1, -1, 1), new(1, -1, -1)
        };

        private static readonly int3[] TetraAxesInteger =
        {
            new(1, 1, 1), new(1, -1, -1),
            new(-1, 1, -1), new(-1, -1, 1)
        };

        private static readonly LineRule[] LinesValue;
        private static readonly DirectionRule[] DirectionsValue;
        private static readonly NodeRule[] NodesValue;
        private static readonly StrandRule[] StrandsValue;
        private static readonly PetalRule[] PetalsValue;
        private static readonly ulong[] NodeIncidentPetalsValue;
        private static readonly SectorBoundary[] SectorBoundariesValue;
        private static readonly TetraFrameRule[] TetraFramesValue;
        private static readonly PhaseFamilyRule[] PhaseFamiliesValue;
        private static readonly ChildPhaseEdgeRule[] ChildPhaseEdgesValue;

        private static readonly ChildPetalRule[] ChildPetalsValue =
        {
            new(PackBarycentric(2, 0, 0), PackBarycentric(1, 1, 0),
                PackBarycentric(1, 0, 1)),
            new(PackBarycentric(1, 1, 0), PackBarycentric(0, 2, 0),
                PackBarycentric(0, 1, 1)),
            new(PackBarycentric(1, 0, 1), PackBarycentric(0, 1, 1),
                PackBarycentric(0, 0, 2)),
            new(PackBarycentric(1, 1, 0), PackBarycentric(0, 1, 1),
                PackBarycentric(1, 0, 1))
        };

        static MerkabaSphereFlowerAuthority()
        {
#if UNITY_EDITOR
            DirectionRule[] directions = BuildDirections();
            NodeRule[] nodes = new NodeRule[directions.Length];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = new NodeRule(directions[i]);
            BuildPetalsAndStrands(nodes, out PetalRule[] petals,
                out StrandRule[] strands);
            BuildSectorBoundaries(petals, nodes, out LineRule[] lines,
                out SectorBoundary[] sectors);
            TetraFrameRule[] tetraFrames = BuildTetraFrames();

            if (directions.Length != DirectedRelationCount ||
                nodes.Length != NodeClassCount ||
                strands.Length != StrandClassCount ||
                petals.Length != PetalClassCount ||
                lines.Length != LineClassCount ||
                tetraFrames.Length != TetraFrameCount)
                throw new InvalidOperationException(
                    "Generated Sphere-Flower alphabet has invalid cardinality.");

            DirectionsValue = directions;
            NodesValue = nodes;
            PetalsValue = petals;
            NodeIncidentPetalsValue = new ulong[NodeClassCount];
            for (int p = 0; p < petals.Length; p++)
            {
                ulong bit = 1UL << p;
                NodeIncidentPetalsValue[petals[p].FaceNode] |= bit;
                NodeIncidentPetalsValue[petals[p].EdgeNode] |= bit;
                NodeIncidentPetalsValue[petals[p].CornerNode] |= bit;
            }
            StrandsValue = strands;
            LinesValue = lines;
            SectorBoundariesValue = sectors;
            TetraFramesValue = tetraFrames;
            PhaseFamiliesValue = BuildPhaseFamilies();
            ChildPhaseEdgesValue = BuildChildPhaseEdges();
#else
            LoadGeneratedTables(out LinesValue, out DirectionsValue,
                out NodesValue, out StrandsValue, out PetalsValue,
                out SectorBoundariesValue, out TetraFramesValue);
            PhaseFamiliesValue = LoadGeneratedPhaseFamilies();
            ChildPhaseEdgesValue = LoadGeneratedChildPhaseEdges();
            NodeIncidentPetalsValue = LoadGeneratedNodeIncidentPetals();
#endif
            ValidateFrozenTables();
#if UNITY_EDITOR
            ValidateCubeSymmetry();
#endif
        }

        public static ReadOnlySpan<LineRule> Lines => LinesValue;
        public static ReadOnlySpan<DirectionRule> Directions => DirectionsValue;
        public static ReadOnlySpan<NodeRule> Nodes => NodesValue;
        public static ReadOnlySpan<StrandRule> Strands => StrandsValue;
        public static ReadOnlySpan<PetalRule> Petals => PetalsValue;
        public static ReadOnlySpan<ulong> NodeIncidentPetals => NodeIncidentPetalsValue;
        public static ReadOnlySpan<ChildPetalRule> ChildPetals => ChildPetalsValue;
        public static ReadOnlySpan<SectorBoundary> SectorBoundaries =>
            SectorBoundariesValue;
        public static ReadOnlySpan<TetraFrameRule> TetraFrames => TetraFramesValue;
        public static ReadOnlySpan<PhaseFamilyRule> PhaseFamilies => PhaseFamiliesValue;
        public static ReadOnlySpan<ChildPhaseEdgeRule> ChildPhaseEdges => ChildPhaseEdgesValue;


        /// <summary>
        /// FNV-1a over every frozen topology word in its canonical serialized
        /// order. The generated C# and HLSL artifacts embed this value, making
        /// a stale or independently edited table a hard parity failure.
        /// </summary>
        public static uint ComputeFrozenTableHash()
        {
            uint hash = 2166136261u;
            void Word(uint value)
            {
                hash = HashByte(hash, (byte)value);
                hash = HashByte(hash, (byte)(value >> 8));
                hash = HashByte(hash, (byte)(value >> 16));
                hash = HashByte(hash, (byte)(value >> 24));
            }

            foreach (LineRule value in LinesValue)
            {
                Word(unchecked((uint)value.Direction.x));
                Word(unchecked((uint)value.Direction.y));
                Word(unchecked((uint)value.Direction.z));
                Word((uint)value.Shell);
                Word(math.asuint(value.UnitDirection.x));
                Word(math.asuint(value.UnitDirection.y));
                Word(math.asuint(value.UnitDirection.z));
                Word(math.asuint(value.E1.x));
                Word(math.asuint(value.E1.y));
                Word(math.asuint(value.E1.z));
                Word(math.asuint(value.E2.x));
                Word(math.asuint(value.E2.y));
                Word(math.asuint(value.E2.z));
                Word(value.SectorOffset);
                Word(value.SectorCount);
            }
            foreach (DirectionRule value in DirectionsValue)
            {
                Word(unchecked((uint)value.Direction.x));
                Word(unchecked((uint)value.Direction.y));
                Word(unchecked((uint)value.Direction.z));
                Word(value.LineClass);
                Word(unchecked((uint)value.Orientation));
                Word((uint)value.Shell);
            }
            foreach (NodeRule value in NodesValue)
            {
                Word(unchecked((uint)value.Direction.x));
                Word(unchecked((uint)value.Direction.y));
                Word(unchecked((uint)value.Direction.z));
                Word(value.LineClass);
                Word(unchecked((uint)value.Orientation));
                Word((uint)value.Shell);
            }
            foreach (StrandRule value in StrandsValue)
            {
                Word(value.Node0);
                Word(value.Node1);
                Word(value.IncidenceKind);
                Word(value.Petal0);
                Word(value.Petal1);
            }
            foreach (PetalRule value in PetalsValue)
            {
                Word(value.FaceNode);
                Word(value.EdgeNode);
                Word(value.CornerNode);
                Word(value.Strand0);
                Word(value.Strand1);
                Word(value.Strand2);
                Word(unchecked((uint)value.Orientation));
                Word(unchecked((uint)value.StrandSign0));
                Word(unchecked((uint)value.StrandSign1));
                Word(unchecked((uint)value.StrandSign2));
            }
            foreach (ChildPetalRule value in ChildPetalsValue)
            {
                Word(value.Vertex0);
                Word(value.Vertex1);
                Word(value.Vertex2);
            }
            foreach (SectorBoundary value in SectorBoundariesValue)
            {
                Word(math.asuint(value.Unit.x));
                Word(math.asuint(value.Unit.y));
                Word(math.asuint(value.Enclosure.X.Lower));
                Word(math.asuint(value.Enclosure.X.Upper));
                Word(math.asuint(value.Enclosure.Y.Lower));
                Word(math.asuint(value.Enclosure.Y.Upper));
            }
            foreach (ulong mask in AnchorSectorPetalMasks)
            {
                Word((uint)mask);
                Word((uint)(mask >> 32));
            }
            foreach (BoundaryRule rule in BoundaryRules)
            {
                for (int axis = 0; axis < 4; axis++) Word(unchecked((uint)rule.Rational[axis]));
                for (int axis = 0; axis < 3; axis++) Word(unchecked((uint)rule.D[axis]));
                Word(rule.Owner);
            }
            foreach (ulong mask in AnchorBoundaryPetalMasks)
            {
                Word((uint)mask);
                Word((uint)(mask >> 32));
            }
            foreach (ushort offset in L2BoundaryFlagOffsets) Word(offset);
            foreach (byte index in L2BoundaryFlagIndices) Word(index);
            foreach (ulong mask in L2BoundaryFlagMasks)
            {
                Word((uint)mask);
                Word((uint)(mask >> 32));
            }
            foreach (TetraFrameRule value in TetraFramesValue)
            {
                for (int i = 0; i < 4; i++)
                    Word(unchecked((uint)value.LineClasses[i]));
                for (int i = 0; i < 4; i++)
                    Word(unchecked((uint)value.Eta[i]));
                Word(unchecked((uint)value.Chirality));
            }
            foreach (SkinChamberRule value in SkinChambers)
            {
                Word(value.High);
                Word(value.Middle);
                Word(value.Low);
                Word(value.ChildSite);
                Word(value.ChildWedge);
                Word(value.NextStitchState);
                Word(unchecked((uint)value.Orientation));
            }
            foreach (ushort value in SkinCanonicalToThread) Word(value);
            foreach (ushort value in SkinThreadToCanonical) Word(value);
            foreach (ushort value in SkinCanonicalL5ToThread) Word(value);
            foreach (byte value in SkinL3ChildRank) Word(value);
            foreach (byte value in SkinL3State) Word(value);
            foreach (byte value in SkinL4ParentThread) Word(value);
            foreach (byte value in SkinL4ChildRank) Word(value);
            foreach (byte value in SkinL4State) Word(value);
            foreach (byte value in SkinL5ChildRank) Word(value);
            foreach (PhaseFamilyRule value in PhaseFamiliesValue)
            {
                Word(value.Packed);
                Word((uint)value.RootIncidentPetals);
                Word((uint)(value.RootIncidentPetals >> 32));
                Word(0u);
            }
            foreach (ChildPhaseEdgeRule value in ChildPhaseEdgesValue) Word(value.Packed);
            foreach (L2KnotRule value in L2Knots) Word(value.Source);
            foreach (L2WedgeRule value in L2Wedges)
            {
                Word(value.Hub); Word(value.Ring0); Word(value.Ring1); Word(value.Source);
            }
            foreach (ushort value in L2SourceToWedge) Word(value);
            foreach (ushort value in L2IncidenceOffsets) Word(value);
            foreach (uint value in L2IncidenceSources) Word(value);
            foreach (ushort value in L2WedgeOwnerOffsets) Word(value);
            foreach (L2WedgeOwnerRule value in L2WedgeOwners)
            {
                Word(unchecked((uint)value.OwnerOffset.x));
                Word(unchecked((uint)value.OwnerOffset.y));
                Word(unchecked((uint)value.OwnerOffset.z));
                Word(value.Packed);
            }
            foreach (ushort value in L2EdgeIncidence) Word(value);
            foreach (JunctionRule value in JunctionRules)
            {
                Word(value.Corners);
                Word(value.Flags);
            }
            for (int level = 0; level < GeometryLevelCount; level++)
                for (int line = 0; line < LineClassCount; line++)
                    Word(math.asuint(EvaluateLoop(level, default, line).Radius));
            return hash;
        }

        public static float LevelStep(int level)
        {
            if ((uint)level >= LevelCount)
                throw new ArgumentOutOfRangeException(nameof(level));
            return LatticeStep / (1 << level);
        }

        public static int3 OverlapOwner(int3 firstOwner, int ordinal)
        {
            if ((uint)ordinal >= 8u)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            return new int3(checked(firstOwner.x + (ordinal & 1)),
                checked(firstOwner.y + ((ordinal >> 1) & 1)),
                checked(firstOwner.z + ((ordinal >> 2) & 1)));
        }

        public static uint OverlapBoundaryMask(int3 firstOwner, int spanShift)
        {
            if (spanShift != 3 && spanShift != 5 && spanShift != 8)
                throw new ArgumentOutOfRangeException(nameof(spanShift));
            uint mask = (1u << spanShift) - 1u;
            bool3 crosses = (math.asuint(firstOwner) & mask) == mask;
            return (crosses.x ? 1u : 0u) | (crosses.y ? 2u : 0u) |
                (crosses.z ? 4u : 0u);
        }

        public static Long3 JunctionAddress(int3 kernel, int3 direction) =>
            new((long)kernel.x * 2L + direction.x,
                (long)kernel.y * 2L + direction.y,
                (long)kernel.z * 2L + direction.z);

        public static int JunctionParity(Long3 junction) =>
            (int)((junction.X & 1L) + (junction.Y & 1L) +
                  (junction.Z & 1L));

        public static bool TryResolveEndpointPair(Long3 junction,
            int lineClass, out Long3 first, out Long3 second)
        {
            if ((uint)lineClass >= LineClassCount)
                throw new ArgumentOutOfRangeException(nameof(lineClass));
            int3 direction = LinesValue[lineClass].Direction;
            long x = junction.X - direction.x;
            long y = junction.Y - direction.y;
            long z = junction.Z - direction.z;
            if (((x | y | z) & 1L) != 0L)
            {
                first = default;
                second = default;
                return false;
            }
            first = new Long3(x / 2L, y / 2L, z / 2L);
            second = new Long3(first.X + direction.x,
                first.Y + direction.y, first.Z + direction.z);
            return true;
        }

        public static int3 CanonicalDirection(int3 direction,
            out sbyte orientation)
        {
            ValidateDirection(direction);
            int first = direction.x != 0 ? direction.x :
                direction.y != 0 ? direction.y : direction.z;
            orientation = (sbyte)(first > 0 ? 1 : -1);
            return first > 0 ? direction : -direction;
        }

        public static int FindLineClass(int3 direction,
            out sbyte orientation)
        {
            int3 canonical = CanonicalDirection(direction, out orientation);
            for (int i = 0; i < CanonicalLinesValue.Length; i++)
                if (math.all(CanonicalLinesValue[i] == canonical)) return i;
            throw new InvalidOperationException("Canonical line is missing.");
        }

        public static Shell ShellOf(int3 direction)
        {
            ValidateDirection(direction);
            int squared = Dot(direction, direction);
            return squared switch
            {
                1 => Shell.R1Core,
                2 => Shell.R2Shape,
                3 => Shell.R3Closure,
                _ => throw new InvalidOperationException()
            };
        }

        public static LoopFrame EvaluateLoop(int level, Long3 junction,
            int lineClass)
        {
            if ((uint)level >= GeometryLevelCount)
                throw new ArgumentOutOfRangeException(nameof(level),
                    "World Sphere-Flower geometry terminates at L2.");
            if ((uint)lineClass >= LineClassCount)
                throw new ArgumentOutOfRangeException(nameof(lineClass));
            float step = LevelStep(level);
            LineRule line = LinesValue[lineClass];
            float3 center = new((float)(0.5 * step * junction.X),
                (float)(0.5 * step * junction.Y),
                (float)(0.5 * step * junction.Z));
            float sphereRadius = (float)(step *
                Math.Sqrt(Dot(line.Direction, line.Direction)));
            float radius = (float)(Math.Sqrt(3.0) * 0.5 * sphereRadius);
            return new LoopFrame(center, sphereRadius, radius, line.UnitDirection,
                line.E1, line.E2);
        }

        public static Interval3 RestrictPlaneToLoop(float3 kernelCenter,
            float3 decodedNormal, float decodedOffset,
            float normalUncertainty, float offsetUncertainty,
            LoopFrame loop)
        {
            if (!math.all(math.isfinite(decodedNormal)) ||
                !math.all(math.isfinite(loop.Center)) || !math.all(math.isfinite(kernelCenter)) ||
                !float.IsFinite(decodedOffset) || !float.IsFinite(loop.Radius) ||
                !float.IsFinite(normalUncertainty) || !float.IsFinite(offsetUncertainty) ||
                !(loop.Radius > 0f) || normalUncertainty < 0f || offsetUncertainty < 0f)
                throw new ArgumentOutOfRangeException(nameof(normalUncertainty));
            float3 relative = new(OrderedSubtract(loop.Center.x, kernelCenter.x),
                OrderedSubtract(loop.Center.y, kernelCenter.y),
                OrderedSubtract(loop.Center.z, kernelCenter.z));
            float a = OrderedSubtract(OrderedDot(decodedNormal, relative), decodedOffset);
            float b = OrderedMultiply(loop.Radius, OrderedDot(decodedNormal, loop.E1));
            float c = OrderedMultiply(loop.Radius, OrderedDot(decodedNormal, loop.E2));

            // Exact operation order of generated M8FlowerPlaneIntervals. The
            // double, minimally rounded mathematical enclosure is not the
            // production enclosure: its midpoint would differ from live HLSL.
            FloatInterval lengthSquared = FloatInterval.Add(FloatInterval.Add(
                FloatInterval.Square(FloatInterval.Singleton(relative.x)),
                FloatInterval.Square(FloatInterval.Singleton(relative.y))),
                FloatInterval.Square(FloatInterval.Singleton(relative.z)));
            FloatInterval length = FloatInterval.Sqrt(lengthSquared);
            float sumA = OrderedAdd(AbsoluteProductSumUpper(decodedNormal, relative), Math.Abs(decodedOffset));
            float sumB = OrderedMultiply(loop.Radius, AbsoluteProductSumUpper(decodedNormal, loop.E1));
            float sumC = OrderedMultiply(loop.Radius, AbsoluteProductSumUpper(decodedNormal, loop.E2));
            float gamma6 = FloatInterval.Enclose(Gamma(6)).Upper;
            float gamma7 = FloatInterval.Enclose(Gamma(7)).Upper;

            float metricA = OrderedMultiply(normalUncertainty, length.Upper);
            float offsetA = OrderedAdd(NextFloat(metricA), offsetUncertainty);
            float roundA = OrderedMultiply(gamma6, NextFloat(sumA));
            float boundA = OrderedAdd(NextFloat(offsetA), NextFloat(roundA));
            float radialMetric = OrderedMultiply(loop.Radius, normalUncertainty);
            float roundB = OrderedMultiply(gamma7, NextFloat(sumB));
            float roundC = OrderedMultiply(gamma7, NextFloat(sumC));
            float boundB = OrderedAdd(NextFloat(radialMetric), NextFloat(roundB));
            float boundC = OrderedAdd(NextFloat(radialMetric), NextFloat(roundC));

            return new Interval3(OrderedCenterRadius(a, NextFloat(boundA)),
                OrderedCenterRadius(b, NextFloat(boundB)),
                OrderedCenterRadius(c, NextFloat(boundC)));
        }

        // Each cast is one binary32 rounding boundary matching the generated
        // precise HLSL expression. A library dot product may reassociate or
        // contract the sum and therefore is not the section-5 operation order.
        private static float OrderedDot(float3 left, float3 right)
        {
            float x = OrderedMultiply(left.x, right.x);
            float y = OrderedMultiply(left.y, right.y);
            float z = OrderedMultiply(left.z, right.z);
            return OrderedAdd(OrderedAdd(x, y), z);
        }

        private static float OrderedAdd(float left, float right) =>
            (float)((double)left + right);

        private static float OrderedSubtract(float left, float right) =>
            (float)((double)left - right);

        private static float OrderedMultiply(float left, float right) =>
            (float)((double)left * right);

        private static float AbsoluteProductSumUpper(float3 left, float3 right)
        {
            float x = NextFloat(Math.Abs(OrderedMultiply(left.x, right.x)));
            float y = NextFloat(Math.Abs(OrderedMultiply(left.y, right.y)));
            float z = NextFloat(Math.Abs(OrderedMultiply(left.z, right.z)));
            return NextFloat(OrderedAdd(NextFloat(OrderedAdd(x, y)), z));
        }

        private static FloatInterval OrderedCenterRadius(float center, float radius) =>
            new(PreviousFloat(OrderedSubtract(center, radius)),
                NextFloat(OrderedAdd(center, radius)));

        // The shader's DivideEnclosed uses exact dyadic product comparisons.
        // Binary64 represents every product of two finite binary32 operands
        // exactly, so the same tight bracket needs no reciprocal multiplication
        // and no dependency on the host's initial division hint.
        private static bool TryDivideEnclosed(float numerator, float denominator,
            out FloatInterval result)
        {
            result = default;
            if (!float.IsFinite(numerator) || !float.IsFinite(denominator) ||
                !(denominator > 0f)) return false;
            uint sign = math.asuint(numerator) & 0x80000000u;
            float positive = math.asfloat(math.asuint(numerator) & 0x7fffffffu);
            if (positive == 0f) return true;
            const uint maximum = 0x7f7fffffu;
            float hint = (float)((double)positive / denominator);
            uint centre = Math.Min(math.asuint(hint), maximum);
            uint lo = centre > 0u ? centre - 1u : 0u;
            uint hi = centre < maximum ? centre + 1u : maximum;
            if ((double)math.asfloat(lo) * denominator > positive) lo = 0u;
            if ((double)math.asfloat(hi) * denominator < positive) hi = maximum;
            if ((double)math.asfloat(hi) * denominator < positive) return false;
            while (hi - lo > 1u)
            {
                uint middle = lo + ((hi - lo) >> 1);
                double product = (double)math.asfloat(middle) * denominator;
                if (product == positive) { lo = middle; hi = middle; break; }
                if (product < positive) lo = middle;
                else hi = middle;
            }
            if ((double)math.asfloat(lo) * denominator == positive) hi = lo;
            else if ((double)math.asfloat(hi) * denominator == positive) lo = hi;
            result = sign == 0u
                ? new FloatInterval(math.asfloat(lo), math.asfloat(hi))
                : new FloatInterval(math.asfloat(hi | sign), math.asfloat(lo | sign));
            return true;
        }

        public static RootClassification ClassifyRoots(Interval3 abc)
        {
            if (abc.X.IsSingleton && abc.Y.IsSingleton && abc.Z.IsSingleton &&
                ExactSquaredEquality(abc.X.Lower, abc.Y.Lower, abc.Z.Lower))
                return abc.Y.Lower == 0f && abc.Z.Lower == 0f
                    ? RootClassification.CoplanarLoop
                    : RootClassification.CertainTangent;

            FloatInterval q = FloatInterval.Add(FloatInterval.Square(abc.Y),
                FloatInterval.Square(abc.Z));
            FloatInterval a2 = FloatInterval.Square(abc.X);

            if (q.IsSingleton && q.Lower == 0f)
            {
                if (abc.X.IsSingleton)
                    return abc.X.Lower == 0f
                        ? RootClassification.CoplanarLoop
                        : RootClassification.Impossible;
                return abc.X.ContainsZero
                    ? RootClassification.Ambiguous
                    : RootClassification.Impossible;
            }

            if (a2.Upper < q.Lower) return RootClassification.CertainSecant;
            if (a2.Lower > q.Upper) return RootClassification.Impossible;
            if (a2.IsSingleton && q.IsSingleton && a2.Lower == q.Lower &&
                q.Lower > 0f)
                return RootClassification.CertainTangent;
            return RootClassification.Ambiguous;
        }

        public static RootResult EvaluateRoots(Interval3 abc)
        {
            if (!float.IsFinite(abc.X.Lower) || !float.IsFinite(abc.X.Upper) ||
                !float.IsFinite(abc.Y.Lower) || !float.IsFinite(abc.Y.Upper) ||
                !float.IsFinite(abc.Z.Lower) || !float.IsFinite(abc.Z.Upper))
                return new RootResult(RootClassification.Ambiguous, default, default);
            RootClassification classification = ClassifyRoots(abc);
            if (classification != RootClassification.CertainSecant &&
                classification != RootClassification.CertainTangent)
                return new RootResult(classification, default, default);

            FloatInterval q = FloatInterval.Add(FloatInterval.Square(abc.Y),
                FloatInterval.Square(abc.Z));
            FloatInterval minusA = new(-abc.X.Upper, -abc.X.Lower);
            FloatInterval baseX = FloatInterval.Multiply(minusA, abc.Y);
            FloatInterval baseY = FloatInterval.Multiply(minusA, abc.Z);
            if (classification == RootClassification.CertainTangent)
            {
                // The classifier proved exact equality. Re-subtracting
                // outward-rounded Q-A² would manufacture uncertainty here.
                bool xValid = FloatInterval.TryDividePositive(baseX, q, out FloatInterval x);
                bool yValid = FloatInterval.TryDividePositive(baseY, q, out FloatInterval y);
                if (!xValid || !yValid)
                    return new RootResult(RootClassification.Ambiguous, default, default);
                var tangent = new Interval2(x, y);
                return new RootResult(classification, tangent, tangent);
            }
            FloatInterval delta = FloatInterval.Subtract(q,
                FloatInterval.Square(abc.X));
            if (!FloatInterval.TrySqrt(delta, out FloatInterval rootDelta))
                return new RootResult(RootClassification.Ambiguous,
                    default, default);
            // M8FlowerRootInterval negates the turn BEFORE multiplication for
            // the minus branch, then uses IAdd for both signs. Preserve even
            // exact-zero/signed-zero operation boundaries, not just algebraic
            // equivalence of subtracting a previously evaluated positive turn.
            FloatInterval negativeTurn = new(-rootDelta.Upper, -rootDelta.Lower);
            FloatInterval minusC = new(-abc.Z.Upper, -abc.Z.Lower);
            FloatInterval minusX = FloatInterval.Add(baseX, FloatInterval.Multiply(negativeTurn, minusC));
            FloatInterval minusY = FloatInterval.Add(baseY, FloatInterval.Multiply(negativeTurn, abc.Y));
            FloatInterval plusX = FloatInterval.Add(baseX, FloatInterval.Multiply(rootDelta, minusC));
            FloatInterval plusY = FloatInterval.Add(baseY, FloatInterval.Multiply(rootDelta, abc.Y));
            bool minusXValid = FloatInterval.TryDividePositive(minusX, q, out FloatInterval mx);
            bool minusYValid = FloatInterval.TryDividePositive(minusY, q, out FloatInterval my);
            bool plusXValid = FloatInterval.TryDividePositive(plusX, q, out FloatInterval px);
            bool plusYValid = FloatInterval.TryDividePositive(plusY, q, out FloatInterval py);
            if (!minusXValid || !minusYValid || !plusXValid || !plusYValid)
                return new RootResult(RootClassification.Ambiguous, default, default);
            Interval2 minus = new(mx, my);
            Interval2 plus = new(px, py);
            return new RootResult(classification, minus, plus);
        }

        public static bool TryEvaluateRoot(float3 abc, bool plus,
            out float2 root)
        {
            float q = abc.y * abc.y + abc.z * abc.z;
            float delta = q - abc.x * abc.x;
            if (!(q > 0f) || delta < 0f)
            {
                root = default;
                return false;
            }
            float s = plus ? 1f : -1f;
            float d = math.sqrt(delta);
            root = new float2(
                (-abc.x * abc.y + s * d * -abc.z) / q,
                (-abc.x * abc.z + s * d * abc.y) / q);
            return true;
        }

        public static ProofClassification ClassifySector(int lineClass,
            Interval2 root, out int sector)
        {
            if ((uint)lineClass >= LineClassCount)
                throw new ArgumentOutOfRangeException(nameof(lineClass));
            LineRule line = LinesValue[lineClass];
            int found = -1;
            for (int i = 0; i < line.SectorCount; i++)
            {
                SectorBoundary start = SectorBoundariesValue[
                    line.SectorOffset + i];
                SectorBoundary end = SectorBoundariesValue[
                    line.SectorOffset + (i + 1) % line.SectorCount];
                FloatInterval left = Cross(start.Enclosure, root);
                FloatInterval right = Cross(root, end.Enclosure);
                if (left.Lower > 0f && right.Lower > 0f)
                {
                    if (found >= 0)
                    {
                        sector = -1;
                        return ProofClassification.Ambiguous;
                    }
                    found = i;
                }
            }
            sector = found;
            return found >= 0 ? ProofClassification.Certain :
                ProofClassification.Ambiguous;
        }

        public static ProofClassification TangentHalfAngle(Interval2 from,
            Interval2 to, out FloatInterval value)
        {
            FloatInterval numerator = Cross(from, to);
            FloatInterval denominator = FloatInterval.Add(
                FloatInterval.Singleton(1f), Dot(from, to));
            return FloatInterval.TryDividePositive(numerator, denominator, out value)
                ? ProofClassification.Certain : ProofClassification.Ambiguous;
        }

        public static float2 RotateTangentHalfAngle(float2 root, float turn)
        {
            float squared = turn * turn;
            float inverse = 1f / (1f + squared);
            float c = (1f - squared) * inverse;
            float s = 2f * turn * inverse;
            return new float2(c * root.x - s * root.y,
                s * root.x + c * root.y);
        }

        public static ProofClassification SealBend(Interval2 first,
            Interval2 second, int lineClass, out Interval2 seal,
            out FloatInterval bend, out int sector)
        {
            seal = default;
            bend = default;
            sector = -1;
            if (ClassifySector(lineClass, first, out sector) !=
                    ProofClassification.Certain ||
                ClassifySector(lineClass, second, out int secondSector) !=
                    ProofClassification.Certain || sector != secondSector)
                return ProofClassification.Ambiguous;
            if (!TrySealRootIntervals(first, second, out seal))
                return ProofClassification.Ambiguous;
            if (ClassifySector(lineClass, seal, out int sharedSector) !=
                    ProofClassification.Certain || sharedSector != sector)
                return ProofClassification.Ambiguous;
            return TangentHalfAngle(seal, second, out bend);
        }

        // The metric expression is shared by strict interval-only and
        // evidence-aware SEAL. Topological boundary evidence never replaces
        // this full ordered sum/normalization with an endpoint or singleton.
        private static bool TrySealRootIntervals(Interval2 first, Interval2 second,
            out Interval2 seal)
        {
            seal = default;
            FloatInterval x = FloatInterval.Add(first.X, second.X);
            FloatInterval y = FloatInterval.Add(first.Y, second.Y);
            if (!FloatInterval.TrySqrt(FloatInterval.Add(FloatInterval.Square(x),
                    FloatInterval.Square(y)), out FloatInterval length) ||
                !(length.Lower > 0f) ||
                !FloatInterval.TryDividePositive(x, length, out FloatInterval sealX) ||
                !FloatInterval.TryDividePositive(y, length, out FloatInterval sealY))
                return false;
            seal = new Interval2(sealX, sealY);
            return true;
        }

        public static ProofClassification RotateTangentHalfAngle(
            Interval2 prediction, FloatInterval turn, int lineClass,
            int sector, out Interval2 root)
        {
            root = default;
            if (ClassifySector(lineClass, prediction, out int predictedSector) !=
                    ProofClassification.Certain || predictedSector != sector)
                return ProofClassification.Ambiguous;
            if (!RotatePhaseMetric(prediction, turn, out root)) return ProofClassification.Ambiguous;
            return ClassifySector(lineClass, root, out int synthesizedSector) ==
                    ProofClassification.Certain && synthesizedSector == sector
                ? ProofClassification.Certain : ProofClassification.Ambiguous;
        }

        internal static FloatInterval DecodePhaseInterval(int lower, int upper)
        {
            if (lower > upper)
                throw new ArgumentOutOfRangeException(nameof(lower));
            // Both products are exact in binary64. A direct int->float cast
            // before scaling can round an endpoint into the stored interval.
            const double inverseScale = 1.0 / 536870912.0;
            return new FloatInterval(
                FloatInterval.Enclose(lower * inverseScale).Lower,
                FloatInterval.Enclose(upper * inverseScale).Upper);
        }

        /// <summary>Section 10.2 prediction for a NEW generated child knot.
        /// Inherited knots do not enter this method: their parent root and
        /// draw identity are copied verbatim by the substitution consumer.
        /// The caller obtains ancestry terms only from generated transport
        /// rows and valid owner-epoch records, in coarse-to-fine order.</summary>
        internal static ProofClassification PredictChildPhase(
            int childGeometryLevel, Interval3 childCarrierAbc,
            int childLineClass, int childSector, bool plusRoot,
            ReadOnlySpan<PhaseTransportTerm> ancestry, out Interval2 prediction)
        {
            prediction = default;
            if (childGeometryLevel <= 0 || childGeometryLevel >= GeometryLevelCount ||
                (uint)childLineClass >= LineClassCount ||
                (uint)childSector >= LinesValue[childLineClass].SectorCount ||
                ancestry.Length > childGeometryLevel)
                return ProofClassification.Ambiguous;
            for (int component = 0; component < 3; component++)
                if (!float.IsFinite(childCarrierAbc[component].Lower) ||
                    !float.IsFinite(childCarrierAbc[component].Upper))
                    return ProofClassification.Ambiguous;
            int previousLevel = -1;
            for (int i = 0; i < ancestry.Length; i++)
            {
                PhaseTransportTerm term = ancestry[i];
                if (term.Lower > term.Upper ||
                    (term.Lower <= 0 && term.Upper >= 0 &&
                        (term.Lower != 0 || term.Upper != 0)) ||
                    (term.Orientation != -1 && term.Orientation != 1) ||
                    term.AncestorLevel <= previousLevel ||
                    term.AncestorLevel >= childGeometryLevel)
                    return ProofClassification.Ambiguous;
                previousLevel = term.AncestorLevel;
            }

            RootResult basis = EvaluateRoots(childCarrierAbc);
            if (basis.Classification == RootClassification.Impossible)
                return ProofClassification.Impossible;
            if (basis.Classification != RootClassification.CertainSecant &&
                basis.Classification != RootClassification.CertainTangent)
                return ProofClassification.Ambiguous;
            Interval2 current = plusRoot ? basis.Plus : basis.Minus;
            if (!IsFinitePhaseRoot(current) ||
                ClassifySector(childLineClass, current, out int baseSector) !=
                    ProofClassification.Certain || baseSector != childSector)
                return ProofClassification.Ambiguous;

            for (int i = 0; i < ancestry.Length; i++)
            {
                PhaseTransportTerm term = ancestry[i];
                FloatInterval residual = DecodePhaseInterval(term.Lower, term.Upper);
                if (term.Orientation < 0)
                    residual = new FloatInterval(-residual.Upper, -residual.Lower);
                // Identity is an exact copy, not another rounded rotation.
                if (residual.IsSingleton && residual.Lower == 0f) continue;
                if (RotateTangentHalfAngle(current, residual, childLineClass,
                        childSector, out Interval2 rotated) != ProofClassification.Certain ||
                    !IsFinitePhaseRoot(rotated))
                    return ProofClassification.Ambiguous;
                current = rotated;
            }
            prediction = current;
            return ProofClassification.Certain;
        }

        /// <summary>Exact child loop and its source family from generated
        /// incidence. An inherited site returns its ORIGINAL level/J/line;
        /// the consumer must copy the corresponding parent root verbatim.</summary>
        internal static bool TryGetChildPhaseLoop(int petalClass, int parentContext,
            int knotSite, out int level, out int3 junctionOffset, out int lineClass,
            out int strandClass, out sbyte endpointOrientation,
            out sbyte phaseOrientation, out int inheritedParentNode)
        {
            level = lineClass = strandClass = 0;
            junctionOffset = default;
            endpointOrientation = phaseOrientation = 0;
            inheritedParentNode = -1;
            if ((uint)petalClass >= PetalClassCount ||
                (uint)parentContext >= ChildPhaseParentContextCount ||
                (uint)knotSite >= ChildPhaseKnotSiteCount)
                return false;
            PetalRule petal = PetalsValue[petalClass];
            if (knotSite < 3)
            {
                inheritedParentNode = knotSite;
                byte packed = parentContext == 0
                    ? (byte)(2 << (2 * knotSite))
                    : ChildPetalsValue[parentContext - 1].Vertex(knotSite);
                int3 weights = ChildCoefficients(packed);
                int rootNode = weights.x == 2 ? 0 : weights.y == 2 ? 1 :
                    weights.z == 2 ? 2 : -1;
                if (rootNode >= 0)
                {
                    NodeRule source = NodesValue[petal.Node(rootNode)];
                    junctionOffset = source.Direction;
                    lineClass = source.LineClass;
                    endpointOrientation = source.Orientation;
                    return true;
                }
                int edge = weights.x == 0 ? 1 : weights.z == 0 ? 0 : 2;
                strandClass = petal.Strand(edge);
                StrandRule strand = StrandsValue[strandClass];
                NodeRule family = NodesValue[PhaseFamiliesValue[strandClass].RootNode];
                level = 1;
                junctionOffset = NodesValue[strand.Node0].Direction +
                    NodesValue[strand.Node1].Direction;
                lineClass = family.LineClass;
                endpointOrientation = family.Orientation;
                return true;
            }

            int localEdge = knotSite - 3;
            int familyEdge = localEdge;
            sbyte endpoint = 1;
            sbyte childPhase = 1;
            if (parentContext != 0)
            {
                ChildPhaseEdgeRule child = ChildPhaseEdgesValue[
                    (parentContext - 1) * 3 + localEdge];
                familyEdge = child.Family;
                endpoint = child.EndpointOrientation;
                childPhase = child.PhaseOrientation;
            }
            strandClass = petal.Strand(familyEdge);
            PhaseFamilyRule rule = PhaseFamiliesValue[strandClass];
            NodeRule channel = NodesValue[rule.RootNode];
            level = parentContext == 0 ? 1 : 2;
            junctionOffset = PhaseParentNode(petal, parentContext, EdgeFirst(localEdge)) +
                PhaseParentNode(petal, parentContext, EdgeSecond(localEdge));
            lineClass = channel.LineClass;
            endpointOrientation = checked((sbyte)(channel.Orientation * endpoint));
            phaseOrientation = checked((sbyte)(rule.PhaseOrientation * childPhase));
            return true;
        }

        private static bool TryOwnerJunction(int3 owner, int level, int3 offset,
            out int3 junction)
        {
            long scale = 2L << level;
            long x = scale * owner.x + offset.x;
            long y = scale * owner.y + offset.y;
            long z = scale * owner.z + offset.z;
            junction = default;
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue ||
                y > int.MaxValue || z < int.MinValue || z > int.MaxValue)
                return false;
            junction = new int3((int)x, (int)y, (int)z);
            return true;
        }

        /// <summary>Production/oracle bridge: restrict the decoded canonical
        /// R1 plane to the ACTUAL child loop in owner-local coordinates, then
        /// consume only epoch-valid innovations from its whole-loop family.
        /// No source channel is inferred from a petal's lone R2 anchor.
        /// Different parent-local predictions remain separate; section 10.6
        /// intersects their synthesized roots, not their innovations.</summary>
        internal static ProofClassification PredictChildFromFamily(int3 rootOwner,
            int petalClass, int parentContext, int knotSite,
            float3 decodedNormal, float decodedOffset,
            float normalUncertainty, float offsetUncertainty,
            int childSector, bool plusRoot,
            ReadOnlySpan<PhaseRootEvidence> parentRoots,
            ReadOnlySpan<PhaseRootEvidence> ancestorRoots,
            ReadOnlySpan<MerkabaFlowerDetailRecord> ancestorRecords,
            ReadOnlySpan<MerkabaFlowerDetailKey> ancestorKeys,
            uint currentParentEpoch, out PhaseRootEvidence prediction)
        {
            prediction = default;
            if (!TryGetChildPhaseLoop(petalClass, parentContext, knotSite,
                    out int level, out int3 offset, out int line, out int strandClass,
                    out sbyte endpoint, out sbyte phase, out int inherited) ||
                !TryOwnerJunction(rootOwner, level, offset, out int3 junction))
                return ProofClassification.Ambiguous;
            if (inherited >= 0)
            {
                if (parentRoots.Length != 3 ||
                    !TryPhaseIdentity(parentRoots[inherited], out var inheritedTag) ||
                    inheritedTag.Level != level || inheritedTag.LineClass != line ||
                    math.any(parentRoots[inherited].Symbol.Junction != junction))
                    return ProofClassification.Ambiguous;
                prediction = parentRoots[inherited];
                return prediction.Classification;
            }
            if (!math.all(math.isfinite(decodedNormal)) ||
                !float.IsFinite(decodedOffset) || !float.IsFinite(normalUncertainty) ||
                !float.IsFinite(offsetUncertainty) || normalUncertainty < 0f ||
                offsetUncertainty < 0f ||
                level <= 0 || level >= GeometryLevelCount ||
                (uint)childSector >= LinesValue[line].SectorCount ||
                ancestorRecords.Length != ancestorKeys.Length ||
                ancestorRecords.Length != ancestorRoots.Length ||
                ancestorRecords.Length > level ||
                (ancestorRecords.Length != 0 && currentParentEpoch == 0u))
                return ProofClassification.Ambiguous;

            PhaseFamilyRule family = PhaseFamiliesValue[strandClass];
            NodeRule source = NodesValue[family.RootNode];
            StrandRule strand = StrandsValue[strandClass];
            if (source.Shell != Shell.R2Shape && ancestorRecords.Length != 0)
                return ProofClassification.Ambiguous;
            Span<PhaseTransportTerm> terms = stackalloc PhaseTransportTerm[2];
            int previousLevel = -1;
            for (int i = 0; i < ancestorRecords.Length; i++)
            {
                MerkabaFlowerDetailKey key = ancestorKeys[i];
                int sourceLevel = key.GeometryLevel;
                if (sourceLevel <= previousLevel || sourceLevel >= level ||
                    !TryPhaseIdentity(ancestorRoots[i], out var sourceTag) ||
                    ancestorRoots[i].Classification != ProofClassification.Certain ||
                    ClassifyPhaseSector(ancestorRoots[i], out int sourceSector) !=
                        ProofClassification.Certain || sourceSector != sourceTag.Sector ||
                    sourceTag.Level != sourceLevel || sourceTag.LineClass != line ||
                    key.Channel != line || key.Sector != sourceTag.Sector ||
                    key.RootSign != sourceTag.RootSign)
                    return ProofClassification.Ambiguous;
                previousLevel = sourceLevel;
                int3 sourceOffset;
                sbyte orientation;
                if (sourceLevel == 0)
                {
                    if (key.GeometryChildPath != 0 ||
                        (family.RootIncidentPetals & (1UL << key.PetalClass)) == 0)
                        return ProofClassification.Ambiguous;
                    sourceOffset = source.Direction;
                    orientation = phase;
                }
                else
                {
                    int path = key.PetalClass == strand.Petal0 ? family.FinePath0 :
                        key.PetalClass == strand.Petal1 ? family.FinePath1 : -1;
                    if (path < 0 || key.GeometryChildPath != path)
                        return ProofClassification.Ambiguous;
                    sourceOffset = NodesValue[strand.Node0].Direction +
                        NodesValue[strand.Node1].Direction;
                    orientation = ChildPhaseEdgesValue[(parentContext - 1) * 3 +
                        knotSite - 3].PhaseOrientation;
                }
                if (!TryOwnerJunction(rootOwner, sourceLevel, sourceOffset,
                        out int3 sourceJunction) ||
                    math.any(ancestorRoots[i].Symbol.Junction != sourceJunction) ||
                    !ancestorRecords[i].TryReadR2Phase(key, currentParentEpoch,
                        out int lower, out int upper) ||
                    lower > upper ||
                    (lower <= 0 && upper >= 0 && (lower != 0 || upper != 0)) ||
                    (orientation != -1 && orientation != 1))
                    return ProofClassification.Ambiguous;
                terms[i] = new PhaseTransportTerm(lower, upper, orientation,
                    checked((byte)sourceLevel));
            }
            LoopFrame loop = EvaluateLoop(level,
                new Long3(offset.x, offset.y, offset.z), line);
            Interval3 abc = RestrictPlaneToLoop(float3.zero, decodedNormal,
                decodedOffset, normalUncertainty, offsetUncertainty, loop);
            RootResult basis = EvaluateRoots(abc);
            if (basis.Classification == RootClassification.Impossible ||
                (basis.Classification == RootClassification.CertainTangent && plusRoot))
                return ProofClassification.Impossible;
            if (basis.Classification != RootClassification.CertainSecant &&
                basis.Classification != RootClassification.CertainTangent)
                return ProofClassification.Ambiguous;
            Interval2 root = plusRoot ? basis.Plus : basis.Minus;
            if (ClassifyPlaneSector(level, offset, line, plusRoot, decodedNormal,
                    decodedOffset, root, out int baseSector, out uint boundaryWitness) !=
                    ProofClassification.Certain || baseSector != childSector)
                return ProofClassification.Ambiguous;
            var tag = MerkabaFlowerSymbolTag.Create(level, line, plusRoot,
                childSector, endpoint < 0, 0, MerkabaFlowerSymbolStatus.Confirmed);
            var symbol = new MerkabaFlowerSymbolKey(junction, tag);
            symbol.Tag |= boundaryWitness;
            var current = new PhaseRootEvidence(symbol, root, ProofClassification.Certain);
            for (int i = 0; i < ancestorRecords.Length; i++)
            {
                PhaseTransportTerm term = terms[i];
                FloatInterval residual = DecodePhaseInterval(term.Lower, term.Upper);
                if (term.Orientation < 0)
                    residual = new FloatInterval(-residual.Upper, -residual.Lower);
                if (RotatePhaseEvidence(current, residual, out PhaseRootEvidence rotated) !=
                    ProofClassification.Certain)
                    return ProofClassification.Ambiguous;
                current = rotated;
            }
            prediction = current;
            return ProofClassification.Certain;
        }


        /// <summary>SEAL/BEND over roots from the canonically ordered fixed
        /// endpoints, at any geometric level. This is not peer-evidence merge
        /// and must not intersect the endpoint roots before extracting bend.</summary>
        internal static ProofClassification SealPhaseRelation(
            PhaseRootEvidence first, PhaseRootEvidence second,
            out PhaseRootEvidence relation, out FloatInterval bend)
        {
            relation = default;
            bend = default;
            if (first.Classification != ProofClassification.Certain ||
                second.Classification != ProofClassification.Certain ||
                !TryPhaseIdentity(first, out var tag) ||
                !TryPhaseIdentity(second, out _))
                return ProofClassification.Ambiguous;
            if (math.any(first.Symbol.Junction != second.Symbol.Junction) ||
                (first.Symbol.Tag & 0x1fffu) != (second.Symbol.Tag & 0x1fffu))
                return ProofClassification.Impossible;
            if (ClassifyPhaseSector(first, out int firstSector) != ProofClassification.Certain ||
                ClassifyPhaseSector(second, out int secondSector) != ProofClassification.Certain ||
                firstSector != tag.Sector || secondSector != tag.Sector ||
                !TrySealRootIntervals(first.Root, second.Root, out Interval2 root))
                return ProofClassification.Ambiguous;
            var canonical = MerkabaFlowerSymbolTag.Create(tag.Level,
                tag.LineClass, tag.RootSign, tag.Sector, false, 0u,
                MerkabaFlowerSymbolStatus.Confirmed);
            var symbol = new MerkabaFlowerSymbolKey(first.Symbol.Junction, canonical);
            uint witness = first.Symbol.Tag & BoundaryWitnessMask;
            if (witness == (second.Symbol.Tag & BoundaryWitnessMask))
                symbol.Tag |= witness;
            var candidate = new PhaseRootEvidence(symbol, root, ProofClassification.Certain);
            if (ClassifyPhaseSector(candidate, out int sector) != ProofClassification.Certain ||
                sector != tag.Sector ||
                TangentHalfAngle(root, second.Root, out bend) != ProofClassification.Certain)
                return ProofClassification.Ambiguous;
            relation = candidate;
            return ProofClassification.Certain;
        }

        /// <summary>Evaluate one fixed L0 endpoint pair in the common loop
        /// basis. The caller loads K=(J-r)/2 and Q=K+r in exactly that order.
        /// Position arithmetic is owner-relative; distant or negative tile
        /// coordinates cannot perturb the ABC evidence or its branch.</summary>
        internal static ProofClassification EvaluateCarrierRelation(int3 junction,
            int lineClass, bool rootSign, uint firstPlane, uint secondPlane,
            float normalUncertainty, float offsetUncertainty,
            out PhaseRootEvidence seal, out FloatInterval bend)
        {
            seal = default;
            bend = default;
            if ((uint)lineClass >= LineClassCount ||
                !TryResolveEndpointPair(new Long3(junction.x, junction.y,
                    junction.z), lineClass, out _, out _))
                return ProofClassification.Impossible;
            if (!M8FlowerHasPlane(firstPlane) || !M8FlowerHasPlane(secondPlane) ||
                (firstPlane & M8_FLOWER_OCCUPIED_FLAG) == 0u ||
                (secondPlane & M8_FLOWER_OCCUPIED_FLAG) == 0u ||
                (firstPlane & M8_FLOWER_SEED_FLAG) != 0u ||
                (secondPlane & M8_FLOWER_SEED_FLAG) != 0u ||
                !float.IsFinite(normalUncertainty) ||
                !float.IsFinite(offsetUncertainty) ||
                normalUncertainty < 0f || offsetUncertainty < 0f)
                return ProofClassification.Ambiguous;
            M8FlowerUnpackPlane(firstPlane, out float3 firstNormal,
                out float firstOffset);
            M8FlowerUnpackPlane(secondPlane, out float3 secondNormal,
                out float secondOffset);
            int3 r = LinesValue[lineClass].Direction;
            LoopFrame firstLoop = EvaluateLoop(0, new Long3(r.x, r.y, r.z), lineClass);
            LoopFrame secondLoop = EvaluateLoop(0, new Long3(-r.x, -r.y, -r.z), lineClass);
            RootResult first = EvaluateRoots(RestrictPlaneToLoop(float3.zero,
                firstNormal, firstOffset, normalUncertainty, offsetUncertainty, firstLoop));
            RootResult second = EvaluateRoots(RestrictPlaneToLoop(float3.zero,
                secondNormal, secondOffset, normalUncertainty, offsetUncertainty, secondLoop));
            if (first.Classification == RootClassification.Impossible ||
                second.Classification == RootClassification.Impossible ||
                (rootSign && (first.Classification == RootClassification.CertainTangent ||
                    second.Classification == RootClassification.CertainTangent)))
                return ProofClassification.Impossible;
            if ((first.Classification != RootClassification.CertainSecant &&
                 first.Classification != RootClassification.CertainTangent) ||
                (second.Classification != RootClassification.CertainSecant &&
                 second.Classification != RootClassification.CertainTangent))
                return ProofClassification.Ambiguous;
            Interval2 firstRoot = rootSign ? first.Plus : first.Minus;
            Interval2 secondRoot = rootSign ? second.Plus : second.Minus;
            if (ClassifyPlaneSector(0, r, lineClass, rootSign, firstNormal,
                    firstOffset, firstRoot, out int firstSector, out uint firstWitness) !=
                    ProofClassification.Certain ||
                ClassifyPlaneSector(0, -r, lineClass, rootSign, secondNormal,
                    secondOffset, secondRoot, out int secondSector, out uint secondWitness) !=
                    ProofClassification.Certain)
                return ProofClassification.Ambiguous;
            var firstTag = MerkabaFlowerSymbolTag.Create(0, lineClass, rootSign, firstSector,
                false, 0u, MerkabaFlowerSymbolStatus.Confirmed);
            var secondTag = MerkabaFlowerSymbolTag.Create(0, lineClass, rootSign, secondSector,
                false, 0u, MerkabaFlowerSymbolStatus.Confirmed);
            var firstSymbol = new MerkabaFlowerSymbolKey(junction, firstTag);
            var secondSymbol = new MerkabaFlowerSymbolKey(junction, secondTag);
            firstSymbol.Tag |= firstWitness;
            secondSymbol.Tag |= secondWitness;
            return SealPhaseRelation(
                new PhaseRootEvidence(firstSymbol, firstRoot, ProofClassification.Certain),
                new PhaseRootEvidence(secondSymbol, secondRoot, ProofClassification.Certain),
                out seal, out bend);
        }

        /// <summary>Apply one committed innovation to its regenerated
        /// prediction. Absence is handled by the caller; an invalid/stale
        /// record must never masquerade as an exact-zero innovation.</summary>
        internal static ProofClassification SynthesizePhaseRecord(
            PhaseRootEvidence prediction, MerkabaFlowerDetailKey expectedKey,
            MerkabaFlowerDetailRecord record, uint parentEpoch,
            out PhaseRootEvidence synthesis)
        {
            synthesis = default;
            if (prediction.Classification != ProofClassification.Certain ||
                !TryPhaseIdentity(prediction, out var tag) ||
                expectedKey.GeometryLevel != tag.Level ||
                expectedKey.Channel != tag.LineClass ||
                expectedKey.Sector != tag.Sector ||
                expectedKey.RootSign != tag.RootSign ||
                !record.TryReadR2Phase(expectedKey, parentEpoch,
                    out int lower, out int upper))
                return ProofClassification.Ambiguous;
            if (RotatePhaseEvidence(prediction, DecodePhaseInterval(lower, upper),
                    out PhaseRootEvidence rotated) != ProofClassification.Certain ||
                !IsFinitePhaseRoot(rotated.Root))
                return ProofClassification.Ambiguous;
            synthesis = rotated;
            return ProofClassification.Certain;
        }

        /// <summary>Section 10.6 closes final child roots, never ancestry
        /// programs, parent predictions or residual coefficients. Endpoint
        /// orientation and scratch status are not physical knot identity.</summary>
        internal static ProofClassification CloseSharedPhaseRoot(
            PhaseRootEvidence first, PhaseRootEvidence second,
            out PhaseRootEvidence shared)
        {
            shared = default;
            if (first.Classification != ProofClassification.Certain ||
                second.Classification != ProofClassification.Certain ||
                !TryPhaseIdentity(first, out var tag) ||
                !TryPhaseIdentity(second, out _))
                return ProofClassification.Ambiguous;
            if (math.any(first.Symbol.Junction != second.Symbol.Junction) ||
                (first.Symbol.Tag & 0x1fffu) != (second.Symbol.Tag & 0x1fffu))
                return ProofClassification.Impossible;
            if (ClassifyPhaseSector(first, out int firstSector) != ProofClassification.Certain ||
                ClassifyPhaseSector(second, out int secondSector) != ProofClassification.Certain ||
                firstSector != tag.Sector || secondSector != tag.Sector)
                return ProofClassification.Ambiguous;
            float xLower = math.max(first.Root.X.Lower, second.Root.X.Lower);
            float xUpper = math.min(first.Root.X.Upper, second.Root.X.Upper);
            float yLower = math.max(first.Root.Y.Lower, second.Root.Y.Lower);
            float yUpper = math.min(first.Root.Y.Upper, second.Root.Y.Upper);
            if (xLower > xUpper || yLower > yUpper)
                return ProofClassification.Impossible;
            var root = new Interval2(new FloatInterval(xLower, xUpper),
                new FloatInterval(yLower, yUpper));
            // Canonicalize the derived tag so reversing incidence order
            // cannot change a published shared symbol's scratch bits.
            var canonical = MerkabaFlowerSymbolTag.Create(tag.Level,
                tag.LineClass, tag.RootSign, tag.Sector, false, 0,
                MerkabaFlowerSymbolStatus.Confirmed);
            var symbol = new MerkabaFlowerSymbolKey(first.Symbol.Junction, canonical);
            uint witness = first.Symbol.Tag & BoundaryWitnessMask;
            if (witness == (second.Symbol.Tag & BoundaryWitnessMask))
                symbol.Tag |= witness;
            var candidate = new PhaseRootEvidence(symbol, root, ProofClassification.Certain);
            if (ClassifyPhaseSector(candidate, out int sector) != ProofClassification.Certain ||
                sector != tag.Sector)
                return ProofClassification.Ambiguous;
            shared = candidate;
            return ProofClassification.Certain;
        }

        private static bool IsFinitePhaseRoot(Interval2 root) =>
            math.all(math.isfinite(new float4(root.X.Lower, root.X.Upper,
                root.Y.Lower, root.Y.Upper)));

        private static bool TryPhaseIdentity(PhaseRootEvidence evidence,
            out MerkabaFlowerSymbolTag tag)
        {
            if (!MerkabaFlowerSymbolTag.TryDecode(evidence.Symbol.Tag, out tag) ||
                tag.Level >= GeometryLevelCount || !IsFinitePhaseRoot(evidence.Root))
                return false;
            int3 direction = LinesValue[tag.LineClass].Direction;
            return math.all(((math.asuint(evidence.Symbol.Junction) ^
                math.asuint(direction)) & 1u) == 0u);
        }

        private static bool TryEncodePhaseResidual(FloatInterval residual,
            out int lower, out int upper)
        {
            const double scale = 536870912.0;
            double lo = Math.Floor(residual.Lower * scale);
            double hi = Math.Ceiling(residual.Upper * scale);
            lower = 0;
            upper = 0;
            if (double.IsNaN(lo) || double.IsNaN(hi) ||
                lo < int.MinValue || hi > int.MaxValue || lo > hi) return false;
            lower = (int)lo;
            upper = (int)hi;
            return true;
        }

        /// <summary>Prediction is differenced from direct evidence. Unlike
        /// peer-evidence merge and incidence closure, this operation never
        /// intersects the input phase intervals.</summary>
        internal static PhaseResidualResult AnalyzePhaseResidual(
            PhaseRootEvidence predicted, PhaseRootEvidence observed)
        {
            if (predicted.Classification == ProofClassification.Impossible ||
                observed.Classification == ProofClassification.Impossible)
                return new PhaseResidualResult(PhaseResidualClassification.Impossible);
            if (predicted.Classification != ProofClassification.Certain ||
                observed.Classification != ProofClassification.Certain ||
                !TryPhaseIdentity(predicted, out var p) ||
                !TryPhaseIdentity(observed, out var o))
                return new PhaseResidualResult(PhaseResidualClassification.Ambiguous);

            // Sector admission needs either strict interval containment or an
            // exact symbolic boundary witness, not merely matching tags.
            if (ClassifyPhaseSector(predicted, out int pSector) !=
                    ProofClassification.Certain ||
                ClassifyPhaseSector(observed, out int oSector) !=
                    ProofClassification.Certain)
                return new PhaseResidualResult(PhaseResidualClassification.Ambiguous);
            if (!math.all(predicted.Symbol.Junction == observed.Symbol.Junction) ||
                (predicted.Symbol.Tag & 0x1fffu) !=
                    (observed.Symbol.Tag & 0x1fffu) ||
                pSector != p.Sector || oSector != o.Sector)
                return new PhaseResidualResult(PhaseResidualClassification.Impossible);

            // The exact identity cross(u,u)=0 avoids artificial interval
            // dependency inflation, but only for singleton values. Equal
            // non-singleton boxes do not prove equal physical roots.
            if (predicted.Root.X.IsSingleton && predicted.Root.Y.IsSingleton &&
                observed.Root.X.IsSingleton && observed.Root.Y.IsSingleton &&
                predicted.Root.X.Lower == observed.Root.X.Lower &&
                predicted.Root.Y.Lower == observed.Root.Y.Lower)
                return new PhaseResidualResult(PhaseResidualClassification.ExactZero,
                    FloatInterval.Singleton(0f), predicted.Root);

            if (TangentHalfAngle(predicted.Root, observed.Root, out var residual) !=
                    ProofClassification.Certain ||
                !float.IsFinite(residual.Lower) || !float.IsFinite(residual.Upper))
                return new PhaseResidualResult(PhaseResidualClassification.Ambiguous);
            if (residual.IsSingleton && residual.Lower == 0f)
                return new PhaseResidualResult(PhaseResidualClassification.ExactZero,
                    residual, predicted.Root);
            if (residual.ContainsZero)
                return new PhaseResidualResult(PhaseResidualClassification.Ambiguous,
                    residual);
            if (!TryEncodePhaseResidual(residual, out int lower, out int upper))
                return new PhaseResidualResult(PhaseResidualClassification.Promote,
                    residual);
            if (lower <= 0 && upper >= 0)
                return new PhaseResidualResult(PhaseResidualClassification.Ambiguous,
                    residual);

            // Quantize outward before checking the synthesis: the interval
            // actually persisted must remain representable in its sector.
            FloatInterval stored = DecodePhaseInterval(lower, upper);
            if (RotatePhaseEvidence(predicted, stored, out PhaseRootEvidence synthesis) ==
                ProofClassification.Certain)
                return new PhaseResidualResult(PhaseResidualClassification.CertainNonzero,
                    residual, synthesis.Root, lower, upper);

            // An unresolved enclosure is not proof that another level is
            // required. Promotion is reserved for proven representation failure.
            bool differentSector = IsFinitePhaseRoot(synthesis.Root) &&
                ClassifySector(p.LineClass, synthesis.Root, out int sector) ==
                    ProofClassification.Certain && sector != p.Sector;
            return new PhaseResidualResult(differentSector
                    ? PhaseResidualClassification.Promote
                    : PhaseResidualClassification.Ambiguous,
                residual, synthesis.Root);
        }

        internal static PhaseResidualResult AnalyzeR3PhaseResidual(
            PhaseRootEvidence predicted, PhaseRootEvidence observed,
            int parity, int axis)
        {
            if ((uint)parity >= TetraFrameCount || (uint)axis >= 4u)
                return new PhaseResidualResult(PhaseResidualClassification.Ambiguous);
            var result = AnalyzePhaseResidual(predicted, observed);
            if (result.Classification != PhaseResidualClassification.CertainNonzero &&
                result.Classification != PhaseResidualClassification.ExactZero)
                return result;
            TetraFrameRule frame = TetraFramesValue[parity];
            int line = (int)((predicted.Symbol.Tag >> 3) & 15u);
            if (frame.LineClasses[axis] != line)
                return new PhaseResidualResult(PhaseResidualClassification.Impossible);
            if (frame.Eta[axis] > 0 ||
                result.Classification == PhaseResidualClassification.ExactZero)
                return result;
            var oriented = new FloatInterval(-result.Residual.Upper,
                -result.Residual.Lower);
            if (!TryEncodePhaseResidual(oriented, out int lower, out int upper))
                return new PhaseResidualResult(PhaseResidualClassification.Promote,
                    oriented, result.Synthesis);
            return new PhaseResidualResult(result.Classification, oriented,
                result.Synthesis, lower, upper);
        }

        public static float4 TetraForward(float4 q)
        {
            float scalar = 0.25f * (q.x + q.y + q.z + q.w);
            float3 vector = 0.75f * (q.x * TetraUnit(0) +
                q.y * TetraUnit(1) + q.z * TetraUnit(2) +
                q.w * TetraUnit(3));
            return new float4(scalar, vector);
        }

        // Metric closure must consume the complete q intervals, not the
        // presentation midpoints accepted by the scalar convenience overload.
        internal static void TetraForwardIntervals(ReadOnlySpan<FloatInterval> q,
            out FloatInterval scalar, out Interval3 vector)
        {
            if (q.Length != 4) throw new ArgumentException("Four R3 axes required.", nameof(q));
            FloatInterval sum = FloatInterval.Add(FloatInterval.Add(
                FloatInterval.Add(q[0], q[1]), q[2]), q[3]);
            scalar = FloatInterval.Multiply(sum, FloatInterval.Singleton(0.25f));
            FloatInterval unit = FloatInterval.Enclose(1.0 / Math.Sqrt(3.0));
            Span<FloatInterval> coordinates = stackalloc FloatInterval[3];
            for (int axis = 0; axis < 3; axis++)
            {
                FloatInterval component = FloatInterval.Singleton(0f);
                for (int i = 0; i < 4; i++)
                {
                    FloatInterval direction = TetraAxesInteger[i][axis] > 0
                        ? unit : new FloatInterval(-unit.Upper, -unit.Lower);
                    FloatInterval term = FloatInterval.Multiply(q[i], direction);
                    component = i == 0 ? term : FloatInterval.Add(component, term);
                }
                coordinates[axis] = FloatInterval.Multiply(component,
                    FloatInterval.Singleton(0.75f));
            }
            vector = new Interval3(coordinates[0], coordinates[1], coordinates[2]);
        }

        public static float4 TetraInverse(float scalar, float3 vector) => new(
            scalar + math.dot(TetraUnit(0), vector),
            scalar + math.dot(TetraUnit(1), vector),
            scalar + math.dot(TetraUnit(2), vector),
            scalar + math.dot(TetraUnit(3), vector));

        public static sbyte BranchChirality(int parity, int4 rootSigns)
        {
            if ((uint)parity >= TetraFrameCount)
                throw new ArgumentOutOfRangeException(nameof(parity));
            int product = TetraFramesValue[parity].Chirality;
            for (int i = 0; i < 4; i++)
            {
                int sign = rootSigns[i];
                if (sign != -1 && sign != 1)
                    throw new ArgumentOutOfRangeException(nameof(rootSigns));
                product *= sign;
            }
            return (sbyte)product;
        }

        public static ProofClassification ClassifyHinge(Interval3 first,
            Interval3 second, out Interval2 root)
        {
            FloatInterval vx = FloatInterval.Subtract(
                FloatInterval.Multiply(first.Y, second.Z),
                FloatInterval.Multiply(first.Z, second.Y));
            FloatInterval vy = FloatInterval.Subtract(
                FloatInterval.Multiply(first.Z, second.X),
                FloatInterval.Multiply(first.X, second.Z));
            FloatInterval vz = FloatInterval.Subtract(
                FloatInterval.Multiply(first.X, second.Y),
                FloatInterval.Multiply(first.Y, second.X));
            if (vx.ContainsZero)
            {
                root = default;
                return ProofClassification.Ambiguous;
            }
            root = new Interval2(FloatInterval.Divide(vy, vx),
                FloatInterval.Divide(vz, vx));
            FloatInterval norm = FloatInterval.Add(FloatInterval.Square(root.X),
                FloatInterval.Square(root.Y));
            if (norm.Upper < 1f || norm.Lower > 1f)
                return ProofClassification.Impossible;
            return norm.IsSingleton && norm.Lower == 1f
                ? ProofClassification.Certain
                : ProofClassification.Ambiguous;
        }

        public static void DecodeBarycentric(byte packed, out int a,
            out int b, out int c)
        {
            a = packed & 3;
            b = (packed >> 2) & 3;
            c = (packed >> 4) & 3;
            if (a + b + c != 2)
                throw new InvalidOperationException(
                    "Invalid generated child barycentric address.");
        }

        private static DirectionRule[] BuildDirections()
        {
            var output = new DirectionRule[DirectedRelationCount];
            int index = 0;
            for (byte line = 0; line < CanonicalLinesValue.Length; line++)
            {
                int3 direction = CanonicalLinesValue[line];
                Shell shell = ShellOf(direction);
                output[index++] = new DirectionRule(direction, line, 1, shell);
                output[index++] = new DirectionRule(-direction, line, -1, shell);
            }
            return output;
        }

        private static int3 ChildCoefficients(byte packed)
        {
            DecodeBarycentric(packed, out int a, out int b, out int c);
            return new int3(a, b, c);
        }

        private static int EdgeFirst(int edge) => edge == 1 ? 1 : 0;
        private static int EdgeSecond(int edge) => edge == 0 ? 1 : 2;

        private static int3 PhaseParentNode(PetalRule petal, int context, int node)
        {
            if (context == 0) return NodesValue[petal.Node(node)].Direction;
            int3 weights = ChildCoefficients(ChildPetalsValue[context - 1].Vertex(node));
            return weights.x * NodesValue[petal.FaceNode].Direction +
                weights.y * NodesValue[petal.EdgeNode].Direction +
                weights.z * NodesValue[petal.CornerNode].Direction;
        }

#if UNITY_EDITOR
        private static byte FineFamilyPath(int petalClass, int strandClass)
        {
            PetalRule petal = PetalsValue[petalClass];
            int edge = -1;
            for (int i = 0; i < 3; i++)
                if (petal.Strand(i) == strandClass)
                {
                    if (edge >= 0) throw new InvalidOperationException(
                        "A parent repeats the same strand family.");
                    edge = i;
                }
            if (edge < 0) throw new InvalidOperationException(
                "A strand has no incidence in its declared parent.");
            byte site = (byte)((1 << (2 * EdgeFirst(edge))) |
                (1 << (2 * EdgeSecond(edge))));
            for (byte child = 0; child < ChildPetalCount; child++)
            {
                ChildPetalRule rule = ChildPetalsValue[child];
                if (rule.Vertex0 == site || rule.Vertex1 == site || rule.Vertex2 == site)
                    return child;
            }
            throw new InvalidOperationException("A strand has no generated child incidence.");
        }

        private static PhaseFamilyRule[] BuildPhaseFamilies()
        {
            var output = new PhaseFamilyRule[PhaseFamilyCount];
            for (int strandClass = 0; strandClass < output.Length; strandClass++)
            {
                StrandRule strand = StrandsValue[strandClass];
                int3 p = NodesValue[strand.Node0].Direction;
                int3 q = NodesValue[strand.Node1].Direction;
                int3 direction = q - p;
                int source = -1;
                for (int node = 0; node < NodesValue.Length; node++)
                    if (math.all(NodesValue[node].Direction == direction))
                    {
                        if (source >= 0) throw new InvalidOperationException(
                            "Whole-Flower source channel incidence is not unique.");
                        source = node;
                    }
                if (source < 0) throw new InvalidOperationException(
                    $"Strand {strandClass} has no whole-Flower source channel {direction}.");

                NodeRule family = NodesValue[source];
                int3 childJ = p + q;
                int3 canonical = LinesValue[family.LineClass].Direction;
                if (math.any(((math.asuint(childJ) ^ math.asuint(canonical)) & 1u) != 0u) ||
                    math.any(childJ != direction + 2 * p))
                    throw new InvalidOperationException(
                        "Strand endpoints violate exact loop homothety.");
                // X_child = X_source/2 + a_source*p/2. The positive
                // homothety preserves the canonical E1/E2 phase orientation;
                // endpoint orientation is separate even for negative d.
                sbyte phaseOrientation = checked((sbyte)Math.Sign(Dot(direction,
                    NodesValue[source].Direction)));
                ulong incident = 0;
                for (int petal = 0; petal < PetalsValue.Length; petal++)
                    for (int node = 0; node < 3; node++)
                        if (PetalsValue[petal].Node(node) == source)
                            incident |= 1UL << petal;
                if (incident == 0 || phaseOrientation != 1)
                    throw new InvalidOperationException(
                        "A source family has no positively oriented generated incidence.");
                output[strandClass] = new PhaseFamilyRule(checked((byte)source),
                    phaseOrientation, FineFamilyPath(strand.Petal0, strandClass),
                    FineFamilyPath(strand.Petal1, strandClass), incident);
            }
            return output;
        }

        private static ChildPhaseEdgeRule[] BuildChildPhaseEdges()
        {
            var output = new ChildPhaseEdgeRule[ChildPhaseEdgeCount];
            int3[] families = { new(-1, 1, 0), new(0, -1, 1), new(-1, 0, 1) };
            for (int child = 0; child < ChildPetalCount; child++)
                for (int edge = 0; edge < 3; edge++)
                {
                    ChildPetalRule rule = ChildPetalsValue[child];
                    int3 difference = ChildCoefficients(rule.Vertex(EdgeSecond(edge))) -
                        ChildCoefficients(rule.Vertex(EdgeFirst(edge)));
                    int found = -1;
                    sbyte endpoint = 0;
                    for (int family = 0; family < families.Length; family++)
                    {
                        int sign = math.all(difference == families[family]) ? 1 :
                            math.all(difference == -families[family]) ? -1 : 0;
                        if (sign == 0) continue;
                        if (found >= 0) throw new InvalidOperationException(
                            "A child edge has competing parent channel families.");
                        found = family;
                        endpoint = checked((sbyte)sign);
                    }
                    if (found < 0) throw new InvalidOperationException(
                        "A child edge is not an exact translated parent channel.");
                    int3 oriented = endpoint * difference;
                    sbyte phase = checked((sbyte)Math.Sign(Dot(oriented, families[found])));
                    // Check all 48 spatial flags, not just the abstract
                    // barycentric pattern. Every child loop is the same-line
                    // half-sized translated source loop.
                    foreach (PetalRule petal in PetalsValue)
                    {
                        int3 u = PhaseParentNode(petal, child + 1, EdgeFirst(edge));
                        int3 v = PhaseParentNode(petal, child + 1, EdgeSecond(edge));
                        int3 p = PhaseParentNode(petal, 0, EdgeFirst(found));
                        int3 q = PhaseParentNode(petal, 0, EdgeSecond(found));
                        if (endpoint < 0) (u, v) = (v, u);
                        if (math.any(v - u != q - p) || math.any(u - p != v - q) ||
                            math.any(u + v != p + q + 2 * (u - p)))
                            throw new InvalidOperationException(
                                "A child loop violates exact whole-family homothety.");
                    }
                    output[child * 3 + edge] = new ChildPhaseEdgeRule(
                        checked((byte)found), endpoint, phase);
                }
            return output;
        }
#endif

        private static void ValidateFrozenTables()
        {
            if (DirectionsValue.Length != DirectedRelationCount ||
                NodesValue.Length != NodeClassCount ||
                StrandsValue.Length != StrandClassCount ||
                PetalsValue.Length != PetalClassCount ||
                LinesValue.Length != LineClassCount ||
                TetraFramesValue.Length != TetraFrameCount ||
                PhaseFamiliesValue.Length != PhaseFamilyCount ||
                ChildPhaseEdgesValue.Length != ChildPhaseEdgeCount)
                throw new InvalidOperationException(
                    "Frozen Sphere-Flower table cardinality is invalid.");
            for (int i = 0; i < LinesValue.Length; i++)
            {
                LineRule line = LinesValue[i];
                if (line.SectorCount == 0 ||
                    line.SectorCount > MaximumSectorCount ||
                    line.SectorOffset + line.SectorCount >
                    SectorBoundariesValue.Length)
                    throw new InvalidOperationException(
                        $"Frozen line {i} has an invalid sector span.");
            }
        }

#if UNITY_EDITOR
        // Exact O_h action on the production tables. Codegen executes this;
        // no symmetry table, matrix or search is added to the GPU hot path.
        public static int ValidateCubeSymmetry()
        {
            var nodes = new int[NodeClassCount];
            var petals = new int[PetalClassCount];
            var strands = new int[StrandClassCount];
            int count = 0;
            ulong orbit = 0;
            void Require(bool condition)
            {
                if (!condition) throw new InvalidOperationException(
                    $"Sphere-Flower O_h equivariance failed at transform {count}.");
            }
            for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
            {
                if (x == y) continue;
                int z = 3 - x - y;
                for (int signs = 0; signs < 8; signs++)
                {
                    int3 s = new((signs & 1) == 0 ? 1 : -1,
                        (signs & 2) == 0 ? 1 : -1, (signs & 4) == 0 ? 1 : -1);
                    int3 Map(int3 v) => s * new int3(v[x], v[y], v[z]);
                    int determinant = Determinant(Map(new int3(1, 0, 0)),
                        Map(new int3(0, 1, 0)), Map(new int3(0, 0, 1)));
                    Require(Math.Abs(determinant) == 1);
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        int3 d = Map(NodesValue[n].Direction);
                        nodes[n] = FindNode(NodesValue, d);
                        Require(NodesValue[nodes[n]].Shell == NodesValue[n].Shell &&
                            Dot(d, d) == (int)NodesValue[n].Shell &&
                            FindLineClass(d, out _) == NodesValue[nodes[n]].LineClass);
                    }
                    for (int p = 0; p < petals.Length; p++)
                    {
                        PetalRule a = PetalsValue[p];
                        int mapped = -1;
                        for (int q = 0; q < petals.Length; q++)
                        {
                            PetalRule b = PetalsValue[q];
                            if (b.FaceNode == nodes[a.FaceNode] &&
                                b.EdgeNode == nodes[a.EdgeNode] &&
                                b.CornerNode == nodes[a.CornerNode])
                            { Require(mapped < 0); mapped = q; }
                        }
                        Require(mapped >= 0);
                        petals[p] = mapped;
                        Require(PetalsValue[mapped].Orientation == determinant * a.Orientation);
                        // Integer child substitution commutes with every
                        // rotation/reflection, including inherited anchors.
                        for (int context = 0; context < 5; context++)
                        for (int corner = 0; corner < 3; corner++)
                            Require(math.all(Map(PhaseParentNode(a, context, corner)) ==
                                PhaseParentNode(PetalsValue[mapped], context, corner)));
                    }
                    orbit |= 1UL << petals[0];
                    for (int e = 0; e < strands.Length; e++)
                    {
                        StrandRule a = StrandsValue[e];
                        int low = Math.Min(nodes[a.Node0], nodes[a.Node1]);
                        int high = Math.Max(nodes[a.Node0], nodes[a.Node1]);
                        int mapped = -1;
                        for (int q = 0; q < strands.Length; q++)
                        {
                            StrandRule b = StrandsValue[q];
                            if (b.Node0 == low && b.Node1 == high && b.IncidenceKind == a.IncidenceKind)
                            { Require(mapped < 0); mapped = q; }
                        }
                        Require(mapped >= 0);
                        strands[e] = mapped;
                        StrandRule target = StrandsValue[mapped];
                        Require(((1UL << petals[a.Petal0]) | (1UL << petals[a.Petal1])) ==
                            ((1UL << target.Petal0) | (1UL << target.Petal1)));
                    }
                    for (int p = 0; p < petals.Length; p++)
                    for (int edge = 0; edge < 3; edge++)
                    {
                        PetalRule a = PetalsValue[p], b = PetalsValue[petals[p]];
                        StrandRule strand = StrandsValue[a.Strand(edge)];
                        int orientation = nodes[strand.Node0] < nodes[strand.Node1] ? 1 : -1;
                        Require(b.Strand(edge) == strands[a.Strand(edge)] &&
                            b.StrandSign(edge) == determinant * orientation * a.StrandSign(edge));
                    }
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        ulong mapped = 0, source = NodeIncidentPetalsValue[n];
                        for (int p = 0; p < petals.Length; p++)
                            if ((source & (1UL << p)) != 0) mapped |= 1UL << petals[p];
                        Require(mapped == NodeIncidentPetalsValue[nodes[n]]);
                    }
                    count++;
                }
            }
            Require(count == 48 && orbit == (1UL << PetalClassCount) - 1UL);
            return count;
        }
#endif

        private static void BuildPetalsAndStrands(NodeRule[] nodes,
            out PetalRule[] petals, out StrandRule[] strands)
        {
            int3[] axes = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
            var temporaryPetals = new List<TemporaryPetal>(PetalClassCount);
            for (int faceAxis = 0; faceAxis < 3; faceAxis++)
            for (int faceSign = -1; faceSign <= 1; faceSign += 2)
            {
                int3 face = axes[faceAxis] * faceSign;
                for (int edgeAxis = 0; edgeAxis < 3; edgeAxis++)
                {
                    if (edgeAxis == faceAxis) continue;
                    for (int edgeSign = -1; edgeSign <= 1; edgeSign += 2)
                    {
                        int3 edge = face + axes[edgeAxis] * edgeSign;
                        int cornerAxis = 3 - faceAxis - edgeAxis;
                        for (int cornerSign = -1; cornerSign <= 1;
                             cornerSign += 2)
                        {
                            int3 corner = edge + axes[cornerAxis] * cornerSign;
                            int determinant = Determinant(face, edge, corner);
                            if (Math.Abs(determinant) != 1)
                                throw new InvalidOperationException(
                                    "A generated flag is degenerate.");
                            temporaryPetals.Add(new TemporaryPetal(
                                FindNode(nodes, face), FindNode(nodes, edge),
                                FindNode(nodes, corner),
                                (sbyte)Math.Sign(determinant)));
                        }
                    }
                }
            }

            var temporaryStrands = new List<TemporaryStrand>(StrandClassCount);
            var lookup = new Dictionary<int, int>(StrandClassCount);
            for (int petal = 0; petal < temporaryPetals.Count; petal++)
            {
                TemporaryPetal value = temporaryPetals[petal];
                value.Strand0 = checked((byte)AddStrand(value.FaceNode,
                    value.EdgeNode, 0, petal, temporaryStrands, lookup));
                value.Strand1 = checked((byte)AddStrand(value.EdgeNode,
                    value.CornerNode, 1, petal, temporaryStrands, lookup));
                value.Strand2 = checked((byte)AddStrand(value.CornerNode,
                    value.FaceNode, 2, petal, temporaryStrands, lookup));
                temporaryPetals[petal] = value;
            }

            petals = new PetalRule[temporaryPetals.Count];
            for (int i = 0; i < petals.Length; i++)
            {
                TemporaryPetal value = temporaryPetals[i];
                petals[i] = new PetalRule(value.FaceNode, value.EdgeNode,
                    value.CornerNode, value.Strand0, value.Strand1,
                    value.Strand2, value.Orientation);
            }

            strands = new StrandRule[temporaryStrands.Count];
            for (int i = 0; i < strands.Length; i++)
            {
                TemporaryStrand value = temporaryStrands[i];
                if (value.Petal0 < 0 || value.Petal1 < 0)
                    throw new InvalidOperationException(
                        "A generated strand lacks its two incident petals.");
                strands[i] = new StrandRule(value.Node0, value.Node1,
                    value.IncidenceKind, (byte)value.Petal0,
                    (byte)value.Petal1);
            }
        }

        private static int AddStrand(byte node0, byte node1, byte kind,
            int petal, List<TemporaryStrand> strands,
            Dictionary<int, int> lookup)
        {
            int low = Math.Min(node0, node1);
            int high = Math.Max(node0, node1);
            int key = low | (high << 8) | (kind << 16);
            if (!lookup.TryGetValue(key, out int index))
            {
                index = strands.Count;
                lookup.Add(key, index);
                strands.Add(new TemporaryStrand((byte)low, (byte)high,
                    kind, petal));
            }
            else
            {
                TemporaryStrand value = strands[index];
                if (value.Petal1 >= 0)
                    throw new InvalidOperationException(
                        "A generated strand has more than two petals.");
                value.Petal1 = petal;
                strands[index] = value;
            }
            return index;
        }

        private static void BuildSectorBoundaries(PetalRule[] petals,
            NodeRule[] nodes, out LineRule[] lines,
            out SectorBoundary[] allBoundaries)
        {
            var boundaries = new List<SectorBoundary>(192);
            lines = new LineRule[LineClassCount];
            for (int lineClass = 0; lineClass < LineClassCount; lineClass++)
            {
                int3 line = CanonicalLinesValue[lineClass];
                List<ExactBoundaryPoint> exact = BuildLineBoundaryPoints(
                    petals, nodes, line);
                ComputeBasis(line, out float3 unit, out float3 e1,
                    out float3 e2);
                ushort offset = checked((ushort)boundaries.Count);
                foreach (ExactBoundaryPoint point in exact)
                {
                    point.ToLoopUnit(line, e1, e2, out float2 value,
                        out Interval2 enclosure);
                    boundaries.Add(new SectorBoundary(value, enclosure));
                }
                lines[lineClass] = new LineRule(line, ShellOf(line), unit,
                    e1, e2, offset, checked((byte)exact.Count));
            }
            allBoundaries = boundaries.ToArray();
        }

        // The exact cuts are retained through this shared builder so sector
        // provenance can use the same algebraic points as phase classification.
        // Their sorted order is never reconstructed by angular sampling.
        private static List<ExactBoundaryPoint> BuildLineBoundaryPoints(
            PetalRule[] petals, NodeRule[] nodes, int3 line)
        {
            var incident = new List<int3>(18);
            for (int petalIndex = 0; petalIndex < petals.Length; petalIndex++)
            {
                PetalRule petal = petals[petalIndex];
                bool contains = false;
                for (int nodeIndex = 0; nodeIndex < 3; nodeIndex++)
                {
                    int3 direction = nodes[petal.Node(nodeIndex)].Direction;
                    if (math.all(direction == line) ||
                        math.all(direction == -line)) contains = true;
                }
                if (!contains) continue;
                for (int nodeIndex = 0; nodeIndex < 3; nodeIndex++)
                    AddUnique(incident,
                        nodes[petal.Node(nodeIndex)].Direction);
            }

            var exact = new List<ExactBoundaryPoint>(32);
            foreach (int3 plane in incident)
                BuildBoundaryPoints(line, plane, exact);
            AddAnchorFlagOrderCuts(petals, nodes, line, exact);
            exact.Sort((left, right) => CompareAroundLine(line,
                left, right));
            for (int i = exact.Count - 1; i > 0; i--)
            {
                if (ExactBoundaryPoint.SquaredDistanceSign(exact[i - 1],
                        exact[i]) == 0)
                    exact.RemoveAt(i);
            }
            if (exact.Count > 1 &&
                ExactBoundaryPoint.SquaredDistanceSign(exact[0],
                    exact[exact.Count - 1]) == 0)
                exact.RemoveAt(exact.Count - 1);
            if (exact.Count == 0 || exact.Count > MaximumSectorCount)
                throw new InvalidOperationException(
                    $"Line {line} generated {exact.Count} sectors.");
            return exact;
        }

        private static TetraFrameRule[] BuildTetraFrames()
        {
            var frames = new TetraFrameRule[TetraFrameCount];
            for (int parity = 0; parity < frames.Length; parity++)
            {
                int3 paritySign = new((parity & 1) == 0 ? 1 : -1,
                    (parity & 2) == 0 ? 1 : -1,
                    (parity & 4) == 0 ? 1 : -1);
                var actual = new int3[4];
                var classes = new int4();
                var eta = new int4();
                for (int i = 0; i < 4; i++)
                {
                    actual[i] = TetraAxesInteger[i] * paritySign;
                    classes[i] = FindLineClass(actual[i], out sbyte sign);
                    eta[i] = sign;
                }
                int chirality = Math.Sign(Determinant(actual[1] - actual[0],
                    actual[2] - actual[0], actual[3] - actual[0]));
                if (chirality == 0)
                    throw new InvalidOperationException(
                        "Generated tetra frame is degenerate.");
                frames[parity] = new TetraFrameRule(classes, eta,
                    (sbyte)chirality);
            }
            return frames;
        }

        private static void BuildBoundaryPoints(int3 line, int3 plane,
            List<ExactBoundaryPoint> output)
        {
            BuildBoundaryPoints(line, plane, new Rational(Dot(plane, plane), 2),
                output);
        }

        private static void BuildBoundaryPoints(int3 line, int3 plane,
            Rational planeOffset, List<ExactBoundaryPoint> output)
        {
            int3 direction = Cross(line, plane);
            int directionSquared = Dot(direction, direction);
            if (directionSquared == 0) return;

            Rational lineOffset = new(Dot(line, line), 2);
            int3 planeCross = Cross(plane, direction);
            int3 directionCross = Cross(direction, line);
            Rational3 origin = (Scale(lineOffset, planeCross) +
                Scale(planeOffset, directionCross)) / directionSquared;
            Rational radiusSquared = new(Dot(line, line));
            Rational tSquared = (radiusSquared - Dot(origin, origin)) /
                directionSquared;
            int sign = tSquared.Sign;
            if (sign < 0) return;
            if (sign == 0)
            {
                AddUnique(output, new ExactBoundaryPoint(origin, direction,
                    Rational.Zero, 0));
                return;
            }
            AddUnique(output, new ExactBoundaryPoint(origin, direction,
                tSquared, -1));
            AddUnique(output, new ExactBoundaryPoint(origin, direction,
                tSquared, 1));
        }

        private static void AddUnique(List<ExactBoundaryPoint> points,
            ExactBoundaryPoint candidate)
        {
            foreach (ExactBoundaryPoint point in points)
                if (ExactBoundaryPoint.SquaredDistanceSign(point,
                        candidate) == 0) return;
            points.Add(candidate);
        }

        private static int CompareAroundLine(int3 line,
            ExactBoundaryPoint left, ExactBoundaryPoint right)
        {
            BuildIntegerBasis(line, out int3 u, out int3 v);
            LinearSurd leftX = left.RadialDot(line, u);
            LinearSurd leftY = left.RadialDot(line, v);
            LinearSurd rightX = right.RadialDot(line, u);
            LinearSurd rightY = right.RadialDot(line, v);
            int leftHalf = HalfPlane(leftX, leftY);
            int rightHalf = HalfPlane(rightX, rightY);
            if (leftHalf != rightHalf) return leftHalf.CompareTo(rightHalf);
            Biquadratic cross = LinearSurd.Multiply(leftX, rightY) -
                LinearSurd.Multiply(leftY, rightX);
            int sign = cross.Sign;
            if (sign == 0)
            {
                if (ExactBoundaryPoint.SquaredDistanceSign(left, right) == 0)
                    return 0;
                throw new InvalidOperationException(
                    "Distinct sector roots have no strict cyclic order.");
            }
            return sign > 0 ? -1 : 1;
        }

        private static int HalfPlane(LinearSurd x, LinearSurd y)
        {
            int ySign = y.Sign;
            if (ySign > 0) return 0;
            if (ySign < 0) return 1;
            return x.Sign >= 0 ? 0 : 1;
        }

        private static void BuildIntegerBasis(int3 line, out int3 u,
            out int3 v)
        {
            int axis = LeastAlignedAxis(line);
            int3 basis = axis switch
            {
                0 => new int3(1, 0, 0),
                1 => new int3(0, 1, 0),
                _ => new int3(0, 0, 1)
            };
            int squared = Dot(line, line);
            u = squared * basis - line[axis] * line;
            v = Cross(line, u);
        }

        private static void ComputeBasis(int3 line, out float3 unit,
            out float3 e1, out float3 e2)
        {
            int axis = LeastAlignedAxis(line);
            float3 basis = axis switch
            {
                0 => new float3(1f, 0f, 0f),
                1 => new float3(0f, 1f, 0f),
                _ => new float3(0f, 0f, 1f)
            };
            unit = math.normalize((float3)line);
            e1 = math.normalize(basis - math.dot(basis, unit) * unit);
            e2 = math.cross(unit, e1);
            unit = CanonicalizeZero(unit);
            e1 = CanonicalizeZero(e1);
            e2 = CanonicalizeZero(e2);
        }

        private static int LeastAlignedAxis(int3 line)
        {
            int axis = 0;
            int value = Math.Abs(line.x);
            int y = Math.Abs(line.y);
            int z = Math.Abs(line.z);
            if (y < value) { axis = 1; value = y; }
            if (z < value) axis = 2;
            return axis;
        }

        private static FloatInterval Cross(Interval2 left, Interval2 right) =>
            FloatInterval.Subtract(FloatInterval.Multiply(left.X, right.Y),
                FloatInterval.Multiply(left.Y, right.X));

        private static FloatInterval Dot(Interval2 left, Interval2 right) =>
            FloatInterval.Add(FloatInterval.Multiply(left.X, right.X),
                FloatInterval.Multiply(left.Y, right.Y));

        private static float3 TetraUnit(int index) =>
            (float3)TetraAxesInteger[index] * (1f / math.sqrt(3f));

        private static double Gamma(int operations)
        {
            double product = operations * FloatUnitRoundoff;
            return product / (1.0 - product);
        }

        private static byte PackBarycentric(int a, int b, int c) =>
            checked((byte)(a | (b << 2) | (c << 4)));

        private static int FindNode(NodeRule[] nodes, int3 direction)
        {
            for (int i = 0; i < nodes.Length; i++)
                if (math.all(nodes[i].Direction == direction)) return i;
            throw new InvalidOperationException("Generated node is missing.");
        }

        private static void AddUnique(List<int3> values, int3 candidate)
        {
            foreach (int3 value in values)
                if (math.all(value == candidate)) return;
            values.Add(candidate);
        }

        private static void ValidateDirection(int3 direction)
        {
            if (math.all(direction == 0) ||
                math.any(direction < -1) || math.any(direction > 1))
                throw new ArgumentOutOfRangeException(nameof(direction));
        }

        private static int Dot(int3 left, int3 right) =>
            left.x * right.x + left.y * right.y + left.z * right.z;

        private static Rational Dot(Rational3 left, Rational3 right) =>
            left.X * right.X + left.Y * right.Y + left.Z * right.Z;

        private static Rational3 Scale(Rational scale, int3 value) => new(
            scale * value.x, scale * value.y, scale * value.z);

        private static int3 Cross(int3 left, int3 right) => new(
            left.y * right.z - left.z * right.y,
            left.z * right.x - left.x * right.z,
            left.x * right.y - left.y * right.x);

        private static int Determinant(int3 a, int3 b, int3 c) =>
            Dot(a, Cross(b, c));

        private static bool ExactSquaredEquality(float a, float b, float c)
        {
            if (!float.IsFinite(a) || !float.IsFinite(b) || !float.IsFinite(c))
                return false;
            DyadicSquare left = MakeDyadicSquare(a);
            DyadicSquare first = MakeDyadicSquare(b);
            DyadicSquare second = MakeDyadicSquare(c);
            if (!first.Nonzero && !second.Nonzero) return !left.Nonzero;
            if (!left.Nonzero) return false;
            if (!first.Nonzero)
                return left.Exponent == second.Exponent &&
                    left.Coefficient == second.Coefficient;
            if (!second.Nonzero)
                return left.Exponent == first.Exponent &&
                    left.Coefficient == first.Coefficient;
            if (first.Exponent == second.Exponent)
            {
                ulong sum = first.Coefficient + second.Coefficient;
                return left.Exponent == first.Exponent + 1 &&
                    left.Coefficient == (sum >> 1);
            }

            DyadicSquare low = first.Exponent < second.Exponent
                ? first : second;
            DyadicSquare high = first.Exponent < second.Exponent
                ? second : first;
            int difference = high.Exponent - low.Exponent;
            if (left.Exponent != low.Exponent || difference >= 64 ||
                BitLength(high.Coefficient) + difference >
                BitLength(left.Coefficient)) return false;
            return left.Coefficient == low.Coefficient +
                (high.Coefficient << difference);
        }

        private static DyadicSquare MakeDyadicSquare(float value)
        {
            uint bits = math.asuint(math.abs(value));
            uint exponentBits = (bits >> 23) & 0xffu;
            uint mantissa = bits & 0x7fffffu;
            int exponent;
            if (exponentBits == 0u)
            {
                if (mantissa == 0u) return default;
                exponent = -149;
            }
            else
            {
                mantissa |= 0x800000u;
                exponent = (int)exponentBits - 127 - 23;
            }
            int trailing = TrailingZeroCount(mantissa);
            mantissa >>= trailing;
            exponent += trailing;
            return new DyadicSquare((ulong)mantissa * mantissa,
                exponent * 2);
        }

        private static int TrailingZeroCount(uint value)
        {
            int count = 0;
            while ((value & 1u) == 0u)
            {
                value >>= 1;
                count++;
            }
            return count;
        }

        private static int BitLength(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value >>= 1;
                count++;
            }
            return count;
        }

        private static float3 CanonicalizeZero(float3 value) => new(
            value.x == 0f ? 0f : value.x,
            value.y == 0f ? 0f : value.y,
            value.z == 0f ? 0f : value.z);

        private static uint HashByte(uint hash, byte value) =>
            (hash ^ value) * 16777619u;

        private readonly struct DyadicSquare
        {
            internal readonly ulong Coefficient;
            internal readonly int Exponent;
            internal readonly bool Nonzero;

            internal DyadicSquare(ulong coefficient, int exponent)
            {
                Coefficient = coefficient;
                Exponent = exponent;
                Nonzero = coefficient != 0;
            }
        }

        private static float PreviousFloat(float value)
        {
            if (float.IsNegativeInfinity(value)) return value;
            if (value == 0f) return BitConverter.Int32BitsToSingle(
                unchecked((int)0x80000001u));
            int bits = BitConverter.SingleToInt32Bits(value);
            bits += value > 0f ? -1 : 1;
            return BitConverter.Int32BitsToSingle(bits);
        }

        private static float NextFloat(float value)
        {
            if (float.IsPositiveInfinity(value)) return value;
            if (value == 0f) return BitConverter.Int32BitsToSingle(1);
            int bits = BitConverter.SingleToInt32Bits(value);
            bits += value > 0f ? 1 : -1;
            return BitConverter.Int32BitsToSingle(bits);
        }

        private struct TemporaryPetal
        {
            internal readonly byte FaceNode;
            internal readonly byte EdgeNode;
            internal readonly byte CornerNode;
            internal readonly sbyte Orientation;
            internal byte Strand0;
            internal byte Strand1;
            internal byte Strand2;

            internal TemporaryPetal(int faceNode, int edgeNode, int cornerNode,
                sbyte orientation)
            {
                FaceNode = checked((byte)faceNode);
                EdgeNode = checked((byte)edgeNode);
                CornerNode = checked((byte)cornerNode);
                Orientation = orientation;
                Strand0 = Strand1 = Strand2 = 0;
            }
        }

        private struct TemporaryStrand
        {
            internal readonly byte Node0;
            internal readonly byte Node1;
            internal readonly byte IncidenceKind;
            internal readonly int Petal0;
            internal int Petal1;

            internal TemporaryStrand(byte node0, byte node1,
                byte incidenceKind, int petal0)
            {
                Node0 = node0;
                Node1 = node1;
                IncidenceKind = incidenceKind;
                Petal0 = petal0;
                Petal1 = -1;
            }
        }

        private readonly struct Rational : IComparable<Rational>
        {
            internal static readonly Rational Zero = new(0);
            internal readonly BigInteger Numerator;
            internal readonly BigInteger Denominator;

            internal Rational(BigInteger numerator, BigInteger denominator)
            {
                if (denominator.IsZero) throw new DivideByZeroException();
                if (denominator.Sign < 0)
                {
                    numerator = -numerator;
                    denominator = -denominator;
                }
                BigInteger divisor = BigInteger.GreatestCommonDivisor(
                    BigInteger.Abs(numerator), denominator);
                Numerator = numerator / divisor;
                Denominator = denominator / divisor;
            }

            internal Rational(int value) : this(value, 1) { }
            internal Rational(int numerator, int denominator) :
                this((BigInteger)numerator, denominator) { }

            internal int Sign => Numerator.Sign;
            internal double Value => (double)Numerator / (double)Denominator;

            public int CompareTo(Rational other) =>
                (Numerator * other.Denominator).CompareTo(
                    other.Numerator * Denominator);

            public static Rational operator +(Rational left, Rational right) =>
                new(left.Numerator * right.Denominator +
                    right.Numerator * left.Denominator,
                    left.Denominator * right.Denominator);

            public static Rational operator -(Rational left, Rational right) =>
                new(left.Numerator * right.Denominator -
                    right.Numerator * left.Denominator,
                    left.Denominator * right.Denominator);

            public static Rational operator -(Rational value) =>
                new(-value.Numerator, value.Denominator);

            public static Rational operator *(Rational left, Rational right) =>
                new(left.Numerator * right.Numerator,
                    left.Denominator * right.Denominator);

            public static Rational operator *(Rational left, int right) =>
                new(left.Numerator * right, left.Denominator);

            public static Rational operator *(int left, Rational right) =>
                right * left;

            public static Rational operator /(Rational left, Rational right) =>
                new(left.Numerator * right.Denominator,
                    left.Denominator * right.Numerator);

            public static Rational operator /(Rational left, int right) =>
                new(left.Numerator, left.Denominator * right);
        }

        private readonly struct Rational3
        {
            internal readonly Rational X;
            internal readonly Rational Y;
            internal readonly Rational Z;

            internal Rational3(Rational x, Rational y, Rational z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            internal Rational this[int index] => index switch
            {
                0 => X,
                1 => Y,
                2 => Z,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            public static Rational3 operator +(Rational3 left,
                Rational3 right) => new(left.X + right.X,
                left.Y + right.Y, left.Z + right.Z);

            public static Rational3 operator -(Rational3 left,
                Rational3 right) => new(left.X - right.X,
                left.Y - right.Y, left.Z - right.Z);

            public static Rational3 operator /(Rational3 value, int divisor) =>
                new(value.X / divisor, value.Y / divisor, value.Z / divisor);
        }

        private readonly struct LinearSurd
        {
            internal readonly Rational A;
            internal readonly Rational B;
            internal readonly Rational Radicand;

            internal LinearSurd(Rational a, Rational b, Rational radicand)
            {
                A = a;
                B = b;
                Radicand = radicand;
            }

            internal int Sign => QuadraticSign(A, B, Radicand);

            internal static Biquadratic Multiply(LinearSurd left,
                LinearSurd right) => new(
                left.A * right.A,
                left.B * right.A,
                left.A * right.B,
                left.B * right.B,
                left.Radicand, right.Radicand);
        }

        private readonly struct Biquadratic
        {
            internal readonly Rational A;
            internal readonly Rational B;
            internal readonly Rational C;
            internal readonly Rational D;
            internal readonly Rational S;
            internal readonly Rational T;

            internal Biquadratic(Rational a, Rational b, Rational c,
                Rational d, Rational s, Rational t)
            {
                A = a; B = b; C = c; D = d; S = s; T = t;
            }

            internal int Sign => BiquadraticSign(this);

            public static Biquadratic operator -(Biquadratic left,
                Biquadratic right)
            {
                if (left.S.CompareTo(right.S) != 0 ||
                    left.T.CompareTo(right.T) != 0)
                    throw new InvalidOperationException(
                        "Biquadratic bases differ.");
                return new Biquadratic(left.A - right.A,
                    left.B - right.B, left.C - right.C,
                    left.D - right.D, left.S, left.T);
            }
        }

        private readonly struct ExactBoundaryPoint
        {
            internal readonly Rational3 Origin;
            internal readonly int3 Direction;
            internal readonly Rational Radicand;
            internal readonly int RootSign;

            internal ExactBoundaryPoint(Rational3 origin, int3 direction,
                Rational radicand, int rootSign)
            {
                Origin = origin;
                Direction = direction;
                Radicand = radicand;
                RootSign = rootSign;
            }

            internal LinearSurd RadialDot(int3 line, int3 vector)
            {
                Rational half = new(1, 2);
                Rational rational = (Origin.X - half * line.x) * vector.x +
                    (Origin.Y - half * line.y) * vector.y +
                    (Origin.Z - half * line.z) * vector.z;
                Rational irrational = new(RootSign * Dot(Direction, vector));
                return new LinearSurd(rational, irrational, Radicand);
            }

            internal void ToLoopUnit(int3 line, float3 e1, float3 e2,
                out float2 value, out Interval2 enclosure)
            {
                double root = Math.Sqrt(Radicand.Value);
                double3 point = new(Origin.X.Value + RootSign * Direction.x * root,
                    Origin.Y.Value + RootSign * Direction.y * root,
                    Origin.Z.Value + RootSign * Direction.z * root);
                double3 radial = point - (double3)line * 0.5;
                double radius = Math.Sqrt(3.0) * 0.5 *
                    Math.Sqrt(Dot(line, line));
                double x = math.dot(radial, (double3)e1) / radius;
                double y = math.dot(radial, (double3)e2) / radius;
                if (x == 0.0) x = 0.0;
                if (y == 0.0) y = 0.0;
                value = new float2((float)x, (float)y);
                enclosure = new Interval2(FloatInterval.Enclose(x),
                    FloatInterval.Enclose(y));
            }

            internal static int SquaredDistanceSign(ExactBoundaryPoint left,
                ExactBoundaryPoint right)
            {
                Rational3 difference = left.Origin - right.Origin;
                Rational a = Dot(difference, difference) +
                    left.Radicand * Dot(left.Direction, left.Direction) +
                    right.Radicand * Dot(right.Direction, right.Direction);
                Rational b = 2 * left.RootSign *
                    (difference.X * left.Direction.x +
                     difference.Y * left.Direction.y +
                     difference.Z * left.Direction.z);
                Rational c = -2 * right.RootSign *
                    (difference.X * right.Direction.x +
                     difference.Y * right.Direction.y +
                     difference.Z * right.Direction.z);
                Rational d = new(-2 * left.RootSign * right.RootSign *
                    Dot(left.Direction, right.Direction));
                return new Biquadratic(a, b, c, d, left.Radicand,
                    right.Radicand).Sign;
            }
        }

        private static int QuadraticSign(Rational a, Rational b,
            Rational radicand)
        {
            if (b.Sign == 0 || radicand.Sign == 0) return a.Sign;
            if (a.Sign == 0) return b.Sign;
            if (a.Sign == b.Sign) return a.Sign;
            int comparison = (a * a).CompareTo(b * b * radicand);
            if (comparison == 0) return 0;
            return a.Sign > 0 ? comparison : -comparison;
        }

        private static int BiquadraticSign(Biquadratic value)
        {
            // (A+B√S) + √T(C+D√S)
            var first = new LinearSurd(value.A, value.B, value.S);
            var second = new LinearSurd(value.C, value.D, value.S);
            int firstSign = first.Sign;
            int secondSign = second.Sign;
            if (value.T.Sign == 0 || secondSign == 0) return firstSign;
            if (firstSign == 0) return secondSign;
            if (firstSign == secondSign) return firstSign;

            Rational p = value.A * value.A +
                value.B * value.B * value.S - value.T *
                (value.C * value.C + value.D * value.D * value.S);
            Rational q = 2 * value.A * value.B -
                2 * value.T * value.C * value.D;
            int squaredDifference = QuadraticSign(p, q, value.S);
            if (squaredDifference == 0) return 0;
            return firstSign > 0 ? squaredDifference : -squaredDifference;
        }
    }
}
