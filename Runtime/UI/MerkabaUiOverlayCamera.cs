using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Genesis.RoomScan.UI
{
    /// <summary>
    /// Keeps the UX above everything. A URP overlay camera stacked on the main
    /// camera renders only the UI layer after the scan readout, the GLB viewer
    /// and every tool preview, with depth cleared, so no world geometry can
    /// hide the panel, the pointer or the cursor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaUiOverlayCamera : MonoBehaviour
    {
        private Camera _base;
        private Camera _overlay;
        private int _uiLayer = -1;

        public bool IsStacked => _overlay != null;

        /// <summary>Ensures the overlay camera exists on the main camera.</summary>
        public static MerkabaUiOverlayCamera Ensure(int uiLayer)
        {
            if (uiLayer < 0) return null;
            Camera main = Camera.main;
            if (main == null) return null;
            MerkabaUiOverlayCamera existing =
                main.GetComponent<MerkabaUiOverlayCamera>();
            if (existing != null) return existing;
            MerkabaUiOverlayCamera created =
                main.gameObject.AddComponent<MerkabaUiOverlayCamera>();
            created._uiLayer = uiLayer;
            created.Build();
            return created;
        }

        private void OnEnable()
        {
            if (_overlay != null) _overlay.enabled = true;
        }

        private void OnDisable()
        {
            if (_overlay != null) _overlay.enabled = false;
        }

        private void OnDestroy() => Teardown();

        private void Build()
        {
            _base = GetComponent<Camera>();
            if (_base == null || _uiLayer < 0 || _overlay != null) return;
            UniversalAdditionalCameraData baseData =
                _base.GetUniversalAdditionalCameraData();
            if (baseData == null ||
                baseData.renderType != CameraRenderType.Base)
            {
                Logger.Error("MerkabaUiOverlayCamera: the main camera is not " +
                    "a URP base camera; the UX stays in the base pass.");
                return;
            }
            var holder = new GameObject("Merkaba UX Overlay Camera");
            holder.transform.SetParent(_base.transform, false);
            _overlay = holder.AddComponent<Camera>();
            _overlay.CopyFrom(_base);
            _overlay.clearFlags = CameraClearFlags.Depth;
            _overlay.cullingMask = 1 << _uiLayer;
            _overlay.depth = _base.depth + 1f;
            _overlay.nearClipPlane = Mathf.Min(_base.nearClipPlane, 0.02f);
            _overlay.useOcclusionCulling = false;
            _overlay.stereoTargetEye = StereoTargetEyeMask.Both;
            UniversalAdditionalCameraData overlayData =
                _overlay.GetUniversalAdditionalCameraData();
            // Overlay cameras clear depth by default (clearDepth is read-only).
            overlayData.renderType = CameraRenderType.Overlay;
            overlayData.renderPostProcessing = false;
            overlayData.renderShadows = false;
            overlayData.requiresColorOption = CameraOverrideOption.Off;
            overlayData.requiresDepthOption = CameraOverrideOption.Off;
            baseData.cameraStack.Add(_overlay);
            // The UI layer is drawn once, by the overlay, after everything.
            _base.cullingMask &= ~(1 << _uiLayer);
        }

        private void Teardown()
        {
            if (_overlay == null) return;
            if (_base != null)
            {
                _base.cullingMask |= 1 << _uiLayer;
                UniversalAdditionalCameraData baseData =
                    _base.GetUniversalAdditionalCameraData();
                baseData?.cameraStack.Remove(_overlay);
            }
            Destroy(_overlay.gameObject);
            _overlay = null;
        }
    }
}
