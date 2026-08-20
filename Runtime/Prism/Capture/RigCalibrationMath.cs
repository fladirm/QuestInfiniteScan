using System;
using Meta.XR;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Genesis.RoomScan.Prism
{
    internal static class RigCalibrationMath
    {
        internal readonly struct ConeRayReference
        {
            internal ConeRayReference(Vector3 center, Vector3 differentialX,
                Vector3 differentialY, float halfAngleX, float halfAngleY,
                float solidAngle)
            {
                Center = center;
                DifferentialX = differentialX;
                DifferentialY = differentialY;
                HalfAngleX = halfAngleX;
                HalfAngleY = halfAngleY;
                SolidAngle = solidAngle;
            }

            internal Vector3 Center { get; }
            internal Vector3 DifferentialX { get; }
            internal Vector3 DifferentialY { get; }
            internal float HalfAngleX { get; }
            internal float HalfAngleY { get; }
            internal float SolidAngle { get; }
        }

        internal static RigIntrinsics FromPassthrough(PassthroughCameraAccess access)
        {
            PassthroughCameraAccess.CameraIntrinsics source = access.Intrinsics;
            Vector2 sensor = source.SensorResolution;
            Vector2 image = access.CurrentResolution;
            Vector2 scale = new(image.x / sensor.x, image.y / sensor.y);
            scale /= Mathf.Max(scale.x, scale.y);
            var crop = new Rect(
                sensor.x * (1f - scale.x) * 0.5f,
                sensor.y * (1f - scale.y) * 0.5f,
                sensor.x * scale.x,
                sensor.y * scale.y);

            Vector2 imageScale = new(image.x / crop.width, image.y / crop.height);
            Vector2 focal = Vector2.Scale(source.FocalLength, imageScale);
            Vector2 principal = Vector2.Scale(source.PrincipalPoint - crop.position,
                imageScale);
            Vector4 fov = IntrinsicsToFov(focal, principal, access.CurrentResolution);
            ulong signature = Signature(focal, principal, source.SensorResolution,
                access.CurrentResolution, source.LensOffset, fov);
            return new RigIntrinsics(focal, principal, source.SensorResolution,
                access.CurrentResolution, source.LensOffset, fov, signature);
        }

        internal static RigIntrinsics FromDepthFov(XRFov fov, Vector2Int resolution)
        {
            float tanLeft = Mathf.Tan(fov.angleLeft);
            float tanRight = Mathf.Tan(fov.angleRight);
            float tanUp = Mathf.Tan(fov.angleUp);
            float tanDown = Mathf.Tan(fov.angleDown);
            float dx = tanRight - tanLeft;
            float dy = tanUp - tanDown;
            if (!(dx > 0f) || !(dy > 0f))
                return default;

            var focal = new Vector2(resolution.x / dx, resolution.y / dy);
            var principal = new Vector2(-focal.x * tanLeft, -focal.y * tanDown);
            Vector4 fovVector = fov.AsVector4();
            Pose noStaticHeadExtrinsic = Pose.identity;
            ulong signature = Signature(focal, principal, resolution, resolution,
                noStaticHeadExtrinsic, fovVector);
            return new RigIntrinsics(focal, principal, resolution, resolution,
                noStaticHeadExtrinsic, fovVector, signature);
        }

        internal static ulong CombineSignatures(ulong rgbLeft, ulong rgbRight,
            ulong depthLeft, ulong depthRight)
        {
            ulong hash = FnvOffset;
            Add(ref hash, rgbLeft);
            Add(ref hash, rgbRight);
            Add(ref hash, depthLeft);
            Add(ref hash, depthRight);
            return hash == 0UL ? 1UL : hash;
        }

        /// <summary>CPU reference for the immutable GPU cone LUT.</summary>
        internal static ConeRayReference ConeRayAtPixel(RigIntrinsics intrinsics,
            int pixelX, int pixelY)
        {
            if (!intrinsics.IsValid || pixelX < 0 || pixelY < 0 ||
                pixelX >= intrinsics.ImageResolution.x ||
                pixelY >= intrinsics.ImageResolution.y)
                return default;

            Vector2 p = new(pixelX + 0.5f, pixelY + 0.5f);
            Vector3 unnormalized = new(
                (p.x - intrinsics.PrincipalPoint.x) / intrinsics.FocalLength.x,
                (p.y - intrinsics.PrincipalPoint.y) / intrinsics.FocalLength.y,
                1f);
            float length = unnormalized.magnitude;
            Vector3 center = unnormalized / length;
            Vector3 dxInput = new(1f / intrinsics.FocalLength.x, 0f, 0f);
            Vector3 dyInput = new(0f, 1f / intrinsics.FocalLength.y, 0f);
            Vector3 dx = (dxInput - center * Vector3.Dot(center, dxInput)) / length;
            Vector3 dy = (dyInput - center * Vector3.Dot(center, dyInput)) / length;

            Vector3 minusX = RayAtImagePoint(intrinsics, p + Vector2.left * 0.5f);
            Vector3 plusX = RayAtImagePoint(intrinsics, p + Vector2.right * 0.5f);
            Vector3 minusY = RayAtImagePoint(intrinsics, p + Vector2.down * 0.5f);
            Vector3 plusY = RayAtImagePoint(intrinsics, p + Vector2.up * 0.5f);
            float halfAngleX = 0.5f * Mathf.Acos(Mathf.Clamp(Vector3.Dot(minusX, plusX), -1f, 1f));
            float halfAngleY = 0.5f * Mathf.Acos(Mathf.Clamp(Vector3.Dot(minusY, plusY), -1f, 1f));
            float solidAngle = Mathf.Abs(Vector3.Dot(center, Vector3.Cross(dx, dy)));
            return new ConeRayReference(center, dx, dy, halfAngleX, halfAngleY,
                solidAngle);
        }

        internal static float ProjectionDepth01FromViewZ(float viewZ, Vector2 nearFar)
        {
            if (!(viewZ > 0f) || !(nearFar.x > 0f) || !(nearFar.y > nearFar.x))
                return 0f;
            return nearFar.y / (nearFar.y - nearFar.x) -
                   (nearFar.y * nearFar.x) /
                   ((nearFar.y - nearFar.x) * viewZ);
        }

        internal static float ViewZFromProjectionDepth01(float rawDepth, Vector2 nearFar)
        {
            if (!(rawDepth > 0f) || !(rawDepth < 1f) ||
                !(nearFar.x > 0f) || !(nearFar.y > nearFar.x))
                return 0f;
            float denominator = nearFar.y - rawDepth * (nearFar.y - nearFar.x);
            return denominator > 1e-8f ? nearFar.x * nearFar.y / denominator : 0f;
        }

        internal static float RangeFromProjectionDepth01(float rawDepth,
            Vector2 nearFar, Vector3 normalizedRay)
        {
            float viewZ = ViewZFromProjectionDepth01(rawDepth, nearFar);
            return viewZ > 0f && normalizedRay.z > 1e-6f
                ? viewZ / normalizedRay.z
                : 0f;
        }

        private static Vector3 RayAtImagePoint(RigIntrinsics intrinsics, Vector2 p)
        {
            return new Vector3(
                (p.x - intrinsics.PrincipalPoint.x) / intrinsics.FocalLength.x,
                (p.y - intrinsics.PrincipalPoint.y) / intrinsics.FocalLength.y,
                1f).normalized;
        }

        private static Vector4 IntrinsicsToFov(Vector2 focal, Vector2 principal,
            Vector2Int resolution)
        {
            float left = Mathf.Atan(-principal.x / focal.x);
            float right = Mathf.Atan((resolution.x - principal.x) / focal.x);
            float down = Mathf.Atan(-principal.y / focal.y);
            float up = Mathf.Atan((resolution.y - principal.y) / focal.y);
            return new Vector4(left, right, up, down);
        }

        private static ulong Signature(Vector2 focal, Vector2 principal,
            Vector2Int sensor, Vector2Int image, Pose headFromSensor, Vector4 fov)
        {
            ulong hash = FnvOffset;
            Add(ref hash, focal.x); Add(ref hash, focal.y);
            Add(ref hash, principal.x); Add(ref hash, principal.y);
            Add(ref hash, sensor.x); Add(ref hash, sensor.y);
            Add(ref hash, image.x); Add(ref hash, image.y);
            Add(ref hash, headFromSensor.position.x); Add(ref hash, headFromSensor.position.y);
            Add(ref hash, headFromSensor.position.z); Add(ref hash, headFromSensor.rotation.x);
            Add(ref hash, headFromSensor.rotation.y); Add(ref hash, headFromSensor.rotation.z);
            Add(ref hash, headFromSensor.rotation.w);
            Add(ref hash, fov.x); Add(ref hash, fov.y); Add(ref hash, fov.z); Add(ref hash, fov.w);
            return hash == 0UL ? 1UL : hash;
        }

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static void Add(ref ulong hash, float value) => Add(ref hash,
            unchecked((ulong)(uint)BitConverter.SingleToInt32Bits(value)));

        private static void Add(ref ulong hash, int value) => Add(ref hash,
            unchecked((ulong)(uint)value));

        private static void Add(ref ulong hash, ulong value)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= FnvPrime;
            }
        }
    }
}
