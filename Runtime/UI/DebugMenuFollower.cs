using UnityEngine;

namespace Genesis.RoomScan.UI
{
    /// <summary>
    /// Keeps the debug menu above the left controller while its plane remains
    /// perpendicular to the user's view.  The menu is a disposable UI readout;
    /// hiding it never changes scanner or carrier state.
    /// </summary>
    public class DebugMenuFollower : MonoBehaviour
    {
        [Header("Placement")]
        [SerializeField, Tooltip("View-up offset above the left controller (meters)")]
        private float verticalOffset = 0.18f;

        [SerializeField, Tooltip("Small offset toward the view to clear the controller")]
        private float viewOffset = 0.04f;

        [SerializeField, Tooltip("Controller-follow speed (higher = snappier)")]
        private float followSpeed = 18f;

        private Transform _cam;
        private bool _tracking;
        private static OVRPlugin.HandState _leftHandState = new();

        public bool IsTracking => _tracking;

        private void OnEnable()
        {
            _cam = Camera.main != null ? Camera.main.transform : null;
        }

        private void LateUpdate()
        {
            if (!_tracking || _cam == null ||
                !TryGetLeftControllerPosition(out Vector3 controllerPosition))
                return;

            Vector3 target = ComputeTargetPosition(controllerPosition);
            float blend = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, target, blend);
            FaceView();
        }

        /// <summary>
        /// Instantly places the panel above the left controller.
        /// </summary>
        public void SnapToLeftController()
        {
            if (_cam == null)
                _cam = Camera.main != null ? Camera.main.transform : null;
            if (_cam == null) return;

            _tracking = true;
            if (TryGetLeftControllerPosition(out Vector3 controllerPosition))
                transform.position = ComputeTargetPosition(controllerPosition);
            FaceView();
        }

        // Kept as a source-compatible UI hook; semantics are controller-native.
        public void SnapToView() => SnapToLeftController();

        public void StopTracking()
        {
            _tracking = false;
        }

        internal static Vector3 ControllerPanelPosition(
            Vector3 controllerPosition, Vector3 viewUp, Vector3 viewForward,
            float upOffset, float towardViewOffset) => controllerPosition +
            viewUp.normalized * upOffset +
            viewForward.normalized * towardViewOffset;

        private Vector3 ComputeTargetPosition(Vector3 controllerPosition) =>
            ControllerPanelPosition(controllerPosition, _cam.up, _cam.forward,
                verticalOffset, viewOffset);

        private void FaceView()
        {
            Vector3 awayFromView = transform.position - _cam.position;
            if (awayFromView.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(awayFromView,
                    _cam.up);
        }

        private bool TryGetLeftControllerPosition(out Vector3 position)
        {
            OVRInput.Controller controller = OVRInput
                .GetActiveControllerForHand(OVRInput.Handedness.LeftHanded);
            if (controller == OVRInput.Controller.None)
            {
                position = default;
                return false;
            }

            Vector3 localPosition;
            Quaternion localRotation;
            if (controller == OVRInput.Controller.LHand)
            {
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render,
                        OVRPlugin.Hand.HandLeft, ref _leftHandState))
                {
                    position = default;
                    return false;
                }
                localPosition = _leftHandState.PointerPose.Position
                    .FromFlippedZVector3f();
                localRotation = _leftHandState.PointerPose.Orientation
                    .FromFlippedZQuatf();
            }
            else
            {
                if (!OVRInput.GetControllerPositionTracked(controller))
                {
                    position = default;
                    return false;
                }
                localPosition = OVRInput.GetLocalControllerPosition(controller);
                localRotation = OVRInput.GetLocalControllerRotation(controller);
            }

            var pose = new OVRPose
            {
                position = localPosition,
                orientation = localRotation,
            }.ToWorldSpacePose(_cam.GetComponent<Camera>());
            position = pose.position;
            return true;
        }
    }
}
