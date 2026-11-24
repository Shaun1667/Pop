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
    /// - Wings 레이어(있을 때만, 날다람쥐): 바닥에서 달리는 동안 팔(비막)을 펴고 가끔 퍼덕인다. 무게 = 속도(wingsSpeedMin~Max).
    ///   Wings 레이어 = 온몸 활공 자세(다리 · 머리는 가만히). 그동안 보이는 모델만 glideHover만큼 띄우고 엉덩이 뼈를 돌려 앞으로 기울인다. 보이는 것뿐이라 캡슐 · 물리 · 이동 높이는 그대로다.
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

        [Header("달릴 때 비막 펴기 (Wings 레이어가 있는 컨트롤러만)")]
        [Tooltip("이 속도(m/s)부터 비막을 펴기 시작한다 (걷기는 그대로)")]
        public float wingsSpeedMin = 1.5f;
        [Tooltip("이 속도(m/s)에서 다 편다")]
        public float wingsSpeedMax = 4f;
        [Tooltip("비막을 펴고 접는 데 걸리는 시간 (s)")]
        public float wingsBlendTime = 0.25f;
        [Tooltip("활공 자세에서 몸 전체를 앞으로 기울이는 각도 (도). 엉덩이 뼈를 돌린다")]
        public float glideLean = 40f;
        [Tooltip("활공 자세에서 보이는 모델만 바닥 위로 띄우는 높이 (m). 캡슐 · 물리 · 이동 높이는 그대로다")]
        public float glideHover = 0.2f;
        [Tooltip("숙인 만큼 머리를 되돌리는 비율 (0 = 같이 숙임, 1 = 머리는 그대로)")]
        [Range(0f, 1f)] public float glideHeadCounter = 0.6f;
        [Header("W/S로 위 · 아래를 볼 때 고개 (보이는 것만)")]
        [Tooltip("W(위 보기)를 누르고 있을 때 고개를 드는 최대 각도 (도). 0이면 끔")]
        public float headLookUp = 32f;
        [Tooltip("S(아래 보기)를 누르고 있을 때 고개를 숙이는 최대 각도 (도). 0이면 끔")]
        public float headLookDown = 30f;
        [Tooltip("고개가 따라가고 되돌아오는 시간 (s). 클수록 느긋하다")]
        public float headLookSmoothTime = 0.18f;
        [Tooltip("각도 중 목 뼈가 맡는 비율 (나머지는 머리 뼈). 목 뼈가 없으면 머리가 전부 맡는다")]
        [Range(0f, 1f)] public float headLookNeckShare = 0.4f;


        public const string WingsLayerName = "Wings";

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int JumpTId = Animator.StringToHash("JumpT");
        static readonly int VSpeedId = Animator.StringToHash("VSpeed");

        PlayerController _player;
        float _airTime;
        float _jumpT = 0.5f;
        bool _wasGrounded = true;
        RuntimeAnimatorController _wingsCtrl;
        int _wingsLayer = -1;
        float _wings;
        Transform _spine, _head, _neck;   // _spine = 기울일 때 돌리는 뼈(엉덩이)
        Vector3 _modelBaseLocal;
        bool _hasModelBase;

        /// <summary>비막을 편 정도 0~1 (Wings 레이어 무게)</summary>
        public float Wings => _wings;

        /// <summary>애니메이션이 보는 '서 있음' (짧은 끊김을 무시한 값)</summary>
        public bool AnimGrounded { get; private set; } = true;
        public float JumpT => _jumpT;
        float _headLook, _headLookVel;   // -1(숙임) ~ +1(듦), 부드럽게 따라간 값
        /// <summary>W/S 고개 정도 -1(숙임) ~ +1(듦)</summary>
        public float HeadLook => _headLook;


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

            UpdateWings(dt);
            UpdateHeadLook(dt, st);
        }

        // W/S(GameInput.LookY, 일시정지 중엔 0)를 부드럽게 따라간다. 기절 · 사망 중엔 고개를 움직이지 않고 원위치로.
        void UpdateHeadLook(float dt, PlayerState st)
        {
            if (dt <= 0f) return;   // 일시정지(timeScale 0): 그대로 둔다
            float target = (st == PlayerState.Stunned || st == PlayerState.Dead) ? 0f : Mathf.Clamp(Pop.Input.GameInput.LookY, -1f, 1f);
            _headLook = Mathf.SmoothDamp(_headLook, target, ref _headLookVel, Mathf.Max(0.01f, headLookSmoothTime), Mathf.Infinity, dt);
        }

        void UpdateWings(float dt)
        {
            var ctrl = animator.runtimeAnimatorController;
            if (ctrl != _wingsCtrl)
            {
                _wingsCtrl = ctrl;
                _wingsLayer = animator.GetLayerIndex(WingsLayerName);
                _spine = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (!_hasModelBase) { _modelBaseLocal = animator.transform.localPosition; _hasModelBase = true; }
                _head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                _neck = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
                _wings = 0f;
            }
            if (_wingsLayer < 0) return;
            float target = AnimGrounded
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(wingsSpeedMin, wingsSpeedMax, animator.GetFloat(SpeedId)))
                : 0f;
            _wings = Mathf.MoveTowards(_wings, target, dt / Mathf.Max(0.01f, wingsBlendTime));
            animator.SetLayerWeight(_wingsLayer, _wings);
        }

        // 보이는 모델만 띄우고(무게만큼), Animator가 자세를 쓴 뒤에 엉덩이 뼈를 돌려 몸 전체를 앞으로 기울인다.
        // 매 프레임 Animator가 뼈를 다시 쓰므로 쌓이지 않는다. Player(캡슐 · Rigidbody)는 건드리지 않는다.
void LateUpdate()
        {
            if (animator == null) return;
            ApplyGlide();
            ApplyHeadLook();   // 활공 머리 보정 뒤에 더한다
        }

        void ApplyGlide()
        {
            if (_wingsLayer < 0 || !_hasModelBase) return;
            var model = animator.transform;
            Vector3 lift = Vector3.up * (glideHover * _wings);
            model.localPosition = _modelBaseLocal + (model.parent != null ? model.parent.InverseTransformVector(lift) : lift);
            if (_wings <= 0.001f || _spine == null) return;
            float lean = glideLean * _wings;
            if (Mathf.Abs(lean) < 0.01f) return;
            Vector3 right = animator.transform.right;
            _spine.Rotate(right, lean, Space.World);
            if (_head != null) _head.Rotate(right, -lean * glideHeadCounter, Space.World);
        }

        // W/S 고개: 모델의 오른쪽 축(월드)으로 돌리므로 좌우 어느 쪽을 보든 위는 위, 아래는 아래다.
        // Animator가 매 프레임 뼈를 다시 쓰므로 쌓이지 않는다.
        void ApplyHeadLook()
        {
            if (_head == null || Mathf.Abs(_headLook) < 0.001f) return;
            // + = 숙임(glideLean과 같은 방향), - = 듦
            float pitch = _headLook > 0f ? -headLookUp * _headLook : headLookDown * -_headLook;
            if (Mathf.Abs(pitch) < 0.01f) return;
            Vector3 right = animator.transform.right;
            float neckPart = _neck != null ? pitch * headLookNeckShare : 0f;
            if (_neck != null) _neck.Rotate(right, neckPart, Space.World);
            _head.Rotate(right, pitch - neckPart, Space.World);
        }
    }
}
