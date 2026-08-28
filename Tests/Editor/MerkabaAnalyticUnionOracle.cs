using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Genesis.RoomScan.Tests
{
    /// <summary>
    /// Test-only analytic oracle. It independently builds the arrangement made by
    /// intersecting the exact tetrahedron-A/tetrahedron-B union with all translated
    /// neighbour planes. Production tables are deliberately not referenced here.
    /// </summary>
    internal static class MerkabaAnalyticUnionOracle
    {
        internal readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            public readonly int3 A;
            public readonly int3 B;
            public readonly int3 C;

            public TriangleKey(double3 a, double3 b, double3 c)
                : this(Quantize(a), Quantize(b), Quantize(c))
            {
            }

            public TriangleKey(int3 a, int3 b, int3 c)
            {
                int3[] values = { a, b, c };
                Array.Sort(values, Compare);
                A = values[0];
                B = values[1];
                C = values[2];
            }

            public TriangleKey RelativeTo(int3 latticeOffset)
            {
                int3 halfUnits = latticeOffset * 2;
                return new TriangleKey(A - halfUnits, B - halfUnits,
                    C - halfUnits);
            }

            public bool Equals(TriangleKey other) =>
                math.all(A == other.A) && math.all(B == other.B) &&
                math.all(C == other.C);
            public override bool Equals(object obj) =>
                obj is TriangleKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B, C);
            public override string ToString() => $"{A}|{B}|{C}";

            private static int3 Quantize(double3 value)
            {
                double3 scaled = value * 2d;
                int3 rounded = (int3)math.round(scaled);
                if (math.cmax(math.abs(scaled - rounded)) > 1e-8)
                    throw new InvalidOperationException(
                        $"Oracle produced a non-half-unit vertex: {value}");
                return rounded;
            }
        }

        internal readonly struct OracleTriangle
        {
            public readonly double3 A;
            public readonly double3 B;
            public readonly double3 C;
            public readonly double3 Normal;

            public OracleTriangle(double3 a, double3 b, double3 c,
                double3 normal)
            {
                A = a;
                B = b;
                C = c;
                Normal = normal;
            }

            public double3 Centroid => (A + B + C) / 3d;
            public TriangleKey Key(int3 center) =>
                new(A + center, B + center, C + center);
        }

        private readonly struct Line : IEquatable<Line>, IComparable<Line>
        {
            public readonly int A;
            public readonly int B;
            public readonly int C;

            public Line(int a, int b, int c)
            {
                if (a == 0 && b == 0 && c == 0)
                {
                    A = B = C = 0;
                    return;
                }
                int divisor = GreatestCommonDivisor(GreatestCommonDivisor(
                    math.abs(a), math.abs(b)), math.abs(c));
                a /= divisor;
                b /= divisor;
                c /= divisor;
                if (a < 0 || (a == 0 && (b < 0 || (b == 0 && c < 0))))
                {
                    a = -a;
                    b = -b;
                    c = -c;
                }
                A = a;
                B = b;
                C = c;
            }

            public bool IsZero => A == 0 && B == 0 && C == 0;
            public double Evaluate(double2 point) => A * point.x + B * point.y + C;
            public bool Equals(Line other) => A == other.A && B == other.B && C == other.C;
            public override bool Equals(object obj) => obj is Line other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B, C);
            public int CompareTo(Line other)
            {
                int result = A.CompareTo(other.A);
                if (result != 0) return result;
                result = B.CompareTo(other.B);
                return result != 0 ? result : C.CompareTo(other.C);
            }
        }

        private static readonly int3[] TetraSigns =
        {
            new( 1,  1,  1), new( 1, -1, -1),
            new(-1,  1, -1), new(-1, -1,  1)
        };

        private static readonly int3[] Neighbours = BuildNeighbours();
        private static readonly OracleTriangle[] CanonicalTriangles = BuildCanonicalTriangles();

        internal static IReadOnlyList<OracleTriangle> CanonicalMicroTriangles =>
            CanonicalTriangles;

        internal static HashSet<TriangleKey> Boundary(HashSet<int3> occupied)
        {
            if (occupied == null) throw new ArgumentNullException(nameof(occupied));
            var result = new HashSet<TriangleKey>();
            foreach (int3 center in occupied)
            foreach (OracleTriangle triangle in CanonicalTriangles)
            {
                double3 centroid = triangle.Centroid + center;
                const double epsilon = 1e-6;
                bool inside = ContainsUnion(occupied,
                    centroid - triangle.Normal * epsilon);
                bool outside = ContainsUnion(occupied,
                    centroid + triangle.Normal * epsilon);
                if (inside && !outside) result.Add(triangle.Key(center));
            }
            return result;
        }

        internal static bool ContainsUnion(HashSet<int3> occupied, double3 point)
        {
            foreach (int3 center in occupied)
                if (ContainsSupport(point - center)) return true;
            return false;
        }

        internal static bool ContainsSupport(double3 point)
        {
            bool tetraA = true;
            bool tetraB = true;
            foreach (int3 sign in TetraSigns)
            {
                double projection = math.dot((double3)sign, point);
                tetraA &= projection >= -1d - 1e-12;
                tetraB &= projection <= 1d + 1e-12;
            }
            return tetraA || tetraB;
        }

        private static OracleTriangle[] BuildCanonicalTriangles()
        {
            var result = new List<OracleTriangle>(96);
            foreach (Face face in BuildFaces())
            {
                var lines = new SortedSet<Line>();
                double3 edge1 = face.B - face.A;
                double3 edge2 = face.C - face.A;
                foreach (int3 neighbour in Neighbours)
                foreach (int3 tetraSign in TetraSigns)
                {
                    AddRestrictedPlane(lines, face.A, edge1, edge2,
                        tetraSign, neighbour, 1);
                    AddRestrictedPlane(lines, face.A, edge1, edge2,
                        tetraSign, neighbour, -1);
                }

                var polygons = new List<List<double2>>
                {
                    new() { new double2(0, 0), new double2(1, 0), new double2(0, 1) }
                };
                foreach (Line line in lines)
                {
                    var split = new List<List<double2>>(polygons.Count + 2);
                    foreach (List<double2> polygon in polygons)
                        Split(polygon, line, split);
                    polygons = split;
                }

                foreach (List<double2> polygon in polygons)
                for (int vertex = 1; vertex + 1 < polygon.Count; vertex++)
                {
                    double3 a = FromBarycentric(face, polygon[0]);
                    double3 b = FromBarycentric(face, polygon[vertex]);
                    double3 c = FromBarycentric(face, polygon[vertex + 1]);
                    double3 cross = math.cross(b - a, c - a);
                    if (math.lengthsq(cross) <= 1e-20) continue;
                    if (math.dot(cross, face.Normal) < 0d)
                        (b, c) = (c, b);
                    result.Add(new OracleTriangle(a, b, c, face.Normal));
                }
            }

            if (result.Count != 96 || result.Select(value =>
                    new TriangleKey(value.A, value.B, value.C)).Distinct().Count() != 96)
                throw new InvalidOperationException(
                    $"Analytic Merkaba plane arrangement produced {result.Count}, expected 96 triangles.");
            return result.ToArray();
        }

        private static IEnumerable<Face> BuildFaces()
        {
            for (int z = -1; z <= 1; z += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int x = -1; x <= 1; x += 2)
            {
                double3 tip = new(x, y, z);
                double3 axisX = new(x, 0, 0);
                double3 axisY = new(0, y, 0);
                double3 axisZ = new(0, 0, z);
                yield return Face.Outward(tip, axisX, axisY);
                yield return Face.Outward(tip, axisY, axisZ);
                yield return Face.Outward(tip, axisZ, axisX);
            }
        }

        private static void AddRestrictedPlane(ISet<Line> lines, double3 origin,
            double3 edge1, double3 edge2, int3 tetraSign, int3 neighbour,
            int constant)
        {
            int a = checked((int)Math.Round(math.dot((double3)tetraSign, edge1)));
            int b = checked((int)Math.Round(math.dot((double3)tetraSign, edge2)));
            int c = checked((int)Math.Round(math.dot((double3)tetraSign,
                origin - neighbour) + constant));
            var line = new Line(a, b, c);
            if (!line.IsZero) lines.Add(line);
        }

        private static void Split(List<double2> polygon, Line line,
            List<List<double2>> destination)
        {
            var positive = new List<double2>(polygon.Count + 2);
            var negative = new List<double2>(polygon.Count + 2);
            for (int index = 0; index < polygon.Count; index++)
            {
                double2 current = polygon[index];
                double2 next = polygon[(index + 1) % polygon.Count];
                double currentValue = line.Evaluate(current);
                double nextValue = line.Evaluate(next);
                if (currentValue >= 0d) positive.Add(current);
                if (currentValue <= 0d) negative.Add(current);
                if ((currentValue > 0d && nextValue < 0d) ||
                    (currentValue < 0d && nextValue > 0d))
                {
                    double t = currentValue / (currentValue - nextValue);
                    double2 intersection = math.lerp(current, next, t);
                    positive.Add(intersection);
                    negative.Add(intersection);
                }
            }
            Cleanup(positive);
            Cleanup(negative);
            if (positive.Count >= 3) destination.Add(positive);
            if (negative.Count >= 3) destination.Add(negative);
        }

        private static void Cleanup(List<double2> polygon)
        {
            for (int index = polygon.Count - 1; index >= 0; index--)
            {
                int previous = (index + polygon.Count - 1) % polygon.Count;
                if (math.lengthsq(polygon[index] - polygon[previous]) <= 1e-24)
                    polygon.RemoveAt(index);
            }
            bool removed;
            do
            {
                removed = false;
                for (int index = 0; index < polygon.Count && polygon.Count >= 3; index++)
                {
                    double2 previous = polygon[(index + polygon.Count - 1) % polygon.Count];
                    double2 current = polygon[index];
                    double2 next = polygon[(index + 1) % polygon.Count];
                    double area = (current.x - previous.x) * (next.y - current.y) -
                                  (current.y - previous.y) * (next.x - current.x);
                    if (math.abs(area) > 1e-12) continue;
                    polygon.RemoveAt(index);
                    removed = true;
                    break;
                }
            } while (removed);
        }

        private static double3 FromBarycentric(Face face, double2 uv) =>
            face.A + (face.B - face.A) * uv.x + (face.C - face.A) * uv.y;

        private static int3[] BuildNeighbours()
        {
            var result = new List<int3>(26);
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                if (x != 0 || y != 0 || z != 0)
                    result.Add(new int3(x, y, z));
            return result.ToArray();
        }

        private static int Compare(int3 left, int3 right)
        {
            int result = left.x.CompareTo(right.x);
            if (result != 0) return result;
            result = left.y.CompareTo(right.y);
            return result != 0 ? result : left.z.CompareTo(right.z);
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                int remainder = left % right;
                left = right;
                right = remainder;
            }
            return left == 0 ? 1 : left;
        }

        private readonly struct Face
        {
            public readonly double3 A;
            public readonly double3 B;
            public readonly double3 C;
            public readonly double3 Normal;

            private Face(double3 a, double3 b, double3 c)
            {
                A = a;
                B = b;
                C = c;
                Normal = math.normalize(math.cross(b - a, c - a));
            }

            public static Face Outward(double3 a, double3 b, double3 c)
            {
                double3 normal = math.cross(b - a, c - a);
                return math.dot(normal, a + b + c) >= 0d
                    ? new Face(a, b, c)
                    : new Face(a, c, b);
            }
        }
    }
}
