using System.Collections.Generic;
using Pop.Player;
using UnityEngine;

namespace Pop.Dev
{
    /// <summary>
    /// 개발용 낙하 시험: 지점마다 플레이어를 정지 상태로 놓고(붙지 않음) 떨어뜨려, 멈춘 곳을 기록한다.
    /// 멈춤 = 속도 0.05 m/s 미만이 1.5초 이어짐. 리스폰(안전망)으로 끝나면 respawned=true.
    /// </summary>
    public class DropTester : MonoBehaviour
    {
        public PlayerController player;
        public List<Vector3> points = new List<Vector3>();
        public float settleTime = 1.5f;
        public float timeout = 30f;

        public bool running, done;
        public int index;
        public readonly List<string> results = new List<string>();

        float _still, _t, _minY;
        bool _respawned;
        string _restOn;

        public void Run()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            player.Respawned -= OnRespawn;
            player.Respawned += OnRespawn;
            results.Clear();
            index = -1;
            running = true;
            done = false;
            NextPoint();
        }

        void OnRespawn(Vector3 p) { if (_t > 0.1f) _respawned = true; }

        void NextPoint()
        {
            index++;
            if (index >= points.Count) { running = false; done = true; return; }
            var orig = player.RespawnPosition;
            player.SetRespawnPoint(points[index]);
            player.Kill(); // 정지 상태로 순간이동
            player.SetRespawnPoint(orig);
            _still = 0f; _t = 0f; _respawned = false; _minY = points[index].y;
        }

        void FixedUpdate()
        {
            if (!running) return;
            float dt = Time.fixedDeltaTime;
            _t += dt;
            var rb = player.Body;
            if (rb.position.y < _minY) _minY = rb.position.y;
            if (rb.linearVelocity.magnitude < 0.05f && _t > 0.3f) _still += dt; else _still = 0f;
            if (_still >= settleTime || _t > timeout || _respawned)
            {
                Vector3 p = rb.position;
                _restOn = "-";
                if (Physics.Raycast(p, Vector3.down, out var h, 2f, ~0, QueryTriggerInteraction.Ignore)) _restOn = h.collider.name;
                var s = points[index];
                results.Add($"{index}: from ({s.x:F1},{s.y:F1}) -> rest ({p.x:F1},{p.y:F1}) on {_restOn}  lost {s.y - p.y:F1} m  state {player.State} grounded {player.IsGrounded}  t {_t:F1}s{(_respawned ? "  RESPAWNED(safety net)" : "")}{(_t > timeout ? "  TIMEOUT" : "")}");
                NextPoint();
            }
        }
    }
}
