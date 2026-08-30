using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Builds the conservative grid-space half-spaces used only by Q_SCAN.
    /// Exact mutation authority remains in the joint observation classifier.
    /// </summary>
    internal static class MerkabaMutationCoverage
    {
        internal const int PlanesPerEye = 4;
        internal const int PlaneCount = 2 * PlanesPerEye;

        internal static void WriteGridPlanes(Matrix4x4[] view,
            Matrix4x4[] projection, Matrix4x4 gridToWorld,
            Vector4[] destination)
        {
            if (view == null || view.Length < 2)
                throw new ArgumentException("Two frozen depth views are required.",
                    nameof(view));
            if (projection == null || projection.Length < 2)
                throw new ArgumentException(
                    "Two frozen depth projections are required.",
                    nameof(projection));
            if (destination == null || destination.Length < PlaneCount)
                throw new ArgumentException($"At least {PlaneCount} planes are required.",
                    nameof(destination));

            Vector3 referenceOrigin = (Vector3)view[0].inverse.GetColumn(3);
            int output = 0;
            for (int eye = 0; eye < 2; eye++)
            {
                Matrix4x4 worldToClip = projection[eye] * view[eye];
                Vector4 rowX = worldToClip.GetRow(0);
                Vector4 rowY = worldToClip.GetRow(1);
                Vector4 rowW = worldToClip.GetRow(3) *
                    MerkabaConstants.MutationOuterRadius;
                destination[output++] = WorldToKernelPlane(
                    NormalizeAndExpand(rowW + rowX, referenceOrigin),
                    gridToWorld);
                destination[output++] = WorldToKernelPlane(
                    NormalizeAndExpand(rowW - rowX, referenceOrigin),
                    gridToWorld);
                destination[output++] = WorldToKernelPlane(
                    NormalizeAndExpand(rowW + rowY, referenceOrigin),
                    gridToWorld);
                destination[output++] = WorldToKernelPlane(
                    NormalizeAndExpand(rowW - rowY, referenceOrigin),
                    gridToWorld);
            }
        }

        private static Vector4 NormalizeAndExpand(Vector4 plane,
            Vector3 referenceOrigin)
        {
            Vector3 normal = new(plane.x, plane.y, plane.z);
            float length = normal.magnitude;
            if (!(length > 1e-8f) || !float.IsFinite(length))
                throw new InvalidOperationException(
                    "Frozen depth projection produced a degenerate coverage plane.");
            plane /= length;

            // Every exact FREE point lies inside a HalfSupport tube around the
            // reference-eye ray. Expanding the other eye plane until that ray
            // origin is included makes the entire O->H segment conservative by
            // convexity; the tube margin covers its lateral extent.
            float atReferenceOrigin = Vector3.Dot(
                new Vector3(plane.x, plane.y, plane.z), referenceOrigin) +
                plane.w;
            plane.w += Mathf.Max(0f, -atReferenceOrigin) +
                MerkabaConstants.HalfSupport;
            return plane;
        }

        private static Vector4 WorldToKernelPlane(Vector4 worldPlane,
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
                    "Grid transform produced a degenerate coverage plane.");
            return new Vector4(kernelNormal.x / length,
                kernelNormal.y / length, kernelNormal.z / length,
                kernelDistance / length);
        }
    }
}
