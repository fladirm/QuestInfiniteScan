using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Converts immutable readout coverage into the signed kernel lattice once
    /// on the CPU. Q_DRAW then tests only grid AABBs; it never transforms a
    /// hierarchy node through GridToWorld.
    /// </summary>
    internal static class MerkabaReadoutCoverage
    {
        internal static Vector4 WorldToKernelPlane(Vector4 worldPlane,
            Matrix4x4 gridToWorld)
        {
            Vector3 normal = new(worldPlane.x, worldPlane.y, worldPlane.z);
            float step = MerkabaConstants.LatticeStep;
            Vector3 kernelNormal = new(
                Vector3.Dot(normal, gridToWorld.MultiplyVector(
                    Vector3.right * step)),
                Vector3.Dot(normal, gridToWorld.MultiplyVector(
                    Vector3.up * step)),
                Vector3.Dot(normal, gridToWorld.MultiplyVector(
                    Vector3.forward * step)));
            Vector3 origin = (Vector3)gridToWorld.GetColumn(3);
            float kernelDistance = Vector3.Dot(normal, origin) + worldPlane.w;
            float length = kernelNormal.magnitude;
            if (!(length > 1e-8f) || !float.IsFinite(length))
                throw new InvalidOperationException(
                    "Grid transform produced a degenerate readout plane.");
            return new Vector4(kernelNormal.x / length,
                kernelNormal.y / length, kernelNormal.z / length,
                kernelDistance / length);
        }

        internal static void WriteGridMetric(Matrix4x4 gridToWorld,
            out Vector3 diagonal, out Vector3 cross)
        {
            Vector3 x = gridToWorld.MultiplyVector(Vector3.right);
            Vector3 y = gridToWorld.MultiplyVector(Vector3.up);
            Vector3 z = gridToWorld.MultiplyVector(Vector3.forward);
            diagonal = new Vector3(Vector3.Dot(x, x), Vector3.Dot(y, y),
                Vector3.Dot(z, z));
            cross = new Vector3(Vector3.Dot(x, y), Vector3.Dot(x, z),
                Vector3.Dot(y, z));
            if (!FinitePositive(diagonal.x) || !FinitePositive(diagonal.y) ||
                !FinitePositive(diagonal.z) || !Finite(cross.x) ||
                !Finite(cross.y) || !Finite(cross.z))
                throw new InvalidOperationException(
                    "Grid transform produced a degenerate readout metric.");
        }

        internal static float GridDistanceSquared(Vector3 gridDeltaMeters,
            Vector3 diagonal, Vector3 cross) =>
            diagonal.x * gridDeltaMeters.x * gridDeltaMeters.x +
            diagonal.y * gridDeltaMeters.y * gridDeltaMeters.y +
            diagonal.z * gridDeltaMeters.z * gridDeltaMeters.z +
            2f * (cross.x * gridDeltaMeters.x * gridDeltaMeters.y +
                  cross.y * gridDeltaMeters.x * gridDeltaMeters.z +
                  cross.z * gridDeltaMeters.y * gridDeltaMeters.z);

        private static bool FinitePositive(float value) =>
            value > 1e-12f && float.IsFinite(value);

        private static bool Finite(float value) => float.IsFinite(value);
    }
}
