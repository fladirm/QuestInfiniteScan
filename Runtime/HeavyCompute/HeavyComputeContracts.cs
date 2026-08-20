using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Genesis.RoomScan.HeavyCompute
{
    public static class HeavyComputeProtocol
    {
        public const int Version = 2;
        public const int ChunkBundleVersion = 1;
        public const int DiffSoupArtifactVersion = 1;
        public const long MaximumUploadBytes = 8L * 1024 * 1024 * 1024;
        public const long MaximumArtifactBytes = 4L * 1024 * 1024 * 1024;
        public const int MaximumJsonCharacters = 1024 * 1024;
        public const string ChunkBundleMediaType =
            "application/vnd.questinfinitescan.chunk+zip";
        public const string DiffSoupArtifactMediaType =
            "application/vnd.questinfinitescan.diffsoup+zip";
    }

    [Serializable]
    public sealed class HeavyComputeJobKey
    {
        public string worldId;
        public string chunkId;
        public int chunkRevision;

        public HeavyComputeJobKey() { }

        public HeavyComputeJobKey(string worldId, string chunkId, int chunkRevision)
        {
            this.worldId = worldId;
            this.chunkId = chunkId;
            this.chunkRevision = chunkRevision;
        }

        public string JobId => HeavyComputeContract.ComputeJobId(this);
    }

    [Serializable]
    public sealed class HeavyComputeBlobDescriptor
    {
        public string mediaType;
        public int formatVersion;
        public long byteLength;
        public string sha256;

        public HeavyComputeBlobDescriptor Clone()
        {
            return new HeavyComputeBlobDescriptor
            {
                mediaType = mediaType,
                formatVersion = formatVersion,
                byteLength = byteLength,
                sha256 = sha256
            };
        }
    }

    [Serializable]
    public sealed class HeavyComputeWarmStart
    {
        public int sourceRevision;
        public string compatibilityTag;
        public HeavyComputeBlobDescriptor checkpoint;
    }

    [Serializable]
    public sealed class HeavyComputeSubmission
    {
        public int schemaVersion = HeavyComputeProtocol.Version;
        public string jobId;
        public string requestFingerprint;
        public HeavyComputeJobKey key;
        public HeavyComputeBlobDescriptor inputBundle;
        public string backend = "diffsoup";
        public string profile = "balanced";
        public bool allowFreshFallback = true;
        // JsonUtility materializes a null nested class as an empty object on some Unity
        // runtimes. This explicit local-presence bit keeps queue round trips unambiguous;
        // it is intentionally omitted from the protocol JSON built below.
        public bool hasWarmStart;
        public HeavyComputeWarmStart warmStart;

        public static bool TryCreate(HeavyComputeJobKey key,
            HeavyComputeBlobDescriptor inputBundle, string profile,
            bool allowFreshFallback, HeavyComputeWarmStart warmStart,
            out HeavyComputeSubmission submission, out string error)
        {
            submission = new HeavyComputeSubmission
            {
                key = key,
                inputBundle = inputBundle,
                profile = profile,
                allowFreshFallback = allowFreshFallback,
                hasWarmStart = warmStart != null,
                warmStart = warmStart
            };
            if (!HeavyComputeContract.TryValidateSubmission(submission, false, out error))
            {
                submission = null;
                return false;
            }
            submission.jobId = HeavyComputeContract.ComputeJobId(key);
            submission.requestFingerprint =
                HeavyComputeContract.ComputeRequestFingerprint(submission);
            return HeavyComputeContract.TryValidateSubmission(submission, true, out error);
        }
    }

    public enum HeavyComputeRemoteState
    {
        Unknown = 0,
        AwaitingUpload = 1,
        Queued = 2,
        Running = 3,
        Succeeded = 4,
        Failed = 5,
        Canceled = 6
    }

    [Serializable]
    public sealed class HeavyComputeJobStatus
    {
        public int schemaVersion;
        public string jobId;
        public string requestFingerprint;
        public HeavyComputeJobKey key;
        public string state;
        public float progress;
        public int attempt;
        public long createdUnixMs;
        public long updatedUnixMs;
        public string message;
        public long retryAfterMs;
        public HeavyComputeBlobDescriptor artifactBundle;
        public string errorCode;

        public HeavyComputeRemoteState RemoteState =>
            HeavyComputeContract.ParseRemoteState(state);
    }

    [Serializable]
    public sealed class HeavyComputeCapabilities
    {
        public int schemaVersion;
        public int[] protocolVersions;
        public int[] chunkBundleFormatVersions;
        public int[] diffSoupArtifactFormatVersions;
        public string[] backends;
        public string[] profiles;
        public long maximumUploadBytes;
        public long maximumArtifactBytes;
        public bool supportsCancel;
        public bool supportsRetry;
        public bool supportsWarmStart;
    }

    public static class HeavyComputeContract
    {
        private static readonly char[] Hex = "0123456789abcdef".ToCharArray();

        public static string ComputeJobId(HeavyComputeJobKey key)
        {
            if (!TryValidateKey(key, out string error))
                throw new ArgumentException(error, nameof(key));
            string identity = $"qis-job-v{HeavyComputeProtocol.Version}\0" +
                              $"{key.worldId.Length}:{key.worldId}\0" +
                              $"{key.chunkId.Length}:{key.chunkId}\0" +
                              key.chunkRevision.ToString(CultureInfo.InvariantCulture);
            return Sha256(Encoding.UTF8.GetBytes(identity));
        }

        public static string ComputeRequestFingerprint(HeavyComputeSubmission submission)
        {
            if (!TryValidateSubmission(submission, false, out string error))
                throw new ArgumentException(error, nameof(submission));
            return Sha256(Encoding.ASCII.GetBytes(BuildImmutableCanonicalJson(submission)));
        }

        public static string BuildSubmissionJson(HeavyComputeSubmission submission)
        {
            if (!TryValidateSubmission(submission, true, out string error))
                throw new ArgumentException(error, nameof(submission));
            var builder = new StringBuilder(1024);
            builder.Append('{');
            AppendProperty(builder, "schemaVersion", submission.schemaVersion);
            builder.Append(',');
            AppendProperty(builder, "jobId", submission.jobId);
            builder.Append(',');
            AppendProperty(builder, "requestFingerprint", submission.requestFingerprint);
            builder.Append(",\"key\":");
            AppendKey(builder, submission.key);
            builder.Append(",\"inputBundle\":");
            AppendBlob(builder, submission.inputBundle);
            builder.Append(',');
            AppendProperty(builder, "backend", submission.backend);
            builder.Append(',');
            AppendProperty(builder, "profile", submission.profile);
            builder.Append(",\"allowFreshFallback\":");
            builder.Append(submission.allowFreshFallback ? "true" : "false");
            builder.Append(",\"warmStart\":");
            AppendWarmStart(builder, submission.hasWarmStart ? submission.warmStart : null);
            builder.Append('}');
            return builder.ToString();
        }

        public static bool TryParseStatus(string json, HeavyComputeSubmission expected,
            out HeavyComputeJobStatus status, out string error)
        {
            status = null;
            error = null;
            if (string.IsNullOrEmpty(json) || json.Length > HeavyComputeProtocol.MaximumJsonCharacters)
            {
                error = "Job status JSON is empty or exceeds the size limit.";
                return false;
            }
            try { status = JsonUtility.FromJson<HeavyComputeJobStatus>(json); }
            catch (Exception exception)
            {
                error = "Job status JSON is malformed: " + exception.Message;
                return false;
            }
            // Unity's JsonUtility can materialize a JSON null class field as an empty
            // object. Normalize the two nullable protocol members before validation.
            if (HasTopLevelNull(json, "artifactBundle"))
                status.artifactBundle = null;
            if (HasTopLevelNull(json, "errorCode"))
                status.errorCode = null;
            if (status == null || status.schemaVersion != HeavyComputeProtocol.Version ||
                !TryValidateKey(status.key, out error) ||
                !IsLowerHexDigest(status.jobId) ||
                !IsLowerHexDigest(status.requestFingerprint) ||
                status.RemoteState == HeavyComputeRemoteState.Unknown ||
                !IsFinite(status.progress) || status.progress < 0f || status.progress > 1f ||
                status.attempt < 0 || status.createdUnixMs < 0 ||
                status.updatedUnixMs < status.createdUnixMs ||
                (status.message?.Length ?? 0) > 1024)
            {
                error ??= "Job status violates protocol v2.";
                status = null;
                return false;
            }
            if (!string.Equals(status.jobId, ComputeJobId(status.key),
                    StringComparison.Ordinal) || expected == null ||
                !string.Equals(status.jobId, expected.jobId, StringComparison.Ordinal) ||
                !string.Equals(status.requestFingerprint, expected.requestFingerprint,
                    StringComparison.Ordinal) ||
                status.key.chunkRevision != expected.key.chunkRevision ||
                !string.Equals(status.key.worldId, expected.key.worldId,
                    StringComparison.Ordinal) ||
                !string.Equals(status.key.chunkId, expected.key.chunkId,
                    StringComparison.Ordinal))
            {
                error = "Job status identity does not match the queued submission.";
                status = null;
                return false;
            }
            bool succeeded = status.RemoteState == HeavyComputeRemoteState.Succeeded;
            bool failed = status.RemoteState == HeavyComputeRemoteState.Failed;
            if (succeeded != (status.artifactBundle != null) ||
                failed != !string.IsNullOrEmpty(status.errorCode))
            {
                error = "Job status terminal fields are inconsistent.";
                status = null;
                return false;
            }
            if (status.artifactBundle != null &&
                !TryValidateBlob(status.artifactBundle,
                    HeavyComputeProtocol.DiffSoupArtifactMediaType,
                    HeavyComputeProtocol.DiffSoupArtifactVersion,
                    HeavyComputeProtocol.MaximumArtifactBytes, out error))
            {
                status = null;
                return false;
            }
            return true;
        }

        public static bool TryParseCapabilities(string json,
            out HeavyComputeCapabilities capabilities, out string error)
        {
            capabilities = null;
            error = null;
            if (string.IsNullOrEmpty(json) || json.Length > HeavyComputeProtocol.MaximumJsonCharacters)
            {
                error = "Capabilities JSON is empty or exceeds the size limit.";
                return false;
            }
            try { capabilities = JsonUtility.FromJson<HeavyComputeCapabilities>(json); }
            catch (Exception exception)
            {
                error = "Capabilities JSON is malformed: " + exception.Message;
                return false;
            }
            if (capabilities == null || capabilities.schemaVersion != HeavyComputeProtocol.Version ||
                !Contains(capabilities.protocolVersions, HeavyComputeProtocol.Version) ||
                !Contains(capabilities.chunkBundleFormatVersions,
                    HeavyComputeProtocol.ChunkBundleVersion) ||
                !Contains(capabilities.diffSoupArtifactFormatVersions,
                    HeavyComputeProtocol.DiffSoupArtifactVersion) ||
                !Contains(capabilities.backends, "diffsoup") ||
                capabilities.maximumUploadBytes <= 0 ||
                capabilities.maximumArtifactBytes <= 0)
            {
                error = "Server capabilities are incompatible with protocol v2.";
                capabilities = null;
                return false;
            }
            return true;
        }

        internal static bool TryValidateSubmission(HeavyComputeSubmission submission,
            bool requireComputedFields, out string error)
        {
            error = null;
            if (submission == null || submission.schemaVersion != HeavyComputeProtocol.Version ||
                !TryValidateKey(submission.key, out error) ||
                !TryValidateBlob(submission.inputBundle,
                    HeavyComputeProtocol.ChunkBundleMediaType,
                    HeavyComputeProtocol.ChunkBundleVersion,
                    HeavyComputeProtocol.MaximumUploadBytes, out error) ||
                !string.Equals(submission.backend, "diffsoup", StringComparison.Ordinal) ||
                !IsProfile(submission.profile))
            {
                error ??= "Heavy-compute submission is invalid.";
                return false;
            }
            if (submission.hasWarmStart)
            {
                HeavyComputeWarmStart warm = submission.warmStart;
                if (warm == null || warm.sourceRevision < 0 ||
                    warm.sourceRevision >= submission.key.chunkRevision ||
                    !IsLowerHexDigest(warm.compatibilityTag) ||
                    !TryValidateBlob(warm.checkpoint,
                        "application/vnd.questinfinitescan.diffsoup-checkpoint", 1,
                        HeavyComputeProtocol.MaximumArtifactBytes, out error))
                {
                    error ??= "Warm-start descriptor is invalid.";
                    return false;
                }
            }
            if (!requireComputedFields)
                return true;
            if (!IsLowerHexDigest(submission.jobId) ||
                !IsLowerHexDigest(submission.requestFingerprint) ||
                !string.Equals(submission.jobId, ComputeJobId(submission.key),
                    StringComparison.Ordinal) ||
                !string.Equals(submission.requestFingerprint,
                    ComputeRequestFingerprint(submission), StringComparison.Ordinal))
            {
                error = "Submission identity or fingerprint is inconsistent.";
                return false;
            }
            return true;
        }

        internal static bool TryValidateKey(HeavyComputeJobKey key, out string error)
        {
            error = null;
            if (key == null || !IsSafeIdentifier(key.worldId, 96) ||
                !IsSafeIdentifier(key.chunkId, 64) || key.chunkRevision < 0)
            {
                error = "Job key is invalid.";
                return false;
            }
            return true;
        }

        internal static bool TryValidateBlob(HeavyComputeBlobDescriptor blob,
            string mediaType, int formatVersion, long maximumBytes, out string error)
        {
            error = null;
            if (blob == null || !string.Equals(blob.mediaType, mediaType,
                    StringComparison.Ordinal) || blob.formatVersion != formatVersion ||
                blob.byteLength <= 0 || blob.byteLength > maximumBytes ||
                !IsLowerHexDigest(blob.sha256))
            {
                error = "Blob descriptor is invalid or unsupported.";
                return false;
            }
            return true;
        }

        internal static HeavyComputeRemoteState ParseRemoteState(string value)
        {
            return value switch
            {
                "awaiting_upload" => HeavyComputeRemoteState.AwaitingUpload,
                "queued" => HeavyComputeRemoteState.Queued,
                "running" => HeavyComputeRemoteState.Running,
                "succeeded" => HeavyComputeRemoteState.Succeeded,
                "failed" => HeavyComputeRemoteState.Failed,
                "canceled" => HeavyComputeRemoteState.Canceled,
                _ => HeavyComputeRemoteState.Unknown
            };
        }

        internal static bool IsLowerHexDigest(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
                return false;
            for (int i = 0; i < value.Length; i++)
                if (!(value[i] >= '0' && value[i] <= '9') &&
                    !(value[i] >= 'a' && value[i] <= 'f'))
                    return false;
            return true;
        }

        internal static bool IsSafeIdentifier(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumLength ||
                !IsAlphaNumeric(value[0]) || !IsAlphaNumeric(value[value.Length - 1]) ||
                value.Contains(".."))
                return false;
            for (int i = 0; i < value.Length; i++)
                if (!IsAlphaNumeric(value[i]) && value[i] != '-' &&
                    value[i] != '_' && value[i] != '.')
                    return false;
            return true;
        }

        private static string BuildImmutableCanonicalJson(HeavyComputeSubmission submission)
        {
            // This exact lexical order mirrors Python json.dumps(sort_keys=True,
            // separators=(",", ":"), ensure_ascii=True). All string fields have an
            // ASCII-only closed vocabulary or identifier grammar.
            var builder = new StringBuilder(768);
            builder.Append("{\"allowFreshFallback\":")
                .Append(submission.allowFreshFallback ? "true" : "false")
                .Append(",\"backend\":\"diffsoup\",\"inputBundle\":{")
                .Append("\"byteLength\":")
                .Append(submission.inputBundle.byteLength.ToString(CultureInfo.InvariantCulture))
                .Append(",\"formatVersion\":")
                .Append(submission.inputBundle.formatVersion.ToString(CultureInfo.InvariantCulture))
                .Append(",\"mediaType\":\"").Append(submission.inputBundle.mediaType)
                .Append("\",\"sha256\":\"").Append(submission.inputBundle.sha256)
                .Append("\"},\"key\":{\"chunkId\":\"").Append(submission.key.chunkId)
                .Append("\",\"chunkRevision\":")
                .Append(submission.key.chunkRevision.ToString(CultureInfo.InvariantCulture))
                .Append(",\"worldId\":\"").Append(submission.key.worldId)
                .Append("\"},\"profile\":\"").Append(submission.profile)
                .Append("\",\"schemaVersion\":")
                .Append(HeavyComputeProtocol.Version.ToString(CultureInfo.InvariantCulture))
                .Append(",\"warmStart\":");
            AppendWarmStartCanonical(builder,
                submission.hasWarmStart ? submission.warmStart : null);
            return builder.Append('}').ToString();
        }

        private static void AppendKey(StringBuilder builder, HeavyComputeJobKey key)
        {
            builder.Append("{\"worldId\":\"").Append(key.worldId)
                .Append("\",\"chunkId\":\"").Append(key.chunkId)
                .Append("\",\"chunkRevision\":")
                .Append(key.chunkRevision.ToString(CultureInfo.InvariantCulture)).Append('}');
        }

        private static void AppendBlob(StringBuilder builder, HeavyComputeBlobDescriptor blob)
        {
            builder.Append("{\"mediaType\":\"").Append(blob.mediaType)
                .Append("\",\"formatVersion\":")
                .Append(blob.formatVersion.ToString(CultureInfo.InvariantCulture))
                .Append(",\"byteLength\":")
                .Append(blob.byteLength.ToString(CultureInfo.InvariantCulture))
                .Append(",\"sha256\":\"").Append(blob.sha256).Append("\"}");
        }

        private static void AppendWarmStart(StringBuilder builder, HeavyComputeWarmStart warm)
        {
            if (warm == null)
            {
                builder.Append("null");
                return;
            }
            builder.Append("{\"sourceRevision\":")
                .Append(warm.sourceRevision.ToString(CultureInfo.InvariantCulture))
                .Append(",\"compatibilityTag\":\"").Append(warm.compatibilityTag)
                .Append("\",\"checkpoint\":");
            AppendBlob(builder, warm.checkpoint);
            builder.Append('}');
        }

        private static void AppendWarmStartCanonical(StringBuilder builder,
            HeavyComputeWarmStart warm)
        {
            if (warm == null)
            {
                builder.Append("null");
                return;
            }
            builder.Append("{\"checkpoint\":{")
                .Append("\"byteLength\":")
                .Append(warm.checkpoint.byteLength.ToString(CultureInfo.InvariantCulture))
                .Append(",\"formatVersion\":")
                .Append(warm.checkpoint.formatVersion.ToString(CultureInfo.InvariantCulture))
                .Append(",\"mediaType\":\"").Append(warm.checkpoint.mediaType)
                .Append("\",\"sha256\":\"").Append(warm.checkpoint.sha256)
                .Append("\"},\"compatibilityTag\":\"").Append(warm.compatibilityTag)
                .Append("\",\"sourceRevision\":")
                .Append(warm.sourceRevision.ToString(CultureInfo.InvariantCulture)).Append('}');
        }

        private static void AppendProperty(StringBuilder builder, string name, string value)
        {
            builder.Append('\"').Append(name).Append("\":\"").Append(value).Append('\"');
        }

        private static void AppendProperty(StringBuilder builder, string name, int value)
        {
            builder.Append('\"').Append(name).Append("\":")
                .Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static string Sha256(byte[] value)
        {
            using var algorithm = SHA256.Create();
            byte[] digest = algorithm.ComputeHash(value);
            var builder = new StringBuilder(64);
            for (int i = 0; i < digest.Length; i++)
            {
                builder.Append(Hex[digest[i] >> 4]);
                builder.Append(Hex[digest[i] & 15]);
            }
            return builder.ToString();
        }

        private static bool IsProfile(string value)
        {
            return value == "preview" || value == "balanced" || value == "quality";
        }

        private static bool IsAlphaNumeric(char value)
        {
            return value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z' ||
                   value >= '0' && value <= '9';
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Contains(int[] values, int expected)
        {
            return values != null && Array.IndexOf(values, expected) >= 0;
        }

        private static bool Contains(string[] values, string expected)
        {
            return values != null && Array.IndexOf(values, expected) >= 0;
        }

        private static bool HasTopLevelNull(string json, string property)
        {
            string marker = "\"" + property + "\"";
            int start = 0;
            while ((start = json.IndexOf(marker, start, StringComparison.Ordinal)) >= 0)
            {
                int cursor = start + marker.Length;
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
                if (cursor >= json.Length || json[cursor++] != ':')
                {
                    start += marker.Length;
                    continue;
                }
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
                return cursor + 4 <= json.Length &&
                       string.CompareOrdinal(json, cursor, "null", 0, 4) == 0;
            }
            return false;
        }
    }
}
