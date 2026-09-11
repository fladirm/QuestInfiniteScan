using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    // GraphicsStateCollection is a manufacturing coverage trace, never a
    // release warm-up or a substitute for driver-produced pipeline binaries.
    internal static class MerkabaPipelineCapture
    {
        private const string Library = "MerkabaVulkanTimestamps";
        private static GraphicsStateCollection _trace;
        private static string _path;
        private static double _nextWrite;
        private static readonly uint[] Stats = new uint[8];

        internal static void Begin()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_trace != null || GetMode() != 1) return;
            string root = Marshal.PtrToStringUTF8(GetCaptureDirectory());
            string build = Marshal.PtrToStringUTF8(GetBuildId());
            string directory = Path.Combine(root, build);
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "unity.graphicsstate");
            _trace = new GraphicsStateCollection();
            if (!_trace.BeginTrace()) throw new InvalidOperationException("Unity graphics-state capture could not start.");
            RenderPipelineManager.endContextRendering += CaptureFrame;
            Application.focusChanged += CaptureFocus;
            Application.quitting += Save;
#endif
        }

        private static void CaptureFrame(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (Time.realtimeSinceStartupAsDouble < _nextWrite) return;
            _nextWrite = Time.realtimeSinceStartupAsDouble + 30.0;
            Save();
        }
        private static void CaptureFocus(bool focused) { if (!focused) Save(); }
        private static void Save()
        {
            if (_trace == null) return;
            if (!_trace.SaveToFile(_path))
                Debug.LogError("Merkaba Unity graphics-state capture write failed.");
            LogStats();
        }
        internal static void LogStats()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (ReadStats(Stats, (uint)Stats.Length) != 0) return;
            Debug.Log($"Merkaba pipeline-binary: mode={Stats[0]} globalKeyMatch={Stats[1]} hits={Stats[2]} " +
                $"pipelineBinaryMiss={Stats[3]} nonBinaryPipelineAttempt={Stats[4]} runtimeCompileFallback={Stats[5]} " +
                $"captureWrites={Stats[6]} failed={Stats[7]}");
#endif
        }

        [DllImport(Library, EntryPoint = "MerkabaPipeline_GetMode")] private static extern int GetMode();
        [DllImport(Library, EntryPoint = "MerkabaPipeline_GetCaptureDirectory")] private static extern IntPtr GetCaptureDirectory();
        [DllImport(Library, EntryPoint = "MerkabaPipeline_GetBuildId")] private static extern IntPtr GetBuildId();
        [DllImport(Library, EntryPoint = "MerkabaPipeline_ReadStats")] private static extern int ReadStats([Out] uint[] values, uint count);
    }
}
