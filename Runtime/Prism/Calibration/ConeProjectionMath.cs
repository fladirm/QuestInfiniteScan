using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// First-order intersection of one calibrated pixel cone with a candidate surface.
    /// Axes are metric displacement per source pixel on that surface; they are the
    /// geometry-filtering and EWA basis used throughout Cone-PRISM.
    /// </summary>
    public readonly struct MetricConeFootprint
    {
        internal MetricConeFootprint(Vector3 axisX, Vector3 axisY,
            Vector3 metricTensor, float area, float incidence)
        {
            AxisX = axisX;
            AxisY = axisY;
            MetricTensor = metricTensor;
            AreaSquareMeters = area;
            Incidence = incidence;
        }

        public Vector3 AxisX { get; }
        public Vector3 AxisY { get; }
        /// <summary>(dot(X,X), dot(X,Y), dot(Y,Y)).</summary>
        public Vector3 MetricTensor { get; }
        public float AreaSquareMeters { get; }
        public float Incidence { get; }
        public bool IsValid => AreaSquareMeters > 0f && Incidence > 0f &&
                               float.IsFinite(AreaSquareMeters);
    }

    internal static class ConeProjectionMath
    {
        /// <summary>
        /// Intersects differential rays with a tangent plane through range*ray. This
        /// naturally enlarges a pixel footprint with range and grazing incidence.
        /// </summary>
        internal static bool TrySurfaceFootprint(Vector3 normalizedRay,
            Vector3 rayDifferentialX, Vector3 rayDifferentialY, float rangeMeters,
            Vector3 surfaceNormal, out MetricConeFootprint footprint)
        {
            footprint = default;
            if (!(rangeMeters > 0f) || normalizedRay.sqrMagnitude < 0.99f ||
                surfaceNormal.sqrMagnitude < 1e-8f)
                return false;

            Vector3 ray = normalizedRay.normalized;
            Vector3 normal = surfaceNormal.normalized;
            float signedIncidence = Vector3.Dot(normal, ray);
            float incidence = Mathf.Abs(signedIncidence);
            if (!(incidence > 1e-4f))
                return false;

            Vector3 axisX = rangeMeters * (rayDifferentialX -
                ray * (Vector3.Dot(normal, rayDifferentialX) / signedIncidence));
            Vector3 axisY = rangeMeters * (rayDifferentialY -
                ray * (Vector3.Dot(normal, rayDifferentialY) / signedIncidence));
            float g00 = Vector3.Dot(axisX, axisX);
            float g01 = Vector3.Dot(axisX, axisY);
            float g11 = Vector3.Dot(axisY, axisY);
            float area = Vector3.Cross(axisX, axisY).magnitude;
            if (!float.IsFinite(area) || !(area > 0f))
                return false;

            footprint = new MetricConeFootprint(axisX, axisY,
                new Vector3(g00, g01, g11), area, incidence);
            return true;
        }

        internal static bool TryWorldToPixel(GpuImageView view, Vector3 worldPoint,
            out Vector2 pixel, out float viewZ)
        {
            pixel = default;
            viewZ = 0f;
            if (!view.IsValid)
                return false;
            Vector3 local = Quaternion.Inverse(view.WorldFromCamera.rotation) *
                            (worldPoint - view.WorldFromCamera.position);
            viewZ = local.z;
            if (!(viewZ > 1e-6f))
                return false;
            RigIntrinsics intrinsics = view.Intrinsics;
            pixel = new Vector2(
                local.x / viewZ * intrinsics.FocalLength.x +
                intrinsics.PrincipalPoint.x,
                local.y / viewZ * intrinsics.FocalLength.y +
                intrinsics.PrincipalPoint.y);
            return float.IsFinite(pixel.x) && float.IsFinite(pixel.y) &&
                   pixel.x >= 0f && pixel.y >= 0f &&
                   pixel.x < intrinsics.ImageResolution.x &&
                   pixel.y < intrinsics.ImageResolution.y;
        }

        internal static bool TryReproject(GpuImageView source, Vector2 sourcePixel,
            float sourceRangeMeters, GpuImageView target, out Vector2 targetPixel,
            out float targetViewZ)
        {
            targetPixel = default;
            targetViewZ = 0f;
            if (!source.IsValid || !target.IsValid || !(sourceRangeMeters > 0f))
                return false;
            Vector3 localRay = RayAtImagePoint(source.Intrinsics, sourcePixel);
            Vector3 worldPoint = source.WorldFromCamera.position +
                source.WorldFromCamera.rotation * (localRay * sourceRangeMeters);
            return TryWorldToPixel(target, worldPoint, out targetPixel,
                out targetViewZ);
        }

        private static Vector3 RayAtImagePoint(RigIntrinsics intrinsics, Vector2 pixel) =>
            new Vector3(
                (pixel.x - intrinsics.PrincipalPoint.x) / intrinsics.FocalLength.x,
                (pixel.y - intrinsics.PrincipalPoint.y) / intrinsics.FocalLength.y,
                1f).normalized;
    }
}
