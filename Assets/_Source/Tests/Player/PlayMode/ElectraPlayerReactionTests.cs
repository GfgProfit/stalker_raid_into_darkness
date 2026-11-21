#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        private const string AMBIENT_PATH = "Assets/_Source/Anomalies/AcidicComet/AcidicComet.prefab";
        private const float AWAKING_TIME = 0.2f;
        private const float BLOWOUT_TIME = 0.15f;
        private const float ACCUMULATE_TIME = 0.6f;
        private const float CYCLE_TIME = AWAKING_TIME + BLOWOUT_TIME + ACCUMULATE_TIME;
        private const float TIMEOUT = 3f;

        private readonly List<Camera> _disabledViews = new();

        private Scene _scene;
        private Scene _previousScene;
        private GameObject _holder;
        private GameObject _electra;
        private StalkerPlayerController _player;
        private AnomalyPlayerReaction _reaction;
        private ElectraVisual _visual;
        private Vector3 _origin;
        private float _previousTimeScale;
        private int? _previousAnomalyQuality;
        private bool _controlsAnomalyQuality;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;
        private RenderTexture _effectsTarget;

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
            floor.AddComponent<AudioListener>();

            _holder.SetActive(true);
            var anomalyHolder = new GameObject("ElectraReactionAnomaly");
            anomalyHolder.SetActive(false);
            _electra = Object.Instantiate(electraPrefab, _origin, Quaternion.identity, anomalyHolder.transform);
            _reaction = _electra.GetComponent<AnomalyPlayerReaction>();
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
            if (_controlsAnomalyQuality)
            {
                AnomalyVisualQuality.Apply(_previousAnomalyQuality);
                _controlsAnomalyQuality = false;
            }
            if (_previousScene.IsValid() && _previousScene.isLoaded)
            {
                SceneManager.SetActiveScene(_previousScene);
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
            if (_effectsTarget != null)
            {
                Object.Destroy(_effectsTarget);
                _effectsTarget = null;
            }

            foreach (Camera view in _disabledViews)
            {
                if (view != null)
                {
                    view.enabled = true;
                }
            }
            _disabledViews.Clear();
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
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Idle), "A non-player collider triggered Electra.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            prop.transform.position = _origin + Vector3.forward * 8f;

            MovePlayer(true);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Awaking, "The real CharacterController did not start awaking.");
            Assert.That(_visual.IsDischarging, Is.False, "Discharge started before awaking completed.");
            MovePlayer(false);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Blowout, "Leaving during awaking cancelled the committed discharge.");
            Assert.That(_visual.IsDischarging, Is.True);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Accumulate, "Blowout did not start accumulation.");
            float firstDeadline = Time.time + _reaction.CooldownRemaining;

            MovePlayer(true);
            yield return WaitPhysics(0.08f);
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Accumulate), "Re-entry bypassed accumulation.");
            Assert.That(Time.time + _reaction.CooldownRemaining, Is.EqualTo(firstDeadline).Within(0.04f), "Re-entry or multiple player colliders restarted accumulation.");
            Assert.That(_visual.IsDischarging, Is.True);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Blowout, "Long presentation particles blocked the next original-rhythm discharge.");
            Assert.That(_visual.IsDischarging, Is.True);

            _visual.Hide();
            _visual.ShowIdle();
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Idle), "Hide followed by ShowIdle in the same frame retained the previous cycle.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Awaking, "A retained player was not detected again after same-frame Hide and ShowIdle.");
            Assert.That(_visual.IsDischarging, Is.False);

            _electra.SetActive(false);
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Idle));
            Assert.That(_reaction.CooldownRemaining, Is.Zero, "Disabling the anomaly did not reset its cycle.");
            yield return WaitPhysics(0.05f);
            _electra.SetActive(true);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Awaking, "An anomaly enabled around the player did not start a new awaking phase.");
            Assert.That(_visual.IsDischarging, Is.False);

            foreach (ParticleSystem system in _electra.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.simulationSpeed = 30f;
            }

            _visual.Hide();
            yield return WaitPhysics(CYCLE_TIME + 0.15f);
            Assert.That(_visual.IsVisible, Is.False, "Trigger stay re-enabled a hidden effect.");
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Idle));
            Assert.That(_reaction.CooldownRemaining, Is.Zero);

            _reaction.enabled = false;
            _visual.ShowIdle();
            yield return WaitPhysics(0.15f);
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Idle), "A disabled reaction processed trigger callbacks.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            _reaction.enabled = true;
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Awaking, "Enabling the reaction while the player stayed inside did not start awaking.");

            MovePlayer(false);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Idle, "The committed cycle did not finish after the player left.");
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Idle));
            _player.enabled = false;
            MovePlayer(true);
            yield return WaitPhysics(AWAKING_TIME + 0.15f);
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Idle), "A disabled player controller triggered Electra.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            _player.enabled = true;
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Awaking, "Re-enabled player inside was not detected.");

            Object.Destroy(_holder);
            yield return null;
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Idle, "The committed cycle did not finish after the player was destroyed.");
            yield return WaitPhysics(0.2f);
            Assert.That(_reaction.Phase, Is.EqualTo(AnomalyPhase.Idle), "Destroyed player left a repeating reaction behind.");
            Assert.That(_reaction.CooldownRemaining, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AmbientParticlesPauseAndResumeWithoutInterruptingDischarge()
        {
            var tierField = typeof(AnomalyVisualQuality).GetField("_tier", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(tierField, Is.Not.Null);
            int tier = (int)tierField.GetValue(null);
            _previousAnomalyQuality = tier >= 0 ? tier : null;
            _controlsAnomalyQuality = true;
            AnomalyVisualQuality.Apply(2);

            var ambientPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AMBIENT_PATH);
            Assert.That(ambientPrefab, Is.Not.Null);
            var ambient = Object.Instantiate(ambientPrefab, _origin + Vector3.forward * 20f, Quaternion.identity);
            var ambientVisual = ambient.GetComponent<OverlappingAnomalyVisual>();
            Assert.That(ambientVisual, Is.Not.Null);
            var auxiliarySystems = new SerializedObject(ambientVisual).FindProperty("_idleAuxiliarySystems");
            ParticleSystem idle = null;
            for (int index = 0; index < auxiliarySystems.arraySize; index++)
            {
                if (auxiliarySystems.GetArrayElementAtIndex(index).objectReferenceValue is ParticleSystem system && system.name == "heat_glow00_green_00")
                {
                    idle = system;
                    break;
                }
            }
            Assert.That(idle, Is.Not.Null);
            var idleRenderer = idle.GetComponent<ParticleSystemRenderer>();
            Assert.That(idleRenderer, Is.Not.Null);
            Assert.That(idleRenderer.enabled, Is.True);

            var visual = new SerializedObject(_visual);
            var lightCarryingIdle = (ParticleSystem)visual.FindProperty("_idle").objectReferenceValue;
            var discharge = (ParticleSystem)visual.FindProperty("_discharge").objectReferenceValue;
            Assert.That(lightCarryingIdle, Is.Not.Null);
            Assert.That(lightCarryingIdle.lights.enabled, Is.True);
            Assert.That(discharge, Is.Not.Null);

            foreach (Camera existingView in Camera.allCameras)
            {
                if (existingView.cameraType == CameraType.Game || existingView.cameraType == CameraType.VR)
                {
                    _disabledViews.Add(existingView);
                    existingView.enabled = false;
                }
            }

            var view = new GameObject("ElectraEffectsView").AddComponent<Camera>();
            _effectsTarget = new RenderTexture(64, 64, 24);
            view.targetTexture = _effectsTarget;
            view.cullingMask = (1 << idle.gameObject.layer) | (1 << discharge.gameObject.layer);
            view.farClipPlane = 800f;
            Vector3 nearPosition = _origin - Vector3.forward * 40f;
            Vector3 farPosition = _origin - Vector3.forward * 500f;
            view.transform.SetPositionAndRotation(nearPosition, Quaternion.identity);
            yield return WaitUntil(() => idle.isPlaying && !idleRenderer.forceRenderingOff, "The visible idle effect did not start.");

            view.transform.position = farPosition;
            yield return WaitUntil(() => idleRenderer.forceRenderingOff, "The test camera did not drive distance culling.");
            yield return WaitUntil(() => idle.isPaused, "Distant idle particles kept simulating.");
            float pausedTime = idle.time;
            yield return WaitPhysics(0.3f);
            Assert.That(idle.time, Is.EqualTo(pausedTime).Within(0.0001f), "The idle simulation advanced while suspended.");
            Assert.That(ambientVisual.IsVisible, Is.True, "Distance culling hid the anomaly itself.");
            Assert.That(lightCarryingIdle.isPaused, Is.False, "Distance culling paused Electra's per-particle lights.");

            view.transform.position = nearPosition;
            yield return WaitUntil(() => idle.isPlaying && !idleRenderer.forceRenderingOff, "Distant idle particles did not return in range.");
            view.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            yield return WaitUntil(() => idle.isPaused, "In-range idle particles behind the camera kept simulating.");
            view.transform.rotation = Quaternion.identity;
            yield return WaitUntil(() => idle.isPlaying, "The idle effect did not return when the camera turned back.");
            float resumedTime = idle.time;
            yield return WaitUntil(() => Mathf.Abs(idle.time - resumedTime) > 0.01f, "The resumed idle simulation did not advance.");

            idle.Pause(false);
            view.transform.position = farPosition;
            yield return WaitUntil(() => idleRenderer.forceRenderingOff, "The manually paused effect did not enter the distance culling range.");
            view.transform.position = nearPosition;
            yield return WaitUntil(() => !idleRenderer.forceRenderingOff, "The manually paused effect did not leave the distance culling range.");
            Assert.That(idle.isPaused, Is.True, "Returning in range resumed a pause owned by another caller.");
            idle.Play(false);

            view.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            yield return WaitUntil(() => idle.isPaused, "Idle particles did not suspend again.");
            Assert.That(lightCarryingIdle.isPaused, Is.False, "Frustum culling paused Electra's per-particle lights.");
            var dischargeMain = discharge.main;
            dischargeMain.simulationSpeed = 0.1f;
            dischargeMain.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            yield return WaitUntil(() => _reaction.IsReady, "Electra did not become ready.");
            MovePlayer(true);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Awaking, "The offscreen anomaly ignored player contact.");
            MovePlayer(false);
            yield return WaitUntil(() => _reaction.Phase == AnomalyPhase.Blowout, "The offscreen anomaly did not discharge.");
            Assert.That(_visual.IsDischarging, Is.True);
            float dischargeTime = discharge.time;
            yield return WaitPhysics(0.45f);
            Assert.That(discharge.isPaused, Is.False, "Visibility culling paused the discharge.");
            Assert.That(discharge.time, Is.GreaterThan(dischargeTime), "Offscreen discharge particles did not advance.");
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
