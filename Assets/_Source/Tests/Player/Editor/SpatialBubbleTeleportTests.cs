using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class SpatialBubbleTeleportTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<GameObject> _objects = new List<GameObject>();
            private readonly StalkerMovementProfile _profile;

            public SpatialBubbleTeleport Bubble { get; }
            public SphereCollider Volume { get; }
            public StalkerPlayerController Player { get; }
            public StalkerMovementMotor Motor { get; }
            public StalkerCameraController Camera { get; }
            public CharacterController Body { get; }
            public Transform Destination { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                _profile = ScriptableObject.CreateInstance<StalkerMovementProfile>();
                _profile.hideFlags = HideFlags.HideAndDontSave;
                GameObject player = Create("Player");
                player.SetActive(false);
                Motor = player.AddComponent<StalkerMovementMotor>();
                Motor.Initialize(_profile);
                Player = player.AddComponent<StalkerPlayerController>();
                Camera = player.AddComponent<StalkerCameraController>();
                Camera.Initialize(_profile);
                SetField(Player, "_profile", _profile);
                SetField(Player, "_motor", Motor);
                SetField(Player, "_camera", Camera);
                player.SetActive(true);
                Body = player.GetComponent<CharacterController>();
                GameObject bubble = Create("Bubble");
                Volume = bubble.AddComponent<SphereCollider>();
                Volume.isTrigger = true;
                Volume.radius = 2f;
                Bubble = bubble.AddComponent<SpatialBubbleTeleport>();
                SetField(Bubble, "_activationVolume", Volume);
                Destination = Create("Destination").transform;
                Destination.position = new Vector3(12f, 3f, -8f);
                Bubble.Destination = Destination;
                Physics.SyncTransforms();
            }

            public GameObject Create(string name)
            {
                GameObject root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, _scene);
                root.hideFlags = HideFlags.HideAndDontSave;
                _objects.Add(root);
                return root;
            }

            public void Enter()
            {
                Invoke(Bubble, "OnTriggerEnter", Body);
                Tick(0f);
            }

            public void Tick(float deltaTime)
            {
                Invoke(Bubble, "Tick", deltaTime);
            }

            public void Place(Vector3 position)
            {
                Motor.Teleport(position);
                Physics.SyncTransforms();
            }

            public void Dispose()
            {
                foreach (GameObject root in _objects)
                {
                    if (root != null)
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                }
                UnityEngine.Object.DestroyImmediate(_profile);
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }

        [Test]
        public void BodyOverlapActivatesWithFeetOutsideTheAuthoredSphere()
        {
            using (var rig = new Rig())
            {
                rig.Place(new Vector3(2.5f, 0f, 0f));
                rig.Enter();
                Assert.That(rig.Bubble.IsActivated, Is.False);
                rig.Place(new Vector3(2.2f, 0f, 0f));
                Assert.That(rig.Body.bounds.min.x, Is.LessThan(2f));
                Assert.That(rig.Player.transform.position.magnitude, Is.GreaterThan(rig.Volume.radius));
                Invoke(rig.Bubble, "OnTriggerStay", rig.Body);
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                Assert.That(rig.Bubble.DelayRemaining, Is.EqualTo(0.9f));
            }
        }

        [Test]
        public void CapsuleSphereUsesTheStrictNativeFloatBoundary()
        {
            using (var rig = new Rig())
            {
                MethodInfo overlaps = typeof(SpatialBubbleTeleport).GetMethod("OverlapsPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(overlaps, Is.Not.Null);
                rig.Body.center = Vector3.zero;
                rig.Body.radius = 0.3f;
                rig.Player.transform.position = new Vector3(2.300999879837036f, 0f, 0f);
                Assert.That((bool)overlaps.Invoke(rig.Bubble, new object[] { rig.Body }), Is.False);
                rig.Player.transform.position = new Vector3(2.3005f, 0f, 0f);
                Assert.That((bool)overlaps.Invoke(rig.Bubble, new object[] { rig.Body }), Is.True);
                rig.Player.transform.position = new Vector3(2.3011f, 0f, 0f);
                Assert.That((bool)overlaps.Invoke(rig.Bubble, new object[] { rig.Body }), Is.False);
            }
        }

        [Test]
        public void StationaryPlayerInsideIsDiscoveredWithoutAnyTriggerCallbacks()
        {
            using (var rig = new Rig())
            {
                Assert.That(rig.Bubble.gameObject.scene.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene));
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                Assert.That(rig.Bubble.DelayRemaining, Is.EqualTo(0.9f));
                rig.Tick(0.9f);
                Assert.That(rig.Player.transform.position, Is.EqualTo(rig.Destination.position));
            }
        }

        [Test]
        public void RaisedSphereDetectsTheBodyWhileTheFeetStayOnTheGround()
        {
            using (var rig = new Rig())
            {
                rig.Volume.transform.position = new Vector3(0f, 3.35f, 0f);
                Physics.SyncTransforms();
                Assert.That(rig.Player.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That((rig.Player.transform.position - rig.Volume.transform.position).magnitude, Is.GreaterThan(rig.Volume.radius));
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                Assert.That(rig.Bubble.DelayRemaining, Is.EqualTo(0.9f));
            }
        }

        [Test]
        public void DisabledCharacterControllerKeepsItsPlayerAndRearmsWithoutCallbacks()
        {
            using (var rig = new Rig())
            {
                rig.Place(new Vector3(6f, 0f, 0f));
                Invoke(rig.Bubble, "OnTriggerEnter", rig.Body);
                rig.Body.enabled = false;
                rig.Player.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                Invoke(rig.Bubble, "OnTriggerExit", rig.Body);
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.False);
                rig.Body.enabled = true;
                Physics.SyncTransforms();
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
            }
        }

        [Test]
        public void CrowdedBroadphaseExpandsItsBufferAndStillFindsThePlayer()
        {
            using (var rig = new Rig())
            {
                for (int index = 0; index < 48; index++)
                {
                    GameObject prop = rig.Create("Prop" + index);
                    prop.transform.position = new Vector3((index % 8 - 3.5f) * 0.05f, 0.9f, (index / 8 - 2.5f) * 0.05f);
                    SphereCollider collider = prop.AddComponent<SphereCollider>();
                    collider.radius = 0.05f;
                    collider.isTrigger = true;
                }
                Physics.SyncTransforms();
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                FieldInfo buffer = typeof(SpatialBubbleTeleport).GetField("_overlapBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(buffer, Is.Not.Null);
                Assert.That(((Array)buffer.GetValue(rig.Bubble)).Length, Is.GreaterThanOrEqualTo(50));
            }
        }

        [Test]
        public void CapsuleRadiusUsesTheLargestHorizontalWorldScale()
        {
            using (var rig = new Rig())
            {
                MethodInfo overlaps = typeof(SpatialBubbleTeleport).GetMethod("OverlapsPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(overlaps, Is.Not.Null);
                rig.Player.transform.localScale = new Vector3(2f, 1.5f, 3f);
                rig.Volume.center = new Vector3(0f, 1.35f, 0f);
                rig.Player.transform.position = new Vector3(2.85f, 0f, 0f);
                Assert.That((bool)overlaps.Invoke(rig.Bubble, new object[] { rig.Body }), Is.True);
                rig.Player.transform.position = new Vector3(3.1f, 0f, 0f);
                Assert.That((bool)overlaps.Invoke(rig.Bubble, new object[] { rig.Body }), Is.False);
            }
        }

        [Test]
        public void TeleportWaitsForTheOriginalNineHundredMilliseconds()
        {
            using (var rig = new Rig())
            {
                rig.Enter();
                rig.Tick(0.899f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                Assert.That(rig.Player.transform.position, Is.EqualTo(Vector3.zero));
                rig.Tick(0.0011f);
                Assert.That(rig.Bubble.IsActivated, Is.False);
                Assert.That(rig.Player.transform.position, Is.EqualTo(rig.Destination.position));
                Assert.That(rig.Bubble.DelayRemaining, Is.Zero);
                Assert.That(rig.Body.enabled, Is.True);
            }
        }

        [Test]
        public void ExitFromOneDetectionVolumeKeepsTheKnownPlayer()
        {
            using (var rig = new Rig())
            {
                Invoke(rig.Bubble, "OnTriggerEnter", rig.Body);
                Invoke(rig.Bubble, "OnTriggerExit", rig.Body);
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                Assert.That(rig.Bubble.DelayRemaining, Is.EqualTo(0.9f));
            }
        }

        [Test]
        public void LeavingAndReenteringDoesNotCancelOrRestartThePendingTransfer()
        {
            using (var rig = new Rig())
            {
                rig.Enter();
                Invoke(rig.Bubble, "OnTriggerExit", rig.Body);
                rig.Place(new Vector3(6f, 0f, 0f));
                rig.Tick(0.4f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                Assert.That(rig.Bubble.DelayRemaining, Is.EqualTo(0.5f).Within(0.00001f));
                rig.Place(Vector3.zero);
                Invoke(rig.Bubble, "OnTriggerEnter", rig.Body);
                rig.Tick(0.5f);
                Assert.That(rig.Player.transform.position, Is.EqualTo(rig.Destination.position));
            }
        }

        [Test]
        public void DestinationUsesCurrentPositionAndYawAndResetsPitchWithoutChangingInput()
        {
            using (var rig = new Rig())
            {
                rig.Camera.SetLook(25f, -18f);
                rig.Camera.Apply(rig.Motor.Stance.CameraHeight);
                rig.Player.GameplayInputBlocked = true;
                rig.Motor.Wish = StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint;
                rig.Enter();
                rig.Tick(0.4f);
                rig.Destination.SetPositionAndRotation(new Vector3(-15f, 7f, 23f), Quaternion.Euler(20f, 126f, 15f));
                rig.Tick(0.5f);
                Assert.That(rig.Player.transform.position, Is.EqualTo(rig.Destination.position));
                Assert.That(Mathf.DeltaAngle(rig.Camera.YawDegrees, rig.Destination.eulerAngles.y), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(rig.Camera.PitchDegrees, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(rig.Player.GameplayInputBlocked, Is.True);
                Assert.That(rig.Motor.Wish, Is.EqualTo(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint));
            }
        }

        [Test]
        public void TeleportClearsExistingMomentumWithoutChangingHealthOrPower()
        {
            using (var rig = new Rig())
            {
                float health = rig.Motor.Condition.Health;
                float power = rig.Motor.Condition.Power;
                rig.Motor.Physics.SetVelocity(new Vector3(5f, -12f, -4f));
                rig.Motor.SetAnomalyControl(new Vector3(3f, 4f, 5f));
                rig.Enter();
                rig.Tick(0.9f);
                Assert.That(rig.Motor.Velocity, Is.EqualTo(Vector3.zero));
                Assert.That(rig.Player.GameplayInputBlocked, Is.False);
                Assert.That(rig.Motor.Condition.Health, Is.EqualTo(health));
                Assert.That(rig.Motor.Condition.Power, Is.EqualTo(power));
            }
        }

        [Test]
        public void DestinationInsideTheBubbleCanActivateAgainWithoutAnExtraCooldown()
        {
            using (var rig = new Rig())
            {
                rig.Destination.position = new Vector3(0.5f, 0f, 0f);
                rig.Enter();
                rig.Tick(0.9f);
                Assert.That(rig.Bubble.IsActivated, Is.False);
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                Assert.That(rig.Bubble.DelayRemaining, Is.EqualTo(0.9f));
                rig.Tick(0.899f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
            }
        }

        [Test]
        public void DisablingTheBubbleCancelsItsPendingTeleportAndClearsContacts()
        {
            using (var rig = new Rig())
            {
                rig.Enter();
                rig.Tick(0.4f);
                rig.Bubble.enabled = false;
                Invoke(rig.Bubble, "OnDisable");
                rig.Tick(1f);
                Assert.That(rig.Bubble.IsActivated, Is.False);
                Assert.That(rig.Player.transform.position, Is.EqualTo(Vector3.zero));
                rig.Bubble.enabled = true;
                rig.Enter();
                Assert.That(rig.Bubble.IsActivated, Is.True);
                Assert.That(rig.Bubble.DelayRemaining, Is.EqualTo(0.9f));
            }
        }

        [Test]
        public void MissingOrDestroyedDestinationNeverMovesThePlayer()
        {
            using (var rig = new Rig())
            {
                rig.Bubble.Destination = null;
                rig.Enter();
                Assert.That(rig.Bubble.IsActivated, Is.False);
                rig.Bubble.Destination = rig.Destination;
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.True);
                UnityEngine.Object.DestroyImmediate(rig.Destination.gameObject);
                rig.Tick(1f);
                Assert.That(rig.Bubble.IsActivated, Is.False);
                Assert.That(rig.Player.transform.position, Is.EqualTo(Vector3.zero));
            }
        }

        [Test]
        public void DisabledVolumeAndNonplayerObjectsCannotActivateTheBubble()
        {
            using (var rig = new Rig())
            {
                GameObject bolt = rig.Create("Bolt");
                rig.Place(new Vector3(6f, 0f, 0f));
                Collider probe = bolt.AddComponent<SphereCollider>();
                bolt.AddComponent<AnomalyProbe>();
                Invoke(rig.Bubble, "OnTriggerEnter", probe);
                rig.Tick(0f);
                Assert.That(rig.Bubble.IsActivated, Is.False);
                rig.Place(Vector3.zero);
                rig.Volume.enabled = false;
                rig.Enter();
                Assert.That(rig.Bubble.IsActivated, Is.False);
            }
        }

        [Test]
        public void DestroyedOrDisabledPlayerCancelsPendingTeleport()
        {
            using (var rig = new Rig())
            {
                rig.Enter();
                rig.Player.enabled = false;
                rig.Tick(1f);
                Assert.That(rig.Bubble.IsActivated, Is.False);
                Assert.That(rig.Player.transform.position, Is.EqualTo(Vector3.zero));
                rig.Player.enabled = true;
                rig.Enter();
                UnityEngine.Object.DestroyImmediate(rig.Player.gameObject);
                Assert.DoesNotThrow(() => rig.Tick(1f));
                Assert.That(rig.Bubble.IsActivated, Is.False);
            }
        }

        [Test]
        public void SourcePostprocessStartsImmediatelyAndFinishesIndependentlyOfTheZone()
        {
            using (var rig = new Rig())
            {
                AnomalyPostProcessAsset effect = ScriptableObject.CreateInstance<AnomalyPostProcessAsset>();
                try
                {
                    effect.SetImportData("anims/teleport.ppe", 2f, null, null);
                    StalkerAnomalyScreenEffects screen = rig.Player.gameObject.AddComponent<StalkerAnomalyScreenEffects>();
                    SetField(rig.Bubble, "_teleportPostProcess", effect);
                    rig.Enter();
                    Assert.That(screen.ActiveLayerCount, Is.EqualTo(1));
                    Assert.That(rig.Player.transform.position, Is.EqualTo(Vector3.zero));
                    Invoke(rig.Bubble, "OnDisable");
                    Assert.That(screen.ActiveLayerCount, Is.EqualTo(1));
                    Assert.That(rig.Bubble.IsActivated, Is.False);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(effect);
                }
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static void Invoke(SpatialBubbleTeleport target, string name, params object[] arguments)
        {
            MethodInfo method = typeof(SpatialBubbleTeleport).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(target, arguments);
        }
    }
}
