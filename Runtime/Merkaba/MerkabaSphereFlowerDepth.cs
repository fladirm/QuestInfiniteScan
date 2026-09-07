using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        public const int DepthCertificateSide = 512;
        public const int DepthCertificateLevelCount = 10;
        public const int DepthCertificateEyeStride = 349525;
        public const int DepthCertificateNodeCount = 2 * DepthCertificateEyeStride;
        public const int DepthCertificateGpuStackCapacity = 64;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public readonly struct DepthCertificateNode
        {
            public readonly float MinLowerDepth;
            public readonly uint AllValid;

            public DepthCertificateNode(float minLowerDepth, bool allValid)
            {
                bool valid = allValid && minLowerDepth > 0f &&
                    !float.IsInfinity(minLowerDepth) && !float.IsNaN(minLowerDepth);
                MinLowerDepth = valid ? minLowerDepth : 0f;
                AllValid = valid ? 1u : 0u;
            }

            public bool ProvesThrough(float supportUpperDepth) =>
                AllValid == 1u && supportUpperDepth >= 0f &&
                supportUpperDepth < MinLowerDepth;

            public static DepthCertificateNode Reduce(DepthCertificateNode a,
                DepthCertificateNode b, DepthCertificateNode c,
                DepthCertificateNode d) => new(
                Math.Min(Math.Min(a.MinLowerDepth, b.MinLowerDepth),
                    Math.Min(c.MinLowerDepth, d.MinLowerDepth)),
                (a.AllValid & b.AllValid & c.AllValid & d.AllValid) == 1u);
        }

        public static int DepthCertificateLevelOffset(int level)
        {
            if ((uint)level >= DepthCertificateLevelCount)
                throw new ArgumentOutOfRangeException(nameof(level));
            int side = DepthCertificateSide >> level;
            return (4 * DepthCertificateSide * DepthCertificateSide -
                4 * side * side) / 3;
        }

        public static int DepthCertificateAddress(int eye, int level, int2 xy)
        {
            int offset = DepthCertificateLevelOffset(level);
            int side = DepthCertificateSide >> level;
            if ((uint)eye >= 2u || math.any(xy < 0) || math.any(xy >= side))
                throw new ArgumentOutOfRangeException(nameof(xy));
            return eye * DepthCertificateEyeStride + offset + xy.y * side + xy.x;
        }

        // CPU oracle only. Production builds the same reduction entirely on GPU.
        // Invalid/padded pixels never acquire an interval by neighbour propagation.
        public static void BuildDepthCertificate(ReadOnlySpan<FloatInterval> depths,
            ReadOnlySpan<byte> validity, int2 imageSize, int eye,
            Span<DepthCertificateNode> certificate)
        {
            if (math.any(imageSize <= 0) || math.any(imageSize > DepthCertificateSide) ||
                (uint)eye >= 2u || depths.Length != imageSize.x * imageSize.y ||
                validity.Length != depths.Length || certificate.Length != DepthCertificateNodeCount)
                throw new ArgumentException("Invalid frozen depth certificate input.");
            int eyeBase = eye * DepthCertificateEyeStride;
            for (int y = 0; y < DepthCertificateSide; y++)
            for (int x = 0; x < DepthCertificateSide; x++)
            {
                bool inside = x < imageSize.x && y < imageSize.y;
                int source = inside ? y * imageSize.x + x : 0;
                FloatInterval interval = inside ? depths[source] : default;
                certificate[eyeBase + y * DepthCertificateSide + x] =
                    new DepthCertificateNode(interval.Lower, inside && validity[source] == 1 &&
                        !float.IsInfinity(interval.Upper));
            }
            for (int level = 1; level < DepthCertificateLevelCount; level++)
            {
                int side = DepthCertificateSide >> level;
                int src = eyeBase + DepthCertificateLevelOffset(level - 1);
                int dst = eyeBase + DepthCertificateLevelOffset(level);
                int stride = side * 2;
                for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    int p = src + 2 * y * stride + 2 * x;
                    certificate[dst + y * side + x] = DepthCertificateNode.Reduce(
                        certificate[p], certificate[p + 1],
                        certificate[p + stride], certificate[p + stride + 1]);
                }
            }
        }

        // Inclusive bounds. This is the smallest aligned quadtree square
        // containing the COMPLETE integer AABB, not a four-corner sample.
        public static int DepthCertificateEnvelopeLevel(int2 minimum, int2 maximum)
        {
            if (math.any(minimum < 0) || math.any(maximum < minimum) ||
                math.any(maximum >= DepthCertificateSide))
                throw new ArgumentOutOfRangeException(nameof(minimum));
            uint difference = (uint)((minimum.x ^ maximum.x) |
                (minimum.y ^ maximum.y));
            return difference == 0u ? 0 : 32 - math.lzcnt(difference);
        }

        private static FloatInterval DepthIntervalRow(float4 row, Interval3 point) =>
            FloatInterval.Add(FloatInterval.Add(FloatInterval.Add(
                FloatInterval.Multiply(FloatInterval.Singleton(row.x), point.X),
                FloatInterval.Multiply(FloatInterval.Singleton(row.y), point.Y)),
                FloatInterval.Multiply(FloatInterval.Singleton(row.z), point.Z)),
                FloatInterval.Singleton(row.w));

        private static FloatInterval DepthMatrixRow(float4x4 matrix, int row,
            Interval3 point) => DepthIntervalRow(new float4(matrix.c0[row],
                matrix.c1[row], matrix.c2[row], matrix.c3[row]), point);

        public readonly struct ObservedLoopFrame
        {
            public readonly Interval3 Center, Axis1, Axis2;
            internal ObservedLoopFrame(Interval3 center, Interval3 axis1, Interval3 axis2)
            {
                Center = center; Axis1 = axis1; Axis2 = axis2;
            }
        }

        private static FloatInterval ObservedDot(Interval3 a, Interval3 b) =>
            FloatInterval.Add(FloatInterval.Add(FloatInterval.Multiply(a.X, b.X),
                FloatInterval.Multiply(a.Y, b.Y)), FloatInterval.Multiply(a.Z, b.Z));

        public static ObservedLoopFrame EvaluateObservedLoop(int3 owner, int direction,
            float4x4 gridToWorld)
        {
            DirectionRule node = DirectionsValue[direction];
            LineRule line = LinesValue[node.LineClass];
            Long3 junction = new(2L * owner.x + node.Direction.x,
                2L * owner.y + node.Direction.y, 2L * owner.z + node.Direction.z);
            FloatInterval Integer(long value)
            {
                float f = value;
                return new FloatInterval(PreviousFloat(f), NextFloat(f));
            }
            FloatInterval halfStep = FloatInterval.Singleton(LatticeStep * 0.5f);
            Interval3 grid = new(FloatInterval.Multiply(Integer(junction.X), halfStep),
                FloatInterval.Multiply(Integer(junction.Y), halfStep),
                FloatInterval.Multiply(Integer(junction.Z), halfStep));
            Interval3 center = new(DepthMatrixRow(gridToWorld, 0, grid),
                DepthMatrixRow(gridToWorld, 1, grid), DepthMatrixRow(gridToWorld, 2, grid));
            FloatInterval radius = FloatInterval.Sqrt(FloatInterval.Multiply(
                FloatInterval.Multiply(FloatInterval.Singleton(LatticeStep),
                    FloatInterval.Singleton(LatticeStep)), FloatInterval.Singleton(0.75f * (int)line.Shell)));
            Interval3 Axis(float3 v)
            {
                Interval3 a = new(FloatInterval.Multiply(FloatInterval.Singleton(v.x), radius),
                    FloatInterval.Multiply(FloatInterval.Singleton(v.y), radius),
                    FloatInterval.Multiply(FloatInterval.Singleton(v.z), radius));
                FloatInterval Row(int row) => DepthIntervalRow(new float4(gridToWorld.c0[row],
                    gridToWorld.c1[row], gridToWorld.c2[row], 0f), a);
                return new Interval3(Row(0), Row(1), Row(2));
            }
            return new ObservedLoopFrame(center, Axis(line.E1), Axis(line.E2));
        }

        public static Interval3 RestrictObservedPlaneToLoop(Interval3 normal,
            Interval3 position, ObservedLoopFrame loop) => new(
                ObservedDot(normal, new Interval3(FloatInterval.Subtract(loop.Center.X, position.X),
                    FloatInterval.Subtract(loop.Center.Y, position.Y),
                    FloatInterval.Subtract(loop.Center.Z, position.Z))),
                ObservedDot(normal, loop.Axis1), ObservedDot(normal, loop.Axis2));

        public static bool ProjectDepthCertificateSupport(float3 minimum, float3 maximum,
            float4x4 view, float4x4 projection, int2 imageSize, float reprojectionError,
            out int2 pixelMin, out int2 pixelMax, out float supportUpperDepth)
        {
            pixelMin = imageSize;
            pixelMax = new int2(-1);
            supportUpperDepth = 0f;
            if (!math.all(math.isfinite(minimum)) || !math.all(math.isfinite(maximum)) ||
                math.any(maximum < minimum) || math.any(imageSize <= 0) ||
                math.any(imageSize > DepthCertificateSide) ||
                !(reprojectionError >= 0f) || float.IsInfinity(reprojectionError) ||
                !math.all(math.isfinite(view.c0)) || !math.all(math.isfinite(view.c1)) ||
                !math.all(math.isfinite(view.c2)) || !math.all(math.isfinite(view.c3)) ||
                !math.all(math.isfinite(projection.c0)) || !math.all(math.isfinite(projection.c1)) ||
                !math.all(math.isfinite(projection.c2)) || !math.all(math.isfinite(projection.c3)) ||
                view.c0.w != 0f || view.c1.w != 0f || view.c2.w != 0f || view.c3.w != 1f)
                return false;
            for (int corner = 0; corner < 8; corner++)
            {
                Interval3 world = Interval3.Singleton(new float3(
                    (corner & 1) == 0 ? minimum.x : maximum.x,
                    (corner & 2) == 0 ? minimum.y : maximum.y,
                    (corner & 4) == 0 ? minimum.z : maximum.z));
                FloatInterval error = new(-reprojectionError, reprojectionError);
                Interval3 eye = new(FloatInterval.Add(DepthMatrixRow(view, 0, world), error),
                    FloatInterval.Add(DepthMatrixRow(view, 1, world), error),
                    FloatInterval.Add(DepthMatrixRow(view, 2, world), error));
                if (!(eye.Z.Upper < 0f)) return false;
                supportUpperDepth = Math.Max(supportUpperDepth, -eye.Z.Lower);
                FloatInterval w = DepthMatrixRow(projection, 3, eye);
                if (!(w.Lower > 0f)) return false;
                FloatInterval x = FloatInterval.Divide(DepthMatrixRow(projection, 0, eye), w);
                FloatInterval y = FloatInterval.Divide(DepthMatrixRow(projection, 1, eye), w);
                FloatInterval z = FloatInterval.Divide(DepthMatrixRow(projection, 2, eye), w);
                if (z.Lower < -1f || z.Upper > 1f) return false;
                x = FloatInterval.Multiply(FloatInterval.Add(x, FloatInterval.Singleton(1f)),
                    FloatInterval.Singleton(imageSize.x * 0.5f));
                y = FloatInterval.Multiply(FloatInterval.Add(y, FloatInterval.Singleton(1f)),
                    FloatInterval.Singleton(imageSize.y * 0.5f));
                if (!math.all(math.isfinite(new float4(x.Lower, x.Upper, y.Lower, y.Upper))) ||
                    x.Lower < 0f || y.Lower < 0f || x.Upper >= imageSize.x || y.Upper >= imageSize.y)
                    return false;
                pixelMin = math.min(pixelMin, new int2((int)Math.Floor(x.Lower), (int)Math.Floor(y.Lower)));
                pixelMax = math.max(pixelMax, new int2((int)Math.Floor(x.Upper), (int)Math.Floor(y.Upper)));
            }
            return !float.IsInfinity(supportUpperDepth);
        }

        public static ProofClassification ClassifyStereoDepthSupport(
            ReadOnlySpan<DepthCertificateNode> certificate, int2 imageSize,
            float3 minimum, float3 maximum, ReadOnlySpan<float4x4> views,
            ReadOnlySpan<float4x4> projections, ReadOnlySpan<float4> errorBounds,
            bool strictEndpointIntersects)
        {
            if (strictEndpointIntersects) return ProofClassification.Impossible;
            if (views.Length != 2 || projections.Length != 2 || errorBounds.Length != 2)
                return ProofClassification.Ambiguous;
            for (int eye = 0; eye < 2; eye++)
                if (errorBounds[eye].w == 1f && math.all(math.isfinite(errorBounds[eye].xyz)) &&
                    math.all(errorBounds[eye].xyz >= 0f) &&
                    ProjectDepthCertificateSupport(minimum, maximum, views[eye], projections[eye],
                        imageSize, errorBounds[eye].y, out int2 lo, out int2 hi, out float upper) &&
                    ClassifyDepthCertificate(certificate, eye, imageSize, lo, hi, upper) ==
                        ProofClassification.Certain) return ProofClassification.Certain;
            return ProofClassification.Ambiguous;
        }

        public static ProofClassification ClassifyDepthCertificate(
            ReadOnlySpan<DepthCertificateNode> certificate, int eye, int2 imageSize,
            int2 minimum, int2 maximum, float supportUpperDepth, bool envelopeOnly = false)
        {
            if (certificate.Length != DepthCertificateNodeCount || (uint)eye >= 2u ||
                math.any(imageSize <= 0) || math.any(imageSize > DepthCertificateSide) ||
                math.any(minimum < 0) || math.any(maximum < minimum) ||
                math.any(maximum >= imageSize) || !(supportUpperDepth >= 0f) ||
                float.IsInfinity(supportUpperDepth))
                return ProofClassification.Ambiguous;
            int level = DepthCertificateEnvelopeLevel(minimum, maximum);
            int2 origin = (minimum >> level) << level;
            if (certificate[DepthCertificateAddress(eye, level, minimum >> level)]
                .ProvesThrough(supportUpperDepth)) return ProofClassification.Certain;
            if (envelopeOnly) return ProofClassification.Ambiguous;

            // No query budget in the semantic oracle. The GPU's bounded stack
            // may lose completeness, never certificate safety.
            var stack = new Stack<int3>();
            stack.Push(new int3(origin.x, origin.y, level));
            while (stack.Count != 0)
            {
                int3 node = stack.Pop();
                int2 lo = node.xy;
                int2 hi = lo + (1 << node.z) - 1;
                if (math.any(hi < minimum) || math.any(lo > maximum)) continue;
                if (node.z == 0 || (math.all(lo >= minimum) && math.all(hi <= maximum)))
                {
                    if (!certificate[DepthCertificateAddress(eye, node.z, lo >> node.z)]
                        .ProvesThrough(supportUpperDepth)) return ProofClassification.Ambiguous;
                    continue;
                }
                int childLevel = node.z - 1;
                int half = 1 << childLevel;
                for (int child = 3; child >= 0; child--)
                    stack.Push(new int3(lo.x + (child & 1) * half,
                        lo.y + (child >> 1) * half, childLevel));
            }
            return ProofClassification.Certain;
        }
    }
}
