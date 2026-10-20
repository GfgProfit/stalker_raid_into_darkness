using UnityEngine;
using Random = UnityEngine.Random;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerCameraRecoil : MonoBehaviour, IRecoil
    {
        private Vector3 _targetRotation;
        private Vector3 _currentRotation;

        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public Vector3 CurrentRotation => _currentRotation;

        public void Tick(float returnSpeed, float snappiness)
        {
            _targetRotation = Vector3.Lerp(_targetRotation, Vector3.zero, returnSpeed * Time.deltaTime);
            _currentRotation = Vector3.Slerp(_currentRotation, _targetRotation, snappiness * Time.deltaTime);
            Rotation = Quaternion.Euler(_currentRotation);
        }

        public void Shoot(float x, float y, float z)
        {
            _targetRotation += new Vector3(-x, Random.Range(-y, y), Random.Range(-z, z));
        }

        public void ResetRecoil()
        {
            _targetRotation = Vector3.zero;
            _currentRotation = Vector3.zero;
            Rotation = Quaternion.identity;
        }
    }
}
