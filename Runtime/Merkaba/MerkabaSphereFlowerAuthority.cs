using System;
using System.Collections.Generic;
using System.Numerics;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// The single CPU oracle for the REV-B M8 Dual Sphere-Flower algebra.
    /// Its finite tables are emitted verbatim to HLSL by the editor codegen.
    /// No method in this type searches world geometry or owns persistent state.
    /// </summary>
    public static partial class MerkabaSphereFlowerAuthority
    {
        public const float LatticeStep = MerkabaConstants.LatticeStep;
        public const int LevelCount = 6;
        public const int DirectedRelationCount = 26;
        public const int LineClassCount = 13;
        public const int NodeClassCount = 26;
        public const int StrandClassCount = 72;
        public const int PetalClassCount = 48;
        public const int MaximumSectorCount = 32;
        public const int ChildPetalCount = 4;
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
            public float Midpoint => Lower + (Upper - Lower) * 0.5f;

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

            public static FloatInterval Add(FloatInterval left,
                FloatInterval right) => new(
                Enclose((double)left.Lower + right.Lower).Lower,
                Enclose((double)left.Upper + right.Upper).Upper);

            public static FloatInterval Subtract(FloatInterval left,
                FloatInterval right) => new(
                Enclose((double)left.Lower - right.Upper).Lower,
                Enclose((double)left.Upper - right.Lower).Upper);

            public static FloatInterval Multiply(FloatInterval left,
                FloatInterval right)
            {
                double p0 = (double)left.Lower * right.Lower;
                double p1 = (double)left.Lower * right.Upper;
                double p2 = (double)left.Upper * right.Lower;
                double p3 = (double)left.Upper * right.Upper;
                double minimum = Math.Min(Math.Min(p0, p1), Math.Min(p2, p3));
                double maximum = Math.Max(Math.Max(p0, p1), Math.Max(p2, p3));
                return new FloatInterval(Enclose(minimum).Lower,
                    Enclose(maximum).Upper);
            }

            public static FloatInterval Divide(FloatInterval numerator,
                FloatInterval denominator)
            {
                if (denominator.ContainsZero)
                    throw new DivideByZeroException(
                        "An interval divisor may not contain zero.");
                return Multiply(numerator, new FloatInterval(
                    Enclose(1.0 / denominator.Upper).Lower,
                    Enclose(1.0 / denominator.Lower).Upper));
            }

            public static FloatInterval Square(FloatInterval value)
            {
                if (value.ContainsZero)
                {
                    double maximum = Math.Max((double)value.Lower * value.Lower,
                        (double)value.Upper * value.Upper);
                    return new FloatInterval(0f, Enclose(maximum).Upper);
                }
                return Multiply(value, value);
            }

            public static FloatInterval Sqrt(FloatInterval value)
            {
                if (value.Lower < 0f)
                    throw new ArgumentOutOfRangeException(nameof(value));
                return new FloatInterval(
                    Enclose(Math.Sqrt(value.Lower)).Lower,
                    Enclose(Math.Sqrt(value.Upper)).Upper);
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
        private static readonly SectorBoundary[] SectorBoundariesValue;
        private static readonly TetraFrameRule[] TetraFramesValue;

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
            StrandsValue = strands;
            LinesValue = lines;
            SectorBoundariesValue = sectors;
            TetraFramesValue = tetraFrames;
#else
            LoadGeneratedTables(out LinesValue, out DirectionsValue,
                out NodesValue, out StrandsValue, out PetalsValue,
                out SectorBoundariesValue, out TetraFramesValue);
#endif
            ValidateFrozenTables();
        }

        public static ReadOnlySpan<LineRule> Lines => LinesValue;
        public static ReadOnlySpan<DirectionRule> Directions => DirectionsValue;
        public static ReadOnlySpan<NodeRule> Nodes => NodesValue;
        public static ReadOnlySpan<StrandRule> Strands => StrandsValue;
        public static ReadOnlySpan<PetalRule> Petals => PetalsValue;
        public static ReadOnlySpan<ChildPetalRule> ChildPetals => ChildPetalsValue;
        public static ReadOnlySpan<SectorBoundary> SectorBoundaries =>
            SectorBoundariesValue;
        public static ReadOnlySpan<TetraFrameRule> TetraFrames => TetraFramesValue;

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
            foreach (TetraFrameRule value in TetraFramesValue)
            {
                for (int i = 0; i < 4; i++)
                    Word(unchecked((uint)value.LineClasses[i]));
                for (int i = 0; i < 4; i++)
                    Word(unchecked((uint)value.Eta[i]));
                Word(unchecked((uint)value.Chirality));
            }
            return hash;
        }

        public static float LevelStep(int level)
        {
            if ((uint)level >= LevelCount)
                throw new ArgumentOutOfRangeException(nameof(level));
            return LatticeStep / (1 << level);
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
            if (normalUncertainty < 0f || offsetUncertainty < 0f)
                throw new ArgumentOutOfRangeException(nameof(normalUncertainty));
            float3 relative = loop.Center - kernelCenter;
            float a = math.dot(decodedNormal, relative) - decodedOffset;
            float b = loop.Radius * math.dot(decodedNormal, loop.E1);
            float c = loop.Radius * math.dot(decodedNormal, loop.E2);

            double sumA = Math.Abs((double)decodedNormal.x * relative.x) +
                Math.Abs((double)decodedNormal.y * relative.y) +
                Math.Abs((double)decodedNormal.z * relative.z) +
                Math.Abs(decodedOffset);
            double sumB = Math.Abs((double)loop.Radius * decodedNormal.x * loop.E1.x) +
                Math.Abs((double)loop.Radius * decodedNormal.y * loop.E1.y) +
                Math.Abs((double)loop.Radius * decodedNormal.z * loop.E1.z);
            double sumC = Math.Abs((double)loop.Radius * decodedNormal.x * loop.E2.x) +
                Math.Abs((double)loop.Radius * decodedNormal.y * loop.E2.y) +
                Math.Abs((double)loop.Radius * decodedNormal.z * loop.E2.z);

            double boundA = normalUncertainty * math.length(relative) +
                offsetUncertainty + Gamma(6) * sumA;
            double boundB = loop.Radius * normalUncertainty +
                Gamma(7) * sumB;
            double boundC = loop.Radius * normalUncertainty +
                Gamma(7) * sumC;

            return new Interval3(FloatInterval.FromCenterRadius(a, boundA),
                FloatInterval.FromCenterRadius(b, boundB),
                FloatInterval.FromCenterRadius(c, boundC));
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
            FloatInterval a2 = FloatInterval.Square(FloatInterval.Abs(abc.X));

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
            RootClassification classification = ClassifyRoots(abc);
            if (classification != RootClassification.CertainSecant &&
                classification != RootClassification.CertainTangent)
                return new RootResult(classification, default, default);

            FloatInterval q = FloatInterval.Add(FloatInterval.Square(abc.Y),
                FloatInterval.Square(abc.Z));
            if (q.ContainsZero)
                return new RootResult(RootClassification.Ambiguous,
                    default, default);
            FloatInterval delta = FloatInterval.Subtract(q,
                FloatInterval.Square(abc.X));
            if (delta.Lower < 0f)
                return new RootResult(RootClassification.Ambiguous,
                    default, default);
            FloatInterval rootDelta = FloatInterval.Sqrt(delta);
            FloatInterval minusA = new(-abc.X.Upper, -abc.X.Lower);
            FloatInterval baseX = FloatInterval.Multiply(minusA, abc.Y);
            FloatInterval baseY = FloatInterval.Multiply(minusA, abc.Z);
            FloatInterval turnX = FloatInterval.Multiply(rootDelta,
                new FloatInterval(-abc.Z.Upper, -abc.Z.Lower));
            FloatInterval turnY = FloatInterval.Multiply(rootDelta, abc.Y);

            Interval2 minus = new(
                FloatInterval.Divide(FloatInterval.Subtract(baseX, turnX), q),
                FloatInterval.Divide(FloatInterval.Subtract(baseY, turnY), q));
            Interval2 plus = new(
                FloatInterval.Divide(FloatInterval.Add(baseX, turnX), q),
                FloatInterval.Divide(FloatInterval.Add(baseY, turnY), q));
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
            if (denominator.ContainsZero)
            {
                value = default;
                return ProofClassification.Ambiguous;
            }
            value = FloatInterval.Divide(numerator, denominator);
            return ProofClassification.Certain;
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

        public static float4 TetraForward(float4 q)
        {
            float scalar = 0.25f * (q.x + q.y + q.z + q.w);
            float3 vector = 0.75f * (q.x * TetraUnit(0) +
                q.y * TetraUnit(1) + q.z * TetraUnit(2) +
                q.w * TetraUnit(3));
            return new float4(scalar, vector);
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

        public static float EndpointBasis(float s)
        {
            if (s < 0f || s > 1f) throw new ArgumentOutOfRangeException(nameof(s));
            float oneMinus = 1f - s;
            return 16f * s * s * oneMinus * oneMinus;
        }

        public static float EndpointBasisDerivative(float s)
        {
            if (s < 0f || s > 1f) throw new ArgumentOutOfRangeException(nameof(s));
            return 32f * s * (1f - s) * (1f - 2f * s);
        }

        public static ProofClassification ClassifyVRepresentability(
            FloatInterval amplitude, float sphereRadius)
        {
            if (!(sphereRadius > 0f))
                throw new ArgumentOutOfRangeException(nameof(sphereRadius));
            FloatInterval magnitude = FloatInterval.Abs(amplitude);
            float limit = sphereRadius * 0.5f;
            if (magnitude.Upper < limit) return ProofClassification.Certain;
            if (magnitude.Lower >= limit) return ProofClassification.Impossible;
            return ProofClassification.Ambiguous;
        }

        public static void EvaluateDeformedLoop(LoopFrame loop, float theta,
            float v, float vPrime, out float3 position, out float3 tangent)
        {
            EvaluateDeformedLoopUnit(loop,
                new float2(math.cos(theta), math.sin(theta)), v, vPrime,
                out position, out tangent);
        }

        public static void EvaluateDeformedLoopUnit(LoopFrame loop,
            float2 loopUnit, float v, float vPrime, out float3 position,
            out float3 tangent)
        {
            float radiusSquared = 0.75f * loop.SphereRadius *
                loop.SphereRadius - 3f * v * v;
            if (!(radiusSquared > 0f))
                throw new ArgumentOutOfRangeException(nameof(v));
            float rho = math.sqrt(radiusSquared);
            float3 radial = loop.E1 * loopUnit.x + loop.E2 * loopUnit.y;
            float3 perpendicular = -loop.E1 * loopUnit.y +
                loop.E2 * loopUnit.x;
            float rhoPrime = -3f * v * vPrime / rho;
            position = loop.Center + 2f * v * loop.Direction + rho * radial;
            tangent = 2f * vPrime * loop.Direction + rhoPrime * radial +
                rho * perpendicular;
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

        private static void ValidateFrozenTables()
        {
            if (DirectionsValue.Length != DirectedRelationCount ||
                NodesValue.Length != NodeClassCount ||
                StrandsValue.Length != StrandClassCount ||
                PetalsValue.Length != PetalClassCount ||
                LinesValue.Length != LineClassCount ||
                TetraFramesValue.Length != TetraFrameCount)
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
                {
                    BuildBoundaryPoints(line, plane, exact);
                }
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
                        $"Line {lineClass} generated {exact.Count} sectors.");

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
            int3 direction = Cross(line, plane);
            int directionSquared = Dot(direction, direction);
            if (directionSquared == 0) return;

            Rational lineOffset = new(Dot(line, line), 2);
            Rational planeOffset = new(Dot(plane, plane), 2);
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
