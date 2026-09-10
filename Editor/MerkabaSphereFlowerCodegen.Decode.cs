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
