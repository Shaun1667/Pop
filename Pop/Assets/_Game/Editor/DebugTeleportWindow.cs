using System.Collections.Generic;
using Pop.CameraSystem;
using Pop.Dev;
using Pop.Player;
using Pop.Plunger;
using UnityEditor;
using UnityEngine;

namespace Pop.EditorTools
{
    /// <summary>
    /// 개발용 순간이동 창 (메뉴 Pop/Debug/Teleport). Play 중에만 동작한다. 씬은 바꾸지 않는다(읽기만).
    /// 목록은 열린 씬에서 만든다: 시작·정상, 구간(S0~S4), 발판(이름에 Ledge), 앵커(PlungerAttachable 아래 3m), 경로 데이터(MountainRoute).
    /// 순간이동은 PlayerController.DebugTeleport(줄 해제 · 기절 해제 · 속도 0 · Z 유지)를 부른다.
    /// 게임 입력은 읽지 않는다(입력은 GameInput 한 곳 규칙). 이 창과 Scene 뷰 클릭은 Game 뷰 포커스 밖이라 뚫어뻥이 나가지 않는다.
    /// </summary>
    public class DebugTeleportWindow : EditorWindow
    {
        const float AnchorDrop = 3f;   // 앵커(부착 패치) 아래로 이만큼 떨어진 곳에 세운다
        const float StepMeters = 10f;

        class Entry
        {
            public string name;
            public Vector3 pos;  // 플레이어 중심 위치
        }

        class Group
        {
            public string title;
            public bool open = true;
            public readonly List<Entry> items = new List<Entry>();
        }

        readonly List<Group> _groups = new List<Group>();
        bool _needsBuild = true;
        Vector2 _scroll;
        float _x, _y;
        bool _sceneClick;
        bool _freezeAfter;
        bool _frozen;
        float _savedTimeScale = 1f;
        string _lastMessage = "";

        [MenuItem("Pop/Debug/Teleport")]
        static void Open() => GetWindow<DebugTeleportWindow>("Pop Teleport");

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.hierarchyChanged += MarkDirty;
            _needsBuild = true;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.hierarchyChanged -= MarkDirty;
            Unfreeze();
        }

        void MarkDirty() => _needsBuild = true;

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) Unfreeze();
            _needsBuild = true;
            Repaint();
        }

        // 실시간 표시 (초당 10번)
        void OnInspectorUpdate() => Repaint();

        // ---------------- 공용 (코드·테스트에서도 부를 수 있다) ----------------

        public static PlayerController FindPlayer() => Object.FindFirstObjectByType<PlayerController>();

        /// <summary>플레이어 발 → 중심 높이 (바닥에 세울 때 더한다)</summary>
        static float FootOffset(PlayerController p)
        {
            var col = p != null ? p.GetComponent<Collider>() : null;
            if (col == null) return 0.62f;
            return p.transform.position.y - col.bounds.min.y + 0.02f;
        }

        /// <summary>Play 중이면 플레이어를 pos로 옮긴다 (z는 플레이어 평면 유지). 성공하면 true.</summary>
        public bool TeleportTo(Vector3 pos, string label)
        {
            if (!EditorApplication.isPlaying) { _lastMessage = "Play 중에만 순간이동할 수 있습니다."; return false; }
            var p = FindPlayer();
            if (p == null) { _lastMessage = "씬에 PlayerController가 없습니다."; return false; }

            p.DebugTeleport(pos);
            var cam = Object.FindFirstObjectByType<CameraFollow>();
            if (cam != null) cam.SnapToTarget();
            if (_freezeAfter) Freeze();
            _lastMessage = $"→ {label}  ({pos.x:F1}, {pos.y:F1})";
            _x = pos.x; _y = pos.y;
            return true;
        }

        void Freeze()
        {
            // Esc 일시정지 중이면 지금 timeScale(0)은 일시정지 것이다: 그 전 값을 기억한다
            if (!_frozen) _savedTimeScale = Pop.Game.PauseMenu.IsPaused ? Pop.Game.PauseMenu.TimeScaleBeforePause : (Time.timeScale > 0f ? Time.timeScale : 1f);
            Time.timeScale = 0f;
            _frozen = true;
            Pop.Game.PauseMenu.DebugFreeze = true;
        }

        void Unfreeze()
        {
            if (!_frozen) return;
            // Esc 일시정지 중이면 timeScale은 일시정지가 갖고 있다(계속할 때 그쪽이 되돌린다)
            if (EditorApplication.isPlaying && !Pop.Game.PauseMenu.IsPaused) Time.timeScale = _savedTimeScale;
            _frozen = false;
            Pop.Game.PauseMenu.DebugFreeze = false;
        }

        // ---------------- 목록 만들기 ----------------

        void Build()
        {
            _needsBuild = false;
            _groups.Clear();
            var player = FindPlayer();
            float off = FootOffset(player);

            // 1) 시작 · 정상
            var g = new Group { title = "시작 · 정상" };
            var respawn = GameObject.Find("RespawnPoint");
            if (respawn != null) Add(g, "시작 (RespawnPoint)", respawn.transform.position);
            else if (player != null) Add(g, "시작 (플레이어 처음 위치)", EditorApplication.isPlaying ? player.RespawnPosition : player.transform.position);

            Collider summit = FindColliderByName("Summit");
            if (summit != null) Add(g, summit.name, TopCenter(summit, off));
            var tree = FindByNamePart("AcornTree");
            if (tree != null)
            {
                Vector3 t = tree.transform.position;
                float y = summit != null ? summit.bounds.max.y + off : t.y + off;
                Add(g, tree.name + " (옆)", new Vector3(t.x - 1.5f, y, 0f));
            }
            AddGroup(g);

            // 2) 구간 부모 (S0_Start, S1_… ~ S4_…): 그 구간의 가장 낮은 발판 위
            g = new Group { title = "구간" };
            var level = GameObject.Find("Level");
            if (level != null)
            {
                foreach (Transform s in level.transform)
                {
                    if (s.name.Length < 3 || s.name[0] != 'S' || !char.IsDigit(s.name[1]) || s.name[2] != '_') continue;
                    Collider best = null;
                    foreach (var c in s.GetComponentsInChildren<Collider>())
                    {
                        if (c.isTrigger || !IsStandName(c.name)) continue;
                        if (best == null || c.bounds.max.y < best.bounds.max.y) best = c;
                    }
                    if (best != null) Add(g, $"{s.name}  [{best.name}]", TopCenter(best, off));
                }
            }
            AddGroup(g);

            // 3) 발판: 씬에서 이름에 Ledge가 들어간 콜라이더 전부 (사용자가 옮기거나 복제한 것도), 높이 순
            g = new Group { title = "발판 (씬)" };
            var ledges = new List<Collider>();
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (!c.isTrigger && c.name.Contains("Ledge")) ledges.Add(c);
            ledges.Sort((a, b) => a.bounds.max.y.CompareTo(b.bounds.max.y));
            foreach (var c in ledges) Add(g, c.name, TopCenter(c, off));
            AddGroup(g);

            // 4) 앵커: PlungerAttachable 면 아래 AnchorDrop m (거기서 위로 쏘면 닿는다), 높이 순
            g = new Group { title = $"앵커 (부착 면 아래 {AnchorDrop:F0}m)" };
            var patches = new List<Collider>();
            foreach (var a in Object.FindObjectsByType<PlungerAttachable>(FindObjectsSortMode.None))
            {
                var c = a.GetComponent<Collider>();
                if (c != null && !c.isTrigger && c.name.Contains("Ledge") == false) patches.Add(c);
            }
            patches.Sort((a, b) => a.bounds.min.y.CompareTo(b.bounds.min.y));
            foreach (var c in patches)
            {
                string n = c.transform.parent != null ? c.transform.parent.name : c.name;
                Vector3 b = c.bounds.center;
                Add(g, n, new Vector3(b.x, c.bounds.min.y - AnchorDrop, 0f));
            }
            AddGroup(g);

            // 5) 경로 데이터 (MountainRoute, 설계 당시 위치. 맵을 손으로 고쳤으면 어긋날 수 있다)
            var route = Object.FindFirstObjectByType<MountainRoute>();
            if (route != null)
            {
                g = new Group { title = "경로 데이터 (Dev_RouteData)", open = false };
                foreach (var s in route.steps)
                {
                    if (s.kind == RouteStepKind.RunJump && s.standPos != Vector3.zero)
                        Add(g, s.label.Replace("run-jump ", "발판 "), s.standPos);
                    else if (s.kind == RouteStepKind.Attach)
                        Add(g, "앵커 " + s.label, s.point + Vector3.down * AnchorDrop);
                }
                AddGroup(g);
            }
        }

        static bool IsStandName(string n) => n.Contains("Ledge") || n.Contains("Plateau");

        static Vector3 TopCenter(Collider c, float off)
        {
            var b = c.bounds;
            return new Vector3(b.center.x, b.max.y + off, 0f);
        }

        static GameObject FindByNamePart(string part)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.Contains(part)) return t.gameObject;
            return null;
        }

        static Collider FindColliderByName(string part)
        {
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (!c.isTrigger && c.name.Contains(part)) return c;
            return null;
        }

        static void Add(Group g, string name, Vector3 pos) => g.items.Add(new Entry { name = name, pos = pos });

        void AddGroup(Group g)
        {
            if (g.items.Count > 0) _groups.Add(g);
        }

        // ---------------- 창 ----------------

        void OnGUI()
        {
            if (_needsBuild) Build();
            bool playing = EditorApplication.isPlaying;
            var player = FindPlayer();

            if (!playing)
                EditorGUILayout.HelpBox("Play 중에만 순간이동합니다. 목록은 미리 볼 수 있습니다.", MessageType.Info);

            // 현재 상태
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (player != null)
                {
                    Vector3 p = player.transform.position;
                    float feet = p.y - FootOffset(player);
                    EditorGUILayout.LabelField("위치", $"x {p.x:F2}   y {p.y:F2}   z {p.z:F2}");
                    EditorGUILayout.LabelField("높이 (발)", $"{feet:F1} m");
                    EditorGUILayout.LabelField("상태", playing ? player.State.ToString() + (_frozen ? "  (얼림)" : "") : "-");
                }
                else EditorGUILayout.LabelField("씬에 PlayerController가 없습니다.");
                if (!string.IsNullOrEmpty(_lastMessage)) EditorGUILayout.LabelField(_lastMessage, EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(!playing || player == null))
            {
                // 빠른 이동
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"+{StepMeters:F0} m") && player != null)
                        TeleportTo(player.transform.position + Vector3.up * StepMeters, $"+{StepMeters:F0} m");
                    if (GUILayout.Button($"−{StepMeters:F0} m") && player != null)
                        TeleportTo(player.transform.position + Vector3.down * StepMeters, $"−{StepMeters:F0} m");
                }

                // 좌표
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = 14;
                    _x = EditorGUILayout.FloatField("X", _x);
                    _y = EditorGUILayout.FloatField("Y", _y);
                    EditorGUIUtility.labelWidth = 0;
                    if (GUILayout.Button("현재", GUILayout.Width(44)) && player != null)
                    { _x = player.transform.position.x; _y = player.transform.position.y; GUI.FocusControl(null); }
                    if (GUILayout.Button("이동", GUILayout.Width(44)))
                        TeleportTo(new Vector3(_x, _y, 0f), "좌표");
                }
            }

            // 옵션
            bool click = EditorGUILayout.ToggleLeft("Scene 뷰 클릭으로 순간이동 (플레이어 Z 평면)", _sceneClick);
            if (click != _sceneClick) { _sceneClick = click; SceneView.RepaintAll(); }
            _freezeAfter = EditorGUILayout.ToggleLeft("순간이동 뒤 얼리기 (Time.timeScale 0)", _freezeAfter);
            if (_frozen)
            {
                if (GUILayout.Button("계속 (얼림 풀기)", GUILayout.Height(24))) Unfreeze();
            }

            // 목록
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("순간이동 목록", EditorStyles.boldLabel);
                if (GUILayout.Button("새로고침", GUILayout.Width(70))) Build();
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            float footOff = FootOffset(player);
            foreach (var g in _groups)
            {
                g.open = EditorGUILayout.Foldout(g.open, $"{g.title}  ({g.items.Count})", true);
                if (!g.open) continue;
                using (new EditorGUI.DisabledScope(!playing))
                {
                    foreach (var e in g.items)
                    {
                        float h = e.pos.y - footOff;
                        if (GUILayout.Button($"{e.name}    {h:F1} m", EditorStyles.miniButton))
                            TeleportTo(e.pos, e.name);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ---------------- Scene 뷰 클릭 ----------------

        void OnSceneGUI(SceneView view)
        {
            if (!_sceneClick || !EditorApplication.isPlaying) return;
            var player = FindPlayer();
            if (player == null) return;

            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id); // 클릭이 선택으로 가지 않게

            float z = player.transform.position.z;
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, z));
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!plane.Raycast(ray, out float dist)) return;
            Vector3 hit = ray.GetPoint(dist);
            hit.z = z;

            Handles.color = Color.cyan;
            Handles.DrawWireDisc(hit, Vector3.forward, 0.6f);
            Handles.Label(hit + Vector3.up * 0.9f, $"클릭 = 순간이동  ({hit.x:F1}, {hit.y:F1})");
            if (e.type == EventType.MouseMove) view.Repaint();

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                // 클릭한 점 = 발 위치
                TeleportTo(hit + Vector3.up * FootOffset(player), "Scene 뷰 클릭");
                e.Use();
                Repaint();
            }
        }
    }
}
