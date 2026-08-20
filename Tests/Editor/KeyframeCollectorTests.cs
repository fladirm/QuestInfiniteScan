using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class KeyframeCollectorTests
    {
        [Test]
        public void CameraWorldPoseConvertsExactlyIntoChunkLocalFrame()
        {
            var worldFromChunk = new RigidPoseData(new Vector3(8f, 1.5f, -3f),
                Quaternion.Euler(0f, 90f, 0f));
            var chunkFromCamera = new RigidPoseData(new Vector3(1f, 0.4f, 2f),
                Quaternion.Euler(-5f, 25f, 2f));
            RigidPoseData worldFromCamera = worldFromChunk * chunkFromCamera;

            Pose converted = KeyframeCollector.ConvertWorldPoseToFrame(
                new Pose(worldFromCamera.position, worldFromCamera.rotation), worldFromChunk);

            Assert.That(Vector3.Distance(converted.position, chunkFromCamera.position),
                Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(converted.rotation, chunkFromCamera.rotation),
                Is.LessThan(0.001f));
        }
    }
}
