using System;
using System.IO;
using Genesis.RoomScan.Exporting;
using Genesis.RoomScan.World;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    /// <summary>Batch entry point used by Tools/gltf/verify_interoperability.sh.</summary>
    public static class GlbInteroperabilityFixtureBuilder
    {
        public static void Build()
        {
            string directory = Environment.GetEnvironmentVariable("QIS_GLTF_FIXTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("QIS_GLTF_FIXTURE_DIR is required.");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);

            string firstPath = Path.Combine(directory, "chunk.glb");
            string secondPath = Path.Combine(directory, "chunk-second.glb");
            ChunkGlbWriteResult first = WriteChunk(firstPath, "chunk-interop");
            ChunkGlbWriteResult second = WriteChunk(secondPath, "chunk-second-interop");
            string worldPath = Path.Combine(directory, "world.glb");
            using (var stream = new FileStream(worldPath, FileMode.Create,
                       FileAccess.Write, FileShare.None, 1024 * 1024,
                       FileOptions.WriteThrough))
            {
                var chunks = new[]
                {
                    new WorldGlbChunkInput
                    {
                        ChunkId = "chunk-000000", Revision = 1,
                        WorldFromChunk = RigidPoseData.Identity,
                        GlbPath = firstPath, ChunkLayout = first
                    },
                    new WorldGlbChunkInput
                    {
                        ChunkId = "chunk-000001", Revision = 2,
                        WorldFromChunk = new RigidPoseData(new Vector3(2f, 3f, 4f),
                            Quaternion.Euler(0f, 90f, 0f)),
                        GlbPath = secondPath, ChunkLayout = second
                    }
                };
                if (!WorldGlbWriter.TryWrite(stream, chunks, new WorldGlbWriteOptions(),
                        out _, out string error))
                    throw new InvalidDataException(error);
                stream.Flush(true);
            }
            Logger.Info("GLB interoperability fixtures: " + directory);
        }

        private static ChunkGlbWriteResult WriteChunk(string path, string name)
        {
            var data = new ChunkGlbExportData
            {
                Name = name,
                Positions = new[]
                {
                    new Vector3(1f, 2f, 3f),
                    new Vector3(2f, 2f, 3f),
                    new Vector3(1f, 3f, 3f)
                },
                Normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                TexCoords0 = new[] { Vector2.zero, Vector2.right, Vector2.up },
                Indices = new[] { 0, 1, 2 },
                TextureWidth = 2,
                TextureHeight = 2,
                BaseColorRgba32 = new byte[]
                {
                    255, 0, 0, 255, 0, 255, 0, 255,
                    0, 0, 255, 255, 255, 255, 255, 255
                },
                NormalRgba32 = new byte[]
                {
                    128, 64, 255, 255, 128, 96, 255, 255,
                    128, 160, 255, 255, 128, 192, 255, 255
                }
            };
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.WriteThrough);
            if (!ChunkGlbWriter.TryWrite(stream, data, new ChunkGlbWriteOptions(),
                    out ChunkGlbWriteResult result, out string error))
                throw new InvalidDataException(error);
            stream.Flush(true);
            return result;
        }
    }
}
