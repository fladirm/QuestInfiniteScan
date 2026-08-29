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
    /// Dependency-free GLB 2.0 writer adapted from target history e9f37c1. It emits the
    /// live kernel authority only as an offline indexed POSITION/NORMAL/COLOR_0 readout.
    /// </summary>
    internal static class MerkabaGlbWriter
    {
        private const uint GlbMagic = 0x46546C67u;
        private const uint JsonChunkType = 0x4E4F534Au;
        private const uint BinaryChunkType = 0x004E4942u;
        internal static MerkabaGlbResult Write(Stream destination,
            IReadOnlyList<MerkabaKernelSnapshot> occupiedKernels,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (destination == null || !destination.CanWrite)
                throw new ArgumentException("GLB destination must be writable.", nameof(destination));
            if (occupiedKernels == null) throw new ArgumentNullException(nameof(occupiedKernels));

            var occupied = new HashSet<int3>();
            long geometryWorkTotal = checked((long)occupiedKernels.Count * 2L);
            for (int kernelIndex = 0; kernelIndex < occupiedKernels.Count;
                 kernelIndex++)
            {
                MerkabaKernelSnapshot kernel = occupiedKernels[kernelIndex];
                if (!kernel.State.IsOccupied || !occupied.Add(kernel.Coord))
                    throw new InvalidDataException("GLB input contains duplicate/non-occupied kernels.");
                if ((kernelIndex + 1) % 1024 == 0 ||
                    kernelIndex + 1 == occupiedKernels.Count)
                    Report(progress, ScanOperationStage.BuildingMerkabaGeometry,
                        kernelIndex + 1, geometryWorkTotal,
                        $"Indexed {kernelIndex + 1}/{occupiedKernels.Count} shell kernels");
            }

            long primitiveCount = 0;
            Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity,
                float.NegativeInfinity);
            for (int kernelIndex = 0; kernelIndex < occupiedKernels.Count;
                 kernelIndex++)
            {
                MerkabaKernelSnapshot kernel = occupiedKernels[kernelIndex];
                foreach (int primitiveId in MerkabaCanonicalGeometry.VisiblePrimitives(
                             kernel.Coord, occupied.Contains))
                {
                    primitiveCount = checked(primitiveCount + 1);
                    for (int vertex = 0;
                         vertex < MerkabaCanonicalGeometry.VerticesPerPrimitive;
                         vertex++)
                    {
                        MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId,
                            vertex, out float3 local, out _);
                        Vector3 converted = Convert(
                            MerkabaConstants.WorldCenter(kernel.Coord) + local);
                        minimum = Vector3.Min(minimum, converted);
                        maximum = Vector3.Max(maximum, converted);
                    }
                }
                if ((kernelIndex + 1) % 1024 == 0 ||
                    kernelIndex + 1 == occupiedKernels.Count)
                    Report(progress, ScanOperationStage.BuildingMerkabaGeometry,
                        (long)occupiedKernels.Count + kernelIndex + 1,
                        geometryWorkTotal,
                        $"Measured geometry for {kernelIndex + 1}/" +
                        $"{occupiedKernels.Count} shell kernels");
            }
            int vertexCount = CheckedVertexCountForPrimitiveCount(primitiveCount);
            int indexCount = vertexCount;

            long positionsOffset = 0;
            long positionsLength = checked((long)vertexCount * 12);
            long normalsOffset = positionsOffset + positionsLength;
            long normalsLength = checked((long)vertexCount * 12);
            long colorsOffset = normalsOffset + normalsLength;
            long colorsLength = checked((long)vertexCount * 4);
            long indicesOffset = colorsOffset + colorsLength;
            long indicesLength = checked((long)indexCount * 4);
            long binaryLength = checked(indicesOffset + indicesLength);
            if (binaryLength > uint.MaxValue)
                throw new InvalidDataException(
                    "GLB binary chunk exceeds the 4 GiB container limit.");

            string json = BuildJson(vertexCount, indexCount, binaryLength,
                positionsOffset, positionsLength, normalsOffset, normalsLength,
                colorsOffset, colorsLength, indicesOffset, indicesLength,
                minimum, maximum);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int paddedJsonLength = Align4(jsonBytes.Length);
            long totalLength = checked(12L + 8L + paddedJsonLength + 8L + binaryLength);
            if (totalLength > uint.MaxValue)
                throw new InvalidDataException("GLB exceeds the 4 GiB container limit.");

            long start = destination.CanSeek ? destination.Position : 0;
            using var writer = new BinaryWriter(destination, new UTF8Encoding(false), true);
            writer.Write(GlbMagic);
            writer.Write(2u);
            writer.Write((uint)totalLength);
            writer.Write((uint)paddedJsonLength);
            writer.Write(JsonChunkType);
            writer.Write(jsonBytes);
            for (int i = jsonBytes.Length; i < paddedJsonLength; i++) writer.Write((byte)0x20);
            writer.Write((uint)binaryLength);
            writer.Write(BinaryChunkType);
            long binaryHeaderBytes = 12L + 8L + paddedJsonLength + 8L;
            Report(progress, ScanOperationStage.WritingFile,
                binaryHeaderBytes, totalLength, "Wrote GLB header");

            long passBytes = 0L;
            VisitVertices(occupiedKernels, occupied, (position, _, _) =>
            {
                Vector3 value = Convert(position);
                writer.Write(value.x); writer.Write(value.y); writer.Write(value.z);
                passBytes += 12L;
            }, _ => Report(progress, ScanOperationStage.WritingFile,
                binaryHeaderBytes + passBytes, totalLength,
                "Writing POSITION data"));
            passBytes = 0L;
            VisitVertices(occupiedKernels, occupied, (_, normal, _) =>
            {
                Vector3 value = Convert(normal).normalized;
                writer.Write(value.x); writer.Write(value.y); writer.Write(value.z);
                passBytes += 12L;
            }, _ => Report(progress, ScanOperationStage.WritingFile,
                binaryHeaderBytes + positionsLength + passBytes, totalLength,
                "Writing NORMAL data"));
            passBytes = 0L;
            VisitVertices(occupiedKernels, occupied, (_, _, color) =>
            {
                writer.Write(color.r); writer.Write(color.g); writer.Write(color.b);
                writer.Write((byte)255);
                passBytes += 4L;
            }, _ => Report(progress, ScanOperationStage.WritingFile,
                binaryHeaderBytes + positionsLength + normalsLength + passBytes,
                totalLength, "Writing COLOR_0 data"));
            for (uint triangle = 0; triangle < (uint)indexCount; triangle += 3)
            {
                // Mirroring X changes handedness; reverse every source triangle.
                writer.Write(triangle);
                writer.Write(triangle + 2);
                writer.Write(triangle + 1);
                if ((triangle & 0x3ffffu) == 0u)
                    Report(progress, ScanOperationStage.WritingFile,
                        binaryHeaderBytes + indicesOffset +
                        (long)(triangle + 3u) * 4L, totalLength,
                        "Writing triangle indices");
            }
            writer.Flush();
            Report(progress, ScanOperationStage.WritingFile, totalLength,
                totalLength, "GLB bytes written");
            long written = destination.CanSeek ? destination.Position - start : totalLength;
            if (written != totalLength)
                throw new InvalidDataException($"GLB length mismatch: {written} != {totalLength}.");
            return new MerkabaGlbResult(written, vertexCount, indexCount,
                checked((int)primitiveCount));
        }

        internal static int CheckedVertexCountForPrimitiveCount(long primitiveCount)
        {
            if (primitiveCount <= 0)
                throw new InvalidDataException("GLB boundary is empty.");
            try
            {
                long vertices = checked(primitiveCount *
                    MerkabaCanonicalGeometry.VerticesPerPrimitive);
                long binaryBytes = checked(vertices * 32L);
                if (binaryBytes > uint.MaxValue || vertices > int.MaxValue)
                    throw new InvalidDataException(
                        "GLB geometry exceeds the 4 GiB container limit.");
                return (int)vertices;
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException(
                    "GLB geometry exceeds the 4 GiB container limit.", exception);
            }
        }

        private static void VisitVertices(
            IReadOnlyList<MerkabaKernelSnapshot> occupiedKernels,
            HashSet<int3> occupied, Action<Vector3, Vector3, Color32> visitor,
            Action<int> kernelCompleted)
        {
            for (int kernelIndex = 0; kernelIndex < occupiedKernels.Count;
                 kernelIndex++)
            {
                MerkabaKernelSnapshot kernel = occupiedKernels[kernelIndex];
                foreach (int primitiveId in
                         MerkabaCanonicalGeometry.VisiblePrimitives(
                             kernel.Coord, occupied.Contains))
                for (int vertex = 0;
                     vertex < MerkabaCanonicalGeometry.VerticesPerPrimitive;
                     vertex++)
                {
                    MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId,
                        vertex, out float3 local, out float3 normal);
                    float3 position = MerkabaConstants.WorldCenter(kernel.Coord) +
                                      local;
                    visitor(position, normal, kernel.State.Color);
                }
                if (kernelIndex + 1 == occupiedKernels.Count ||
                    (kernelIndex + 1) % 1024 == 0)
                    kernelCompleted?.Invoke(kernelIndex + 1);
            }
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
            json.Append("\"meshes\":[{\"name\":\"Merkaba Boundary\",\"primitives\":[{");
            json.Append("\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"COLOR_0\":2},");
            json.Append("\"indices\":3,\"material\":0,\"mode\":4}]}],");
            json.Append("\"materials\":[{\"name\":\"Merkaba Matte\",");
            json.Append("\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1],");
            json.Append("\"metallicFactor\":0,\"roughnessFactor\":0.85},\"doubleSided\":false}],");
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
