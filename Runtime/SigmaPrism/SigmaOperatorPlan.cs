using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>Closed semantic vocabulary accepted by the exact operator DAG.</summary>
    public enum SigmaOperatorOpcode : byte
    {
        GATHER,
        SCATTER,
        XOR_INDEX,
        PERMUTE,
        SIGN,
        NEGATE,
        ADD,
        SUB,
        SHIFT_LEFT,
        SHIFT_RIGHT,
        CMP_LT,
        CMP_LE,
        CMP_EQ,
        MIN,
        MAX,
        MASK,
        SELECT,
        FIXED_BOUNDED_REDUCTION,
        QMUL,
        QDIV,
        INTERVAL_MUL_LO,
        INTERVAL_MUL_HI,
        INTERVAL_DIV_LO,
        INTERVAL_DIV_HI,
        CONSTANT,
    }

    public readonly struct SigmaOperatorNode
    {
        internal SigmaOperatorNode(SigmaOperatorOpcode opcode, int a, int b,
            int c, int argument, long constant)
        {
            Opcode = opcode;
            A = a;
            B = b;
            C = c;
            Argument = argument;
            Constant = constant;
        }

        public SigmaOperatorOpcode Opcode { get; }
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public int Argument { get; }
        public long Constant { get; }
    }

    /// <summary>
    /// Immutable scalar DAG. Output scatter order and the source bracket descriptor
    /// are part of its fingerprint and cannot be erased by CSE.
    /// </summary>
    public sealed class SigmaOperatorPlan
    {
        internal SigmaOperatorPlan(string name, string bracketDescriptor,
            int inputCount, SigmaOperatorNode[] nodes, int[] reductionOperands,
            int[] outputs)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            BracketDescriptor = bracketDescriptor ?? string.Empty;
            InputCount = inputCount;
            Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            ReductionOperands = reductionOperands ??
                throw new ArgumentNullException(nameof(reductionOperands));
            Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
            Fingerprint = ComputeFingerprint();
        }

        public string Name { get; }
        public string BracketDescriptor { get; }
        public int InputCount { get; }
        public IReadOnlyList<SigmaOperatorNode> Nodes { get; }
        public IReadOnlyList<int> ReductionOperands { get; }
        public IReadOnlyList<int> Outputs { get; }
        public string Fingerprint { get; }

        public bool Contains(SigmaOperatorOpcode opcode)
        {
            for (int index = 0; index < Nodes.Count; ++index)
            {
                if (Nodes[index].Opcode == opcode)
                    return true;
            }
            return false;
        }

        private string ComputeFingerprint()
        {
            var text = new StringBuilder();
            text.Append(Name).Append('|').Append(BracketDescriptor).Append('|')
                .Append(InputCount).Append('|');
            for (int index = 0; index < Nodes.Count; ++index)
            {
                SigmaOperatorNode node = Nodes[index];
                text.Append((int)node.Opcode).Append(',').Append(node.A).Append(',')
                    .Append(node.B).Append(',').Append(node.C).Append(',')
                    .Append(node.Argument).Append(',').Append(node.Constant).Append(';');
            }
            text.Append('|');
            for (int index = 0; index < ReductionOperands.Count; ++index)
                text.Append(ReductionOperands[index]).Append(',');
            text.Append('|');
            for (int index = 0; index < Outputs.Count; ++index)
                text.Append(Outputs[index]).Append(',');
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
            var hex = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; ++index)
                hex.Append(digest[index].ToString("x2"));
            return hex.ToString();
        }
    }

    /// <summary>Deterministic CSE builder for the exact scalar DAG.</summary>
    public sealed class SigmaOperatorPlanBuilder
    {
        private readonly int _inputCount;
        private readonly bool _enableCommonSubexpressions;
        private readonly List<SigmaOperatorNode> _nodes = new();
        private readonly List<int> _reductionOperands = new();
        private readonly Dictionary<string, int> _common = new(StringComparer.Ordinal);

        public SigmaOperatorPlanBuilder(int inputCount,
            bool enableCommonSubexpressions = true)
        {
            if (inputCount < 0)
                throw new ArgumentOutOfRangeException(nameof(inputCount));
            _inputCount = inputCount;
            _enableCommonSubexpressions = enableCommonSubexpressions;
        }

        public int Gather(int input, int lane, bool permutation = false)
        {
            if ((uint)input >= _inputCount)
                throw new ArgumentOutOfRangeException(nameof(input));
            if ((uint)lane >= SigmaS16.LaneCount)
                throw new ArgumentOutOfRangeException(nameof(lane));
            return Intern(permutation ? SigmaOperatorOpcode.PERMUTE :
                SigmaOperatorOpcode.GATHER, input, lane, -1, 0, 0L);
        }

        public int Constant(long value) =>
            Intern(SigmaOperatorOpcode.CONSTANT, -1, -1, -1, 0, value);
        public int Sign(int value, int sign)
        {
            if (sign != -1 && sign != 1)
                throw new ArgumentOutOfRangeException(nameof(sign));
            return sign > 0 ? value : Negate(value);
        }
        public int Negate(int value) =>
            Intern(SigmaOperatorOpcode.NEGATE, value, -1, -1, 0, 0L);
        public int Add(int left, int right) =>
            InternCommutative(SigmaOperatorOpcode.ADD, left, right);
        public int Sub(int left, int right) =>
            Intern(SigmaOperatorOpcode.SUB, left, right, -1, 0, 0L);
        public int QMul(int left, int right) =>
            InternCommutative(SigmaOperatorOpcode.QMUL, left, right);
        public int QDiv(int numerator, int denominator) =>
            Intern(SigmaOperatorOpcode.QDIV, numerator, denominator, -1, 0, 0L);
        public int Min(int left, int right) =>
            InternCommutative(SigmaOperatorOpcode.MIN, left, right);
        public int Max(int left, int right) =>
            InternCommutative(SigmaOperatorOpcode.MAX, left, right);
        public int CompareLess(int left, int right) =>
            Intern(SigmaOperatorOpcode.CMP_LT, left, right, -1, 0, 0L);
        public int CompareLessEqual(int left, int right) =>
            Intern(SigmaOperatorOpcode.CMP_LE, left, right, -1, 0, 0L);
        public int CompareEqual(int left, int right) =>
            InternCommutative(SigmaOperatorOpcode.CMP_EQ, left, right);
        public int Mask(int predicate) =>
            Intern(SigmaOperatorOpcode.MASK, predicate, -1, -1, 0, 0L);
        public int Select(int predicate, int whenTrue, int whenFalse) =>
            Intern(SigmaOperatorOpcode.SELECT, predicate, whenTrue, whenFalse, 0, 0L);
        public int ShiftLeft(int value, int count) =>
            Intern(SigmaOperatorOpcode.SHIFT_LEFT, value, -1, -1, count, 0L);
        public int ShiftRight(int value, int count) =>
            Intern(SigmaOperatorOpcode.SHIFT_RIGHT, value, -1, -1, count, 0L);
        public int IntervalMulLower(int left, int right) =>
            Intern(SigmaOperatorOpcode.INTERVAL_MUL_LO, left, right, -1, 0, 0L);
        public int IntervalMulUpper(int left, int right) =>
            Intern(SigmaOperatorOpcode.INTERVAL_MUL_HI, left, right, -1, 0, 0L);
        public int IntervalDivLower(int left, int right) =>
            Intern(SigmaOperatorOpcode.INTERVAL_DIV_LO, left, right, -1, 0, 0L);
        public int IntervalDivUpper(int left, int right) =>
            Intern(SigmaOperatorOpcode.INTERVAL_DIV_HI, left, right, -1, 0, 0L);

        public int FixedReduction(IReadOnlyList<int> operands)
        {
            if (operands == null || operands.Count == 0)
                throw new ArgumentException("A reduction requires at least one operand.",
                    nameof(operands));
            var key = new StringBuilder("R:");
            for (int index = 0; index < operands.Count; ++index)
                key.Append(operands[index]).Append(',');
            string stableKey = key.ToString();
            if (_enableCommonSubexpressions &&
                _common.TryGetValue(stableKey, out int existing))
                return existing;
            int first = _reductionOperands.Count;
            for (int index = 0; index < operands.Count; ++index)
                _reductionOperands.Add(operands[index]);
            int nodeIndex = _nodes.Count;
            _nodes.Add(new SigmaOperatorNode(
                SigmaOperatorOpcode.FIXED_BOUNDED_REDUCTION,
                first, operands.Count, -1, 0, 0L));
            if (_enableCommonSubexpressions)
                _common.Add(stableKey, nodeIndex);
            return nodeIndex;
        }

        public SigmaOperatorPlan Build(string name, string bracketDescriptor,
            IReadOnlyList<int> outputs)
        {
            if (outputs == null || outputs.Count == 0)
                throw new ArgumentException("An operator needs at least one output.",
                    nameof(outputs));
            return new SigmaOperatorPlan(name, bracketDescriptor, _inputCount,
                _nodes.ToArray(), _reductionOperands.ToArray(), Copy(outputs));
        }

        private int InternCommutative(SigmaOperatorOpcode opcode, int left, int right)
        {
            if (right < left)
                (left, right) = (right, left);
            return Intern(opcode, left, right, -1, 0, 0L);
        }

        private int Intern(SigmaOperatorOpcode opcode, int a, int b, int c,
            int argument, long constant)
        {
            string key = $"{(int)opcode}:{a}:{b}:{c}:{argument}:{constant}";
            if (_enableCommonSubexpressions &&
                _common.TryGetValue(key, out int existing))
                return existing;
            int index = _nodes.Count;
            _nodes.Add(new SigmaOperatorNode(opcode, a, b, c, argument, constant));
            if (_enableCommonSubexpressions)
                _common.Add(key, index);
            return index;
        }

        private static int[] Copy(IReadOnlyList<int> values)
        {
            var output = new int[values.Count];
            for (int index = 0; index < values.Count; ++index)
                output[index] = values[index];
            return output;
        }
    }

    public static class SigmaOperatorEvaluator
    {
        public static long[] Evaluate(SigmaOperatorPlan plan, params SigmaS16[] inputs)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (inputs == null || inputs.Length != plan.InputCount)
                throw new ArgumentException("Operator input count mismatch.", nameof(inputs));
            var values = new long[plan.Nodes.Count];
            for (int index = 0; index < plan.Nodes.Count; ++index)
            {
                SigmaOperatorNode node = plan.Nodes[index];
                values[index] = EvaluateNode(node, values, inputs,
                    plan.ReductionOperands);
            }
            var output = new long[plan.Outputs.Count];
            for (int index = 0; index < output.Length; ++index)
                output[index] = values[plan.Outputs[index]];
            return output;
        }

        public static SigmaS16 EvaluateS16(SigmaOperatorPlan plan,
            params SigmaS16[] inputs)
        {
            if (plan.Outputs.Count != SigmaS16.LaneCount)
                throw new ArgumentException("Plan does not return one S16 value.",
                    nameof(plan));
            return SigmaS16.FromArray(Evaluate(plan, inputs));
        }

        private static long EvaluateNode(SigmaOperatorNode node, long[] values,
            SigmaS16[] inputs, IReadOnlyList<int> reductions)
        {
            switch (node.Opcode)
            {
                case SigmaOperatorOpcode.GATHER:
                case SigmaOperatorOpcode.PERMUTE:
                    return inputs[node.A][node.B];
                case SigmaOperatorOpcode.CONSTANT:
                    return node.Constant;
                case SigmaOperatorOpcode.SIGN:
                    return node.Argument < 0
                        ? SigmaNumericDomain.QNegate(values[node.A]) : values[node.A];
                case SigmaOperatorOpcode.NEGATE:
                    return SigmaNumericDomain.QNegate(values[node.A]);
                case SigmaOperatorOpcode.ADD:
                    return SigmaNumericDomain.QAdd(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.SUB:
                    return SigmaNumericDomain.QSub(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.SHIFT_LEFT:
                    return SigmaNumericDomain.QShiftLeft(values[node.A], node.Argument);
                case SigmaOperatorOpcode.SHIFT_RIGHT:
                    return SigmaNumericDomain.QShiftRight(values[node.A], node.Argument);
                case SigmaOperatorOpcode.CMP_LT:
                    return values[node.A] < values[node.B] ? -1L : 0L;
                case SigmaOperatorOpcode.CMP_LE:
                    return values[node.A] <= values[node.B] ? -1L : 0L;
                case SigmaOperatorOpcode.CMP_EQ:
                    return values[node.A] == values[node.B] ? -1L : 0L;
                case SigmaOperatorOpcode.MIN:
                    return Math.Min(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.MAX:
                    return Math.Max(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.MASK:
                    return values[node.A] != 0L ? -1L : 0L;
                case SigmaOperatorOpcode.SELECT:
                    return values[node.A] != 0L ? values[node.B] : values[node.C];
                case SigmaOperatorOpcode.QMUL:
                    return SigmaNumericDomain.QMul(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.QDIV:
                    return SigmaNumericDomain.QDiv(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.INTERVAL_MUL_LO:
                    return SigmaNumericDomain.QMulLower(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.INTERVAL_MUL_HI:
                    return SigmaNumericDomain.QMulUpper(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.INTERVAL_DIV_LO:
                    return SigmaNumericDomain.QDivLower(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.INTERVAL_DIV_HI:
                    return SigmaNumericDomain.QDivUpper(values[node.A], values[node.B]);
                case SigmaOperatorOpcode.FIXED_BOUNDED_REDUCTION:
                {
                    long sum = 0L;
                    for (int operand = 0; operand < node.B; ++operand)
                        sum = SigmaNumericDomain.QAdd(sum,
                            values[reductions[node.A + operand]]);
                    return sum;
                }
                default:
                    throw new NotSupportedException($"Unsupported exact opcode {node.Opcode}.");
            }
        }
    }

    /// <summary>Canonical generated plans; dense products are explicitly named fallback plans.</summary>
    public static class SigmaOperatorPlans
    {
        private static readonly SigmaOperatorPlan ConjugationValue = BuildConjugation();
        private static readonly SigmaOperatorPlan HadamardValue = BuildHadamard(false);
        private static readonly SigmaOperatorPlan HadamardTransposeValue = BuildHadamard(true);
        private static readonly SigmaOperatorPlan GeometryValue = BuildReadout(true);
        private static readonly SigmaOperatorPlan HiddenValue = BuildReadout(false);
        private static readonly SigmaOperatorPlan TransitionValue = BuildTransition();
        private static readonly SigmaOperatorPlan AssociatorValue = BuildAssociator();
        private static readonly SigmaOperatorPlan ViewValue = BuildView();
        private static readonly SigmaOperatorPlan ProjectiveMeetValue = BuildProjectiveMeet();
        private static readonly SigmaOperatorPlan ProjectiveCommitValue = BuildProjectiveCommit();
        private static readonly SigmaOperatorPlan CodecPredicateValue = BuildCodecPredicates();
        private static readonly string PlanBundleFingerprintValue =
            ComputePlanBundleFingerprint();

        public static SigmaOperatorPlan Conjugation => ConjugationValue;
        public static SigmaOperatorPlan HadamardB => HadamardValue;
        public static SigmaOperatorPlan HadamardBT => HadamardTransposeValue;
        public static SigmaOperatorPlan GeometryG => GeometryValue;
        public static SigmaOperatorPlan HiddenF => HiddenValue;
        public static SigmaOperatorPlan Transition => TransitionValue;
        public static SigmaOperatorPlan Associator => AssociatorValue;
        public static SigmaOperatorPlan View => ViewValue;
        public static SigmaOperatorPlan ProjectiveMeet => ProjectiveMeetValue;
        public static SigmaOperatorPlan ProjectiveCommit => ProjectiveCommitValue;
        public static SigmaOperatorPlan CodecPredicates => CodecPredicateValue;
        public static string PlanBundleFingerprint => PlanBundleFingerprintValue;

        public static SigmaOperatorPlan LeftBasis(int basis) => BuildBasis(basis, true);
        public static SigmaOperatorPlan RightBasis(int basis) => BuildBasis(basis, false);
        public static SigmaOperatorPlan RightSignedDyad(SigmaSignedDyad dyad) =>
            BuildSignedDyad(dyad, false);
        public static SigmaOperatorPlan LeftSignedDyad(SigmaSignedDyad dyad) =>
            BuildSignedDyad(dyad, true);

        private static SigmaOperatorPlan BuildConjugation()
        {
            var builder = new SigmaOperatorPlanBuilder(1);
            var output = new int[SigmaS16.LaneCount];
            for (int lane = 0; lane < output.Length; ++lane)
                output[lane] = builder.Sign(builder.Gather(0, lane),
                    SigmaGeneratedAlgebra.ConjugateSigns[lane]);
            return builder.Build("conjugation", "conjugate(s)", output);
        }

        private static SigmaOperatorPlan BuildBasis(int basis, bool left)
        {
            if ((uint)basis >= SigmaS16.LaneCount)
                throw new ArgumentOutOfRangeException(nameof(basis));
            byte[] sources = left ? SigmaGeneratedAlgebra.LeftBasisSources :
                SigmaGeneratedAlgebra.RightBasisSources;
            sbyte[] signs = left ? SigmaGeneratedAlgebra.LeftBasisSigns :
                SigmaGeneratedAlgebra.RightBasisSigns;
            var builder = new SigmaOperatorPlanBuilder(1);
            var output = new int[SigmaS16.LaneCount];
            int row = basis << 4;
            for (int lane = 0; lane < output.Length; ++lane)
            {
                int value = builder.Gather(0, sources[row + lane], permutation: true);
                output[lane] = builder.Sign(value, signs[row + lane]);
            }
            string side = left ? "left" : "right";
            return builder.Build($"{side}-basis-{basis}",
                left ? $"mul(e{basis},s)" : $"mul(s,e{basis})", output);
        }

        private static SigmaOperatorPlan BuildSignedDyad(SigmaSignedDyad dyad,
            bool left)
        {
            var builder = new SigmaOperatorPlanBuilder(1);
            var output = new int[SigmaS16.LaneCount];
            byte[] sources = left ? SigmaGeneratedAlgebra.LeftBasisSources :
                SigmaGeneratedAlgebra.RightBasisSources;
            sbyte[] signs = left ? SigmaGeneratedAlgebra.LeftBasisSigns :
                SigmaGeneratedAlgebra.RightBasisSigns;
            for (int lane = 0; lane < output.Length; ++lane)
            {
                int firstOffset = (dyad.FirstIndex << 4) + lane;
                int secondOffset = (dyad.SecondIndex << 4) + lane;
                int first = builder.Sign(builder.Gather(0, sources[firstOffset], true),
                    signs[firstOffset] * dyad.FirstSign);
                int second = builder.Sign(builder.Gather(0, sources[secondOffset], true),
                    signs[secondOffset] * dyad.SecondSign);
                output[lane] = builder.Add(first, second);
            }
            return builder.Build(left ? "left-signed-dyad" : "right-signed-dyad",
                left ? "mul(dyad,s)" : "mul(s,dyad)", output);
        }

        private static SigmaOperatorPlan BuildHadamard(bool transpose)
        {
            var builder = new SigmaOperatorPlanBuilder(1);
            var output = new int[SigmaS16.LaneCount];
            for (int row = 0; row < SigmaS16.LaneCount; ++row)
                output[row] = BuildHadamardRow(builder, 0, row, transpose);
            return builder.Build(transpose ? "hadamard-bt" : "hadamard-b",
                transpose ? "B^T(s)" : "B(s)", output);
        }

        private static SigmaOperatorPlan BuildReadout(bool geometry)
        {
            byte[] rows = geometry ? SigmaGeneratedAlgebra.GeometryRows :
                SigmaGeneratedAlgebra.HiddenRows;
            var builder = new SigmaOperatorPlanBuilder(1);
            var output = new int[rows.Length];
            for (int index = 0; index < rows.Length; ++index)
                output[index] = BuildHadamardRow(builder, 0, rows[index], false);
            return builder.Build(geometry ? "geometry-g" : "hidden-f",
                geometry ? "G(s)" : "F(s)", output);
        }

        private static int BuildHadamardRow(SigmaOperatorPlanBuilder builder,
            int input, int row, bool transpose)
        {
            var terms = new int[SigmaS16.LaneCount];
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                int tableOffset = transpose ? (lane << 4) + row : (row << 4) + lane;
                terms[lane] = builder.Sign(builder.Gather(input, lane),
                    SigmaGeneratedAlgebra.HadamardSigns[tableOffset]);
            }
            return builder.FixedReduction(terms);
        }

        private static SigmaOperatorPlan BuildTransition()
        {
            var builder = new SigmaOperatorPlanBuilder(2);
            int[] left = GatherState(builder, 0);
            for (int lane = 1; lane < left.Length; ++lane)
                left[lane] = builder.Negate(left[lane]);
            int[] right = GatherState(builder, 1);
            int[] output = DenseProduct(builder, left, right);
            return builder.Build("transition-dense-fallback",
                "mul(conjugate(lhs),rhs)", output);
        }

        private static SigmaOperatorPlan BuildAssociator()
        {
            var builder = new SigmaOperatorPlanBuilder(3);
            int[] a = GatherState(builder, 0);
            int[] b = GatherState(builder, 1);
            int[] c = GatherState(builder, 2);
            int[] abThenC = DenseProduct(builder, DenseProduct(builder, a, b), c);
            int[] aThenBc = DenseProduct(builder, a, DenseProduct(builder, b, c));
            var output = new int[SigmaS16.LaneCount];
            for (int lane = 0; lane < output.Length; ++lane)
                output[lane] = builder.Sub(abThenC[lane], aThenBc[lane]);
            return builder.Build("associator-fused",
                "sub(mul(mul(a,b),c),mul(a,mul(b,c)))", output);
        }

        private static SigmaOperatorPlan BuildView()
        {
            // Input 1 is the exact sparse quaternionic lift nu(omega): only lanes
            // e1/e2/e3 participate. The generated plan therefore needs 96 qmul,
            // not a pair of generic 16x16 schoolbook products.
            var builder = new SigmaOperatorPlanBuilder(2);
            int[] state = GatherState(builder, 0);
            int[] right = new int[SigmaS16.LaneCount];
            for (int output = 0; output < right.Length; ++output)
            {
                var terms = new int[3];
                for (int basis = 1; basis <= 3; ++basis)
                {
                    int source = basis ^ output;
                    int coefficient = builder.Negate(builder.Gather(1, basis));
                    int term = builder.QMul(state[source], coefficient);
                    terms[basis - 1] = builder.Sign(term,
                        SigmaGeneratedAlgebra.MultiplicationSigns[(source << 4) + basis]);
                }
                right[output] = builder.FixedReduction(terms);
            }
            var result = new int[SigmaS16.LaneCount];
            for (int output = 0; output < result.Length; ++output)
            {
                var terms = new int[3];
                for (int basis = 1; basis <= 3; ++basis)
                {
                    int source = basis ^ output;
                    int term = builder.QMul(builder.Gather(1, basis), right[source]);
                    terms[basis - 1] = builder.Sign(term,
                        SigmaGeneratedAlgebra.MultiplicationSigns[(basis << 4) + source]);
                }
                result[output] = builder.FixedReduction(terms);
            }
            return builder.Build("view-quaternionic-specialized",
                "mul(nu,mul(s,conjugate(nu)))", result);
        }

        private static SigmaOperatorPlan BuildProjectiveMeet()
        {
            var builder = new SigmaOperatorPlanBuilder(4);
            var output = new int[32];
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                output[lane] = builder.Max(builder.Gather(0, lane),
                    builder.Gather(2, lane));
                output[16 + lane] = builder.Min(builder.Gather(1, lane),
                    builder.Gather(3, lane));
            }
            return builder.Build("projective-cell-meet",
                "lower=max(lowerA,lowerB);upper=min(upperA,upperB)", output);
        }

        private static SigmaOperatorPlan BuildProjectiveCommit()
        {
            var builder = new SigmaOperatorPlanBuilder(3);
            var clamped = new int[SigmaS16.LaneCount];
            for (int lane = 0; lane < clamped.Length; ++lane)
            {
                int prior = builder.Gather(0, lane);
                int lower = builder.Gather(1, lane);
                int upper = builder.Gather(2, lane);
                clamped[lane] = builder.Min(builder.Max(prior, lower), upper);
            }
            var output = new int[SigmaS16.LaneCount];
            for (int row = 0; row < SigmaS16.LaneCount; ++row)
            {
                var terms = new int[SigmaS16.LaneCount];
                for (int lane = 0; lane < terms.Length; ++lane)
                {
                    terms[lane] = builder.Sign(clamped[lane],
                        SigmaGeneratedAlgebra.HadamardSigns[(row << 4) + lane]);
                }
                output[row] = builder.ShiftRight(builder.FixedReduction(terms), 4);
            }
            return builder.Build("projective-cell-commit",
                "shift(B^T(clamp(prior,lower,upper)),4)", output);
        }

        private static SigmaOperatorPlan BuildCodecPredicates()
        {
            var builder = new SigmaOperatorPlanBuilder(2);
            var output = new int[SigmaS16.LaneCount];
            for (int lane = 0; lane < output.Length; ++lane)
            {
                output[lane] = builder.Mask(builder.CompareEqual(
                    builder.Gather(0, lane), builder.Gather(1, lane)));
            }
            return builder.Build("codec-exact-equality-predicates",
                "mask(laneA==laneB)", output);
        }

        private static int[] GatherState(SigmaOperatorPlanBuilder builder, int input)
        {
            var output = new int[SigmaS16.LaneCount];
            for (int lane = 0; lane < output.Length; ++lane)
                output[lane] = builder.Gather(input, lane);
            return output;
        }

        private static int[] DenseProduct(SigmaOperatorPlanBuilder builder,
            int[] left, int[] right)
        {
            var buckets = new List<int>[SigmaS16.LaneCount];
            for (int lane = 0; lane < buckets.Length; ++lane)
                buckets[lane] = new List<int>(SigmaS16.LaneCount);
            for (int leftLane = 0; leftLane < SigmaS16.LaneCount; ++leftLane)
            {
                for (int rightLane = 0; rightLane < SigmaS16.LaneCount; ++rightLane)
                {
                    int offset = (leftLane << 4) + rightLane;
                    int product = builder.QMul(left[leftLane], right[rightLane]);
                    product = builder.Sign(product,
                        SigmaGeneratedAlgebra.MultiplicationSigns[offset]);
                    buckets[SigmaGeneratedAlgebra.MultiplicationIndices[offset]]
                        .Add(product);
                }
            }
            var output = new int[SigmaS16.LaneCount];
            for (int lane = 0; lane < output.Length; ++lane)
                output[lane] = builder.FixedReduction(buckets[lane]);
            return output;
        }

        private static string ComputePlanBundleFingerprint()
        {
            string descriptor = string.Join("|", new[]
            {
                SigmaGeneratedAlgebra.BundleFingerprint,
                ConjugationValue.Fingerprint,
                HadamardValue.Fingerprint,
                HadamardTransposeValue.Fingerprint,
                GeometryValue.Fingerprint,
                HiddenValue.Fingerprint,
                TransitionValue.Fingerprint,
                AssociatorValue.Fingerprint,
                ViewValue.Fingerprint,
                ProjectiveMeetValue.Fingerprint,
                ProjectiveCommitValue.Fingerprint,
                CodecPredicateValue.Fingerprint,
            });
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(descriptor));
            var hex = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; ++index)
                hex.Append(digest[index].ToString("x2"));
            return hex.ToString();
        }
    }
}
