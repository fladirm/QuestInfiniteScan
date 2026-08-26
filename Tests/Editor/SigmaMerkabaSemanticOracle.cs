using System;
using System.Collections.Generic;
using System.Linq;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaNativeOracleResolution
    {
        None,
        Unique,
        CommonDelta,
        Ambiguous,
    }

    internal readonly struct SigmaNativePhotometricLaw
    {
        internal SigmaNativePhotometricLaw(bool metadataPresent,
            bool calibrationMatches, SigmaQ48Interval exposure,
            SigmaQ48Interval gain, SigmaQ48Interval illumination,
            SigmaQ48Interval offset, string transferFingerprint)
        {
            MetadataPresent = metadataPresent;
            CalibrationMatches = calibrationMatches;
            Exposure = exposure;
            Gain = gain;
            Illumination = illumination;
            Offset = offset;
            TransferFingerprint = transferFingerprint ??
                throw new ArgumentNullException(nameof(transferFingerprint));
        }

        internal bool MetadataPresent { get; }
        internal bool CalibrationMatches { get; }
        internal SigmaQ48Interval Exposure { get; }
        internal SigmaQ48Interval Gain { get; }
        internal SigmaQ48Interval Illumination { get; }
        internal SigmaQ48Interval Offset { get; }
        internal string TransferFingerprint { get; }
        internal bool HasBoundedClaim => MetadataPresent && CalibrationMatches &&
            !Exposure.IsEmpty && !Gain.IsEmpty && !Illumination.IsEmpty &&
            !Offset.IsEmpty && TransferFingerprint.Length == 64;

        internal SigmaQ48Interval Apply(SigmaQ48Interval nativeResponse) =>
            HasBoundedClaim
                ? SigmaMerkabaSemanticOracle.AddOutward(
                    SigmaMerkabaSemanticOracle.MultiplyOutward(
                        SigmaMerkabaSemanticOracle.MultiplyOutward(
                            SigmaMerkabaSemanticOracle.MultiplyOutward(
                                nativeResponse, Exposure), Gain), Illumination), Offset)
                : SigmaQ48Interval.Full;
    }

    internal sealed class SigmaNativeOracleQuery
    {
        internal SigmaNativeOracleQuery(string entryPoint, int footprint,
            IReadOnlyList<long> orderRow, IReadOnlyList<long> opticalRow,
            SigmaQ48Interval measuredOrder, SigmaQ48Interval measuredOptical,
            SigmaQ48Interval direction, bool orderEvidence,
            bool opticalEvidence, SigmaNativePhotometricLaw photometricLaw)
        {
            if (SigmaGeneratedMerkabaProgram.EntryPoints.All(entry =>
                    !string.Equals(entry.Id, entryPoint, StringComparison.Ordinal)))
                throw new ArgumentException("Query is not a generated entry point.",
                    nameof(entryPoint));
            if (orderRow == null || orderRow.Count != 4)
                throw new ArgumentException("Order contraction has four rows.",
                    nameof(orderRow));
            if (opticalRow == null || opticalRow.Count != 4)
                throw new ArgumentException("Optical contraction has four rows.",
                    nameof(opticalRow));
            EntryPoint = entryPoint;
            Footprint = footprint;
            OrderRow = orderRow.ToArray();
            OpticalRow = opticalRow.ToArray();
            MeasuredOrder = measuredOrder;
            MeasuredOptical = measuredOptical;
            Direction = direction;
            OrderEvidence = orderEvidence;
            OpticalEvidence = opticalEvidence;
            PhotometricLaw = photometricLaw;
        }

        internal string EntryPoint { get; }
        internal int Footprint { get; }
        internal long[] OrderRow { get; }
        internal long[] OpticalRow { get; }
        internal SigmaQ48Interval MeasuredOrder { get; }
        internal SigmaQ48Interval MeasuredOptical { get; }
        internal SigmaQ48Interval Direction { get; }
        internal bool OrderEvidence { get; }
        internal bool OpticalEvidence { get; }
        internal SigmaNativePhotometricLaw PhotometricLaw { get; }
    }

    internal readonly struct SigmaNativeOracleCell
    {
        internal SigmaNativeOracleCell(int supportKey, int footprint,
            SigmaGaugeCell gauge, SigmaS16 state, bool resident = true)
        {
            SupportKey = supportKey;
            Footprint = footprint;
            Gauge = gauge;
            State = state;
            Resident = resident;
        }

        internal int SupportKey { get; }
        internal int Footprint { get; }
        internal SigmaGaugeCell Gauge { get; }
        internal SigmaS16 State { get; }
        internal bool Resident { get; }
        internal long Measure
        {
            get
            {
                // N2R's finite Vulkan oracle carries exact dyadic area in Q48.
                // Deeper production refinement requires the exponent form owned
                // by N4R; silently rounding an exact positive cell to zero is
                // forbidden even in disposable proof code.
                if (Gauge.Level > SigmaNumericDomain.FractionBits / 2)
                    throw new InvalidOperationException(
                        "N2R Q48 measure fixture exceeds its exact dyadic range.");
                return SigmaNumericDomain.QShiftRight(SigmaNumericDomain.One,
                    checked(Gauge.Level * 2));
            }
        }
    }

    internal readonly struct SigmaNativeQuerySupportSummary
    {
        internal SigmaNativeQuerySupportSummary(int cellIndex, bool allDefault,
            bool defaultBoundaryClosed, bool resident, bool refined,
            string programFingerprint, string gaugeFingerprint)
        {
            CellIndex = cellIndex;
            AllDefault = allDefault;
            DefaultBoundaryClosed = defaultBoundaryClosed;
            Resident = resident;
            Refined = refined;
            ProgramFingerprint = programFingerprint;
            GaugeFingerprint = gaugeFingerprint;
        }

        internal int CellIndex { get; }
        internal bool AllDefault { get; }
        internal bool DefaultBoundaryClosed { get; }
        internal bool Resident { get; }
        internal bool Refined { get; }
        internal string ProgramFingerprint { get; }
        internal string GaugeFingerprint { get; }

        internal bool CanOmit(string expectedProgram, string expectedGauge)
        {
            bool fingerprintsMatch = string.Equals(ProgramFingerprint,
                    expectedProgram, StringComparison.Ordinal) &&
                string.Equals(GaugeFingerprint, expectedGauge,
                    StringComparison.Ordinal);
            return SigmaGeneratedMerkabaProgram.CanOmitQueryRegion(AllDefault,
                DefaultBoundaryClosed, fingerprintsMatch);
        }
    }

    internal readonly struct SigmaNativeContribution
    {
        internal SigmaNativeContribution(int supportKey, int footprint,
            SigmaQ48Interval order, SigmaQ48Interval weightedOptical,
            long measure, int sourceCell)
        {
            SupportKey = supportKey;
            Footprint = footprint;
            Order = order;
            WeightedOptical = weightedOptical;
            Measure = measure;
            SourceCell = sourceCell;
        }

        internal int SupportKey { get; }
        internal int Footprint { get; }
        internal SigmaQ48Interval Order { get; }
        internal SigmaQ48Interval WeightedOptical { get; }
        internal long Measure { get; }
        internal int SourceCell { get; }
    }

    internal readonly struct SigmaNativeSceneShadow : IEquatable<SigmaNativeSceneShadow>
    {
        internal SigmaNativeSceneShadow(int footprint, int[] firstSupports,
            int[] behindSupports, SigmaQ48Interval order,
            SigmaQ48Interval optical)
        {
            Footprint = footprint;
            FirstSupports = firstSupports ?? Array.Empty<int>();
            BehindSupports = behindSupports ?? Array.Empty<int>();
            Order = order;
            Optical = optical;
        }

        internal int Footprint { get; }
        internal int[] FirstSupports { get; }
        internal int[] BehindSupports { get; }
        internal SigmaQ48Interval Order { get; }
        internal SigmaQ48Interval Optical { get; }
        internal bool IsDefault => FirstSupports.Length == 0;

        public bool Equals(SigmaNativeSceneShadow other) =>
            Footprint == other.Footprint && Order == other.Order &&
            Optical == other.Optical &&
            FirstSupports.SequenceEqual(other.FirstSupports) &&
            BehindSupports.SequenceEqual(other.BehindSupports);
        public override bool Equals(object obj) =>
            obj is SigmaNativeSceneShadow other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Footprint, Order,
            Optical, FirstSupports.Length, BehindSupports.Length);
    }

    internal readonly struct SigmaNativeDeltaWitness : IEquatable<SigmaNativeDeltaWitness>
    {
        internal SigmaNativeDeltaWitness(long u, long v, SigmaS16 state)
        {
            U = u;
            V = v;
            State = state;
        }

        internal long U { get; }
        internal long V { get; }
        internal SigmaS16 State { get; }
        public bool Equals(SigmaNativeDeltaWitness other) =>
            U == other.U && V == other.V && State == other.State;
        public override bool Equals(object obj) =>
            obj is SigmaNativeDeltaWitness other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(U, V, State);
    }

    internal readonly struct SigmaNativePreimageCandidate
    {
        internal SigmaNativePreimageCandidate(int candidateOrdinal,
            SigmaNativeOracleCell prior, SigmaNativeOracleCell proposed,
            SigmaNativeDeltaWitness delta, bool nativeRelationSatisfied,
            bool identityPreservingTransport)
        {
            CandidateOrdinal = candidateOrdinal;
            Prior = prior;
            Proposed = proposed;
            Delta = delta;
            NativeRelationSatisfied = nativeRelationSatisfied;
            IdentityPreservingTransport = identityPreservingTransport;
        }

        internal int CandidateOrdinal { get; }
        internal SigmaNativeOracleCell Prior { get; }
        internal SigmaNativeOracleCell Proposed { get; }
        internal SigmaNativeDeltaWitness Delta { get; }
        internal bool NativeRelationSatisfied { get; }
        internal bool IdentityPreservingTransport { get; }
    }

    internal readonly struct SigmaNativeContractBranch
    {
        internal SigmaNativeContractBranch(int candidateOrdinal,
            int supportKey, SigmaNativeDeltaWitness delta,
            SigmaNativeQueryClaim claim, SigmaDirectionalActionWitness action,
            bool opticalClaim)
            : this(candidateOrdinal, supportKey, delta,
                new[] { claim }, new[] { action }, new[] { opticalClaim })
        {
        }

        internal SigmaNativeContractBranch(int candidateOrdinal,
            int supportKey, SigmaNativeDeltaWitness delta,
            SigmaNativeQueryClaim[] claims,
            SigmaDirectionalActionWitness[] actions, bool[] opticalClaims)
        {
            if (claims == null || actions == null || opticalClaims == null ||
                claims.Length == 0 || claims.Length != actions.Length ||
                claims.Length != opticalClaims.Length)
                throw new ArgumentException(
                    "A joint branch retains one complete witness per query.");
            CandidateOrdinal = candidateOrdinal;
            SupportKey = supportKey;
            Delta = delta;
            Claims = claims.ToArray();
            Actions = actions.ToArray();
            OpticalClaims = opticalClaims.ToArray();
        }

        internal int CandidateOrdinal { get; }
        internal int SupportKey { get; }
        internal SigmaNativeDeltaWitness Delta { get; }
        internal SigmaNativeQueryClaim[] Claims { get; }
        internal SigmaDirectionalActionWitness[] Actions { get; }
        internal bool[] OpticalClaims { get; }
        internal SigmaNativeQueryClaim Claim => Claims[0];
        internal SigmaDirectionalActionWitness Action => Actions[0];
        internal bool OpticalClaim => OpticalClaims.Any(value => value);
    }

    internal sealed class SigmaNativeContractResult
    {
        internal SigmaNativeContractResult(IEnumerable<SigmaNativeContractBranch> branches)
        {
            Branches = branches.OrderBy(branch => branch.CandidateOrdinal).ToArray();
            Resolution = Branches.Length switch
            {
                0 => SigmaNativeOracleResolution.None,
                1 => SigmaNativeOracleResolution.Unique,
                _ when Branches.All(branch => branch.Delta.Equals(Branches[0].Delta))
                    => SigmaNativeOracleResolution.CommonDelta,
                _ => SigmaNativeOracleResolution.Ambiguous,
            };
        }

        internal SigmaNativeContractBranch[] Branches { get; }
        internal SigmaNativeOracleResolution Resolution { get; }
        internal bool HasCanonicalAnswer => Resolution == SigmaNativeOracleResolution.Unique ||
            Resolution == SigmaNativeOracleResolution.CommonDelta;
    }

    internal readonly struct SigmaNativeRelationWitness : IEquatable<SigmaNativeRelationWitness>
    {
        internal SigmaNativeRelationWitness(SigmaS16 transition,
            SigmaS16 associator, SigmaMerkabaRelationClass relationClass)
        {
            Transition = transition;
            Associator = associator;
            RelationClass = relationClass;
        }

        internal SigmaS16 Transition { get; }
        internal SigmaS16 Associator { get; }
        internal SigmaMerkabaRelationClass RelationClass { get; }
        public bool Equals(SigmaNativeRelationWitness other) =>
            Transition == other.Transition && Associator == other.Associator &&
            RelationClass == other.RelationClass;
        public override bool Equals(object obj) =>
            obj is SigmaNativeRelationWitness other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(Transition, Associator, RelationClass);
    }

    /// <summary>
    /// N2R-only exact semantic evaluator. It consumes the N1R generated program,
    /// owns no field state and returns only disposable forward/reverse proof data.
    /// </summary>
    internal static class SigmaMerkabaSemanticOracle
    {
        internal static SigmaNativeQuerySupportSummary SummarizeCell(int cellIndex,
            IReadOnlyList<SigmaNativeOracleCell> cells, bool defaultBoundaryClosed,
            string gaugeFingerprint)
        {
            SigmaNativeOracleCell cell = cells[cellIndex];
            return new SigmaNativeQuerySupportSummary(cellIndex, cell.State.IsZero,
                defaultBoundaryClosed, cell.Resident, cell.Gauge.Level != 0,
                SigmaGeneratedMerkabaProgram.ProgramFingerprint, gaugeFingerprint);
        }

        internal static int[] SelectNativeQuerySupport(
            IEnumerable<SigmaNativeQuerySupportSummary> summaries,
            string expectedGaugeFingerprint) => summaries
            .Where(summary => !summary.CanOmit(
                SigmaGeneratedMerkabaProgram.ProgramFingerprint,
                expectedGaugeFingerprint))
            .Select(summary => summary.CellIndex).Distinct().OrderBy(index => index)
            .ToArray();

        internal static long[] EvaluateMerkabaShadow(SigmaS16 state)
        {
            var shadow = new long[4];
            for (int axis = 0; axis < shadow.Length; ++axis)
            {
                long sum = 0L;
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    long weight = SigmaNumericDomain.FromRatio(
                        SigmaGeneratedMerkabaProgram.ShadowNumerator(lane, axis), 4L);
                    sum = SigmaNumericDomain.QAdd(sum,
                        SigmaNumericDomain.QMul(state[lane], weight));
                }
                shadow[axis] = sum;
            }
            return shadow;
        }

        internal static SigmaNativeContribution? EvaluateNativeQuery(
            SigmaNativeOracleCell cell, int sourceCell,
            SigmaNativeOracleQuery query)
        {
            RequireSensorExpressionShape();
            if (cell.State.IsZero || cell.Footprint != query.Footprint)
                return null;
            long[] shadow = EvaluateMerkabaShadow(cell.State);
            long order = DotPoint(shadow, query.OrderRow);
            long nativeOptical = DotPoint(shadow, query.OpticalRow);
            long weightedOptical = SigmaNumericDomain.QMul(nativeOptical,
                cell.Measure);
            return new SigmaNativeContribution(cell.SupportKey, cell.Footprint,
                Point(order), Point(weightedOptical), cell.Measure, sourceCell);
        }

        internal static SigmaNativeSceneShadow EvaluateAndReduce(
            IReadOnlyList<SigmaNativeOracleCell> cells,
            IEnumerable<int> selectedCells, SigmaNativeOracleQuery query)
        {
            SigmaNativeContribution[] contributions = selectedCells
                .Select(index => EvaluateNativeQuery(cells[index], index, query))
                .Where(value => value.HasValue).Select(value => value.Value).ToArray();
            return ReduceNativeQuery(contributions, query.Footprint);
        }

        internal static SigmaNativeSceneShadow ReduceNativeQuery(
            IEnumerable<SigmaNativeContribution> source, int footprint)
        {
            var supports = source.Where(value => value.Footprint == footprint)
                .GroupBy(value => value.SupportKey).Select(group =>
                {
                    SigmaNativeContribution[] values = group.ToArray();
                    SigmaQ48Interval order = Hull(values.Select(value => value.Order));
                    SigmaQ48Interval weighted = SumOutward(values.Select(
                        value => value.WeightedOptical));
                    long measure = values.Aggregate(0L, (sum, value) =>
                        SigmaNumericDomain.QAdd(sum, value.Measure));
                    SigmaQ48Interval optical = DivideOutward(weighted, Point(measure));
                    return (Support: group.Key, Order: order, Optical: optical);
                }).OrderBy(value => value.Support).ToArray();
            if (supports.Length == 0)
                return new SigmaNativeSceneShadow(footprint, Array.Empty<int>(),
                    Array.Empty<int>(), SigmaQ48Interval.Empty,
                    SigmaQ48Interval.Empty);

            var first = supports.Where(candidate => supports.All(other =>
                    other.Support == candidate.Support ||
                    !ProvenStrictlyBefore(other.Order, candidate.Order)))
                .ToArray();
            int[] firstKeys = first.Select(value => value.Support).OrderBy(v => v).ToArray();
            int[] behind = supports.Select(value => value.Support).Except(firstKeys)
                .OrderBy(value => value).ToArray();
            return new SigmaNativeSceneShadow(footprint, firstKeys, behind,
                Hull(first.Select(value => value.Order)),
                Hull(first.Select(value => value.Optical)));
        }

        internal static SigmaNativeContractResult ContractNativeQuery(
            SigmaNativeOracleQuery query,
            IEnumerable<SigmaNativePreimageCandidate> candidates)
        {
            var branches = new List<SigmaNativeContractBranch>();
            foreach (SigmaNativePreimageCandidate candidate in candidates)
            {
                SigmaNativeContribution? proposed = EvaluateNativeQuery(
                    candidate.Proposed, candidate.CandidateOrdinal, query);
                if (!proposed.HasValue || !candidate.NativeRelationSatisfied)
                    continue;
                bool orderCompatible = !query.OrderEvidence ||
                    Overlaps(proposed.Value.Order, query.MeasuredOrder);
                bool opticalClaim = query.OpticalEvidence &&
                    query.PhotometricLaw.HasBoundedClaim;
                SigmaQ48Interval proposedOptical = DivideOutward(
                    proposed.Value.WeightedOptical, Point(proposed.Value.Measure));
                bool opticalCompatible = !opticalClaim || Overlaps(
                    query.PhotometricLaw.Apply(proposedOptical),
                    query.MeasuredOptical);
                bool hasEvidence = query.OrderEvidence || opticalClaim;
                if (!orderCompatible || !opticalCompatible || !hasEvidence)
                    continue;

                SigmaNativeQueryClaim claim;
                SigmaQ48Interval residual;
                if (candidate.Prior.State.IsZero)
                {
                    claim = SigmaNativeQueryClaim.FirstHitMould;
                    residual = query.MeasuredOrder;
                }
                else
                {
                    SigmaNativeContribution prior = EvaluateNativeQuery(candidate.Prior,
                        candidate.CandidateOrdinal, query).Value;
                    residual = SubtractOutward(query.MeasuredOrder, prior.Order);
                    claim = ProvenStrictlyBefore(prior.Order, query.MeasuredOrder)
                        ? SigmaNativeQueryClaim.PreHitExclusion
                        : Overlaps(prior.Order, query.MeasuredOrder)
                            ? SigmaNativeQueryClaim.FirstHitMould
                            : SigmaNativeQueryClaim.NoClaim;
                    if (claim == SigmaNativeQueryClaim.NoClaim ||
                        claim == SigmaNativeQueryClaim.PreHitExclusion &&
                        !candidate.IdentityPreservingTransport)
                        continue;
                }
                SigmaDirectionalActionWitness action = SigmaGeneratedMerkabaProgram
                    .BuildDirectionalAction(claim, query.Direction, residual);
                branches.Add(new SigmaNativeContractBranch(candidate.CandidateOrdinal,
                    candidate.Proposed.SupportKey, candidate.Delta, claim, action,
                    opticalClaim));
            }
            return new SigmaNativeContractResult(branches);
        }

        internal static SigmaNativeContractResult ContractJoint(
            IEnumerable<SigmaNativeOracleQuery> queries,
            IReadOnlyList<SigmaNativePreimageCandidate> candidates)
        {
            SigmaNativeContractBranch[][] perQuery = queries.Select(query =>
                ContractNativeQuery(query, candidates).Branches).ToArray();
            if (perQuery.Length == 0)
                return new SigmaNativeContractResult(Array.Empty<SigmaNativeContractBranch>());
            var intersection = perQuery[0].ToDictionary(branch =>
                branch.CandidateOrdinal, branch => new List<SigmaNativeContractBranch>
                {
                    branch,
                });
            for (int index = 1; index < perQuery.Length; ++index)
            {
                var allowed = perQuery[index].ToDictionary(branch =>
                    branch.CandidateOrdinal, branch => branch);
                foreach (int key in intersection.Keys.Where(key =>
                    !allowed.ContainsKey(key)).ToArray())
                    intersection.Remove(key);
                foreach (int key in intersection.Keys)
                    intersection[key].Add(allowed[key]);
            }
            return new SigmaNativeContractResult(intersection.Values.Select(values =>
            {
                SigmaNativeContractBranch first = values[0];
                return new SigmaNativeContractBranch(first.CandidateOrdinal,
                    first.SupportKey, first.Delta,
                    values.SelectMany(value => value.Claims).ToArray(),
                    values.SelectMany(value => value.Actions).ToArray(),
                    values.SelectMany(value => value.OpticalClaims).ToArray());
            }));
        }

        internal static SigmaNativeRelationWitness EvaluateNativeRelation(
            SigmaS16 left, SigmaS16 right, SigmaS16 context)
        {
            if (left.IsZero && right.IsZero && context.IsZero)
                return new SigmaNativeRelationWitness(SigmaS16.Zero,
                    SigmaS16.Zero, SigmaMerkabaRelationClass.DefaultSat);
            SigmaS16 transition = SigmaS16Operators.Transition(left, right);
            SigmaS16 associator = SigmaS16Operators.Associator(left, right, context);
            SigmaMerkabaRelationClass relation = !associator.IsZero
                ? SigmaMerkabaRelationClass.NonassociativeContext
                : SigmaMerkabaRelationClass.Regular;
            return new SigmaNativeRelationWitness(transition, associator, relation);
        }

        internal static SigmaQ48Interval AddOutward(SigmaQ48Interval left,
            SigmaQ48Interval right) => left.IsEmpty || right.IsEmpty
            ? SigmaQ48Interval.Empty
            : new SigmaQ48Interval(SigmaNumericDomain.QAdd(left.Lower, right.Lower),
                SigmaNumericDomain.QAdd(left.Upper, right.Upper));

        internal static SigmaQ48Interval SubtractOutward(SigmaQ48Interval left,
            SigmaQ48Interval right) => left.IsEmpty || right.IsEmpty
            ? SigmaQ48Interval.Empty
            : new SigmaQ48Interval(SigmaNumericDomain.QSub(left.Lower, right.Upper),
                SigmaNumericDomain.QSub(left.Upper, right.Lower));

        internal static SigmaQ48Interval MultiplyOutward(SigmaQ48Interval left,
            SigmaQ48Interval right)
        {
            if (left.IsEmpty || right.IsEmpty) return SigmaQ48Interval.Empty;
            long[] lower =
            {
                SigmaNumericDomain.QMulLower(left.Lower, right.Lower),
                SigmaNumericDomain.QMulLower(left.Lower, right.Upper),
                SigmaNumericDomain.QMulLower(left.Upper, right.Lower),
                SigmaNumericDomain.QMulLower(left.Upper, right.Upper),
            };
            long[] upper =
            {
                SigmaNumericDomain.QMulUpper(left.Lower, right.Lower),
                SigmaNumericDomain.QMulUpper(left.Lower, right.Upper),
                SigmaNumericDomain.QMulUpper(left.Upper, right.Lower),
                SigmaNumericDomain.QMulUpper(left.Upper, right.Upper),
            };
            return new SigmaQ48Interval(lower.Min(), upper.Max());
        }

        private static SigmaQ48Interval DivideOutward(SigmaQ48Interval numerator,
            SigmaQ48Interval denominator)
        {
            if (numerator.IsEmpty || denominator.IsEmpty || denominator.Contains(0L))
                return SigmaQ48Interval.Full;
            long[] lower =
            {
                SigmaNumericDomain.QDivLower(numerator.Lower, denominator.Lower),
                SigmaNumericDomain.QDivLower(numerator.Lower, denominator.Upper),
                SigmaNumericDomain.QDivLower(numerator.Upper, denominator.Lower),
                SigmaNumericDomain.QDivLower(numerator.Upper, denominator.Upper),
            };
            long[] upper =
            {
                SigmaNumericDomain.QDivUpper(numerator.Lower, denominator.Lower),
                SigmaNumericDomain.QDivUpper(numerator.Lower, denominator.Upper),
                SigmaNumericDomain.QDivUpper(numerator.Upper, denominator.Lower),
                SigmaNumericDomain.QDivUpper(numerator.Upper, denominator.Upper),
            };
            return new SigmaQ48Interval(lower.Min(), upper.Max());
        }

        private static SigmaQ48Interval SumOutward(
            IEnumerable<SigmaQ48Interval> values) => values.Aggregate(Point(0L),
            AddOutward);

        private static SigmaQ48Interval Hull(IEnumerable<SigmaQ48Interval> values)
        {
            SigmaQ48Interval[] materialized = values.Where(value => !value.IsEmpty)
                .ToArray();
            return materialized.Length == 0 ? SigmaQ48Interval.Empty :
                new SigmaQ48Interval(materialized.Min(value => value.Lower),
                    materialized.Max(value => value.Upper));
        }

        private static bool Overlaps(SigmaQ48Interval left,
            SigmaQ48Interval right) => !left.Intersect(right).IsEmpty;

        private static bool ProvenStrictlyBefore(SigmaQ48Interval left,
            SigmaQ48Interval right) => !left.IsEmpty && !right.IsEmpty &&
            left.Upper < right.Lower;

        private static long DotPoint(IReadOnlyList<long> value,
            IReadOnlyList<long> row)
        {
            long sum = 0L;
            for (int index = 0; index < 4; ++index)
                sum = SigmaNumericDomain.QAdd(sum,
                    SigmaNumericDomain.QMul(value[index], row[index]));
            return sum;
        }

        private static SigmaQ48Interval Point(long value) => new(value, value);

        private static void RequireSensorExpressionShape()
        {
            SigmaMerkabaExpression expression = SigmaGeneratedMerkabaProgram
                .Expressions.Single(value => value.Id == "SENSOR_SCENE_SHADOW");
            SigmaMerkabaIrOpcode[] actual = SigmaGeneratedMerkabaProgram.IrNodes
                .Skip(expression.NodeStart).Take(expression.NodeCount)
                .Select(node => node.Opcode).ToArray();
            SigmaMerkabaIrOpcode[] expected =
            {
                SigmaMerkabaIrOpcode.INPUT_FIELD,
                SigmaMerkabaIrOpcode.INPUT_QUERY,
                SigmaMerkabaIrOpcode.MERKABA_SHADOW,
                SigmaMerkabaIrOpcode.CALIBRATED_QUERY_CONTRACT,
                SigmaMerkabaIrOpcode.SCENE_REDUCE,
            };
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException(
                    "Generated sensor expression no longer matches this evaluator.");
        }
    }
}
