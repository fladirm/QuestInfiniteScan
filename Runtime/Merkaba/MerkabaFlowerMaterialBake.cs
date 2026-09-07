using System;
using System.IO;
using System.IO.Compression;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    // Presentation texture only. The fixed bake raster never chooses a skin
    // level, alters scan split masks, or introduces a geometric child vertex.
    internal static class MerkabaFlowerMaterialBake
    {
        internal const int Resolution = 256;

        internal static void Bake(MerkabaFlowerPresentation presentation,
            MerkabaFlowerPresentation.Carrier carrier, int wedge, bool color, bool normal,
            out byte[] capturedPng, out byte[] normalPng)
        {
            presentation.WedgeFrame(carrier, wedge, out _, out _, out _, out float3 du, out float3 dv);
            int n = Resolution, stride = 1 + 4 * n;
            var pixels = color ? new byte[checked(n * stride)] : null;
            var normals = normal ? new byte[checked(n * stride)] : null;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Reflect padding through the opposite triangle edge. It is
                // outside the raster primitive and only supplies filter taps.
                int u = x, v = y;
                if (u + v >= n) { u = n - 1 - x; v = n - 1 - y; }
                float3 bc = new float3(n - 1 - u - v, u, v) / (n - 1f);
                var sample = MerkabaSphereFlowerAuthority.EvaluateSkinDrawSignal(
                    carrier.SkinHeader, carrier.SkinSamples, wedge, bc, du, dv,
                    out _, out _, out float2 gradient);
                int offset = y * stride + 1 + 4 * x;
                if (color)
                {
                    pixels[offset] = Srgb(sample.CapturedRgb.x);
                    pixels[offset + 1] = Srgb(sample.CapturedRgb.y);
                    pixels[offset + 2] = Srgb(sample.CapturedRgb.z);
                    pixels[offset + 3] = 255;
                }
                if (normal)
                {
                    float3 micro = new float3(-gradient.x, -gradient.y, 1f) /
                        math.sqrt(1f + gradient.x * gradient.x + gradient.y * gradient.y);
                    normals[offset] = LinearByte(0.5f + 0.5f * micro.x);
                    normals[offset + 1] = LinearByte(0.5f + 0.5f * micro.y);
                    normals[offset + 2] = LinearByte(0.5f + 0.5f * micro.z);
                    normals[offset + 3] = 255;
                }
            }
            capturedPng = color ? Png(pixels, n, n) : null;
            normalPng = normal ? Png(normals, n, n) : null;
        }

        internal static bool HasChromaticDetail(MerkabaFlowerPresentation.Carrier carrier)
        {
            float3 root = carrier.SkinSamples[0].CapturedRgb;
            foreach (var sample in carrier.SkinSamples)
                if (math.any(math.asuint(sample.CapturedRgb) != math.asuint(root))) return true;
            return false;
        }

        internal static bool HasMetricDetail(MerkabaFlowerPresentation.Carrier carrier)
        {
            foreach (var sample in carrier.SkinSamples)
                if (sample.Amplitude3 != 0 || sample.Amplitude4 != 0 || sample.Amplitude5 != 0) return true;
            return false;
        }

        internal static byte LinearByte(float linear) => checked((byte)Math.Round(
            255.0 * math.clamp(linear, 0f, 1f), MidpointRounding.ToEven));

        private static byte Srgb(float value)
        {
            double linear = math.clamp(value, 0f, 1f);
            double encoded = linear <= 0.0031308 ? 12.92 * linear :
                1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
            return checked((byte)Math.Round(255.0 * encoded, MidpointRounding.ToEven));
        }

        private static byte[] Png(byte[] pixels, int width, int height)
        {
            using var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var ihdr = new byte[13];
            BigEndian(ihdr, 0, (uint)width); BigEndian(ihdr, 4, (uint)height);
            ihdr[8] = 8; ihdr[9] = 6;
            Chunk(png, "IHDR", ihdr);
            // DeflateStream supplies RFC1951; the PNG IDAT uses its RFC1950
            // zlib framing and Adler32, not a platform-specific image encoder.
            using var compressed = new MemoryStream();
            compressed.WriteByte(0x78); compressed.WriteByte(0x01);
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, true))
                deflate.Write(pixels, 0, pixels.Length);
            uint a = 1, b = 0;
            foreach (byte value in pixels) { a = (a + value) % 65521u; b = (b + a) % 65521u; }
            var checksum = new byte[4]; BigEndian(checksum, 0, (b << 16) | a);
            compressed.Write(checksum, 0, checksum.Length);
            Chunk(png, "IDAT", compressed.ToArray()); Chunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void Chunk(Stream destination, string type, byte[] payload)
        {
            var length = new byte[4]; BigEndian(length, 0, (uint)payload.Length);
            destination.Write(length, 0, length.Length);
            var bytes = new byte[checked(4 + payload.Length)];
            for (int i = 0; i < 4; i++) bytes[i] = (byte)type[i];
            Buffer.BlockCopy(payload, 0, bytes, 4, payload.Length);
            destination.Write(bytes, 0, bytes.Length);
            BigEndian(length, 0, MerkabaSphereFlowerPersistenceAbi.ComputeBytesCrc(bytes));
            destination.Write(length, 0, length.Length);
        }

        private static void BigEndian(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value;
        }
    }
}
