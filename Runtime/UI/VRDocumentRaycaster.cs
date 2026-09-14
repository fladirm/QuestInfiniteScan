using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace FinalScan.UI
{
    /// <summary>
    /// World-space UI Toolkit raycaster fed by the controller ray carried in <see cref="OVRPointerEventData.worldSpaceRay"/>
    /// (OVRInputModule), restricted to the UI layer so the panel always wins over scene geometry. Without VR pointer
    /// data (editor mouse) it falls back to the screen-to-camera ray. Lives on the EventSystem object next to OVRInputModule.
    /// </summary>
    [AddComponentMenu("FinalScan/VR Document Raycaster")]
    public class VRDocumentRaycaster : WorldDocumentRaycaster
    {
        [SerializeField, Tooltip("Max ray distance for UI interaction (metres)")] float maxRayDistance = 5f;

        protected override bool GetWorldRay(PointerEventData eventData, out Ray worldRay, out float maxDistance, out int layerMask)
        {
            if (eventData is OVRPointerEventData ovrData && ovrData.worldSpaceRay.direction.sqrMagnitude > 0.001f)
            {
                worldRay = ovrData.worldSpaceRay;
                maxDistance = maxRayDistance;
                int uiLayer = LayerMask.NameToLayer("UI");
                layerMask = uiLayer >= 0 ? 1 << uiLayer : 0;
                return true;
            }
            return base.GetWorldRay(eventData, out worldRay, out maxDistance, out layerMask);
        }
    }
}
