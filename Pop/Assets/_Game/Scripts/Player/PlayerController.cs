using System;
using Pop.Input;
using UnityEngine;

namespace Pop.Player
{
    /// <summary>
    /// Rigidbody 기반 캡슐 플레이어. 측면 한 평면(XY)에서 A/D 이동과 점프만 한다.
    /// 입력은 Update에서 읽고 물리는 FixedUpdate에서 적용한다. 점프는 버퍼로 놓치지 않는다.
    /// 바닥 판정은 접촉 법선(위쪽 성분)으로 한다. 법선이 바닥 경사(maxGroundSlope)보다 가파르면 벽/천장.
    /// Attached 중 접촉 → 즉시 해제 (바닥이면 해제만, 벽/천장이면 해제 + Stunned).
    /// 공중(Normal/Flying/Falling, 바닥 접촉 없음)에서 벽/천장에 airStunMinImpactSpeed 넘게 부딪히면 Stunned (2026-11-20 사용자).
    /// 바닥에 서서 벽으로 걸어 들어가는 것, 바닥과 벽에 같이 닿은 스텝, 스치기는 기절이 아니다.
    /// 부착 면 예외 없음: 부착한 콜라이더 자신에 닿아도 같은 규칙 (2026-11-13 사용자 결정, 기획서 4장 표 첫 줄과 다름).
    /// 바닥에서 부착하면 groundAttachGrace 동안 모든 접촉 무시, 바닥에서 떨어지기 전까지 바닥 접촉 무시.
    /// Stunned는 바닥에 착지해야만 풀린다. 낙사(killY 아래 / KillZone) → Dead → 즉시 리스폰 → Normal.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class PlayerController : MonoBehaviour
    {
        public PlayerTuning tuning;
        [Tooltip("마찰 0 PhysicsMaterial. 비어 있으면 실행 시 만들어 붙인다.")]
        public PhysicsMaterial noFrictionMaterial;
        [Tooltip("리스폰 지점. 비우면 씬의 'RespawnPoint' 오브젝트, 그것도 없으면 시작 위치. 체크포인트는 SetRespawnPoint로 갱신한다.")]
        public Transform respawnPoint;

        Rigidbody _rb;
        CapsuleCollider _col;

        // 입력 (Update에서 채움)
        float _moveInput;
        float _jumpBufferTimer;

        // 바닥 판정
        bool _groundContactThisStep;
        Vector3 _groundNormalThisStep = Vector3.up;
        bool _isGrounded;
        Vector3 _groundNormal = Vector3.up;
        float _coyoteTimer;
        float _groundIgnoreTimer;

        // 벽/천장 접촉 (바닥이 아닌 접촉). 끼임 탈출 방향에 쓴다.
        Vector3 _lastWallNormal = Vector3.zero;

        PlayerState _state = PlayerState.Falling;

        /// <summary>현재 상태</summary>
        public PlayerState State => _state;
        /// <summary>상태가 바뀔 때 (이전, 새 상태)</summary>
        public event Action<PlayerState, PlayerState> StateChanged;

        public bool IsGrounded => _isGrounded;
        public Vector3 GroundNormal => _groundNormal;
        public Rigidbody Body => _rb;
        public Vector3 Velocity => _rb != null ? _rb.linearVelocity : Vector3.zero;
        /// <summary>현재 A/D 입력 (-1~1). Stunned/Dead 중에는 0.</summary>
        public float MoveInput => _moveInput;
        public bool IsStunned => _state == PlayerState.Stunned;
        /// <summary>마지막 벽/천장 접촉 법선 (플레이어 쪽). 없으면 zero.</summary>
        public Vector3 LastWallNormal => _lastWallNormal;

        // ---- 뚫어뻥 줄 (Attached) ----
        [Tooltip("줄 길이보다 이만큼 넘게 벌어지면(충돌 등) 위치를 바로 원 위로 되돌린다 (m). 그 이하는 속도로 맞춘다.")]
        public float ropeSnapTolerance = 0.02f;

        Vector3 _attachPoint;
        Vector3 _attachNormal = Vector3.up;
        Collider _attachedCollider;
        float _ropeLength;
        float _reelFromLength;
        bool _attachedFromGround;
        float _attachTime = -999f;
        bool _leftGroundSinceAttach;
        bool _releaseRequested;
        bool _releaseWithJump;

        // 부착 중 접촉 (지난 물리 스텝). 벽/천장 > 바닥.
        bool _attachHitWall;
        bool _attachHitGround;

        // 기절
        float _stuckTimer;

        // 공중 벽/천장 충돌 기절 (Normal/Flying/Falling 중 공중)
        bool _airHitWall;          // 지난 물리 스텝에 airStunMinImpactSpeed 넘게 벽/천장에 부딪혔는가
        bool _preStepValid;        // 지난 FixedUpdate가 일반 이동 경로로 속도를 넣었는가
        bool _preStepAirborne;     // 그 스텝을 공중에서 시작했는가 (바닥에서 점프한 스텝은 false)
        Vector3 _preStepVelocity;  // 그 스텝에 넣은 속도 + 기본 중력 한 스텝 = 충돌 직전 속도 추정

        // 리스폰
        Vector3 _respawnPosition;
        float _respawnFixedTime = -999f; // 마지막 리스폰 시각 (Time.fixedTime)
        const float RespawnSettleTime = 0.05f; // 리스폰 직후 이 시간 동안 재낙사·Falling 전환 무시

        bool JustRespawned => Time.fixedTime - _respawnFixedTime < RespawnSettleTime;

        public bool IsAttached => _state == PlayerState.Attached;
        /// <summary>부착 지점 (월드). Attached가 아니면 마지막 값.</summary>
        public Vector3 AttachPoint => _attachPoint;
        /// <summary>부착 면 법선</summary>
        public Vector3 AttachNormal => _attachNormal;
        /// <summary>부착한 콜라이더 (해제 후 null)</summary>
        public Collider AttachedCollider => _attachedCollider;
        /// <summary>고정 줄 길이 (m). 바닥 부착이면 거리 − groundAttachLift (최소 0.5).</summary>
        public float RopeLength => _ropeLength;
        /// <summary>지금 줄 제약에 쓰는 길이. 바닥 부착 직후 groundAttachGrace 동안 RopeLength까지 부드럽게 줄어든다.</summary>
        public float EffectiveRopeLength
        {
            get
            {
                if (!_attachedFromGround || _reelFromLength <= _ropeLength || tuning == null) return _ropeLength;
                float grace = Mathf.Max(0.05f, tuning.groundAttachGrace);
                float t = Mathf.Clamp01((Time.time - _attachTime) / grace);
                t = t * t * (3f - 2f * t);
                return Mathf.Lerp(_reelFromLength, _ropeLength, t);
            }
        }
        /// <summary>바닥에 선 채로 부착했는가</summary>
        public bool AttachedFromGround => _attachedFromGround;
        /// <summary>부착한 시각 (Time.time)</summary>
        public float AttachTime => _attachTime;
        /// <summary>바닥 부착 후 groundAttachGrace 안인가. 이 동안은 모든 접촉 판정을 무시한다.</summary>
        public bool InGroundAttachGrace => IsAttached && _attachedFromGround && tuning != null && Time.time - _attachTime < tuning.groundAttachGrace;
        /// <summary>부착 후 바닥에서 떨어진 적이 있는가. 공중에서 부착했으면 처음부터 true. false인 동안은 바닥 접촉으로 해제하지 않는다.</summary>
        public bool LeftGroundSinceAttach => _leftGroundSinceAttach;
        /// <summary>플레이어(물리 위치) ~ 부착 지점 거리 (XY)</summary>
        public float DistanceToAttachPoint
        {
            get
            {
                if (_rb == null) return 0f;
                Vector3 d = _rb.position - _attachPoint; d.z = 0f;
                return d.magnitude;
            }
        }

        /// <summary>지금 리스폰 위치</summary>
        public Vector3 RespawnPosition => _respawnPosition;

        /// <summary>부착했을 때</summary>
        public event Action Attached;
        /// <summary>해제했을 때 (점프 해제였는가). 접촉 해제·낙사로 풀릴 때도 false로 온다.</summary>
        public event Action<bool> Released;
        /// <summary>부착 중 접촉으로 해제됐을 때 (기절했는가)</summary>
        public event Action<bool> ContactReleased;
        /// <summary>공중에서 벽/천장에 부딪혀 기절했을 때</summary>
        public event Action AirStunned;
        /// <summary>낙사했을 때 (리스폰 직전)</summary>
        public event Action Died;
        /// <summary>리스폰 지점으로 옮겨졌을 때 (위치). 카메라가 이걸 듣고 바로 따라간다.</summary>
        public event Action<Vector3> Respawned;


        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _col = GetComponent<CapsuleCollider>();

            _rb.useGravity = true;
            _rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.sleepThreshold = 0f; // 잠들면 OnCollisionStay가 안 와서 바닥 판정이 끊긴다

            if (noFrictionMaterial == null)
            {
                noFrictionMaterial = new PhysicsMaterial("PlayerNoFriction (runtime)")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }
            _col.sharedMaterial = noFrictionMaterial;

            if (respawnPoint == null)
            {
                var go = GameObject.Find("RespawnPoint");
                if (go != null) respawnPoint = go.transform;
            }
            _respawnPosition = respawnPoint != null ? respawnPoint.position : transform.position;

            SetState(_state, force: true);
        }

        void Update()
        {
            if (tuning == null) return;

            // 기절 / 낙사 중: 이동·점프 입력 무시. 버퍼도 비워서 착지 순간 점프가 튀어나오지 않게.
            if (_state == PlayerState.Stunned || _state == PlayerState.Dead || _controlLocked)
            {
                _moveInput = 0f;
                _jumpBufferTimer = 0f;
                return;
            }

            _moveInput = GameInput.MoveX;

            if (GameInput.JumpPressed)
            {
                if (_state == PlayerState.Attached)
                {
                    // 부착 중 Space = 해제 + 점프 힘. 일반 점프 버퍼에는 넣지 않는다.
                    RequestRelease(withJump: true);
                    _jumpBufferTimer = 0f;
                }
                else
                {
                    _jumpBufferTimer = tuning.jumpBufferTime;
                }
            }
            else if (_jumpBufferTimer > 0f)
                _jumpBufferTimer -= Time.deltaTime;
        }

        void FixedUpdate()
        {
            if (tuning == null) return;
            float dt = Time.fixedDeltaTime;

            // 0) 낙사 높이
            if (_rb.position.y < tuning.killY)
            {
                Kill();
                return;
            }

            bool groundContactRaw = _groundContactThisStep; // 점프 직후 무시 타이머와 상관없는 실제 바닥 접촉
            // 1) 지난 물리 스텝의 접촉으로 바닥 판정 확정
            if (_groundIgnoreTimer > 0f)
            {
                _groundIgnoreTimer -= dt;
                _isGrounded = false;
            }
            else
            {
                _isGrounded = _groundContactThisStep;
            }
            _groundNormal = _isGrounded ? _groundNormalThisStep : Vector3.up;
            _groundContactThisStep = false;
            _groundNormalThisStep = Vector3.up;

            if (_isGrounded) _coyoteTimer = tuning.coyoteTime;
            else if (_coyoteTimer > 0f) _coyoteTimer -= dt;

            // 1a) 부착 중 다른 콜라이더 접촉 → 즉시 해제. 벽/천장이 우선(기절).
            bool hitWall = _attachHitWall, hitGround = _attachHitGround;
            _attachHitWall = false;
            _attachHitGround = false;
            if (_state == PlayerState.Attached && (hitWall || hitGround))
                DoContactRelease(stun: hitWall);
            // 1a') 공중에서 벽/천장에 부딪힘 → 기절. 이번 스텝에 바닥 접촉이 있었으면(착지 · 모서리) 기절하지 않는다.
            bool airHit = _airHitWall;
            _airHitWall = false;
            _preStepValid = false;
            if (airHit && !groundContactRaw && IsAirStunState(_state))
                DoAirStun();
            bool stepStartAirborne = !groundContactRaw && !_isGrounded;


            // 1b) 해제 요청 (클릭 / Space) 처리
            if (_releaseRequested)
            {
                bool jump = _releaseWithJump;
                _releaseRequested = false;
                _releaseWithJump = false;
                if (_state == PlayerState.Attached)
                    DoRelease(jump);
            }

            Vector3 v = _rb.linearVelocity;

            // 기절: 입력 없음. 속도 유지 + 중력. 바닥에 착지하는 순간에만 Normal로 (아래 일반 처리로 넘어간다).
            if (_state == PlayerState.Stunned)
            {
                if (_isGrounded)
                {
                    _stuckTimer = 0f;
                    SetState(PlayerState.Normal);
                }
                else
                {
                    if (tuning.gravityMultiplier != 1f)
                        v += Physics.gravity * ((tuning.gravityMultiplier - 1f) * dt);
                    if (v.y < -tuning.maxFallSpeed)
                        v.y = -tuning.maxFallSpeed;

                    // 끼임 방지: 바닥이 아닌데 거의 멈춘 채 머물면 벽 법선 + 아래로 밀어 빼낸다.
                    Vector2 v2 = new Vector2(v.x, v.y);
                    if (v2.magnitude < tuning.stuckSpeedThreshold)
                    {
                        _stuckTimer += dt;
                        if (_stuckTimer >= tuning.stuckNudgeTime)
                        {
                            Vector3 wn = _lastWallNormal; wn.z = 0f;
                            Vector3 dir = (wn.sqrMagnitude > 1e-4f ? wn.normalized : Vector3.zero) + Vector3.down * 0.5f;
                            v = dir.normalized * tuning.stuckNudgeSpeed;
                            _stuckTimer = 0f;
                        }
                    }
                    else _stuckTimer = 0f;

                    v.z = 0f;
                    _rb.linearVelocity = v;
                    return;
                }
            }

            // 부착 중: 지상/공중 이동·점프 코드를 쓰지 않는다. 중력은 그대로, A/D는 줄의 접선 방향 힘.
            if (_state == PlayerState.Attached)
            {
                if (!_isGrounded) _leftGroundSinceAttach = true;

                if (!_isGrounded && tuning.gravityMultiplier != 1f)
                    v += Physics.gravity * ((tuning.gravityMultiplier - 1f) * dt);
                if (v.y < -tuning.maxFallSpeed)
                    v.y = -tuning.maxFallSpeed;
                v.z = 0f;

                ApplyRope(ref v, dt);

                v.z = 0f;
                _rb.linearVelocity = v;
                return;
            }

            // 2) 좌우 이동
            float target = _moveInput * tuning.moveSpeed;
            if (_isGrounded)
            {
                v.x = Mathf.MoveTowards(v.x, target, tuning.groundAccel * dt);
            }
            else if (Mathf.Abs(_moveInput) > 0.01f)
            {
                // 공중: 입력이 있을 때만 약하게. 같은 방향으로 이미 더 빠르면 관성을 깎지 않는다.
                bool sameDirFaster = Mathf.Sign(v.x) == Mathf.Sign(target) && Mathf.Abs(v.x) >= Mathf.Abs(target);
                if (!sameDirFaster)
                    v.x = Mathf.MoveTowards(v.x, target, tuning.airAccel * dt);
            }

            // 3) 점프 (버퍼 + 코요테)
            if (_jumpBufferTimer > 0f && (_isGrounded || _coyoteTimer > 0f))
            {
                v.y = tuning.jumpVelocity;
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
                _groundIgnoreTimer = tuning.groundIgnoreAfterJump;
                _isGrounded = false;
            }

            // 4) 추가 중력 + 낙하 속도 제한
            if (!_isGrounded && tuning.gravityMultiplier != 1f)
                v += Physics.gravity * ((tuning.gravityMultiplier - 1f) * dt);
            if (v.y < -tuning.maxFallSpeed)
                v.y = -tuning.maxFallSpeed;

            v.z = 0f;
            _rb.linearVelocity = v;

            // 다음 충돌 판정용: 이 속도에 물리 엔진이 기본 중력을 한 스텝 더한 것이 충돌 직전 속도다.
            _preStepVelocity = v + (_rb.useGravity ? Physics.gravity * dt : Vector3.zero);
            _preStepAirborne = stepStartAirborne;
            _preStepValid = true;

            // 5) 상태. Normal/Falling은 바닥 여부로 자동 전환, Flying은 착지하면 Normal.
            //    Attached·Stunned는 위에서 처리, Dead는 Kill 안에서 바로 Normal로 돌아온다.
            //    리스폰 직후 한두 스텝은 접촉이 아직 안 와서 Falling으로 튀지 않게 건너뛴다.
            if ((_state == PlayerState.Normal || _state == PlayerState.Falling) && !(JustRespawned && !_isGrounded))
                SetState(_isGrounded ? PlayerState.Normal : PlayerState.Falling);
            else if (_state == PlayerState.Flying && _isGrounded)
                SetState(PlayerState.Normal);
        }

        void OnCollisionEnter(Collision c) => EvaluateContacts(c);
        void OnCollisionStay(Collision c) => EvaluateContacts(c);

        void EvaluateContacts(Collision c)
        {
            if (tuning == null) return;
            float minUp = Mathf.Cos(tuning.maxGroundSlope * Mathf.Deg2Rad);

            // 부착 중 해제 판정: 트리거는 제외, 바닥 부착 유예 중이면 전부 무시.
            // 부착 면 예외 없음 (2026-11-13 사용자 결정, 기획서 4장 표 첫 줄과 다름):
            // 부착한 콜라이더 자신이어도 벽/천장 법선이면 기절, 바닥 법선(윗면)이면 해제만.
            bool attachCheck = _state == PlayerState.Attached
                               && c.collider != null && !c.collider.isTrigger
                               && !InGroundAttachGrace;
            // 바닥에서 부착했고 아직 한 번도 바닥에서 떨어지지 않았으면 바닥 접촉은 무시
            bool ignoreGround = _attachedFromGround && !_leftGroundSinceAttach;
            // 공중 충돌 기절 판정: 지난 스텝을 공중에서 시작했고 지금 Normal/Flying/Falling일 때만.
            bool airCheck = _preStepValid && _preStepAirborne && IsAirStunState(_state)
                            && c.collider != null && !c.collider.isTrigger;

            int n = c.contactCount;
            for (int i = 0; i < n; i++)
            {
                Vector3 normal = c.GetContact(i).normal; // 플레이어 쪽을 향한 면 법선
                if (normal.y >= minUp)
                {
                    if (!_groundContactThisStep || normal.y > _groundNormalThisStep.y)
                        _groundNormalThisStep = normal;
                    _groundContactThisStep = true;
                    if (attachCheck && !ignoreGround) _attachHitGround = true;
                }
                else
                {
                    // 벽/천장. Attached면 해제 + 기절. 공중(Normal·Flying·Falling)이면 면 안쪽 속도가
                    // airStunMinImpactSpeed를 넘을 때만 기절(스치기 무시). 바닥에서 시작한 스텝·Stunned에서는 무해.
                    _lastWallNormal = normal;
                    if (attachCheck) _attachHitWall = true;
                    if (airCheck && -Vector3.Dot(_preStepVelocity, normal) > tuning.airStunMinImpactSpeed)
                        _airHitWall = true;
                }
            }
        }

        /// <summary>
        /// 부착. 줄 길이 = 지금 플레이어 위치 ~ point 거리로 고정.
        /// 바닥에 선 채면 거리 − groundAttachLift (최소 0.5m)로 짧게 해 살짝 떠오른다.
        /// Attached/Stunned/Dead 중에는 못 한다.
        /// </summary>
        public bool TryAttach(Vector3 point, Vector3 normal, Collider collider)
        {
            if (tuning == null || _rb == null) return false;
            if (_state == PlayerState.Attached || _state == PlayerState.Stunned || _state == PlayerState.Dead) return false;

            Vector3 d = _rb.position - point; d.z = 0f;
            float dist = d.magnitude;

            _attachPoint = point;
            _attachNormal = normal.sqrMagnitude > 0f ? normal.normalized : Vector3.up;
            _attachedCollider = collider;
            _attachedFromGround = _isGrounded;
            _leftGroundSinceAttach = !_isGrounded;
            _attachTime = Time.time;
            _reelFromLength = Mathf.Max(0.1f, dist);
            _ropeLength = _attachedFromGround
                ? Mathf.Max(0.5f, dist - tuning.groundAttachLift)
                : _reelFromLength;
            _releaseRequested = false;
            _releaseWithJump = false;
            _jumpBufferTimer = 0f;
            _attachHitWall = false;
            _airHitWall = false;
            _attachHitGround = false;

            SetState(PlayerState.Attached);
            Attached?.Invoke();
            return true;
        }

        /// <summary>해제 요청. 다음 FixedUpdate에서 처리한다 (속도 × releaseInertiaMultiplier, withJump면 위로 jumpVelocity 추가).</summary>
        public void RequestRelease(bool withJump = false)
        {
            if (_state != PlayerState.Attached) return;
            _releaseRequested = true;
            _releaseWithJump |= withJump;
        }

        void DoRelease(bool withJump)
        {
            Vector3 v = _rb.linearVelocity * tuning.releaseInertiaMultiplier;
            if (withJump)
            {
                v.y += tuning.jumpVelocity;
                _groundIgnoreTimer = tuning.groundIgnoreAfterJump;
                _isGrounded = false;
            }
            v.z = 0f;
            _rb.linearVelocity = v;
            _coyoteTimer = 0f;
            _jumpBufferTimer = 0f;
            _attachedCollider = null;
            SetState(PlayerState.Flying);
            Released?.Invoke(withJump);
        }

        /// <summary>
        /// 부착 중 접촉 해제. 속도는 건드리지 않는다(충돌 뒤 속도 그대로).
        /// stun이면 Stunned, 아니면(바닥) 이미 착지했으니 Normal.
        /// </summary>
        void DoContactRelease(bool stun)
        {
            _releaseRequested = false;
            _releaseWithJump = false;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _attachedCollider = null;
            _stuckTimer = 0f;
            if (stun) SetState(PlayerState.Stunned);
            else SetState(_isGrounded ? PlayerState.Normal : PlayerState.Falling);
            Released?.Invoke(false);
            ContactReleased?.Invoke(stun);
        }

        static bool IsAirStunState(PlayerState s) =>
            s == PlayerState.Normal || s == PlayerState.Flying || s == PlayerState.Falling;

        /// <summary>공중에서 벽/천장에 부딪혀 기절. 속도는 충돌 뒤 그대로 두고 떨어진다. 착지해야 풀린다.</summary>
        void DoAirStun()
        {
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _stuckTimer = 0f;
            SetState(PlayerState.Stunned);
            AirStunned?.Invoke();
        }

        // ---- 조작 잠금 (골인 등) ----
        bool _controlLocked;
        /// <summary>조작 잠금 중인가 (골인 뒤). 이동·점프·발사 입력을 무시한다. 물리·이동 규칙은 그대로.</summary>
        public bool ControlLocked => _controlLocked;

        /// <summary>
        /// 조작 잠금 (골인한 RunManager가 부른다). 잠그면 A/D·Space·클릭 입력을 받지 않고, 매달려 있으면 놓는다.
        /// 이동 수치·물리는 건드리지 않는다. 풀면 다시 입력을 받는다.
        /// </summary>
        public void SetControlLocked(bool locked)
        {
            _controlLocked = locked;
            if (!locked) return;
            _moveInput = 0f;
            _jumpBufferTimer = 0f;
            RequestRelease(withJump: false);
        }

        /// <summary>리스폰 지점을 바꾼다 (체크포인트가 부른다). z는 0으로 맞춘다.</summary>
        public void SetRespawnPoint(Vector3 position)
        {
            position.z = 0f;
            _respawnPosition = position;
        }

        /// <summary>
        /// 개발용 순간이동 (에디터 창 Pop/Debug/Teleport가 부른다). 게임 진행에는 쓰지 않는다.
        /// 줄 해제 · 기절 해제 · 속도 0 · Z는 지금 값 유지 → Normal (착지 전이면 곧 Falling). 이동·튜닝·기절 규칙은 건드리지 않는다.
        /// </summary>
        public void DebugTeleport(Vector3 position)
        {
            if (_rb == null) return;
            bool wasAttached = _state == PlayerState.Attached;

            position.z = _rb.position.z; // Z 고정 평면 유지
            _attachedCollider = null;
            _releaseRequested = false;
            _releaseWithJump = false;
            _attachHitWall = false;
            _attachHitGround = false;
            _airHitWall = false;
            _preStepValid = false;

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = position;
            transform.position = position;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _groundIgnoreTimer = 0f;
            _stuckTimer = 0f;
            _isGrounded = false;
            _groundContactThisStep = false;
            _groundNormalThisStep = Vector3.up;
            _lastWallNormal = Vector3.zero;

            _respawnFixedTime = Time.fixedTime; // 리스폰과 같이: 한두 스텝 Falling 전환 · 재낙사 무시
            SetState(PlayerState.Normal);
            if (wasAttached) Released?.Invoke(false);
        }


        /// <summary>
        /// 낙사. 줄 해제 · 기절 해제 · 속도 0 → 즉시 리스폰 지점으로 → Normal.
        /// KillZone 트리거나 killY가 부른다. Dead 중 다시 불려도 무시.
        /// </summary>
        public void Kill()
        {
            // 같은 물리 스텝의 트리거 콜백(Enter/Stay)이 리스폰 뒤 또 부르는 것을 막는다.
            if (_rb == null || _state == PlayerState.Dead || JustRespawned) return;
            bool wasAttached = _state == PlayerState.Attached;

            SetState(PlayerState.Dead);
            _attachedCollider = null;
            _releaseRequested = false;
            _releaseWithJump = false;
            _attachHitWall = false;
            _attachHitGround = false;
            _airHitWall = false;
            _preStepValid = false;
            if (wasAttached) Released?.Invoke(false);
            Died?.Invoke();

            // 리스폰
            Vector3 p = _respawnPosition; p.z = 0f;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = p;          // 보간 없이 순간이동
            transform.position = p;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _groundIgnoreTimer = 0f;
            _stuckTimer = 0f;
            _isGrounded = false;
            _groundContactThisStep = false;
            _groundNormalThisStep = Vector3.up;
            _lastWallNormal = Vector3.zero;

            _respawnFixedTime = Time.fixedTime;
            SetState(PlayerState.Normal);
            Respawned?.Invoke(p);
        }

        /// <summary>
        /// 늘어나지 않는 로프(거리 ≤ 길이). 위치 기반 제약을 속도로 푼다:
        /// 이번 스텝 뒤 예상 위치(중력 포함)가 원 밖이면 원 위로 끌어온 점에 닿는 속도를 준다.
        /// 그래서 텔레포트 없이 원호를 따라 움직이고(보간 유지, 떨림 없음), 줄이 느슨하면 그냥 자유낙하.
        /// 충돌 등으로 ropeSnapTolerance보다 더 벌어지면 위치를 원 위로 되돌리고 바깥 반지름 속도를 없앤다.
        /// </summary>
        void ApplyRope(ref Vector3 v, float dt)
        {
            Vector3 a = _attachPoint; a.z = 0f;
            Vector3 p = _rb.position;
            float pz = p.z;
            p.z = 0f;
            float L = EffectiveRopeLength;

            Vector3 d = p - a;
            float dist = d.magnitude;
            if (dist < 1e-4f) return;
            Vector3 n = d / dist; // 부착 지점 → 플레이어

            // A/D: 줄에 수직(접선) 방향. D가 항상 오른쪽(+x) 성분을 갖도록 부호를 맞춘다.
            if (Mathf.Abs(_moveInput) > 0.01f)
            {
                Vector3 t = new Vector3(-n.y, n.x, 0f);
                if (t.x < 0f) t = -t;
                v += t * (_moveInput * tuning.swingForce * dt);
            }

            // 크게 벌어졌으면 위치 보정 + 바깥 반지름 속도 제거
            if (dist > L + ropeSnapTolerance)
            {
                p = a + n * L;
                _rb.position = new Vector3(p.x, p.y, pz);
                float outward = Vector3.Dot(v, n);
                if (outward > 0f) v -= n * outward;
            }

            // 예측 제약: 물리 엔진이 이번 스텝에 더할 기본 중력까지 포함해 다음 위치를 본다.
            Vector3 g = _rb.useGravity ? Physics.gravity : Vector3.zero;
            g.z = 0f;
            Vector3 pNext = p + (v + g * dt) * dt;
            Vector3 dn = pNext - a;
            float distNext = dn.magnitude;
            if (distNext > L && distNext > 1e-4f)
            {
                Vector3 onCircle = a + dn * (L / distNext);
                v = (onCircle - p) / dt - g * dt;
            }
        }

        /// <summary>상태 변경. 모든 전환이 StateChanged로 나간다.</summary>
        public void SetState(PlayerState next, bool force = false)
        {
            if (!force && next == _state) return;
            var prev = _state;
            _state = next;
            if (prev != next) StateChanged?.Invoke(prev, next);
        }
    }
}
