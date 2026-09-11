using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal readonly struct MerkabaGlbResult
    {
        public readonly long ByteLength;
        public readonly int VertexCount;
        public readonly int IndexCount;
        public readonly int PrimitiveCount;
        public readonly Vector3 Minimum;
        public readonly Vector3 Maximum;

        public MerkabaGlbResult(long byteLength, int vertexCount, int indexCount,
            int primitiveCount, Vector3 minimum, Vector3 maximum)
        {
            ByteLength = byteLength; VertexCount = vertexCount;
            IndexCount = indexCount; PrimitiveCount = primitiveCount;
            Minimum = minimum; Maximum = maximum;
        }
    }

    /// <summary>Bounded shared-position GLB preview of direct M8 surface.</summary>
    internal static class MerkabaGlbWriter
    {
        private const uint GlbMagic = 0x46546C67u;
        private const uint JsonChunkType = 0x4E4F534Au;
        private const uint BinaryChunkType = 0x004E4942u;
        private const int IoPacketBytes = 64 * 1024;

        private readonly struct PrimitiveRange
        {
            internal readonly int FirstIndex, IndexCount, Texture;
            internal PrimitiveRange(int first, int count, int texture)
            { FirstIndex = first; IndexCount = count; Texture = texture; }
        }

        private readonly struct ImageRange
        {
            internal readonly long Offset;
            internal readonly int Length;
            internal ImageRange(long offset, int length) { Offset = offset; Length = length; }
        }

        [Serializable]
        internal sealed class ResumeState
        {
            public int vertices, indices, primitives, ranges;
            public Vector3 minimum, maximum;
            public MerkabaFlowerMaterialBake.ResumeState atlas;
        }

        internal static readonly string[] JournalFiles =
        {
            "content/positions.bin", "content/colors.bin",
            "content/indices.bin", "content/uv.bin", "content/images.bin",
            "content/ranges.bin", "content/image-ranges.bin",
            "content/atlas-sizes.bin", "content/atlas-cells.bin"
        };

        /// <summary>
        /// Attribute, index, atlas, range, image and JSON spools are bounded.
        /// Nothing here accumulates a world-sized geometry or metadata list.
        /// Caller publishes the destination only after Complete succeeds.
        /// </summary>
        internal sealed class StreamingSession : IDisposable
        {
            private readonly string _directory;
            private readonly FileStream _positions, _colors, _indices, _uvs, _images;
            private readonly FileStream _ranges, _imageRanges;
            private readonly BinaryWriter _positionWriter, _colorWriter, _indexWriter, _uvWriter;
            private readonly BinaryWriter _rangeWriter, _imageRangeWriter;
            private readonly MerkabaFlowerMaterialBake.Atlas _atlas;
            private readonly bool _restoring, _preserveSpool;
            private MerkabaNativePackage.Snapshot _nativePackage;
            private int _vertexCount, _indexCount, _primitiveCount, _rangeCount;
            private PrimitiveRange _pendingRange;
            private bool _hasPendingRange, _completed, _disposed, _failed;
            private Vector3 _minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            private Vector3 _maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            internal StreamingSession(string directory, ResumeState resume = null,
                bool preserveSpool = false, CancellationToken cancellationToken = default)
            {
                if (string.IsNullOrWhiteSpace(directory))
                    throw new ArgumentException("GLB spool directory is required.", nameof(directory));
                if (Directory.Exists(directory) && resume == null)
                    throw new IOException("GLB spool directory already exists: " + directory);
                if (resume != null && !Directory.Exists(directory))
                    throw new IOException("Verified GLB spool is missing: " + directory);
                _directory = directory;
                _restoring = resume != null;
                _preserveSpool = preserveSpool;
                Directory.CreateDirectory(directory);
                try
                {
                    _positions = Open("positions.bin");
                    _colors = Open("colors.bin"); _indices = Open("indices.bin");
                    _uvs = Open("uv.bin"); _images = Open("images.bin");
                    _ranges = Open("ranges.bin"); _imageRanges = Open("image-ranges.bin");
                    var encoding = new UTF8Encoding(false);
                    _positionWriter = new BinaryWriter(_positions, encoding, true);
                    _colorWriter = new BinaryWriter(_colors, encoding, true);
                    _indexWriter = new BinaryWriter(_indices, encoding, true);
                    _uvWriter = new BinaryWriter(_uvs, encoding, true);
                    _rangeWriter = new BinaryWriter(_ranges, encoding, true);
                    _imageRangeWriter = new BinaryWriter(_imageRanges, encoding, true);
                    _atlas = new MerkabaFlowerMaterialBake.Atlas(directory, resume?.atlas, cancellationToken);
                    if (resume != null)
                    {
                        if (resume.vertices < 0 || resume.indices < 0 || resume.primitives < 0 ||
                            resume.ranges < 0 || resume.indices != 3L * resume.primitives ||
                            _positions.Length != 12L * resume.vertices ||
                            _colors.Length != 4L * resume.vertices || _uvs.Length != 8L * resume.vertices ||
                            _indices.Length != 4L * resume.indices || _ranges.Length != 12L * resume.ranges ||
                            _images.Length != 0 || _imageRanges.Length != 0)
                            throw new InvalidDataException("GLB resume state differs from verified spool extents.");
                        _vertexCount = resume.vertices; _indexCount = resume.indices;
                        _primitiveCount = resume.primitives; _rangeCount = resume.ranges;
                        if (_vertexCount != 0) { _minimum = resume.minimum; _maximum = resume.maximum; }
                        foreach (FileStream file in new[] { _positions, _colors, _uvs, _indices, _ranges })
                            file.Position = file.Length;
                        string json = Path.Combine(directory, "document.json");
                        if (File.Exists(json)) File.Delete(json); // uncommitted final-container scratch only
                    }
                }
                catch { Dispose(); throw; }
            }

            internal ResumeState Checkpoint(CancellationToken cancellationToken)
            {
                RequireMutable(cancellationToken);
                FlushRange();
                _positionWriter.Flush(); _colorWriter.Flush();
                _indexWriter.Flush(); _uvWriter.Flush(); _rangeWriter.Flush(); _imageRangeWriter.Flush();
                foreach (FileStream file in new[] { _positions, _colors, _indices,
                             _uvs, _ranges, _images, _imageRanges })
                { cancellationToken.ThrowIfCancellationRequested(); file.Flush(true); }
                return new ResumeState
                {
                    vertices = _vertexCount, indices = _indexCount, primitives = _primitiveCount,
                    ranges = _rangeCount,
                    minimum = _vertexCount == 0 ? Vector3.zero : _minimum,
                    maximum = _vertexCount == 0 ? Vector3.zero : _maximum,
                    atlas = _atlas.Checkpoint(cancellationToken)
                };
            }

            // The whole-export lease owns this immutable snapshot until all
            // writers finish. Its records are independent of the preview mesh.
            internal void AttachNativePackage(MerkabaNativePackage.Snapshot package)
            {
                RequireMutable(default);
                if (package == null) throw new ArgumentNullException(nameof(package));
                if (_nativePackage != null) throw new InvalidOperationException("Native package already attached.");
                _nativePackage = package;
            }

            internal void Append(MerkabaFlowerPresentation presentation,
                IProgress<OperationWorkProgress> progress = null, float3 localOrigin = default,
                CancellationToken cancellationToken = default)
            {
                RequireMutable(cancellationToken);
                if (presentation == null) throw new ArgumentNullException(nameof(presentation));
                try
                {
                    int completed = 0;
                    foreach (MerkabaFlowerPresentation.Carrier carrier in presentation.Carriers)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        uint active = carrier.Symbol.ActiveWedgeMask;
                        if (active == 0u || (active & ~63u) != 0u ||
                            (carrier.Symbol.CompletedWedgeMask & ~active) != 0u ||
                            (carrier.Symbol.ReverseWedgeMask & ~active) != 0u)
                            throw new InvalidDataException("Noncanonical Flower presentation masks.");
                        bool textured = MerkabaFlowerMaterialBake.HasSignalDetail(carrier);
                        MerkabaFlowerMaterialBake.Cell cell = textured
                            ? _atlas.Append(presentation, carrier, cancellationToken) : default;
                        float3 rgb = textured ? new float3(1f) :
                            carrier.SkinSamples[checked((int)carrier.SkinHeader.FirstSample)].CapturedRgb;
                        uint color = PackColor(rgb);
                        uint used = 0u;
                        for (int wedge = 0; wedge < 6; wedge++)
                        {
                            if ((active & (1u << wedge)) == 0u) continue;
                            int3 sites = MerkabaSphereFlowerAuthority.L2CarrierTriangleIndices(wedge);
                            for (int corner = 0; corner < 3; corner++)
                                used |= 1u << sites[corner];
                        }
                        uint vertexBase = checked((uint)_vertexCount);
                        float3 hub = presentation.Position(carrier, 0);
                        for (int site = 0; site < 7; site++)
                        {
                            bool referenced = (used & (1u << site)) != 0u;
                            // Unproved inactive sites are never indexed. Their
                            // reserved tuple is inert H, not invented geometry.
                            float3 position = referenced ? presentation.Position(carrier, site) : hub;
                            WriteVertex(position - localOrigin, color, textured ? cell.Uv(site) : float2.zero);
                        }
                        for (int wedge = 0; wedge < 6; wedge++)
                        {
                            if ((active & (1u << wedge)) == 0u) continue;
                            int3 sites = MerkabaSphereFlowerAuthority.L2CarrierTriangleIndices(wedge,
                                (carrier.Symbol.ReverseWedgeMask & (1u << wedge)) != 0u);
                            AppendRange(textured ? cell.Page : -1);
                            // The running index count is the exclusive active-
                            // wedge prefix. Reflect X exactly once for glTF.
                            WriteTriangle(vertexBase + (uint)sites.x, vertexBase + (uint)sites.z,
                                vertexBase + (uint)sites.y);
                            completed++;
                        }
                        Report(progress, ScanOperationStage.BuildingMerkabaGeometry, completed,
                            presentation.TriangleCount, "Writing seven shared L2 sites and carrier RGB/V atlas cells");
                    }
                    if (completed != presentation.TriangleCount)
                        throw new InvalidDataException("Flower presentation triangle receipt mismatch.");
                }
                catch { _failed = true; throw; }
            }

            private void WriteVertex(float3 position, uint color, float2 uv)
            {
                if (!math.all(math.isfinite(position)) ||
                    !math.all(math.isfinite(uv)))
                    throw new InvalidDataException("Nonfinite Flower presentation attribute.");
                // No interpolated NORMAL/TANGENT is attached to a shared knot.
                // glTF without NORMAL requires the actual triangle's flat
                // normal and a UV-derived tangent frame, matching the V bake.
                Vector3 converted = Convert(position);
                _positionWriter.Write(converted.x); _positionWriter.Write(converted.y); _positionWriter.Write(converted.z);
                _colorWriter.Write(color); // little-endian RGBA8, linear COLOR_0
                _uvWriter.Write(uv.x); _uvWriter.Write(uv.y);
                _vertexCount = checked(_vertexCount + 1);
                _minimum = Vector3.Min(_minimum, converted); _maximum = Vector3.Max(_maximum, converted);
            }

            private void WriteTriangle(uint a, uint b, uint c)
            {
                _indexWriter.Write(a); _indexWriter.Write(b); _indexWriter.Write(c);
                _indexCount = checked(_indexCount + 3);
                _primitiveCount = checked(_primitiveCount + 1);
            }

            private void AppendRange(int texture)
            {
                if (_hasPendingRange && _pendingRange.Texture == texture &&
                    checked(_pendingRange.FirstIndex + _pendingRange.IndexCount) == _indexCount)
                {
                    _pendingRange = new PrimitiveRange(_pendingRange.FirstIndex,
                        checked(_pendingRange.IndexCount + 3), texture);
                    return;
                }
                FlushRange();
                _pendingRange = new PrimitiveRange(_indexCount, 3, texture);
                _hasPendingRange = true;
            }

            private void FlushRange()
            {
                if (!_hasPendingRange) return;
                _rangeWriter.Write(_pendingRange.FirstIndex); _rangeWriter.Write(_pendingRange.IndexCount);
                _rangeWriter.Write(_pendingRange.Texture);
                _rangeCount = checked(_rangeCount + 1); _hasPendingRange = false;
            }

            internal MerkabaGlbResult Complete(Stream destination,
                IProgress<OperationWorkProgress> progress = null,
                CancellationToken cancellationToken = default,
                long maximumByteLength = uint.MaxValue)
            {
                RequireMutable(cancellationToken);
                if (_primitiveCount == 0) throw new InvalidDataException("GLB Flower geometry is empty.");
                if (destination == null || !destination.CanWrite)
                    throw new ArgumentException("GLB destination must be writable.", nameof(destination));
                if (maximumByteLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumByteLength));
                try
                {
                    FlushRange();
                    _positionWriter.Flush(); _colorWriter.Flush();
                    _indexWriter.Flush(); _uvWriter.Flush(); _rangeWriter.Flush();
                    if (_positions.Length != 12L * _vertexCount ||
                        _colors.Length != 4L * _vertexCount || _indices.Length != 4L * _indexCount ||
                        _uvs.Length != 8L * _vertexCount || _ranges.Length != 12L * _rangeCount)
                        throw new InvalidDataException("Bounded GLB spool length mismatch.");
                    for (int page = 0; page < _atlas.PageCount; page++)
                    {
                        for (int channel = 0; channel < 2; channel++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            long offset = _images.Position;
                            long length = _atlas.WritePage(page, channel != 0, _images, cancellationToken);
                            _imageRangeWriter.Write(offset); _imageRangeWriter.Write(checked((int)length));
                            while ((_images.Position & 3L) != 0L) _images.WriteByte(0);
                        }
                        Report(progress, ScanOperationStage.BuildingMerkabaGeometry, page + 1,
                            _atlas.PageCount, "Encoding bounded captured-RGB and linear V-normal atlas pages");
                    }
                    _imageRangeWriter.Flush(); _images.Flush();
                    long positionsOffset = 0, colorsOffset = _positions.Length;
                    long indicesOffset = checked(colorsOffset + _colors.Length);
                    long uvsOffset = checked(indicesOffset + _indices.Length);
                    bool hasTextures = _atlas.PageCount != 0;
                    long uvBytes = hasTextures ? _uvs.Length : 0L;
                    long imagesOffset = checked(uvsOffset + uvBytes);
                    long nativeManifestOffset = checked(imagesOffset + _images.Length);
                    long nativePayloadOffset = _nativePackage == null ? nativeManifestOffset :
                        checked((nativeManifestOffset + _nativePackage.ManifestBytes.Length + 3L) & ~3L);
                    long binaryLength = checked(nativePayloadOffset + (_nativePackage?.PayloadBytes ?? 0L));
                    CheckLength(binaryLength, maximumByteLength);
                    using var json = Open("document.json");
                    using (var text = new StreamWriter(json, new UTF8Encoding(false), IoPacketBytes, true))
                        WriteJson(text, binaryLength, positionsOffset, colorsOffset,
                            indicesOffset, uvsOffset, imagesOffset, cancellationToken);
                    long paddedJsonLength = checked((json.Length + 3L) & ~3L);
                    long totalLength = checked(28L + paddedJsonLength + binaryLength);
                    CheckLength(totalLength, maximumByteLength);
                    // No final bytes are written before every required image,
                    // range, bound and GLB/container budget has been validated.
                    cancellationToken.ThrowIfCancellationRequested();
                    long start = destination.CanSeek ? destination.Position : 0;
                    using var writer = new BinaryWriter(destination, new UTF8Encoding(false), true);
                    writer.Write(GlbMagic); writer.Write(2u); writer.Write((uint)totalLength);
                    writer.Write((uint)paddedJsonLength); writer.Write(JsonChunkType); writer.Flush();
                    var packet = new byte[IoPacketBytes];
                    Copy(json, destination, packet, cancellationToken);
                    for (long padding = json.Length; padding < paddedJsonLength; padding++) writer.Write((byte)0x20);
                    writer.Write((uint)binaryLength); writer.Write(BinaryChunkType); writer.Flush();
                    Copy(_positions, destination, packet, cancellationToken);
                    Copy(_colors, destination, packet, cancellationToken);
                    Copy(_indices, destination, packet, cancellationToken);
                    if (hasTextures) Copy(_uvs, destination, packet, cancellationToken);
                    Copy(_images, destination, packet, cancellationToken);
                    if (_nativePackage != null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        destination.Write(_nativePackage.ManifestBytes, 0, _nativePackage.ManifestBytes.Length);
                        for (long offset = nativeManifestOffset + _nativePackage.ManifestBytes.Length;
                             offset < nativePayloadOffset; offset++) destination.WriteByte(0);
                        _nativePackage.WritePayload(destination, cancellationToken);
                    }
                    destination.Flush();
                    long written = destination.CanSeek ? destination.Position - start : totalLength;
                    if (written != totalLength) throw new InvalidDataException("GLB final length mismatch.");
                    cancellationToken.ThrowIfCancellationRequested();
                    _completed = true;
                    Report(progress, ScanOperationStage.WritingFile, written, written, "GLB bytes written");
                    return new MerkabaGlbResult(written, _vertexCount, _indexCount, _primitiveCount, _minimum, _maximum);
                }
                catch { _failed = true; throw; }
            }

            private IEnumerable<PrimitiveRange> ReadRanges(CancellationToken cancellationToken)
            {
                _ranges.Position = 0;
                using var reader = new BinaryReader(_ranges, Encoding.UTF8, true);
                for (int index = 0; index < _rangeCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int first = reader.ReadInt32(), count = reader.ReadInt32(), texture = reader.ReadInt32();
                    yield return new PrimitiveRange(first, count, texture);
                }
            }

            private IEnumerable<ImageRange> ReadImages(CancellationToken cancellationToken)
            {
                _imageRanges.Position = 0;
                using var reader = new BinaryReader(_imageRanges, Encoding.UTF8, true);
                for (int index = 0; index < checked(2 * _atlas.PageCount); index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new ImageRange(reader.ReadInt64(), reader.ReadInt32());
                }
            }

            private void WriteJson(TextWriter json, long binaryLength, long positionsOffset,
                long colorsOffset, long indicesOffset, long uvsOffset, long imagesOffset,
                CancellationToken cancellationToken)
            {
                int pageCount = _atlas.PageCount, imageCount = checked(2 * pageCount);
                const int textureMaterial = 1;
                int uvAccessor = checked(2 + _rangeCount);
                json.Write("{\"asset\":{\"version\":\"2.0\",\"generator\":\"Quest Infinite Merkaba\"},");
                json.Write("\"extensionsUsed\":[\"KHR_materials_unlit\"");
                if (_nativePackage != null)
                {
                    int nativeView = checked(3 + (imageCount > 0 ? 1 : 0) + imageCount);
                    json.Write(",\"M8_flower_thread\"],\"extensions\":{\"M8_flower_thread\":{\"version\":");
                    json.Write(MerkabaNativePackage.Version);
                    json.Write(",\"manifest\":"); json.Write(nativeView);
                    json.Write(",\"payloads\":["); json.Write(checked(nativeView + 1)); json.Write("]}},");
                }
                else json.Write("],");
                json.Write("\"extras\":{\"m8Preview\":\"captured-RGB+V-normal\",\"proceduralRgbv\":false,\"normalBakeAvailable\":");
                json.Write(imageCount > 0 ? "true" : "false");
                json.Write(",\"normalTextureUse\":\"lit-fallback-only\",\"atlasSampling\":\"finite-presentation-approximation\"},");
                json.Write("\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"name\":\"MerkabaGrid\",\"mesh\":0}],");
                json.Write("\"meshes\":[{\"name\":\"M8 Presentation\",\"primitives\":[");
                int ordinal = 0;
                foreach (PrimitiveRange range in ReadRanges(cancellationToken))
                {
                    if (ordinal != 0) json.Write(',');
                    json.Write("{\"attributes\":{\"POSITION\":0,\"COLOR_0\":1");
                    if (range.Texture >= 0)
                    {
                        if (range.Texture >= pageCount)
                            throw new InvalidDataException("Flower primitive has no completed RGB/V atlas.");
                        json.Write(",\"TEXCOORD_0\":"); json.Write(uvAccessor);
                    }
                    json.Write("},\"indices\":"); json.Write(checked(2 + ordinal));
                    json.Write(",\"material\":"); json.Write(range.Texture >= 0 ? checked(textureMaterial + range.Texture) : 0);
                    json.Write(",\"mode\":4,\"extras\":{\"m8Provenance\":\"");
                    json.Write("CONFIRMED\",\"capturedRadiance\":true");
                    json.Write("}}"); ordinal++;
                }
                json.Write("]}],\"materials\":[{\"name\":\"M8 Captured Radiance\",");
                json.Write("\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1],\"metallicFactor\":0,\"roughnessFactor\":1},");
                json.Write("\"doubleSided\":true,\"extensions\":{\"KHR_materials_unlit\":{}}}");
                for (int page = 0; page < pageCount; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    json.Write(",{\"name\":\"M8 Captured RGB / V Atlas "); json.Write(page);
                    json.Write("\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1],\"baseColorTexture\":{\"index\":");
                    json.Write(checked(2 * page));
                    json.Write("},\"metallicFactor\":0,\"roughnessFactor\":1},\"normalTexture\":{\"index\":");
                    json.Write(checked(2 * page + 1));
                    json.Write(",\"texCoord\":0,\"scale\":1},\"doubleSided\":true,\"extensions\":{\"KHR_materials_unlit\":{}},");
                    json.Write("\"extras\":{\"capturedRadiance\":true,\"atlasWidth\":");
                    json.Write(MerkabaFlowerMaterialBake.Resolution);
                    json.Write(",\"atlasCell\":"); json.Write(_atlas.PageCellSize(page));
                    json.Write(",\"atlasGutter\":"); json.Write(MerkabaFlowerMaterialBake.Gutter);
                    json.Write(",\"normalBakeAvailable\":true,\"m8NormalTexture\":\"derived-V-lit-fallback\",\"normalFrame\":\"flat-L2-UV\"}}");
                }
                json.Write("],");
                if (imageCount > 0)
                {
                    json.Write("\"samplers\":[{\"magFilter\":9729,\"minFilter\":9729,\"wrapS\":33071,\"wrapT\":33071}],\"textures\":[");
                    for (int page = 0; page < imageCount; page++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (page != 0) json.Write(',');
                        json.Write("{\"sampler\":0,\"source\":"); json.Write(page); json.Write('}');
                    }
                    json.Write("],\"images\":[");
                    for (int page = 0; page < imageCount; page++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (page != 0) json.Write(',');
                        json.Write("{\"bufferView\":"); json.Write(checked(4 + page));
                        json.Write(",\"mimeType\":\"image/png\"}");
                    }
                    json.Write("],");
                }
                json.Write("\"buffers\":[{\"byteLength\":"); json.Write(binaryLength); json.Write("}],\"bufferViews\":[");
                BufferView(json, positionsOffset, _positions.Length, 34962); json.Write(',');
                BufferView(json, colorsOffset, _colors.Length, 34962); json.Write(',');
                BufferView(json, indicesOffset, _indices.Length, 34963);
                if (imageCount > 0) { json.Write(','); BufferView(json, uvsOffset, _uvs.Length, 34962); }
                foreach (ImageRange image in ReadImages(cancellationToken))
                { json.Write(','); BufferView(json, checked(imagesOffset + image.Offset), image.Length, 0); }
                if (_nativePackage != null)
                {
                    long manifestOffset = checked(imagesOffset + _images.Length);
                    long payloadOffset = checked((manifestOffset + _nativePackage.ManifestBytes.Length + 3L) & ~3L);
                    json.Write(','); BufferView(json, manifestOffset, _nativePackage.ManifestBytes.Length, 0);
                    json.Write(','); BufferView(json, payloadOffset, _nativePackage.PayloadBytes, 0);
                }
                json.Write("],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":");
                json.Write(_vertexCount); json.Write(",\"type\":\"VEC3\",\"min\":[");
                VectorJson(json, _minimum); json.Write("],\"max\":["); VectorJson(json, _maximum);
                json.Write("]},{\"bufferView\":1,\"componentType\":5121,\"normalized\":true,\"count\":");
                json.Write(_vertexCount); json.Write(",\"type\":\"VEC4\"}");
                int covered = 0;
                foreach (PrimitiveRange range in ReadRanges(cancellationToken))
                {
                    if (range.FirstIndex != covered || range.IndexCount <= 0 || range.IndexCount % 3 != 0)
                        throw new InvalidDataException("GLB primitive ranges are not a contiguous triangle partition.");
                    covered = checked(covered + range.IndexCount);
                    json.Write(",{\"bufferView\":2,\"byteOffset\":"); json.Write(checked(4L * range.FirstIndex));
                    json.Write(",\"componentType\":5125,\"count\":"); json.Write(range.IndexCount);
                    json.Write(",\"type\":\"SCALAR\"}");
                }
                if (covered != _indexCount) throw new InvalidDataException("GLB ranges do not cover the index buffer.");
                if (imageCount > 0)
                {
                    json.Write(",{\"bufferView\":3,\"componentType\":5126,\"count\":"); json.Write(_vertexCount);
                    json.Write(",\"type\":\"VEC2\"}");
                }
                json.Write("]}");
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _positionWriter?.Dispose(); _colorWriter?.Dispose();
                _indexWriter?.Dispose(); _uvWriter?.Dispose(); _rangeWriter?.Dispose(); _imageRangeWriter?.Dispose();
                _positions?.Dispose(); _colors?.Dispose(); _indices?.Dispose();
                _uvs?.Dispose(); _images?.Dispose(); _ranges?.Dispose(); _imageRanges?.Dispose(); _atlas?.Dispose();
                // This directory was created exclusively by this session and
                // never contains a published model or caller-owned records.
                if (!_preserveSpool && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }

            private FileStream Open(string name) => new(Path.Combine(_directory, name),
                _restoring && name != "document.json" ? FileMode.Open : FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.Read, IoPacketBytes);
            private void RequireMutable(CancellationToken cancellationToken)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(StreamingSession));
                if (_failed) throw new InvalidOperationException("Failed GLB append cannot publish a partial model.");
                if (_completed) throw new InvalidOperationException("The bounded GLB stream is already complete.");
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        internal static MerkabaGlbResult Write(Stream destination,
            MerkabaFlowerPresentation presentation, float3 localOrigin,
            IProgress<OperationWorkProgress> progress = null, CancellationToken cancellationToken = default,
            long maximumByteLength = uint.MaxValue)
        {
            string spool = Path.Combine(Path.GetTempPath(), "m8-flower-glb-" + Guid.NewGuid().ToString("N"));
            using var session = new StreamingSession(spool);
            session.Append(presentation, progress, localOrigin, cancellationToken);
            return session.Complete(destination, progress, cancellationToken, maximumByteLength);
        }

        internal static int CheckedIndexCountForPrimitiveCount(long primitiveCount)
        {
            if (primitiveCount <= 0) throw new InvalidDataException("GLB Flower geometry is empty.");
            try
            {
                long indices = checked(primitiveCount * 3L);
                if (checked(indices * 32L) > uint.MaxValue || indices > int.MaxValue)
                    throw new InvalidDataException("GLB geometry exceeds the 4 GiB container limit.");
                return (int)indices;
            }
            catch (OverflowException exception)
            { throw new InvalidDataException("GLB geometry exceeds the 4 GiB container limit.", exception); }
        }

        private static uint PackColor(float3 color)
        {
            if (!math.all(math.isfinite(color))) throw new InvalidDataException("Nonfinite captured RGB.");
            return MerkabaFlowerMaterialBake.LinearByte(color.x) |
                ((uint)MerkabaFlowerMaterialBake.LinearByte(color.y) << 8) |
                ((uint)MerkabaFlowerMaterialBake.LinearByte(color.z) << 16) | 0xff000000u;
        }

        private static void CheckLength(long length, long maximum)
        {
            if (length < 0 || length > uint.MaxValue || length > maximum)
                throw new InvalidDataException("GLB exceeds its container/leaf byte budget; split the canonical export batch.");
        }

        private static void Copy(FileStream source, Stream destination, byte[] packet, CancellationToken token)
        {
            source.Position = 0;
            int count;
            while ((count = source.Read(packet, 0, packet.Length)) != 0)
            { token.ThrowIfCancellationRequested(); destination.Write(packet, 0, count); }
        }

        private static void BufferView(TextWriter json, long offset, long length, int target)
        {
            json.Write("{\"buffer\":0,\"byteOffset\":"); json.Write(offset);
            json.Write(",\"byteLength\":"); json.Write(length);
            if (target != 0) { json.Write(",\"target\":"); json.Write(target); }
            json.Write('}');
        }
        private static void VectorJson(TextWriter json, Vector3 value)
        {
            json.Write(Number(value.x)); json.Write(','); json.Write(Number(value.y));
            json.Write(','); json.Write(Number(value.z));
        }
        private static void Report(IProgress<OperationWorkProgress> progress, ScanOperationStage stage,
            long completed, long total, string text) =>
            progress?.Report(new OperationWorkProgress(stage, completed, total, text));
        private static Vector3 Convert(float3 value) => new(-value.x, value.y, value.z);
        private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
