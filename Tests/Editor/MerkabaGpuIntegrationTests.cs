using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGpuIntegrationTests
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct RenderRecord
        {
            public int X, Y, Z;
            public uint ActiveMask;
            public uint PackedColor;
            public uint Padding0, Padding1, Padding2;
        }

        [Test, Timeout(30000)]
        public void ProductionGpuDepthPath_EatsFalseForegroundAndPreservesTrueWall()
        {
            ComputeShader compute = LoadCompute("MerkabaIntegration.compute");
            int kernel = compute.FindKernel("IntegrateMerkaba");
            const int pageCount = 2;
            int stateCount = pageCount * MerkabaConstants.KernelsPerChunk;
            var states = new KernelState[stateCount];
            var pageCoords = new[] { new int4(0, 0, -2, 0), new int4(0, 0, -3, 1) };
            int[] neighbours = PageNeighbours(pageCoords);
            var activeSlots = new[] { 0, 1 };
            var dirty = new uint[stateCount];

            using var stateBuffer = new ComputeBuffer(stateCount, 16);
            using var pageBuffer = new ComputeBuffer(pageCount, 16);
            using var neighbourBuffer = new ComputeBuffer(pageCount * 27, sizeof(int));
            using var activeBuffer = new ComputeBuffer(pageCount, sizeof(int));
            using var dirtyBuffer = new ComputeBuffer(stateCount, sizeof(uint));
            stateBuffer.SetData(states);
            pageBuffer.SetData(pageCoords);
            neighbourBuffer.SetData(neighbours);
            activeBuffer.SetData(activeSlots);
            dirtyBuffer.SetData(dirty);

            Matrix4x4 projection = Matrix4x4.Perspective(90f, 1f, 0.1f, 10f);
            Matrix4x4[] projections = { projection, projection };
            Matrix4x4[] projectionInv = { projection.inverse, projection.inverse };
            Matrix4x4[] views = { Matrix4x4.identity, Matrix4x4.identity };
            Texture2DArray depth = MakeDepth(DepthNdc(projection, 1f));
            Texture2DArray normals = MakeNormals();
            Texture2DArray dilation = MakeDilation(DepthNdc(projection, 1f));
            Texture2D camera = MakeCamera();

            try
            {
                compute.SetBuffer(kernel, "_MerkabaKernels", stateBuffer);
                compute.SetBuffer(kernel, "_MerkabaPageCoords", pageBuffer);
                compute.SetBuffer(kernel, "_MerkabaPageNeighbours", neighbourBuffer);
                compute.SetBuffer(kernel, "_MerkabaIntegrationSlots", activeBuffer);
                compute.SetBuffer(kernel, "_MerkabaKernelDirty", dirtyBuffer);
                compute.SetInt("_MerkabaIntegrationChunkCount", pageCount);
                compute.SetMatrix("_MerkabaGridToWorld", Matrix4x4.identity);
                compute.SetFloat("_MerkabaMaxUpdateDistance", 5f);
                compute.SetInt("_MerkabaExclusionCount", 0);
                compute.SetInt("_MerkabaCameraAvailable", 0);
                compute.SetTexture(kernel, "_MerkabaCameraRgb", camera);
                compute.SetTexture(kernel, DepthCapture.DepthTexID, depth);
                compute.SetTexture(kernel, DepthCapture.NormTexID, normals);
                compute.SetTexture(kernel, DepthCapture.DilatedDepthTexID, dilation);
                compute.SetMatrixArray(DepthCapture.ProjID, projections);
                compute.SetMatrixArray(DepthCapture.ProjInvID, projectionInv);
                compute.SetMatrixArray(DepthCapture.ViewID, views);
                compute.SetMatrixArray(DepthCapture.ViewInvID, views);
                compute.SetVector(DepthCapture.ZParamsID, new Vector4(0.1f, 10f, 0f, 0f));
                compute.SetVector(DepthCapture.TexSizeID, new Vector4(16, 16, 0, 0));

                int groups = stateCount / 64;
                compute.Dispatch(kernel, groups, 1, 1);
                compute.Dispatch(kernel, groups, 1, 1);
                stateBuffer.GetData(states);
                int foregroundIndex = MerkabaConstants.Flatten(new int3(0, 0, 24)); // global z=-40
                Assert.That(states[foregroundIndex].IsOccupied, Is.True,
                    "the synthetic one-metre false surface should cross ON");

                SetDepth(depth, DepthNdc(projection, 2f));
                SetDilation(dilation, DepthNdc(projection, 2f));
                for (int pass = 0; pass < 8; pass++)
                    compute.Dispatch(kernel, groups, 1, 1);
                stateBuffer.GetData(states);
                int wallIndex = MerkabaConstants.KernelsPerChunk +
                    MerkabaConstants.Flatten(new int3(0, 0, 16)); // global z=-80

                Assert.That(states[foregroundIndex].IsOccupied, Is.False,
                    "valid clear rays must eat the false foreground");
                Assert.That(states[wallIndex].IsOccupied, Is.True,
                    "the measured two-metre wall must persist");
                Assert.That(states[foregroundIndex].OccupancyEvidence,
                    Is.LessThanOrEqualTo(MerkabaConstants.OccupiedOffThreshold));
                Assert.That(states[wallIndex].OccupancyEvidence,
                    Is.GreaterThan(MerkabaConstants.OccupiedOnThreshold));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depth);
                UnityEngine.Object.DestroyImmediate(normals);
                UnityEngine.Object.DestroyImmediate(dilation);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }

        [Test, Timeout(30000)]
        public void ProductionGpuDepthPath_ConsumesRightEyeWhenLeftEyeIsInvalid()
        {
            ComputeShader compute = LoadCompute("MerkabaIntegration.compute");
            int kernel = compute.FindKernel("IntegrateMerkaba");
            int stateCount = MerkabaConstants.KernelsPerChunk;
            var states = new KernelState[stateCount];
            var pageCoords = new[] { new int4(0, 0, -2, 0) };
            int[] neighbours = PageNeighbours(pageCoords);
            var activeSlots = new[] { 0 };
            var dirty = new uint[stateCount];

            using var stateBuffer = new ComputeBuffer(stateCount, 16);
            using var pageBuffer = new ComputeBuffer(1, 16);
            using var neighbourBuffer = new ComputeBuffer(27, sizeof(int));
            using var activeBuffer = new ComputeBuffer(1, sizeof(int));
            using var dirtyBuffer = new ComputeBuffer(stateCount, sizeof(uint));
            stateBuffer.SetData(states);
            pageBuffer.SetData(pageCoords);
            neighbourBuffer.SetData(neighbours);
            activeBuffer.SetData(activeSlots);
            dirtyBuffer.SetData(dirty);

            Matrix4x4 projection = Matrix4x4.Perspective(90f, 1f, 0.1f, 10f);
            Matrix4x4[] projections = { projection, projection };
            Matrix4x4[] projectionInv = { projection.inverse, projection.inverse };
            Matrix4x4[] views = { Matrix4x4.identity, Matrix4x4.identity };
            float rightDepth = DepthNdc(projection, 1f);
            Texture2DArray depth = MakeDepth(0f, rightDepth);
            Texture2DArray normals = MakeNormals();
            Texture2DArray dilation = MakeDilation(0f, rightDepth);
            Texture2D camera = MakeCamera();

            try
            {
                compute.SetBuffer(kernel, "_MerkabaKernels", stateBuffer);
                compute.SetBuffer(kernel, "_MerkabaPageCoords", pageBuffer);
                compute.SetBuffer(kernel, "_MerkabaPageNeighbours", neighbourBuffer);
                compute.SetBuffer(kernel, "_MerkabaIntegrationSlots", activeBuffer);
                compute.SetBuffer(kernel, "_MerkabaKernelDirty", dirtyBuffer);
                compute.SetInt("_MerkabaIntegrationChunkCount", 1);
                compute.SetMatrix("_MerkabaGridToWorld", Matrix4x4.identity);
                compute.SetFloat("_MerkabaMaxUpdateDistance", 5f);
                compute.SetInt("_MerkabaExclusionCount", 0);
                compute.SetInt("_MerkabaCameraAvailable", 0);
                compute.SetTexture(kernel, "_MerkabaCameraRgb", camera);
                compute.SetTexture(kernel, DepthCapture.DepthTexID, depth);
                compute.SetTexture(kernel, DepthCapture.NormTexID, normals);
                compute.SetTexture(kernel, DepthCapture.DilatedDepthTexID, dilation);
                compute.SetMatrixArray(DepthCapture.ProjID, projections);
                compute.SetMatrixArray(DepthCapture.ProjInvID, projectionInv);
                compute.SetMatrixArray(DepthCapture.ViewID, views);
                compute.SetMatrixArray(DepthCapture.ViewInvID, views);
                compute.SetVector(DepthCapture.ZParamsID,
                    new Vector4(0.1f, 10f, 0f, 0f));
                compute.SetVector(DepthCapture.TexSizeID,
                    new Vector4(16, 16, 0, 0));

                int groups = stateCount / 64;
                compute.Dispatch(kernel, groups, 1, 1);
                compute.Dispatch(kernel, groups, 1, 1);
                stateBuffer.GetData(states);
                int surfaceIndex = MerkabaConstants.Flatten(new int3(0, 0, 24));
                Assert.That(states[surfaceIndex].IsOccupied, Is.True,
                    "Right-eye-only valid depth did not contribute to integration.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depth);
                UnityEngine.Object.DestroyImmediate(normals);
                UnityEngine.Object.DestroyImmediate(dilation);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }

        [Test, Timeout(30000)]
        public void ProductionGpuTopology_MatchesCpuOwnershipAcrossChunkBorder()
        {
            ComputeShader compute = LoadCompute("MerkabaTopology.compute");
            int kernel = compute.FindKernel("BuildVisibleRecords");
            const int pageCount = 2;
            int stateCount = pageCount * MerkabaConstants.KernelsPerChunk;
            var states = new KernelState[stateCount];
            var occupied = new HashSet<int3>
            {
                new(31, 0, 0), new(32, 0, 0), new(31, 1, 0), new(32, 1, 1)
            };
            foreach (int3 coord in occupied)
            {
                int slot = coord.x < 32 ? 0 : 1;
                int3 local = MerkabaConstants.LocalCoord(coord);
                ref KernelState state = ref states[slot * MerkabaConstants.KernelsPerChunk +
                    MerkabaConstants.Flatten(local)];
                MerkabaIntegrator.IntegrateClassified(ref state,
                    MerkabaObservationKind.Surface, 1f, new Color32(20, 40, 80, 255));
            }
            var pageCoords = new[] { new int4(0, 0, 0, 0), new int4(1, 0, 0, 1) };
            int[] neighbours = PageNeighbours(pageCoords);
            var dirty = new uint[stateCount];
            Array.Fill(dirty, 1u);
            var masks = new uint[stateCount];
            var visible = new[] { 0, 1 };

            using var stateBuffer = new ComputeBuffer(stateCount, 16);
            using var pageBuffer = new ComputeBuffer(pageCount, 16);
            using var neighbourBuffer = new ComputeBuffer(pageCount * 27, sizeof(int));
            using var visibleBuffer = new ComputeBuffer(pageCount, sizeof(int));
            using var dirtyBuffer = new ComputeBuffer(stateCount, sizeof(uint));
            using var maskBuffer = new ComputeBuffer(stateCount, sizeof(uint));
            using var records = new ComputeBuffer(stateCount, 32, ComputeBufferType.Append);
            using var countBuffer = new ComputeBuffer(1, sizeof(uint),
                ComputeBufferType.Raw);
            stateBuffer.SetData(states);
            pageBuffer.SetData(pageCoords);
            neighbourBuffer.SetData(neighbours);
            visibleBuffer.SetData(visible);
            dirtyBuffer.SetData(dirty);
            maskBuffer.SetData(masks);
            records.SetCounterValue(0);

            compute.SetBuffer(kernel, "_MerkabaKernels", stateBuffer);
            compute.SetBuffer(kernel, "_MerkabaPageCoords", pageBuffer);
            compute.SetBuffer(kernel, "_MerkabaPageNeighbours", neighbourBuffer);
            compute.SetBuffer(kernel, "_MerkabaVisibleSlots", visibleBuffer);
            compute.SetBuffer(kernel, "_MerkabaKernelDirty", dirtyBuffer);
            compute.SetBuffer(kernel, "_MerkabaTopologyMasks", maskBuffer);
            compute.SetBuffer(kernel, "_MerkabaRenderRecords", records);
            compute.SetInt("_MerkabaVisibleChunkCount", pageCount);
            compute.Dispatch(kernel, stateCount / 64, 1, 1);
            ComputeBuffer.CopyCount(records, countBuffer, 0);
            var count = new uint[1];
            countBuffer.GetData(count);
            Assert.That(count[0], Is.EqualTo((uint)occupied.Count));

            var output = new RenderRecord[(int)count[0]];
            records.GetData(output, 0, 0, output.Length);
            var gpuMasks = new Dictionary<int3, uint>();
            foreach (RenderRecord record in output)
                gpuMasks.Add(new int3(record.X, record.Y, record.Z), record.ActiveMask);
            foreach (int3 coord in occupied)
            {
                uint expected = MerkabaCanonicalGeometry.ActivePrimitiveMask(
                    coord, occupied.Contains);
                Assert.That(gpuMasks[coord], Is.EqualTo(expected),
                    $"GPU direct primitive rule diverged at {coord}");
            }
        }

        private static ComputeShader LoadCompute(string file)
        {
            string path = "Packages/com.genesis.roomscan/Runtime/Shaders/" + file;
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            Assert.That(shader, Is.Not.Null, path);
            return shader;
        }

        private static int[] PageNeighbours(int4[] pages)
        {
            var result = new int[pages.Length * 27];
            Array.Fill(result, -1);
            for (int slot = 0; slot < pages.Length; slot++)
            for (int other = 0; other < pages.Length; other++)
            {
                int3 delta = pages[other].xyz - pages[slot].xyz;
                if (math.any(delta < -1) || math.any(delta > 1)) continue;
                int index = (delta.x + 1) + 3 * (delta.y + 1) + 9 * (delta.z + 1);
                result[slot * 27 + index] = other;
            }
            return result;
        }

        private static float DepthNdc(Matrix4x4 projection, float distance)
        {
            Vector4 clip = projection * new Vector4(0, 0, -distance, 1);
            return clip.z / clip.w * 0.5f + 0.5f;
        }

        private static Texture2DArray MakeDepth(float depth)
            => MakeDepth(depth, depth);

        private static Texture2DArray MakeDepth(float leftDepth, float rightDepth)
        {
            var texture = new Texture2DArray(16, 16, 2, TextureFormat.RFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            SetDepth(texture, leftDepth, rightDepth);
            return texture;
        }

        private static void SetDepth(Texture2DArray texture, float depth)
            => SetDepth(texture, depth, depth);

        private static void SetDepth(Texture2DArray texture, float leftDepth,
            float rightDepth)
        {
            var pixels = new Color[16 * 16];
            Array.Fill(pixels, new Color(leftDepth, 0, 0, 0));
            texture.SetPixels(pixels, 0, 0);
            Array.Fill(pixels, new Color(rightDepth, 0, 0, 0));
            texture.SetPixels(pixels, 1, 0);
            texture.Apply(false, false);
        }

        private static Texture2DArray MakeNormals()
        {
            var texture = new Texture2DArray(16, 16, 2, TextureFormat.RGBAFloat,
                false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[16 * 16];
            Array.Fill(pixels, new Color(0, 0, 1, 1));
            texture.SetPixels(pixels, 0, 0);
            texture.SetPixels(pixels, 1, 0);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2DArray MakeDilation(float depth) =>
            MakeDilation(depth, depth);

        private static Texture2DArray MakeDilation(float leftDepth,
            float rightDepth)
        {
            var texture = new Texture2DArray(16, 16, 2, TextureFormat.RGBAFloat,
                false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            SetDilation(texture, leftDepth, rightDepth);
            return texture;
        }

        private static void SetDilation(Texture2DArray texture, float depth) =>
            SetDilation(texture, depth, depth);

        private static void SetDilation(Texture2DArray texture, float leftDepth,
            float rightDepth)
        {
            var pixels = new Color[16 * 16];
            Array.Fill(pixels, new Color(0, 0, leftDepth, 0));
            texture.SetPixels(pixels, 0, 0);
            Array.Fill(pixels, new Color(0, 0, rightDepth, 0));
            texture.SetPixels(pixels, 1, 0);
            texture.Apply(false, false);
        }

        private static Texture2D MakeCamera()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            texture.SetPixel(0, 0, Color.black);
            texture.Apply(false, false);
            return texture;
        }
    }
}
