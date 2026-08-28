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
            Assert.That(provider.CameraAccess, Is.SameAs(borrowed));
            Assert.That(provider.OwnsCameraAccess, Is.False);
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
        public void OwnedPca_IsConfiguredBeforeEnable()
        {
            PassthroughCameraProvider provider = CreateProvider();

            provider.StartCapture();

            PassthroughCameraAccess owned = provider.CameraAccess;
            Assert.That(owned, Is.Not.Null);
            Assert.That(provider.OwnsCameraAccess, Is.True);
            Assert.That(owned.CameraPosition,
                Is.EqualTo(PassthroughCameraAccess.CameraPositionType.Left));
            Assert.That(owned.RequestedResolution, Is.EqualTo(new Vector2Int(1280, 960)));
            Assert.That(owned.MaxFramerate, Is.EqualTo(30),
                "Meta rejects MaxFramerate writes while enabled, so this value proves configuration preceded enable.");
            Assert.That(owned.enabled, Is.True);
        }

        [Test]
        public void OwnedPca_StopCaptureDisables()
        {
            PassthroughCameraProvider provider = CreateProvider();
            provider.StartCapture();
            PassthroughCameraAccess owned = provider.CameraAccess;

            provider.StopCapture();

            Assert.That(owned.enabled, Is.False);
        }

        [Test]
        public void RepeatedStartCapture_IsIdempotent()
        {
            PassthroughCameraProvider provider = CreateProvider();
            provider.StartCapture();
            PassthroughCameraAccess first = provider.CameraAccess;

            provider.StartCapture();

            Assert.That(provider.CameraAccess, Is.SameAs(first));
            Assert.That(Object.FindObjectsByType<PassthroughCameraAccess>(
                FindObjectsInactive.Include, FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(first.enabled, Is.True);
            Assert.That(first.MaxFramerate, Is.EqualTo(30));
        }

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
