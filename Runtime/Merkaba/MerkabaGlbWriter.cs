using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal readonly struct MerkabaGlbResult
    {
        public readonly long ByteLength;
        public readonly int VertexCount;
        public readonly int IndexCount;
        public readonly int PrimitiveCount;

        public MerkabaGlbResult(long byteLength, int vertexCount, int indexCount,
            int primitiveCount)
        {
            ByteLength = byteLength;
            VertexCount = vertexCount;
            IndexCount = indexCount;
            PrimitiveCount = primitiveCount;
        }
    }

    /// <summary>
    /// Multi-pass streaming GLB 2.0 writer for the read-only export membrane. No
    /// vertex/index collection proportional to export size is retained in RAM.
    /// </summary>
    internal static class MerkabaGlbWriter
    {
        private const uint GlbMagic = 0x46546C67u;
        private const uint JsonChunkType = 0x4E4F534Au;
        private const uint BinaryChunkType = 0x004E4942u;

        private readonly struct GeometryPlan
        {
            internal readonly HashSet<int3> Occupied;
            internal readonly int LegacyPrimitiveCount;
            internal readonly int PrimitiveCount;
            internal readonly int VertexCount;
            internal readonly int IndexCount;
            internal readonly Vector3 Minimum;
            internal readonly Vector3 Maximum;

            internal GeometryPlan(HashSet<int3> occupied,
                int legacyPrimitiveCount, int primitiveCount, int vertexCount,
                int indexCount, Vector3 minimum, Vector3 maximum)
            {
                Occupied = occupied;
                LegacyPrimitiveCount = legacyPrimitiveCount;
                PrimitiveCount = primitiveCount;
                VertexCount = vertexCount;
                IndexCount = indexCount;
                Minimum = minimum;
                Maximum = maximum;
            }
        }

        internal static MerkabaGlbResult Write(Stream destination,
            MerkabaExportMembraneResult membrane,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (destination == null || !destination.CanWrite)
                throw new ArgumentException("GLB destination must be writable.",
                    nameof(destination));
            if (membrane == null) throw new ArgumentNullException(nameof(membrane));
            if (membrane.Patches.Count == 0 && membrane.LegacyKernels.Count == 0)
                throw new InvalidDataException("GLB membrane is empty.");

            GeometryPlan plan = Plan(membrane, progress);
            long positionsOffset = 0;
            long positionsLength = checked((long)plan.VertexCount * 12);
            long normalsOffset = positionsOffset + positionsLength;
            long normalsLength = checked((long)plan.VertexCount * 12);
            long colorsOffset = normalsOffset + normalsLength;
            long colorsLength = checked((long)plan.VertexCount * 4);
            long indicesOffset = colorsOffset + colorsLength;
            long indicesLength = checked((long)plan.IndexCount * 4);
            long binaryLength = checked(indicesOffset + indicesLength);
            if (binaryLength > uint.MaxValue)
                throw new InvalidDataException(
                    "GLB binary chunk exceeds the 4 GiB container limit.");

            string json = BuildJson(plan.VertexCount, plan.IndexCount,
                binaryLength, positionsOffset, positionsLength, normalsOffset,
                normalsLength, colorsOffset, colorsLength, indicesOffset,
                indicesLength, plan.Minimum, plan.Maximum);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int paddedJsonLength = Align4(jsonBytes.Length);
            long totalLength = checked(12L + 8L + paddedJsonLength + 8L +
                binaryLength);
            if (totalLength > uint.MaxValue)
                throw new InvalidDataException("GLB exceeds the 4 GiB container limit.");

            long start = destination.CanSeek ? destination.Position : 0;
            using var writer = new BinaryWriter(destination,
                new UTF8Encoding(false), true);
            writer.Write(GlbMagic);
            writer.Write(2u);
            writer.Write((uint)totalLength);
            writer.Write((uint)paddedJsonLength);
            writer.Write(JsonChunkType);
            writer.Write(jsonBytes);
            for (int index = jsonBytes.Length; index < paddedJsonLength; index++)
                writer.Write((byte)0x20);
            writer.Write((uint)binaryLength);
            writer.Write(BinaryChunkType);
            long binaryHeaderBytes = 12L + 8L + paddedJsonLength + 8L;
            Report(progress, ScanOperationStage.WritingFile,
                binaryHeaderBytes, totalLength, "Wrote GLB header");

            long passBytes = 0;
            VisitVertices(membrane, plan.Occupied, (position, _, _) =>
            {
                Vector3 value = Convert(position);
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
                passBytes += 12;
            }, completed => ReportVertexPass(progress, binaryHeaderBytes +
                passBytes, totalLength, completed, plan.VertexCount,
                "Writing POSITION data"));

            passBytes = 0;
            VisitVertices(membrane, plan.Occupied, (_, normal, _) =>
            {
                Vector3 value = Convert(normal).normalized;
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
                passBytes += 12;
            }, completed => ReportVertexPass(progress, binaryHeaderBytes +
                normalsOffset + passBytes, totalLength, completed,
                plan.VertexCount, "Writing NORMAL data"));

            passBytes = 0;
            VisitVertices(membrane, plan.Occupied, (_, _, packedColor) =>
            {
                Color32 color = KernelState.UnpackColor(packedColor);
                writer.Write(color.r);
                writer.Write(color.g);
                writer.Write(color.b);
                writer.Write((byte)255);
                passBytes += 4;
            }, completed => ReportVertexPass(progress, binaryHeaderBytes +
                colorsOffset + passBytes, totalLength, completed,
                plan.VertexCount, "Writing COLOR_0 data"));

            WriteIndices(writer, membrane, plan, progress,
                binaryHeaderBytes + indicesOffset, totalLength);
            writer.Flush();
            Report(progress, ScanOperationStage.WritingFile, totalLength,
                totalLength, "GLB bytes written");
            long written = destination.CanSeek ? destination.Position - start :
                totalLength;
            if (written != totalLength)
                throw new InvalidDataException(
                    $"GLB length mismatch: {written} != {totalLength}.");
            return new MerkabaGlbResult(written, plan.VertexCount,
                plan.IndexCount, plan.PrimitiveCount);
        }

        internal static int CheckedIndexCountForPrimitiveCount(long primitiveCount)
        {
            if (primitiveCount <= 0)
                throw new InvalidDataException("GLB membrane is empty.");
            try
            {
                long indices = checked(primitiveCount * 3L);
                long worstCaseBinaryBytes = checked(indices * 32L);
                if (worstCaseBinaryBytes > uint.MaxValue || indices > int.MaxValue)
                    throw new InvalidDataException(
                        "GLB geometry exceeds the 4 GiB container limit.");
                return (int)indices;
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException(
                    "GLB geometry exceeds the 4 GiB container limit.", exception);
            }
        }

        private static GeometryPlan Plan(MerkabaExportMembraneResult membrane,
            IProgress<OperationWorkProgress> progress)
        {
            var occupied = new HashSet<int3>(
                membrane.CanonicalOccupiedCoordinates);
            int legacyPrimitiveCount = 0;
            foreach (MerkabaKernelSnapshot kernel in membrane.LegacyKernels)
                foreach (int _ in MerkabaCanonicalGeometry.VisiblePrimitives(
                             kernel.Coord, occupied.Contains))
                    legacyPrimitiveCount = checked(legacyPrimitiveCount + 1);
            int primitiveCount = checked(membrane.Patches.Count * 2 +
                legacyPrimitiveCount);
            int indexCount = CheckedIndexCountForPrimitiveCount(primitiveCount);
            int vertexCount = checked(membrane.Patches.Count * 4 +
                legacyPrimitiveCount * 3);

            Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity,
                float.NegativeInfinity);
            int visited = 0;
            VisitVertices(membrane, occupied, (position, _, _) =>
            {
                Vector3 value = Convert(position);
                minimum = Vector3.Min(minimum, value);
                maximum = Vector3.Max(maximum, value);
            }, completed =>
            {
                visited = completed;
                if (completed == vertexCount || (completed & 0x3ffff) == 0)
                    Report(progress, ScanOperationStage.BuildingMerkabaGeometry,
                        completed, vertexCount,
                        $"Planned {completed}/{vertexCount} export vertices");
            });
            if (visited != vertexCount)
                throw new InvalidDataException(
                    $"GLB vertex planning mismatch: {visited} != {vertexCount}.");
            return new GeometryPlan(occupied, legacyPrimitiveCount,
                primitiveCount, vertexCount, indexCount, minimum, maximum);
        }

        private static void VisitVertices(MerkabaExportMembraneResult membrane,
            HashSet<int3> occupied, Action<float3, float3, uint> visitor,
            Action<int> progress)
        {
            int completed = 0;
            foreach (MerkabaExportMembranePatch patch in membrane.Patches)
            for (int corner = 0; corner < 4; corner++)
            {
                visitor(patch.Corner(corner), patch.Normal, patch.PackedColor);
                progress?.Invoke(++completed);
            }
            foreach (MerkabaKernelSnapshot kernel in membrane.LegacyKernels)
            foreach (int primitiveId in MerkabaCanonicalGeometry.VisiblePrimitives(
                         kernel.Coord, occupied.Contains))
            for (int corner = 0;
                 corner < MerkabaCanonicalGeometry.VerticesPerPrimitive; corner++)
            {
                MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId, corner,
                    out float3 local, out float3 normal);
                visitor(MerkabaConstants.WorldCenter(kernel.Coord) + local,
                    normal, kernel.State.PackedColor);
                progress?.Invoke(++completed);
            }
        }

        private static void WriteIndices(BinaryWriter writer,
            MerkabaExportMembraneResult membrane, GeometryPlan plan,
            IProgress<OperationWorkProgress> progress, long binaryOffset,
            long totalLength)
        {
            uint vertex = 0;
            int completed = 0;
            foreach (MerkabaExportMembranePatch _ in membrane.Patches)
            {
                WriteTriangle(writer, vertex, vertex + 2, vertex + 1);
                WriteTriangle(writer, vertex, vertex + 3, vertex + 2);
                vertex += 4;
                completed += 6;
                ReportIndexPass(progress, binaryOffset, totalLength, completed,
                    plan.IndexCount);
            }
            foreach (MerkabaKernelSnapshot kernel in membrane.LegacyKernels)
            foreach (int _ in MerkabaCanonicalGeometry.VisiblePrimitives(
                         kernel.Coord, plan.Occupied.Contains))
            {
                WriteTriangle(writer, vertex, vertex + 2, vertex + 1);
                vertex += 3;
                completed += 3;
                ReportIndexPass(progress, binaryOffset, totalLength, completed,
                    plan.IndexCount);
            }
            if (completed != plan.IndexCount || vertex != plan.VertexCount)
                throw new InvalidDataException("GLB index streaming count mismatch.");
        }

        private static void WriteTriangle(BinaryWriter writer, uint a, uint b,
            uint c)
        {
            writer.Write(a);
            writer.Write(b);
            writer.Write(c);
        }

        private static void ReportVertexPass(
            IProgress<OperationWorkProgress> progress, long completedBytes,
            long totalBytes, int completed, int total, string text)
        {
            if (completed != total && (completed & 0x3ffff) != 0) return;
            Report(progress, ScanOperationStage.WritingFile, completedBytes,
                totalBytes, text);
        }

        private static void ReportIndexPass(
            IProgress<OperationWorkProgress> progress, long binaryOffset,
            long totalLength, int completed, int total)
        {
            if (completed != total && (completed & 0x3ffff) != 0) return;
            Report(progress, ScanOperationStage.WritingFile,
                binaryOffset + (long)completed * 4, totalLength,
                "Writing triangle indices");
        }

        private static void Report(IProgress<OperationWorkProgress> progress,
            ScanOperationStage stage, long completed, long total, string text) =>
            progress?.Report(new OperationWorkProgress(stage, completed, total,
                text));

        private static Vector3 Convert(Vector3 unity) =>
            new(-unity.x, unity.y, unity.z);
        private static Vector3 Convert(float3 unity) => Convert((Vector3)unity);

        private static string BuildJson(int vertexCount, int indexCount,
            long binaryLength, long positionsOffset, long positionsLength,
            long normalsOffset, long normalsLength, long colorsOffset,
            long colorsLength, long indicesOffset, long indicesLength,
            Vector3 minimum, Vector3 maximum)
        {
            string min = $"[{Number(minimum.x)},{Number(minimum.y)},{Number(minimum.z)}]";
            string max = $"[{Number(maximum.x)},{Number(maximum.y)},{Number(maximum.z)}]";
            var json = new StringBuilder(1400);
            json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"Quest Infinite Merkaba\"},");
            json.Append("\"scene\":0,\"scenes\":[{\"nodes\":[0]}],");
            json.Append("\"nodes\":[{\"name\":\"MerkabaGrid\",\"mesh\":0}],");
            json.Append("\"meshes\":[{\"name\":\"M8 Measured Membrane\",\"primitives\":[{");
            json.Append("\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"COLOR_0\":2},");
            json.Append("\"indices\":3,\"material\":0,\"mode\":4}]}],");
            json.Append("\"materials\":[{\"name\":\"M8 Membrane Matte\",");
            json.Append("\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1],");
            json.Append("\"metallicFactor\":0,\"roughnessFactor\":0.85},\"doubleSided\":true}],");
            json.Append("\"buffers\":[{\"byteLength\":").Append(binaryLength).Append("}],");
            json.Append("\"bufferViews\":[");
            BufferView(json, positionsOffset, positionsLength, 34962); json.Append(',');
            BufferView(json, normalsOffset, normalsLength, 34962); json.Append(',');
            BufferView(json, colorsOffset, colorsLength, 34962); json.Append(',');
            BufferView(json, indicesOffset, indicesLength, 34963); json.Append("],");
            json.Append("\"accessors\":[");
            json.Append("{\"bufferView\":0,\"componentType\":5126,\"count\":")
                .Append(vertexCount).Append(",\"type\":\"VEC3\",\"min\":")
                .Append(min).Append(",\"max\":").Append(max).Append("},");
            json.Append("{\"bufferView\":1,\"componentType\":5126,\"count\":")
                .Append(vertexCount).Append(",\"type\":\"VEC3\"},");
            json.Append("{\"bufferView\":2,\"componentType\":5121,\"normalized\":true,\"count\":")
                .Append(vertexCount).Append(",\"type\":\"VEC4\"},");
            json.Append("{\"bufferView\":3,\"componentType\":5125,\"count\":")
                .Append(indexCount).Append(",\"type\":\"SCALAR\"}]}");
            return json.ToString();
        }

        private static void BufferView(StringBuilder json, long offset,
            long length, int target)
        {
            json.Append("{\"buffer\":0,\"byteOffset\":").Append(offset)
                .Append(",\"byteLength\":").Append(length)
                .Append(",\"target\":").Append(target).Append('}');
        }

        private static string Number(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);
        private static int Align4(int value) => (value + 3) & ~3;
    }
}
