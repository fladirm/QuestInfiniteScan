using System;
using System.Collections.Generic;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaQuerySupportBoundsKind : uint
    {
        Unknown = 0u,
        ExactProjectiveHull = 1u,
    }

    /// <summary>
    /// Exact packed-Q16.48 AABB used only as a zero-false-negative residency
    /// broad phase. It is derived metadata and never enters canonical bytes.
    /// </summary>
    internal readonly struct SigmaQ48Bounds3 : IEquatable<SigmaQ48Bounds3>
    {
        internal SigmaQ48Bounds3(long lowerX, long lowerY, long lowerZ,
            long upperX, long upperY, long upperZ)
        {
            if (lowerX > upperX || lowerY > upperY || lowerZ > upperZ)
                throw new ArgumentOutOfRangeException(nameof(lowerX),
                    "Query-support bounds are inverted.");
            LowerX = lowerX; LowerY = lowerY; LowerZ = lowerZ;
            UpperX = upperX; UpperY = upperY; UpperZ = upperZ;
        }

        internal long LowerX { get; }
        internal long LowerY { get; }
        internal long LowerZ { get; }
        internal long UpperX { get; }
        internal long UpperY { get; }
        internal long UpperZ { get; }

        internal bool Intersects(SigmaQ48Bounds3 other) =>
            LowerX <= other.UpperX && UpperX >= other.LowerX &&
            LowerY <= other.UpperY && UpperY >= other.LowerY &&
            LowerZ <= other.UpperZ && UpperZ >= other.LowerZ;

        internal SigmaQ48Bounds3 Include(long x, long y, long z) => new(
            Math.Min(LowerX, x), Math.Min(LowerY, y), Math.Min(LowerZ, z),
            Math.Max(UpperX, x), Math.Max(UpperY, y), Math.Max(UpperZ, z));

        internal static SigmaQ48Bounds3 Union(SigmaQ48Bounds3 left,
            SigmaQ48Bounds3 right) => new(
                Math.Min(left.LowerX, right.LowerX),
                Math.Min(left.LowerY, right.LowerY),
                Math.Min(left.LowerZ, right.LowerZ),
                Math.Max(left.UpperX, right.UpperX),
                Math.Max(left.UpperY, right.UpperY),
                Math.Max(left.UpperZ, right.UpperZ));

        public bool Equals(SigmaQ48Bounds3 other) =>
            LowerX == other.LowerX && LowerY == other.LowerY &&
            LowerZ == other.LowerZ && UpperX == other.UpperX &&
            UpperY == other.UpperY && UpperZ == other.UpperZ;
        public override bool Equals(object obj) =>
            obj is SigmaQ48Bounds3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(
            HashCode.Combine(LowerX, LowerY, LowerZ),
            HashCode.Combine(UpperX, UpperY, UpperZ));
    }

    /// <summary>
    /// Generated-plan consumer for the PREDICTION_SUPPORT residency broad phase.
    /// The bound encloses every exact projective point rasterized by the existing
    /// sensor-forward witness. Direct-order/first-hit/occlusion only remove those
    /// contributions, so disjoint hulls may be skipped; every other case is kept.
    /// </summary>
    internal static class SigmaQuerySupportPlan
    {
        internal static SigmaDurableHash Fingerprint =>
            SigmaDurableHash.Parse(
                SigmaGeneratedQuerySupport.PlanFingerprint);

        internal static SigmaQuerySupportBoundsKind SummarizePage(
            SigmaDecodedPage page, out SigmaQ48Bounds3 bounds)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            bool any = false;
            bounds = default;
            for (int sample = 0; sample < page.ActiveSampleCount; ++sample)
                Include(page.SampleAt(sample), ref any, ref bounds);
            return any ? SigmaQuerySupportBoundsKind.ExactProjectiveHull :
                SigmaQuerySupportBoundsKind.Unknown;
        }

        internal static SigmaQuerySupportBoundsKind SummarizeBlocks(
            SigmaEncodedPageHeader header,
            IReadOnlyList<SigmaEncodedBlock> blocks,
            out SigmaQ48Bounds3 bounds)
        {
            if (blocks == null || blocks.Count != SigmaDecodedPage.BlockCount)
                throw new ArgumentException(
                    "A support summary requires exactly 64 codec blocks.",
                    nameof(blocks));
            bool any = false;
            bounds = default;
            for (int block = 0; block < blocks.Count; ++block)
            {
                if (blocks[block].Mode == SigmaBlockMode.Null)
                    continue;
                SigmaS16[] decoded = SigmaCarrierCodec.DecodeBlock(blocks[block]);
                if (blocks[block].Mode == SigmaBlockMode.Constant)
                {
                    Include(decoded[0], ref any, ref bounds);
                    continue;
                }
                int blockX = block & 7;
                int blockY = block >> 3;
                for (int y = 0; y < SigmaDecodedPage.BlockSize; ++y)
                for (int x = 0; x < SigmaDecodedPage.BlockSize; ++x)
                {
                    int pageSample =
                        (blockY * SigmaDecodedPage.BlockSize + y) *
                            SigmaDecodedPage.PageSize +
                        blockX * SigmaDecodedPage.BlockSize + x;
                    if ((uint)pageSample >= header.ActiveSampleCount)
                        continue;
                    Include(decoded[y * SigmaDecodedPage.BlockSize + x],
                        ref any, ref bounds);
                }
            }
            return any ? SigmaQuerySupportBoundsKind.ExactProjectiveHull :
                SigmaQuerySupportBoundsKind.Unknown;
        }

        internal static bool TryBuildSensorQueryBounds(
            StereoRigFrameLease source, Matrix4x4 worldToRoom,
            float translationPriorMetres, float rotationPriorDegrees,
            out SigmaQ48Bounds3 bounds)
        {
            bounds = default;
            if (source == null || !source.IsValid ||
                !float.IsFinite(translationPriorMetres) ||
                !float.IsFinite(rotationPriorDegrees) ||
                translationPriorMetres < 0f || rotationPriorDegrees < 0f)
                return false;
            return TryBuildSensorQueryBounds(source.DepthLeft,
                source.DepthRight, worldToRoom, translationPriorMetres,
                rotationPriorDegrees, out bounds);
        }

        internal static bool TryBuildSensorQueryBounds(GpuImageView left,
            GpuImageView right, Matrix4x4 worldToRoom,
            float translationPriorMetres, float rotationPriorDegrees,
            out SigmaQ48Bounds3 bounds)
        {
            bounds = default;
            if (!float.IsFinite(translationPriorMetres) ||
                !float.IsFinite(rotationPriorDegrees) ||
                translationPriorMetres < 0f || rotationPriorDegrees < 0f)
                return false;
            bool any = false;
            double lowerX = 0.0, lowerY = 0.0, lowerZ = 0.0;
            double upperX = 0.0, upperY = 0.0, upperZ = 0.0;
            double maximumRadius = 0.0;
            double maximumFar = 0.0;
            double minimumFocal = double.PositiveInfinity;
            if (!IncludeFrustum(left, worldToRoom, ref any,
                    ref lowerX, ref lowerY, ref lowerZ,
                    ref upperX, ref upperY, ref upperZ,
                    ref maximumRadius, ref maximumFar, ref minimumFocal) ||
                !IncludeFrustum(right, worldToRoom, ref any,
                    ref lowerX, ref lowerY, ref lowerZ,
                    ref upperX, ref upperY, ref upperZ,
                    ref maximumRadius, ref maximumFar, ref minimumFocal) ||
                !any)
                return false;
            double theta = Math.Sqrt(3.0) * rotationPriorDegrees *
                Math.PI / 180.0;
            double posePadding = Math.Sqrt(3.0) * translationPriorMetres +
                2.0 * maximumRadius * Math.Sin(Math.Min(Math.PI, theta) * 0.5);
            // Prediction uses a 1.35 px billboard. Two pixels plus a 2 cm
            // calibration/quantization envelope is a strict outward superset.
            double footprintPadding = maximumFar * 2.0 / minimumFocal;
            double padding = posePadding + footprintPadding + 0.02;
            if (!double.IsFinite(padding) || padding < 0.0)
                return false;
            try
            {
                bounds = new SigmaQ48Bounds3(
                    SigmaNumericDomain.QuantizeLower(lowerX - padding),
                    SigmaNumericDomain.QuantizeLower(lowerY - padding),
                    SigmaNumericDomain.QuantizeLower(lowerZ - padding),
                    SigmaNumericDomain.QuantizeUpper(upperX + padding),
                    SigmaNumericDomain.QuantizeUpper(upperY + padding),
                    SigmaNumericDomain.QuantizeUpper(upperZ + padding));
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
            catch (OverflowException) { return false; }
        }

        private static void Include(SigmaS16 state, ref bool any,
            ref SigmaQ48Bounds3 bounds)
        {
            if (!SigmaGeometryReadout.TryReadExact(state,
                    out SigmaExactGeometrySample sample))
                return;
            if (!any)
            {
                bounds = new SigmaQ48Bounds3(sample.XRaw, sample.YRaw,
                    sample.ZRaw, sample.XRaw, sample.YRaw, sample.ZRaw);
                any = true;
            }
            else
                bounds = bounds.Include(sample.XRaw, sample.YRaw, sample.ZRaw);
        }

        private static bool IncludeFrustum(GpuImageView view,
            Matrix4x4 worldToRoom, ref bool any,
            ref double lowerX, ref double lowerY, ref double lowerZ,
            ref double upperX, ref double upperY, ref double upperZ,
            ref double maximumRadius, ref double maximumFar,
            ref double minimumFocal)
        {
            RigIntrinsics intrinsics = view.Intrinsics;
            if (!view.IsValid || !intrinsics.IsValid)
                return false;
            double near = view.DepthNearFar.x;
            double far = RigDepthContract.FiniteRasterFar(view.DepthNearFar);
            double fx = intrinsics.FocalLength.x;
            double fy = intrinsics.FocalLength.y;
            if (!(near > 0.0) || !(far > near) || !(fx > 0.0) || !(fy > 0.0))
                return false;
            double[] slopesX =
            {
                -intrinsics.PrincipalPoint.x / fx,
                (intrinsics.ImageResolution.x -
                    intrinsics.PrincipalPoint.x) / fx,
            };
            double[] slopesY =
            {
                -intrinsics.PrincipalPoint.y / fy,
                (intrinsics.ImageResolution.y -
                    intrinsics.PrincipalPoint.y) / fy,
            };
            Matrix4x4 roomFromCamera = SigmaRoomFrame.FromCamera(worldToRoom,
                view.WorldFromCamera);
            double[] depths = { near, far };
            foreach (double depth in depths)
            foreach (double slopeY in slopesY)
            foreach (double slopeX in slopesX)
            {
                double x = slopeX * depth;
                double y = slopeY * depth;
                double z = depth;
                double roomX = roomFromCamera.m00 * x +
                    roomFromCamera.m01 * y + roomFromCamera.m02 * z +
                    roomFromCamera.m03;
                double roomY = roomFromCamera.m10 * x +
                    roomFromCamera.m11 * y + roomFromCamera.m12 * z +
                    roomFromCamera.m13;
                double roomZ = roomFromCamera.m20 * x +
                    roomFromCamera.m21 * y + roomFromCamera.m22 * z +
                    roomFromCamera.m23;
                if (!double.IsFinite(roomX) || !double.IsFinite(roomY) ||
                    !double.IsFinite(roomZ))
                    return false;
                if (!any)
                {
                    lowerX = upperX = roomX;
                    lowerY = upperY = roomY;
                    lowerZ = upperZ = roomZ;
                    any = true;
                }
                else
                {
                    lowerX = Math.Min(lowerX, roomX);
                    lowerY = Math.Min(lowerY, roomY);
                    lowerZ = Math.Min(lowerZ, roomZ);
                    upperX = Math.Max(upperX, roomX);
                    upperY = Math.Max(upperY, roomY);
                    upperZ = Math.Max(upperZ, roomZ);
                }
                maximumRadius = Math.Max(maximumRadius,
                    Math.Sqrt(x * x + y * y + z * z));
            }
            maximumFar = Math.Max(maximumFar, far);
            minimumFocal = Math.Min(minimumFocal, Math.Min(fx, fy));
            return true;
        }
    }

    /// <summary>
    /// Disposable incremental AVL index over exact support hulls. Tree shape,
    /// balancing and traversal are execution-only; every returned key is checked
    /// in full against the selected durable PageRecord before rehydrate.
    /// </summary>
    internal sealed class SigmaQuerySupportIndex
    {
        private readonly struct SpatialKey : IComparable<SpatialKey>
        {
            internal SpatialKey(SigmaQ48Bounds3 bounds,
                SigmaCarrierPageCoordinate coordinate)
            {
                X = Midpoint(bounds.LowerX, bounds.UpperX);
                Y = Midpoint(bounds.LowerY, bounds.UpperY);
                Z = Midpoint(bounds.LowerZ, bounds.UpperZ);
                Coordinate = coordinate;
            }
            internal long X { get; }
            internal long Y { get; }
            internal long Z { get; }
            internal SigmaCarrierPageCoordinate Coordinate { get; }
            public int CompareTo(SpatialKey other)
            {
                int value = X.CompareTo(other.X);
                if (value != 0) return value;
                value = Y.CompareTo(other.Y);
                if (value != 0) return value;
                value = Z.CompareTo(other.Z);
                return value != 0 ? value : Coordinate.CompareTo(other.Coordinate);
            }
            private static long Midpoint(long lower, long upper) =>
                (lower & upper) + ((lower ^ upper) >> 1);
        }

        private sealed class Entry
        {
            internal SigmaDurablePageRecord Record;
            internal SigmaQ48Bounds3 Bounds;
            internal SpatialKey Key;
        }

        private sealed class Node
        {
            internal Entry Entry;
            internal Node Left;
            internal Node Right;
            internal int Height = 1;
            internal SigmaQ48Bounds3 SubtreeBounds;
        }

        private readonly object _gate = new();
        private readonly Dictionary<SigmaCarrierPageCoordinate, SpatialKey>
            _boundedKeys = new();
        private readonly Dictionary<SigmaCarrierPageCoordinate,
            SigmaDurablePageRecord> _unbounded = new();
        private Node _root;
        private int _lastVisitedNodes;

        internal int Count
        {
            get { lock (_gate) return _boundedKeys.Count + _unbounded.Count; }
        }
        internal int UnboundedCount
        {
            get { lock (_gate) return _unbounded.Count; }
        }
        internal int LastVisitedNodes
        {
            get { lock (_gate) return _lastVisitedNodes; }
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _root = null;
                _boundedKeys.Clear();
                _unbounded.Clear();
                _lastVisitedNodes = 0;
            }
        }

        internal void Rebuild(IEnumerable<(SigmaDurablePageRecord Record,
            SigmaQuerySupportSummary? Summary)> pages)
        {
            if (pages == null) throw new ArgumentNullException(nameof(pages));
            lock (_gate)
            {
                _root = null;
                _boundedKeys.Clear();
                _unbounded.Clear();
                foreach (var page in pages)
                    UpsertNoLock(page.Record, page.Summary);
                _lastVisitedNodes = 0;
            }
        }

        internal void Upsert(SigmaDurablePageRecord record,
            SigmaQuerySupportSummary? summary)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            lock (_gate) UpsertNoLock(record, summary);
        }

        internal IReadOnlyList<SigmaResidencyKey> Query(
            SigmaQ48Bounds3 bounds)
        {
            lock (_gate)
            {
                var result = new List<SigmaResidencyKey>();
                foreach (SigmaDurablePageRecord record in _unbounded.Values)
                    result.Add(ToKey(record));
                int visited = 0;
                if (_root != null)
                {
                    var stack = new Stack<Node>();
                    stack.Push(_root);
                    while (stack.Count != 0)
                    {
                        Node node = stack.Pop();
                        ++visited;
                        if (!node.SubtreeBounds.Intersects(bounds))
                            continue;
                        if (node.Entry.Bounds.Intersects(bounds))
                            result.Add(ToKey(node.Entry.Record));
                        if (node.Left != null) stack.Push(node.Left);
                        if (node.Right != null) stack.Push(node.Right);
                    }
                }
                _lastVisitedNodes = visited;
                result.Sort(CompareKeys);
                return result;
            }
        }

        private void UpsertNoLock(SigmaDurablePageRecord record,
            SigmaQuerySupportSummary? summary)
        {
            RemoveNoLock(record.Coordinate);
            if (summary.HasValue && summary.Value.HasExactProjectiveBounds)
            {
                var entry = new Entry
                {
                    Record = record,
                    Bounds = summary.Value.ProjectiveBounds,
                };
                entry.Key = new SpatialKey(entry.Bounds, record.Coordinate);
                _root = Insert(_root, entry);
                _boundedKeys.Add(record.Coordinate, entry.Key);
            }
            else
                _unbounded[record.Coordinate] = record;
        }

        private void RemoveNoLock(SigmaCarrierPageCoordinate coordinate)
        {
            _unbounded.Remove(coordinate);
            if (_boundedKeys.TryGetValue(coordinate, out SpatialKey key))
            {
                _root = Remove(_root, key);
                _boundedKeys.Remove(coordinate);
            }
        }

        private static Node Insert(Node node, Entry entry)
        {
            if (node == null)
                return Update(new Node
                {
                    Entry = entry,
                    SubtreeBounds = entry.Bounds,
                });
            int comparison = entry.Key.CompareTo(node.Entry.Key);
            if (comparison < 0) node.Left = Insert(node.Left, entry);
            else if (comparison > 0) node.Right = Insert(node.Right, entry);
            else throw new InvalidOperationException(
                "Query-support index received a duplicate full spatial key.");
            return Balance(Update(node));
        }

        private static Node Remove(Node node, SpatialKey key)
        {
            if (node == null) return null;
            int comparison = key.CompareTo(node.Entry.Key);
            if (comparison < 0) node.Left = Remove(node.Left, key);
            else if (comparison > 0) node.Right = Remove(node.Right, key);
            else
            {
                if (node.Left == null) return node.Right;
                if (node.Right == null) return node.Left;
                Node successor = Minimum(node.Right);
                node.Entry = successor.Entry;
                node.Right = Remove(node.Right, successor.Entry.Key);
            }
            return Balance(Update(node));
        }

        private static Node Minimum(Node node)
        {
            while (node.Left != null) node = node.Left;
            return node;
        }

        private static Node Balance(Node node)
        {
            int factor = Height(node.Left) - Height(node.Right);
            if (factor > 1)
            {
                if (Height(node.Left.Left) < Height(node.Left.Right))
                    node.Left = RotateLeft(node.Left);
                return RotateRight(node);
            }
            if (factor < -1)
            {
                if (Height(node.Right.Right) < Height(node.Right.Left))
                    node.Right = RotateRight(node.Right);
                return RotateLeft(node);
            }
            return node;
        }

        private static Node RotateLeft(Node node)
        {
            Node next = node.Right;
            node.Right = next.Left;
            next.Left = Update(node);
            return Update(next);
        }

        private static Node RotateRight(Node node)
        {
            Node next = node.Left;
            node.Left = next.Right;
            next.Right = Update(node);
            return Update(next);
        }

        private static Node Update(Node node)
        {
            node.Height = 1 + Math.Max(Height(node.Left), Height(node.Right));
            SigmaQ48Bounds3 bounds = node.Entry.Bounds;
            if (node.Left != null)
                bounds = SigmaQ48Bounds3.Union(bounds,
                    node.Left.SubtreeBounds);
            if (node.Right != null)
                bounds = SigmaQ48Bounds3.Union(bounds,
                    node.Right.SubtreeBounds);
            node.SubtreeBounds = bounds;
            return node;
        }

        private static int Height(Node node) => node?.Height ?? 0;
        private static SigmaResidencyKey ToKey(SigmaDurablePageRecord record) =>
            new(record.Coordinate, record.Revision, record.PageGeneration);
        private static int CompareKeys(SigmaResidencyKey left,
            SigmaResidencyKey right)
        {
            int value = left.Coordinate.CompareTo(right.Coordinate);
            if (value != 0) return value;
            value = left.RootContext.CompareTo(right.RootContext);
            return value != 0 ? value :
                left.PageGeneration.CompareTo(right.PageGeneration);
        }
    }
}
