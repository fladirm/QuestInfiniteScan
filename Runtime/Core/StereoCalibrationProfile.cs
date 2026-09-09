using UnityEngine;

namespace Genesis.RoomScan
{
    [CreateAssetMenu(menuName = "Quest Merkaba/Stereo calibration profile")]
    public sealed class StereoCalibrationProfile : ScriptableObject
    {
        public const string HostAssetPath = "Assets/Settings/MerkabaStereoCalibration.asset";

        [Tooltip("Provenance of the measured maximum errors, including sensor/capture configuration.")]
        public string Source;
        [Tooltip("Metres: sensor eye-z error, reprojection error, hypothesis width; w=1 only for calibrated bounds.")]
        public Vector4 DepthLeft, DepthRight;
        [Tooltip("Normal-vector norm error, plane-offset error in metres, reserved=0, calibrated=1.")]
        public Vector4 Plane;
        [Tooltip("Captured linear-radiance error bounds R/G/B; w=1 only for calibrated bounds.")]
        public Vector4 RgbLeft, RgbRight;

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(Source))
            {
                error = "Stereo calibration has no source/provenance.";
                return false;
            }
            if (!ValidateBounds(DepthLeft, nameof(DepthLeft), out error) ||
                !ValidateBounds(DepthRight, nameof(DepthRight), out error) ||
                !ValidateBounds(Plane, nameof(Plane), out error) ||
                !ValidateBounds(RgbLeft, nameof(RgbLeft), out error) ||
                !ValidateBounds(RgbRight, nameof(RgbRight), out error)) return false;
            if (Plane.z != 0f)
            {
                error = "Stereo calibration Plane.z is reserved and must be zero.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool ValidateBounds(Vector4 value, string field, out string error)
        {
            bool valid = value.w == 1f && float.IsFinite(value.x) &&
                float.IsFinite(value.y) && float.IsFinite(value.z) &&
                value.x >= 0f && value.y >= 0f && value.z >= 0f;
            error = valid ? null : $"Stereo calibration {field} is missing or invalid: {value}.";
            return valid;
        }
    }
}
