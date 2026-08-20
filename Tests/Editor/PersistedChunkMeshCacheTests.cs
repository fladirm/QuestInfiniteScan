using System;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class PersistedChunkMeshCacheTests
    {
        [Test]
        public void CacheBuildsLocalMeshAppliesChunkPoseAndEvictsFarthest()
        {
            Shader shader = Shader.Find("Genesis/RoomScan/PersistedChunkVertexColor");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var root = new GameObject("Chunk cache test");
            try
            {
                var cache = root.AddComponent<PersistedChunkMeshCache>();
                cache.Initialize(material, 2);
                ChunkLiveMeshSnapshot snapshot = CreateTriangle();
                ChunkRecord a = CreateChunk("chunk-000000", 0f);
                ChunkRecord b = CreateChunk("chunk-000001", 10f);
                ChunkRecord c = CreateChunk("chunk-000002", 1f);

                Assert.That(cache.TryPromote(a, snapshot, out string aError), Is.True, aError);
                Assert.That(cache.TryPromote(b, snapshot, out string bError), Is.True, bError);
                Assert.That(cache.TryPromote(c, snapshot, out string cError), Is.True, cError);

                Assert.That(cache.Count, Is.EqualTo(2));
                Assert.That(cache.Contains(a.chunkId), Is.True);
                Assert.That(cache.Contains(b.chunkId), Is.False,
                    "chunk farthest from the newly promoted/camera fallback pose must be evicted");
                Assert.That(cache.Contains(c.chunkId), Is.True);

                Transform cTransform = root.transform.Find("Persisted " + c.chunkId);
                Assert.That(cTransform, Is.Not.Null);
                Assert.That(cTransform.position.x, Is.EqualTo(1f).Within(0.0001f));
                Mesh mesh = cTransform.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.EqualTo(3));
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo(3));
                Assert.That(mesh.bounds, Is.EqualTo(snapshot.LocalBounds.ToUnityBounds()));

                MeshRenderer cachedRenderer = cTransform.GetComponent<MeshRenderer>();
                cache.SetRenderMode(ScanRenderMode.None);
                Assert.That(cachedRenderer.enabled, Is.False);
                cache.SetRenderMode(ScanRenderMode.Wireframe);
                Assert.That(cachedRenderer.enabled, Is.True,
                    "cached and active chunks must follow one render-mode state");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CacheKeepsIndexedMeshUntilWireframeActuallyNeedsBarycentrics()
        {
            Shader shader = Shader.Find("Genesis/RoomScan/PersistedChunkVertexColor");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var root = new GameObject("Chunk representation test");
            try
            {
                var cache = root.AddComponent<PersistedChunkMeshCache>();
                cache.Initialize(material, 1);
                ChunkRecord chunk = CreateChunk("chunk-000000", 0f);

                Assert.That(cache.TryPromote(chunk, CreateSharedQuad(), out string error),
                    Is.True, error);
                MeshFilter filter = root.transform.Find("Persisted " + chunk.chunkId)
                    .GetComponent<MeshFilter>();
                Assert.That(filter.sharedMesh.vertexCount, Is.EqualTo(4));
                Assert.That(filter.sharedMesh.GetIndexCount(0), Is.EqualTo(6));

                cache.SetRenderMode(ScanRenderMode.Wireframe);
                Assert.That(filter.sharedMesh.vertexCount, Is.EqualTo(6),
                    "wireframe expands only its six triangle corners");

                cache.SetRenderMode(ScanRenderMode.Vertex);
                Assert.That(filter.sharedMesh.vertexCount, Is.EqualTo(4),
                    "leaving wireframe restores compact indexed topology");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static ChunkRecord CreateChunk(string id, float x)
        {
            return new ChunkRecord
            {
                chunkId = id,
                state = ChunkLifecycleState.Persisted,
                worldFromChunk = new RigidPoseData(new Vector3(x, 0f, 0f),
                    Quaternion.identity)
            };
        }

        private static ChunkLiveMeshSnapshot CreateTriangle()
        {
            var vertices = new byte[3 * ChunkLiveMeshSnapshot.VertexStride];
            WriteVertex(vertices, 0, new Vector3(0f, 0f, 0f));
            WriteVertex(vertices, 1, new Vector3(1f, 0f, 0f));
            WriteVertex(vertices, 2, new Vector3(0f, 1f, 0f));
            var indices = new byte[3 * sizeof(uint)];
            Buffer.BlockCopy(new[] { 0, 1, 2 }, 0, indices, 0, indices.Length);
            return new ChunkLiveMeshSnapshot
            {
                VertexCount = 3,
                IndexCount = 3,
                LocalBounds = new BoundsData(new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0.01f)),
                VertexBytes = vertices,
                IndexBytes = indices
            };
        }

        private static ChunkLiveMeshSnapshot CreateSharedQuad()
        {
            var vertices = new byte[4 * ChunkLiveMeshSnapshot.VertexStride];
            WriteVertex(vertices, 0, new Vector3(0f, 0f, 0f));
            WriteVertex(vertices, 1, new Vector3(1f, 0f, 0f));
            WriteVertex(vertices, 2, new Vector3(0f, 1f, 0f));
            WriteVertex(vertices, 3, new Vector3(1f, 1f, 0f));
            var indices = new byte[6 * sizeof(uint)];
            Buffer.BlockCopy(new[] { 0, 1, 2, 2, 1, 3 }, 0, indices, 0,
                indices.Length);
            return new ChunkLiveMeshSnapshot
            {
                VertexCount = 4,
                IndexCount = 6,
                LocalBounds = new BoundsData(new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0.01f)),
                VertexBytes = vertices,
                IndexBytes = indices
            };
        }

        private static void WriteVertex(byte[] target, int index, Vector3 position)
        {
            int offset = index * ChunkLiveMeshSnapshot.VertexStride;
            Buffer.BlockCopy(BitConverter.GetBytes(position.x), 0, target, offset, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(position.y), 0, target, offset + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(position.z), 0, target, offset + 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, target, offset + 20, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0xFFFFFFFFu), 0, target, offset + 24, 4);
        }
    }
}
