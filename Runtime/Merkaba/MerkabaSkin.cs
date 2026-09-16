using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Analytic boundary oracle of the M8 readout skin. The 50 mm support of a
    /// measured kernel is the 26-faced convex envelope on the 25 mm lattice;
    /// the published skin is the boundary of the union of the supports of the
    /// connected neighbourhood. The arrangement of the 26 neighbour supports on
    /// every support face is solved once, exactly, into refined facelets whose
    /// visibility is a single 26-bit mask test at runtime. No field is sampled
    /// and no surface is reconstructed: this only reads canonical M8 state.
    /// </summary>
    internal static class MerkabaSkin
    {
        // Vertex coordinates are integers in units of LatticeStep / 6, so the
        // 25 mm lattice step is 6 units and the support half extent is 6 units.
        internal const int LatticeUnits = 6;
        internal const int SupportHalfUnits = 6;
        internal const float VertexUnit = MerkabaConstants.LatticeStep / 6f;
        internal const int VerticesPerFacelet = 3;
        internal const int IndicesPerFacelet = 3;

        internal readonly struct Facelet
        {
            internal readonly ushort A;
            internal readonly ushort B;
            internal readonly ushort C;
            internal readonly uint OccluderMask;

            internal Facelet(int a, int b, int c, uint occluderMask)
            {
                A = checked((ushort)a);
                B = checked((ushort)b);
                C = checked((ushort)c);
                OccluderMask = occluderMask;
            }
        }

        private static readonly (int3 Normal, int Offset)[] ConstraintsValue =
            BuildConstraints();
        private static readonly int3[] NeighboursValue = BuildNeighbours();
        private static int3[] _vertices;
        private static uint[] _vertexContacts;
        private static Facelet[] _facelets;

        static MerkabaSkin() => BuildBoundary();

        internal static int VertexCount => _vertices.Length;
        internal static int FaceletCount => _facelets.Length;
        internal static ReadOnlySpan<int3> Vertices => _vertices;
        internal static ReadOnlySpan<uint> VertexContacts => _vertexContacts;
        internal static ReadOnlySpan<Facelet> Facelets => _facelets;
        internal static ReadOnlySpan<int3> Neighbours => NeighboursValue;

        private static (int3, int)[] BuildConstraints()
        {
            var list = new List<(int3, int)>(26);
            for (int sign = 1; sign >= -1; sign -= 2)
            {
                list.Add((new int3(sign, 0, 0), SupportHalfUnits));
                list.Add((new int3(0, sign, 0), SupportHalfUnits));
                list.Add((new int3(0, 0, sign), SupportHalfUnits));
            }
            for (int a = 1; a >= -1; a -= 2)
            for (int b = 1; b >= -1; b -= 2)
            {
                list.Add((new int3(a, b, 0), 8));
                list.Add((new int3(0, a, b), 8));
                list.Add((new int3(a, 0, b), 8));
            }
            for (int a = 1; a >= -1; a -= 2)
            for (int b = 1; b >= -1; b -= 2)
            for (int c = 1; c >= -1; c -= 2)
                list.Add((new int3(a, b, c), 10));
            return list.ToArray();
        }

        private static int3[] BuildNeighbours()
        {
            var list = new List<int3>(26);
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                if (x != 0 || y != 0 || z != 0)
                    list.Add(new int3(x, y, z));
            return list.ToArray();
        }

        /// <summary>Lattice offset of a neighbour bit, in kernel steps.</summary>
        internal static int3 NeighbourOffset(int index) =>
            NeighboursValue[index];

        private readonly struct Rat
        {
            internal readonly long N;
            internal readonly long D;

            internal Rat(long numerator, long denominator = 1)
            {
                if (denominator < 0)
                {
                    numerator = -numerator;
                    denominator = -denominator;
                }
                long divisor = Gcd(Math.Abs(numerator), denominator);
                if (divisor == 0) divisor = 1;
                N = numerator / divisor;
                D = denominator / divisor;
            }

            private static long Gcd(long a, long b)
            {
                while (b != 0) (a, b) = (b, a % b);
                return a == 0 ? 1 : a;
            }

            public static Rat operator +(Rat a, Rat b) =>
                new(a.N * b.D + b.N * a.D, a.D * b.D);
            public static Rat operator -(Rat a, Rat b) =>
                new(a.N * b.D - b.N * a.D, a.D * b.D);
            public static Rat operator *(Rat a, Rat b) =>
                new(a.N * b.N, a.D * b.D);
            public static Rat operator /(Rat a, Rat b) =>
                new(a.N * b.D, a.D * b.N);
            internal int Sign => Math.Sign(N);
            internal bool IsInteger => D == 1;
            public override string ToString() => $"{N}/{D}";
        }

        private readonly struct Point : IEquatable<Point>
        {
            internal readonly Rat X;
            internal readonly Rat Y;
            internal readonly Rat Z;

            internal Point(Rat x, Rat y, Rat z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            internal Rat Dot(int3 normal) =>
                X * new Rat(normal.x) + Y * new Rat(normal.y) +
                Z * new Rat(normal.z);

            internal static Point Lerp(Point a, Point b, Rat t) => new(
                a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t);

            public bool Equals(Point other) =>
                X.N == other.X.N && X.D == other.X.D &&
                Y.N == other.Y.N && Y.D == other.Y.D &&
                Z.N == other.Z.N && Z.D == other.Z.D;

            public override bool Equals(object obj) =>
                obj is Point other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(X.N, X.D,
                Y.N, Y.D, Z.N, Z.D);
        }

        private static List<Point> ClipHalfSpace(List<Point> polygon,
            int3 normal, Rat offset)
        {
            var result = new List<Point>(polygon.Count + 2);
            for (int index = 0; index < polygon.Count; index++)
            {
                Point a = polygon[index];
                Point b = polygon[(index + 1) % polygon.Count];
                Rat da = a.Dot(normal) - offset;
                Rat db = b.Dot(normal) - offset;
                if (da.Sign <= 0) result.Add(a);
                if ((da.Sign < 0 && db.Sign > 0) ||
                    (da.Sign > 0 && db.Sign < 0))
                    result.Add(Point.Lerp(a, b, da / (da - db)));
            }
            for (int index = result.Count - 1; index >= 0; index--)
            {
                Point previous = result[(index + result.Count - 1) %
                    result.Count];
                if (result.Count > 1 && result[index].Equals(previous))
                    result.RemoveAt(index);
            }
            return result.Count >= 3 ? result : new List<Point>();
        }

        private static bool ContainsNeighbour(Point point, int3 shift,
            bool strict)
        {
            foreach ((int3 normal, int offset) in ConstraintsValue)
            {
                Rat value = point.Dot(normal) -
                    new Rat(math.dot(normal, shift));
                int sign = (value - new Rat(offset)).Sign;
                if (strict ? sign >= 0 : sign > 0) return false;
            }
            return true;
        }

        private static void BuildBoundary()
        {
            var vertexIds = new Dictionary<Point, int>();
            var vertices = new List<int3>();
            var contacts = new List<uint>();
            var facelets = new List<Facelet>();
            int3[] shifts = new int3[NeighboursValue.Length];
            for (int index = 0; index < shifts.Length; index++)
                shifts[index] = NeighboursValue[index] * LatticeUnits;

            for (int face = 0; face < ConstraintsValue.Length; face++)
            {
                List<Point> polygon = BuildFacePolygon(face);
                if (polygon.Count < 3) continue;
                var pieces = SplitFace(polygon, face, shifts);
                MergeAndEmit(pieces, ConstraintsValue[face].Normal, shifts,
                    vertexIds, vertices, contacts, facelets);
            }

            _vertices = vertices.ToArray();
            _vertexContacts = contacts.ToArray();
            _facelets = facelets.ToArray();
        }

        private static List<Point> BuildFacePolygon(int face)
        {
            (int3 normal, int offset) = ConstraintsValue[face];
            BuildPlaneBasis(normal, out int3 u, out int3 v);
            var denominator = new Rat(math.dot(normal, normal));
            var origin = new Point(new Rat(normal.x * offset) / denominator,
                new Rat(normal.y * offset) / denominator,
                new Rat(normal.z * offset) / denominator);
            var span = new Rat(64);
            var polygon = new List<Point>(4);
            foreach ((int su, int sv) in new[] { (1, 1), (-1, 1), (-1, -1),
                         (1, -1) })
                polygon.Add(new Point(
                    origin.X + new Rat(u.x * su + v.x * sv) * span,
                    origin.Y + new Rat(u.y * su + v.y * sv) * span,
                    origin.Z + new Rat(u.z * su + v.z * sv) * span));
            for (int other = 0; other < ConstraintsValue.Length; other++)
            {
                if (other == face) continue;
                polygon = ClipHalfSpace(polygon,
                    ConstraintsValue[other].Normal,
                    new Rat(ConstraintsValue[other].Offset));
                if (polygon.Count < 3) break;
            }
            return polygon;
        }

        private static void BuildPlaneBasis(int3 normal, out int3 u, out int3 v)
        {
            int3 axis = math.abs(normal.x) <= math.abs(normal.y) &&
                math.abs(normal.x) <= math.abs(normal.z)
                ? new int3(1, 0, 0)
                : math.abs(normal.y) <= math.abs(normal.z)
                    ? new int3(0, 1, 0) : new int3(0, 0, 1);
            u = IntegerCross(normal, axis);
            v = IntegerCross(normal, u);
        }

        private static int3 IntegerCross(int3 a, int3 b) => new(
            a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x);

        private static List<List<Point>> SplitFace(List<Point> polygon,
            int face, int3[] shifts)
        {
            var cuts = new List<(int3 Normal, int Offset)>();
            foreach (int3 shift in shifts)
            {
                List<Point> region = polygon;
                foreach ((int3 normal, int offset) in ConstraintsValue)
                {
                    region = ClipHalfSpace(region, normal,
                        new Rat(offset + math.dot(normal, shift)));
                    if (region.Count < 3) break;
                }
                if (region.Count < 3) continue;
                foreach ((int3 normal, int offset) in ConstraintsValue)
                {
                    int plane = offset + math.dot(normal, shift);
                    bool above = false;
                    bool below = false;
                    foreach (Point point in polygon)
                    {
                        int sign = (point.Dot(normal) - new Rat(plane)).Sign;
                        above |= sign > 0;
                        below |= sign < 0;
                    }
                    if (!above || !below) continue;
                    bool touches = false;
                    foreach (Point point in region)
                        touches |= (point.Dot(normal) -
                            new Rat(plane)).Sign == 0;
                    if (!touches) continue;
                    if (!cuts.Contains((normal, plane)))
                        cuts.Add((normal, plane));
                }
            }

            var parts = new List<List<Point>> { polygon };
            foreach ((int3 normal, int plane) in cuts)
            {
                var next = new List<List<Point>>(parts.Count + 4);
                foreach (List<Point> part in parts)
                {
                    bool above = false;
                    bool below = false;
                    foreach (Point point in part)
                    {
                        int sign = (point.Dot(normal) - new Rat(plane)).Sign;
                        above |= sign > 0;
                        below |= sign < 0;
                    }
                    if (!above || !below)
                    {
                        next.Add(part);
                        continue;
                    }
                    List<Point> inside = ClipHalfSpace(part, normal,
                        new Rat(plane));
                    List<Point> outside = ClipHalfSpace(part, -normal,
                        new Rat(-plane));
                    if (inside.Count >= 3) next.Add(inside);
                    if (outside.Count >= 3) next.Add(outside);
                }
                parts = next;
            }
            return parts;
        }

        private static void MergeAndEmit(List<List<Point>> pieces, int3 normal,
            int3[] shifts, Dictionary<Point, int> vertexIds,
            List<int3> vertices, List<uint> contacts, List<Facelet> facelets)
        {
            var byMask = new SortedDictionary<uint, List<List<Point>>>();
            foreach (List<Point> piece in pieces)
            {
                Point centroid = Centroid(piece);
                uint mask = 0u;
                for (int index = 0; index < shifts.Length; index++)
                    if (ContainsNeighbour(centroid, shifts[index], true))
                        mask |= 1u << index;
                if (!byMask.TryGetValue(mask, out List<List<Point>> group))
                    byMask[mask] = group = new List<List<Point>>();
                group.Add(piece);
            }

            foreach (KeyValuePair<uint, List<List<Point>>> entry in byMask)
            {
                foreach (List<Point> loop in TraceLoops(entry.Value))
                {
                    List<Point> clean = RemoveCollinear(loop, normal);
                    foreach ((Point a, Point b, Point c) in EarClip(clean,
                                 normal))
                        facelets.Add(new Facelet(
                            VertexId(a, shifts, vertexIds, vertices, contacts),
                            VertexId(b, shifts, vertexIds, vertices, contacts),
                            VertexId(c, shifts, vertexIds, vertices, contacts),
                            entry.Key));
                }
            }
        }

        private static int VertexId(Point point, int3[] shifts,
            Dictionary<Point, int> vertexIds, List<int3> vertices,
            List<uint> contacts)
        {
            if (vertexIds.TryGetValue(point, out int existing)) return existing;
            if (!point.X.IsInteger || !point.Y.IsInteger || !point.Z.IsInteger)
                throw new InvalidOperationException(
                    "Skin boundary vertex is not on the canonical lattice: " +
                    $"{point.X},{point.Y},{point.Z}");
            int id = vertices.Count;
            vertices.Add(new int3((int)point.X.N, (int)point.Y.N,
                (int)point.Z.N));
            uint contact = 0u;
            for (int index = 0; index < shifts.Length; index++)
                if (ContainsNeighbour(point, shifts[index], false))
                    contact |= 1u << index;
            contacts.Add(contact);
            vertexIds[point] = id;
            return id;
        }

        private static Point Centroid(List<Point> polygon)
        {
            var count = new Rat(polygon.Count);
            Rat x = new(0);
            Rat y = new(0);
            Rat z = new(0);
            foreach (Point point in polygon)
            {
                x += point.X;
                y += point.Y;
                z += point.Z;
            }
            return new Point(x / count, y / count, z / count);
        }

        private static List<List<Point>> TraceLoops(List<List<Point>> polygons)
        {
            var edges = new Dictionary<(Point, Point), bool>();
            foreach (List<Point> polygon in polygons)
            for (int index = 0; index < polygon.Count; index++)
            {
                Point a = polygon[index];
                Point b = polygon[(index + 1) % polygon.Count];
                if (edges.ContainsKey((b, a))) edges.Remove((b, a));
                else edges[(a, b)] = true;
            }
            var next = new Dictionary<Point, List<Point>>();
            foreach ((Point a, Point b) in edges.Keys)
            {
                if (!next.TryGetValue(a, out List<Point> targets))
                    next[a] = targets = new List<Point>();
                targets.Add(b);
            }
            var used = new HashSet<(Point, Point)>();
            var loops = new List<List<Point>>();
            foreach ((Point start, Point second) in edges.Keys)
            {
                if (used.Contains((start, second))) continue;
                var loop = new List<Point> { start };
                Point current = second;
                used.Add((start, second));
                while (!current.Equals(start) && loop.Count < 4096)
                {
                    loop.Add(current);
                    Point step = default;
                    bool found = false;
                    if (next.TryGetValue(current, out List<Point> targets))
                        foreach (Point candidate in targets)
                            if (!used.Contains((current, candidate)))
                            {
                                step = candidate;
                                found = true;
                                break;
                            }
                    if (!found) break;
                    used.Add((current, step));
                    current = step;
                }
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        private static List<Point> RemoveCollinear(List<Point> loop,
            int3 normal)
        {
            var result = new List<Point>(loop.Count);
            for (int index = 0; index < loop.Count; index++)
            {
                Point a = loop[(index + loop.Count - 1) % loop.Count];
                Point b = loop[index];
                Point c = loop[(index + 1) % loop.Count];
                if (TwiceArea(a, b, c, normal).Sign != 0) result.Add(b);
            }
            return result.Count >= 3 ? result : loop;
        }

        private static Rat TwiceArea(Point a, Point b, Point c, int3 normal)
        {
            Rat ux = b.X - a.X;
            Rat uy = b.Y - a.Y;
            Rat uz = b.Z - a.Z;
            Rat vx = c.X - a.X;
            Rat vy = c.Y - a.Y;
            Rat vz = c.Z - a.Z;
            Rat cx = uy * vz - uz * vy;
            Rat cy = uz * vx - ux * vz;
            Rat cz = ux * vy - uy * vx;
            return cx * new Rat(normal.x) + cy * new Rat(normal.y) +
                cz * new Rat(normal.z);
        }

        private static List<(Point, Point, Point)> EarClip(List<Point> loop,
            int3 normal)
        {
            var points = new List<Point>(loop);
            var triangles = new List<(Point, Point, Point)>();
            if (points.Count < 3) return triangles;
            if (LoopArea(points, normal).Sign < 0) points.Reverse();
            int guard = 0;
            while (points.Count > 3 && guard++ < 4096)
            {
                bool clipped = false;
                for (int index = 0; index < points.Count; index++)
                {
                    Point a = points[(index + points.Count - 1) %
                        points.Count];
                    Point b = points[index];
                    Point c = points[(index + 1) % points.Count];
                    if (TwiceArea(a, b, c, normal).Sign <= 0) continue;
                    bool ear = true;
                    foreach (Point point in points)
                    {
                        if (point.Equals(a) || point.Equals(b) ||
                            point.Equals(c)) continue;
                        if (TwiceArea(a, b, point, normal).Sign >= 0 &&
                            TwiceArea(b, c, point, normal).Sign >= 0 &&
                            TwiceArea(c, a, point, normal).Sign >= 0)
                        {
                            ear = false;
                            break;
                        }
                    }
                    if (!ear) continue;
                    triangles.Add((a, b, c));
                    points.RemoveAt(index);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (points.Count == 3)
                triangles.Add((points[0], points[1], points[2]));
            return triangles;
        }

        private static Rat LoopArea(List<Point> loop, int3 normal)
        {
            Rat total = new(0);
            for (int index = 1; index + 1 < loop.Count; index++)
                total += TwiceArea(loop[0], loop[index], loop[index + 1],
                    normal);
            return total;
        }

        /// <summary>
        /// Neighbours whose measured support is compatible with this kernel's,
        /// as the 26-bit connectivity key of the boundary decision.
        /// </summary>
        internal static uint CompatibleNeighbourMask(int3 coord,
            KernelState main, IReadOnlyDictionary<int3, KernelState> context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            uint mask = 0u;
            for (int index = 0; index < NeighboursValue.Length; index++)
            {
                int3 neighbourCoord = coord + NeighboursValue[index];
                if (!context.TryGetValue(neighbourCoord,
                        out KernelState neighbour) ||
                    !neighbour.IsOccupied ||
                    !neighbour.HasMeasuredSurfacePlane)
                    continue;
                if (Compatible(coord, main, neighbourCoord, neighbour, context))
                    mask |= 1u << index;
            }
            return mask;
        }

        private static bool Compatible(int3 mainCoord, KernelState main,
            int3 neighbourCoord, KernelState neighbour,
            IReadOnlyDictionary<int3, KernelState> context)
        {
            int3 mainFree = FreeSide(mainCoord, main, context);
            int3 neighbourFree = FreeSide(neighbourCoord, neighbour, context);
            if (math.all(mainFree == int3.zero) ||
                math.all(neighbourFree == int3.zero))
                return true;
            return math.dot(mainFree, neighbourFree) >= 0;
        }

        private static int3 FreeSide(int3 coord, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context)
        {
            if (!state.HasMeasuredSurfacePlane) return int3.zero;
            KernelState.DecodeSurfacePlane(state.Flags, out float3 normal,
                out _);
            int3 step = NormalStep(normal);
            bool plus = KnownFree(coord + step, context);
            bool minus = KnownFree(coord - step, context);
            if (plus == minus) return int3.zero;
            return plus ? step : -step;
        }

        private static bool KnownFree(int3 coord,
            IReadOnlyDictionary<int3, KernelState> context) =>
            context.TryGetValue(coord, out KernelState state) &&
            !state.IsOccupied &&
            state.OccupancyEvidence <= MerkabaConstants.ExportKnownFreeThreshold;

        // The canonical 26-direction chart, never a second classifier.
        private static int3 NormalStep(float3 normal) =>
            MerkabaOverlapShell.NearestGridNormalStep(normal);

        internal static int VisibleFaceletCount(uint neighbourMask)
        {
            int count = 0;
            foreach (Facelet facelet in _facelets)
                if ((facelet.OccluderMask & neighbourMask) == 0u) count++;
            return count;
        }

        /// <summary>
        /// Canonical position of a boundary vertex. The measured shift is the
        /// mean over the kernels sharing that vertex, so both sides of a shared
        /// boundary compute the same point.
        /// </summary>
        internal static float3 VertexPosition(int3 kernelCoord,
            KernelState state, IReadOnlyDictionary<int3, KernelState> context,
            uint neighbourMask, int vertex)
        {
            float3 basePosition = (float3)kernelCoord *
                MerkabaConstants.LatticeStep +
                (float3)_vertices[vertex] * VertexUnit;
            if (!state.HasMeasuredSurfacePlane) return basePosition;
            float3 shift = MeasuredShift(state);
            float weight = 1f;
            uint shared = _vertexContacts[vertex] & neighbourMask;
            for (int index = 0; index < NeighboursValue.Length; index++)
            {
                if ((shared & (1u << index)) == 0u) continue;
                if (!context.TryGetValue(kernelCoord + NeighboursValue[index],
                        out KernelState neighbour) ||
                    !neighbour.HasMeasuredSurfacePlane) continue;
                shift += MeasuredShift(neighbour);
                weight += 1f;
            }
            return basePosition + shift / weight;
        }

        internal static uint VertexColor(int3 kernelCoord, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context,
            uint neighbourMask, int vertex)
        {
            uint shared = _vertexContacts[vertex] & neighbourMask;
            ulong weight = math.max(1u, state.ColorConfidence);
            Color32 own = KernelState.UnpackColor(state.PackedColor);
            ulong red = own.r * weight;
            ulong green = own.g * weight;
            ulong blue = own.b * weight;
            ulong total = weight;
            for (int index = 0; index < NeighboursValue.Length; index++)
            {
                if ((shared & (1u << index)) == 0u) continue;
                if (!context.TryGetValue(kernelCoord + NeighboursValue[index],
                        out KernelState neighbour)) continue;
                ulong neighbourWeight = math.max(1u, neighbour.ColorConfidence);
                Color32 color = KernelState.UnpackColor(neighbour.PackedColor);
                red += color.r * neighbourWeight;
                green += color.g * neighbourWeight;
                blue += color.b * neighbourWeight;
                total += neighbourWeight;
            }
            return KernelState.PackColor(new Color32(
                (byte)math.min(255ul, (red + total / 2ul) / total),
                (byte)math.min(255ul, (green + total / 2ul) / total),
                (byte)math.min(255ul, (blue + total / 2ul) / total), 255));
        }

        private static float3 MeasuredShift(KernelState state)
        {
            KernelState.DecodeSurfacePlane(state.Flags, out float3 normal,
                out float signedOffset);
            return normal * signedOffset;
        }

        internal static string BuildGeneratedHlsl()
        {
            var text = new StringBuilder(96 * 1024);
            text.AppendLine("// GENERATED from MerkabaSkin.cs. DO NOT EDIT.");
            text.AppendLine("#ifndef GENESIS_MERKABA_SKIN_INCLUDED");
            text.AppendLine("#define GENESIS_MERKABA_SKIN_INCLUDED");
            text.AppendLine();
            text.AppendLine("#define M8_SKIN_VERTEX_COUNT " +
                _vertices.Length + "u");
            text.AppendLine("#define M8_SKIN_FACELET_COUNT " +
                _facelets.Length + "u");
            text.AppendLine("#define M8_SKIN_NEIGHBOUR_COUNT 26u");
            text.AppendLine("#define M8_SKIN_VERTEX_MASK_WORDS " +
                ((_vertices.Length + 31) / 32) + "u");
            text.AppendLine("#define M8_SKIN_VERTEX_SCALE (1.0 / 6.0)");
            text.AppendLine();
            text.AppendLine("static const int3 M8_SKIN_NEIGHBOURS[26] = {");
            for (int index = 0; index < NeighboursValue.Length; index++)
            {
                int3 value = NeighboursValue[index];
                text.Append("    int3(").Append(value.x).Append(", ")
                    .Append(value.y).Append(", ").Append(value.z).Append(')');
                text.AppendLine(index + 1 == NeighboursValue.Length ? "" : ",");
            }
            text.AppendLine("};");
            text.AppendLine();
            text.AppendLine("static const int4 M8_SKIN_VERTICES[" +
                _vertices.Length + "] = {");
            for (int index = 0; index < _vertices.Length; index++)
            {
                int3 value = _vertices[index];
                text.Append("    int4(").Append(value.x).Append(", ")
                    .Append(value.y).Append(", ").Append(value.z).Append(", ")
                    .Append(unchecked((int)_vertexContacts[index]))
                    .Append(')');
                text.AppendLine(index + 1 == _vertices.Length ? "" : ",");
            }
            text.AppendLine("};");
            text.AppendLine();
            text.AppendLine("static const uint4 M8_SKIN_FACELETS[" +
                _facelets.Length + "] = {");
            for (int index = 0; index < _facelets.Length; index++)
            {
                Facelet facelet = _facelets[index];
                text.Append("    uint4(").Append(facelet.A).Append("u, ")
                    .Append(facelet.B).Append("u, ").Append(facelet.C)
                    .Append("u, 0x").Append(facelet.OccluderMask
                        .ToString("x8")).Append("u)");
                text.AppendLine(index + 1 == _facelets.Length ? "" : ",");
            }
            text.AppendLine("};");
            text.AppendLine();
            text.AppendLine("#endif");
            return text.ToString();
        }
    }
}
