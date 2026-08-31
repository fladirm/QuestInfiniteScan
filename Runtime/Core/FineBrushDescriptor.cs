using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal enum FineBrushOperation : byte
    {
        None = 0,
        Refine = 1,
        Erase = 2,
        Preview = 3
    }

    /// <summary>
    /// Immutable, attempt-local manual authority. It is presentation/control data,
    /// never persistent reconstruction state.
    /// </summary>
    internal readonly struct FineBrushDescriptor
    {
        internal readonly Vector3 EyeOrigin;
        internal readonly Vector3 CursorPosition;
        internal readonly Vector3 SurfaceNormal;
        internal readonly Vector3 Axis;
        internal readonly float CosHalfAngleSquared;
        internal readonly float ToolDepthSquared;
        internal readonly FineBrushOperation Operation;

        internal bool IsActive => Operation != FineBrushOperation.None;
        internal bool IsRefine => Operation == FineBrushOperation.Refine;
        internal bool IsErase => Operation == FineBrushOperation.Erase;

        private FineBrushDescriptor(Vector3 eyeOrigin, Vector3 cursorPosition,
            Vector3 surfaceNormal, Vector3 axis, float cosHalfAngleSquared,
            float toolDepthSquared, FineBrushOperation operation)
        {
            EyeOrigin = eyeOrigin;
            CursorPosition = cursorPosition;
            SurfaceNormal = surfaceNormal;
            Axis = axis;
            CosHalfAngleSquared = cosHalfAngleSquared;
            ToolDepthSquared = toolDepthSquared;
            Operation = operation;
        }

        internal static bool TryCreate(Vector3 eyeOrigin,
            Vector3 cursorPosition, Vector3 surfaceNormal,
            float fullAngleDegrees, float toolDepth,
            FineBrushOperation operation, out FineBrushDescriptor descriptor)
        {
            descriptor = default;
            Vector3 difference = cursorPosition - eyeOrigin;
            float lengthSquared = difference.sqrMagnitude;
            if (operation == FineBrushOperation.None ||
                !IsFinite(eyeOrigin) || !IsFinite(cursorPosition) ||
                !IsFinite(surfaceNormal) ||
                !float.IsFinite(fullAngleDegrees) ||
                !float.IsFinite(toolDepth) || lengthSquared <= 1e-10f ||
                surfaceNormal.sqrMagnitude <= 1e-10f ||
                toolDepth <= 0f || fullAngleDegrees <= 0f ||
                fullAngleDegrees >= 180f)
                return false;

            Vector3 axis = difference / Mathf.Sqrt(lengthSquared);
            surfaceNormal.Normalize();
            if (Vector3.Dot(surfaceNormal, eyeOrigin - cursorPosition) < 0f)
                surfaceNormal = -surfaceNormal;
            float cosine = Mathf.Cos(fullAngleDegrees * 0.5f * Mathf.Deg2Rad);
            descriptor = new FineBrushDescriptor(eyeOrigin, cursorPosition,
                surfaceNormal, axis, cosine * cosine,
                toolDepth * toolDepth, operation);
            return true;
        }

        internal bool Contains(Vector3 worldPosition)
        {
            if (!IsActive || !IsFinite(worldPosition)) return false;
            Vector3 relative = worldPosition - EyeOrigin;
            float distanceSquared = relative.sqrMagnitude;
            float axial = Vector3.Dot(relative, Axis);
            return distanceSquared <= ToolDepthSquared && axial >= 0f &&
                   axial * axial >= distanceSquared * CosHalfAngleSquared;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) &&
            float.IsFinite(value.z);
    }
}
