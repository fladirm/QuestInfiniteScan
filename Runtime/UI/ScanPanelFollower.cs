using UnityEngine;

namespace FinalScan.UI
{
    /// <summary>
    /// Keeps the scan panel above the LEFT controller (or the left hand's pointer pose when hands are tracked) with
    /// wrist-relative orientation, port of the donor follower: on Show the panel snaps above the controller facing
    /// the view and remembers the controller→panel rotation; afterwards it follows with an exponential blend.
    /// Pose source: OVRInput local controller pose (or OVRPlugin hand state) converted to world space through the
    /// main camera (OVRCameraRig tracking space), i.e. the leftControllerAnchor / left hand anchor pose.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScanPanelFollower : MonoBehaviour
    {
        [SerializeField] float verticalOffset = 0.18f;
        [SerializeField] float viewOffset = 0.04f;
        [SerializeField] float followSpeed = 18f;
        [SerializeField, Range(0.25f, 1f)] float menuScale = 0.75f;

        Transform _camera;
        bool _tracking;
        Quaternion _controllerToPanelRotation = Quaternion.identity;
        Vector3 _authoredScale;
        static OVRPlugin.HandState s_leftHandState = new OVRPlugin.HandState();

        public bool IsTracking => _tracking;
        public bool HasLeftPose { get; private set; }

        void Awake()
        {
            _authoredScale = transform.localScale;
            transform.localScale = _authoredScale * menuScale;
        }

        void OnEnable() => _camera = Camera.main != null ? Camera.main.transform : null;

        void LateUpdate()
        {
            if (!_tracking) return;
            if (_camera == null) _camera = Camera.main != null ? Camera.main.transform : null;
            Vector3 controllerPosition = default; Quaternion controllerRotation = Quaternion.identity;
            HasLeftPose = _camera != null && TryGetLeftControllerPose(out controllerPosition, out controllerRotation);
            if (!HasLeftPose) return;
            Vector3 target = ControllerPanelPosition(controllerPosition, _camera.up, _camera.forward, verticalOffset, viewOffset);
            float blend = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, target, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, controllerRotation * _controllerToPanelRotation, blend);
        }

        public void SnapToLeftController()
        {
            if (_camera == null) _camera = Camera.main != null ? Camera.main.transform : null;
            if (_camera == null) return;
            _tracking = true;
            if (TryGetLeftControllerPose(out Vector3 position, out Quaternion rotation))
            {
                transform.position = ControllerPanelPosition(position, _camera.up, _camera.forward, verticalOffset, viewOffset);
                FaceView();
                _controllerToPanelRotation = Quaternion.Inverse(rotation) * transform.rotation;
                HasLeftPose = true;
            }
            else
            {
                // No controller pose yet (e.g. editor): park the panel in front of the view.
                transform.position = _camera.position + _camera.forward * 0.6f - _camera.up * 0.1f;
                FaceView();
                HasLeftPose = false;
            }
        }

        public void StopTracking() => _tracking = false;

        /// <summary>Pure placement rule shared with tests: above the controller along view-up, nudged toward the view.</summary>
        public static Vector3 ControllerPanelPosition(Vector3 controllerPosition, Vector3 viewUp, Vector3 viewForward, float upOffset, float towardViewOffset) =>
            controllerPosition + viewUp.normalized * upOffset + viewForward.normalized * towardViewOffset;

        void FaceView()
        {
            Vector3 away = transform.position - _camera.position;
            if (away.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(away, _camera.up);
        }

        bool TryGetLeftControllerPose(out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = default;
            OVRInput.Controller controller;
            try { controller = OVRInput.GetActiveControllerForHand(OVRInput.Handedness.LeftHanded); }
            catch (System.Exception) { return false; }
            if (controller == OVRInput.Controller.None) return false;
            Vector3 localPosition; Quaternion localRotation;
            if (controller == OVRInput.Controller.LHand)
            {
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandLeft, ref s_leftHandState)) return false;
                localPosition = s_leftHandState.PointerPose.Position.FromFlippedZVector3f();
                localRotation = s_leftHandState.PointerPose.Orientation.FromFlippedZQuatf();
            }
            else
            {
                if (!OVRInput.GetControllerPositionTracked(controller)) return false;
                localPosition = OVRInput.GetLocalControllerPosition(controller);
                localRotation = OVRInput.GetLocalControllerRotation(controller);
            }
            Camera cam = _camera.GetComponent<Camera>();
            if (cam == null) return false;
            OVRPose pose = new OVRPose { position = localPosition, orientation = localRotation }.ToWorldSpacePose(cam);
            position = pose.position; rotation = pose.orientation;
            return true;
        }
    }
}
