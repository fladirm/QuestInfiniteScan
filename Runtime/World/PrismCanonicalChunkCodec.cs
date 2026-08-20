using System;
using System.IO;
using System.Text;
using Genesis.RoomScan.Prism;

namespace Genesis.RoomScan.World
{
    /// <summary>Detached canonical PRISM payload safe for worker-thread encoding.</summary>
    public sealed class PrismCanonicalChunkSnapshot
    {
        public int FilmCount;
        public int BoundaryCount;
        public uint FilmGeneration;
        public uint BoundaryGeneration;
        public ulong CalibrationEpoch;
        public byte[] FilmHeaders = Array.Empty<byte>();
        public byte[] FilmInformation = Array.Empty<byte>();
        public byte[] BoundaryHeaders = Array.Empty<byte>();
        public byte[] BoundaryInformation = Array.Empty<byte>();
    }

    /// <summary>
    /// Strict versioned binary codec for resumable ContactFilms. It deliberately stores
    /// canonical posterior statistics, not only a derived mesh, so a rehydrated chunk can
    /// continue the same pressure solve after a restart or revisit.
    /// </summary>
    public static class PrismCanonicalChunkCodec
    {
        public const int FormatVersion = 2;
        private const uint Magic = 0x33515043; // "CPQ3"
        private const uint EndianMarker = 0x01020304;
        private const int MaximumFilms = 1_000_000;
        private const int MaximumBoundaries = 2_000_000;

        public static bool TryValidate(PrismCanonicalChunkSnapshot snapshot,
            out string error)
        {
            error = Validate(snapshot);
            return error == null;
        }

        public static bool TryWrite(Stream stream, PrismCanonicalChunkSnapshot snapshot,
            out string error)
        {
            error = Validate(snapshot);
            if (error != null) return false;
            if (stream == null || !stream.CanWrite)
            {
                error = "PRISM destination is not writable.";
                return false;
            }
            try
            {
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(EndianMarker);
                writer.Write(ContactFilmHeaderGpu.Stride);
                writer.Write(ContactBoundaryHeaderGpu.Stride);
                writer.Write(snapshot.FilmCount);
                writer.Write(snapshot.BoundaryCount);
                writer.Write(snapshot.FilmGeneration);
                writer.Write(snapshot.BoundaryGeneration);
                writer.Write(snapshot.CalibrationEpoch);
                WriteBytes(writer, snapshot.FilmHeaders);
                WriteBytes(writer, snapshot.FilmInformation);
                WriteBytes(writer, snapshot.BoundaryHeaders);
                WriteBytes(writer, snapshot.BoundaryInformation);
                writer.Flush();
                return true;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is ArgumentException ||
                                              exception is OverflowException)
            {
                error = "PRISM serialization failed: " + exception.Message;
                return false;
            }
        }

        public static bool TryRead(Stream stream, out PrismCanonicalChunkSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = null;
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                error = "PRISM source must be readable and bounded.";
                return false;
            }
            try
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                if (reader.ReadUInt32() != Magic)
                    throw new InvalidDataException("PRISM magic is invalid.");
                if (reader.ReadInt32() != FormatVersion)
                    throw new InvalidDataException("Unsupported PRISM format version.");
                if (reader.ReadUInt32() != EndianMarker)
                    throw new InvalidDataException("PRISM endian marker is invalid.");
                if (reader.ReadInt32() != ContactFilmHeaderGpu.Stride ||
                    reader.ReadInt32() != ContactBoundaryHeaderGpu.Stride)
                    throw new InvalidDataException("PRISM GPU structure stride changed.");
                var candidate = new PrismCanonicalChunkSnapshot
                {
                    FilmCount = reader.ReadInt32(),
                    BoundaryCount = reader.ReadInt32(),
                    FilmGeneration = reader.ReadUInt32(),
                    BoundaryGeneration = reader.ReadUInt32(),
                    CalibrationEpoch = reader.ReadUInt64()
                };
                ValidateCounts(candidate.FilmCount, candidate.BoundaryCount);
                candidate.FilmHeaders = ReadBytes(reader,
                    checked(candidate.FilmCount * ContactFilmHeaderGpu.Stride));
                candidate.FilmInformation = ReadBytes(reader,
                    checked(candidate.FilmCount * 9 * sizeof(float) * 4));
                candidate.BoundaryHeaders = ReadBytes(reader,
                    checked(candidate.BoundaryCount * ContactBoundaryHeaderGpu.Stride));
                candidate.BoundaryInformation = ReadBytes(reader,
                    checked(candidate.BoundaryCount *
                        ContactBoundaryPool.InformationRecordsPerBoundary *
                        sizeof(float) * 4));
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("PRISM payload has trailing bytes.");
                error = Validate(candidate);
                if (error != null) return false;
                snapshot = candidate;
                return true;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is ArgumentException ||
                                              exception is OverflowException ||
                                              exception is EndOfStreamException)
            {
                error = "PRISM artifact rejected: " + exception.Message;
                return false;
            }
        }

        private static string Validate(PrismCanonicalChunkSnapshot snapshot)
        {
            if (snapshot == null) return "PRISM snapshot is null.";
            try { ValidateCounts(snapshot.FilmCount, snapshot.BoundaryCount); }
            catch (Exception exception) { return exception.Message; }
            if (!LengthIs(snapshot.FilmHeaders,
                    (long)snapshot.FilmCount * ContactFilmHeaderGpu.Stride) ||
                !LengthIs(snapshot.FilmInformation,
                    (long)snapshot.FilmCount * 9 * sizeof(float) * 4) ||
                !LengthIs(snapshot.BoundaryHeaders,
                    (long)snapshot.BoundaryCount * ContactBoundaryHeaderGpu.Stride) ||
                !LengthIs(snapshot.BoundaryInformation,
                    (long)snapshot.BoundaryCount *
                    ContactBoundaryPool.InformationRecordsPerBoundary *
                    sizeof(float) * 4))
                return "PRISM payload lengths do not match canonical counts.";
            if ((snapshot.FilmCount > 0 && snapshot.FilmGeneration == 0) ||
                (snapshot.BoundaryCount > 0 && snapshot.BoundaryGeneration == 0))
                return "PRISM live generations must be non-zero.";
            return null;
        }

        private static void ValidateCounts(int films, int boundaries)
        {
            if (films < 0 || films > MaximumFilms || boundaries < 0 ||
                boundaries > MaximumBoundaries)
                throw new InvalidDataException("PRISM canonical counts exceed limits.");
        }

        private static bool LengthIs(byte[] bytes, long expected) =>
            bytes != null && bytes.LongLength == expected;

        private static void WriteBytes(BinaryWriter writer, byte[] bytes)
        {
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static byte[] ReadBytes(BinaryReader reader, int expected)
        {
            int length = reader.ReadInt32();
            if (length != expected)
                throw new InvalidDataException("PRISM section length is invalid.");
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (remaining < length)
                throw new EndOfStreamException("PRISM section is truncated.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
    }
}
