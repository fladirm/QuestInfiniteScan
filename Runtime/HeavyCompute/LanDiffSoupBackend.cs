using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.World;
using UnityEngine.Networking;

namespace Genesis.RoomScan.HeavyCompute
{
    /// <summary>
    /// Protocol-v2 LAN client. UploadHandlerFile and DownloadHandlerFile keep multi-GB
    /// bundles out of managed memory; awaiting a request yields to Unity every frame.
    /// </summary>
    public sealed class LanDiffSoupBackend : IHeavyComputeBackend
    {
        private readonly Uri _baseUri;
        private readonly int _timeoutSeconds;

        public LanDiffSoupBackend(string baseUrl, int timeoutSeconds = 60)
        {
            if (!TryNormalizeBaseUri(baseUrl, out _baseUri, out string error))
                throw new ArgumentException(error, nameof(baseUrl));
            _timeoutSeconds = Math.Max(5, timeoutSeconds);
        }

        public string Name => "diffsoup";
        public bool IsEnabled => true;
        public string BaseUrl => _baseUri.AbsoluteUri.TrimEnd('/');

        public async Task<HeavyComputeCapabilities> GetCapabilitiesAsync(
            CancellationToken cancellationToken)
        {
            string json = await SendJsonAsync(UnityWebRequest.Get(Url("v2/capabilities")),
                cancellationToken);
            if (!HeavyComputeContract.TryParseCapabilities(json,
                    out HeavyComputeCapabilities capabilities, out string error))
                throw Permanent("invalid_capabilities", error);
            return capabilities;
        }

        public Task<HeavyComputeJobStatus> CreateOrReplayAsync(
            HeavyComputeSubmission submission, CancellationToken cancellationToken)
        {
            string json = HeavyComputeContract.BuildSubmissionJson(submission);
            var request = new UnityWebRequest(Url("v2/jobs/" + submission.jobId), "PUT")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return SendStatusAsync(request, submission, cancellationToken);
        }

        public Task<HeavyComputeJobStatus> UploadInputAsync(HeavyComputeSubmission submission,
            string inputPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath) ||
                new FileInfo(inputPath).Length != submission.inputBundle.byteLength)
                throw Permanent("input_missing", "Queued input bundle is missing or changed.");
            var request = new UnityWebRequest(Url("v2/jobs/" + submission.jobId + "/input"),
                "PUT")
            {
                uploadHandler = new UploadHandlerFile(inputPath),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", submission.inputBundle.mediaType);
            return SendStatusAsync(request, submission, cancellationToken);
        }

        public Task<HeavyComputeJobStatus> EnqueueAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken) => SendStatusAsync(
            PostWithoutBody("v2/jobs/" + submission.jobId + "/enqueue"), submission,
            cancellationToken);

        public Task<HeavyComputeJobStatus> GetStatusAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken) => SendStatusAsync(
            UnityWebRequest.Get(Url("v2/jobs/" + submission.jobId)), submission,
            cancellationToken);

        public Task<HeavyComputeJobStatus> CancelAsync(HeavyComputeSubmission submission,
            CancellationToken cancellationToken) => SendStatusAsync(
            PostWithoutBody("v2/jobs/" + submission.jobId + "/cancel"), submission,
            cancellationToken);

        public async Task DownloadArtifactAsync(HeavyComputeSubmission submission,
            HeavyComputeBlobDescriptor descriptor, string destinationPath,
            CancellationToken cancellationToken)
        {
            if (!HeavyComputeContract.TryValidateBlob(descriptor,
                    HeavyComputeProtocol.DiffSoupArtifactMediaType,
                    HeavyComputeProtocol.DiffSoupArtifactVersion,
                    HeavyComputeProtocol.MaximumArtifactBytes, out string descriptorError))
                throw Permanent("invalid_artifact_descriptor", descriptorError);
            string parent = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(parent))
                throw Permanent("invalid_artifact_path", "Artifact path has no parent.");
            Directory.CreateDirectory(parent);
            if (File.Exists(destinationPath))
                throw Permanent("artifact_path_exists", "Artifact staging path already exists.");

            var request = UnityWebRequest.Get(Url("v2/jobs/" + submission.jobId + "/artifact"));
            request.downloadHandler = new DownloadHandlerFile(destinationPath);
            try
            {
                await SendAsync(request, cancellationToken,
                    HeavyComputeProtocol.MaximumArtifactBytes);
                var info = new FileInfo(destinationPath);
                if (!info.Exists || info.Length != descriptor.byteLength)
                    throw Permanent("artifact_length_mismatch",
                        "Downloaded artifact length does not match server status.");
                string digest = await Task.Run(() => Hashing.ComputeSha256(destinationPath),
                    cancellationToken);
                if (!string.Equals(digest, descriptor.sha256, StringComparison.Ordinal))
                    throw Permanent("artifact_hash_mismatch",
                        "Downloaded artifact SHA-256 does not match server status.");
            }
            catch
            {
                TryDeleteExact(destinationPath);
                throw;
            }
            finally
            {
                request.Dispose();
            }
        }

        internal static bool TryNormalizeBaseUri(string value, out Uri uri, out string error)
        {
            uri = null;
            error = null;
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri parsed) ||
                parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) ||
                !string.IsNullOrEmpty(parsed.Fragment))
            {
                error = "Server URL must be an absolute HTTP(S) origin without credentials, " +
                        "query, or fragment.";
                return false;
            }
            uri = new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
            return true;
        }

        private async Task<HeavyComputeJobStatus> SendStatusAsync(UnityWebRequest request,
            HeavyComputeSubmission expected, CancellationToken cancellationToken)
        {
            string json = await SendJsonAsync(request, cancellationToken);
            if (!HeavyComputeContract.TryParseStatus(json, expected,
                    out HeavyComputeJobStatus status, out string error))
                throw Permanent("invalid_job_status", error);
            return status;
        }

        private async Task<string> SendJsonAsync(UnityWebRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                await SendAsync(request, cancellationToken,
                    HeavyComputeProtocol.MaximumJsonCharacters);
                string json = request.downloadHandler?.text;
                if (string.IsNullOrEmpty(json) ||
                    json.Length > HeavyComputeProtocol.MaximumJsonCharacters)
                    throw Permanent("invalid_json_response",
                        "Server JSON response is empty or too large.");
                return json;
            }
            finally
            {
                request.Dispose();
            }
        }

        private async Task SendAsync(UnityWebRequest request,
            CancellationToken cancellationToken, long maximumDownloadBytes)
        {
            request.timeout = _timeoutSeconds;
            UnityWebRequestAsyncOperation operation;
            try { operation = request.SendWebRequest(); }
            catch (Exception exception)
            {
                throw new HeavyComputeBackendException("network_start_failed",
                    exception.Message, true, exception);
            }
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if ((long)request.downloadedBytes > maximumDownloadBytes)
                {
                    request.Abort();
                    throw Permanent("response_too_large", "Server response exceeds its limit.");
                }
                await Task.Yield();
            }
            long statusCode = request.responseCode;
            if (statusCode >= 200 && statusCode < 300 &&
                request.result == UnityWebRequest.Result.Success)
                return;
            bool transient = statusCode == 0 || statusCode == 408 || statusCode == 425 ||
                             statusCode == 429 || statusCode >= 500;
            string code = statusCode == 0 ? "network_unavailable" : "http_" + statusCode;
            throw new HeavyComputeBackendException(code,
                string.IsNullOrEmpty(request.error)
                    ? $"Server returned HTTP {statusCode}." : request.error, transient);
        }

        private UnityWebRequest PostWithoutBody(string path)
        {
            return new UnityWebRequest(Url(path), "POST")
            {
                uploadHandler = new UploadHandlerRaw(Array.Empty<byte>()),
                downloadHandler = new DownloadHandlerBuffer()
            };
        }

        private string Url(string relative) => new Uri(_baseUri, relative).AbsoluteUri;

        private static HeavyComputeBackendException Permanent(string code, string message)
        {
            return new HeavyComputeBackendException(code, message, false);
        }

        private static void TryDeleteExact(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
