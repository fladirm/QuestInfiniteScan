using System;
using System.Collections.Generic;
using System.Text;

namespace Genesis.RoomScan.SigmaPrism
{
    [Flags]
    public enum SigmaExactPrimitive : uint
    {
        None = 0,
        GatherScatter = 1u << 0,
        PermuteSign = 1u << 1,
        AddSubtract = 1u << 2,
        Shift = 1u << 3,
        CompareMinMax = 1u << 4,
        MaskSelect = 1u << 5,
        FixedReduction = 1u << 6,
        Multiply = 1u << 7,
        Divide = 1u << 8,
        IntervalMultiply = 1u << 9,
        IntervalDivide = 1u << 10,
        All = 0x7ffu,
    }

    /// <summary>
    /// Backend legality is explicit and non-authoritative. An unproven execution
    /// path can render diagnostics but cannot mutate canonical state.
    /// </summary>
    public sealed class SigmaBackendCapabilityProfile
    {
        public SigmaBackendCapabilityProfile(string id,
            SigmaExactPrimitive exactPrimitives, bool canonicalMutationAllowed)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ExactPrimitives = exactPrimitives;
            CanonicalMutationAllowed = canonicalMutationAllowed;
        }

        public string Id { get; }
        public SigmaExactPrimitive ExactPrimitives { get; }
        public bool CanonicalMutationAllowed { get; }

        public static SigmaBackendCapabilityProfile Packed32Proven { get; } = new(
            "quest.vulkan.packed32.q48.exact.v1", SigmaExactPrimitive.All, true);

        public static SigmaBackendCapabilityProfile NativeI64Unproven { get; } = new(
            "quest.vulkan.native_i64.unproven", SigmaExactPrimitive.None, false);

        public bool Supports(SigmaOperatorOpcode opcode) =>
            (ExactPrimitives & RequiredPrimitive(opcode)) == RequiredPrimitive(opcode);

        public void RequireCanonical(SigmaOperatorPlan plan)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (!CanonicalMutationAllowed)
                throw new InvalidOperationException(
                    $"Backend '{Id}' is not proven for canonical mutation.");
            for (int index = 0; index < plan.Nodes.Count; ++index)
            {
                if (!Supports(plan.Nodes[index].Opcode))
                    throw new InvalidOperationException(
                        $"Backend '{Id}' has no exact lowering for " +
                        $"{plan.Nodes[index].Opcode} in '{plan.Name}'.");
            }
        }

        private static SigmaExactPrimitive RequiredPrimitive(SigmaOperatorOpcode opcode)
        {
            return opcode switch
            {
                SigmaOperatorOpcode.GATHER or SigmaOperatorOpcode.SCATTER =>
                    SigmaExactPrimitive.GatherScatter,
                SigmaOperatorOpcode.XOR_INDEX or SigmaOperatorOpcode.PERMUTE or
                    SigmaOperatorOpcode.SIGN or SigmaOperatorOpcode.NEGATE =>
                    SigmaExactPrimitive.PermuteSign,
                SigmaOperatorOpcode.ADD or SigmaOperatorOpcode.SUB or
                    SigmaOperatorOpcode.CONSTANT => SigmaExactPrimitive.AddSubtract,
                SigmaOperatorOpcode.SHIFT_LEFT or SigmaOperatorOpcode.SHIFT_RIGHT =>
                    SigmaExactPrimitive.Shift,
                SigmaOperatorOpcode.CMP_LT or SigmaOperatorOpcode.CMP_LE or
                    SigmaOperatorOpcode.CMP_EQ or SigmaOperatorOpcode.MIN or
                    SigmaOperatorOpcode.MAX => SigmaExactPrimitive.CompareMinMax,
                SigmaOperatorOpcode.MASK or SigmaOperatorOpcode.SELECT =>
                    SigmaExactPrimitive.MaskSelect,
                SigmaOperatorOpcode.FIXED_BOUNDED_REDUCTION =>
                    SigmaExactPrimitive.FixedReduction,
                SigmaOperatorOpcode.QMUL => SigmaExactPrimitive.Multiply,
                SigmaOperatorOpcode.QDIV => SigmaExactPrimitive.Divide,
                SigmaOperatorOpcode.INTERVAL_MUL_LO or
                    SigmaOperatorOpcode.INTERVAL_MUL_HI =>
                    SigmaExactPrimitive.IntervalMultiply,
                SigmaOperatorOpcode.INTERVAL_DIV_LO or
                    SigmaOperatorOpcode.INTERVAL_DIV_HI =>
                    SigmaExactPrimitive.IntervalDivide,
                _ => SigmaExactPrimitive.None,
            };
        }
    }

    /// <summary>Deterministic source lowering of the same operator DAG to packed-Q48 HLSL.</summary>
    public static class SigmaHlslLowerer
    {
        public static string Lower(SigmaOperatorPlan plan,
            SigmaBackendCapabilityProfile backend)
        {
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));
            backend.RequireCanonical(plan);
            var source = new StringBuilder();
            source.Append("// exact-plan ").Append(plan.Name).Append(' ')
                .Append(plan.Fingerprint).Append('\n');
            source.Append("// bracket ").Append(plan.BracketDescriptor).Append('\n');
            source.Append("void SigmaPlan(uint2 inputs[")
                .Append(plan.InputCount * SigmaS16.LaneCount)
                .Append("], out uint2 outputs[").Append(plan.Outputs.Count)
                .Append("], inout uint valid)\n{\n");
            for (int index = 0; index < plan.Nodes.Count; ++index)
            {
                SigmaOperatorNode node = plan.Nodes[index];
                source.Append("    uint2 v").Append(index).Append(" = ")
                    .Append(LowerNode(node, plan.ReductionOperands)).Append(";\n");
            }
            for (int index = 0; index < plan.Outputs.Count; ++index)
            {
                source.Append("    outputs[").Append(index).Append("] = v")
                    .Append(plan.Outputs[index]).Append(";\n");
            }
            source.Append("}\n");
            return source.ToString();
        }

        private static string LowerNode(SigmaOperatorNode node,
            IReadOnlyList<int> reductions)
        {
            string A() => $"v{node.A}";
            string B() => $"v{node.B}";
            return node.Opcode switch
            {
                SigmaOperatorOpcode.GATHER or SigmaOperatorOpcode.PERMUTE =>
                    $"inputs[{node.A * SigmaS16.LaneCount + node.B}]",
                SigmaOperatorOpcode.CONSTANT =>
                    $"uint2(0x{unchecked((uint)node.Constant):x8}u," +
                    $"0x{unchecked((uint)(node.Constant >> 32)):x8}u)",
                SigmaOperatorOpcode.SIGN => node.Argument < 0
                    ? $"SigmaQ48NegateChecked({A()},valid)" : A(),
                SigmaOperatorOpcode.NEGATE => $"SigmaQ48NegateChecked({A()},valid)",
                SigmaOperatorOpcode.ADD => $"SigmaQ48AddChecked({A()},{B()},valid)",
                SigmaOperatorOpcode.SUB => $"SigmaQ48SubChecked({A()},{B()},valid)",
                SigmaOperatorOpcode.SHIFT_LEFT =>
                    $"SigmaQ48ShiftLeftChecked({A()},{node.Argument}u,valid)",
                SigmaOperatorOpcode.SHIFT_RIGHT =>
                    $"SigmaQ48ShiftRightNearestEven({A()},{node.Argument}u,valid)",
                SigmaOperatorOpcode.CMP_LT => $"SigmaQ48Mask(SigmaQ48Less({A()},{B()}))",
                SigmaOperatorOpcode.CMP_LE => $"SigmaQ48Mask(!SigmaQ48Less({B()},{A()}))",
                SigmaOperatorOpcode.CMP_EQ => $"SigmaQ48Mask(all({A()} == {B()}))",
                SigmaOperatorOpcode.MIN => $"SigmaQ48Min({A()},{B()})",
                SigmaOperatorOpcode.MAX => $"SigmaQ48Max({A()},{B()})",
                SigmaOperatorOpcode.MASK => $"SigmaQ48Mask(any({A()} != 0u))",
                SigmaOperatorOpcode.SELECT =>
                    $"SigmaQ48Select(any({A()} != 0u),v{node.B},v{node.C})",
                SigmaOperatorOpcode.QMUL => $"SigmaQ48MulNearestEven({A()},{B()},valid)",
                SigmaOperatorOpcode.QDIV => $"SigmaQ48DivNearestEven({A()},{B()},valid)",
                SigmaOperatorOpcode.INTERVAL_MUL_LO =>
                    $"SigmaQ48MulLower({A()},{B()},valid)",
                SigmaOperatorOpcode.INTERVAL_MUL_HI =>
                    $"SigmaQ48MulUpper({A()},{B()},valid)",
                SigmaOperatorOpcode.INTERVAL_DIV_LO =>
                    $"SigmaQ48DivLower({A()},{B()},valid)",
                SigmaOperatorOpcode.INTERVAL_DIV_HI =>
                    $"SigmaQ48DivUpper({A()},{B()},valid)",
                SigmaOperatorOpcode.FIXED_BOUNDED_REDUCTION =>
                    LowerReduction(node, reductions),
                _ => throw new NotSupportedException(
                    $"No HLSL lowering for {node.Opcode}."),
            };
        }

        private static string LowerReduction(SigmaOperatorNode node,
            IReadOnlyList<int> reductions)
        {
            string expression = "uint2(0u,0u)";
            for (int offset = 0; offset < node.B; ++offset)
                expression = $"SigmaQ48AddChecked({expression},v{reductions[node.A + offset]},valid)";
            return expression;
        }
    }
}
