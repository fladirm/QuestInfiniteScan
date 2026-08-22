using System;
using System.Collections.Generic;

namespace Genesis.RoomScan.SigmaPrism
{
    public readonly struct SigmaTransitionKey : IEquatable<SigmaTransitionKey>
    {
        public SigmaTransitionKey(ulong leftCoordinate, uint leftGeneration,
            ulong rightCoordinate, uint rightGeneration)
        {
            LeftCoordinate = leftCoordinate;
            LeftGeneration = leftGeneration;
            RightCoordinate = rightCoordinate;
            RightGeneration = rightGeneration;
        }

        public ulong LeftCoordinate { get; }
        public uint LeftGeneration { get; }
        public ulong RightCoordinate { get; }
        public uint RightGeneration { get; }
        public bool Equals(SigmaTransitionKey other) =>
            LeftCoordinate == other.LeftCoordinate &&
            LeftGeneration == other.LeftGeneration &&
            RightCoordinate == other.RightCoordinate &&
            RightGeneration == other.RightGeneration;
        public override bool Equals(object obj) =>
            obj is SigmaTransitionKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(LeftCoordinate,
            LeftGeneration, RightCoordinate, RightGeneration);
    }

    public readonly struct SigmaTransitionSignature
    {
        public SigmaTransitionSignature(SigmaS16 transition,
            int annihilatorId, long annihilatorError)
        {
            Transition = transition;
            AnnihilatorId = annihilatorId;
            AnnihilatorError = annihilatorError;
        }

        public SigmaS16 Transition { get; }
        public int AnnihilatorId { get; }
        public long AnnihilatorError { get; }
    }

    /// <summary>
    /// Disposable generation-pair cache. It owns no physical state and invalidates
    /// deterministically whenever either endpoint generation changes.
    /// </summary>
    public sealed class SigmaTransitionCache
    {
        private readonly int _capacity;
        private readonly Dictionary<SigmaTransitionKey, SigmaTransitionSignature> _records;
        private readonly Queue<SigmaTransitionKey> _insertionOrder;

        public SigmaTransitionCache(int capacity = 4096)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _records = new Dictionary<SigmaTransitionKey, SigmaTransitionSignature>(capacity);
            _insertionOrder = new Queue<SigmaTransitionKey>(capacity);
        }

        public ulong HitCount { get; private set; }
        public ulong MissCount { get; private set; }

        public SigmaTransitionSignature GetOrCompute(SigmaTransitionKey key,
            SigmaS16 left, SigmaS16 right)
        {
            if (_records.TryGetValue(key, out SigmaTransitionSignature cached))
            {
                ++HitCount;
                return cached;
            }
            ++MissCount;
            SigmaS16 transition = SigmaOperatorEvaluator.EvaluateS16(
                SigmaOperatorPlans.Transition, left, right);
            int bestId = 0;
            long bestError = long.MaxValue;
            for (int action = 0;
                action < SigmaGeneratedAlgebra.AnnihilatorActionCount; ++action)
            {
                SigmaS16 residual = SigmaS16Operators.RightSignedDyadAction(
                    transition, SigmaS16Operators.GetAnnihilatorAction(action));
                long error = SigmaS16Operators.L1RawChecked(residual);
                if (error < bestError)
                {
                    bestError = error;
                    bestId = action;
                }
            }
            var result = new SigmaTransitionSignature(transition, bestId, bestError);
            if (_records.Count >= _capacity)
            {
                SigmaTransitionKey oldest = _insertionOrder.Dequeue();
                _records.Remove(oldest);
            }
            _records.Add(key, result);
            _insertionOrder.Enqueue(key);
            return result;
        }

        public void Clear()
        {
            _records.Clear();
            _insertionOrder.Clear();
            HitCount = 0UL;
            MissCount = 0UL;
        }
    }
}
