using UnityEngine;

namespace Genesis.RoomScan.UI
{
    /// <summary>Tracks the UX above the left controller with wrist-relative orientation.</summary>
    public sealed class DebugMenuFollower : MonoBehaviour
    {
        [SerializeField] private float verticalOffset = 0.18f;
        [SerializeField] private float viewOffset = 0.04f;
        [SerializeField] private float followSpeed = 18f;
        [SerializeField, Range(0.25f, 1f)] private float menuScale = 0.75f;

        private Transform _camera;
        private bool _tracking;
        private Quaternion _controllerToPanelRotation = Quaternion.identity;
        private Vector3 _authoredScale;
        private static OVRPlugin.HandState _leftHandState = new();

        public bool IsTracking => _tracking;

        private void Awake()
        {
            _authoredScale = transform.localScale;
            transform.localScale = _authoredScale * menuScale;
        }

        private void OnEnable() =>
            _camera = Camera.main != null ? Camera.main.transform : null;

        private void LateUpdate()
        {
            if (!_tracking) return;
            if (_camera == null)
                _camera = Camera.main != null ? Camera.main.transform : null;
            if (_camera == null ||
                !TryGetLeftControllerPose(out Vector3 controllerPosition,
                    out Quaternion controllerRotation))
                return;
            Vector3 target = ControllerPanelPosition(controllerPosition, _camera.up,
                _camera.forward, verticalOffset, viewOffset);
            float blend = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, target, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                controllerRotation * _controllerToPanelRotation, blend);
        }

        public void SnapToLeftController()
        {
            if (_camera == null)
                _camera = Camera.main != null ? Camera.main.transform : null;
            if (_camera == null) return;
            _tracking = true;
            if (TryGetLeftControllerPose(out Vector3 position,
                    out Quaternion rotation))
            {
                transform.position = ControllerPanelPosition(position, _camera.up,
                    _camera.forward, verticalOffset, viewOffset);
                FaceView();
                _controllerToPanelRotation = Quaternion.Inverse(rotation) *
                    transform.rotation;
            }
        }

        public void SnapToView() => SnapToLeftController();
        public void StopTracking() => _tracking = false;

        internal static Vector3 ControllerPanelPosition(Vector3 controllerPosition,
            Vector3 viewUp, Vector3 viewForward, float upOffset, float towardViewOffset) =>
            controllerPosition + viewUp.normalized * upOffset +
            viewForward.normalized * towardViewOffset;

        private void FaceView()
        {
            Vector3 away = transform.position - _camera.position;
            if (away.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(away, _camera.up);
        }

        private bool TryGetLeftControllerPose(out Vector3 position,
            out Quaternion rotation)
        {
            OVRInput.Controller controller = OVRInput.GetActiveControllerForHand(
                OVRInput.Handedness.LeftHanded);
            if (controller == OVRInput.Controller.None)
            {
                position = default;
                rotation = default;
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
                    rotation = default;
                    return false;
                }
                localPosition = _leftHandState.PointerPose.Position.FromFlippedZVector3f();
                localRotation = _leftHandState.PointerPose.Orientation.FromFlippedZQuatf();
            }
            else
            {
                if (!OVRInput.GetControllerPositionTracked(controller))
                {
                    position = default;
                    rotation = default;
                    return false;
                }
                localPosition = OVRInput.GetLocalControllerPosition(controller);
                localRotation = OVRInput.GetLocalControllerRotation(controller);
            }
            OVRPose pose = new OVRPose
            {
                position = localPosition,
                orientation = localRotation
            }.ToWorldSpacePose(_camera.GetComponent<Camera>());
            position = pose.position;
            rotation = pose.orientation;
            return true;
        }
    }
}
