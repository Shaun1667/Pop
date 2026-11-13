using UnityEngine;

namespace Pop.Player
{
    /// <summary>플레이어 튜닝 값. 인스펙터에서 조절한다.</summary>
    [CreateAssetMenu(menuName = "Pop/Player Tuning", fileName = "PlayerTuning")]
    public class PlayerTuning : ScriptableObject
    {
        [Header("이동")]
        [Tooltip("지상 목표 속도 (m/s)")]
        public float moveSpeed = 6f;
        [Tooltip("지상 가속/감속 (m/s^2)")]
        public float groundAccel = 60f;
        [Tooltip("공중 A/D 가속 (m/s^2). 관성을 크게 해치지 않도록 약하게. 입력이 없으면 공중에서는 감속하지 않는다.")]
        public float airAccel = 15f;

        [Header("점프 / 중력")]
        [Tooltip("점프 시 위쪽 속도 (m/s)")]
        public float jumpVelocity = 8f;
        [Tooltip("착지 직전에 누른 Space를 기억하는 시간 (s)")]
        public float jumpBufferTime = 0.12f;
        [Tooltip("발판을 벗어난 직후에도 점프를 허용하는 시간 (s)")]
        public float coyoteTime = 0.08f;
        [Tooltip("중력 배율. 1 = 기본 중력. 기획서: 약간 무겁게")]
        public float gravityMultiplier = 1.6f;
        [Tooltip("최대 낙하 속도 (m/s)")]
        public float maxFallSpeed = 30f;

        [Header("바닥 판정")]
        [Tooltip("바닥으로 치는 최대 경사 (도). 접촉 법선과 위쪽의 각도")]
        [Range(0f, 89f)]
        public float maxGroundSlope = 45f;
        [Tooltip("점프 직후 바닥 판정을 무시하는 시간 (s). 점프한 프레임에 다시 바닥으로 잡히는 것 방지")]
        public float groundIgnoreAfterJump = 0.1f;

        [Header("뚫어뻥")]
        [Tooltip("뚫어뻥 사거리 (m)")]
        public float plungerRange = 15f;
        [Tooltip("발사 실패 후 재발사 대기 (s)")]
        public float fireFailCooldown = 0.3f;
        [Tooltip("바닥에서 천장/벽 부착 시 유예 시간 (s)")]
        public float groundAttachGrace = 0.2f;
        [Tooltip("바닥에서 천장/벽 부착 시 당겨 떠오르는 거리 (m, 기획서 0.3~0.5)")]
        public float groundAttachLift = 0.4f;
        [Tooltip("해제 시 관성 배율")]
        public float releaseInertiaMultiplier = 1.0f;
        [Tooltip("부착 중 A/D 스윙 힘. 줄에 수직(접선) 방향 가속도 (m/s^2, 질량 무관)")]
        public float swingForce = 20f;

        [Header("기절 (Stunned)")]
        [Tooltip("기절 중 바닥이 아닌데 거의 멈춘 채(벽 틈에 걸림) 이만큼 지나면 밀어서 빼낸다 (s)")]
        public float stuckNudgeTime = 0.4f;
        [Tooltip("끼임 탈출 속도 (m/s). 마지막 벽 접촉 법선 + 아래 방향")]
        public float stuckNudgeSpeed = 2f;
        [Tooltip("이 속도(m/s) 미만이면 '거의 멈춤'으로 본다")]
        public float stuckSpeedThreshold = 0.2f;

        [Header("낙사 (Dead)")]
        [Tooltip("이 높이(y) 아래로 떨어지면 낙사 → 즉시 리스폰")]
        public float killY = -10f;
    }
}
