using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Automatically saves camera keyframes (JPEG + pose + intrinsics) to disk during scanning.
    /// Uses motion-based selection to avoid redundant captures. The export folder is always
    /// ready for adb pull and subsequent Gaussian Splat training.
    /// </summary>
    public class KeyframeCollector : MonoBehaviour
    {
        [SerializeField, Tooltip("Min translation (m) from any saved keyframe to trigger a new capture")]
        private float moveThreshold = 0.15f;

        [SerializeField, Tooltip("Min rotation (deg) from any saved keyframe to trigger a new capture")]
        private float rotateThresholdDeg = 10f;

        [SerializeField, Range(50, 100)]
        private int jpegQuality = 95;

        [SerializeField, Tooltip("Max angular velocity (deg/s) to accept a frame (rejects motion blur)")]
        private float maxAngularVelocity = 120f;

        [SerializeField, Tooltip("Min seconds between captures to prevent burst saves")]
        private float minCaptureInterval = 0.25f;

        private string _exportDir;
        private string _imagesDir;
        private string _manifestPath;

        private readonly List<Vector3> _savedPositions = new();
        private readonly List<Quaternion> _savedRotations = new();
        private int _nextId;
        private int _pendingWrites;
        private Quaternion _prevRot;
        private float _prevRotTime;
        private float _lastCaptureTime;
        private bool _initialized;
        private readonly object _manifestGate = new();
        private string _chunkId = string.Empty;
        private int _chunkRevision;
        private RigidPoseData _worldFromCaptureFrame = RigidPoseData.Identity;
        private bool _captureInChunkSpace;
        private bool _captureEnabled = true;

        /// <summary>Number of keyframes saved so far in this session.</summary>
        public int SavedCount => _nextId;

        public int PendingWriteCount => Volatile.Read(ref _pendingWrites);

        public string ChunkId => _chunkId;

        public bool CaptureEnabled
        {
            get => _captureEnabled;
            set => _captureEnabled = value;
        }

        /// <summary>Absolute path to the keyframe export directory on device.</summary>
        public string ExportDirectory => _exportDir;

        private void Start()
        {
            _prevRot = Quaternion.identity;
            _prevRotTime = Time.time;
            _initialized = true;

        }

        /// <summary>
        /// Sets the keyframe export directory. Creates the directory structure if needed.
        /// Pass null to disable keyframe capture.
        /// </summary>
        public void SetExportDirectory(string dir)
        {
            _chunkId = string.Empty;
            _chunkRevision = 0;
            _worldFromCaptureFrame = RigidPoseData.Identity;
            _captureInChunkSpace = false;
            ConfigureDirectory(dir);
        }

        /// <summary>
        /// Routes subsequent frames into one chunk working directory and stores camera poses
        /// in that chunk's local coordinate frame. Switching chunks resets motion selection;
        /// append mode keeps monotonically increasing image IDs for revisit observations.
        /// Call only after <see cref="WaitForPendingWritesAsync"/> succeeds.
        /// </summary>
        public void SetChunkContext(string dir, string chunkId, int chunkRevision,
            RigidPoseData worldFromChunk, bool appendExisting)
        {
            if (string.IsNullOrEmpty(chunkId))
                throw new ArgumentException("Chunk identifier is required.", nameof(chunkId));
            _chunkId = chunkId;
            _chunkRevision = chunkRevision;
            _worldFromCaptureFrame = worldFromChunk;
            _captureInChunkSpace = true;
            _savedPositions.Clear();
            _savedRotations.Clear();
            _nextId = 0;
            ConfigureDirectory(dir);
            if (appendExisting && !string.IsNullOrEmpty(_manifestPath))
                _nextId = FindNextFrameId(_manifestPath);
        }

        /// <summary>
        /// Updates only the world placement of the current immutable chunk frame after a
        /// pose-graph correction. Existing and future keyframe poses remain chunk-local;
        /// capture numbering and motion selection are intentionally not reset.
        /// </summary>
        public bool TryUpdateChunkWorldPose(string chunkId,
            RigidPoseData worldFromChunk)
        {
            if (!_captureInChunkSpace || string.IsNullOrEmpty(chunkId) ||
                !string.Equals(_chunkId, chunkId, StringComparison.Ordinal))
                return false;
            Vector3 position = worldFromChunk.position;
            Quaternion rotation = worldFromChunk.rotation;
            float norm = rotation.x * rotation.x + rotation.y * rotation.y +
                         rotation.z * rotation.z + rotation.w * rotation.w;
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) ||
                !Finite(norm) || Mathf.Abs(norm - 1f) > 0.01f)
                return false;
            _worldFromCaptureFrame = worldFromChunk;
            return true;
        }

        private void ConfigureDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                _exportDir = null;
                _imagesDir = null;
                _manifestPath = null;
                return;
            }

            _exportDir = dir;
            _imagesDir = Path.Combine(dir, "images");
            _manifestPath = Path.Combine(dir, "frames.jsonl");
            Directory.CreateDirectory(_imagesDir);
            Logger.Info($"KeyframeCollector: export dir={_exportDir}");
        }

        /// <summary>
        /// Called by RoomScanner each integration tick with the current camera data.
        /// Determines whether to save a new keyframe based on motion thresholds.
        /// </summary>
        public void TrySaveKeyframe(Texture frame, Vector3 pos, Quaternion rot,
            Vector2 focalLen, Vector2 principalPt, Vector2 sensorRes, Vector2 currentRes)
        {
            if (!_initialized || !_captureEnabled || frame == null || _exportDir == null) return;

            if (Time.time - _lastCaptureTime < minCaptureInterval) return;

            float dt = Time.time - _prevRotTime;
            if (dt > 0.001f)
            {
                float angVel = Quaternion.Angle(_prevRot, rot) / dt;
                _prevRot = rot;
                _prevRotTime = Time.time;
                if (angVel > maxAngularVelocity) return;
            }

            Pose capturePose = _captureInChunkSpace
                ? ConvertWorldPoseToFrame(new Pose(pos, rot), _worldFromCaptureFrame)
                : new Pose(pos, rot);

            if (!ShouldCapture(capturePose.position, capturePose.rotation)) return;

            int id = _nextId++;
            _savedPositions.Add(capturePose.position);
            _savedRotations.Add(capturePose.rotation);
            _lastCaptureTime = Time.time;

            float timestamp = Time.realtimeSinceStartup;
            string imagesDir = _imagesDir;
            string manifestPath = _manifestPath;
            string chunkId = _chunkId;
            int chunkRevision = _chunkRevision;

            if (frame is RenderTexture rt)
            {
                Interlocked.Increment(ref _pendingWrites);
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                    OnReadbackComplete(req, id, timestamp, capturePose.position,
                        capturePose.rotation, focalLen, principalPt, sensorRes, currentRes,
                        imagesDir, manifestPath, chunkId, chunkRevision));
            }
            else if (frame is Texture2D tex2d)
            {
                Interlocked.Increment(ref _pendingWrites);
                try
                {
                    QueueKeyframeWrite(tex2d.EncodeToJPG(jpegQuality), id, timestamp,
                        capturePose.position, capturePose.rotation, focalLen, principalPt,
                        sensorRes, currentRes, imagesDir, manifestPath, chunkId, chunkRevision);
                }
                catch (Exception exception)
                {
                    Logger.Error($"KeyframeCollector: encode error frame {id}: " +
                                 exception.Message);
                    Interlocked.Decrement(ref _pendingWrites);
                }
            }
        }

        private bool ShouldCapture(Vector3 pos, Quaternion rot)
        {
            for (int i = 0; i < _savedPositions.Count; i++)
            {
                float dist = Vector3.Distance(pos, _savedPositions[i]);
                float angle = Quaternion.Angle(rot, _savedRotations[i]);
                if (dist < moveThreshold && angle < rotateThresholdDeg)
                    return false;
            }
            return true;
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest req, int id, float timestamp,
            Vector3 pos, Quaternion rot, Vector2 focalLen, Vector2 principalPt,
            Vector2 sensorRes, Vector2 currentRes, string imagesDir, string manifestPath,
            string chunkId, int chunkRevision)
        {
            if (req.hasError)
            {
                Logger.Warning($"KeyframeCollector: readback error for frame {id}");
                Interlocked.Decrement(ref _pendingWrites);
                return;
            }

            try
            {
                var data = req.GetData<byte>();
                var tex = new Texture2D(req.width, req.height, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(data);
                tex.Apply();
                byte[] jpg = tex.EncodeToJPG(jpegQuality);
                Destroy(tex);

                QueueKeyframeWrite(jpg, id, timestamp, pos, rot,
                    focalLen, principalPt, sensorRes, currentRes, imagesDir,
                    manifestPath, chunkId, chunkRevision);
            }
            catch (Exception e)
            {
                Logger.Error($"KeyframeCollector: encode error frame {id}: {e.Message}");
                Interlocked.Decrement(ref _pendingWrites);
            }
        }

        private void QueueKeyframeWrite(byte[] jpgBytes, int id, float timestamp,
            Vector3 pos, Quaternion rot, Vector2 focalLen, Vector2 principalPt,
            Vector2 sensorRes, Vector2 currentRes, string imagesDir, string manifestPath,
            string chunkId, int chunkRevision)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(imagesDir) || string.IsNullOrEmpty(manifestPath))
                        throw new InvalidOperationException("Keyframe destination changed or closed.");
                    Directory.CreateDirectory(imagesDir);
                    string imgPath = Path.Combine(imagesDir, $"{id:D6}.jpg");
                    File.WriteAllBytes(imgPath, jpgBytes);

                    var sb = new StringBuilder(256);
                    sb.Append("{\"id\":").Append(id);
                    sb.Append(",\"ts\":").Append(timestamp.ToString("F3", CultureInfo.InvariantCulture));
                    if (!string.IsNullOrEmpty(chunkId))
                    {
                        sb.Append(",\"space\":\"chunk\"");
                        sb.Append(",\"chunk\":\"").Append(chunkId).Append('"');
                        sb.Append(",\"revision\":").Append(chunkRevision);
                    }
                    sb.Append(",\"px\":").Append(pos.x.ToString("F6", CultureInfo.InvariantCulture));
                    sb.Append(",\"py\":").Append(pos.y.ToString("F6", CultureInfo.InvariantCulture));
                    sb.Append(",\"pz\":").Append(pos.z.ToString("F6", CultureInfo.InvariantCulture));
                    sb.Append(",\"qx\":").Append(rot.x.ToString("F6", CultureInfo.InvariantCulture));
                    sb.Append(",\"qy\":").Append(rot.y.ToString("F6", CultureInfo.InvariantCulture));
                    sb.Append(",\"qz\":").Append(rot.z.ToString("F6", CultureInfo.InvariantCulture));
                    sb.Append(",\"qw\":").Append(rot.w.ToString("F6", CultureInfo.InvariantCulture));
                    sb.Append(",\"fx\":").Append(focalLen.x.ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"fy\":").Append(focalLen.y.ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"cx\":").Append(principalPt.x.ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"cy\":").Append(principalPt.y.ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"sw\":").Append((int)sensorRes.x);
                    sb.Append(",\"sh\":").Append((int)sensorRes.y);
                    sb.Append(",\"w\":").Append((int)currentRes.x);
                    sb.Append(",\"h\":").Append((int)currentRes.y);
                    sb.Append('}');

                    lock (_manifestGate)
                    {
                        File.AppendAllText(manifestPath, sb.ToString() + "\n");
                    }

                    if (id < 5 || id % 50 == 0)
                        Logger.Info($"KeyframeCollector: saved frame {id} ({jpgBytes.Length / 1024}KB)");
                }
                catch (Exception e)
                {
                    Logger.Error($"KeyframeCollector: write error frame {id}: {e.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingWrites);
                }
            });
        }

        /// <summary>
        /// Saves a pre-captured JPEG as a keyframe unconditionally (no motion/interval gates).
        /// Used by detection modules that need to capture the frame before async processing
        /// and only decide to save after results are known.
        /// Returns the assigned keyframe ID, or -1 if export is not configured.
        /// </summary>
        public int SaveCapturedKeyframe(byte[] jpgBytes, float timestamp,
            Vector3 pos, Quaternion rot, Vector2 focalLen, Vector2 principalPt,
            Vector2 sensorRes, Vector2 currentRes)
        {
            if (!_captureEnabled || _exportDir == null || jpgBytes == null ||
                jpgBytes.Length == 0) return -1;
            Pose capturePose = _captureInChunkSpace
                ? ConvertWorldPoseToFrame(new Pose(pos, rot), _worldFromCaptureFrame)
                : new Pose(pos, rot);
            int id = _nextId++;
            _savedPositions.Add(capturePose.position);
            _savedRotations.Add(capturePose.rotation);
            Interlocked.Increment(ref _pendingWrites);
            QueueKeyframeWrite(jpgBytes, id, timestamp, capturePose.position,
                capturePose.rotation, focalLen, principalPt, sensorRes, currentRes,
                _imagesDir, _manifestPath, _chunkId, _chunkRevision);
            return id;
        }

        public async Task<bool> WaitForPendingWritesAsync(int timeoutMilliseconds = 30_000)
        {
            if (timeoutMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            var stopwatch = Stopwatch.StartNew();
            while (Volatile.Read(ref _pendingWrites) > 0)
            {
                if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                    return false;
                await Task.Delay(10);
            }
            return true;
        }

        public static Pose ConvertWorldPoseToFrame(Pose cameraWorld,
            RigidPoseData worldFromFrame)
        {
            RigidPoseData frameFromWorld = worldFromFrame.Inverse();
            return new Pose(frameFromWorld.TransformPoint(cameraWorld.position),
                frameFromWorld.rotation * cameraWorld.rotation);
        }

        /// <summary>
        /// Clears in-memory state only. Call before background file deletion.
        /// </summary>
        public void ClearInMemory()
        {
            _savedPositions.Clear();
            _savedRotations.Clear();
            _nextId = 0;
        }

        private static int FindNextFrameId(string manifestPath)
        {
            if (!File.Exists(manifestPath))
                return 0;
            int maximum = -1;
            foreach (string line in File.ReadLines(manifestPath))
            {
                int marker = line.IndexOf("\"id\":", StringComparison.Ordinal);
                if (marker < 0)
                    continue;
                marker += 5;
                int end = marker;
                while (end < line.Length && char.IsDigit(line[end]))
                    end++;
                if (end > marker && int.TryParse(line.Substring(marker, end - marker),
                        NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                    maximum = Math.Max(maximum, id);
            }
            return maximum == int.MaxValue ? int.MaxValue : maximum + 1;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

    }
}
