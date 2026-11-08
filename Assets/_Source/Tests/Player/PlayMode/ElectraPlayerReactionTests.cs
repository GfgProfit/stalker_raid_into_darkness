#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class ElectraPlayerReactionTests
    {
        private const string PLAYER_PATH = "Assets/_Source/FirstPerson/FPS_Player.prefab";
        private const string ELECTRA_PATH = "Assets/_Source/Anomalies/Electra/Electra.prefab";
        private const float AWAKING_TIME = 0.2f;
        private const float BLOWOUT_TIME = 0.15f;
        private const float ACCUMULATE_TIME = 0.6f;
        private const float CYCLE_TIME = AWAKING_TIME + BLOWOUT_TIME + ACCUMULATE_TIME;
        private const float TIMEOUT = 3f;

        private Scene _scene;
        private Scene _previousScene;
        private GameObject _holder;
        private GameObject _electra;
        private StalkerPlayerController _player;
        private ElectraPlayerReaction _reaction;
        private ElectraVisual _visual;
        private Vector3 _origin;
        private float _previousTimeScale;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousScene = SceneManager.GetActiveScene();
            _previousTimeScale = Time.timeScale;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene(nameof(ElectraPlayerReactionTests));
            SceneManager.SetActiveScene(_scene);
            _origin = new Vector3(10000f, 1000f, 10000f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ElectraReactionFloor";
            floor.transform.position = _origin + Vector3.down * 0.1f;
            floor.transform.localScale = new Vector3(30f, 0.2f, 30f);

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PATH);
            var electraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ELECTRA_PATH);
            Assert.That(playerPrefab, Is.Not.Null);
            Assert.That(electraPrefab, Is.Not.Null);
            _holder = new GameObject("ElectraReactionPlayer");
            _holder.SetActive(false);
            var playerObject = Object.Instantiate(playerPrefab, _holder.transform);
            playerObject.transform.position = _origin + new Vector3(6f, 0.05f, 0f);
            _player = playerObject.GetComponent<StalkerPlayerController>();
            Assert.That(_player, Is.Not.Null);
            _player.GameplayInputBlocked = true;

            foreach (MonoBehaviour behaviour in playerObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = behaviour == _player || behaviour is StalkerMovementMotor || behaviour is StalkerStanceController || behaviour is StalkerActorCondition;
            }

            var input = new SerializedObject(playerObject.GetComponent<StalkerPlayerInput>());
            input.FindProperty("_actions").objectReferenceValue = null;
            input.ApplyModifiedPropertiesWithoutUndo();
            var camera = new SerializedObject(playerObject.GetComponent<StalkerCameraController>());
            camera.FindProperty("_lockCursorOnStart").boolValue = false;
            camera.ApplyModifiedPropertiesWithoutUndo();

            foreach (Camera view in playerObject.GetComponentsInChildren<Camera>(true))
            {
                view.enabled = false;
            }

            foreach (AudioListener listener in playerObject.GetComponentsInChildren<AudioListener>(true))
            {
                listener.enabled = false;
            }

            _holder.SetActive(true);
            var anomalyHolder = new GameObject("ElectraReactionAnomaly");
            anomalyHolder.SetActive(false);
            _electra = Object.Instantiate(electraPrefab, _origin, Quaternion.identity, anomalyHolder.transform);
            _reaction = _electra.GetComponent<ElectraPlayerReaction>();
            _visual = _electra.GetComponent<ElectraVisual>();
            Assert.That(_reaction, Is.Not.Null);
            Assert.That(_visual, Is.Not.Null);
            var reaction = new SerializedObject(_reaction);
            reaction.FindProperty("_awakingTime").floatValue = AWAKING_TIME;
            reaction.FindProperty("_blowoutTime").floatValue = BLOWOUT_TIME;
            reaction.FindProperty("_accumulateTime").floatValue = ACCUMULATE_TIME;
            reaction.ApplyModifiedPropertiesWithoutUndo();

            var extra = new GameObject("AdditionalPlayerCollider");
            extra.transform.SetParent(playerObject.transform, false);
            extra.transform.localPosition = Vector3.up;
            extra.AddComponent<SphereCollider>().radius = 0.2f;
            anomalyHolder.SetActive(true);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_previousScene.IsValid() && _previousScene.isLoaded)
            {
                SceneManager.SetActiveScene(_previousScene);
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }

            Time.timeScale = _previousTimeScale;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
        }

        [UnityTest]
        public IEnumerator OnlyPlayerTriggersOriginalPhasesAndLifecycleGuards()
        {
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.transform.position = _origin + Vector3.up;
            yield return WaitUntil(() => _reaction.IsReady, "Electra did not become ready.");
            yield return WaitPhysics(0.1f);
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Idle), "A non-player collider triggered Electra.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            prop.transform.position = _origin + Vector3.forward * 8f;

            MovePlayer(true);
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Awaking, "The real CharacterController did not start awaking.");
            Assert.That(_visual.IsDischarging, Is.False, "Discharge started before awaking completed.");
            MovePlayer(false);
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Blowout, "Leaving during awaking cancelled the committed discharge.");
            Assert.That(_visual.IsDischarging, Is.True);
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Accumulate, "Blowout did not start accumulation.");
            float firstDeadline = Time.time + _reaction.CooldownRemaining;

            MovePlayer(true);
            yield return WaitPhysics(0.08f);
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Accumulate), "Re-entry bypassed accumulation.");
            Assert.That(Time.time + _reaction.CooldownRemaining, Is.EqualTo(firstDeadline).Within(0.04f), "Re-entry or multiple player colliders restarted accumulation.");
            Assert.That(_visual.IsDischarging, Is.True);
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Blowout, "Long presentation particles blocked the next original-rhythm discharge.");
            Assert.That(_visual.IsDischarging, Is.True);

            _visual.Hide();
            _visual.ShowIdle();
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Idle), "Hide followed by ShowIdle in the same frame retained the previous cycle.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Awaking, "A retained player was not detected again after same-frame Hide and ShowIdle.");
            Assert.That(_visual.IsDischarging, Is.False);

            _electra.SetActive(false);
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Idle));
            Assert.That(_reaction.CooldownRemaining, Is.Zero, "Disabling the anomaly did not reset its cycle.");
            yield return WaitPhysics(0.05f);
            _electra.SetActive(true);
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Awaking, "An anomaly enabled around the player did not start a new awaking phase.");
            Assert.That(_visual.IsDischarging, Is.False);

            foreach (ParticleSystem system in _electra.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.simulationSpeed = 30f;
            }

            _visual.Hide();
            yield return WaitPhysics(CYCLE_TIME + 0.15f);
            Assert.That(_visual.IsVisible, Is.False, "Trigger stay re-enabled a hidden effect.");
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Idle));
            Assert.That(_reaction.CooldownRemaining, Is.Zero);

            _reaction.enabled = false;
            _visual.ShowIdle();
            yield return WaitPhysics(0.15f);
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Idle), "A disabled reaction processed trigger callbacks.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            _reaction.enabled = true;
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Awaking, "Enabling the reaction while the player stayed inside did not start awaking.");

            MovePlayer(false);
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Idle, "The committed cycle did not finish after the player left.");
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Idle));
            _player.enabled = false;
            MovePlayer(true);
            yield return WaitPhysics(AWAKING_TIME + 0.15f);
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Idle), "A disabled player controller triggered Electra.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            _player.enabled = true;
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Awaking, "Re-enabled player inside was not detected.");

            Object.Destroy(_holder);
            yield return null;
            yield return WaitUntil(() => _reaction.Phase == ElectraPhase.Idle, "The committed cycle did not finish after the player was destroyed.");
            yield return WaitPhysics(0.2f);
            Assert.That(_reaction.Phase, Is.EqualTo(ElectraPhase.Idle), "Destroyed player left a repeating reaction behind.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        private void MovePlayer(bool inside)
        {
            _player.Motor.Teleport(_origin + new Vector3(inside ? 0f : 6f, 0.05f, 0f));
            Physics.SyncTransforms();
        }

        private static IEnumerator WaitPhysics(float duration)
        {
            float end = Time.time + duration;
            while (Time.time < end)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, string failure)
        {
            float deadline = Time.time + TIMEOUT;
            while (condition() == false && Time.time < deadline)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }

            Assert.That(condition(), Is.True, failure);
        }
    }
}
#endif
