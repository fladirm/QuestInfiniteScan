using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.UI
{
    /// <summary>
    /// Picks the active VR controller, keeps <see cref="OVRInputModule.rayTransform"/>
    /// pointing along the controller ray, and draws a laser + cursor dot.
    /// Place on the same GameObject as the <c>EventSystem</c> / <c>OVRInputModule</c>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OVRInputModule))]
    public sealed class ControllerRayDriver : MonoBehaviour
    {
        [Header("Ray")]
        [SerializeField, Tooltip("Forward offset from controller origin (meters)")]
        private float rayStartOffset = 0.05f;
        [SerializeField] private float maxLength = 5f;

        [Header("Laser Visual")]
        [SerializeField] private float beamWidth = 0.003f;
        [SerializeField] private Color idleColor = new(0.25f, 0.85f, 1f, 0.65f);
        [SerializeField] private Color hoverColor = new(0.1f, 1f, 0.65f, 0.95f);

        [Header("Cursor Dot")]
        [SerializeField] private float cursorRadius = 0.006f;
        [SerializeField] private Color cursorColor = new(1f, 1f, 1f, 0.9f);

        [Header("Rendering")]
        [SerializeField] internal Shader overlayShader;

        private OVRInputModule _inputModule;
        private Transform _rayHelper;
        private LineRenderer _line;
        private GameObject _cursor;
        private MeshRenderer _cursorRenderer;
        private Material _overlayMaterial;
        private MaterialPropertyBlock _cursorProperties;
        private OVRInput.Controller _activeController = OVRInput.Controller.None;
        private bool _hasTrackedPose;

        private static OVRPlugin.HandState _handState = new();
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Layer mask matching the debug menu's panel collider layer
        private int _uiLayerMask;

        public Shader OverlayShader => overlayShader;
        public bool HasTrackedPose => _hasTrackedPose;

        private void Awake()
        {
            _inputModule = GetComponent<OVRInputModule>();

            _rayHelper = new GameObject("ControllerRayHelper").transform;
            _rayHelper.SetParent(transform, false);
            _inputModule.rayTransform = _rayHelper;
            _inputModule.joyPadClickButton = OVRInput.Button.PrimaryIndexTrigger;

            _uiLayerMask = LayerMask.GetMask("Default", "UI");

            if (overlayShader == null)
            {
                Logger.Error("ControllerRayDriver: controller-ray shader is not wired; " +
                             "pointer interaction remains active but the ray is invisible.");
                return;
            }

            _overlayMaterial = new Material(overlayShader)
            {
                name = "Sigma Controller Ray (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            SetupLineRenderer();
            SetupCursor();
        }

        private void Update()
        {
            _activeController = ChooseBestController(_activeController);
            _hasTrackedPose = TryUpdateRayOrigin();
            if (_line != null)
                _line.enabled = _hasTrackedPose;
            if (!_hasTrackedPose && _cursor != null)
                _cursor.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_hasTrackedPose)
                DrawLaser();
        }

        private void OnDestroy()
        {
            if (_rayHelper != null) Destroy(_rayHelper.gameObject);
            if (_cursor != null) Destroy(_cursor);
            if (_overlayMaterial != null) Destroy(_overlayMaterial);
        }

        // ─── Controller Selection (adapted from Meta ImmersiveDebugger) ───

        private static OVRInput.Controller ChooseBestController(OVRInput.Controller previous)
        {
            var left = OVRInput.GetActiveControllerForHand(OVRInput.Handedness.LeftHanded);
            var right = OVRInput.GetActiveControllerForHand(OVRInput.Handedness.RightHanded);

            if (left != OVRInput.Controller.None &&
                OVRInput.Get(OVRInput.Button.Any, left))
                return left;
            if (right != OVRInput.Controller.None &&
                OVRInput.Get(OVRInput.Button.Any, right))
                return right;
            if (previous != OVRInput.Controller.None &&
                (previous == left || previous == right))
                return previous;
            if (OVRInput.GetDominantHand() == OVRInput.Handedness.LeftHanded &&
                left != OVRInput.Controller.None)
                return left;
            return right != OVRInput.Controller.None ? right : left;
        }

        // ─── Ray Transform ───

        private bool TryUpdateRayOrigin()
        {
            if (_activeController == OVRInput.Controller.None || _rayHelper == null)
                return false;

            bool isHand = _activeController is OVRInput.Controller.LHand or OVRInput.Controller.RHand;

            Vector3 localPos;
            Quaternion localRot;

            if (isHand)
            {
                var hand = _activeController == OVRInput.Controller.LHand
                    ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref _handState))
                    return false;
                localPos = _handState.PointerPose.Position.FromFlippedZVector3f();
                localRot = _handState.PointerPose.Orientation.FromFlippedZQuatf();
            }
            else
            {
                if (!OVRInput.GetControllerPositionTracked(_activeController) &&
                    !OVRInput.GetControllerOrientationTracked(_activeController))
                    return false;
                localPos = OVRInput.GetLocalControllerPosition(_activeController);
                localRot = OVRInput.GetLocalControllerRotation(_activeController);
            }

            var pose = new OVRPose { position = localPos, orientation = localRot };

            var cam = Camera.main;
            if (cam == null)
                return false;
            pose = pose.ToWorldSpacePose(cam);

            _rayHelper.SetPositionAndRotation(pose.position, pose.orientation);
            return true;
        }

        // ─── Laser Visual ───

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

            // Remove the collider so it doesn't interfere with raycasts
            var col = _cursor.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _cursorRenderer = _cursor.GetComponent<MeshRenderer>();
            _cursorRenderer.sharedMaterial = _overlayMaterial;
            _cursorProperties = new MaterialPropertyBlock();
            SetCursorColor(cursorColor);
            _cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cursorRenderer.receiveShadows = false;

            _cursor.SetActive(false);
        }

        private void DrawLaser()
        {
            if (_rayHelper == null || _line == null) return;

            var origin = _rayHelper.position;
            var dir = _rayHelper.forward;
            var start = origin + dir * rayStartOffset;
            var end = start + dir * maxLength;
            bool hoveringUI = false;

            // Only highlight when hitting a world-space UI Toolkit panel collider
            if (Physics.Raycast(origin, dir, out var hit, maxLength + rayStartOffset,
                    _uiLayerMask, QueryTriggerInteraction.Collide))
            {
                end = hit.point;

                // Check if we hit a UIDocument's auto-generated panel collider
                var uiDoc = hit.collider.GetComponentInParent<UIDocument>();
                hoveringUI = uiDoc != null;
            }

            _line.SetPosition(0, start);
            _line.SetPosition(1, end);

            var color = hoveringUI ? hoverColor : idleColor;
            _line.startColor = _line.endColor = color;

            // Position cursor dot at the end of the ray
            if (_cursor != null)
            {
                bool showCursor = hoveringUI;
                _cursor.SetActive(showCursor);
                if (showCursor)
                {
                    _cursor.transform.position = end;
                    _cursor.transform.LookAt(_rayHelper);
                    SetCursorColor(hoverColor);
                }
            }
        }

        private void SetCursorColor(Color color)
        {
            if (_cursorRenderer == null)
                return;
            _cursorProperties ??= new MaterialPropertyBlock();
            _cursorProperties.SetColor(ColorId, color);
            _cursorRenderer.SetPropertyBlock(_cursorProperties);
        }
    }
}
