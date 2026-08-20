using System;
using System.IO;
using System.Threading;

namespace Genesis.RoomScan.Exporting
{
    /// <summary>
    /// Dependency-free deterministic RGBA8 PNG encoder. QRS texture byte arrays use
    /// row zero for UV v=0. glTF also defines (0,0) at the first (upper-left) encoded
    /// image row, so rows are intentionally emitted in source order without a flip.
    /// The zlib payload uses stored DEFLATE blocks: output is reproducible on every
    /// Unity platform and encoding never needs a second full-size texture allocation.
    /// </summary>
    internal static class DeterministicPngWriter
    {
        private static readonly byte[] Signature =
            { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly byte[] Ihdr = { 73, 72, 68, 82 };
        private static readonly byte[] Idat = { 73, 68, 65, 84 };
        private static readonly byte[] Iend = { 73, 69, 78, 68 };
        private const int MaximumStoredBlockBytes = 65_535;

        internal static bool TryGetEncodedLength(int width, int height,
            out long encodedLength, out string error)
        {
            encodedLength = 0;
            error = ValidateDimensions(width, height, out long rgbaBytes,
                out long filteredBytes);
            if (error != null)
                return false;
            try
            {
                long blockCount = checked((filteredBytes + MaximumStoredBlockBytes - 1L) /
                                          MaximumStoredBlockBytes);
                long idatLength = checked(2L + filteredBytes + blockCount * 5L + 4L);
                if (idatLength > uint.MaxValue)
                {
                    error = "PNG IDAT payload exceeds the PNG chunk limit.";
                    return false;
                }
                // signature + IHDR framing/payload + IDAT framing/payload + IEND framing
                encodedLength = checked(8L + 25L + 12L + idatLength + 12L);
                return rgbaBytes <= int.MaxValue;
            }
            catch (OverflowException)
            {
                error = "PNG encoded length overflowed.";
                return false;
            }
        }

        internal static bool TryWriteRgba8(Stream destination, byte[] rgba, int width,
            int height, CancellationToken cancellationToken, out long bytesWritten,
            out string error)
        {
            bytesWritten = 0;
            error = ValidateDimensions(width, height, out long rgbaBytes,
                out long filteredBytes);
            if (error != null)
                return false;
            if (rgba == null || rgba.LongLength != rgbaBytes)
            {
                error = "PNG dimensions do not match the RGBA8 source payload.";
                return false;
            }
            if (destination == null || !destination.CanWrite)
            {
                error = "PNG destination is not writable.";
                return false;
            }
            if (!TryGetEncodedLength(width, height, out long expectedLength, out error))
                return false;

            long start = destination.CanSeek ? destination.Position : 0L;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Write(Signature, 0, Signature.Length);

                byte[] header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 8; // bit depth
                header[9] = 6; // RGBA
                header[10] = 0; // DEFLATE
                header[11] = 0; // adaptive filtering
                header[12] = 0; // no interlace
                WriteChunk(destination, Ihdr, header, 0, header.Length);

                long blockCount = (filteredBytes + MaximumStoredBlockBytes - 1L) /
                                  MaximumStoredBlockBytes;
                long idatLength = checked(2L + filteredBytes + blockCount * 5L + 4L);
                WriteBigEndian(destination, (uint)idatLength);
                destination.Write(Idat, 0, Idat.Length);
                uint crc = Crc32.Begin();
                crc = Crc32.Update(crc, Idat, 0, Idat.Length);

                // 0x78 0x01 is a valid zlib header for DEFLATE with no compression.
                byte[] zlibHeader = { 0x78, 0x01 };
                WriteAndCrc(destination, zlibHeader, 0, zlibHeader.Length, ref crc);

                var source = new FilteredRgbaSource(rgba, width, height);
                byte[] block = new byte[MaximumStoredBlockBytes];
                long remaining = filteredBytes;
                uint adlerA = 1;
                uint adlerB = 0;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(remaining, MaximumStoredBlockBytes);
                    bool final = remaining == count;
                    byte[] storedHeader = new byte[5];
                    storedHeader[0] = final ? (byte)1 : (byte)0;
                    storedHeader[1] = (byte)count;
                    storedHeader[2] = (byte)(count >> 8);
                    int complement = (~count) & 0xFFFF;
                    storedHeader[3] = (byte)complement;
                    storedHeader[4] = (byte)(complement >> 8);
                    WriteAndCrc(destination, storedHeader, 0, storedHeader.Length, ref crc);

                    source.Fill(block, count);
                    UpdateAdler(block, count, ref adlerA, ref adlerB);
                    WriteAndCrc(destination, block, 0, count, ref crc);
                    remaining -= count;
                }

                uint adler = (adlerB << 16) | adlerA;
                byte[] adlerBytes = new byte[4];
                WriteBigEndian(adlerBytes, 0, adler);
                WriteAndCrc(destination, adlerBytes, 0, adlerBytes.Length, ref crc);
                WriteBigEndian(destination, Crc32.End(crc));

                WriteChunk(destination, Iend, Array.Empty<byte>(), 0, 0);
                bytesWritten = destination.CanSeek
                    ? destination.Position - start
                    : expectedLength;
                if (bytesWritten != expectedLength)
                {
                    error = $"PNG length mismatch: expected {expectedLength}, wrote " +
                            $"{bytesWritten}.";
                    return false;
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                error = "PNG encoding was canceled.";
                return false;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is OverflowException ||
                                              exception is ObjectDisposedException ||
                                              exception is NotSupportedException)
            {
                error = "PNG encoding failed: " + exception.Message;
                return false;
            }
        }

        private static string ValidateDimensions(int width, int height,
            out long rgbaBytes, out long filteredBytes)
        {
            rgbaBytes = 0;
            filteredBytes = 0;
            if (width <= 0 || height <= 0 || width > 8_192 || height > 8_192)
                return "PNG dimensions must be in [1, 8192].";
            try
            {
                rgbaBytes = checked((long)width * height * 4L);
                filteredBytes = checked((long)height * (checked((long)width * 4L) + 1L));
                if (rgbaBytes > 256L * 1024L * 1024L || rgbaBytes > int.MaxValue)
                    return "PNG RGBA8 source exceeds the 256 MiB texture limit.";
                return null;
            }
            catch (OverflowException)
            {
                return "PNG dimensions overflowed.";
            }
        }

        private static void WriteChunk(Stream destination, byte[] type, byte[] payload,
            int offset, int count)
        {
            WriteBigEndian(destination, (uint)count);
            destination.Write(type, 0, type.Length);
            if (count > 0)
                destination.Write(payload, offset, count);
            uint crc = Crc32.Begin();
            crc = Crc32.Update(crc, type, 0, type.Length);
            crc = Crc32.Update(crc, payload, offset, count);
            WriteBigEndian(destination, Crc32.End(crc));
        }

        private static void WriteAndCrc(Stream destination, byte[] bytes, int offset,
            int count, ref uint crc)
        {
            destination.Write(bytes, offset, count);
            crc = Crc32.Update(crc, bytes, offset, count);
        }

        private static void UpdateAdler(byte[] bytes, int count, ref uint a, ref uint b)
        {
            const uint modulus = 65_521;
            int offset = 0;
            while (offset < count)
            {
                int batch = Math.Min(5_552, count - offset);
                int end = offset + batch;
                for (; offset < end; offset++)
                {
                    a += bytes[offset];
                    b += a;
                }
                a %= modulus;
                b %= modulus;
            }
        }

        private static void WriteBigEndian(Stream destination, uint value)
        {
            byte[] bytes = new byte[4];
            WriteBigEndian(bytes, 0, value);
            destination.Write(bytes, 0, bytes.Length);
        }

        private static void WriteBigEndian(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }

        private sealed class FilteredRgbaSource
        {
            private readonly byte[] _rgba;
            private readonly int _rowBytes;
            private readonly int _height;
            private int _row;
            private int _column = -1;

            internal FilteredRgbaSource(byte[] rgba, int width, int height)
            {
                _rgba = rgba;
                _rowBytes = checked(width * 4);
                _height = height;
            }

            internal void Fill(byte[] destination, int count)
            {
                int written = 0;
                while (written < count)
                {
                    if (_row >= _height)
                        throw new EndOfStreamException("PNG filtered source was exhausted.");
                    if (_column < 0)
                    {
                        destination[written++] = 0; // filter type None
                        _column = 0;
                        continue;
                    }
                    int copy = Math.Min(count - written, _rowBytes - _column);
                    Buffer.BlockCopy(_rgba, _row * _rowBytes + _column,
                        destination, written, copy);
                    _column += copy;
                    written += copy;
                    if (_column == _rowBytes)
                    {
                        _row++;
                        _column = -1;
                    }
                }
            }
        }

        private static class Crc32
        {
            private static readonly uint[] Table = BuildTable();

            internal static uint Begin() => uint.MaxValue;
            internal static uint End(uint crc) => crc ^ uint.MaxValue;

            internal static uint Update(uint crc, byte[] bytes, int offset, int count)
            {
                for (int i = 0; i < count; i++)
                    crc = Table[(crc ^ bytes[offset + i]) & 0xFF] ^ (crc >> 8);
                return crc;
            }

            private static uint[] BuildTable()
            {
                var table = new uint[256];
                for (uint i = 0; i < table.Length; i++)
                {
                    uint value = i;
                    for (int bit = 0; bit < 8; bit++)
                        value = (value & 1) != 0
                            ? 0xEDB88320u ^ (value >> 1)
                            : value >> 1;
                    table[i] = value;
                }
                return table;
            }
        }
    }
}
