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
        internal readonly Vector3 CursorPosition;
        internal readonly Vector3 SurfaceNormal;
        internal readonly Vector3 Axis;
        internal readonly float Radius;
        internal readonly float Length;
        internal readonly FineBrushOperation Operation;

        internal bool IsActive => Operation != FineBrushOperation.None;
        internal bool IsRefine => Operation == FineBrushOperation.Refine;
        internal bool IsErase => Operation == FineBrushOperation.Erase;

        internal Vector3 BoundsCenter =>
            CursorPosition + Axis * (Length * 0.5f);
        internal float BoundsRadius => Mathf.Sqrt(
            Radius * Radius + Length * Length * 0.25f);

        private FineBrushDescriptor(Vector3 cursorPosition,
            Vector3 surfaceNormal, Vector3 axis, float radius, float length,
            FineBrushOperation operation)
        {
            CursorPosition = cursorPosition;
            SurfaceNormal = surfaceNormal;
            Axis = axis;
            Radius = radius;
            Length = length;
            Operation = operation;
        }

        internal static bool TryCreate(Vector3 cursorPosition,
            Vector3 surfaceNormal, Vector3 axis, float radius, float length,
            FineBrushOperation operation, out FineBrushDescriptor descriptor)
        {
            descriptor = default;
            if (operation == FineBrushOperation.None ||
                !IsFinite(cursorPosition) || !IsFinite(surfaceNormal) ||
                !IsFinite(axis) || !float.IsFinite(radius) ||
                !float.IsFinite(length) || axis.sqrMagnitude <= 1e-10f ||
                surfaceNormal.sqrMagnitude <= 1e-10f ||
                radius <= 0f || length <= 0f)
                return false;

            axis.Normalize();
            surfaceNormal.Normalize();
            if (Vector3.Dot(surfaceNormal, -axis) < 0f)
                surfaceNormal = -surfaceNormal;
            descriptor = new FineBrushDescriptor(cursorPosition,
                surfaceNormal, axis, radius, length, operation);
            return true;
        }

        internal bool Contains(Vector3 worldPosition)
        {
            if (!IsActive || !IsFinite(worldPosition)) return false;
            Vector3 relative = worldPosition - CursorPosition;
            float axial = Vector3.Dot(relative, Axis);
            Vector3 radial = relative - Axis * axial;
            return axial >= 0f && axial <= Length &&
                   radial.sqrMagnitude <= Radius * Radius;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) &&
            float.IsFinite(value.z);
    }
}
