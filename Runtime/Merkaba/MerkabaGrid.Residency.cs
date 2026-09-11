using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    public partial class MerkabaGrid
    {
        private Action<CommandBuffer> _recordObservationResidency;
        private MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob _residencyJob;
        private uint _residencyJobGeneration;
        private bool _residencyRequested;

        internal void ConfigureObservationResidency(Action<CommandBuffer> record) =>
            _recordObservationResidency = record;

        // Retire before the lifecycle submission gate: sleep/OPEN may stop new
        // jobs, but must still release a submitted job's world ownership.
        private void RetireObservationResidency()
        {
            if (_residencyJob == null || !_residencyJob.Poll(out string error)) return;
            CompleteNativeWorldMutation(_residencyJobGeneration, string.IsNullOrEmpty(error));
            _residencyJobGeneration = 0u;
            _residencyJob.Dispose();
            _residencyJob = null;
            if (!string.IsNullOrEmpty(error)) Logger.Error(error);
        }

        internal async System.Threading.Tasks.Task RetireSubmittedResidencyAsync()
        {
            while (_residencyJob != null)
            {
                RetireObservationResidency();
                if (_residencyJob != null) await System.Threading.Tasks.Task.Yield();
            }
        }

        // A GPU-owned address gate decides whether these commands do work.
        // No counter readback, camera lease, bin replay or geometry consumer.
        private bool SubmitObservationResidency()
        {
            if (!_residencyRequested || _recordObservationResidency == null ||
                !WorldMutationSubmissionAllowed) return false;
            CommandBuffer command = CommandBufferPool.Get("Merkaba address residency");
            uint generation = 0u;
            bool submitted = false;
            try
            {
#if !UNITY_EDITOR && UNITY_ANDROID
                var resources = new IntPtr[MerkabaNativeVulkanExecutor.ResourceCount];
                FillNativeExecutorWorldResources(resources);
                var uniforms = new MerkabaNativeUniformTable();
                generation = BeginNativeWorldMutation(uniforms);
                if (!MerkabaNativeVulkanExecutor.TryCreateJob(
                        MerkabaNativeVulkanExecutor.JobKind.Residency, generation,
                        resources, uniforms, 0, 0, 0, 0, out var job)) return false;
                try
                {
                    job.RecordPrepareAndSubmit(command);
                    // From this point submission may have reached Unity. Retain
                    // the job and generation even if Execute reports an error.
                    _residencyJob = job;
                    _residencyJobGeneration = generation;
                    submitted = true;
                    Graphics.ExecuteCommandBuffer(command);
                }
                catch
                {
                    if (!submitted)
                    {
                        job.CancelBeforeExecution();
                        job.Dispose();
                    }
                    throw;
                }
#else
                generation = RecordWorldMutation(command, worldCompute);
                _recordObservationResidency(command);
                SubmitWorldMutation(command, generation);
                submitted = true;
#endif
                _residencyRequested = false;
                return true;
            }
            finally
            {
                if (!submitted) CancelWorldMutationBeforeSubmit(generation);
                CommandBufferPool.Release(command);
            }
        }
    }
}
