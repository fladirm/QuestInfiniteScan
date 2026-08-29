using Meta.XR;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Genesis.RoomScan.Tests
{
    public sealed class PassthroughCameraProviderTests
    {
        private GameObject _borrowedHost;
        private GameObject _providerHost;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            if (_providerHost != null)
                Object.DestroyImmediate(_providerHost);
            if (_borrowedHost != null)
                Object.DestroyImmediate(_borrowedHost);
        }

        [Test]
        public void BorrowedEnabledPca_StartCaptureDoesNotToggleEnabled()
        {
            PassthroughCameraAccess borrowed = CreateEnabledBorrowedPca();
            PassthroughCameraProvider provider = CreateProvider();

            provider.StartCapture();

            Assert.That(borrowed.enabled, Is.True);
            Assert.That(provider.CameraAccess(StereoEye.Right),
                Is.SameAs(borrowed));
            Assert.That(provider.OwnsCameraAccess(StereoEye.Right), Is.False);
            Assert.That(provider.CameraAccess(StereoEye.Left), Is.Not.Null);
            Assert.That(provider.OwnsCameraAccess(StereoEye.Left), Is.True);
        }

        [Test]
        public void BorrowedEnabledPca_StartCapturePreservesConfiguration()
        {
            PassthroughCameraAccess borrowed = CreateEnabledBorrowedPca();
            PassthroughCameraProvider provider = CreateProvider();

            provider.StartCapture();

            Assert.That(borrowed.CameraPosition,
                Is.EqualTo(PassthroughCameraAccess.CameraPositionType.Right));
            Assert.That(borrowed.RequestedResolution, Is.EqualTo(new Vector2Int(640, 480)));
            Assert.That(borrowed.MaxFramerate, Is.EqualTo(60));
        }

        [Test]
        public void BorrowedPca_StopCaptureDoesNotDisable()
        {
            PassthroughCameraAccess borrowed = CreateEnabledBorrowedPca();
            PassthroughCameraProvider provider = CreateProvider();
            provider.StartCapture();

            provider.StopCapture();

            Assert.That(borrowed.enabled, Is.True);
        }

        [Test]
        public void OwnedStereoPca_IsConfiguredBeforeEnable()
        {
            PassthroughCameraProvider provider = CreateProvider();

            provider.StartCapture();

            PassthroughCameraAccess left = provider.CameraAccess(StereoEye.Left);
            PassthroughCameraAccess right = provider.CameraAccess(StereoEye.Right);
            Assert.That(left, Is.Not.Null);
            Assert.That(right, Is.Not.Null);
            Assert.That(provider.OwnsCameraAccess(StereoEye.Left), Is.True);
            Assert.That(provider.OwnsCameraAccess(StereoEye.Right), Is.True);
            Assert.That(left.CameraPosition,
                Is.EqualTo(PassthroughCameraAccess.CameraPositionType.Left));
            Assert.That(right.CameraPosition,
                Is.EqualTo(PassthroughCameraAccess.CameraPositionType.Right));
            Assert.That(left.RequestedResolution,
                Is.EqualTo(new Vector2Int(1280, 960)));
            Assert.That(right.RequestedResolution,
                Is.EqualTo(new Vector2Int(1280, 960)));
            Assert.That(left.MaxFramerate, Is.EqualTo(30),
                "Meta rejects MaxFramerate writes while enabled, so this value proves configuration preceded enable.");
            Assert.That(right.MaxFramerate, Is.EqualTo(30));
            Assert.That(left.enabled, Is.True);
            Assert.That(right.enabled, Is.True);
        }

        [Test]
        public void OwnedPca_StopCaptureDisables()
        {
            PassthroughCameraProvider provider = CreateProvider();
            provider.StartCapture();
            PassthroughCameraAccess left = provider.CameraAccess(StereoEye.Left);
            PassthroughCameraAccess right = provider.CameraAccess(StereoEye.Right);

            provider.StopCapture();

            Assert.That(left.enabled, Is.False);
            Assert.That(right.enabled, Is.False);
        }

        [Test]
        public void RepeatedStartCapture_IsIdempotent()
        {
            PassthroughCameraProvider provider = CreateProvider();
            provider.StartCapture();
            PassthroughCameraAccess firstLeft =
                provider.CameraAccess(StereoEye.Left);
            PassthroughCameraAccess firstRight =
                provider.CameraAccess(StereoEye.Right);

            provider.StartCapture();

            Assert.That(provider.CameraAccess(StereoEye.Left),
                Is.SameAs(firstLeft));
            Assert.That(provider.CameraAccess(StereoEye.Right),
                Is.SameAs(firstRight));
            Assert.That(Object.FindObjectsByType<PassthroughCameraAccess>(
                FindObjectsInactive.Include), Has.Length.EqualTo(2));
            Assert.That(firstLeft.enabled, Is.True);
            Assert.That(firstRight.enabled, Is.True);
            Assert.That(firstLeft.MaxFramerate, Is.EqualTo(30));
            Assert.That(firstRight.MaxFramerate, Is.EqualTo(30));
        }

        [Test]
        public void Pairing_RequiresBothEyesInsideOneDepthWindow()
        {
            var leftTexture = new Texture2D(2, 2);
            var rightTexture = new Texture2D(2, 2);
            try
            {
                CameraFrameDescriptor left = Descriptor(leftTexture,
                    StereoEye.Left, 100.000);
                CameraFrameDescriptor right = Descriptor(rightTexture,
                    StereoEye.Right, 100.010);
                StereoFrameMatch match = PassthroughCameraProvider.MatchFrames(
                    left, right, 100.005, 0.020, out StereoCameraFrame frame);

                Assert.That(match, Is.EqualTo(StereoFrameMatch.Ready));
                Assert.That(frame.IsValid, Is.True);
                Assert.That(frame.MaximumSkewSeconds,
                    Is.EqualTo(0.010).Within(1e-9));
                Assert.That(frame.Left.Eye, Is.EqualTo(StereoEye.Left));
                Assert.That(frame.Right.Eye, Is.EqualTo(StereoEye.Right));
            }
            finally
            {
                Object.DestroyImmediate(leftTexture);
                Object.DestroyImmediate(rightTexture);
            }
        }

        [Test]
        public void Pairing_DropsDepthAfterEitherPcaPassesItsWindow()
        {
            var leftTexture = new Texture2D(2, 2);
            var rightTexture = new Texture2D(2, 2);
            try
            {
                CameraFrameDescriptor left = Descriptor(leftTexture,
                    StereoEye.Left, 100.050);
                CameraFrameDescriptor right = Descriptor(rightTexture,
                    StereoEye.Right, 100.010);
                StereoFrameMatch match = PassthroughCameraProvider.MatchFrames(
                    left, right, 100.000, 0.020, out StereoCameraFrame frame);

                Assert.That(match,
                    Is.EqualTo(StereoFrameMatch.DepthExpired));
                Assert.That(frame.IsValid, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(leftTexture);
                Object.DestroyImmediate(rightTexture);
            }
        }

        [Test]
        public void DuplicatePhysicalEyeProducer_FailsClosed()
        {
            CreateEnabledBorrowedPca();
            var duplicateHost = new GameObject("Duplicate right PCA");
            duplicateHost.SetActive(false);
            var duplicate = duplicateHost.AddComponent<PassthroughCameraAccess>();
            duplicate.enabled = false;
            duplicate.CameraPosition =
                PassthroughCameraAccess.CameraPositionType.Right;
            PassthroughCameraProvider provider = CreateProvider();
            try
            {
                Assert.Throws<System.InvalidOperationException>(
                    provider.StartCapture);
            }
            finally
            {
                Object.DestroyImmediate(duplicateHost);
            }
        }

        private static CameraFrameDescriptor Descriptor(Texture texture,
            StereoEye eye, double unixSeconds) => new(texture,
            new Pose(Vector3.zero, Quaternion.identity), Vector2.one,
            Vector2.zero, new Vector2(2f, 2f), new Vector2(2f, 2f),
            unixSeconds, 1u, eye);

        private PassthroughCameraProvider CreateProvider()
        {
            _providerHost = new GameObject("Provider");
            _providerHost.SetActive(false);
            return _providerHost.AddComponent<PassthroughCameraProvider>();
        }

        private PassthroughCameraAccess CreateEnabledBorrowedPca()
        {
            _borrowedHost = new GameObject("Building Block PCA");
            _borrowedHost.SetActive(false);
            var borrowed = _borrowedHost.AddComponent<PassthroughCameraAccess>();
            borrowed.enabled = false;
            borrowed.CameraPosition = PassthroughCameraAccess.CameraPositionType.Right;
            borrowed.RequestedResolution = new Vector2Int(640, 480);
            borrowed.MaxFramerate = 60;
            borrowed.enabled = true;
            return borrowed;
        }
    }
}
