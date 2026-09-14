using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace FinalScan.UI
{
    /// <summary>
    /// Right-controller UI pointer (donor port): drives OVRInputModule.rayTransform from the right controller (or right
    /// hand pointer pose), draws the laser with Shaders/FinalScanControllerRay.shader, shows a cursor while hovering
    /// the UI layer, and reports <see cref="IsPointingAtUi"/> so scene tools never see a trigger the UI consumed.
    /// Click/select = right index trigger (OVRInputModule.joyPadClickButton = SecondaryIndexTrigger).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OVRInputModule))]
    public sealed class ControllerRayDriver : MonoBehaviour
    {
        public const string ShaderName = "FinalScan/ControllerRay";

        [SerializeField] float rayStartOffset = 0.05f;
        [SerializeField] float maxLength = 5f;
        [SerializeField] float beamWidth = 0.003f;
        [SerializeField] Color idleColor = new Color(0.25f, 0.85f, 1f, 0.65f);
        [SerializeField] Color hoverColor = new Color(0.1f, 1f, 0.65f, 0.95f);
        [SerializeField] float cursorRadius = 0.006f;
        [SerializeField] Color cursorColor = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("FinalScan/ControllerRay; assigned by the editor setup so it ships in the build.")]
        [SerializeField] Shader overlayShader;

        OVRInputModule _inputModule;
        Transform _rayHelper;
        LineRenderer _line;
        GameObject _cursor;
        MeshRenderer _cursorRenderer;
        Material _overlayMaterial;
        MaterialPropertyBlock _cursorProperties;
        bool _pointingAtUi, _hoveringUi, _hasTrackedPose, _uiTriggerCaptured;
        Vector3 _uiHitPoint;
        int _uiLayerMask;

        static OVRPlugin.HandState s_leftHandState = new OVRPlugin.HandState();
        static OVRPlugin.HandState s_rightHandState = new OVRPlugin.HandState();
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public Shader OverlayShader => overlayShader;
        public bool HasTrackedPose => _hasTrackedPose;
        /// <summary>True while the ray hovers the UI or a trigger press started on the UI is still held: scene input must ignore the trigger.</summary>
        public bool IsPointingAtUi => _pointingAtUi;
        public bool IsHoveringUi => _hoveringUi;

        void Awake()
        {
            _inputModule = GetComponent<OVRInputModule>();
            _rayHelper = new GameObject("FinalScanRayHelper").transform;
            _rayHelper.SetParent(transform, false);
            _inputModule.rayTransform = _rayHelper;
            _inputModule.joyPadClickButton = OVRInput.Button.SecondaryIndexTrigger;   // right index trigger = click
            _uiLayerMask = LayerMask.GetMask("UI");
            Shader shader = overlayShader != null ? overlayShader : Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError("[FinalScan] ControllerRayDriver: ray shader missing; pointer stays active without a laser."); return; }
            _overlayMaterial = new Material(shader) { name = "FS-ControllerRay", hideFlags = HideFlags.DontSave };
            SetupLineRenderer();
            SetupCursor();
        }

        void Update()
        {
            _hasTrackedPose = TryUpdateRayOrigin();
            if (_line != null) _line.enabled = _hasTrackedPose;
            if (!_hasTrackedPose)
            {
                _pointingAtUi = _hoveringUi = _uiTriggerCaptured = false;
                if (_cursor != null) _cursor.SetActive(false);
                return;
            }
            RefreshUiAuthority();
        }

        void LateUpdate() { if (_hasTrackedPose && _line != null) DrawLaser(); }

        void OnDestroy()
        {
            if (_rayHelper != null) Destroy(_rayHelper.gameObject);
            if (_cursor != null) Destroy(_cursor);
            if (_overlayMaterial != null) Destroy(_overlayMaterial);
        }

        public bool TryGetWorldRay(out Vector3 origin, out Vector3 direction)
        {
            origin = default; direction = default;
            if (!_hasTrackedPose || _rayHelper == null) return false;
            origin = _rayHelper.position; direction = _rayHelper.forward.normalized;
            return direction.sqrMagnitude > 0.99f;
        }

        public bool TryGetWorldPose(out Vector3 position, out Quaternion rotation) => TryGetWorldPose(OVRInput.Handedness.RightHanded, out position, out rotation);
        public bool TryGetLeftWorldPose(out Vector3 position, out Quaternion rotation) => TryGetWorldPose(OVRInput.Handedness.LeftHanded, out position, out rotation);

        bool TryUpdateRayOrigin()
        {
            if (_rayHelper == null || !TryGetWorldPose(OVRInput.Handedness.RightHanded, out Vector3 position, out Quaternion rotation)) return false;
            _rayHelper.SetPositionAndRotation(position, rotation);
            return true;
        }

        static bool TryGetWorldPose(OVRInput.Handedness handedness, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = default;
            OVRInput.Controller controller;
            try { controller = OVRInput.GetActiveControllerForHand(handedness); } catch (System.Exception) { return false; }
            if (controller == OVRInput.Controller.None) return false;
            bool hand = controller == OVRInput.Controller.LHand || controller == OVRInput.Controller.RHand;
            Vector3 localPosition; Quaternion localRotation;
            if (hand)
            {
                OVRPlugin.Hand which = controller == OVRInput.Controller.LHand ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;
                ref OVRPlugin.HandState handState = ref (which == OVRPlugin.Hand.HandLeft ? ref s_leftHandState : ref s_rightHandState);
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, which, ref handState)) return false;
                localPosition = handState.PointerPose.Position.FromFlippedZVector3f();
                localRotation = handState.PointerPose.Orientation.FromFlippedZQuatf();
            }
            else
            {
                if (!OVRInput.GetControllerPositionTracked(controller) || !OVRInput.GetControllerOrientationTracked(controller)) return false;
                localPosition = OVRInput.GetLocalControllerPosition(controller);
                localRotation = OVRInput.GetLocalControllerRotation(controller);
            }
            Camera camera = Camera.main;
            if (camera == null) return false;
            OVRPose pose = new OVRPose { position = localPosition, orientation = localRotation }.ToWorldSpacePose(camera);
            position = pose.position; rotation = pose.orientation;
            return true;
        }

        void SetupLineRenderer()
        {
            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
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

        void SetupCursor()
        {
            _cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cursor.name = "FinalScanRayCursor";
            _cursor.transform.localScale = Vector3.one * (cursorRadius * 2f);
            Collider collider = _cursor.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _cursorRenderer = _cursor.GetComponent<MeshRenderer>();
            _cursorRenderer.sharedMaterial = _overlayMaterial;
            _cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cursorRenderer.receiveShadows = false;
            _cursorProperties = new MaterialPropertyBlock();
            SetCursorColor(cursorColor);
            _cursor.SetActive(false);
        }

        void DrawLaser()
        {
            Vector3 origin = _rayHelper.position, direction = _rayHelper.forward;
            Vector3 start = origin + direction * rayStartOffset;
            Vector3 end = _hoveringUi ? _uiHitPoint : start + direction * maxLength;
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);
            _line.startColor = _line.endColor = _hoveringUi ? hoverColor : idleColor;
            if (_cursor == null) return;
            _cursor.SetActive(_hoveringUi);
            if (!_hoveringUi) return;
            _cursor.transform.position = end;
            _cursor.transform.LookAt(_rayHelper);
            SetCursorColor(hoverColor);
        }

        /// <summary>UI wins: a physics hit on the UI layer that belongs to a UIDocument (world-space panel collider).</summary>
        void RefreshUiAuthority()
        {
            _hoveringUi = Physics.Raycast(_rayHelper.position, _rayHelper.forward, out RaycastHit hit, maxLength + rayStartOffset, _uiLayerMask, QueryTriggerInteraction.Collide)
                          && hit.collider.GetComponentInParent<UIDocument>() != null;
            if (_hoveringUi) _uiHitPoint = hit.point;
            if (_hoveringUi && OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger)) _uiTriggerCaptured = true;
            if (!OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger)) _uiTriggerCaptured = false;
            _pointingAtUi = _hoveringUi || _uiTriggerCaptured;
        }

        void SetCursorColor(Color color)
        {
            if (_cursorRenderer == null) return;
            _cursorProperties ??= new MaterialPropertyBlock();
            _cursorProperties.SetColor(ColorId, color);
            _cursorRenderer.SetPropertyBlock(_cursorProperties);
        }
    }
}
