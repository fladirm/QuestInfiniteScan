using System;
using UnityEngine;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// Pure managed quaternion/pose math for the sensor authority. UnityEngine.Quaternion.Slerp/Inverse/AngleAxis are
    /// engine-native (extern) calls, which makes them unusable in host tests and in deterministic replay; everything
    /// the rings and the pairer need is therefore implemented here on plain floats.
    /// </summary>
    public static class SensorMath
    {
        public const double NsPerSecond = 1e9;
        public const long MsToNs = 1_000_000L;

        public static Quaternion Normalize(Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (n < 1e-12f) return Quaternion.identity;
            float inv = 1f / n;
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        /// <summary>Inverse of a unit quaternion (conjugate).</summary>
        public static Quaternion Inverse(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        /// <summary>Spherical interpolation with shortest-path sign handling; t may lie outside [0,1] (extrapolation).</summary>
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            float d = Dot(a, b);
            if (d < 0f) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); d = -d; }
            if (d > 0.9995f)
            {
                var r = new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
                return Normalize(r);
            }
            float theta0 = Mathf.Acos(Mathf.Clamp(d, -1f, 1f));
            float theta = theta0 * t;
            float sin0 = Mathf.Sin(theta0);
            float s1 = Mathf.Sin(theta0 - theta) / sin0;
            float s2 = Mathf.Sin(theta) / sin0;
            return Normalize(new Quaternion(a.x * s1 + b.x * s2, a.y * s1 + b.y * s2, a.z * s1 + b.z * s2, a.w * s1 + b.w * s2));
        }

        /// <summary>Rotation of <paramref name="angleDeg"/> degrees about <paramref name="axis"/>.</summary>
        public static Quaternion AngleAxis(float angleDeg, Vector3 axis)
        {
            float len = axis.magnitude;
            if (len < 1e-12f) return Quaternion.identity;
            axis /= len;
            float half = angleDeg * Mathf.Deg2Rad * 0.5f;
            float s = Mathf.Sin(half);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(half));
        }

        /// <summary>Angle in degrees between two rotations.</summary>
        public static float AngleDeg(Quaternion a, Quaternion b)
        {
            float d = Mathf.Min(Mathf.Abs(Dot(a, b)), 1f);
            return d > 0.999999f ? 0f : Mathf.Acos(d) * 2f * Mathf.Rad2Deg;
        }

        /// <summary>Rotation vector (axis * angle in radians) of a unit quaternion, shortest arc.</summary>
        public static Vector3 RotationVector(Quaternion q)
        {
            if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            float s = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            if (s < 1e-9f) return Vector3.zero;
            float angle = 2f * Mathf.Atan2(s, q.w);
            return new Vector3(q.x, q.y, q.z) * (angle / s);
        }

        /// <summary>Angular velocity (rad/s, world frame) taking rotation <paramref name="a"/> to <paramref name="b"/> over <paramref name="dtSeconds"/>.</summary>
        public static Vector3 AngularVelocity(Quaternion a, Quaternion b, double dtSeconds)
        {
            if (dtSeconds <= 0) return Vector3.zero;
            Quaternion delta = Normalize(b * Inverse(a));
            return RotationVector(delta) / (float)dtSeconds;
        }

        public static Pose Lerp(in Pose a, in Pose b, float t)
            => new Pose(a.position + (b.position - a.position) * t, Slerp(a.rotation, b.rotation, t));

        /// <summary>Composes parent * child (child expressed in parent frame).</summary>
        public static Pose Mul(in Pose parent, in Pose child)
            => new Pose(parent.position + parent.rotation * child.position, Normalize(parent.rotation * child.rotation));

        public static Pose InversePose(in Pose p)
        {
            Quaternion inv = Inverse(p.rotation);
            return new Pose(inv * (-p.position), inv);
        }

        /// <summary>Pose of <paramref name="b"/> expressed in the frame of <paramref name="a"/> (a^-1 * b).</summary>
        public static Pose Relative(in Pose a, in Pose b) => Mul(InversePose(a), b);

        public static bool IsFinite(in Pose p)
            => IsFinite(p.position) && !float.IsNaN(p.rotation.x) && !float.IsNaN(p.rotation.y) && !float.IsNaN(p.rotation.z) && !float.IsNaN(p.rotation.w)
               && Mathf.Abs(Dot(p.rotation, p.rotation) - 1f) < 1e-2f;

        public static bool IsFinite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        /// <summary>Median of a small buffer (copies; intended for ≤ 64 elements).</summary>
        public static double Median(double[] values, int count)
        {
            if (count <= 0) return double.NaN;
            var tmp = new double[count]; Array.Copy(values, tmp, count); Array.Sort(tmp);
            return (count & 1) == 1 ? tmp[count / 2] : 0.5 * (tmp[count / 2 - 1] + tmp[count / 2]);
        }
    }
}
