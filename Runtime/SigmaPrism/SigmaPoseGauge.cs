using System;
using Unity.Collections;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Exact six-component readout-gauge correction. It is not carrier state: the
    /// immutable Meta poses/timestamps remain unchanged and this bounded twist is
    /// applied only while evaluating a prediction or inverse readout.
    /// </summary>
    public readonly struct SigmaPoseGaugeState : IEquatable<SigmaPoseGaugeState>
    {
        private readonly long _tx, _ty, _tz, _rx, _ry, _rz;

        internal SigmaPoseGaugeState(uint calibrationEpoch, uint revision,
            bool resolved, long tx, long ty, long tz, long rx, long ry, long rz)
        {
            CalibrationEpoch = calibrationEpoch;
            Revision = revision;
            Resolved = resolved;
            _tx = tx; _ty = ty; _tz = tz;
            _rx = rx; _ry = ry; _rz = rz;
        }

        public uint CalibrationEpoch { get; }
        public uint Revision { get; }
        public bool Resolved { get; }
        internal bool IsIdentity => _tx == 0L && _ty == 0L && _tz == 0L &&
            _rx == 0L && _ry == 0L && _rz == 0L;
        internal long Raw(int component) => component switch
        {
            0 => _tx, 1 => _ty, 2 => _tz,
            3 => _rx, 4 => _ry, 5 => _rz,
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };

        internal static SigmaPoseGaugeState Identity(uint epoch, uint revision = 0u) =>
            new(epoch, revision, false, 0L, 0L, 0L, 0L, 0L, 0L);

        internal static SigmaPoseGaugeState FromGpu(NativeArray<uint> words,
            uint epoch, uint revision)
        {
            if (words.Length < 16 || words[0] == 0u || words[1] != 0u)
                return Identity(epoch, revision);
            return new SigmaPoseGaugeState(epoch, revision, true,
                Read(words, 4), Read(words, 6), Read(words, 8),
                Read(words, 10), Read(words, 12), Read(words, 14));
        }

        internal Pose Apply(Pose reference, Pose sensor)
        {
            if (!Resolved)
                return sensor;
            Vector3 translation = new((float)SigmaNumericDomain.ToDouble(_tx),
                (float)SigmaNumericDomain.ToDouble(_ty),
                (float)SigmaNumericDomain.ToDouble(_tz));
            Vector3 rotationVector = new((float)SigmaNumericDomain.ToDouble(_rx),
                (float)SigmaNumericDomain.ToDouble(_ry),
                (float)SigmaNumericDomain.ToDouble(_rz));
            float angle = rotationVector.magnitude;
            Quaternion rotation = angle > 1e-12f
                ? Quaternion.AngleAxis(angle * Mathf.Rad2Deg,
                    rotationVector / angle)
                : Quaternion.identity;
            Matrix4x4 referenceWorld = Matrix4x4.TRS(reference.position,
                reference.rotation, Vector3.one);
            Matrix4x4 sensorWorld = Matrix4x4.TRS(sensor.position,
                sensor.rotation, Vector3.one);
            Matrix4x4 corrected = referenceWorld * Matrix4x4.TRS(translation,
                rotation, Vector3.one) * referenceWorld.inverse * sensorWorld;
            return new Pose(corrected.GetColumn(3), corrected.rotation);
        }

        public bool Equals(SigmaPoseGaugeState other) =>
            CalibrationEpoch == other.CalibrationEpoch && Revision == other.Revision &&
            Resolved == other.Resolved && _tx == other._tx && _ty == other._ty &&
            _tz == other._tz && _rx == other._rx && _ry == other._ry && _rz == other._rz;
        public override bool Equals(object obj) =>
            obj is SigmaPoseGaugeState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(
            HashCode.Combine(CalibrationEpoch, Revision, Resolved, _tx, _ty),
            _tz, _rx, _ry, _rz);

        internal static long MinimumMagnitude(long lo, long hi)
        {
            if (lo > hi)
                throw new ArgumentOutOfRangeException(nameof(lo));
            if (lo <= 0L && hi >= 0L)
                return 0L;
            return lo > 0L ? lo : hi;
        }

        private static long Read(NativeArray<uint> words, int offset) =>
            unchecked((long)((ulong)words[offset] |
                ((ulong)words[offset + 1] << 32)));
    }
}
