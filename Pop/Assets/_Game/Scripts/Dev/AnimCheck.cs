using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Pop.Player;
using UnityEngine;

namespace Pop.Dev
{
    /// <summary>
    /// 개발용: Play 중 다람쥐 애니메이션을 자동으로 확인한다 (게임에는 쓰지 않는다).
    /// 쓰는 법 (Play 중): new GameObject("AnimCheck").AddComponent&lt;Pop.Dev.AnimCheck&gt;().Run(폴더);
    /// 끝나면 done = true, summary에 결과, 폴더에 animcheck_*.png / animcheck_log.txt.
    /// 키 입력 대신 PlayerController의 입력 값(_moveInput, _jumpBufferTimer)을 Update 뒤에 직접 넣는다
    /// (에디터 창 포커스와 상관없이 돌리려고). 게임 코드는 바꾸지 않는다.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class AnimCheck : MonoBehaviour
    {
        public bool done;
        public string summary = "";
        public string outDir;
        public int tile = 288;

        public float TPoseHandSpan = 0.852f; // Run 전에 모델에 맞게 넣는다 (예전 다람쥐 0.852, 날다람쥐는 모델에서 잰 값). // 모델 단위(스케일 1)에서 T자 자세의 양손 거리

        PlayerController _player;
        PlayerAnimationVisual _pav;
        PlayerFacingVisual _facing;
        Animator _anim;
        Transform _lHand, _rHand, _lFoot, _rFoot;
        FieldInfo _fMove, _fJumpBuf;
        float _hold;
        Camera _cam;
        RenderTexture _rt;
        readonly StringBuilder _log = new StringBuilder();
        string _phase = "init";

        class Stat { public int frames, loco, air, fall, tpose, trans; public float maxSpan, sumSpeedParam, sumVx; }
        readonly Dictionary<string, Stat> _stats = new Dictionary<string, Stat>();
        readonly List<Vector4> _footSamples = new List<Vector4>(); // walk 구간: (yL, xL, yR, xR)
        readonly List<Vector2> _footTimes = new List<Vector2>();    // (time, bodyVx)
        bool _recordFeet;

        public void Run(string dir)
        {
            outDir = dir;
            StartCoroutine(Sequence());
        }

        void Update()
        {
            if (_player == null) return;
            if (_hold != 0f && _fMove != null && _player.State != PlayerState.Stunned && _player.State != PlayerState.Dead)
                _fMove.SetValue(_player, _hold);
        }

        void PressJump()
        {
            if (_fJumpBuf != null && _player.tuning != null) _fJumpBuf.SetValue(_player, _player.tuning.jumpBufferTime);
        }

        void LateUpdate()
        {
            if (_anim == null || done) return;
            if (!_stats.TryGetValue(_phase, out var s)) { s = new Stat(); _stats[_phase] = s; }
            var info = _anim.GetCurrentAnimatorStateInfo(0);
            s.frames++;
            if (info.IsName("Locomotion")) s.loco++;
            else if (info.IsName("Air")) s.air++;
            else if (info.IsName("Fall")) s.fall++;
            if (_anim.IsInTransition(0)) s.trans++;
            s.sumSpeedParam += _anim.GetFloat("Speed");
            s.sumVx += Mathf.Abs(_player.Velocity.x);
            if (_lHand != null && _rHand != null)
            {
                float span = Vector3.Distance(_lHand.position, _rHand.position) / Mathf.Max(1e-4f, _anim.transform.lossyScale.x);
                if (span > s.maxSpan) s.maxSpan = span;
                if (span > TPoseHandSpan * 0.93f) s.tpose++;
            }
            if (_recordFeet && _lFoot != null && _rFoot != null)
            {
                _footSamples.Add(new Vector4(_lFoot.position.y, _lFoot.position.x, _rFoot.position.y, _rFoot.position.x));
                _footTimes.Add(new Vector2(Time.time, _player.Velocity.x));
            }
        }

        IEnumerator Sequence()
        {
            _player = FindFirstObjectByType<PlayerController>();
            if (_player == null) { Finish("PlayerController 없음"); yield break; }
            _pav = _player.GetComponent<PlayerAnimationVisual>();
            _facing = _player.GetComponent<PlayerFacingVisual>();
            _anim = _pav != null ? _pav.animator : _player.GetComponentInChildren<Animator>();
            if (_anim == null) { Finish("Animator 없음"); yield break; }
            _lHand = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            _rHand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            _lFoot = _anim.GetBoneTransform(HumanBodyBones.LeftToes) ?? _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rFoot = _anim.GetBoneTransform(HumanBodyBones.RightToes) ?? _anim.GetBoneTransform(HumanBodyBones.RightFoot);
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
            _fMove = typeof(PlayerController).GetField("_moveInput", bf);
            _fJumpBuf = typeof(PlayerController).GetField("_jumpBufferTimer", bf);
            if (_fMove == null || _fJumpBuf == null) { Finish("PlayerController 입력 필드를 못 찾음"); yield break; }
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("AnimCheckCamera");
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 1.05f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 80f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.62f, 0.74f, 0.86f);
            _cam.enabled = false;
            _rt = new RenderTexture(tile, tile, 24);
            _cam.targetTexture = _rt;

            _log.AppendLine($"controller={(_anim.runtimeAnimatorController != null ? _anim.runtimeAnimatorController.name : "NONE")} avatar={(_anim.avatar != null ? _anim.avatar.name : "none")} human={_anim.isHuman} applyRootMotion={_anim.applyRootMotion}");
            _log.AppendLine($"tuning: moveSpeed={_player.tuning.moveSpeed} jumpVelocity={_player.tuning.jumpVelocity} gravityMul={_player.tuning.gravityMultiplier} gravity={Physics.gravity.y}");

            // 0) 착지 기다림 → 대기
            _phase = "settle";
            float t0 = Time.time;
            while (!_player.IsGrounded && Time.time - t0 < 6f) yield return null;
            yield return new WaitForSeconds(0.6f);
            _phase = "idle";
            var idleTiles = new List<Texture2D>();
            for (int i = 0; i < 6; i++) { yield return new WaitForSeconds(0.33f); yield return Shot(idleTiles); }
            Save("animcheck_idle.png", idleTiles, 6);

            // 1) 오른쪽으로 이동 (D)
            _phase = "walk_right";
            _hold = 1f;
            yield return new WaitForSeconds(0.35f);
            _recordFeet = true;
            var walkTiles = new List<Texture2D>();
            float next = Time.time;
            float tStart = Time.time;
            bool shotGame = false;
            while (walkTiles.Count < 12 && Time.time - tStart < 1.2f)
            {
                if (Time.time >= next)
                {
                    yield return Shot(walkTiles);
                    next += 0.03f;
                }
                else yield return null;
                if (!shotGame && Time.time - tStart > 0.15f)
                {
                    shotGame = true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "animcheck_gameview_walk.png"));
                }
            }
            float walkVx = _player.Velocity.x;
            int facingRight = _facing != null ? _facing.Facing : 0;
            _recordFeet = false;
            Save("animcheck_walk.png", walkTiles, 6);
            _log.AppendLine($"walk_right: vx={walkVx:F2} facing={facingRight} visualYaw={VisualYaw():F0} speedParam={_anim.GetFloat("Speed"):F2}");
            _log.AppendLine(FootSlide());
            _hold = 0f;
            _phase = "stop";
            yield return new WaitForSeconds(0.6f);
            _log.AppendLine($"stop: vx={_player.Velocity.x:F2} speedParam={_anim.GetFloat("Speed"):F2} state={StateName()}");

            // 2) 왼쪽으로 이동 (A): 방향 전환
            _phase = "walk_left";
            _hold = -1f;
            yield return new WaitForSeconds(0.8f);
            var leftTiles = new List<Texture2D>();
            yield return Shot(leftTiles);
            _log.AppendLine($"walk_left: vx={_player.Velocity.x:F2} facing={(_facing != null ? _facing.Facing : 0)} visualYaw={VisualYaw():F0}");
            _hold = 0f;
            _phase = "stop2";
            yield return new WaitForSeconds(0.6f);

            // 3) 제자리 점프
            _phase = "jump";
            var jumpTiles = new List<Texture2D>();
            float groundY = _player.transform.position.y;
            PressJump();
            float tj = Time.time;
            float airStart = -1f, airEnd = -1f, maxY = groundY;
            int shots = 0;
            while (Time.time - tj < 1.45f)
            {
                float e = Time.time - tj;
                if (airStart < 0f && !_player.IsGrounded) airStart = Time.time;
                if (airStart >= 0f && airEnd < 0f && _player.IsGrounded && e > 0.3f) airEnd = Time.time;
                maxY = Mathf.Max(maxY, _player.transform.position.y);
                if (shots < 12 && e >= shots * 0.115f) { shots++; yield return Shot(jumpTiles, $"{e:F2}s {StateName()} T{_anim.GetFloat("JumpT"):F2}"); }
                else yield return null;
            }
            Save("animcheck_jump.png", jumpTiles, 6);
            _log.AppendLine($"jump: airTime={(airEnd > 0 ? (airEnd - airStart).ToString("F3") : "?")}s height={maxY - groundY:F2}m (식: 2v/g={2f * _player.tuning.jumpVelocity / (-Physics.gravity.y * _player.tuning.gravityMultiplier):F3}s)");
            _log.AppendLine("jump tiles: " + string.Join(" | ", _tileNotes));
            _tileNotes.Clear();

            // 4) 달리며 점프
            _phase = "run_jump";
            _hold = 1f;
            yield return new WaitForSeconds(0.3f);
            PressJump();
            yield return new WaitForSeconds(0.2f);
            t0 = Time.time;
            while (!_player.IsGrounded && Time.time - t0 < 3f) yield return null;
            _hold = 0f;
            yield return new WaitForSeconds(0.5f);

            // 5) 기절 색: 점프 중 Stunned로 바꿔 본다 (착지하면 풀림)
            _phase = "stun";
            PressJump();
            yield return new WaitForSeconds(0.35f);
            var stunTiles = new List<Texture2D>();
            var rend = _anim.GetComponentInChildren<Renderer>();
            Color before = ReadTint(rend);
            _player.SetState(PlayerState.Stunned);
            yield return new WaitForSeconds(0.12f);
            Color during = ReadTint(rend);
            yield return Shot(stunTiles);
            t0 = Time.time;
            while (_player.State == PlayerState.Stunned && Time.time - t0 < 4f) yield return null;
            yield return new WaitForSeconds(0.5f);
            Color after = ReadTint(rend);
            yield return Shot(stunTiles);
            _log.AppendLine($"stun tint (_BaseColor block): before={Fmt(before)} during={Fmt(during)} after={Fmt(after)} stateAfter={_player.State}");

            // 6) 긴 낙하: 위로 25 m 순간이동 → Fall 자세
            _phase = "long_fall";
            _player.DebugTeleport(_player.transform.position + Vector3.up * 25f);
            yield return new WaitForSeconds(1.3f);
            yield return Shot(stunTiles);
            _log.AppendLine($"long_fall @1.3s: vy={_player.Velocity.y:F1} state={StateName()}");
            t0 = Time.time;
            while (!_player.IsGrounded && Time.time - t0 < 8f) yield return null;
            _phase = "after_fall";
            yield return new WaitForSeconds(0.7f);
            yield return Shot(stunTiles);
            stunTiles.InsertRange(0, leftTiles);
            Save("animcheck_misc_left_stun_recovered_fall_landed.png", stunTiles, stunTiles.Count);
            _log.AppendLine($"after_fall: state={StateName()} playerState={_player.State} grounded={_player.IsGrounded}");

            foreach (var kv in _stats)
            {
                var s = kv.Value;
                if (s.frames == 0) continue;
                _log.AppendLine($"[{kv.Key}] frames={s.frames} Locomotion={s.loco} Air={s.air} Fall={s.fall} inTransition={s.trans} " +
                                $"avgSpeedParam={s.sumSpeedParam / s.frames:F2} avg|vx|={s.sumVx / s.frames:F2} maxHandSpan={s.maxSpan:F3} (T-pose {TPoseHandSpan}) tposeFrames={s.tpose}");
            }
            Finish("ok");
        }

        readonly List<string> _tileNotes = new List<string>();

        IEnumerator Shot(List<Texture2D> into, string note = null)
        {
            yield return new WaitForEndOfFrame();
            Vector3 p = _player.transform.position;
            _cam.transform.position = new Vector3(p.x, p.y + 0.15f, p.z - 20f);
            _cam.transform.rotation = Quaternion.identity;
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(tile, tile, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, tile, tile), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            into.Add(tex);
            if (note != null) _tileNotes.Add(note);
        }

        void Save(string name, List<Texture2D> tiles, int cols)
        {
            if (tiles.Count == 0) return;
            cols = Mathf.Max(1, Mathf.Min(cols, tiles.Count));
            int rows = (tiles.Count + cols - 1) / cols;
            var sheet = new Texture2D(cols * tile, rows * tile, TextureFormat.RGB24, false);
            var fill = new Color32[cols * tile * rows * tile];
            sheet.SetPixels32(fill);
            for (int i = 0; i < tiles.Count; i++)
            {
                int c = i % cols, r = i / cols;
                sheet.SetPixels(c * tile, (rows - 1 - r) * tile, tile, tile, tiles[i].GetPixels());
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name), sheet.EncodeToPNG());
            Destroy(sheet);
        }

        string FootSlide()
        {
            if (_footSamples.Count < 8) return "foot slide: 샘플 부족";
            float minL = float.MaxValue, maxL = float.MinValue, minR = float.MaxValue, maxR = float.MinValue;
            foreach (var s in _footSamples) { minL = Mathf.Min(minL, s.x); maxL = Mathf.Max(maxL, s.x); minR = Mathf.Min(minR, s.z); maxR = Mathf.Max(maxR, s.z); }
            float sum = 0f, body = 0f; int n = 0;
            for (int i = 1; i < _footSamples.Count; i++)
            {
                float dt = _footTimes[i].x - _footTimes[i - 1].x;
                if (dt <= 1e-5f) continue;
                var a = _footSamples[i - 1]; var b = _footSamples[i];
                // 디딤 구간 = 발이 그 발의 높이 범위 아래 12% 안에 있을 때. 이때 발의 월드 X 속도 = 미끄러짐.
                if (a.x < minL + 0.12f * (maxL - minL) && b.x < minL + 0.12f * (maxL - minL)) { sum += (b.y - a.y) / dt; body += _footTimes[i].y; n++; }
                if (a.z < minR + 0.12f * (maxR - minR) && b.z < minR + 0.12f * (maxR - minR)) { sum += (b.w - a.w) / dt; body += _footTimes[i].y; n++; }
            }
            if (n == 0) return "foot slide: 디딤 구간 없음";
            float slide = sum / n, bodyV = body / n;
            return $"foot slide (참고용: 빠른 달리기에서는 디딤 판정이 틀려 믿을 수 없다. walk_right, 디딤 발의 월드 X 속도 평균): {slide:F2} m/s at body {bodyV:F2} m/s → 애니메이션이 덮는 속도 {bodyV - slide:F2} m/s ({(bodyV != 0 ? 100f * slide / bodyV : 0f):F0}% 미끄러짐), 발 높이 범위 L {maxL - minL:F3} R {maxR - minR:F3} m, samples {n}";
        }

        float VisualYaw() => _facing != null && _facing.visual != null ? _facing.visual.localEulerAngles.y : float.NaN;

        string StateName()
        {
            var info = _anim.GetCurrentAnimatorStateInfo(0);
            return info.IsName("Locomotion") ? "Locomotion" : info.IsName("Air") ? "Air" : info.IsName("Fall") ? "Fall" : "?";
        }

        static Color ReadTint(Renderer r)
        {
            if (r == null) return Color.clear;
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            return b.isEmpty ? new Color(1f, 1f, 1f, 1f) : b.GetColor("_BaseColor");
        }

        static string Fmt(Color c) => $"({c.r:F2},{c.g:F2},{c.b:F2})";

        void Finish(string result)
        {
            _hold = 0f;
            summary = result + "\n" + _log;
            try { if (!string.IsNullOrEmpty(outDir)) File.WriteAllText(Path.Combine(outDir, "animcheck_log.txt"), summary); } catch { }
            if (_cam != null) Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            done = true;
            Debug.Log("[AnimCheck] " + summary);
        }
    }
}
