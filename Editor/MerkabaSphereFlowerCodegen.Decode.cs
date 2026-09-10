using System;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace Genesis.RoomScan.Editor
{
    public static partial class MerkabaSphereFlowerCodegen
    {
        private static void AppendR1WitnessDecode(StringBuilder output,
            bool csharp, GpuTables tables = null)
        {
            Emit("R1WitnessLoops", MerkabaSphereFlowerAuthority.R1WitnessLoops);
            Emit("R1WitnessDirections", MerkabaSphereFlowerAuthority.R1WitnessDirections);
            Emit("R1WitnessPeers", MerkabaSphereFlowerAuthority.R1WitnessPeers);
            Emit("DecodePetalCarriers", MerkabaSphereFlowerAuthority.DecodePetalCarriers);
            Emit("DecodeCarrierPetals", MerkabaSphereFlowerAuthority.DecodeCarrierPetals);
            if (csharp)
            {
                Emit("CarrierTripleMasks", MerkabaSphereFlowerAuthority.CarrierTripleMasks);
                output.AppendLine(@"
        private static uint2 M8FlowerDecodeNodePetals(int node) =>
            new uint2((uint)NodeIncidentPetalsValue[node], (uint)(NodeIncidentPetalsValue[node] >> 32));
        private static uint4 M8FlowerDecodePetalCarriersAt(uint petal) => DecodePetalCarriers[(int)petal];");
            }
            else output.AppendLine(@"
uint2 M8FlowerDecodeNodePetals(int node) { return M8FlowerNodeIncidentPetalsAt((uint)node); }");

            // One integer selection recipe for the production CPU and HLSL
            // backends. These are incidence masks, NEVER RootOwner masks.
            string reach = @"
uint4 M8FlowerReachedCarriers(uint absentOriginalNodes)
{
    uint2 excluded = 0u;
    [loop] while (absentOriginalNodes != 0u)
    {
        int node = (int)firstbitlow(absentOriginalNodes);
        absentOriginalNodes &= absentOriginalNodes - 1u;
        excluded |= M8FlowerDecodeNodePetals(node);
    }
    uint2 petals = ~excluded;
    petals.y &= 65535u;
    uint4 carriers = 0u;
    [loop] for (int word = 0; word < 2; word++)
    {
        uint pending = petals[word];
        [loop] while (pending != 0u)
        {
            uint petal = (uint)(32 * word + (int)firstbitlow(pending));
            pending &= pending - 1u;
            carriers |= M8FlowerDecodePetalCarriersAt(petal);
        }
    }
    return carriers;
}";
            output.AppendLine(csharp ? reach.Replace("uint4 M8FlowerReachedCarriers(",
                    "internal static uint4 M8FlowerReachedCarriers(")
                .Replace("[loop] ", string.Empty).Replace("firstbitlow(", "tzcnt(") : reach);

            void Emit(string name, ReadOnlySpan<uint4> rows)
            {
                if (!csharp)
                {
                    tables.AppendVectors(output, "M8Flower" + name, "uint4", rows);
                    return;
                }
                output.Append("        private static uint4[] LoadGenerated").Append(name)
                    .AppendLine("() => new uint4[] {");
                foreach (uint4 row in rows)
                {
                    output.Append("            new uint4(");
                    for (int word = 0; word < 4; word++)
                    {
                        if (word != 0) output.Append(',');
                        output.Append("0x").Append(row[word].ToString("x8", CultureInfo.InvariantCulture)).Append('u');
                    }
                    output.AppendLine("),");
                }
                output.AppendLine("        };");
            }
        }
    }
}
