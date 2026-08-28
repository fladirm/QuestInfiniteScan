using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>On-demand offline GLB PBR readout of the canonical Merkaba grid.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaExporter : MonoBehaviour
    {
        private const string ExportFileName = "QuestMerkabaScan.glb";

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;

        public bool IsExporting { get; private set; }
        public string LastExportPath { get; private set; }
        public string LastStatus { get; private set; } = "Not exported";
        public event Action StatusChanged;

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
        }

        public async Task<bool> ExportGlbAsync()
        {
            if (IsExporting || _grid == null) return false;
            IsExporting = true;
            SetStatus("Exporting GLB…");
            try
            {
                if (_integrator != null)
                    await _integrator.SynchronizeCanonicalStateAsync();
                else
                    await _grid.SynchronizeResidentStateAsync();
                List<MerkabaKernelSnapshot> kernels = _grid.OccupiedKernelsSorted().ToList();
                if (kernels.Count == 0)
                    throw new InvalidOperationException("The Merkaba grid has no occupied kernels.");

                string directory = Path.Combine(Application.persistentDataPath,
                    "MerkabaScan", "exports");
                string destination = Path.Combine(directory, ExportFileName);
                MerkabaGlbResult result = await Task.Run(() =>
                {
                    Directory.CreateDirectory(directory);
                    string temporary = destination + ".tmp";
                    MerkabaGlbResult written;
                    using (var stream = new FileStream(temporary, FileMode.Create,
                               FileAccess.Write, FileShare.None, 1024 * 1024,
                               FileOptions.WriteThrough))
                    {
                        written = MerkabaGlbWriter.Write(stream, kernels);
                        stream.Flush(true);
                    }
                    MerkabaFilePublishing.Publish(temporary, destination);
                    return written;
                });
                LastExportPath = destination;
                SetStatus($"GLB: {result.VertexCount} vertices, {result.ByteLength} bytes");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba GLB export failed: " + exception);
                SetStatus("Export failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsExporting = false;
                StatusChanged?.Invoke();
            }
        }

        private void SetStatus(string status)
        {
            LastStatus = status;
            StatusChanged?.Invoke();
        }
    }
}
