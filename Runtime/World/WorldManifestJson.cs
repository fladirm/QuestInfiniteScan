using System;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// The only supported JSON entry point for world manifests. Callers receive a
    /// validated object or an explicit error; partially parsed documents never escape.
    /// </summary>
    public static class WorldManifestJson
    {
        public static bool TryDeserialize(string json, out WorldManifest manifest,
            out WorldValidationResult validation)
        {
            manifest = null;
            validation = new WorldValidationResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                validation.Add("$", "JSON is empty");
                return false;
            }
            if (json.Length > WorldSchema.MaximumJsonCharacters)
            {
                validation.Add("$", $"JSON exceeds {WorldSchema.MaximumJsonCharacters} characters");
                return false;
            }

            string trimmed = json.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{')
            {
                validation.Add("$", "JSON root must be an object");
                return false;
            }

            var parsed = new WorldManifest
            {
                schemaVersion = int.MinValue,
                worldId = null,
                displayName = null,
                createdUnixMilliseconds = -1,
                updatedUnixMilliseconds = -1,
                revision = -1,
                worldAnchorId = null,
                chunks = null,
                edges = null
            };
            try
            {
                JsonUtility.FromJsonOverwrite(json, parsed);
            }
            catch (Exception exception)
            {
                validation.Add("$", $"JSON parse failed: {exception.Message}");
                return false;
            }

            validation = WorldManifestValidator.Validate(parsed);
            if (!validation.IsValid)
                return false;
            manifest = parsed;
            return true;
        }

        public static bool TrySerialize(WorldManifest manifest, bool prettyPrint,
            out string json, out WorldValidationResult validation)
        {
            json = null;
            validation = WorldManifestValidator.Validate(manifest);
            if (!validation.IsValid)
                return false;

            try
            {
                json = JsonUtility.ToJson(manifest, prettyPrint);
            }
            catch (Exception exception)
            {
                validation.Add("$", $"JSON serialization failed: {exception.Message}");
                return false;
            }

            if (json.Length > WorldSchema.MaximumJsonCharacters)
            {
                validation.Add("$", $"serialized JSON exceeds {WorldSchema.MaximumJsonCharacters} characters");
                json = null;
                return false;
            }
            return true;
        }
    }
}
