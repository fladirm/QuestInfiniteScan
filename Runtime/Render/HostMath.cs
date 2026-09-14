using System;
using UnityEngine;

namespace FinalScan.Render
{
    /// <summary>
    /// Pure conversions between Unity types and the float arrays of the ABI 2 host API. Convention (documented for
    /// Native~/src/host/render): every 16-float matrix is column-major, i.e. element [c*4 + r] = M[r][c]; this is
    /// Unity's own Matrix4x4 memory order (m00,m10,m20,m30,m01,...). View matrices are world→view (Unity camera
    /// convention, right-handed view space with -Z forward); projection matrices are what the GPU consumes
    /// (GL.GetGPUProjectionMatrix(proj, renderIntoTexture:true)) so the native cull sees the same clip space as the
    /// Unity draw (reversed-Z on Vulkan, y flipped for render textures).
    /// </summary>
    public static class HostMath
    {
        public const double DisplayPeriod72HzMs = 1000.0 / 72.0;   // 13.888.. ms, contract §3.1

        /// <summary>Writes a Matrix4x4 into 16 floats, column-major.</summary>
        public static void ToColumnMajor(in Matrix4x4 m, float[] dst, int offset = 0)
        {
            if (dst == null || dst.Length < offset + 16) throw new ArgumentException("need 16 floats", nameof(dst));
            for (int c = 0; c < 4; c++)
            {
                Vector4 col = m.GetColumn(c);
                dst[offset + c * 4 + 0] = col.x;
                dst[offset + c * 4 + 1] = col.y;
                dst[offset + c * 4 + 2] = col.z;
                dst[offset + c * 4 + 3] = col.w;
            }
        }

        /// <summary>Inverse of <see cref="ToColumnMajor"/> (tests and telemetry).</summary>
        public static Matrix4x4 FromColumnMajor(float[] src, int offset = 0)
        {
            var m = new Matrix4x4();
            for (int c = 0; c < 4; c++)
                m.SetColumn(c, new Vector4(src[offset + c * 4], src[offset + c * 4 + 1], src[offset + c * 4 + 2], src[offset + c * 4 + 3]));
            return m;
        }

        public static void ToArray(in Vector3 v, float[] dst) { dst[0] = v.x; dst[1] = v.y; dst[2] = v.z; }
        public static void ToArray(in Quaternion q, float[] dst) { dst[0] = q.x; dst[1] = q.y; dst[2] = q.z; dst[3] = q.w; }

        /// <summary>
        /// Environment Depth field of view as tangents [left, right, up, down] from the XRFov half angles in radians.
        /// The signs are preserved as reported by the provider (left/down negative on Quest: e.g. [-49°, 45°, 48°, -50°]),
        /// so tan(angleLeft) is negative; the native reprojection uses x = tan(angle) * z directly.
        /// </summary>
        public static void FovTangents(float angleLeft, float angleRight, float angleUp, float angleDown, float[] dst4)
        {
            dst4[0] = Mathf.Tan(angleLeft);
            dst4[1] = Mathf.Tan(angleRight);
            dst4[2] = Mathf.Tan(angleUp);
            dst4[3] = Mathf.Tan(angleDown);
        }

        /// <summary>GPU headroom in microseconds against the 72 Hz period; negative means the GPU is over budget.</summary>
        public static int GpuHeadroomUs(double gpuFrameTimeMs, double periodMs = DisplayPeriod72HzMs)
        {
            if (double.IsNaN(gpuFrameTimeMs) || double.IsInfinity(gpuFrameTimeMs) || gpuFrameTimeMs <= 0.0) return (int)(periodMs * 1000.0);
            double us = (periodMs - gpuFrameTimeMs) * 1000.0;
            if (us > int.MaxValue) return int.MaxValue;
            if (us < int.MinValue) return int.MinValue;
            return (int)Math.Round(us);
        }

        /// <summary>Predicted display time: XR-runtime clock now plus one display period (plus optional extra pipeline latency).</summary>
        public static double PredictedDisplayTime(double nowSec, float displayHz, double extraLatencySec = 0.0)
        {
            double period = displayHz > 1f ? 1.0 / displayHz : DisplayPeriod72HzMs / 1000.0;
            return nowSec + period + Math.Max(0.0, extraLatencySec);
        }
    }
}
