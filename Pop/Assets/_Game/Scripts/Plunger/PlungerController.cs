using System;
using Pop.Input;
using Pop.Player;
using UnityEngine;

namespace Pop.Plunger
{
    /// <summary>
    /// 뚫어뻥 조준·발사·보이는 것(머리, 줄, 조준 커서). 줄 물리는 PlayerController가 한다.
    /// 조준: 카메라에서 커서로 쏜 레이와 플레이 평면(z=0)의 교점 = 커서 월드 위치.
    /// 발사: 플레이어 → 커서 방향 Raycast(plungerRange). 처음 맞은 면에 PlungerAttachable이 있으면 부착,
    /// 없으면(부착 불가 면 / 아무것도 없음 / 사거리 밖) 실패 + fireFailCooldown (그동안 클릭 무시).
    /// 부착 중 클릭 = 해제.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public class PlungerController : MonoBehaviour
    {
        public enum AimKind
        {
            /// <summary>사거리 안에 아무것도 없음 (회색)</summary>
            None,
            /// <summary>부착 가능 면 (초록)</summary>
            Attachable,
            /// <summary>부착 불가 면 (빨강)</summary>
            Blocked
        }

        [Tooltip("조준에 쓸 카메라. 비우면 Camera.main")]
        public Camera aimCamera;
        [Tooltip("뚫어뻥 레이가 맞는 레이어")]
        public LayerMask hitMask = ~0;

        [Header("보이는 것")]
        public Material headMaterial;
        public Material ropeMaterial;
        public float ropeWidth = 0.05f;
        [Tooltip("조준 커서 크기 (px)")]
        public float cursorSize = 26f;
        public Color colorAttachable = new Color(0.25f, 1f, 0.35f, 1f);
        public Color colorBlocked = new Color(1f, 0.25f, 0.2f, 1f);
        public Color colorNone = new Color(0.65f, 0.65f, 0.65f, 0.9f);
        [Tooltip("Play 중 시스템 커서 숨김 (꺼지면 다시 보임)")]
        public bool hideSystemCursor = true;

        [Header("발사 연출 (보이는 것만, 판정·물리와 무관)")]
        [Tooltip("뚫어뻥 머리가 날아가는 속도 (m/s)")]
        public float shotSpeed = 45f;
        [Tooltip("발사 성공(부착) 때 속도 배율. 부착은 즉시 처리되고 머리만 따라간다")]
        public float attachShotSpeedMultiplier = 2f;
        [Tooltip("부착 불가 면에서 튕겨 나오는 속도 (m/s)")]
        public float bounceSpeed = 6f;
        [Tooltip("튕길 때 아래로 더하는 속도 (m/s)")]
        public float bounceDownSpeed = 2f;
        [Tooltip("튕긴 뒤 떨어지다 사라지기까지 (s)")]
        public float bounceTime = 0.25f;
        [Tooltip("튕긴 머리에 거는 중력 배율")]
        public float bounceGravityScale = 2f;
        [Tooltip("튕긴 머리 회전 속도 (도/s)")]
        public float bounceSpinSpeed = 900f;
        [Tooltip("사거리 끝에서 멈칫하는 시간 (s)")]
        public float retractPause = 0.06f;
        [Tooltip("사거리 끝에서 플레이어로 되감기는 속도 (m/s)")]
        public float retractSpeed = 90f;

        public enum ShotPhase { None, Out, Bounce, Pause, Retract }

        ShotPhase _shot;
        bool _shotForAttach;
        bool _shotBlocked;
        Vector3 _shotOrigin, _shotDir, _shotNormal, _shotPos, _bounceVel;
        float _shotDist, _shotTraveled, _shotTimer;
        Quaternion _shotRot = Quaternion.identity;

        Vector3 _aimOrigin, _aimDir;
        bool _aimDirValid;

        /// <summary>지금 발사 연출 단계</summary>
        public ShotPhase Shot => _shot;
        /// <summary>발사 연출 중 머리 위치</summary>
        public Vector3 ShotHeadPosition => _shotPos;

        
PlayerController _player;
        Rigidbody _rb;
        Collider[] _ownColliders;
        readonly RaycastHit[] _hits = new RaycastHit[32];

        float _cooldown;
        float _failBlink;
        RaycastHit _aimHit;

        LineRenderer _line;
        GameObject _head;
        Texture2D _ringTex;

        public AimKind Aim { get; private set; }
        /// <summary>조준 지점: 맞은 면이 있으면 맞은 점, 없으면 커서 월드 위치(z=0)</summary>
        public Vector3 AimPoint { get; private set; }
        /// <summary>커서 월드 위치 (z=0 평면)</summary>
        public Vector3 CursorWorld { get; private set; }
        /// <summary>남은 발사 실패 쿨타임 (s)</summary>
        public float CooldownRemaining => Mathf.Max(0f, _cooldown);
        public PlayerController Player => _player;

        /// <summary>발사 실패 (부착 불가 면 / 아무것도 없음)</summary>
        public event Action FireFailed;

        PlayerTuning Tuning => _player != null ? _player.tuning : null;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _rb = GetComponent<Rigidbody>();
            _ownColliders = GetComponentsInChildren<Collider>(true);
            BuildVisuals();
            _ringTex = MakeRingTexture(64, 0.38f, 0.5f);
        }

        void OnEnable()
        {
            if (hideSystemCursor) Cursor.visible = false;
        }

void OnDisable()
        {
            Cursor.visible = true;
            ClearShot();
            HideAttachVisuals();
        }

        void OnDestroy()
        {
            if (_head != null) Destroy(_head);
            if (_line != null) Destroy(_line.gameObject);
            if (_ringTex != null) Destroy(_ringTex);
        }

void Update()
        {
            var tuning = Tuning;
            if (tuning == null) return;

            float dt = Time.deltaTime;
            if (_cooldown > 0f) _cooldown -= dt;
            if (_failBlink > 0f) _failBlink -= dt;

            UpdateAim(tuning);

            // 기절 / 낙사 중에는 발사 입력 무시 (실패 처리도 하지 않아 쿨타임이 생기지 않는다)
            var st = _player.State;
            if (st == PlayerState.Stunned || st == PlayerState.Dead) return;

            // 조작 잠금(골인 뒤 클리어 화면): 발사 안 함, 시스템 커서를 보여 UI 버튼을 누를 수 있게
            if (_player.ControlLocked) { Cursor.visible = true; return; }

            if (!GameInput.FirePressed) return;

            if (_player.State == PlayerState.Attached)
            {
                _player.RequestRelease(withJump: false);
                return;
            }
            if (_cooldown > 0f) return; // 쿨타임 중 클릭 무시

            Fire(tuning);
        }

        /// <summary>
        /// 지금 조준(UpdateAim 결과)대로 발사. 클릭과 같은 규칙: 부착 가능 면이면 즉시 부착, 아니면 실패 + 쿨타임.
        /// 어느 쪽이든 머리가 날아가는 연출을 시작한다. 부착했으면 true.
        /// </summary>
        bool Fire(PlayerTuning tuning)
        {
            if (Aim == AimKind.Attachable && _player.TryAttach(_aimHit.point, _aimHit.normal, _aimHit.collider))
            {
                StartShot(attach: true);
                return true;
            }
            FailFire(tuning);
            StartShot(attach: false);
            return false;
        }

        /// <summary>
        /// 테스트·스크립트용: 월드 지점 쪽으로 발사 (커서가 거기 있는 것처럼). 쿨타임·상태 규칙은 클릭과 같다.
        /// </summary>
        public bool FireToward(Vector3 worldTarget)
        {
            var tuning = Tuning;
            if (tuning == null) return false;
            var st = _player.State;
            if (st == PlayerState.Stunned || st == PlayerState.Dead || st == PlayerState.Attached || _cooldown > 0f) return false;
            worldTarget.z = 0f;
            Probe(tuning, worldTarget);
            return Fire(tuning);
        }

        void FailFire(PlayerTuning tuning)
        {
            _cooldown = tuning.fireFailCooldown;
            _failBlink = tuning.fireFailCooldown;
            FireFailed?.Invoke();
        }

        /// <summary>
        /// 조준 갱신. 커서 월드 위치를 구하고 플레이어 → 커서 방향으로 사거리만큼 레이를 쏜다.
        /// 플레이어 자신의 콜라이더는 건너뛰고, 가장 가까운 맞은 면으로 판정한다.
        /// </summary>
void UpdateAim(PlayerTuning tuning)
        {
            Aim = AimKind.None;
            _aimDirValid = false;
            var cam = aimCamera != null ? aimCamera : Camera.main;
            if (cam == null || !GameInput.HasPointer) return;

            Ray r = cam.ScreenPointToRay(GameInput.PointerScreenPosition);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            if (!plane.Raycast(r, out float enter)) return;
            Vector3 cursor = r.GetPoint(enter);
            cursor.z = 0f;
            CursorWorld = cursor;
            Probe(tuning, cursor);
        }

        /// <summary>플레이어 → target 방향으로 사거리만큼 레이. Aim, AimPoint, _aimHit, _aimOrigin/_aimDir를 채운다.</summary>
        void Probe(PlayerTuning tuning, Vector3 target)
        {
            Aim = AimKind.None;
            _aimDirValid = false;
            AimPoint = target;

            Vector3 origin = _rb.position;
            origin.z = 0f;
            Vector3 dir = target - origin;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            _aimOrigin = origin;
            _aimDir = dir;
            _aimDirValid = true;

            int n = Physics.RaycastNonAlloc(origin, dir, _hits, tuning.plungerRange, hitMask, QueryTriggerInteraction.Ignore);
            int best = -1;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || IsOwn(h.collider)) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = i;
                }
            }
            if (best < 0) return;

            _aimHit = _hits[best];
            AimPoint = _aimHit.point;
            var att = _aimHit.collider.GetComponentInParent<PlungerAttachable>();
            Aim = att != null && att.enabled ? AimKind.Attachable : AimKind.Blocked;
        }

        bool IsOwn(Collider c)
        {
            for (int i = 0; i < _ownColliders.Length; i++)
                if (_ownColliders[i] == c) return true;
            return false;
        }

void LateUpdate()
        {
            if (_player == null) return;

            var st = _player.State;
            if (st == PlayerState.Stunned || st == PlayerState.Dead) ClearShot();

            if (_shot != ShotPhase.None && UpdateShot(Time.deltaTime))
            {
                ShowHeadAndRope(_shotPos, _shotRot);
                return;
            }

            if (_player.IsAttached)
            {
                ShowHeadAndRope(_player.AttachPoint, Quaternion.FromToRotation(Vector3.up, _player.AttachNormal));
            }
            else
            {
                HideAttachVisuals();
            }
        }

        void ShowHeadAndRope(Vector3 headPos, Quaternion headRot)
        {
            _head.transform.SetPositionAndRotation(headPos, headRot);
            if (!_head.activeSelf) _head.SetActive(true);
            _line.positionCount = 2;
            _line.SetPosition(0, transform.position);
            _line.SetPosition(1, headPos);
            _line.enabled = true;
        }

        // ---- 발사 연출 ----

        void StartShot(bool attach)
        {
            ClearShot();
            var tuning = Tuning;
            if (tuning == null || !_aimDirValid) return;

            _shotForAttach = attach;
            _shotOrigin = transform.position;
            _shotOrigin.z = 0f;
            Vector3 end;
            if (attach)
            {
                end = _player.AttachPoint;
                _shotBlocked = false;
            }
            else if (Aim == AimKind.Blocked)
            {
                end = _aimHit.point;
                _shotNormal = _aimHit.normal;
                _shotBlocked = true;
            }
            else
            {
                end = _aimOrigin + _aimDir * tuning.plungerRange;
                _shotBlocked = false;
            }
            Vector3 d = end - _shotOrigin;
            _shotDist = d.magnitude;
            _shotDir = _shotDist > 1e-4f ? d / _shotDist : _aimDir;
            _shotTraveled = 0f;
            _shotPos = _shotOrigin;
            _shotRot = Quaternion.FromToRotation(Vector3.up, -_shotDir); // 컵이 앞, 손잡이가 뒤
            _shot = ShotPhase.Out;
        }

        void ClearShot()
        {
            _shot = ShotPhase.None;
        }

        /// <summary>연출을 한 프레임 진행. 계속 보여야 하면 true, 끝났으면 false.</summary>
        bool UpdateShot(float dt)
        {
            switch (_shot)
            {
                case ShotPhase.Out:
                {
                    if (_shotForAttach && !_player.IsAttached) { ClearShot(); return false; } // 날아가는 도중 해제
                    float speed = shotSpeed * (_shotForAttach ? attachShotSpeedMultiplier : 1f);
                    _shotTraveled = Mathf.Min(_shotDist, _shotTraveled + Mathf.Max(0.01f, speed) * dt);
                    _shotPos = _shotOrigin + _shotDir * _shotTraveled;
                    if (_shotTraveled >= _shotDist)
                    {
                        if (_shotForAttach)
                        {
                            ClearShot(); // 꽂힘: 이제부터는 부착 표시가 이어받는다
                            return false;
                        }
                        if (_shotBlocked)
                        {
                            Vector3 refl = Vector3.Reflect(_shotDir, _shotNormal);
                            refl.z = 0f;
                            _bounceVel = refl.normalized * bounceSpeed + Vector3.down * bounceDownSpeed;
                            _shotPos += _shotNormal * 0.05f;
                            _shotTimer = bounceTime;
                            _shot = ShotPhase.Bounce;
                        }
                        else
                        {
                            _shotTimer = retractPause;
                            _shot = ShotPhase.Pause;
                        }
                    }
                    return true;
                }
                case ShotPhase.Bounce:
                {
                    _shotTimer -= dt;
                    if (_shotTimer <= 0f) { ClearShot(); return false; }
                    _bounceVel += Physics.gravity * (bounceGravityScale * dt);
                    _bounceVel.z = 0f;
                    _shotPos += _bounceVel * dt;
                    _shotRot = Quaternion.AngleAxis(bounceSpinSpeed * dt, Vector3.forward) * _shotRot;
                    return true;
                }
                case ShotPhase.Pause:
                {
                    _shotTimer -= dt;
                    if (_shotTimer <= 0f) _shot = ShotPhase.Retract;
                    return true;
                }
                case ShotPhase.Retract:
                {
                    Vector3 target = transform.position;
                    _shotPos = Vector3.MoveTowards(_shotPos, target, Mathf.Max(0.01f, retractSpeed) * dt);
                    Vector3 back = target - _shotPos;
                    if (back.sqrMagnitude < 0.3f * 0.3f) { ClearShot(); return false; }
                    _shotRot = Quaternion.FromToRotation(Vector3.up, back.normalized); // 손잡이가 플레이어 쪽
                    return true;
                }
            }
            return false;
        }

        void HideAttachVisuals()
        {
            if (_head != null && _head.activeSelf) _head.SetActive(false);
            if (_line != null && (_line.enabled || _line.positionCount > 0))
            {
                _line.positionCount = 0;
                _line.enabled = false;
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _ringTex == null || !GameInput.HasPointer) return;
            if (_player != null && _player.ControlLocked) return; // 클리어 화면에서는 조준 커서 안 그림

            Vector2 sp = GameInput.PointerScreenPosition;
            float x = sp.x;
            float y = Screen.height - sp.y;
            float s = cursorSize;

            Color c = Aim == AimKind.Attachable ? colorAttachable : Aim == AimKind.Blocked ? colorBlocked : colorNone;
            if (_failBlink > 0f && Mathf.Repeat(_failBlink, 0.1f) < 0.05f) c.a *= 0.2f;

            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - s * 0.5f, y - s * 0.5f, s, s), _ringTex);
            float arm = s * 0.3f;
            GUI.DrawTexture(new Rect(x - arm, y - 1f, arm * 2f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1f, y - arm, 2f, arm * 2f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        // ---- 임시 비주얼 ----

        void BuildVisuals()
        {
            // 머리: 부착 면 법선(+Y) 방향으로 선 빨간 컵 + 손잡이. 콜라이더 없음.
            _head = new GameObject("PlungerHead (runtime)");
            var headMat = headMaterial != null ? headMaterial : MakeFallbackMaterial(new Color(0.85f, 0.1f, 0.1f), unlit: false);

            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.name = "Cup";
            Destroy(cup.GetComponent<Collider>());
            cup.transform.SetParent(_head.transform, false);
            cup.transform.localScale = new Vector3(0.42f, 0.07f, 0.42f);
            cup.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            cup.GetComponent<MeshRenderer>().sharedMaterial = headMat;

            var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stick.name = "Stick";
            Destroy(stick.GetComponent<Collider>());
            stick.transform.SetParent(_head.transform, false);
            stick.transform.localScale = new Vector3(0.08f, 0.18f, 0.08f);
            stick.transform.localPosition = new Vector3(0f, 0.14f + 0.18f, 0f);
            stick.GetComponent<MeshRenderer>().sharedMaterial = headMat;

            _head.SetActive(false);

            // 줄
            var ropeGo = new GameObject("PlungerRope (runtime)");
            _line = ropeGo.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 0;
            _line.startWidth = ropeWidth;
            _line.endWidth = ropeWidth;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sharedMaterial = ropeMaterial != null ? ropeMaterial : MakeFallbackMaterial(new Color(0.25f, 0.15f, 0.08f), unlit: true);
            _line.enabled = false;
        }

        static Material MakeFallbackMaterial(Color color, bool unlit)
        {
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = "Plunger (runtime)" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            m.color = color;
            return m;
        }

        static Texture2D MakeRingTexture(int size, float inner, float outer)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AimRing (runtime)" };
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float aa = 1.5f / size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / size;
                float dy = (y + 0.5f - half) / size;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((r - inner) / aa) * Mathf.Clamp01((outer - aa - r) / aa);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
