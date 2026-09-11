using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    // Frozen-export presentation of the shared RGB/V evaluator. Atlas sampling
    // never chooses scan subdivision or changes a canonical L2 position.
    internal static class MerkabaFlowerMaterialBake
    {
        internal const int Resolution = 2048;
        internal const int Gutter = 2;
        internal const int MaximumCellSize = 64;
        private const int ReceiptBytes = 12 + 2 * 32;

        private readonly struct NormalFrame
        {
            internal readonly float3 Tangent1, Tangent2, Normal, Du, Dv, TextureU, TextureV;

            internal NormalFrame(MerkabaFlowerPresentation presentation,
                MerkabaFlowerPresentation.Carrier carrier, int wedge)
            {
                presentation.WedgeFrame(carrier, wedge, out float3 t1, out float3 t2,
                    out float3 normal, out float3 du, out float3 dv);
                if ((carrier.Symbol.ReverseWedgeMask & (1u << wedge)) != 0u)
                { normal = -normal; t2 = -t2; dv = -dv; }
                Tangent1 = t1; Tangent2 = t2; Normal = normal; Du = du; Dv = dv;
                float2 a = MerkabaSphereFlowerAuthority.L2CarrierChartSite(1 + wedge);
                float2 b = MerkabaSphereFlowerAuthority.L2CarrierChartSite(1 + (wedge + 1) % 6);
                float3 e1 = presentation.Position(carrier, 1 + wedge) - presentation.Position(carrier, 0);
                float3 e2 = presentation.Position(carrier, 1 + (wedge + 1) % 6) - presentation.Position(carrier, 0);
                // The chart determinant is exactly +1 for every wedge. UV
                // scale is positive and isotropic; it cancels on normalization.
                TextureU = math.normalize(e1 * b.y - e2 * a.y);
                float3 bitangent = math.cross(normal, TextureU);
                TextureV = math.dot(bitangent, e2 * a.x - e1 * b.x) < 0f ? -bitangent : bitangent;
            }

            internal float3 Evaluate(float2 gradient)
            {
                float3 micro = MerkabaSphereFlowerAuthority.SkinMicroNormal(
                    Tangent1, Tangent2, Normal, gradient);
                // Reflecting X for glTF preserves these dot products. Its
                // generated flat-normal/MikkTSpace frame has the same UV axes.
                return new float3(math.dot(micro, TextureU), math.dot(micro, TextureV),
                    math.dot(micro, Normal));
            }
        }

        [Serializable]
        internal sealed class ResumeState
        {
            public int pages;
            public int[] current, used;
            public long receipts;
        }

        internal readonly struct Cell
        {
            internal readonly int Page, Size, X, Y;
            internal Cell(int page, int size, int ordinal)
            {
                Page = page; Size = size;
                int perRow = Resolution / size;
                X = (ordinal % perRow) * size;
                Y = (ordinal / perRow) * size;
            }

            internal float2 Uv(int site) =>
                (new float2(X + Gutter, Y + Gutter) +
                 (MerkabaSphereFlowerAuthority.L2CarrierChartSite(site) * 0.5f + 0.5f) *
                 (Size - 2 * Gutter)) / Resolution;
        }

        // Three size-class cursors, not three resident pixel pages. RGB/V,
        // page sizes and final PNGs are spooled; only one <=64² cell pair and one
        // PNG row/IDAT packet are resident. No world-sized page/image list.
        internal sealed class Atlas : IDisposable
        {
            private readonly string _directory;
            private readonly FileStream _sizes;
            private readonly FileStream _receipts;
            private readonly int[] _current = { -1, -1, -1 };
            private readonly int[] _used = new int[3];
            private readonly byte[] _cell = new byte[MaximumCellSize * MaximumCellSize * 4];
            private readonly byte[] _normalCell = new byte[MaximumCellSize * MaximumCellSize * 4];
            private readonly int[] _durableCurrent = { -1, -1, -1 };
            private int _durablePages;
            internal int PageCount { get; private set; }

            internal Atlas(string directory, ResumeState resume = null,
                CancellationToken cancellationToken = default)
            {
                _directory = directory;
                _sizes = new FileStream(Path.Combine(directory, "atlas-sizes.bin"),
                    resume == null ? FileMode.CreateNew : FileMode.Open,
                    FileAccess.ReadWrite, FileShare.Read, 4096);
                try
                {
                    _receipts = new FileStream(Path.Combine(directory, "atlas-cells.bin"),
                        resume == null ? FileMode.CreateNew : FileMode.Open,
                        FileAccess.ReadWrite, FileShare.Read, 4096);
                    if (resume != null) Restore(resume, cancellationToken);
                }
                catch { _receipts?.Dispose(); _sizes.Dispose(); throw; }
            }

            internal Cell Append(MerkabaFlowerPresentation presentation, MerkabaFlowerPresentation.Carrier carrier,
                CancellationToken cancellationToken)
            {
                int size = CellSize(carrier);
                Span<NormalFrame> frames = stackalloc NormalFrame[6];
                for (int wedge = 0; wedge < 6; wedge++)
                    if ((carrier.Symbol.ActiveWedgeMask & (1u << wedge)) != 0u)
                        frames[wedge] = new NormalFrame(presentation, carrier, wedge);
                int sizeClass = size == 16 ? 0 : size == 32 ? 1 : 2;
                int perRow = Resolution / size;
                if (_current[sizeClass] < 0 || _used[sizeClass] == perRow * perRow)
                {
                    int page = PageCount;
                    for (int channel = 0; channel < 2; channel++)
                        using (var pixels = new FileStream(PagePath(page, channel != 0), FileMode.CreateNew,
                            FileAccess.Write, FileShare.None, 4096))
                            pixels.SetLength(checked((long)Resolution * Resolution * 4));
                    _sizes.Position = page;
                    _sizes.WriteByte((byte)size);
                    PageCount = checked(PageCount + 1);
                    _current[sizeClass] = page;
                    _used[sizeClass] = 0;
                }
                var cell = new Cell(_current[sizeClass], size, _used[sizeClass]);
                for (int y = 0; y < size; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = 0; x < size; x++)
                    {
                        // Texel centres and the UV rectangle use exactly the
                        // same cell/gutter convention. PNG row zero is chart
                        // v=-1; glTF UVs retain that row convention.
                        float2 chart = new float2(
                            (float)(2.0 * (x + 0.5 - Gutter) / (size - 2 * Gutter) - 1.0),
                            (float)(2.0 * (y + 0.5 - Gutter) / (size - 2 * Gutter) - 1.0));
                        ChartSample(chart, carrier.Symbol.ActiveWedgeMask, out int wedge, out float3 bc);
                        NormalFrame frame = frames[wedge];
                        var sample = MerkabaSphereFlowerAuthority.EvaluateSkinDrawSignal(
                            carrier.SkinHeader, carrier.SkinSamples, wedge, bc,
                            frame.Du, frame.Dv, out _, out _, out float2 gradient);
                        float3 normal = frame.Evaluate(gradient);
                        if (!math.all(math.isfinite(normal)))
                            throw new InvalidDataException("Nonfinite Flower V normal.");
                        int offset = 4 * (y * size + x);
                        _cell[offset] = Srgb(sample.CapturedRgb.x);
                        _cell[offset + 1] = Srgb(sample.CapturedRgb.y);
                        _cell[offset + 2] = Srgb(sample.CapturedRgb.z);
                        _cell[offset + 3] = 255;
                        _normalCell[offset] = LinearByte(0.5f + 0.5f * normal.x);
                        _normalCell[offset + 1] = LinearByte(0.5f + 0.5f * normal.y);
                        _normalCell[offset + 2] = LinearByte(0.5f + 0.5f * normal.z);
                        _normalCell[offset + 3] = 255;
                    }
                }
                for (int channel = 0; channel < 2; channel++)
                {
                    byte[] bytes = channel == 0 ? _cell : _normalCell;
                    using var pixels = new FileStream(PagePath(cell.Page, channel != 0), FileMode.Open,
                        FileAccess.Write, FileShare.None, 4096);
                    for (int y = 0; y < size; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        pixels.Position = checked(4L * ((long)(cell.Y + y) * Resolution + cell.X));
                        pixels.Write(bytes, y * size * 4, size * 4);
                    }
                }
                // Pixels are immutable within an allocated cell. This bounded
                // receipt hashes each cell once, not the whole atlas per tile.
                using (var writer = new BinaryWriter(_receipts, System.Text.Encoding.UTF8, true))
                using (SHA256 sha = SHA256.Create())
                {
                    writer.Write(cell.Page); writer.Write(cell.Size); writer.Write(_used[sizeClass]);
                    writer.Write(sha.ComputeHash(_cell, 0, size * size * 4));
                    writer.Write(sha.ComputeHash(_normalCell, 0, size * size * 4));
                    writer.Flush();
                }
                _used[sizeClass]++;
                return cell;
            }

            internal ResumeState Checkpoint(CancellationToken token)
            {
                // Only the previous three active pages and newly allocated
                // pages may have changed since the last durable checkpoint.
                for (int i = 0; i < 3; i++)
                    if (_durableCurrent[i] >= 0) FlushPage(_durableCurrent[i], token);
                for (int page = _durablePages; page < PageCount; page++) FlushPage(page, token);
                _sizes.Flush(true); _receipts.Flush(true);
                Array.Copy(_current, _durableCurrent, 3); _durablePages = PageCount;
                return new ResumeState { pages = PageCount, current = (int[])_current.Clone(),
                    used = (int[])_used.Clone(), receipts = _receipts.Length };
            }

            private void FlushPage(int page, CancellationToken token)
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    token.ThrowIfCancellationRequested();
                    using var file = new FileStream(PagePath(page, channel != 0), FileMode.Open,
                        FileAccess.Write, FileShare.None, 4096);
                    file.Flush(true);
                }
            }

            private void Restore(ResumeState state, CancellationToken token)
            {
                if (state.pages < 0 || state.current?.Length != 3 || state.used?.Length != 3 ||
                    state.receipts < 0 || state.receipts % ReceiptBytes != 0 ||
                    _sizes.Length != state.pages || _receipts.Length != state.receipts)
                    throw new InvalidDataException("Invalid atlas resume receipt.");
                PageCount = state.pages;
                int pages = 0;
                using var reader = new BinaryReader(_receipts, System.Text.Encoding.UTF8, true);
                while (_receipts.Position < _receipts.Length)
                {
                    token.ThrowIfCancellationRequested();
                    int page = reader.ReadInt32(), size = reader.ReadInt32(), ordinal = reader.ReadInt32();
                    byte[] expectedRgb = reader.ReadBytes(32), expectedV = reader.ReadBytes(32);
                    if ((size != 16 && size != 32 && size != 64) || page < 0 || page >= PageCount ||
                        PageCellSize(page) != size) throw new InvalidDataException("Invalid atlas cell receipt.");
                    int sizeClass = size == 16 ? 0 : size == 32 ? 1 : 2;
                    int perRow = Resolution / size, count = perRow * perRow;
                    if (_current[sizeClass] < 0 || _used[sizeClass] == count)
                    {
                        if (page != pages++) throw new InvalidDataException("Noncanonical atlas page allocation.");
                        _current[sizeClass] = page; _used[sizeClass] = 0;
                    }
                    if (page != _current[sizeClass] || ordinal != _used[sizeClass]++)
                        throw new InvalidDataException("Noncanonical atlas cell allocation.");
                    var cell = new Cell(page, size, ordinal);
                    for (int channel = 0; channel < 2; channel++)
                    {
                        using var pixels = File.OpenRead(PagePath(page, channel != 0));
                        if (pixels.Length != 4L * Resolution * Resolution)
                            throw new InvalidDataException("Atlas pixel spool is truncated.");
                        for (int row = 0; row < size; row++)
                        {
                            token.ThrowIfCancellationRequested();
                            pixels.Position = 4L * ((cell.Y + row) * Resolution + cell.X);
                            int remaining = size * 4, cursor = row * size * 4;
                            while (remaining != 0)
                            {
                                int got = pixels.Read(_cell, cursor, remaining);
                                if (got <= 0) throw new EndOfStreamException();
                                remaining -= got; cursor += got;
                            }
                        }
                        using SHA256 sha = SHA256.Create();
                        if (!sha.ComputeHash(_cell, 0, size * size * 4).AsSpan().SequenceEqual(
                                channel == 0 ? expectedRgb : expectedV))
                            throw new InvalidDataException("Completed RGB/V atlas cell checksum mismatch.");
                    }
                }
                if (pages != PageCount) throw new InvalidDataException("Atlas page receipt is incomplete.");
                for (int i = 0; i < 3; i++)
                {
                    if (_current[i] != state.current[i] || _used[i] != state.used[i])
                        throw new InvalidDataException("Atlas cursor differs from complete cells.");
                    if (_current[i] >= 0) ClearUncommittedCells(_current[i], 16 << i, _used[i], token);
                }
                foreach (string path in Directory.EnumerateFiles(_directory, "atlas-*.rgba"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    bool normal = name.StartsWith("atlas-v-", StringComparison.Ordinal);
                    string prefix = normal ? "atlas-v-" : "atlas-rgb-";
                    if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                        !int.TryParse(name.AsSpan(prefix.Length), out int page) || page < 0 ||
                        Path.GetFileName(path) != Path.GetFileName(PagePath(page, normal)))
                        throw new InvalidDataException("Unrecognized file in atlas staging.");
                    if (page >= PageCount) File.Delete(path); // never a completed page
                }
                Array.Copy(_current, _durableCurrent, 3); _durablePages = PageCount;
                _sizes.Position = _sizes.Length; _receipts.Position = _receipts.Length;
            }

            private void ClearUncommittedCells(int page, int size, int used, CancellationToken token)
            {
                int perRow = Resolution / size, firstRow = used / perRow * size;
                var zeros = new byte[Resolution * 4];
                for (int channel = 0; channel < 2; channel++)
                {
                    using var pixels = new FileStream(PagePath(page, channel != 0), FileMode.Open,
                        FileAccess.Write, FileShare.None, 4096);
                    for (int y = firstRow; y < Resolution; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        int x = y < firstRow + size ? (used % perRow) * size : 0;
                        pixels.Position = 4L * (y * Resolution + x);
                        pixels.Write(zeros, 0, 4 * (Resolution - x));
                    }
                    pixels.Flush(true);
                }
            }

            internal int PageCellSize(int page)
            {
                if ((uint)page >= (uint)PageCount) throw new ArgumentOutOfRangeException(nameof(page));
                _sizes.Position = page;
                int size = _sizes.ReadByte();
                if (size != 16 && size != 32 && size != 64)
                    throw new InvalidDataException("Atlas size-class receipt is incomplete.");
                return size;
            }

            internal long WritePage(int page, bool normal, Stream destination, CancellationToken cancellationToken)
            {
                PageCellSize(page);
                using var pixels = new FileStream(PagePath(page, normal), FileMode.Open,
                    FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                return Png(destination, pixels, normal, cancellationToken);
            }

            private string PagePath(int page, bool normal) => Path.Combine(_directory,
                (normal ? "atlas-v-" : "atlas-rgb-") +
                page.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + ".rgba");

            public void Dispose() { _receipts?.Dispose(); _sizes.Dispose(); }
        }

        internal static bool HasSignalDetail(MerkabaFlowerPresentation.Carrier carrier)
        {
            uint low = carrier.SkinHeader.SplitBitsLo, high = carrier.SkinHeader.SplitBitsHi;
            int groups = MerkabaFlowerSkinSplitBits.GroupCount(low, high);
            if (groups < 0 || carrier.SkinSamples == null ||
                (ulong)carrier.SkinHeader.FirstSample + 1ul + 7ul * (uint)groups >
                (ulong)carrier.SkinSamples.Length)
                throw new InvalidDataException("Flower RGB/V draw signal receipt is incomplete.");
            return MerkabaFlowerSkinSplitBits.SplitL2(low);
        }

        internal static int CellSize(MerkabaFlowerPresentation.Carrier carrier)
        {
            if (!HasSignalDetail(carrier))
                throw new InvalidDataException("Uniform L2 signal needs no atlas cell.");
            uint low = carrier.SkinHeader.SplitBitsLo, high = carrier.SkinHeader.SplitBitsHi;
            if (MerkabaFlowerSkinSplitBits.SplitL4Low(low, high) != 0u ||
                MerkabaFlowerSkinSplitBits.SplitL4High(high) != 0u) return 64;
            return MerkabaFlowerSkinSplitBits.SplitL3Thread(low) != 0u ? 32 : 16;
        }

        private static void ChartSample(float2 chart, uint active, out int wedge, out float3 bc)
        {
            if (MerkabaSphereFlowerAuthority.TryL2CarrierChartWedge(chart, out wedge,
                    out bc, out _, out _) && (active & (1u << wedge)) != 0u) return;
            // Extend only already active support into padding. Missing wedges
            // have no evaluable world frame; never read their unproved sites.
            double best = double.PositiveInfinity;
            wedge = -1;
            for (int candidate = 0; candidate < 6; candidate++)
            {
                if ((active & (1u << candidate)) == 0u) continue;
                int3 sites = MerkabaSphereFlowerAuthority.L2CarrierTriangleIndices(candidate);
                for (int edge = 0; edge < 3; edge++)
                {
                    int next = (edge + 1) % 3;
                    float2 a = MerkabaSphereFlowerAuthority.L2CarrierChartSite(sites[edge]);
                    float2 b = MerkabaSphereFlowerAuthority.L2CarrierChartSite(sites[next]);
                    double dx = b.x - a.x, dy = b.y - a.y;
                    double t = Math.Max(0.0, Math.Min(1.0,
                        ((chart.x - a.x) * dx + (chart.y - a.y) * dy) / (dx * dx + dy * dy)));
                    double x = chart.x - (a.x + t * dx), y = chart.y - (a.y + t * dy);
                    double distance = x * x + y * y;
                    if (distance >= best) continue;
                    best = distance; wedge = candidate;
                    bc = float3.zero; bc[edge] = 1f - (float)t; bc[next] = (float)t;
                }
            }
            if (wedge < 0) throw new InvalidDataException("Nonfinite atlas chart coordinate.");
        }

        internal static byte LinearByte(float linear) => checked((byte)Math.Round(
            255.0 * math.clamp(linear, 0f, 1f), MidpointRounding.ToEven));

        private static byte Srgb(float value)
        {
            if (!math.isfinite(value)) throw new InvalidDataException("Nonfinite captured RGB.");
            double linear = math.clamp(value, 0f, 1f);
            double encoded = linear <= 0.0031308 ? 12.92 * linear :
                1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
            return checked((byte)Math.Round(255.0 * encoded, MidpointRounding.ToEven));
        }

        private static long Png(Stream destination, Stream pixels, bool normal, CancellationToken cancellationToken)
        {
            if (!destination.CanSeek) throw new ArgumentException("Atlas image spool must be seekable.");
            long start = destination.Position;
            destination.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var ihdr = new byte[13];
            BigEndian(ihdr, 0, Resolution); BigEndian(ihdr, 4, Resolution);
            ihdr[8] = 8; ihdr[9] = 6; // RGBA8: sRGB radiance or linear normal data
            Chunk(destination, "IHDR", ihdr, ihdr.Length);
            if (!normal) Chunk(destination, "sRGB", new byte[] { 0 }, 1);
            using var idat = new IdatStream(destination, cancellationToken);
            idat.WriteByte(0x78); idat.WriteByte(0x01);
            uint a = 1, b = 0;
            var row = new byte[1 + 4 * Resolution];
            using (var deflate = new DeflateStream(idat, CompressionLevel.Fastest, true))
            {
                for (int y = 0; y < Resolution; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = 1;
                    while (read < row.Length)
                    {
                        int count = pixels.Read(row, read, row.Length - read);
                        if (count == 0) throw new EndOfStreamException("Incomplete atlas pixel page.");
                        read += count;
                    }
                    foreach (byte value in row)
                    { a = (a + value) % 65521u; b = (b + a) % 65521u; }
                    deflate.Write(row, 0, row.Length);
                }
            }
            var checksum = new byte[4]; BigEndian(checksum, 0, (b << 16) | a);
            idat.Write(checksum, 0, checksum.Length); idat.Flush();
            Chunk(destination, "IEND", Array.Empty<byte>(), 0);
            return checked(destination.Position - start);
        }

        // A zlib stream may span any number of IDAT chunks. Chunk framing
        // bounds memory even for a high-entropy 2048² image; no PNG ToArray.
        private sealed class IdatStream : Stream
        {
            private readonly Stream _destination;
            private readonly CancellationToken _cancellationToken;
            private readonly byte[] _packet = new byte[64 * 1024];
            private int _count;
            internal IdatStream(Stream destination, CancellationToken cancellationToken)
            { _destination = destination; _cancellationToken = cancellationToken; }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count)
            {
                while (count != 0)
                {
                    int take = Math.Min(count, _packet.Length - _count);
                    Buffer.BlockCopy(buffer, offset, _packet, _count, take);
                    _count += take; offset += take; count -= take;
                    if (_count == _packet.Length) Flush();
                }
            }
            public override void WriteByte(byte value)
            { _packet[_count++] = value; if (_count == _packet.Length) Flush(); }
            public override void Flush()
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (_count == 0) return;
                Chunk(_destination, "IDAT", _packet, _count); _count = 0;
            }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        private static void Chunk(Stream destination, string type, byte[] payload, int count)
        {
            var length = new byte[4]; BigEndian(length, 0, (uint)count);
            destination.Write(length, 0, length.Length);
            var bytes = new byte[checked(4 + count)];
            for (int i = 0; i < 4; i++) bytes[i] = (byte)type[i];
            Buffer.BlockCopy(payload, 0, bytes, 4, count);
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
