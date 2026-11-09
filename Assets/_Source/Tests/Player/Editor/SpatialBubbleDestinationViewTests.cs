using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class SpatialBubbleDestinationViewTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<GameObject> _objects = new List<GameObject>();

            public SpatialBubbleDestinationView View { get; }
            public MeshRenderer Surface { get; }
            public Camera Viewer { get; }
            public Transform Destination { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                GameObject bubble = Create("Bubble");
                Surface = bubble.AddComponent<MeshRenderer>();
                SpatialBubbleTeleport teleport = bubble.AddComponent<SpatialBubbleTeleport>();
                View = bubble.AddComponent<SpatialBubbleDestinationView>();
                Destination = Create("Destination").transform;
                teleport.Destination = Destination;
                Viewer = Create("Viewer").AddComponent<Camera>();
                Viewer.enabled = false;
                SetField(View, "_teleport", teleport);
                SetField(View, "_surface", Surface);
                SetField(View, "_viewerCamera", Viewer);
                SetField(View, "_resolvedViewer", Viewer);
            }

            public GameObject Create(string name)
            {
                GameObject root = new GameObject(name);
                root.SetActive(false);
                root.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(root, _scene);
                _objects.Add(root);
                return root;
            }

            public Camera CreateCapture()
            {
                Invoke(View, "EnsureCamera");
                return GetField<Camera>(View, "_captureCamera");
            }

            public void Dispose()
            {
                try
                {
                    Invoke(View, "ReleaseResources");
                }
                finally
                {
                    foreach (GameObject root in _objects)
                    {
                        if (root != null)
                        {
                            UnityEngine.Object.DestroyImmediate(root);
                        }
                    }
                    EditorSceneManager.ClosePreviewScene(_scene);
                }
            }
        }

        [Test]
        public void EditModeUpdateDoesNotCreateCameraOrRenderTexture()
        {
            using (var rig = new Rig())
            {
                Assert.That(Application.isPlaying, Is.False);
                Invoke(rig.View, "LateUpdate");
                Assert.That(GetField<Camera>(rig.View, "_captureCamera"), Is.Null);
                Assert.That(GetField<RenderTexture>(rig.View, "_target"), Is.Null);
                Assert.That(GetField<bool>(rig.View, "_subscribed"), Is.False);
            }
        }

        [Test]
        public void CaptureUsesExitPositionEyeHeightAndYawWithoutExitPitchOrRoll()
        {
            using (var rig = new Rig())
            {
                SetField(rig.View, "_fullParallax", false);
                Camera capture = rig.CreateCapture();
                rig.Destination.SetPositionAndRotation(new Vector3(12f, 4f, -8f), Quaternion.Euler(35f, 123f, 18f));
                rig.View.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
                rig.Viewer.transform.rotation = Quaternion.Euler(0f, 80f, 0f);
                SetField(rig.View, "_fallbackEyeHeight", 1.7f);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Assert.That(Vector3.Distance(capture.transform.position, rig.Destination.position + Vector3.up * 1.7f), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(capture.transform.rotation, Quaternion.Euler(0f, 173f, 0f)), Is.LessThan(0.001f));
            }
        }

        [Test]
        public void CaptureYawWrapsAcrossZeroAndIgnoresEntryAndViewerPitchRollAndPosition()
        {
            using (var rig = new Rig())
            {
                SetField(rig.View, "_fullParallax", false);
                Camera capture = rig.CreateCapture();
                rig.Destination.SetPositionAndRotation(new Vector3(12f, 4f, -8f), Quaternion.Euler(15f, 355f, 30f));
                rig.View.transform.SetPositionAndRotation(new Vector3(-40f, 7f, 20f), Quaternion.Euler(25f, 350f, 31f));
                rig.Viewer.transform.SetPositionAndRotation(new Vector3(20f, 10f, -30f), Quaternion.Euler(-40f, 10f, -22f));
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Assert.That(Quaternion.Angle(capture.transform.rotation, Quaternion.Euler(0f, 15f, 0f)), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(capture.transform.position, rig.Destination.position + Vector3.up * 1.7f), Is.LessThan(0.0001f));
                rig.View.transform.rotation = Quaternion.Euler(-20f, 350f, -17f);
                rig.Viewer.transform.rotation = Quaternion.Euler(45f, 10f, 25f);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Assert.That(Quaternion.Angle(capture.transform.rotation, Quaternion.Euler(0f, 15f, 0f)), Is.LessThan(0.001f));
            }
        }

        [Test]
        public void CaptureFallsBackToExitYawWhenViewerIsMissing()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                rig.Destination.rotation = Quaternion.Euler(22f, 273f, -15f);
                rig.View.transform.rotation = Quaternion.Euler(0f, 87f, 0f);
                SetField(rig.View, "_resolvedViewer", null);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Assert.That(Quaternion.Angle(capture.transform.rotation, Quaternion.Euler(0f, 273f, 0f)), Is.LessThan(0.001f));
            }
        }

        [Test]
        public void CaptureUsesCurrentPlayerStanceInsteadOfFallbackHeight()
        {
            using (var rig = new Rig())
            {
                SetField(rig.View, "_fullParallax", false);
                GameObject playerRoot = rig.Create("Player");
                StalkerMovementMotor motor = playerRoot.AddComponent<StalkerMovementMotor>();
                StalkerPlayerController player = playerRoot.AddComponent<StalkerPlayerController>();
                StalkerStanceController stance = playerRoot.GetComponent<StalkerStanceController>();
                SetField(motor, "_stance", stance);
                SetField(player, "_motor", motor);
                SetField(stance, "_currentCameraHeight", 0.85f);
                SetField(rig.View, "_viewerPlayer", player);
                Camera capture = rig.CreateCapture();
                rig.Destination.position = new Vector3(2f, 3f, 4f);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Assert.That(capture.transform.position.y, Is.EqualTo(3.85f).Within(0.0001f));
            }
        }

        [Test]
        public void FullParallaxMapsViewerOffsetAndOrientationWithoutAnchorScaleOrExtraEyeHeight()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                rig.View.transform.SetPositionAndRotation(new Vector3(100f, 10f, -50f), Quaternion.Euler(20f, 30f, 10f));
                rig.View.transform.localScale = new Vector3(3f, 4f, 5f);
                rig.Destination.SetPositionAndRotation(new Vector3(-20f, 40f, 60f), Quaternion.Euler(-10f, 120f, 25f));
                rig.Destination.localScale = Vector3.one * 0.5f;
                rig.Viewer.transform.SetPositionAndRotation(new Vector3(102f, 11.25f, -54f), Quaternion.Euler(15f, 75f, 5f));
                SetField(rig.View, "_fallbackEyeHeight", 8f);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Assert.That(GetField<bool>(rig.View, "_captureFullParallax"), Is.True);
                Assert.That(Vector3.Distance(capture.transform.position, new Vector3(-24f, 41.25f, 58f)), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(capture.transform.rotation, Quaternion.Euler(15f, 165f, 5f)), Is.LessThan(0.05f));
            }
        }

        [Test]
        public void FullParallaxTransfersSidewaysVerticalAndDepthMovementOneToOne()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                rig.View.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                rig.Destination.position = new Vector3(100f, 20f, 30f);
                rig.Viewer.transform.position = new Vector3(-3f, 0.8f, 5f);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Vector3 firstPosition = capture.transform.position;
                Quaternion firstRotation = capture.transform.rotation;
                Assert.That(Vector3.Distance(firstPosition, new Vector3(95f, 20.8f, 27f)), Is.LessThan(0.0001f));
                rig.Viewer.transform.position = new Vector3(2f, 1.6f, -4f);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Assert.That(Vector3.Distance(capture.transform.position, new Vector3(104f, 21.6f, 32f)), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(capture.transform.position - firstPosition, new Vector3(9f, 0.8f, 5f)), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(capture.transform.rotation, firstRotation), Is.LessThan(0.001f));
            }
        }

        [Test]
        public void CaptureProjectionTracksCustomViewerProjectionAndResetsCopiedViewOverride()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                capture.worldToCameraMatrix = Matrix4x4.identity;
                rig.Viewer.transform.SetPositionAndRotation(new Vector3(2f, 1.2f, -4f), Quaternion.Euler(17f, 38f, 7f));
                rig.Destination.SetPositionAndRotation(new Vector3(20f, 3f, 8f), Quaternion.Euler(0f, 90f, 0f));
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Matrix4x4 expectedView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(capture.transform.position, capture.transform.rotation, Vector3.one).inverse;
                AssertMatrix(capture.worldToCameraMatrix, expectedView);
                Matrix4x4 firstProjection = Matrix4x4.Perspective(67f, 1.3f, 0.2f, 450f);
                firstProjection.m02 = 0.18f;
                firstProjection.m12 = -0.09f;
                rig.Viewer.projectionMatrix = firstProjection;
                Invoke(rig.View, "UpdateCaptureProjection");
                AssertMatrix(capture.projectionMatrix, firstProjection);
                Matrix4x4 secondProjection = Matrix4x4.Perspective(45f, 1.7f, 0.15f, 600f);
                secondProjection.m02 = -0.22f;
                secondProjection.m12 = 0.13f;
                rig.Viewer.projectionMatrix = secondProjection;
                Invoke(rig.View, "UpdateCaptureProjection");
                AssertMatrix(capture.projectionMatrix, secondProjection);
                AssertMatrix(rig.Viewer.projectionMatrix, secondProjection);
            }
        }

        [Test]
        public void FullParallaxProjectsCorrespondingSourcePointsThroughTheMappedCapture()
        {
            using (var rig = new Rig())
            {
                rig.CreateCapture();
                rig.View.transform.SetPositionAndRotation(new Vector3(10f, 2f, -5f), Quaternion.Euler(0f, 35f, 0f));
                rig.Destination.SetPositionAndRotation(new Vector3(-20f, 8f, 15f), Quaternion.Euler(0f, 125f, 0f));
                rig.Viewer.transform.SetPositionAndRotation(new Vector3(12f, 3.4f, -9f), Quaternion.Euler(17f, 48f, -6f));
                Matrix4x4 projection = Matrix4x4.Perspective(67f, 1.3f, 0.2f, 450f);
                projection.m02 = 0.18f;
                projection.m12 = -0.09f;
                rig.Viewer.projectionMatrix = projection;
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Invoke(rig.View, "UpdateCaptureProjection");
                Invoke(rig.View, "UpdateDestinationProjection");
                Matrix4x4 sourceToCaptureClip = GetField<Matrix4x4>(rig.View, "_destinationViewWorldToClip");
                Matrix4x4 sourceToViewerClip = GL.GetGPUProjectionMatrix(projection, true) * rig.Viewer.worldToCameraMatrix;
                foreach (Vector3 point in new[] { new Vector3(0.75f, -0.3f, 3f), new Vector3(-1.1f, 0.6f, 18f), new Vector3(2f, -1f, 70f) })
                {
                    Vector3 sourcePoint = rig.Viewer.transform.TransformPoint(point);
                    Assert.That(Vector3.Distance(ProjectNdc(sourceToCaptureClip, sourcePoint), ProjectNdc(sourceToViewerClip, sourcePoint)), Is.LessThan(0.0001f));
                }
            }
        }

        [Test]
        public void DestinationHeightOffsetMovesTheFullParallaxViewWithoutBreakingProjectionOrLegacyMode()
        {
            Shader shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Source/Anomalies/SpatialBubble/Presentation/Src/Shaders/SpatialBubbleMesh.shader");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                using (var rig = new Rig())
                {
                    Camera capture = rig.CreateCapture();
                    rig.Surface.sharedMaterial = material;
                    rig.View.transform.SetPositionAndRotation(new Vector3(10f, 2f, -5f), Quaternion.Euler(0f, 35f, 0f));
                    rig.Destination.SetPositionAndRotation(new Vector3(-20f, 8f, 15f), Quaternion.Euler(0f, 125f, 0f));
                    rig.Viewer.transform.SetPositionAndRotation(new Vector3(12f, 3.4f, -9f), Quaternion.Euler(17f, 48f, -6f));
                    Vector3 sourcePoint = rig.Viewer.transform.TransformPoint(new Vector3(0.75f, -0.3f, 12f));
                    Matrix4x4 sourceToViewerClip = GL.GetGPUProjectionMatrix(rig.Viewer.projectionMatrix, true) * rig.Viewer.worldToCameraMatrix;
                    foreach (float height in new[] { 2f, -1.25f })
                    {
                        material.SetFloat("_DestinationViewHeightOffset", height);
                        Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                        Invoke(rig.View, "UpdateCaptureProjection");
                        Invoke(rig.View, "UpdateDestinationProjection");
                        Assert.That(Vector3.Distance(capture.transform.position, new Vector3(-24f, 9.4f + height, 13f)), Is.LessThan(0.0001f));
                        Assert.That(Quaternion.Angle(capture.transform.rotation, Quaternion.Euler(17f, 138f, -6f)), Is.LessThan(0.05f));
                        Matrix4x4 sourceToCaptureClip = GetField<Matrix4x4>(rig.View, "_destinationViewWorldToClip");
                        Assert.That(Vector3.Distance(ProjectNdc(sourceToCaptureClip, sourcePoint), ProjectNdc(sourceToViewerClip, sourcePoint)), Is.LessThan(0.0001f));
                    }
                    SetField(rig.View, "_fullParallax", false);
                    Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                    Assert.That(Vector3.Distance(capture.transform.position, new Vector3(-20f, 9.7f, 15f)), Is.LessThan(0.0001f));
                    Assert.That(Vector3.Distance(rig.Destination.position, new Vector3(-20f, 8f, 15f)), Is.LessThan(0.0001f));
                    Assert.That(Vector3.Distance(rig.Viewer.transform.position, new Vector3(12f, 3.4f, -9f)), Is.LessThan(0.0001f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void DisablingFullParallaxRestoresLegacyPoseAndClearsProjectiveBinding()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                rig.View.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                rig.Destination.SetPositionAndRotation(new Vector3(10f, 2f, 30f), Quaternion.Euler(0f, 180f, 0f));
                rig.Viewer.transform.SetPositionAndRotation(new Vector3(2f, 1.3f, -5f), Quaternion.Euler(20f, 120f, -7f));
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Invoke(rig.View, "UpdateCaptureProjection");
                Invoke(rig.View, "UpdateDestinationProjection");
                Assert.That(GetField<bool>(rig.View, "_captureFullParallax"), Is.True);
                SetField(rig.View, "_fullParallax", false);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Invoke(rig.View, "UpdateDestinationProjection");
                Assert.That(GetField<bool>(rig.View, "_captureFullParallax"), Is.False);
                Assert.That(Vector3.Distance(capture.transform.position, new Vector3(10f, 3.7f, 30f)), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(capture.transform.rotation, Quaternion.Euler(0f, 210f, 0f)), Is.LessThan(0.001f));
                AssertMatrix(GetField<Matrix4x4>(rig.View, "_destinationViewWorldToClip"), Matrix4x4.identity);
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetFloat("_DestinationViewParallax", 1f);
                block.SetMatrix("_DestinationViewWorldToClip", Matrix4x4.Translate(Vector3.one));
                block.SetFloat("_CentralDistortionStrength", 4.25f);
                rig.Surface.SetPropertyBlock(block);
                Invoke(rig.View, "BindSurface", false);
                rig.Surface.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_DestinationViewParallax"), Is.Zero);
                AssertMatrix(block.GetMatrix("_DestinationViewWorldToClip"), Matrix4x4.identity);
                Assert.That(block.GetFloat("_CentralDistortionStrength"), Is.EqualTo(4.25f));
            }
        }

        [Test]
        public void CameraConfigurationPreservesViewerAndCreatesNoAudioListener()
        {
            using (var rig = new Rig())
            {
                rig.Viewer.fieldOfView = 77f;
                rig.Viewer.cullingMask = 12345;
                rig.Viewer.allowDynamicResolution = true;
                Camera capture = rig.CreateCapture();
                Invoke(rig.View, "ConfigureCamera");
                Assert.That(capture.fieldOfView, Is.EqualTo(77f));
                Assert.That(capture.cullingMask, Is.EqualTo(12345));
                Assert.That(capture.enabled, Is.False);
                Assert.That(capture.allowHDR, Is.False);
                Assert.That(capture.allowMSAA, Is.False);
                Assert.That(capture.allowDynamicResolution, Is.False);
                Assert.That(capture.GetComponent<AudioListener>(), Is.Null);
                Assert.That(rig.Viewer.allowDynamicResolution, Is.True);
                Assert.That(rig.Viewer.targetTexture, Is.Null);
                Assert.That(rig.Viewer.fieldOfView, Is.EqualTo(77f));
            }
        }

        [Test]
        public void CaptureAntialiasingDefaultsToSmaaAndSwitchesWithoutChangingViewer()
        {
            using (var rig = new Rig())
            {
                rig.CreateCapture();
                Component data = GetField<Component>(rig.View, "_captureData");
                FieldInfo antialiasing = data.GetType().GetField("antialiasing", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo quality = data.GetType().GetField("SMAAQuality", BindingFlags.Public | BindingFlags.Instance);
                Assert.That(antialiasing, Is.Not.Null);
                Assert.That(quality, Is.Not.Null);
                Component viewerData = rig.Viewer.gameObject.AddComponent(data.GetType());
                antialiasing.SetValue(viewerData, Enum.Parse(antialiasing.FieldType, "TemporalAntialiasing"));
                Invoke(rig.View, "ConfigureCamera");
                Assert.That(antialiasing.GetValue(data).ToString(), Is.EqualTo("SubpixelMorphologicalAntiAliasing"));
                Assert.That(quality.GetValue(data).ToString(), Is.EqualTo("High"));
                foreach (SpatialBubbleDestinationView.CaptureAntialiasing mode in new[] { SpatialBubbleDestinationView.CaptureAntialiasing.None, SpatialBubbleDestinationView.CaptureAntialiasing.Fxaa, SpatialBubbleDestinationView.CaptureAntialiasing.Smaa })
                {
                    SetField(rig.View, "_hasRendered", true);
                    SetField(rig.View, "_antialiasing", mode);
                    Invoke(rig.View, "ConfigureCamera");
                    string expected = mode == SpatialBubbleDestinationView.CaptureAntialiasing.None ? "None" : mode == SpatialBubbleDestinationView.CaptureAntialiasing.Fxaa ? "FastApproximateAntialiasing" : "SubpixelMorphologicalAntiAliasing";
                    Assert.That(antialiasing.GetValue(data).ToString(), Is.EqualTo(expected));
                    object frameSettings = GetField<object>(data, "m_RenderingPathCustomFrameSettings");
                    MethodInfo isEnabled = frameSettings.GetType().GetMethod("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
                    Assert.That(isEnabled, Is.Not.Null);
                    object field = Enum.Parse(isEnabled.GetParameters()[0].ParameterType, "Antialiasing");
                    Assert.That((bool)isEnabled.Invoke(frameSettings, new[] { field }), Is.EqualTo(mode != SpatialBubbleDestinationView.CaptureAntialiasing.None));
                    Assert.That(GetField<bool>(rig.View, "_hasRendered"), Is.False);
                }
                Assert.That(antialiasing.GetValue(viewerData).ToString(), Is.EqualTo("TemporalAntialiasing"));
            }
        }

        [Test]
        public void AdaptiveCaptureSizeQuantizesCoverageAndKeepsResizeHysteresis()
        {
            Vector2 screen = new Vector2(1920f, 1080f);
            Assert.That(AdaptiveSize(screen, new Rect(0.25f, 0.25f, 0.2f, 0.2f), 2048, 256, Vector2Int.zero), Is.EqualTo(new Vector2Int(384, 256)));
            Assert.That(AdaptiveSize(new Vector2(3840f, 2160f), new Rect(0f, 0f, 1f, 1f), 1024, 256, Vector2Int.zero), Is.EqualTo(new Vector2Int(1024, 1024)));
            Assert.That(AdaptiveSize(screen, new Rect(0f, 0f, 0.134f, 0.2f), 2048, 256, new Vector2Int(256, 256)), Is.EqualTo(new Vector2Int(256, 256)));
            Assert.That(AdaptiveSize(screen, new Rect(0f, 0f, 0.25f, 0.4f), 2048, 256, new Vector2Int(256, 256)), Is.EqualTo(new Vector2Int(512, 512)));
            Assert.That(AdaptiveSize(screen, new Rect(0f, 0f, 0.22f, 0.4f), 2048, 256, new Vector2Int(512, 512)), Is.EqualTo(new Vector2Int(512, 512)));
            Assert.That(AdaptiveSize(screen, new Rect(0f, 0f, 0.12f, 0.2f), 2048, 256, new Vector2Int(512, 512)), Is.EqualTo(new Vector2Int(256, 256)));
            Assert.That(AdaptiveSize(screen, new Rect(0f, 0f, 0.01f, 0.01f), 512, 4096, Vector2Int.zero), Is.EqualTo(new Vector2Int(512, 512)));
        }

        [Test]
        public void AdaptiveCaptureViewportKeepsPaddingAndUsesFullFrameNearOrInsideTheBubble()
        {
            using (var rig = new Rig())
            {
                rig.Viewer.orthographic = true;
                rig.Viewer.orthographicSize = 5f;
                rig.Viewer.pixelRect = new Rect(0f, 0f, 1000f, 500f);
                rig.Viewer.aspect = 2f;
                rig.Viewer.nearClipPlane = 0.1f;
                Rect viewport = CaptureViewport(rig.Viewer, new Bounds(new Vector3(0f, 0f, 20f), new Vector3(4f, 4f, 2f)));
                Assert.That(viewport.xMin, Is.EqualTo(0.336f).Within(0.001f));
                Assert.That(viewport.xMax, Is.EqualTo(0.664f).Within(0.001f));
                Assert.That(viewport.yMin, Is.EqualTo(0.172f).Within(0.001f));
                Assert.That(viewport.yMax, Is.EqualTo(0.828f).Within(0.001f));
                Assert.That(CaptureViewport(rig.Viewer, new Bounds(Vector3.zero, Vector3.one * 4f)), Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
                Assert.That(CaptureViewport(rig.Viewer, new Bounds(new Vector3(0f, 0f, 0.2f), Vector3.one)), Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
            }
        }

        [Test]
        public void CroppedCapturePreservesProjectiveCorrespondenceAndTheViewerProjection()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                rig.View.transform.SetPositionAndRotation(new Vector3(10f, 2f, -5f), Quaternion.Euler(0f, 35f, 0f));
                rig.Destination.SetPositionAndRotation(new Vector3(-20f, 8f, 15f), Quaternion.Euler(0f, 125f, 0f));
                rig.Viewer.transform.SetPositionAndRotation(new Vector3(12f, 3.4f, -9f), Quaternion.Euler(17f, 48f, -6f));
                Matrix4x4 projection = Matrix4x4.Perspective(67f, 1.3f, 0.2f, 450f);
                projection.m02 = -0.18f;
                projection.m12 = 0.07f;
                rig.Viewer.projectionMatrix = projection;
                Rect viewport = new Rect(0.2f, 0.1f, 0.4f, 0.6f);
                SetField(rig.View, "_captureViewport", viewport);
                Invoke(rig.View, "UpdateCapturePose", rig.Destination);
                Invoke(rig.View, "UpdateCaptureProjection");
                Matrix4x4 mapping = GetField<Matrix4x4>(rig.View, "_sourceToDestination");
                foreach (Vector3 local in new[] { new Vector3(0.1f, -0.1f, 3f), new Vector3(-1f, 0.6f, 18f), new Vector3(2f, -1f, 70f) })
                {
                    Vector3 source = rig.Viewer.transform.TransformPoint(local);
                    Vector3 original = rig.Viewer.WorldToViewportPoint(source);
                    Vector3 cropped = capture.WorldToViewportPoint(mapping.MultiplyPoint3x4(source));
                    Assert.That(cropped.x, Is.EqualTo((original.x - viewport.x) / viewport.width).Within(0.0001f));
                    Assert.That(cropped.y, Is.EqualTo((original.y - viewport.y) / viewport.height).Within(0.0001f));
                    Assert.That(cropped.z, Is.EqualTo(original.z).Within(0.0001f));
                }
                AssertMatrix(rig.Viewer.projectionMatrix, projection);
            }
        }

        [Test]
        public void CaptureResolutionPreservesAspectAndClampsLongestSide()
        {
            Assert.That(CalculateSize(1024, 16f / 9f), Is.EqualTo(new Vector2Int(1024, 576)));
            Assert.That(CalculateSize(1024, 9f / 16f), Is.EqualTo(new Vector2Int(576, 1024)));
            Assert.That(CalculateSize(4096, 1f), Is.EqualTo(new Vector2Int(2048, 2048)));
            Assert.That(CalculateSize(1, 1f), Is.EqualTo(new Vector2Int(128, 128)));
            Assert.That(CalculateSize(1024, float.NaN), Is.EqualTo(new Vector2Int(1024, 1024)));
        }

        [Test]
        public void CaptureHidesOnlyItsOwnSurfaceAndRestoresOriginalRenderingFlag()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                Invoke(rig.View, "BeginCameraRendering", default(ScriptableRenderContext), rig.Viewer);
                Assert.That(rig.Surface.forceRenderingOff, Is.False);
                Invoke(rig.View, "EndCameraRendering", default(ScriptableRenderContext), capture);
                Assert.That(rig.Surface.forceRenderingOff, Is.False);
                Invoke(rig.View, "BeginCameraRendering", default(ScriptableRenderContext), capture);
                Assert.That(rig.Surface.forceRenderingOff, Is.True);
                Invoke(rig.View, "EndCameraRendering", default(ScriptableRenderContext), rig.Viewer);
                Assert.That(rig.Surface.forceRenderingOff, Is.True);
                Invoke(rig.View, "BeginCameraRendering", default(ScriptableRenderContext), rig.Viewer);
                Assert.That(rig.Surface.forceRenderingOff, Is.False);
                Invoke(rig.View, "BeginCameraRendering", default(ScriptableRenderContext), capture);
                Assert.That(rig.Surface.forceRenderingOff, Is.True);
                Invoke(rig.View, "EndCameraRendering", default(ScriptableRenderContext), capture);
                Assert.That(rig.Surface.forceRenderingOff, Is.False);
                rig.Surface.forceRenderingOff = true;
                Invoke(rig.View, "BeginCameraRendering", default(ScriptableRenderContext), capture);
                Invoke(rig.View, "EndCameraRendering", default(ScriptableRenderContext), capture);
                Assert.That(rig.Surface.forceRenderingOff, Is.True);
            }
        }

        [Test]
        public void MissingRenderTargetCannotPublishAnAvailableDestinationImage()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                Invoke(rig.View, "BindSurface", true);
                Invoke(rig.View, "EndCameraRendering", default(ScriptableRenderContext), capture);
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                rig.Surface.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_DestinationViewAvailable"), Is.Zero);
                Assert.That(GetField<bool>(rig.View, "_hasRendered"), Is.False);
            }
        }

        [Test]
        public void BindingPublishesActualTextureAspectForUndistortedCenterCrop()
        {
            using (var rig = new Rig())
            {
                RenderTexture target = new RenderTexture(1024, 576, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                target.hideFlags = HideFlags.HideAndDontSave;
                SetField(rig.View, "_target", target);
                Invoke(rig.View, "BindSurface", false);
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                rig.Surface.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_DestinationViewAspect"), Is.EqualTo(16f / 9f).Within(0.0001f));
                Assert.That(block.GetFloat("_DestinationViewAvailable"), Is.Zero);
                Assert.That(block.GetTexture("_DestinationViewTex"), Is.EqualTo(Texture2D.blackTexture));
            }
        }

        [Test]
        public void DisableDuringCaptureRestoresSurfaceAndPreservesUnrelatedProperties()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetFloat("_CentralDistortionStrength", 3.25f);
                rig.Surface.SetPropertyBlock(block);
                Invoke(rig.View, "BindSurface", true);
                rig.Surface.GetPropertyBlock(block);
                block.SetFloat("_DestinationViewParallax", 1f);
                block.SetMatrix("_DestinationViewWorldToClip", Matrix4x4.Translate(Vector3.one));
                rig.Surface.SetPropertyBlock(block);
                Invoke(rig.View, "BeginCameraRendering", default(ScriptableRenderContext), capture);
                Invoke(rig.View, "OnDisable");
                rig.Surface.GetPropertyBlock(block);
                Assert.That(rig.Surface.forceRenderingOff, Is.False);
                Assert.That(block.GetFloat("_DestinationViewAvailable"), Is.Zero);
                Assert.That(block.GetFloat("_CentralDistortionStrength"), Is.EqualTo(3.25f));
                Assert.That(block.GetTexture("_DestinationViewTex"), Is.EqualTo(Texture2D.blackTexture));
                Assert.That(block.GetFloat("_DestinationViewParallax"), Is.Zero);
                AssertMatrix(block.GetMatrix("_DestinationViewWorldToClip"), Matrix4x4.identity);
                Assert.That(capture == null, Is.True);
                Assert.That(GetField<Camera>(rig.View, "_captureCamera"), Is.Null);
            }
        }

        [Test]
        public void SurfaceReplacementClearsPreviousBindingWithoutChangingItsOtherProperties()
        {
            using (var rig = new Rig())
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetFloat("_CentralDistortionStrength", 2.5f);
                rig.Surface.SetPropertyBlock(block);
                Invoke(rig.View, "BindSurface", true);
                MeshRenderer replacement = rig.Create("Replacement").AddComponent<MeshRenderer>();
                SetField(rig.View, "_surface", replacement);
                Invoke(rig.View, "BindSurface", false);
                rig.Surface.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_DestinationViewAvailable"), Is.Zero);
                Assert.That(block.GetFloat("_CentralDistortionStrength"), Is.EqualTo(2.5f));
                Assert.That(GetField<MeshRenderer>(rig.View, "_boundSurface"), Is.EqualTo(replacement));
            }
        }

        [Test]
        public void CleanupDestroysOwnedUncreatedTextureAndCameraAndIsIdempotent()
        {
            using (var rig = new Rig())
            {
                Camera capture = rig.CreateCapture();
                RenderTexture target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                target.hideFlags = HideFlags.HideAndDontSave;
                SetField(rig.View, "_target", target);
                Invoke(rig.View, "ReleaseResources");
                Assert.That(target == null, Is.True);
                Assert.That(capture == null, Is.True);
                Assert.That(GetField<RenderTexture>(rig.View, "_target"), Is.Null);
                Assert.DoesNotThrow(() => Invoke(rig.View, "ReleaseResources"));
            }
        }

        private static Vector2Int CalculateSize(int resolution, float aspect)
        {
            MethodInfo method = typeof(SpatialBubbleDestinationView).GetMethod("CalculateCaptureSize", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Vector2Int)method.Invoke(null, new object[] { resolution, aspect });
        }

        private static Vector2Int AdaptiveSize(Vector2 screenSize, Rect viewport, int resolution, int minimumResolution, Vector2Int previousSize)
        {
            MethodInfo method = typeof(SpatialBubbleDestinationView).GetMethod("CalculateAdaptiveCaptureSize", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Vector2Int)method.Invoke(null, new object[] { screenSize, viewport, resolution, minimumResolution, previousSize });
        }

        private static Rect CaptureViewport(Camera viewer, Bounds bounds)
        {
            MethodInfo method = typeof(SpatialBubbleDestinationView).GetMethod("CalculateCaptureViewport", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Rect)method.Invoke(null, new object[] { viewer, bounds });
        }

        private static Vector3 ProjectNdc(Matrix4x4 matrix, Vector3 position)
        {
            Vector4 clip = matrix * new Vector4(position.x, position.y, position.z, 1f);
            Assert.That(clip.w, Is.GreaterThan(0f));
            return new Vector3(clip.x, clip.y, clip.z) / clip.w;
        }

        private static void AssertMatrix(Matrix4x4 actual, Matrix4x4 expected)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(0.0001f));
                }
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(target);
        }

        private static void Invoke(SpatialBubbleDestinationView target, string name, params object[] arguments)
        {
            MethodInfo method = typeof(SpatialBubbleDestinationView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(target, arguments);
        }
    }
}
