using UnityEngine;

namespace Pop.Player
{
    /// <summary>
    /// 다람쥐 애니메이션(보이는 것만). PlayerController의 상태 · 속도를 읽어 Animator 파라미터만 넣는다.
    /// 이동 · 물리 · 입력에는 손대지 않는다. Root Motion은 쓰지 않는다.
    /// - Speed  : 바닥에서의 좌우 속도 크기 (m/s). 0 = 대기, 클수록 걷기 → 달리기 (블렌드 트리).
    /// - Grounded: 바닥에 서 있는가. false면 공중(Air) 자세.
    /// - JumpT  : 공중 자세 진행도 0~1. 위쪽 속도 +jumpVelocity = 0(도약), 0 = 0.5(정점), -jumpVelocity = 1(착지 직전).
    ///            속도로 정하므로 점프 높이 · 체공 시간이 달라도 자세가 맞는다. 줄에 매달린 동안은 hangPose로 고정.
    /// - VSpeed : 위쪽 속도 (m/s). 오래 떨어지면(-fall 기준) Fall 자세로 넘어간다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimationVisual : MonoBehaviour
    {
        [Tooltip("다람쥐 모델의 Animator (Visual 아래)")]
        public Animator animator;
        [Tooltip("Speed 파라미터를 부드럽게 따라가는 시간 (s)")]
        public float speedDamp = 0.06f;
        [Tooltip("바닥 판정이 잠깐 끊겨도(경사 · 모서리) 이 시간 동안은 서 있는 것으로 본다 (s). 점프는 바로 공중으로 본다")]
        public float airborneDelay = 0.06f;
        [Tooltip("공중 자세(JumpT)가 목표를 따라가는 속도 (1/s)")]
        public float jumpPoseRate = 5f;
        [Tooltip("줄에 매달린 동안의 공중 자세 (0~1, 0.5 = 점프 정점 자세)")]
        [Range(0f, 1f)] public float hangPose = 0.5f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int JumpTId = Animator.StringToHash("JumpT");
        static readonly int VSpeedId = Animator.StringToHash("VSpeed");

        PlayerController _player;
        float _airTime;
        float _jumpT = 0.5f;
        bool _wasGrounded = true;

        /// <summary>애니메이션이 보는 '서 있음' (짧은 끊김을 무시한 값)</summary>
        public bool AnimGrounded { get; private set; } = true;
        public float JumpT => _jumpT;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.applyRootMotion = false;
        }

        void Update()
        {
            if (animator == null || _player == null || animator.runtimeAnimatorController == null) return;
            float dt = Time.deltaTime;
            var st = _player.State;
            Vector3 v = _player.Velocity;
            float jumpV = _player.tuning != null ? Mathf.Max(0.1f, _player.tuning.jumpVelocity) : 8f;

            bool hanging = st == PlayerState.Attached;
            bool grounded = _player.IsGrounded && !hanging;
            if (grounded) _airTime = 0f; else _airTime += dt;
            // 점프(위로 빠르게) · 매달림 · 기절은 바로 공중. 그 밖의 짧은 끊김은 서 있는 것으로.
            AnimGrounded = grounded
                || (_airTime < airborneDelay && v.y < 1f && !hanging && st != PlayerState.Stunned);

            float target = hanging ? hangPose : Mathf.InverseLerp(jumpV, -jumpV, v.y);
            // 바닥에 있는 동안은 그대로 둔다(착지 전환 중 자세가 튀지 않게). 공중으로 나가는 순간 목표로 맞추고, 그 뒤로는 따라간다.
            if (!AnimGrounded)
                _jumpT = _wasGrounded ? target : Mathf.MoveTowards(_jumpT, target, jumpPoseRate * dt);
            _wasGrounded = AnimGrounded;

            animator.SetBool(GroundedId, AnimGrounded);
            animator.SetFloat(SpeedId, AnimGrounded ? Mathf.Abs(v.x) : 0f, speedDamp, dt);
            animator.SetFloat(JumpTId, Mathf.Clamp(_jumpT, 0f, 0.999f));
            animator.SetFloat(VSpeedId, hanging ? 0f : v.y);
        }
    }
}
