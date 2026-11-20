using System.Collections.Generic;
using System.Reflection;
using Pop.Player;
using Pop.Plunger;
using UnityEngine;

namespace Pop.Dev
{
    /// <summary>
    /// 개발용 자동 플레이어: MountainRoute의 단계를 실제 PlayerController / PlungerController로 플레이한다.
    /// A/D는 PlayerController의 입력 값(_moveInput)을, 점프는 점프 버퍼(_jumpBufferTimer)를 리플렉션으로 넣고,
    /// 발사는 PlungerController.FireToward, Space 해제는 PlayerController.RequestRelease(true)를 쓴다.
    /// 플레이어의 FixedUpdate보다 먼저 돌아야 해서 실행 순서를 앞당긴다. Play 중 AddComponent로 붙여 Run을 부른다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class RouteBot : MonoBehaviour
    {
        public MountainRoute route;
        public PlayerController player;
        public PlungerController plunger;
        public float stepTimeout = 12f;

        public bool running;
        public bool done;
        public bool success;
        public int current;
        public int endStep;
        public string failReason = "";
        public float elapsed;
        public float minPlayerY = float.MaxValue;
        public readonly List<string> log = new List<string>();

        FieldInfo _fMove, _fJump;
        int _phase;
        bool _wasRising;
        float _stepT;
        Vector3 _origRespawn;

        /// <summary>from 단계부터 to 단계 전까지 플레이. teleport면 from 단계(RunJump)의 standPos에 세운다.</summary>
        public void Run(int from, int to, bool teleport)
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (plunger == null) plunger = player.GetComponent<PlungerController>();
            if (route == null) route = FindAnyObjectByType<MountainRoute>();
            _fMove = typeof(PlayerController).GetField("_moveInput", BindingFlags.NonPublic | BindingFlags.Instance);
            _fJump = typeof(PlayerController).GetField("_jumpBufferTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            player.Died -= OnDied;
            player.StateChanged -= OnState;

            log.Clear();
            failReason = "";
            done = false;
            success = false;
            elapsed = 0f;
            minPlayerY = float.MaxValue;
            current = from;
            endStep = to < 0 || to > route.steps.Count ? route.steps.Count : to;
            _phase = 0;
            _stepT = 0f;

            if (teleport)
            {
                _origRespawn = player.RespawnPosition;
                player.SetRespawnPoint(route.steps[from].standPos);
                player.Kill(); // 리스폰 경로로 순간이동 (상태·속도 초기화)
                player.SetRespawnPoint(_origRespawn);
            }

            player.Died += OnDied;
            player.StateChanged += OnState;
            running = true;
            log.Add($"start step {from} at {player.Body.position}");
        }

        void OnDestroy()
        {
            if (player != null)
            {
                player.Died -= OnDied;
                player.StateChanged -= OnState;
            }
        }

        void OnDied()
        {
            if (running) Fail("died (KillZone / killY) at " + player.Body.position);
        }

        void OnState(PlayerState a, PlayerState b)
        {
            if (running && b == PlayerState.Stunned) Fail("stunned at " + player.Body.position);
        }

        void Fail(string why)
        {
            if (!running) return;
            running = false;
            done = true;
            success = false;
            failReason = $"step {current} ({Label()}) phase {_phase}: {why}";
            log.Add("FAIL " + failReason);
            if (_fMove != null) _fMove.SetValue(player, 0f);
        }

        string Label() => current < route.steps.Count ? route.steps[current].label : "-";

        void Next(string note)
        {
            log.Add($"ok {current} {Label()} t={elapsed:F2} {note}");
            current++;
            _phase = 0;
            _stepT = 0f;
            if (current >= endStep)
            {
                running = false;
                done = true;
                success = true;
                if (_fMove != null) _fMove.SetValue(player, 0f);
            }
        }

        void FixedUpdate()
        {
            if (!running) return;
            float dt = Time.fixedDeltaTime;
            _stepT += dt;
            elapsed += dt;
            if (_stepT > stepTimeout) { Fail("timeout"); return; }

            var s = route.steps[current];
            var rb = player.Body;
            Vector3 p = rb.position;
            Vector3 v = rb.linearVelocity;
            if (p.y < minPlayerY) minPlayerY = p.y;
            float inp = s.dir;
            if (s.kind == RouteStepKind.Attach && _phase == 1) inp *= s.swingInput;
            _fMove.SetValue(player, inp);

            switch (s.kind)
            {
                case RouteStepKind.RunJump:
                    if (_phase == 0)
                    {
                        if (player.IsGrounded && player.State == PlayerState.Normal && (p.x - s.jumpX) * s.dir >= 0f)
                        {
                            _fJump.SetValue(player, 0.12f);
                            _phase = 1;
                        }
                        else if (!player.IsGrounded && player.State == PlayerState.Falling && _stepT > 0.3f)
                            Fail("walked off before jumping");
                    }
                    else if (!player.IsGrounded && v.y > 0f)
                        Next($"jump x={p.x:F2}");
                    break;

                case RouteStepKind.Attach:
                    if (_phase == 0)
                    {
                        if (player.IsGrounded) { Fail("landed before attach at " + p); return; }
                        if (v.y <= 0f)
                        {
                            Vector3 d = s.point - p; d.z = 0f;
                            bool ok = plunger.FireToward(s.point);
                            if (!ok) { Fail($"fire failed: aim={plunger.Aim} dist={d.magnitude:F2} hit={plunger.AimPoint}"); return; }
                            log.Add($"   attach {current} dist={player.RopeLength:F2} (sim {s.simAttachDistance:F2}) at {p}");
                            _phase = 1;
                            _wasRising = false;
                            _fMove.SetValue(player, s.dir * s.swingInput); // 부착한 스텝부터 흔들기 입력
                        }
                    }
                    else if (_phase == 1)
                    {
                        if (player.State != PlayerState.Attached) { Fail("detached: " + player.State); return; }
                        Vector3 rel = p - player.AttachPoint;
                        float ang = Mathf.Atan2(rel.x * s.dir, -rel.y) * Mathf.Rad2Deg;
                        bool taut = rel.magnitude >= player.RopeLength - 0.05f;
                        if ((ang >= s.releaseAngle && v.y > 0f) || (ang > 5f && v.y <= 0f && taut && _wasRising))
                        {
                            player.RequestRelease(true);
                            _phase = 2;
                            _fMove.SetValue(player, (float)s.dir); // 해제 스텝의 공중 조작
                        }
                        else if (ang > 0f && v.y > 0.05f && taut) _wasRising = true;
                    }
                    else if (player.State != PlayerState.Attached)
                        Next($"release at {p} v={v}");
                    break;

                case RouteStepKind.Land:
                    if (player.IsGrounded && player.State == PlayerState.Normal)
                        Next($"landed {p}");
                    break;
            }
        }
    }
}
