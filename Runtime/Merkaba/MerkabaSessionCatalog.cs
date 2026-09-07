using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Genesis.RoomScan
{
    [Serializable]
    public sealed class MerkabaSessionInfo
    {
        public int formatVersion = 1;
        public string sessionId;
        public string displayName;
        public string createdUtc;
        public string modifiedUtc;
        public string anchorUuid;
        public string thumbnailPath;

        public Guid Id => Guid.TryParse(sessionId, out Guid value)
            ? value : Guid.Empty;
        public Guid AnchorId => Guid.TryParse(anchorUuid, out Guid value)
            ? value : Guid.Empty;
    }

    /// <summary>
    /// Durable names around REV-B session directories. A present manifest is
    /// always validated against the metadata identity; metadata-only directories
    /// are valid unsaved sessions and no historical format is migrated.
    /// </summary>
    internal sealed class MerkabaSessionCatalog
    {
        internal const int FormatVersion = 1;
        internal const string MetadataFileName = "session.json";
        internal const string DesignFileName = "design.json";
        internal const string AnnotationsFileName = "annotations.json";

        private readonly string _applicationRoot;
        private readonly string _sessionsRoot;

        internal MerkabaSessionCatalog(string applicationRoot)
        {
            if (string.IsNullOrWhiteSpace(applicationRoot))
                throw new ArgumentException("Application storage root is required.",
                    nameof(applicationRoot));
            _applicationRoot = Path.GetFullPath(applicationRoot);
            _sessionsRoot = Path.Combine(_applicationRoot, "sessions");
        }

        internal string SessionsRoot => _sessionsRoot;
        internal string LibraryRoot => Path.Combine(_applicationRoot,
            "library");
        internal string SessionDirectory(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Session ID is required.",
                    nameof(sessionId));
            return Path.Combine(_sessionsRoot, sessionId.ToString("N"));
        }

        internal IReadOnlyList<MerkabaSessionInfo> List()
        {
            Directory.CreateDirectory(_sessionsRoot);
            var sessions = new List<MerkabaSessionInfo>();
            string[] directories = Directory.GetDirectories(_sessionsRoot);
            Array.Sort(directories, StringComparer.Ordinal);
            foreach (string directory in directories)
            {
                try
                {
                    MerkabaSessionInfo session = ReadDirectory(directory);
                    if (session != null) sessions.Add(session);
                }
                catch (Exception exception)
                {
                    Logger.Warning($"Ignoring invalid scan session " +
                        $"'{Path.GetFileName(directory)}': {exception.Message}");
                }
            }
            sessions.Sort(CompareNewestFirst);
            return sessions;
        }

        internal MerkabaSessionInfo Create(Guid anchorUuid,
            string displayName = null)
        {
            if (anchorUuid == Guid.Empty)
                throw new ArgumentException(
                    "A session requires its persisted room anchor UUID.",
                    nameof(anchorUuid));
            Directory.CreateDirectory(_sessionsRoot);
            Guid id;
            string directory;
            do
            {
                id = Guid.NewGuid();
                directory = SessionDirectory(id);
            } while (Directory.Exists(directory));
            Directory.CreateDirectory(directory);
            string now = UtcNow();
            var session = new MerkabaSessionInfo
            {
                formatVersion = FormatVersion,
                sessionId = id.ToString("D"),
                displayName = NormalizeName(displayName, now),
                createdUtc = now,
                modifiedUtc = now,
                anchorUuid = anchorUuid.ToString("D"),
                thumbnailPath = string.Empty
            };
            Write(session);
            return session;
        }

        internal MerkabaSessionInfo Read(Guid sessionId)
        {
            MerkabaSessionInfo session = ReadDirectory(
                SessionDirectory(sessionId));
            return session ?? throw new FileNotFoundException(
                "Scan session metadata was not found.", SessionDirectory(
                    sessionId));
        }

        internal void MarkSaved(MerkabaSessionInfo session)
        {
            string directory = SessionDirectory(session.Id);
            Validate(session, directory);
            MerkabaSessionManifest manifest =
                MerkabaSsdStore.ReadManifestFromDirectory(directory);
            if (manifest.SessionUuid != session.Id ||
                manifest.AnchorUuid != session.AnchorId)
                throw new InvalidDataException(
                    "Session metadata and committed manifest identity differ.");
            session.modifiedUtc = UtcNow();
            Write(session);
        }

        internal void Rename(MerkabaSessionInfo session, string displayName)
        {
            Validate(session, SessionDirectory(session.Id));
            session.displayName = NormalizeName(displayName,
                session.createdUtc);
            session.modifiedUtc = UtcNow();
            Write(session);
        }

        internal void Delete(Guid sessionId)
        {
            string directory = SessionDirectory(sessionId);
            if (!Directory.Exists(directory)) return;
            ValidateContainedDirectory(directory);
            Directory.Delete(directory, true);
        }

        private MerkabaSessionInfo ReadDirectory(string directory)
        {
            string metadataPath = Path.Combine(directory, MetadataFileName);
            if (!File.Exists(metadataPath)) return null;
            string json = File.ReadAllText(metadataPath, Encoding.UTF8);
            MerkabaSessionInfo session = JsonUtility.FromJson<
                MerkabaSessionInfo>(json);
            Validate(session, directory);
            string manifestPath = Path.Combine(directory,
                MerkabaSsdStore.ManifestFileName);
            if (File.Exists(manifestPath))
            {
                MerkabaSessionManifest manifest =
                    MerkabaSsdStore.ReadManifestFromDirectory(directory);
                if (manifest.SessionUuid != session.Id ||
                    manifest.AnchorUuid != session.AnchorId)
                    throw new InvalidDataException(
                        "Session metadata and committed manifest identity differ.");
            }
            return session;
        }

        private void Write(MerkabaSessionInfo session)
        {
            string directory = SessionDirectory(session.Id);
            Validate(session, directory);
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, MetadataFileName);
            string temporary = destination + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(
                JsonUtility.ToJson(session, true) + "\n");
            using (var stream = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 16 * 1024,
                       FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            MerkabaFilePublishing.Publish(temporary, destination);
        }

        private void Validate(MerkabaSessionInfo session, string directory)
        {
            if (session == null || session.formatVersion != FormatVersion ||
                session.Id == Guid.Empty || session.AnchorId == Guid.Empty)
                throw new InvalidDataException("Invalid scan session metadata.");
            ValidateContainedDirectory(directory);
            if (!string.Equals(Path.GetFileName(directory),
                    session.Id.ToString("N"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Session metadata does not match its directory.");
            session.displayName = NormalizeName(session.displayName,
                session.createdUtc);
        }

        private void ValidateContainedDirectory(string directory)
        {
            string full = Path.GetFullPath(directory).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(full);
            if (!string.Equals(parent, _sessionsRoot,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Session path escapes the catalog root.");
        }

        private static string NormalizeName(string name, string timestamp)
        {
            string value = string.IsNullOrWhiteSpace(name)
                ? "Scan " + DisplayTimestamp(timestamp)
                : name.Trim();
            return value.Length <= 80 ? value : value.Substring(0, 80);
        }

        private static string DisplayTimestamp(string timestamp)
        {
            if (DateTime.TryParse(timestamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime value))
                return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture);
            return "Untitled";
        }

        private static string UtcNow() => DateTime.UtcNow.ToString("O",
            CultureInfo.InvariantCulture);

        private static int CompareNewestFirst(MerkabaSessionInfo left,
            MerkabaSessionInfo right)
        {
            int modified = string.CompareOrdinal(right.modifiedUtc,
                left.modifiedUtc);
            if (modified != 0) return modified;
            int name = string.Compare(left.displayName, right.displayName,
                StringComparison.Ordinal);
            return name != 0 ? name : left.Id.CompareTo(right.Id);
        }
    }
}
