using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerMotorHarness : IDisposable
    {
        public const string PROFILE_PATH = "Assets/_Source/Settings/Player/StalkerMovementProfile.asset";

        private readonly List<GameObject> _objects = new List<GameObject>();

        public StalkerMotorHarness()
        {
            Profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>(PROFILE_PATH);
            if (Profile == null)
            {
                throw new InvalidOperationException("StalkerMovementProfile.asset is missing; run Tools > X-Ray > Build Stalker Movement Profile.");
            }

            AddBox("Ground", new Vector3(0.0f, -0.25f, 0.0f), new Vector3(400.0f, 0.5f, 400.0f), Quaternion.identity);
            var player = new GameObject("TestPlayer");
            _objects.Add(player);
            player.transform.position = new Vector3(0.0f, 0.0f, 0.0f);
            Motor = player.AddComponent<StalkerMovementMotor>();
            Motor.Initialize(Profile);
            Physics.SyncTransforms();
        }

        public StalkerMovementProfile Profile { get; }
        public StalkerMovementMotor Motor { get; }
        public Transform Transform => Motor.transform;
        public float Yaw { get; set; }
        public float Pitch { get; set; }

        public GameObject AddBox(string name, Vector3 position, Vector3 size, Quaternion rotation)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetPositionAndRotation(position, rotation);
            box.transform.localScale = size;
            _objects.Add(box);
            Physics.SyncTransforms();
            return box;
        }

        public StalkerLadder AddLadder(Vector3 bottom, Vector3 normal, float height)
        {
            var ladder = new GameObject("TestLadder");
            ladder.transform.SetPositionAndRotation(bottom, Quaternion.LookRotation(normal, Vector3.up));
            var collider = ladder.AddComponent<BoxCollider>();
            collider.center = new Vector3(0.0f, height * 0.5f, -0.05f);
            collider.size = new Vector3(0.6f, height, 0.1f);
            var component = ladder.AddComponent<StalkerLadder>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("_height").floatValue = height;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _objects.Add(ladder);
            Physics.SyncTransforms();
            return component;
        }

        public void Place(Vector3 position)
        {
            Motor.Teleport(position);
            Physics.SyncTransforms();
        }

        public void Settle(float seconds, float deltaTime = 1.0f / 60.0f)
        {
            Run(StalkerMoveFlags.None, seconds, deltaTime);
        }

        public void Run(StalkerMoveFlags wish, float seconds, float deltaTime, Action<float> onFrame = null)
        {
            float time = 0.0f;
            while (time < seconds - 0.00001f)
            {
                Frame(wish, deltaTime);
                time += deltaTime;
                onFrame?.Invoke(time);
            }
        }

        public void Frame(StalkerMoveFlags wish, float deltaTime)
        {
            Vector3 direction = Quaternion.Euler(Pitch, Yaw, 0.0f) * Vector3.forward;
            Motor.SetView(Yaw, direction);
            Motor.Wish = (Motor.Wish & ~StalkerMoveFlags.Jump) | wish;

            Motor.Tick(deltaTime);
            Motor.ClearHeldCommands();
        }

        public float HorizontalSpeed
        {
            get
            {
                Vector3 velocity = Motor.Velocity;
                velocity.y = 0.0f;
                return velocity.magnitude;
            }
        }

        public void Dispose()
        {
            foreach (GameObject item in _objects)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }

            _objects.Clear();
        }
    }
}
