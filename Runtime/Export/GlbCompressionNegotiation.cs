using System;
using System.Collections.Generic;

namespace Genesis.RoomScan.Exporting
{
    public enum GlbCompressionRequirement
    {
        Disabled = 0,
        Prefer = 1,
        Require = 2
    }

    public sealed class GlbCompressionRequest
    {
        public GlbCompressionRequirement Meshopt { get; set; } =
            GlbCompressionRequirement.Disabled;
        public GlbCompressionRequirement Ktx2 { get; set; } =
            GlbCompressionRequirement.Disabled;
        public IReadOnlyList<string> ConsumerExtensions { get; set; } =
            Array.Empty<string>();
    }

    /// <summary>
    /// Capabilities are evidence, not feature flags. An encoder is usable only when a probe
    /// verified the concrete implementation and supplied a non-empty implementation identity.
    /// </summary>
    public sealed class GlbCompressionRuntimeCapabilities
    {
        public static GlbCompressionRuntimeCapabilities BaselineOnly => new();
        public bool MeshoptEncoderVerified { get; set; }
        public string MeshoptImplementationId { get; set; }
        public bool Ktx2EncoderVerified { get; set; }
        public string Ktx2ImplementationId { get; set; }
    }

    public sealed class GlbCompressionSelection
    {
        public bool Success { get; internal set; }
        public bool UseMeshopt { get; internal set; }
        public bool UseKtx2 { get; internal set; }
        public string MeshoptImplementationId { get; internal set; }
        public string Ktx2ImplementationId { get; internal set; }
        public string FallbackReason { get; internal set; }
        public string Error { get; internal set; }
    }

    public static class GlbCompressionNegotiator
    {
        public const string MeshoptExtension = "EXT_meshopt_compression";
        public const string Ktx2Extension = "KHR_texture_basisu";

        public static GlbCompressionSelection Negotiate(GlbCompressionRequest request,
            GlbCompressionRuntimeCapabilities capabilities)
        {
            request ??= new GlbCompressionRequest();
            capabilities ??= GlbCompressionRuntimeCapabilities.BaselineOnly;
            if (!Enum.IsDefined(typeof(GlbCompressionRequirement), request.Meshopt) ||
                !Enum.IsDefined(typeof(GlbCompressionRequirement), request.Ktx2))
                return Failure("Compression request contains an unsupported requirement.");

            var consumer = new HashSet<string>(StringComparer.Ordinal);
            if (request.ConsumerExtensions != null)
            {
                for (int i = 0; i < request.ConsumerExtensions.Count; i++)
                {
                    string extension = request.ConsumerExtensions[i];
                    if (string.IsNullOrWhiteSpace(extension) || extension.Length > 128)
                        return Failure("Consumer extension declarations are invalid.");
                    consumer.Add(extension);
                }
            }

            bool meshoptAvailable = capabilities.MeshoptEncoderVerified &&
                                    !string.IsNullOrWhiteSpace(
                                        capabilities.MeshoptImplementationId);
            bool ktx2Available = capabilities.Ktx2EncoderVerified &&
                                !string.IsNullOrWhiteSpace(capabilities.Ktx2ImplementationId);
            bool meshoptSupported = consumer.Contains(MeshoptExtension);
            bool ktx2Supported = consumer.Contains(Ktx2Extension);

            var fallbacks = new List<string>();
            bool useMeshopt = Resolve("meshopt", request.Meshopt, meshoptAvailable,
                meshoptSupported, fallbacks, out string error);
            if (error != null) return Failure(error);
            bool useKtx2 = Resolve("KTX2", request.Ktx2, ktx2Available,
                ktx2Supported, fallbacks, out error);
            if (error != null) return Failure(error);

            return new GlbCompressionSelection
            {
                Success = true,
                UseMeshopt = useMeshopt,
                UseKtx2 = useKtx2,
                MeshoptImplementationId = useMeshopt
                    ? capabilities.MeshoptImplementationId
                    : null,
                Ktx2ImplementationId = useKtx2
                    ? capabilities.Ktx2ImplementationId
                    : null,
                FallbackReason = fallbacks.Count == 0 ? null : string.Join("; ", fallbacks)
            };
        }

        private static bool Resolve(string label, GlbCompressionRequirement requirement,
            bool encoderAvailable, bool consumerSupported, List<string> fallbacks,
            out string error)
        {
            error = null;
            if (requirement == GlbCompressionRequirement.Disabled)
                return false;
            if (encoderAvailable && consumerSupported)
                return true;
            string reason = !encoderAvailable
                ? $"{label} encoder was not verified"
                : $"consumer did not declare {label} extension support";
            if (requirement == GlbCompressionRequirement.Require)
            {
                error = $"Required {label} compression is unavailable: {reason}. " +
                        "Use baseline GLB or install/probe an encoder and declare consumer support.";
                return false;
            }
            fallbacks.Add(reason + "; baseline GLB selected");
            return false;
        }

        private static GlbCompressionSelection Failure(string error) => new()
        {
            Error = error,
            Success = false
        };
    }
}
