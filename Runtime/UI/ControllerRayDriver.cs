using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.UI
{
    /// <summary>Donor-proven right-controller UI pointer with laser and cursor feedback.</summary>
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
        private Vector3 _fineTargetPosition;
        private FineBrushOperation _fineTargetOperation;
        private OVRInput.Controller _activeController = OVRInput.Controller.None;
        private bool _hasTrackedPose;
        private int _uiLayerMask;

        private static OVRPlugin.HandState _handState = new();
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public Shader OverlayShader => overlayShader;
        public bool HasTrackedPose => _hasTrackedPose;

        private void Awake()
        {
            _inputModule = GetComponent<OVRInputModule>();
            _rayHelper = new GameObject("ControllerRayHelper").transform;
            _rayHelper.SetParent(transform, false);
            _inputModule.rayTransform = _rayHelper;
            _inputModule.joyPadClickButton = OVRInput.Button.SecondaryIndexTrigger;
            _uiLayerMask = LayerMask.GetMask("Default", "UI");

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
            _activeController = OVRInput.GetActiveControllerForHand(
                OVRInput.Handedness.RightHanded);
            _hasTrackedPose = TryUpdateRayOrigin();
            if (_line != null) _line.enabled = _hasTrackedPose;
            if (!_hasTrackedPose && _cursor != null) _cursor.SetActive(false);
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

            float targetDistance = Mathf.Sqrt(descriptor.ToolDepthSquared);
            float halfAngle = Mathf.Acos(Mathf.Sqrt(Mathf.Clamp01(
                descriptor.CosHalfAngleSquared)));
            float radius = targetDistance * Mathf.Tan(halfAngle);
            Vector3 surfaceNormal = descriptor.SurfaceNormal;
            _fineCursor.transform.SetPositionAndRotation(
                descriptor.CursorPosition + surfaceNormal * 0.001f,
                Quaternion.FromToRotation(Vector3.up, surfaceNormal));
            _fineCursor.transform.localScale = new Vector3(radius * 2f,
                0.0005f, radius * 2f);

            Color color = GetFineBrushPreviewColor(operation);
            _fineProperties ??= new MaterialPropertyBlock();
            Color cursorTint = color;
            cursorTint.a = Mathf.Max(0.55f, color.a) * 0.5f;
            _fineProperties.SetColor(ColorId, cursorTint);
            _fineCursorRenderer.SetPropertyBlock(_fineProperties);
        }

        private bool TryUpdateRayOrigin()
        {
            if (_activeController == OVRInput.Controller.None || _rayHelper == null)
                return false;
            bool hand = _activeController is OVRInput.Controller.LHand or
                OVRInput.Controller.RHand;
            Vector3 localPosition;
            Quaternion localRotation;
            if (hand)
            {
                OVRPlugin.Hand which = _activeController == OVRInput.Controller.LHand
                    ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, which, ref _handState))
                    return false;
                localPosition = _handState.PointerPose.Position.FromFlippedZVector3f();
                localRotation = _handState.PointerPose.Orientation.FromFlippedZQuatf();
            }
            else
            {
                if (!OVRInput.GetControllerPositionTracked(_activeController) &&
                    !OVRInput.GetControllerOrientationTracked(_activeController))
                    return false;
                localPosition = OVRInput.GetLocalControllerPosition(_activeController);
                localRotation = OVRInput.GetLocalControllerRotation(_activeController);
            }
            Camera camera = Camera.main;
            if (camera == null) return false;
            OVRPose pose = new OVRPose
            {
                position = localPosition,
                orientation = localRotation
            }.ToWorldSpacePose(camera);
            _rayHelper.SetPositionAndRotation(pose.position, pose.orientation);
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
            bool hovering = false;
            if (Physics.Raycast(origin, direction, out RaycastHit hit,
                    maxLength + rayStartOffset, _uiLayerMask,
                    QueryTriggerInteraction.Collide) &&
                hit.collider.GetComponentInParent<UIDocument>() != null)
            {
                end = hit.point;
                hovering = true;
            }
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

        private void SetCursorColor(Color color)
        {
            if (_cursorRenderer == null) return;
            _cursorProperties ??= new MaterialPropertyBlock();
            _cursorProperties.SetColor(ColorId, color);
            _cursorRenderer.SetPropertyBlock(_cursorProperties);
        }
    }
}
