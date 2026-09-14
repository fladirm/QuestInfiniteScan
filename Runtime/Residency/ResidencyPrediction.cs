using UnityEngine;

namespace FinalScan.Residency
{
    /// <summary>
    /// Pure helpers for the position-only residency center (contract §12.2/§12.3). No orientation input exists
    /// by construction: callers can only pass position and linear velocity.
    /// </summary>
    public static class ResidencyPrediction
    {
        public const float DefaultHorizonSec = 0.5f;
        public const float DefaultMaxLeadM = 1.0f;

        /// <summary>predicted = p + clamp(v * horizon, maxLead). NaN/inf velocity is treated as zero.</summary>
        public static Vector3 PredictCenter(Vector3 position, Vector3 velocity, float horizonSec = DefaultHorizonSec, float maxLeadM = DefaultMaxLeadM)
        {
            if (!IsFinite(velocity)) velocity = Vector3.zero;
            Vector3 lead = velocity * Mathf.Max(0f, horizonSec);
            float len = lead.magnitude;
            if (len > maxLeadM && len > 0f) lead *= maxLeadM / len;
            return position + lead;
        }

        /// <summary>Exponential moving average of a finite-difference velocity; returns previous when dt is unusable.</summary>
        public static Vector3 UpdateVelocity(Vector3 previousVelocity, Vector3 previousPos, Vector3 pos, float dt, float smoothing = 0.25f)
        {
            if (!(dt > 1e-4f) || !IsFinite(pos) || !IsFinite(previousPos)) return previousVelocity;
            Vector3 raw = (pos - previousPos) / dt;
            if (raw.sqrMagnitude > 100f) raw = raw.normalized * 10f;   // 10 m/s cap: tracking jumps are not motion
            float a = Mathf.Clamp01(smoothing);
            return previousVelocity + (raw - previousVelocity) * a;
        }

        /// <summary>Signed yaw rate in degrees per second between two rotations (about world up).</summary>
        public static float YawRateDegPerSec(Quaternion previous, Quaternion current, float dt)
        {
            if (!(dt > 1e-4f)) return 0f;
            float y0 = previous.eulerAngles.y, y1 = current.eulerAngles.y;
            return Mathf.DeltaAngle(y0, y1) / dt;
        }

        public static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
