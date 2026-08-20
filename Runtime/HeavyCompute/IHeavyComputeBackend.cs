using System;
using System.Threading;
using System.Threading.Tasks;

namespace Genesis.RoomScan.HeavyCompute
{
    public sealed class HeavyComputeBackendException : Exception
    {
        public string Code { get; }
        public bool IsTransient { get; }

        public HeavyComputeBackendException(string code, string message, bool isTransient,
            Exception innerException = null) : base(message, innerException)
        {
            Code = string.IsNullOrEmpty(code) ? "backend_error" : code;
            IsTransient = isTransient;
        }
    }

    public interface IHeavyComputeBackend
    {
        string Name { get; }
        bool IsEnabled { get; }

        Task<HeavyComputeCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);
        Task<HeavyComputeJobStatus> CreateOrReplayAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken);
        Task<HeavyComputeJobStatus> UploadInputAsync(HeavyComputeSubmission submission,
            string inputPath, CancellationToken cancellationToken);
        Task<HeavyComputeJobStatus> EnqueueAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken);
        Task<HeavyComputeJobStatus> GetStatusAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken);
        Task<HeavyComputeJobStatus> CancelAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken);
        Task DownloadArtifactAsync(HeavyComputeSubmission submission,
            HeavyComputeBlobDescriptor descriptor, string destinationPath,
            CancellationToken cancellationToken);
    }

    public sealed class NoneHeavyComputeBackend : IHeavyComputeBackend
    {
        public string Name => "none";
        public bool IsEnabled => false;

        public Task<HeavyComputeCapabilities> GetCapabilitiesAsync(
            CancellationToken cancellationToken) => Disabled<HeavyComputeCapabilities>();

        public Task<HeavyComputeJobStatus> CreateOrReplayAsync(
            HeavyComputeSubmission submission, CancellationToken cancellationToken) =>
            Disabled<HeavyComputeJobStatus>();

        public Task<HeavyComputeJobStatus> UploadInputAsync(HeavyComputeSubmission submission,
            string inputPath, CancellationToken cancellationToken) =>
            Disabled<HeavyComputeJobStatus>();

        public Task<HeavyComputeJobStatus> EnqueueAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken) => Disabled<HeavyComputeJobStatus>();

        public Task<HeavyComputeJobStatus> GetStatusAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken) => Disabled<HeavyComputeJobStatus>();

        public Task<HeavyComputeJobStatus> CancelAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken) => Disabled<HeavyComputeJobStatus>();

        public Task DownloadArtifactAsync(HeavyComputeSubmission submission,
            HeavyComputeBlobDescriptor descriptor, string destinationPath,
            CancellationToken cancellationToken) => Disabled<object>();

        private static Task<T> Disabled<T>()
        {
            return Task.FromException<T>(new HeavyComputeBackendException(
                "backend_disabled", "Heavy compute is disabled; the durable job remains queued.",
                true));
        }
    }
}
