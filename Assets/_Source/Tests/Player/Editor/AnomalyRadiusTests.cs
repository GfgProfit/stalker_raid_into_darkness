using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyRadiusTests
    {
        [Test]
        public void SourceRadiusChoosesEitherIntegerOnceAndSurvivesReenable()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(6723);
                bool foundTwo = false;
                bool foundThree = false;
                for (int index = 0; index < 32; index++)
                {
                    GameObject root = new GameObject(nameof(AnomalyRadiusTests));
                    SceneManager.MoveGameObjectToScene(root, scene);
                    root.hideFlags = HideFlags.HideAndDontSave;
                    root.SetActive(false);
                    SphereCollider volume = root.AddComponent<SphereCollider>();
                    volume.radius = 5f;
                    AnomalyPlayerReaction reaction = root.AddComponent<AnomalyPlayerReaction>();
                    SetField(reaction, "_randomizeRadius", true);
                    SetField(reaction, "_randomRadiusMin", 2);
                    SetField(reaction, "_randomRadiusMax", 3);
                    Invoke(reaction, "Awake");
                    float radius = volume.radius;
                    Assert.That(radius == 2f || radius == 3f, Is.True);
                    foundTwo |= radius == 2f;
                    foundThree |= radius == 3f;
                    Invoke(reaction, "OnEnable");
                    Invoke(reaction, "OnDisable");
                    Invoke(reaction, "OnEnable");
                    Assert.That(volume.radius, Is.EqualTo(radius));
                    UnityEngine.Object.DestroyImmediate(root);
                }
                Assert.That(foundTwo, Is.True);
                Assert.That(foundThree, Is.True);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Random.state = randomState;
            }
        }

        [Test]
        public void ExistingAnomalyRadiusIsUnchangedWithoutOptIn()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = new GameObject(nameof(AnomalyRadiusTests));
                SceneManager.MoveGameObjectToScene(root, scene);
                root.hideFlags = HideFlags.HideAndDontSave;
                root.SetActive(false);
                SphereCollider volume = root.AddComponent<SphereCollider>();
                volume.radius = 4.7f;
                AnomalyPlayerReaction reaction = root.AddComponent<AnomalyPlayerReaction>();
                Invoke(reaction, "Awake");
                Assert.That(volume.radius, Is.EqualTo(4.7f));
                UnityEngine.Object.DestroyImmediate(root);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void ProbePulsePresentsHitOnlyForActiveNonignoredContactWithoutPlayingAudio()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = new GameObject(nameof(AnomalyRadiusTests));
                SceneManager.MoveGameObjectToScene(root, scene);
                root.hideFlags = HideFlags.HideAndDontSave;
                AnomalyAudio audio = root.AddComponent<AnomalyAudio>();
                AnomalyLightTestVisual visual = root.AddComponent<AnomalyLightTestVisual>();
                SetField(visual, "_audio", audio);
                AnomalyPlayerReaction reaction = root.AddComponent<AnomalyPlayerReaction>();
                SetField(reaction, "_visual", visual);
                SetField(reaction, "_smallObjectIgnoreDelay", -1f);
                visual.ShowIdle();
                GameObject bolt = new GameObject("Probe");
                SceneManager.MoveGameObjectToScene(bolt, scene);
                bolt.hideFlags = HideFlags.HideAndDontSave;
                bolt.transform.position = new Vector3(7f, 8f, 9f);
                SphereCollider collider = bolt.AddComponent<SphereCollider>();
                bolt.AddComponent<AnomalyProbe>();
                Invoke(reaction, "RegisterContact", collider);

                Invoke(reaction, "ApplyPulse");
                Assert.That(GetField<Vector3>(audio, "_hitPosition"), Is.EqualTo(bolt.transform.position));

                Vector3 unchanged = new Vector3(-2f, -3f, -4f);
                SetField(audio, "_hitPosition", unchanged);
                SetField(reaction, "_ignoreSmallObjects", true);
                Invoke(reaction, "ApplyPulse");
                Assert.That(GetField<Vector3>(audio, "_hitPosition"), Is.EqualTo(unchanged));
                SetField(reaction, "_ignoreSmallObjects", false);
                collider.enabled = false;
                Invoke(reaction, "ApplyPulse");
                Assert.That(GetField<Vector3>(audio, "_hitPosition"), Is.EqualTo(unchanged));
                Assert.That(audio.GetComponentsInChildren<AudioSource>().Length, Is.Zero);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(bolt);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FindField(target, name).SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            return (T)FindField(target, name).GetValue(target);
        }

        private static FieldInfo FindField(object target, string name)
        {
            System.Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }
                type = type.BaseType;
            }
            Assert.Fail(name);
            return null;
        }

        private static void Invoke(AnomalyPlayerReaction target, string name, params object[] arguments)
        {
            MethodInfo method = typeof(AnomalyPlayerReaction).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(target, arguments);
        }
    }
}
