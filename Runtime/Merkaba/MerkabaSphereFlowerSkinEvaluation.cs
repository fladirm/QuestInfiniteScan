using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    // Exactly the aligned GPU hot sample. This is derived presentation state;
    // RGB and Q5.26 V intervals remain separate persistent authorities.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerSkinDrawSample
    {
        internal const int ByteSize = 64;
        internal float3 CapturedRgb;
        internal uint Flags;
        internal int Amplitude3;
        internal int Amplitude4;
        internal int Amplitude5;
        internal uint Reserved;
        internal float4 Optical;
        internal float4 CaptureView;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerSkinDrawHeader
    {
        internal const int ByteSize = 16;
        internal uint SplitBitsLo;
        internal uint SplitBitsHi;
        internal uint FirstSample;
        internal uint ParentEpoch;
    }

    public static partial class MerkabaSphereFlowerAuthority
    {
        internal static MerkabaFlowerSkinDrawSample[] CompileSkinDrawSamples(
            float3 capturedRoot, uint parentEpoch,
            MerkabaThreadRun? rgbRun,
            IReadOnlyList<MerkabaThreadColorGroup> rgbGroups,
            MerkabaFlowerSkinMetricRun? vRun,
            IReadOnlyList<MerkabaFlowerVGroup> vGroups,
            MerkabaThreadProgramRecord? optical,
            out MerkabaFlowerSkinDrawHeader header)
        {
            if (!math.all(math.isfinite(capturedRoot)))
                throw new ArgumentOutOfRangeException(nameof(capturedRoot));
            // A stale run is absent, not an alternate older signal.
            bool rgb = rgbRun.HasValue && parentEpoch != 0u &&
                rgbRun.Value.ParentEpoch == parentEpoch;
            bool metric = vRun.HasValue && parentEpoch != 0u &&
                vRun.Value.ParentEpoch == parentEpoch;
            if (rgb && (!rgbRun.Value.IsValidFor(parentEpoch) ||
                        rgbGroups == null || rgbGroups.Count != rgbRun.Value.GroupCount))
                throw new ArgumentException("RGB run/group publication is incomplete.");
            if (metric && (!vRun.Value.IsValidFor(parentEpoch) ||
                           vGroups == null || vGroups.Count != vRun.Value.GroupCount))
                throw new ArgumentException("V run/group publication is incomplete.");
            if (rgb && metric && rgbRun.Value.FlowerKey != vRun.Value.FlowerKey)
                throw new ArgumentException("RGB/V runs address different L2 carriers.");
            uint rgbLow = rgb ? rgbRun.Value.SplitBitsLo : 0u;
            uint rgbHigh = rgb ? rgbRun.Value.SplitBitsHi : 0u;
            uint vLow = metric ? vRun.Value.SplitBitsLo : 0u;
            uint vHigh = metric ? vRun.Value.SplitBitsHi : 0u;
            uint low = rgbLow | vLow;
            uint high = rgbHigh | vHigh;
            int groups = MerkabaFlowerSkinSplitBits.GroupCount(low, high);
            if (groups < 0)
                throw new ArgumentException("RGB/V union contains an orphan split.");
            header = new MerkabaFlowerSkinDrawHeader
            {
                SplitBitsLo = low, SplitBitsHi = high,
                FirstSample = 0u, ParentEpoch = parentEpoch
            };
            var root = new MerkabaFlowerSkinDrawSample
            {
                CapturedRgb = capturedRoot
            };
            if (rgb && rgbRun.Value.ProgramRef != uint.MaxValue)
            {
                if (!optical.HasValue || !optical.Value.HasCanonicalIntervals())
                    throw new ArgumentException("Thread optical program is missing or invalid.");
                if (optical.Value.OpticalValid)
                {
                    root.Flags = 1u;
                    float4 opticalLower = optical.Value.OpticalLower;
                    float4 opticalUpper = optical.Value.OpticalUpper;
                    float4 viewLower = optical.Value.CaptureViewLower;
                    float4 viewUpper = optical.Value.CaptureViewUpper;
                    root.Optical = opticalLower +
                        (opticalUpper - opticalLower) * 0.5f;
                    root.CaptureView = viewLower +
                        (viewUpper - viewLower) * 0.5f;
                }
            }
            var result = new MerkabaFlowerSkinDrawSample[1 + 7 * groups];
            result[0] = root;
            // Visit only explicit union groups. Uniform L2 allocates one
            // sample; no 399-value expansion or runtime Fibonacci exists.
            if (!MerkabaFlowerSkinSplitBits.SplitL2(low)) return result;
            for (int c3 = 0; c3 < 7; c3++)
            {
                MerkabaFlowerSkinDrawSample child3 = WithSkinInnovation(root,
                    3, c3, 0, 0, rgbLow, rgbHigh, rgbGroups, vLow, vHigh, vGroups);
                StoreSkinSample(result, low, high, 3, c3, 0, 0, child3);
                int j3 = SkinL3ParentThreadIndex(c3);
                if ((MerkabaFlowerSkinSplitBits.SplitL3Thread(low) &
                     (1u << j3)) == 0u) continue;
                for (int c4 = 0; c4 < 7; c4++)
                {
                    MerkabaFlowerSkinDrawSample child4 = WithSkinInnovation(child3,
                        4, c3, c4, 0, rgbLow, rgbHigh, rgbGroups, vLow, vHigh, vGroups);
                    StoreSkinSample(result, low, high, 4, c3, c4, 0, child4);
                    int j4 = SkinL4ParentThreadIndex(c3, c4);
                    uint word = j4 < 32
                        ? MerkabaFlowerSkinSplitBits.SplitL4Low(low, high)
                        : MerkabaFlowerSkinSplitBits.SplitL4High(high);
                    if ((word & (1u << (j4 & 31))) == 0u) continue;
                    for (int c5 = 0; c5 < 7; c5++)
                        StoreSkinSample(result, low, high, 5, c3, c4, c5,
                            WithSkinInnovation(child4, 5, c3, c4, c5,
                                rgbLow, rgbHigh, rgbGroups, vLow, vHigh, vGroups));
                }
            }
            return result;
        }

        private static MerkabaFlowerSkinDrawSample WithSkinInnovation(
            MerkabaFlowerSkinDrawSample parent, int level, int c3, int c4, int c5,
            uint rgbLow, uint rgbHigh, IReadOnlyList<MerkabaThreadColorGroup> rgb,
            uint vLow, uint vHigh, IReadOnlyList<MerkabaFlowerVGroup> metric)
        {
            if (MerkabaFlowerSkinSplitBits.TryCompactChildAddress(0u,
                    rgbLow, rgbHigh, level, c3, c4, c5, out uint colorGroup,
                    out int colorRank))
            {
                MerkabaThreadColorInterval value = rgb[checked((int)colorGroup)]
                    .Child(colorRank);
                if (!value.IsCanonical)
                    throw new ArgumentException("RGB child interval is not canonical.");
                float4 lower = value.LowerLinearRgba;
                float4 upper = value.UpperLinearRgba;
                parent.CapturedRgb = lower.xyz + (upper.xyz - lower.xyz) * 0.5f;
            }
            if (MerkabaFlowerSkinSplitBits.TryCompactChildAddress(0u,
                    vLow, vHigh, level, c3, c4, c5, out uint metricGroup,
                    out int metricRank))
            {
                MerkabaFlowerVInterval value = metric[checked((int)metricGroup)]
                    .Child(metricRank);
                if (!value.IsCanonical)
                    throw new ArgumentException("V child interval is not canonical.");
                if (level == 3) parent.Amplitude3 = value.DrawMidpoint;
                else if (level == 4) parent.Amplitude4 = value.DrawMidpoint;
                else parent.Amplitude5 = value.DrawMidpoint;
            }
            return parent;
        }

        private static void StoreSkinSample(MerkabaFlowerSkinDrawSample[] samples,
            uint low, uint high, int level, int c3, int c4, int c5,
            MerkabaFlowerSkinDrawSample sample)
        {
            if (!MerkabaFlowerSkinSplitBits.TryCompactChildAddress(0u, low,
                    high, level, c3, c4, c5, out uint group, out int rank))
                throw new InvalidOperationException("Explicit union group is missing.");
            samples[checked(1 + 7 * (int)group + rank)] = sample;
        }

        internal static MerkabaFlowerSkinDrawSample EvaluateSkinDrawSignal(
            in MerkabaFlowerSkinDrawHeader header,
            IReadOnlyList<MerkabaFlowerSkinDrawSample> samples,
            int wedge, float3 barycentric, float3 barycentricDu,
            float3 barycentricDv, out int3 locality, out float microHeight,
            out float2 microGradient)
        {
            if (!MerkabaFlowerSkinSplitBits.IsCanonical(header.SplitBitsLo,
                    header.SplitBitsHi))
                throw new ArgumentException("Noncanonical draw split mask.");
            int count = 1 + 7 * MerkabaFlowerSkinSplitBits.GroupCount(
                header.SplitBitsLo, header.SplitBitsHi);
            if (samples == null || (ulong)header.FirstSample + (uint)count >
                (ulong)samples.Count)
                throw new ArgumentException("Draw sample range is incomplete.");
            SkinChamberRule rule3 = DescendSkin(barycentric, wedge, out float3 bc3);
            float3 du3 = ApplySkinChamber(barycentricDu, rule3);
            float3 dv3 = ApplySkinChamber(barycentricDv, rule3);
            SkinChamberRule rule4 = DescendSkin(bc3, rule3.ChildWedge, out float3 bc4);
            float3 du4 = ApplySkinChamber(du3, rule4);
            float3 dv4 = ApplySkinChamber(dv3, rule4);
            SkinChamberRule rule5 = DescendSkin(bc4, rule4.ChildWedge, out float3 bc5);
            float3 du5 = ApplySkinChamber(du4, rule5);
            float3 dv5 = ApplySkinChamber(dv4, rule5);
            locality = new int3(rule3.ChildSite, rule4.ChildSite, rule5.ChildSite);
            uint index = header.FirstSample;
            // These are scan-authored mask reads, never presentation LOD.
            for (int level = 3; level <= 5; level++)
                if (MerkabaFlowerSkinSplitBits.TryCompactChildAddress(0u,
                        header.SplitBitsLo, header.SplitBitsHi, level,
                        locality.x, locality.y, locality.z,
                        out uint group, out int rank))
                    index = checked(header.FirstSample + 1u + 7u * group + (uint)rank);
            MerkabaFlowerSkinDrawSample sample = samples[checked((int)index)];
            const float q26 = 1f / 67108864f;
            float3 amplitudes = new float3(sample.Amplitude3, sample.Amplitude4,
                sample.Amplitude5) * q26;
            microHeight = EvaluateNestedSkinV(bc3, bc4, bc5, amplitudes);
            float3 g3 = SkinBubbleGradient(bc3);
            float3 g4 = SkinBubbleGradient(bc4);
            float3 g5 = SkinBubbleGradient(bc5);
            microGradient = new float2(
                amplitudes.x * math.dot(g3, du3) + amplitudes.y * math.dot(g4, du4) +
                amplitudes.z * math.dot(g5, du5),
                amplitudes.x * math.dot(g3, dv3) + amplitudes.y * math.dot(g4, dv4) +
                amplitudes.z * math.dot(g5, dv5));
            return sample;
        }

        private static float3 ApplySkinChamber(float3 value, SkinChamberRule rule) =>
            new(value[rule.High] - value[rule.Middle],
                2f * (value[rule.Middle] - value[rule.Low]), 3f * value[rule.Low]);

        public static float3 SkinMicroNormal(float3 tangent1, float3 tangent2,
            float3 normal, float2 gradient)
        {
            // The caller supplies the evaluated orthonormal L2 world frame.
            float3 value = normal - gradient.x * tangent1 - gradient.y * tangent2;
            return value / math.sqrt(1f + gradient.x * gradient.x + gradient.y * gradient.y);
        }

        // V-1 is an explicitly enabled presentation policy, not an optical
        // certificate or an estimate of capture lighting. Keep the ordered
        // operations identical to M8FlowerRelativeDiffuse. Default/off returns
        // the original bits without evaluating either normal or light.
        public static float3 RelativeDiffuse(float3 capturedRgb, uint sampleFlags,
            float3 normal, float3 microNormal, float4 presentationLight)
        {
            if (presentationLight.w != 1f || (sampleFlags & 1u) == 0u)
                return capturedRgb;
            float squared = presentationLight.x * presentationLight.x +
                presentationLight.y * presentationLight.y;
            squared = squared + presentationLight.z * presentationLight.z;
            if (!(squared > 0f) || !math.isfinite(squared)) return capturedRgb;
            float length = math.sqrt(squared);
            float3 light = presentationLight.xyz / length;
            float e0 = normal.x * light.x + normal.y * light.y;
            e0 = e0 + normal.z * light.z;
            // Exactly 2^-5: an explicit V-1 presentation floor, never an
            // epsilon added to reconstruction or a proof of OPTICAL_VALID.
            if (!(e0 > 0.03125f)) return capturedRgb;
            float ef = microNormal.x * light.x + microNormal.y * light.y;
            ef = ef + microNormal.z * light.z;
            float response = math.saturate(math.max(ef, 0f) / e0);
            return capturedRgb * response;
        }
    }
}
