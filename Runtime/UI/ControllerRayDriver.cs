using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.UI
{
    /// <summary>Donor-proven right-controller UI pointer with laser and cursor feedback.</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OVRInputModule))]
    public sealed class ControllerRayDriver : MonoBehaviour
    {
        [SerializeField] private float rayStartOffset = 0.05f;
        [SerializeField] private float maxLength = 5f;
        [SerializeField] private float beamWidth = 0.003f;
        [SerializeField] private Color idleColor = new(0.25f, 0.85f, 1f, 0.65f);
        [SerializeField] private Color hoverColor = new(0.1f, 1f, 0.65f, 0.95f);
        [SerializeField] private float cursorRadius = 0.006f;
        [SerializeField] private Color cursorColor = new(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color fineIdleColor =
            new(0.15f, 0.8f, 1f, 0.12f);
        [SerializeField] private Color fineRefineColor =
            new(0.1f, 1f, 0.45f, 0.18f);
        [SerializeField] private Color fineEraseColor =
            new(1f, 0.15f, 0.2f, 0.2f);
        [SerializeField] internal Shader overlayShader;

        private OVRInputModule _inputModule;
        private Transform _rayHelper;
        private LineRenderer _line;
        private GameObject _cursor;
        private MeshRenderer _cursorRenderer;
        private Material _overlayMaterial;
        private MaterialPropertyBlock _cursorProperties;
        private GameObject _fineCursor;
        private MeshRenderer _fineCursorRenderer;
        private MaterialPropertyBlock _fineProperties;
        private bool _fineTargetVisible;
        private bool _pointingAtUi;
        private bool _hoveringUi;
        private Vector3 _uiHitPoint;
        private bool _uiTriggerCaptured;
        private Vector3 _fineTargetPosition;
        private FineBrushOperation _fineTargetOperation;
        private bool _hasTrackedPose;
        private int _uiLayerMask;

        private static OVRPlugin.HandState _leftHandState = new();
        private static OVRPlugin.HandState _rightHandState = new();
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public Shader OverlayShader => overlayShader;
        public bool HasTrackedPose => _hasTrackedPose;
        internal bool IsPointingAtUi => _pointingAtUi;

        private void Awake()
        {
            _inputModule = GetComponent<OVRInputModule>();
            _rayHelper = new GameObject("ControllerRayHelper").transform;
            _rayHelper.SetParent(transform, false);
            _inputModule.rayTransform = _rayHelper;
            _inputModule.joyPadClickButton = OVRInput.Button.SecondaryIndexTrigger;
            _uiLayerMask = LayerMask.GetMask("UI");

            if (overlayShader == null)
            {
                Logger.Error("ControllerRayDriver: shader is not wired; pointer remains active.");
                return;
            }
            _overlayMaterial = new Material(overlayShader)
            {
                name = "Merkaba Controller Ray (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            SetupLineRenderer();
            SetupCursor();
            SetupFinePreview();
        }

        private void Update()
        {
            _hasTrackedPose = TryUpdateRayOrigin();
            if (_line != null) _line.enabled = _hasTrackedPose;
            if (!_hasTrackedPose)
            {
                _pointingAtUi = false;
                _hoveringUi = false;
                _uiTriggerCaptured = false;
                if (_cursor != null) _cursor.SetActive(false);
                return;
            }
            RefreshUiAuthority();
        }

        private void LateUpdate()
        {
            if (_hasTrackedPose) DrawLaser();
        }

        private void OnDestroy()
        {
            if (_rayHelper != null) Destroy(_rayHelper.gameObject);
            if (_cursor != null) Destroy(_cursor);
            if (_fineCursor != null) Destroy(_fineCursor);
            if (_overlayMaterial != null) Destroy(_overlayMaterial);
        }

        internal bool TryGetWorldRay(out Vector3 origin, out Vector3 direction)
        {
            if (!_hasTrackedPose || _rayHelper == null)
            {
                origin = default;
                direction = default;
                return false;
            }
            origin = _rayHelper.position;
            direction = _rayHelper.forward.normalized;
            return direction.sqrMagnitude > 0.99f;
        }

        internal bool TryGetWorldPose(out Vector3 position,
            out Quaternion rotation) => TryGetWorldPose(
                OVRInput.Handedness.RightHanded, out position, out rotation);

        internal bool TryGetLeftWorldPose(out Vector3 position,
            out Quaternion rotation) => TryGetWorldPose(
                OVRInput.Handedness.LeftHanded, out position, out rotation);

        internal Color GetFineBrushPreviewColor(FineBrushOperation operation) =>
            operation switch
            {
                FineBrushOperation.Refine => fineRefineColor,
                FineBrushOperation.Erase => fineEraseColor,
                _ => fineIdleColor
            };

        internal void SetFineBrushPreview(FineBrushDescriptor descriptor,
            FineBrushOperation operation, bool cursorOnSurface = false)
        {
            bool visible = descriptor.IsActive && _fineCursor != null;
            _fineTargetVisible = visible && cursorOnSurface;
            _fineTargetPosition = descriptor.CursorPosition;
            _fineTargetOperation = operation;
            if (_fineCursor != null)
                _fineCursor.SetActive(_fineTargetVisible);
            if (!_fineTargetVisible) return;

            Vector3 axis = descriptor.Axis.normalized;
            _fineCursor.transform.SetPositionAndRotation(
                descriptor.CursorPosition + axis * (descriptor.Length * 0.5f),
                Quaternion.FromToRotation(Vector3.up, axis));
            _fineCursor.transform.localScale = new Vector3(
                descriptor.Radius * 2f, descriptor.Length * 0.5f,
                descriptor.Radius * 2f);

            Color color = GetFineBrushPreviewColor(operation);
            _fineProperties ??= new MaterialPropertyBlock();
            Color cursorTint = color;
            cursorTint.a = Mathf.Clamp01(color.a * 0.5f);
            _fineProperties.SetColor(ColorId, cursorTint);
            _fineCursorRenderer.SetPropertyBlock(_fineProperties);
        }

        private bool TryUpdateRayOrigin()
        {
            if (_rayHelper == null || !TryGetWorldPose(
                    OVRInput.Handedness.RightHanded, out Vector3 position,
                    out Quaternion rotation))
                return false;
            _rayHelper.SetPositionAndRotation(position, rotation);
            return true;
        }

        private static bool TryGetWorldPose(OVRInput.Handedness handedness,
            out Vector3 position, out Quaternion rotation)
        {
            OVRInput.Controller controller =
                OVRInput.GetActiveControllerForHand(handedness);
            if (controller == OVRInput.Controller.None)
            {
                position = default;
                rotation = default;
                return false;
            }
            bool hand = controller is OVRInput.Controller.LHand or
                OVRInput.Controller.RHand;
            Vector3 localPosition;
            Quaternion localRotation;
            if (hand)
            {
                OVRPlugin.Hand which = controller == OVRInput.Controller.LHand
                    ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;
                ref OVRPlugin.HandState handState = ref (which ==
                    OVRPlugin.Hand.HandLeft ? ref _leftHandState :
                    ref _rightHandState);
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, which,
                        ref handState))
                {
                    position = default;
                    rotation = default;
                    return false;
                }
                localPosition = handState.PointerPose.Position.FromFlippedZVector3f();
                localRotation = handState.PointerPose.Orientation.FromFlippedZQuatf();
            }
            else
            {
                if (!OVRInput.GetControllerPositionTracked(controller) ||
                    !OVRInput.GetControllerOrientationTracked(controller))
                {
                    position = default;
                    rotation = default;
                    return false;
                }
                localPosition = OVRInput.GetLocalControllerPosition(controller);
                localRotation = OVRInput.GetLocalControllerRotation(controller);
            }
            Camera camera = Camera.main;
            if (camera == null)
            {
                position = default;
                rotation = default;
                return false;
            }
            OVRPose pose = new OVRPose
            {
                position = localPosition,
                orientation = localRotation
            }.ToWorldSpacePose(camera);
            position = pose.position;
            rotation = pose.orientation;
            return true;
        }

        private void SetupLineRenderer()
        {
            _line = GetComponent<LineRenderer>() ?? gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.startWidth = beamWidth;
            _line.endWidth = beamWidth * 0.5f;
            _line.sharedMaterial = _overlayMaterial;
            _line.startColor = _line.endColor = idleColor;
            _line.useWorldSpace = true;
            _line.receiveShadows = false;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.enabled = false;
        }

        private void SetupCursor()
        {
            _cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cursor.name = "RayCursor";
            _cursor.transform.localScale = Vector3.one * (cursorRadius * 2f);
            Collider collider = _cursor.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _cursorRenderer = _cursor.GetComponent<MeshRenderer>();
            _cursorRenderer.sharedMaterial = _overlayMaterial;
            _cursorProperties = new MaterialPropertyBlock();
            SetCursorColor(cursorColor);
            _cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cursorRenderer.receiveShadows = false;
            _cursor.SetActive(false);
        }

        private void SetupFinePreview()
        {
            _fineCursor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _fineCursor.name = "FineBrushCursor";
            _fineCursor.transform.localScale = new Vector3(0.018f, 0.001f,
                0.018f);
            Collider collider = _fineCursor.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _fineCursorRenderer = _fineCursor.GetComponent<MeshRenderer>();
            _fineCursorRenderer.sharedMaterial = _overlayMaterial;
            _fineCursorRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            _fineCursorRenderer.receiveShadows = false;
            _fineCursor.SetActive(false);
        }

        private void DrawLaser()
        {
            Vector3 origin = _rayHelper.position;
            Vector3 direction = _rayHelper.forward;
            Vector3 start = origin + direction * rayStartOffset;
            Vector3 end = start + direction * maxLength;
            bool hovering = _hoveringUi;
            if (hovering) end = _uiHitPoint;
            else if (_fineTargetVisible)
                end = _fineTargetPosition;
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);
            Color color = hovering ? hoverColor : _fineTargetVisible
                ? GetFineBrushPreviewColor(_fineTargetOperation) : idleColor;
            _line.startColor = _line.endColor = color;
            if (_cursor == null) return;
            _cursor.SetActive(hovering);
            if (!hovering) return;
            _cursor.transform.position = end;
            _cursor.transform.LookAt(_rayHelper);
            SetCursorColor(hoverColor);
        }

        private void RefreshUiAuthority()
        {
            Vector3 origin = _rayHelper.position;
            Vector3 direction = _rayHelper.forward;
            _hoveringUi = Physics.Raycast(origin, direction,
                    out RaycastHit hit, maxLength + rayStartOffset,
                    _uiLayerMask, QueryTriggerInteraction.Collide) &&
                hit.collider.GetComponentInParent<UIDocument>() != null;
            if (_hoveringUi) _uiHitPoint = hit.point;
            if (_hoveringUi && OVRInput.GetDown(
                    OVRInput.Button.SecondaryIndexTrigger))
                _uiTriggerCaptured = true;
            if (!OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger))
                _uiTriggerCaptured = false;
            _pointingAtUi = _hoveringUi || _uiTriggerCaptured;
        }

        private void SetCursorColor(Color color)
        {
            if (_cursorRenderer == null) return;
            _cursorProperties ??= new MaterialPropertyBlock();
            _cursorProperties.SetColor(ColorId, color);
            _cursorRenderer.SetPropertyBlock(_cursorProperties);
        }
    }
}
