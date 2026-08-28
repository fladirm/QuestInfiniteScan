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
            var pageCoords = new[] { new int4(0, 0, -2, 0), new int4(0, 0, -3, 1) };
            using var fixture = new SparseIntegrationFixture(pageCoords);
            fixture.SetMeasuredDistance(1f, 1f);
            fixture.Run();
            fixture.Run();
            KernelState[] afterFalseHit = fixture.ReadStates();
            var falseForeground = new List<int>();
            for (int index = 0; index < afterFalseHit.Length; index++)
            {
                int3 coord = GlobalCoord(pageCoords, index);
                if (afterFalseHit[index].IsOccupied && math.abs(coord.z + 40) <= 1)
                    falseForeground.Add(index);
            }
            Assert.That(falseForeground, Is.Not.Empty,
                "surface-driven queue did not create the one-metre false foreground");
            Assert.That(fixture.LastSurfaceWorkCount,
                Is.LessThan((uint)afterFalseHit.Length),
                "surface integration regressed to a dense page-volume work domain");

            fixture.SetMeasuredDistance(2f, 2f);
            for (int pass = 0; pass < 10; pass++) fixture.Run();
            KernelState[] afterClear = fixture.ReadStates();
            foreach (int index in falseForeground)
            {
                Assert.That(afterClear[index].IsOccupied, Is.False,
                    "valid clear rays must eat every prior false-foreground kernel");
                Assert.That(afterClear[index].OccupancyEvidence,
                    Is.LessThanOrEqualTo(MerkabaConstants.OccupiedOffThreshold));
            }
            bool wallPersists = false;
            for (int index = 0; index < afterClear.Length; index++)
            {
                int3 coord = GlobalCoord(pageCoords, index);
                if (afterClear[index].IsOccupied && math.abs(coord.z + 80) <= 1)
                    wallPersists = true;
            }
            Assert.That(wallPersists, Is.True,
                "the measured two-metre wall must persist through the sparse path");
            Assert.That(fixture.LastCarveWorkCount, Is.GreaterThan(0u));
        }

        [Test, Timeout(30000)]
        public void ProductionGpuDepthPath_ConsumesRightEyeWhenLeftEyeIsInvalid()
        {
            var pageCoords = new[] { new int4(0, 0, -2, 0) };
            using var fixture = new SparseIntegrationFixture(pageCoords);
            fixture.SetMeasuredDistance(0f, 1f);
            fixture.Run();
            fixture.Run();
            KernelState[] states = fixture.ReadStates();
            bool rightEyeSurface = false;
            for (int index = 0; index < states.Length; index++)
            {
                int3 coord = GlobalCoord(pageCoords, index);
                if (states[index].IsOccupied && math.abs(coord.z + 40) <= 1)
                    rightEyeSurface = true;
            }
            Assert.That(rightEyeSurface, Is.True,
                "right-eye-only valid depth did not enter the sparse surface queue");
        }

        [Test, Timeout(30000)]
        public void SparseSurfaceAndCarve_CrossNegativeChunkBorderWithoutDenseScan()
        {
            var pageCoords = new[] { new int4(0, 0, -1, 0), new int4(0, 0, -2, 1) };
            using var fixture = new SparseIntegrationFixture(pageCoords);
            fixture.SetMeasuredDistance(0.8125f, 0.8125f); // straddles global z=-32/-33
            fixture.Run();
            fixture.Run();
            KernelState[] hit = fixture.ReadStates();
            var occupiedAtBorder = new List<int>();
            bool firstPage = false;
            bool secondPage = false;
            for (int index = 0; index < hit.Length; index++)
            {
                int3 coord = GlobalCoord(pageCoords, index);
                if (!hit[index].IsOccupied || math.abs(coord.z + 32) > 1) continue;
                occupiedAtBorder.Add(index);
                if (index < MerkabaConstants.KernelsPerChunk) firstPage = true;
                else secondPage = true;
            }
            Assert.That(firstPage && secondPage, Is.True,
                "surface candidates must cross both sides of the z=-32 chunk border");

            fixture.SetMeasuredDistance(1.2f, 1.2f);
            for (int pass = 0; pass < 10; pass++) fixture.Run();
            KernelState[] cleared = fixture.ReadStates();
            foreach (int index in occupiedAtBorder)
                Assert.That(cleared[index].IsOccupied, Is.False,
                    "carve must remove false evidence on either side of a chunk border");
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

        private static int3 GlobalCoord(int4[] pages, int stateIndex)
        {
            int slot = stateIndex / MerkabaConstants.KernelsPerChunk;
            int localIndex = stateIndex % MerkabaConstants.KernelsPerChunk;
            return pages[slot].xyz * MerkabaConstants.ChunkSize +
                   MerkabaConstants.Unflatten(localIndex);
        }

        private sealed class SparseIntegrationFixture : IDisposable
        {
            private const int HashCapacity = 16;
            private readonly ComputeShader _compute;
            private readonly int _generate;
            private readonly int _prepare;
            private readonly int _surface;
            private readonly int _gather;
            private readonly int _carve;
            private readonly int4[] _pages;
            private readonly int _stateCount;
            private readonly Matrix4x4 _projection;
            private readonly ComputeBuffer _states;
            private readonly ComputeBuffer _pageCoords;
            private readonly ComputeBuffer _pageNeighbours;
            private readonly ComputeBuffer _slots;
            private readonly ComputeBuffer _enabled;
            private readonly ComputeBuffer _dirty;
            private readonly ComputeBuffer _hash;
            private readonly ComputeBuffer _surfaceBits;
            private readonly ComputeBuffer _surfaceQueue;
            private readonly ComputeBuffer _surfaceCount;
            private readonly ComputeBuffer _carveBits;
            private readonly ComputeBuffer _carveLocal;
            private readonly ComputeBuffer _carveCounts;
            private readonly ComputeBuffer _carveQueue;
            private readonly ComputeBuffer _carveCount;
            private readonly ComputeBuffer _surfaceArgs;
            private readonly ComputeBuffer _carveArgs;
            private readonly Texture2DArray _depth;
            private readonly Texture2DArray _normals;
            private readonly Texture2DArray _dilation;
            private readonly Texture2D _camera;
            private readonly uint[] _zero = { 0u };

            public uint LastSurfaceWorkCount { get; private set; }
            public uint LastCarveWorkCount { get; private set; }

            public SparseIntegrationFixture(int4[] pages)
            {
                _pages = pages;
                _stateCount = pages.Length * MerkabaConstants.KernelsPerChunk;
                _compute = LoadCompute("MerkabaIntegration.compute");
                _generate = _compute.FindKernel("GenerateSurfaceCandidates");
                _prepare = _compute.FindKernel("PrepareIndirectArgs");
                _surface = _compute.FindKernel("IntegrateSurfaceCandidates");
                _gather = _compute.FindKernel("GatherCarveCandidates");
                _carve = _compute.FindKernel("IntegrateCarveCandidates");

                _states = new ComputeBuffer(_stateCount, 16);
                _pageCoords = new ComputeBuffer(pages.Length, 16);
                _pageNeighbours = new ComputeBuffer(pages.Length * 27, sizeof(int));
                _slots = new ComputeBuffer(pages.Length, sizeof(int));
                _enabled = new ComputeBuffer(pages.Length, sizeof(uint));
                _dirty = new ComputeBuffer(_stateCount, sizeof(uint));
                _hash = new ComputeBuffer(HashCapacity, 16);
                _surfaceBits = new ComputeBuffer(_stateCount / 32, sizeof(uint));
                _surfaceQueue = new ComputeBuffer(_stateCount, sizeof(uint));
                _surfaceCount = new ComputeBuffer(1, sizeof(uint));
                _carveBits = new ComputeBuffer(_stateCount / 32, sizeof(uint));
                _carveLocal = new ComputeBuffer(_stateCount, sizeof(uint));
                _carveCounts = new ComputeBuffer(pages.Length, sizeof(uint));
                _carveQueue = new ComputeBuffer(_stateCount, sizeof(uint));
                _carveCount = new ComputeBuffer(1, sizeof(uint));
                _surfaceArgs = new ComputeBuffer(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _carveArgs = new ComputeBuffer(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);

                _states.SetData(new KernelState[_stateCount]);
                _pageCoords.SetData(pages);
                _pageNeighbours.SetData(PageNeighbours(pages));
                var slots = new int[pages.Length];
                var enabled = new uint[pages.Length];
                for (int i = 0; i < pages.Length; i++)
                {
                    slots[i] = i;
                    enabled[i] = 1u;
                }
                _slots.SetData(slots);
                _enabled.SetData(enabled);
                _dirty.SetData(new uint[_stateCount]);
                _surfaceBits.SetData(new uint[_stateCount / 32]);
                _carveBits.SetData(new uint[_stateCount / 32]);
                _carveCounts.SetData(new uint[pages.Length]);
                _surfaceCount.SetData(_zero);
                _carveCount.SetData(_zero);
                _surfaceArgs.SetData(new uint[] { 0, 1, 1 });
                _carveArgs.SetData(new uint[] { 0, 1, 1 });
                _hash.SetData(BuildHash(pages));

                _projection = Matrix4x4.Perspective(90f, 1f, 0.1f, 10f);
                _depth = MakeDepth(0f);
                _normals = MakeNormals();
                _dilation = MakeDilation(0f);
                _camera = MakeCamera();
                BindCommon();
            }

            public void SetMeasuredDistance(float left, float right)
            {
                float leftNdc = left > 0f ? DepthNdc(_projection, left) : 0f;
                float rightNdc = right > 0f ? DepthNdc(_projection, right) : 0f;
                SetDepth(_depth, leftNdc, rightNdc);
                SetDilation(_dilation, leftNdc, rightNdc);
            }

            public void Run()
            {
                _surfaceCount.SetData(_zero);
                _carveCount.SetData(_zero);
                _compute.Dispatch(_generate, 2, 2, 2);
                Prepare(_surfaceCount, _surfaceArgs);
                _compute.DispatchIndirect(_surface, _surfaceArgs);
                _compute.Dispatch(_gather, _pages.Length, 1, 1);
                Prepare(_carveCount, _carveArgs);
                _compute.DispatchIndirect(_carve, _carveArgs);
                LastSurfaceWorkCount = ReadCount(_surfaceCount);
                LastCarveWorkCount = ReadCount(_carveCount);
            }

            public KernelState[] ReadStates()
            {
                var result = new KernelState[_stateCount];
                _states.GetData(result);
                return result;
            }

            private void BindCommon()
            {
                Matrix4x4[] projections = { _projection, _projection };
                Matrix4x4[] projectionInv = { _projection.inverse, _projection.inverse };
                Matrix4x4[] identity = { Matrix4x4.identity, Matrix4x4.identity };
                _compute.SetMatrixArray(DepthCapture.ProjID, projections);
                _compute.SetMatrixArray(DepthCapture.ProjInvID, projectionInv);
                _compute.SetMatrixArray(DepthCapture.ViewID, identity);
                _compute.SetMatrixArray(DepthCapture.ViewInvID, identity);
                _compute.SetVector(DepthCapture.ZParamsID,
                    new Vector4(0.1f, 10f, 0f, 0f));
                _compute.SetVector(DepthCapture.TexSizeID,
                    new Vector4(16, 16, 0f, 0f));
                _compute.SetInt("_MerkabaIntegrationChunkCount", _pages.Length);
                _compute.SetInt("_MerkabaPageHashCapacity", HashCapacity);
                _compute.SetInt("_MerkabaWorkCapacity", _stateCount);
                _compute.SetMatrix("_MerkabaGridToWorld", Matrix4x4.identity);
                _compute.SetMatrix("_MerkabaWorldToGrid", Matrix4x4.identity);
                _compute.SetFloat("_MerkabaMaxUpdateDistance", 5f);
                _compute.SetInt("_MerkabaExclusionCount", 0);
                _compute.SetInt("_MerkabaCameraAvailable", 0);

                _compute.SetBuffer(_generate, "_MerkabaPageHash", _hash);
                _compute.SetBuffer(_generate, "_MerkabaIntegrationEnabledSlots", _enabled);
                _compute.SetBuffer(_generate, "_MerkabaSurfaceCandidateBits", _surfaceBits);
                _compute.SetBuffer(_generate, "_MerkabaSurfaceQueue", _surfaceQueue);
                _compute.SetBuffer(_generate, "_MerkabaSurfaceCount", _surfaceCount);
                _compute.SetTexture(_generate, DepthCapture.DepthTexID, _depth);

                BindObservation(_surface);
                _compute.SetBuffer(_surface, "_MerkabaSurfaceCandidateBits", _surfaceBits);
                _compute.SetBuffer(_surface, "_MerkabaSurfaceQueue", _surfaceQueue);
                _compute.SetBuffer(_surface, "_MerkabaSurfaceCount", _surfaceCount);
                _compute.SetBuffer(_surface, "_MerkabaCarveListedBits", _carveBits);
                _compute.SetBuffer(_surface, "_MerkabaCarveLocalIndices", _carveLocal);
                _compute.SetBuffer(_surface, "_MerkabaCarveCounts", _carveCounts);
                _compute.SetTexture(_surface, "_MerkabaCameraRgb", _camera);

                _compute.SetBuffer(_gather, "_MerkabaKernels", _states);
                _compute.SetBuffer(_gather, "_MerkabaIntegrationSlots", _slots);
                _compute.SetBuffer(_gather, "_MerkabaCarveLocalIndices", _carveLocal);
                _compute.SetBuffer(_gather, "_MerkabaCarveCounts", _carveCounts);
                _compute.SetBuffer(_gather, "_MerkabaCarveQueue", _carveQueue);
                _compute.SetBuffer(_gather, "_MerkabaCarveCount", _carveCount);

                BindObservation(_carve);
                _compute.SetBuffer(_carve, "_MerkabaCarveQueue", _carveQueue);
                _compute.SetBuffer(_carve, "_MerkabaCarveCount", _carveCount);
            }

            private void BindObservation(int kernel)
            {
                _compute.SetBuffer(kernel, "_MerkabaKernels", _states);
                _compute.SetBuffer(kernel, "_MerkabaPageCoords", _pageCoords);
                _compute.SetBuffer(kernel, "_MerkabaPageNeighbours", _pageNeighbours);
                _compute.SetBuffer(kernel, "_MerkabaKernelDirty", _dirty);
                _compute.SetTexture(kernel, DepthCapture.DepthTexID, _depth);
                _compute.SetTexture(kernel, DepthCapture.NormTexID, _normals);
                _compute.SetTexture(kernel, DepthCapture.DilatedDepthTexID, _dilation);
            }

            private void Prepare(ComputeBuffer count, ComputeBuffer args)
            {
                _compute.SetBuffer(_prepare, "_MerkabaWorkCount", count);
                _compute.SetBuffer(_prepare, "_MerkabaIndirectArgs", args);
                _compute.Dispatch(_prepare, 1, 1, 1);
            }

            private static uint ReadCount(ComputeBuffer buffer)
            {
                var value = new uint[1];
                buffer.GetData(value);
                return value[0];
            }

            private static int4[] BuildHash(int4[] pages)
            {
                var result = new int4[HashCapacity];
                Array.Fill(result, new int4(0, 0, 0, -1));
                for (int slot = 0; slot < pages.Length; slot++)
                {
                    int index = (int)(MerkabaGrid.HashPageCoord(pages[slot].xyz) &
                                      (HashCapacity - 1));
                    while (result[index].w >= 0)
                        index = (index + 1) & (HashCapacity - 1);
                    result[index] = new int4(pages[slot].xyz, slot);
                }
                return result;
            }

            public void Dispose()
            {
                _states.Dispose();
                _pageCoords.Dispose();
                _pageNeighbours.Dispose();
                _slots.Dispose();
                _enabled.Dispose();
                _dirty.Dispose();
                _hash.Dispose();
                _surfaceBits.Dispose();
                _surfaceQueue.Dispose();
                _surfaceCount.Dispose();
                _carveBits.Dispose();
                _carveLocal.Dispose();
                _carveCounts.Dispose();
                _carveQueue.Dispose();
                _carveCount.Dispose();
                _surfaceArgs.Dispose();
                _carveArgs.Dispose();
                UnityEngine.Object.DestroyImmediate(_depth);
                UnityEngine.Object.DestroyImmediate(_normals);
                UnityEngine.Object.DestroyImmediate(_dilation);
                UnityEngine.Object.DestroyImmediate(_camera);
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
