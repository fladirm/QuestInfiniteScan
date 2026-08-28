using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal sealed class MerkabaChunkSnapshot
    {
        public int3 Coord;
        public KernelState[] States;
    }

    internal sealed class MerkabaSessionSnapshot
    {
        public Guid AnchorUuid;
        public Matrix4x4 AnchorAtSave = Matrix4x4.identity;
        public int IntegrationCount;
        public readonly List<MerkabaChunkSnapshot> Chunks = new();
    }

    /// <summary>
    /// Versioned deterministic persistence for canonical chunk state only. Derived masks,
    /// render records, normals, vertices, indices, and GPU residency are rebuilt on load.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaPersistence : MonoBehaviour
    {
        private const uint Magic = 0x47424B4Du; // MKBG little-endian
        private const int FormatVersion = 1;
        private const int MaximumChunkCount = 1_000_000;
        private const string SessionFileName = "merkaba-grid.bin";

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;

        public bool IsBusy { get; private set; }
        public bool SavedSessionExists => File.Exists(SessionPath);
        public string LastStatus { get; private set; } = "Not saved";
        public string SessionPath => Path.Combine(Application.persistentDataPath,
            "MerkabaScan", SessionFileName);
        public event Action StatusChanged;

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
        }

        public async Task<bool> SaveAsync()
        {
            if (IsBusy || _grid == null) return false;
            IsBusy = true;
            SetStatus("Saving…");
            try
            {
                if (_integrator != null)
                    await _integrator.SynchronizeCanonicalStateAsync();
                else
                    await _grid.SynchronizeResidentStateAsync();
                MerkabaSessionSnapshot snapshot = CaptureSnapshot(_grid,
                    _integrator != null ? _integrator.IntegrationCount : 0);
                string path = SessionPath;
                await Task.Run(() =>
                {
                    string directory = Path.GetDirectoryName(path);
                    Directory.CreateDirectory(directory);
                    string temporary = path + ".tmp";
                    using (var stream = new FileStream(temporary, FileMode.Create,
                               FileAccess.Write, FileShare.None, 1024 * 1024,
                               FileOptions.WriteThrough))
                    {
                        WriteSnapshot(stream, snapshot);
                        stream.Flush(true);
                    }
                    MerkabaFilePublishing.Publish(temporary, path);
                });
                foreach (MerkabaChunk chunk in _grid.Chunks.Values)
                    chunk.Persisted = true;
                SetStatus($"Saved {snapshot.Chunks.Count} chunks");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba save failed: " + exception);
                SetStatus("Save failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        public async Task<bool> LoadAsync()
        {
            if (IsBusy || _grid == null || !SavedSessionExists) return false;
            IsBusy = true;
            SetStatus("Loading…");
            try
            {
                string path = SessionPath;
                MerkabaSessionSnapshot snapshot = await Task.Run(() =>
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                    return ReadSnapshot(stream);
                });

                if (snapshot.AnchorUuid != Guid.Empty && RoomAnchorManager.Instance != null)
                {
                    Matrix4x4? localized = await RoomAnchorManager.Instance
                        .LoadSpatialAnchorAsync(snapshot.AnchorUuid);
                    if (localized.HasValue && RoomSpaceRoot.Instance != null)
                        await RoomSpaceRoot.WaitForBindAsync();
                    else if (!localized.HasValue)
                        Logger.Warning("Merkaba load: saved spatial anchor did not localize; " +
                            "using current MRUK/world frame fallback.");
                }

                ApplySnapshot(_grid, snapshot);
                _integrator?.RestoreIntegrationCount(snapshot.IntegrationCount);
                SetStatus($"Loaded {snapshot.Chunks.Count} chunks");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba load failed: " + exception);
                SetStatus("Load failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        public void ClearSavedSession()
        {
            if (IsBusy) return;
            try
            {
                if (File.Exists(SessionPath)) File.Delete(SessionPath);
                string temporary = SessionPath + ".tmp";
                if (File.Exists(temporary)) File.Delete(temporary);
                SetStatus("No saved session");
            }
            catch (Exception exception)
            {
                Logger.Error("Could not clear saved Merkaba session: " + exception.Message);
                SetStatus("Clear failed: " + exception.Message);
            }
        }

        internal static MerkabaSessionSnapshot CaptureSnapshot(MerkabaGrid grid,
            int integrationCount)
        {
            var snapshot = new MerkabaSessionSnapshot
            {
                IntegrationCount = Mathf.Max(0, integrationCount)
            };
            RoomAnchorManager anchor = RoomAnchorManager.Instance;
            if (anchor != null && anchor.HasSpatialAnchor)
            {
                snapshot.AnchorUuid = anchor.SpatialAnchorUuid;
                snapshot.AnchorAtSave = anchor.SpatialAnchorMatrix;
            }

            foreach (MerkabaChunk chunk in grid.ChunksSorted())
            {
                bool hasCanonicalState = false;
                foreach (KernelState state in chunk.States)
                {
                    if (state.OccupancyEvidence != 0 || state.PackedColor != 0 ||
                        state.ColorConfidence != 0 || state.Flags != 0)
                    {
                        hasCanonicalState = true;
                        break;
                    }
                }
                if (!hasCanonicalState) continue;
                var states = new KernelState[MerkabaConstants.KernelsPerChunk];
                Array.Copy(chunk.States, states, states.Length);
                snapshot.Chunks.Add(new MerkabaChunkSnapshot
                {
                    Coord = chunk.Coord,
                    States = states
                });
            }
            return snapshot;
        }

        internal static void ApplySnapshot(MerkabaGrid grid, MerkabaSessionSnapshot snapshot)
        {
            grid.Clear();
            foreach (MerkabaChunkSnapshot source in snapshot.Chunks)
            {
                MerkabaChunk chunk = grid.GetOrCreateChunk(source.Coord);
                Array.Copy(source.States, chunk.States, chunk.States.Length);
                chunk.CpuStateCurrent = true;
                chunk.Persisted = true;
            }
            grid.RecountOccupied();
        }

        internal static void WriteSnapshot(Stream destination, MerkabaSessionSnapshot snapshot)
        {
            if (destination == null || !destination.CanWrite)
                throw new ArgumentException("Destination must be writable.", nameof(destination));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Chunks.Count > MaximumChunkCount)
                throw new InvalidDataException("Merkaba snapshot has too many chunks.");

            using var writer = new BinaryWriter(destination, new UTF8Encoding(false), true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(MerkabaConstants.SupportSize);
            writer.Write(MerkabaConstants.LatticeStep);
            writer.Write(MerkabaConstants.ChunkSize);
            writer.Write(snapshot.AnchorUuid.ToByteArray());
            for (int i = 0; i < 16; i++) writer.Write(snapshot.AnchorAtSave[i]);
            writer.Write(snapshot.IntegrationCount);
            writer.Write(snapshot.Chunks.Count);

            int3 previous = default;
            bool first = true;
            foreach (MerkabaChunkSnapshot chunk in snapshot.Chunks)
            {
                if (chunk?.States == null ||
                    chunk.States.Length != MerkabaConstants.KernelsPerChunk)
                    throw new InvalidDataException("Merkaba chunk payload size is invalid.");
                if (!first && Compare(chunk.Coord, previous) <= 0)
                    throw new InvalidDataException("Merkaba chunks are not strictly sorted.");
                first = false;
                previous = chunk.Coord;
                writer.Write(chunk.Coord.x);
                writer.Write(chunk.Coord.y);
                writer.Write(chunk.Coord.z);
                writer.Write(MerkabaConstants.KernelsPerChunk);
                foreach (KernelState state in chunk.States)
                {
                    writer.Write(state.OccupancyEvidence);
                    writer.Write(state.PackedColor);
                    writer.Write(state.ColorConfidence);
                    writer.Write(state.Flags & MerkabaConstants.OccupiedFlag);
                }
            }
            writer.Flush();
        }

        internal static MerkabaSessionSnapshot ReadSnapshot(Stream source)
        {
            if (source == null || !source.CanRead)
                throw new ArgumentException("Source must be readable.", nameof(source));
            using var reader = new BinaryReader(source, Encoding.UTF8, true);
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Bad Merkaba magic.");
            if (reader.ReadInt32() != FormatVersion)
                throw new InvalidDataException("Unsupported Merkaba format version.");
            if (reader.ReadSingle() != MerkabaConstants.SupportSize ||
                reader.ReadSingle() != MerkabaConstants.LatticeStep ||
                reader.ReadInt32() != MerkabaConstants.ChunkSize)
                throw new InvalidDataException("Merkaba geometry constants do not match.");

            var snapshot = new MerkabaSessionSnapshot
            {
                AnchorUuid = new Guid(ReadExact(reader, 16))
            };
            Matrix4x4 matrix = default;
            for (int i = 0; i < 16; i++) matrix[i] = reader.ReadSingle();
            snapshot.AnchorAtSave = matrix;
            snapshot.IntegrationCount = reader.ReadInt32();
            if (snapshot.IntegrationCount < 0)
                throw new InvalidDataException("Negative integration count.");
            int chunkCount = reader.ReadInt32();
            if (chunkCount < 0 || chunkCount > MaximumChunkCount)
                throw new InvalidDataException("Merkaba chunk count is invalid.");

            var seen = new HashSet<int3>();
            int3 previous = default;
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                int3 coord = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                if (!seen.Add(coord) || (chunkIndex > 0 && Compare(coord, previous) <= 0))
                    throw new InvalidDataException("Merkaba chunk coordinates are duplicated/unsorted.");
                previous = coord;
                if (reader.ReadInt32() != MerkabaConstants.KernelsPerChunk)
                    throw new InvalidDataException("Merkaba kernel count is invalid.");
                var states = new KernelState[MerkabaConstants.KernelsPerChunk];
                for (int i = 0; i < states.Length; i++)
                {
                    states[i].OccupancyEvidence = reader.ReadInt32();
                    states[i].PackedColor = reader.ReadUInt32();
                    states[i].ColorConfidence = reader.ReadUInt32();
                    states[i].Flags = reader.ReadUInt32();
                    Validate(states[i]);
                }
                snapshot.Chunks.Add(new MerkabaChunkSnapshot
                {
                    Coord = coord,
                    States = states
                });
            }
            if (source.CanSeek && source.Position != source.Length)
                throw new InvalidDataException("Merkaba file has trailing bytes.");
            return snapshot;
        }

        private static void Validate(KernelState state)
        {
            if (state.OccupancyEvidence < MerkabaConstants.MinimumEvidence ||
                state.OccupancyEvidence > MerkabaConstants.MaximumEvidence ||
                state.ColorConfidence > MerkabaConstants.MaximumColorConfidence ||
                (state.Flags & ~MerkabaConstants.OccupiedFlag) != 0)
                throw new InvalidDataException("Merkaba kernel state is out of range.");
            if (state.IsOccupied &&
                state.OccupancyEvidence <= MerkabaConstants.OccupiedOffThreshold)
                throw new InvalidDataException("Occupied kernel is below the OFF threshold.");
            if (!state.IsOccupied &&
                state.OccupancyEvidence >= MerkabaConstants.OccupiedOnThreshold)
                throw new InvalidDataException("Empty kernel is above the ON threshold.");
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            return bytes;
        }

        private static int Compare(int3 left, int3 right)
        {
            if (left.x != right.x) return left.x.CompareTo(right.x);
            if (left.y != right.y) return left.y.CompareTo(right.y);
            return left.z.CompareTo(right.z);
        }

        private void SetStatus(string status)
        {
            LastStatus = status;
            StatusChanged?.Invoke();
        }
    }

    internal static class MerkabaFilePublishing
    {
        public static void Publish(string temporary, string destination)
        {
            if (!File.Exists(destination))
            {
                File.Move(temporary, destination);
                return;
            }

            string backup = destination + ".bak";
            try
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temporary, destination, backup, true);
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temporary, destination, true);
                File.Delete(temporary);
            }
        }
    }
}
