using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaNativeOracleResolution
    {
        None,
        Unique,
        CommonDelta,
        Ambiguous,
    }

    internal readonly struct SigmaNativePhotometricSegment
    {
        internal SigmaNativePhotometricSegment(long domainLower, long domainUpper,
            long slope, long offset)
        {
            if (domainLower > domainUpper)
                throw new ArgumentException("Transfer segment domain is empty.");
            if (slope < 0L)
                throw new ArgumentOutOfRangeException(nameof(slope),
                    "Calibrated transfer must be monotone.");
            DomainLower = domainLower;
            DomainUpper = domainUpper;
            Slope = slope;
            Offset = offset;
        }

        internal long DomainLower { get; }
        internal long DomainUpper { get; }
        internal long Slope { get; }
        internal long Offset { get; }
    }

    internal sealed class SigmaNativePhotometricChannelLaw
    {
        internal SigmaNativePhotometricChannelLaw(SigmaQ48Interval gain,
            SigmaQ48Interval illumination, SigmaQ48Interval whiteBalance,
            SigmaQ48Interval offset,
            IReadOnlyList<SigmaNativePhotometricSegment> transfer)
        {
            Gain = gain;
            Illumination = illumination;
            WhiteBalance = whiteBalance;
            Offset = offset;
            Transfer = transfer?.ToArray() ??
                throw new ArgumentNullException(nameof(transfer));
            if (Transfer.Length == 0)
                throw new ArgumentException("At least one transfer segment is required.",
                    nameof(transfer));
            for (int index = 1; index < Transfer.Length; ++index)
            {
                if (Transfer[index - 1].DomainUpper != Transfer[index].DomainLower)
                    throw new ArgumentException(
                        "Transfer segments must form one contiguous domain.",
                        nameof(transfer));
                long previous = SigmaNumericDomain.QAdd(
                    SigmaNumericDomain.QMul(Transfer[index - 1].DomainUpper,
                        Transfer[index - 1].Slope), Transfer[index - 1].Offset);
                long next = SigmaNumericDomain.QAdd(
                    SigmaNumericDomain.QMul(Transfer[index].DomainLower,
                        Transfer[index].Slope), Transfer[index].Offset);
                if (previous != next)
                    throw new ArgumentException(
                        "Transfer segments must be exactly continuous.",
                        nameof(transfer));
            }
        }

        internal SigmaQ48Interval Gain { get; }
        internal SigmaQ48Interval Illumination { get; }
        internal SigmaQ48Interval WhiteBalance { get; }
        internal SigmaQ48Interval Offset { get; }
        internal SigmaNativePhotometricSegment[] Transfer { get; }

        internal bool IsBounded => !Gain.IsEmpty && !Illumination.IsEmpty &&
            !WhiteBalance.IsEmpty && !Offset.IsEmpty;
    }

    internal sealed class SigmaNativePhotometricLaw
    {
        internal const int ChannelCount = 3;

        internal SigmaNativePhotometricLaw(bool metadataPresent,
            bool calibrationMatches, SigmaQ48Interval exposure,
            IReadOnlyList<SigmaNativePhotometricChannelLaw> channels,
            string transferFingerprint)
        {
            MetadataPresent = metadataPresent;
            CalibrationMatches = calibrationMatches;
            Exposure = exposure;
            Channels = channels?.ToArray() ??
                throw new ArgumentNullException(nameof(channels));
            if (Channels.Length != ChannelCount)
                throw new ArgumentException("Optical law requires three channels.",
                    nameof(channels));
            TransferFingerprint = transferFingerprint ??
                throw new ArgumentNullException(nameof(transferFingerprint));
        }

        internal bool MetadataPresent { get; }
        internal bool CalibrationMatches { get; }
        internal SigmaQ48Interval Exposure { get; }
        internal SigmaNativePhotometricChannelLaw[] Channels { get; }
        internal string TransferFingerprint { get; }
        internal bool HasBoundedClaim => MetadataPresent && CalibrationMatches &&
            !Exposure.IsEmpty && Channels.All(channel => channel.IsBounded) &&
            string.Equals(TransferFingerprint, ComputeTransferFingerprint(Channels),
                StringComparison.Ordinal);

        internal bool TryApply(int channel, SigmaQ48Interval nativeResponse,
            out SigmaQ48Interval observed)
        {
            observed = SigmaQ48Interval.Empty;
            if (!HasBoundedClaim || (uint)channel >= ChannelCount)
                return false;
            SigmaNativePhotometricChannelLaw law = Channels[channel];
            SigmaQ48Interval input = SigmaMerkabaSemanticOracle.AddOutward(
                SigmaMerkabaSemanticOracle.MultiplyOutward(
                    SigmaMerkabaSemanticOracle.MultiplyOutward(
                        SigmaMerkabaSemanticOracle.MultiplyOutward(
                            SigmaMerkabaSemanticOracle.MultiplyOutward(
                                nativeResponse, Exposure), law.Gain),
                        law.Illumination), law.WhiteBalance), law.Offset);
            if (input.IsEmpty || input.Lower < law.Transfer[0].DomainLower ||
                input.Upper > law.Transfer[^1].DomainUpper)
                return false;
            SigmaNativePhotometricSegment lower = law.Transfer.First(segment =>
                input.Lower >= segment.DomainLower &&
                input.Lower <= segment.DomainUpper);
            SigmaNativePhotometricSegment upper = law.Transfer.Last(segment =>
                input.Upper >= segment.DomainLower &&
                input.Upper <= segment.DomainUpper);
            observed = new SigmaQ48Interval(
                SigmaNumericDomain.QAdd(
                    SigmaNumericDomain.QMulLower(input.Lower, lower.Slope),
                    lower.Offset),
                SigmaNumericDomain.QAdd(
                    SigmaNumericDomain.QMulUpper(input.Upper, upper.Slope),
                    upper.Offset));
            return !observed.IsEmpty;
        }

        internal static string ComputeTransferFingerprint(
            IEnumerable<SigmaNativePhotometricChannelLaw> channels)
        {
            string canonical = string.Join("|", channels.SelectMany(
                (channel, channelIndex) => channel.Transfer.Select(
                    (segment, segmentIndex) => string.Join(":", channelIndex,
                        segmentIndex, segment.DomainLower, segment.DomainUpper,
                        segment.Slope, segment.Offset))));
            using SHA256 hash = SHA256.Create();
            return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))
                .Select(value => value.ToString("x2")));
        }
    }

    internal sealed class SigmaNativeOracleQuery
    {
        internal SigmaNativeOracleQuery(string entryPoint, int footprint,
            IReadOnlyList<long> orderRow,
            IReadOnlyList<IReadOnlyList<long>> opticalRows,
            SigmaQ48Interval measuredOrder,
            IReadOnlyList<SigmaQ48Interval> measuredOptical,
            SigmaQ48Interval direction, bool orderEvidence,
            bool opticalEvidence, SigmaNativePhotometricLaw photometricLaw)
        {
            EntryPoint = SigmaGeneratedMerkabaProgram.EntryPoints.SingleOrDefault(
                entry => string.Equals(entry.Id, entryPoint,
                    StringComparison.Ordinal));
            if (EntryPoint.Id == null)
                throw new ArgumentException("Query is not a generated entry point.",
                    nameof(entryPoint));
            if (orderRow == null || orderRow.Count != 4)
                throw new ArgumentException("Order contraction has four rows.",
                    nameof(orderRow));
            if (opticalRows == null ||
                opticalRows.Count != SigmaNativePhotometricLaw.ChannelCount ||
                opticalRows.Any(row => row == null || row.Count != 4))
                throw new ArgumentException(
                    "Optical contraction has three four-axis rows.",
                    nameof(opticalRows));
            if (measuredOptical == null ||
                measuredOptical.Count != SigmaNativePhotometricLaw.ChannelCount)
                throw new ArgumentException("Optical evidence has three channels.",
                    nameof(measuredOptical));
            Footprint = footprint;
            OrderRow = orderRow.ToArray();
            OpticalRows = opticalRows.Select(row => row.ToArray()).ToArray();
            MeasuredOrder = measuredOrder;
            MeasuredOptical = measuredOptical.ToArray();
            Direction = direction;
            OrderEvidence = orderEvidence;
            OpticalEvidence = opticalEvidence;
            PhotometricLaw = photometricLaw;
        }

        internal SigmaMerkabaEntryPoint EntryPoint { get; }
        internal int Footprint { get; }
        internal long[] OrderRow { get; }
        internal long[][] OpticalRows { get; }
        internal SigmaQ48Interval MeasuredOrder { get; }
        internal SigmaQ48Interval[] MeasuredOptical { get; }
        internal SigmaQ48Interval Direction { get; }
        internal bool OrderEvidence { get; }
        internal bool OpticalEvidence { get; }
        internal SigmaNativePhotometricLaw PhotometricLaw { get; }
    }

    internal readonly struct SigmaNativeOracleCell
    {
        internal SigmaNativeOracleCell(ulong supportKey, int footprint,
            SigmaGaugeCell gauge, SigmaS16 state, bool resident = true)
        {
            SupportKey = supportKey;
            Footprint = footprint;
            Gauge = gauge;
            State = state;
            Resident = resident;
        }

        internal ulong SupportKey { get; }
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
        internal SigmaNativeContribution(ulong supportKey, int footprint,
            SigmaQ48Interval order,
            IReadOnlyList<SigmaQ48Interval> weightedOptical,
            long measure, int sourceCell)
        {
            SupportKey = supportKey;
            Footprint = footprint;
            Order = order;
            WeightedOptical = weightedOptical?.ToArray() ??
                throw new ArgumentNullException(nameof(weightedOptical));
            if (WeightedOptical.Length != SigmaNativePhotometricLaw.ChannelCount)
                throw new ArgumentException("Contribution has three optical channels.",
                    nameof(weightedOptical));
            Measure = measure;
            SourceCell = sourceCell;
        }

        internal ulong SupportKey { get; }
        internal int Footprint { get; }
        internal SigmaQ48Interval Order { get; }
        internal SigmaQ48Interval[] WeightedOptical { get; }
        internal long Measure { get; }
        internal int SourceCell { get; }
    }

    internal readonly struct SigmaNativeSceneShadow : IEquatable<SigmaNativeSceneShadow>
    {
        internal SigmaNativeSceneShadow(int reducer, int footprint,
            ulong[] firstSupports, ulong[] behindSupports,
            SigmaQ48Interval order,
            IReadOnlyList<SigmaQ48Interval> optical)
        {
            Reducer = reducer;
            Footprint = footprint;
            FirstSupports = firstSupports ?? Array.Empty<ulong>();
            BehindSupports = behindSupports ?? Array.Empty<ulong>();
            Order = order;
            Optical = optical?.ToArray() ??
                throw new ArgumentNullException(nameof(optical));
            if (Optical.Length != SigmaNativePhotometricLaw.ChannelCount)
                throw new ArgumentException("Reduced query has three channels.",
                    nameof(optical));
        }

        internal int Reducer { get; }
        internal int Footprint { get; }
        internal ulong[] FirstSupports { get; }
        internal ulong[] BehindSupports { get; }
        internal SigmaQ48Interval Order { get; }
        internal SigmaQ48Interval[] Optical { get; }
        internal bool IsDefault => FirstSupports.Length == 0;

        public bool Equals(SigmaNativeSceneShadow other) =>
            Reducer == other.Reducer && Footprint == other.Footprint &&
            Order == other.Order && Optical.SequenceEqual(other.Optical) &&
            FirstSupports.SequenceEqual(other.FirstSupports) &&
            BehindSupports.SequenceEqual(other.BehindSupports);
        public override bool Equals(object obj) =>
            obj is SigmaNativeSceneShadow other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Reducer, Footprint,
            Order, Optical.Length, FirstSupports.Length, BehindSupports.Length);
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

    internal readonly struct SigmaNativeNearSingularLaw
    {
        internal SigmaNativeNearSingularLaw(SigmaQ48Interval residualMagnitude,
            string fingerprint)
        {
            ResidualMagnitude = residualMagnitude;
            Fingerprint = fingerprint ?? throw new ArgumentNullException(
                nameof(fingerprint));
        }

        internal SigmaQ48Interval ResidualMagnitude { get; }
        internal string Fingerprint { get; }
        internal bool IsCalibrated => !ResidualMagnitude.IsEmpty &&
            !ResidualMagnitude.Contains(0L) && string.Equals(Fingerprint,
                ComputeFingerprint(ResidualMagnitude), StringComparison.Ordinal);

        internal bool Contains(BigInteger rawMagnitude) => IsCalibrated &&
            rawMagnitude >= ResidualMagnitude.Lower &&
            rawMagnitude <= ResidualMagnitude.Upper;

        internal static string ComputeFingerprint(SigmaQ48Interval interval)
        {
            using SHA256 hash = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(
                $"near-singular-q48:{interval.Lower}:{interval.Upper}");
            return string.Concat(hash.ComputeHash(bytes)
                .Select(value => value.ToString("x2")));
        }
    }

    internal readonly struct SigmaNativeRelationInput
    {
        internal SigmaNativeRelationInput(SigmaS16 left, SigmaS16 right,
            SigmaS16 context, int transportGenerator, int transportAddress,
            int plaquetteA, int plaquetteC, int plaquetteBase,
            SigmaNativeNearSingularLaw nearLaw = default)
        {
            if ((uint)transportGenerator >= 16u ||
                (uint)transportAddress >= 16u || (uint)plaquetteA >= 16u ||
                (uint)plaquetteC >= 16u || (uint)plaquetteBase >= 16u)
                throw new ArgumentOutOfRangeException(nameof(transportGenerator));
            Left = left;
            Right = right;
            Context = context;
            TransportGenerator = transportGenerator;
            TransportAddress = transportAddress;
            PlaquetteA = plaquetteA;
            PlaquetteC = plaquetteC;
            PlaquetteBase = plaquetteBase;
            NearLaw = nearLaw;
        }

        internal SigmaS16 Left { get; }
        internal SigmaS16 Right { get; }
        internal SigmaS16 Context { get; }
        internal int TransportGenerator { get; }
        internal int TransportAddress { get; }
        internal int PlaquetteA { get; }
        internal int PlaquetteC { get; }
        internal int PlaquetteBase { get; }
        internal SigmaNativeNearSingularLaw NearLaw { get; }
    }

    internal readonly struct SigmaNativeRelationFactor :
        IEquatable<SigmaNativeRelationFactor>
    {
        internal SigmaNativeRelationFactor(SigmaS16 raw,
            SigmaQ48Interval[] normalized, bool diffractionKernel,
            SigmaExactFactorClass factorClass)
        {
            Raw = raw;
            Normalized = normalized ?? throw new ArgumentNullException(
                nameof(normalized));
            if (Normalized.Length != SigmaS16.LaneCount)
                throw new ArgumentException("A relation factor has sixteen lanes.",
                    nameof(normalized));
            DiffractionKernel = diffractionKernel;
            FactorClass = factorClass;
        }

        internal SigmaS16 Raw { get; }
        internal SigmaQ48Interval[] Normalized { get; }
        internal bool DiffractionKernel { get; }
        internal SigmaExactFactorClass FactorClass { get; }
        public bool Equals(SigmaNativeRelationFactor other) => Raw == other.Raw &&
            DiffractionKernel == other.DiffractionKernel &&
            FactorClass == other.FactorClass &&
            Normalized.SequenceEqual(other.Normalized);
        public override bool Equals(object obj) =>
            obj is SigmaNativeRelationFactor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Raw,
            DiffractionKernel, FactorClass);
    }

    internal readonly struct SigmaNativeRelationWitness :
        IEquatable<SigmaNativeRelationWitness>
    {
        internal SigmaNativeRelationWitness(SigmaS16 transition,
            SigmaNativeRelationFactor link, SigmaNativeRelationFactor associator,
            SigmaQ48Interval plaquette, SigmaExactFactorClass plaquetteClass,
            SigmaExactFactorClass closureClass, int exactAnnihilatorAction,
            BigInteger minimumAnnihilatorResidual,
            SigmaMerkabaRelationClass relationClass)
        {
            Transition = transition;
            Link = link;
            Associator = associator;
            Plaquette = plaquette;
            PlaquetteClass = plaquetteClass;
            ClosureClass = closureClass;
            ExactAnnihilatorAction = exactAnnihilatorAction;
            MinimumAnnihilatorResidual = minimumAnnihilatorResidual;
            RelationClass = relationClass;
        }

        internal SigmaS16 Transition { get; }
        internal SigmaNativeRelationFactor Link { get; }
        internal SigmaNativeRelationFactor Associator { get; }
        internal SigmaQ48Interval Plaquette { get; }
        internal SigmaExactFactorClass PlaquetteClass { get; }
        internal SigmaExactFactorClass ClosureClass { get; }
        internal int ExactAnnihilatorAction { get; }
        internal BigInteger MinimumAnnihilatorResidual { get; }
        internal SigmaMerkabaRelationClass RelationClass { get; }
        internal bool PermitsClosure => RelationClass ==
                SigmaMerkabaRelationClass.DefaultSat ||
            RelationClass == SigmaMerkabaRelationClass.Regular ||
            RelationClass == SigmaMerkabaRelationClass.ExactZeroDivisor ||
            RelationClass == SigmaMerkabaRelationClass.NearSingularQ48;
        internal bool PermitsIdentityTransport => PermitsClosure &&
            RelationClass != SigmaMerkabaRelationClass.NearSingularQ48;
        public bool Equals(SigmaNativeRelationWitness other) =>
            Transition == other.Transition && Link.Equals(other.Link) &&
            Associator.Equals(other.Associator) && Plaquette == other.Plaquette &&
            PlaquetteClass == other.PlaquetteClass &&
            ClosureClass == other.ClosureClass &&
            ExactAnnihilatorAction == other.ExactAnnihilatorAction &&
            MinimumAnnihilatorResidual == other.MinimumAnnihilatorResidual &&
            RelationClass == other.RelationClass;
        public override bool Equals(object obj) =>
            obj is SigmaNativeRelationWitness other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Transition,
            Link, Associator, Plaquette, ClosureClass, RelationClass);
    }

    internal readonly struct SigmaNativePreimageCandidate
    {
        internal SigmaNativePreimageCandidate(int candidateOrdinal,
            SigmaNativeOracleCell prior, SigmaNativeOracleCell proposed,
            SigmaNativeDeltaWitness delta, SigmaNativeRelationInput relation)
        {
            if (relation.Left != proposed.State)
                throw new ArgumentException(
                    "Candidate relation must evaluate the proposed full S16 state.",
                    nameof(relation));
            CandidateOrdinal = candidateOrdinal;
            Prior = prior;
            Proposed = proposed;
            Delta = delta;
            Relation = relation;
        }

        internal int CandidateOrdinal { get; }
        internal SigmaNativeOracleCell Prior { get; }
        internal SigmaNativeOracleCell Proposed { get; }
        internal SigmaNativeDeltaWitness Delta { get; }
        internal SigmaNativeRelationInput Relation { get; }
    }

    internal readonly struct SigmaNativeContractBranch
    {
        internal SigmaNativeContractBranch(int candidateOrdinal,
            ulong supportKey, SigmaNativeDeltaWitness delta,
            SigmaNativeQueryClaim claim, SigmaDirectionalActionWitness action,
            bool opticalClaim, SigmaNativeRelationWitness relation)
            : this(candidateOrdinal, supportKey, delta,
                new[] { claim }, new[] { action }, new[] { opticalClaim }, relation)
        {
        }

        internal SigmaNativeContractBranch(int candidateOrdinal,
            ulong supportKey, SigmaNativeDeltaWitness delta,
            SigmaNativeQueryClaim[] claims,
            SigmaDirectionalActionWitness[] actions, bool[] opticalClaims,
            SigmaNativeRelationWitness relation)
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
            Relation = relation;
        }

        internal int CandidateOrdinal { get; }
        internal ulong SupportKey { get; }
        internal SigmaNativeDeltaWitness Delta { get; }
        internal SigmaNativeQueryClaim[] Claims { get; }
        internal SigmaDirectionalActionWitness[] Actions { get; }
        internal bool[] OpticalClaims { get; }
        internal SigmaNativeRelationWitness Relation { get; }
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
        internal bool HasCanonicalAnswer => Resolution ==
                SigmaNativeOracleResolution.Unique ||
            Resolution == SigmaNativeOracleResolution.CommonDelta;
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
            RequireForwardExpression(query.EntryPoint, 7,
                SigmaMerkabaIrOpcode.SCENE_REDUCE);
            if (query.EntryPoint.Reducer == 3)
                throw new InvalidOperationException(
                    "Intrinsic relation entry point requires a native context tuple.");
            if (cell.State.IsZero || cell.Footprint != query.Footprint)
                return null;
            long[] shadow = EvaluateMerkabaShadow(cell.State);
            long order = DotPoint(shadow, query.OrderRow);
            SigmaQ48Interval[] weightedOptical = query.OpticalRows.Select(row =>
                Point(SigmaNumericDomain.QMul(DotPoint(shadow, row),
                    cell.Measure))).ToArray();
            return new SigmaNativeContribution(cell.SupportKey, cell.Footprint,
                Point(order), weightedOptical, cell.Measure, sourceCell);
        }

        internal static SigmaNativeSceneShadow EvaluateAndReduce(
            IReadOnlyList<SigmaNativeOracleCell> cells,
            IEnumerable<int> selectedCells, SigmaNativeOracleQuery query)
        {
            SigmaNativeContribution[] contributions = selectedCells
                .Select(index => EvaluateNativeQuery(cells[index], index, query))
                .Where(value => value.HasValue).Select(value => value.Value).ToArray();
            return ReduceNativeQuery(contributions, query);
        }

        internal static SigmaNativeSceneShadow ReduceNativeQuery(
            IEnumerable<SigmaNativeContribution> source,
            SigmaNativeOracleQuery query)
        {
            int reducer = query.EntryPoint.Reducer;
            if (reducer != 0 && reducer != 1 && reducer != 4)
                throw new InvalidOperationException(
                    $"Reducer {reducer} is not a field-shadow reducer.");
            var supports = source.Where(value => value.Footprint == query.Footprint)
                .GroupBy(value => value.SupportKey).Select(group =>
                {
                    SigmaNativeContribution[] values = group.ToArray();
                    SigmaQ48Interval order = Hull(values.Select(value => value.Order));
                    long measure = values.Aggregate(0L, (sum, value) =>
                        SigmaNumericDomain.QAdd(sum, value.Measure));
                    SigmaQ48Interval[] optical = Enumerable.Range(0,
                            SigmaNativePhotometricLaw.ChannelCount)
                        .Select(channel => DivideOutward(SumOutward(values.Select(
                            value => value.WeightedOptical[channel])), Point(measure)))
                        .ToArray();
                    return (Support: group.Key, Order: order, Optical: optical);
                }).OrderBy(value => value.Support).ToArray();
            if (supports.Length == 0)
                return new SigmaNativeSceneShadow(reducer, query.Footprint,
                    Array.Empty<ulong>(), Array.Empty<ulong>(),
                    SigmaQ48Interval.Empty, EmptyOptical());

            var first = reducer == 1
                ? supports.Where(candidate => supports.All(other =>
                    other.Support == candidate.Support ||
                    !ProvenStrictlyBefore(other.Order, candidate.Order))).ToArray()
                : supports;
            ulong[] firstKeys = first.Select(value => value.Support)
                .OrderBy(value => value).ToArray();
            ulong[] behind = reducer == 1
                ? supports.Select(value => value.Support).Except(firstKeys)
                    .OrderBy(value => value).ToArray()
                : Array.Empty<ulong>();
            SigmaQ48Interval[] opticalResult = Enumerable.Range(0,
                    SigmaNativePhotometricLaw.ChannelCount)
                .Select(channel => Hull(first.Select(value =>
                    value.Optical[channel]))).ToArray();
            return new SigmaNativeSceneShadow(reducer, query.Footprint,
                firstKeys, behind,
                Hull(first.Select(value => value.Order)),
                opticalResult);
        }

        internal static SigmaNativeContractResult ContractNativeQuery(
            SigmaNativeOracleQuery query,
            IEnumerable<SigmaNativePreimageCandidate> candidates)
        {
            RequireReverseExpression(query.EntryPoint);
            var branches = new List<SigmaNativeContractBranch>();
            foreach (SigmaNativePreimageCandidate candidate in candidates)
            {
                SigmaNativeRelationWitness relation = EvaluateNativeRelation(
                    candidate.Relation);
                SigmaNativeContribution? proposed = EvaluateNativeQuery(
                    candidate.Proposed, candidate.CandidateOrdinal, query);
                if (!proposed.HasValue || !relation.PermitsClosure)
                    continue;
                bool orderCompatible = !query.OrderEvidence ||
                    Overlaps(proposed.Value.Order, query.MeasuredOrder);
                bool opticalClaim = query.OpticalEvidence &&
                    query.PhotometricLaw.HasBoundedClaim;
                bool opticalCompatible = true;
                for (int channel = 0;
                    opticalClaim && channel < SigmaNativePhotometricLaw.ChannelCount;
                    ++channel)
                {
                    SigmaQ48Interval proposedOptical = DivideOutward(
                        proposed.Value.WeightedOptical[channel],
                        Point(proposed.Value.Measure));
                    opticalCompatible &= query.PhotometricLaw.TryApply(channel,
                        proposedOptical, out SigmaQ48Interval predicted) &&
                        Overlaps(predicted, query.MeasuredOptical[channel]);
                }
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
                    bool identityPreservingTransport =
                        DeriveIdentityPreservingTransport(candidate, relation);
                    if (claim == SigmaNativeQueryClaim.NoClaim ||
                        claim == SigmaNativeQueryClaim.PreHitExclusion &&
                        !identityPreservingTransport)
                        continue;
                }
                SigmaDirectionalActionWitness action = SigmaGeneratedMerkabaProgram
                    .BuildDirectionalAction(claim, query.Direction, residual);
                branches.Add(new SigmaNativeContractBranch(candidate.CandidateOrdinal,
                    candidate.Proposed.SupportKey, candidate.Delta, claim, action,
                    opticalClaim, relation));
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
                    values.SelectMany(value => value.OpticalClaims).ToArray(),
                    first.Relation);
            }));
        }

        internal static SigmaNativeRelationWitness EvaluateNativeRelation(
            SigmaS16 left, SigmaS16 right, SigmaS16 context) =>
            EvaluateNativeRelation(new SigmaNativeRelationInput(left, right,
                context, 0, 0, 0, 0, 0));

        internal static SigmaNativeRelationWitness EvaluateNativeRelation(
            SigmaNativeRelationInput input)
        {
            RequireIntrinsicRelationExpression();
            bool allDefault = input.Left.IsZero && input.Right.IsZero &&
                input.Context.IsZero;
            int transport = SigmaGeneratedMerkabaProgram.SignTransport(
                input.TransportGenerator, input.TransportAddress);
            SigmaS16 transported = transport < 0
                ? Negate(input.Left) : input.Left;
            SigmaS16 linkRaw = SigmaS16Operators.Subtract(input.Right, transported);
            SigmaS16 associatorRaw = SigmaS16Operators.Associator(input.Left,
                input.Right, input.Context);
            SigmaNativeRelationFactor link = NormalizeFactor(linkRaw);
            SigmaNativeRelationFactor associator = NormalizeFactor(associatorRaw);
            int holonomy = SigmaGeneratedMerkabaProgram.PlaquetteHolonomy(
                input.PlaquetteA, input.PlaquetteC, input.PlaquetteBase);
            SigmaQ48Interval plaquette = Point(SigmaNumericDomain.FromRatio(
                holonomy - 1L, 2L));
            SigmaExactFactorClass plaquetteClass =
                SigmaGeneratedMerkabaProgram.ClassifyExactZeroFactor(plaquette);
            SigmaExactFactorClass closureClass = AggregateFactorClasses(
                link.FactorClass, associator.FactorClass, plaquetteClass);
            SigmaS16 transitionState = SigmaS16Operators.Transition(input.Left,
                input.Right);
            FindAnnihilator(transitionState, out int exactAnnihilator,
                out BigInteger minimumResidual);
            bool exactZd = !transitionState.IsZero && exactAnnihilator >= 0;
            bool calibratedNear = !exactZd &&
                input.NearLaw.Contains(minimumResidual);

            SigmaMerkabaRelationClass relationClass;
            if (allDefault)
                relationClass = SigmaMerkabaRelationClass.DefaultSat;
            else if (closureClass == SigmaExactFactorClass.Unresolved)
                relationClass = SigmaMerkabaRelationClass.Unresolved;
            else if (associator.FactorClass ==
                     SigmaExactFactorClass.ProvenIncompatible)
                relationClass = SigmaMerkabaRelationClass.NonassociativeContext;
            else if (closureClass == SigmaExactFactorClass.ProvenIncompatible)
                relationClass = SigmaMerkabaRelationClass.NoRelation;
            else if (exactZd)
                relationClass = SigmaMerkabaRelationClass.ExactZeroDivisor;
            else if (calibratedNear)
                relationClass = SigmaMerkabaRelationClass.NearSingularQ48;
            else
                relationClass = SigmaMerkabaRelationClass.Regular;
            return new SigmaNativeRelationWitness(transitionState, link,
                associator, plaquette, plaquetteClass, closureClass,
                exactAnnihilator, minimumResidual, relationClass);
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

        private static SigmaQ48Interval[] EmptyOptical() => Enumerable.Repeat(
            SigmaQ48Interval.Empty, SigmaNativePhotometricLaw.ChannelCount).ToArray();

        private static SigmaNativeRelationFactor NormalizeFactor(SigmaS16 raw)
        {
            bool normalized = SigmaGeneratedMerkabaProgram.TryNormalizePrimitiveDefect(
                raw, out SigmaQ48Interval[] intervals, out bool diffractionKernel);
            SigmaExactFactorClass factorClass = diffractionKernel || !normalized
                ? SigmaExactFactorClass.Unresolved
                : AggregateFactorClasses(intervals.Select(
                    SigmaGeneratedMerkabaProgram.ClassifyExactZeroFactor).ToArray());
            return new SigmaNativeRelationFactor(raw, intervals,
                diffractionKernel, factorClass);
        }

        private static SigmaExactFactorClass AggregateFactorClasses(
            params SigmaExactFactorClass[] factors)
        {
            if (factors.Any(value =>
                    value == SigmaExactFactorClass.ProvenIncompatible))
                return SigmaExactFactorClass.ProvenIncompatible;
            return factors.Any(value => value == SigmaExactFactorClass.Unresolved)
                ? SigmaExactFactorClass.Unresolved
                : SigmaExactFactorClass.ProvenExactClosed;
        }

        private static void FindAnnihilator(SigmaS16 transition,
            out int exactAction, out BigInteger minimumResidual)
        {
            exactAction = -1;
            minimumResidual = BigInteger.Zero;
            bool first = true;
            for (int action = 0;
                action < SigmaGeneratedAlgebra.AnnihilatorActionCount; ++action)
            {
                SigmaS16 residual = SigmaS16Operators.RightSignedDyadAction(
                    transition, SigmaS16Operators.GetAnnihilatorAction(action));
                BigInteger error = BigInteger.Zero;
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                    error += BigInteger.Abs(new BigInteger(residual[lane]));
                if (first || error < minimumResidual)
                {
                    first = false;
                    minimumResidual = error;
                }
                if (error.IsZero && exactAction < 0)
                    exactAction = action;
            }
        }

        private static SigmaS16 Negate(SigmaS16 value)
        {
            var output = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < output.Length; ++lane)
                output[lane] = SigmaNumericDomain.QNegate(value[lane]);
            return SigmaS16.FromArray(output);
        }

        private static bool DeriveIdentityPreservingTransport(
            SigmaNativePreimageCandidate candidate,
            SigmaNativeRelationWitness relation) =>
            candidate.Prior.SupportKey == candidate.Proposed.SupportKey &&
            SameGaugeCell(candidate.Prior.Gauge, candidate.Proposed.Gauge) &&
            relation.PermitsIdentityTransport;

        private static bool SameGaugeCell(SigmaGaugeCell left,
            SigmaGaugeCell right) => left.U == right.U && left.V == right.V &&
            left.Level == right.Level && string.Equals(left.PayloadFingerprint,
                right.PayloadFingerprint, StringComparison.Ordinal);

        private static void RequireForwardExpression(
            SigmaMerkabaEntryPoint entryPoint, int requiredExpression,
            SigmaMerkabaIrOpcode requiredRoot)
        {
            if (entryPoint.ForwardExpression != requiredExpression)
                throw new InvalidOperationException(
                    $"Entry {entryPoint.Id} does not select expression " +
                    $"{requiredExpression}.");
            SigmaMerkabaExpression expression = SigmaGeneratedMerkabaProgram
                .Expressions[entryPoint.ForwardExpression];
            if (SigmaGeneratedMerkabaProgram.IrNodes[expression.RootNode].Opcode !=
                requiredRoot)
                throw new InvalidOperationException(
                    $"Entry {entryPoint.Id} root does not match generated IR.");
        }

        private static void RequireReverseExpression(
            SigmaMerkabaEntryPoint entryPoint)
        {
            if (entryPoint.ReverseExpression < 0)
                throw new InvalidOperationException(
                    $"Entry {entryPoint.Id} forbids reverse evaluation.");
            SigmaMerkabaExpression expression = SigmaGeneratedMerkabaProgram
                .Expressions[entryPoint.ReverseExpression];
            if (SigmaGeneratedMerkabaProgram.IrNodes[expression.RootNode].Opcode !=
                SigmaMerkabaIrOpcode.PREIMAGE_UNION)
                throw new InvalidOperationException(
                    "Generated reverse entry does not retain support disjunction.");
        }

        private static void RequireIntrinsicRelationExpression()
        {
            SigmaMerkabaEntryPoint entry = SigmaGeneratedMerkabaProgram.EntryPoints
                .Single(value => value.Id == "INTRINSIC_RELATION");
            RequireForwardExpression(entry, 3,
                SigmaMerkabaIrOpcode.NORMALIZE_FACTOR);
            int[] factorExpressions = { 3, 4, 5 };
            foreach (int expressionIndex in factorExpressions)
            {
                SigmaMerkabaExpression expression = SigmaGeneratedMerkabaProgram
                    .Expressions[expressionIndex];
                SigmaMerkabaIrOpcode opcode = SigmaGeneratedMerkabaProgram.IrNodes[
                    expression.RootNode].Opcode;
                if (opcode != SigmaMerkabaIrOpcode.NORMALIZE_FACTOR &&
                    opcode != SigmaMerkabaIrOpcode.PLAQUETTE_NORMALIZE_HALF)
                    throw new InvalidOperationException(
                        "Generated native factor no longer matches relation evaluator.");
            }
            SigmaMerkabaExpression closure = SigmaGeneratedMerkabaProgram
                .Expressions[6];
            if (SigmaGeneratedMerkabaProgram.IrNodes[closure.RootNode].Opcode !=
                SigmaMerkabaIrOpcode.DIRECT_SUM)
                throw new InvalidOperationException(
                    "Generated native closure is not the direct sum of factors.");
        }
    }
}
