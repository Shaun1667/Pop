using UnityEngine;

namespace Pop.CameraSystem
{
    /// <summary>측면 추적 카메라 튜닝 값.</summary>
    [CreateAssetMenu(menuName = "Pop/Camera Tuning", fileName = "CameraTuning")]
    public class CameraTuning : ScriptableObject
    {
        [Header("추적")]
        [Tooltip("플레이어와의 Z 거리 (카메라는 -Z 쪽에서 +Z를 바라봄)")]
        public float distance = 14f;
        [Tooltip("플레이어 위쪽 시야 여유 (m)")]
        public float offsetY = 2f;
        [Tooltip("추적 SmoothDamp 시간 (s). 작을수록 딱 붙음")]
        public float followSmoothTime = 0.15f;

        [Header("W/S 시점 이동")]
        [Tooltip("W/S로 이동할 수 있는 최대 거리 (m)")]
        public float lookDistance = 6f;
        [Tooltip("W/S를 누르는 동안 시점이 움직이는 속도 (m/s)")]
        public float lookSpeed = 12f;
        [Tooltip("W/S를 떼면 원위치로 돌아오는 SmoothDamp 시간 (s)")]
        public float lookReturnSmoothTime = 0.25f;
    }
}
