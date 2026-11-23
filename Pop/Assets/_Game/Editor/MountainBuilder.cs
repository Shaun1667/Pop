using System;
using System.Collections.Generic;
using System.Text;
using Pop.Dev;
using Pop.Player;
using Pop.Plunger;
using UnityEditor;
using UnityEngine;

namespace Pop.EditorTools
{
    /// <summary>
    /// Mountain 맵 생성기. 열린 씬의 "Level"을 지우고 다시 만든다 (메뉴 Pop/Dev/Rebuild Mountain Level).
    ///
    /// 구조: 좌우 벽으로 둘러싼 탑. 경로는 층(tier)마다 방향을 바꾸며 지그재그로 쌓인다.
    /// 실패하면 순간이동 없이 몸이 그대로 떨어진다: 층 가장자리의 발판(돌아서는 계단)에 걸리면 조금만 잃고,
    /// 가운데로 떨어지면 아래층 바위(꼭대기가 뾰족해 설 수 없다)에 튕기며 맨 아래 시작 분지까지 내려간다.
    /// 설 수 있는 곳은 경로의 발판, 시작 분지(걸어서 시작점으로), 정상뿐이다. 벽 바깥·아래의 안전망만 KillZone.
    ///
    /// 경로는 PlayerController의 물리(중력 ×1.6, 늘어나지 않는 로프, 스윙 힘, Space 해제)를 그대로 흉내 낸
    /// 시뮬레이션으로 설계한다: 꼭짓점에서 부착 → dir 키를 누른 채 흔들기 → releaseAngle에서 Space 해제 → 다음 꼭짓점.
    /// 그 정답 경로를 MountainRoute(개발용)에 남겨 RouteBot이 Play에서 그대로 검증한다.
    /// </summary>
    public static class MountainBuilder
    {
        const float Dt = 0.02f;
        const float HalfH = 0.6f;
        const float Depth = 4f;
        const float PeakDeg = 55f;            // 바위 지붕 경사 (45° 넘으면 설 수 없다)
        const string MeshAssetPath = "Assets/_Game/Level/MountainMeshes.asset";
        static readonly Vector2 G = new Vector2(0f, -9.81f);

        enum Ph { Fly, Rope, Ground }

        class Anchor
        {
            public Vector2 a, attachPos;
            public string style, label;
            public int pathStart, pathRelease, pathEnd;
            public float rope, window;
        }

        class Ledge { public float x0, x1, top, bottom; public string name; public Transform t; }

        class SectionInfo { public string name; public Transform t; public int pathFrom, pathTo; public int anchors, tiers; public float minWindow = 99f, maxRope; }

        static PlayerTuning T;
        static Vector2 p, v;
        static int dir;
        static List<Vector2> path;
        static List<Ph> phase;
        static List<RouteStep> steps;
        static List<Anchor> anchors;
        static List<Ledge> ledges;
        static List<SectionInfo> sections;
        static List<Mesh> meshes;
        static Dictionary<string, Mesh> meshCache;
        static Transform root, section;
        static Material mLevel, mAtt, mBackdrop, mTrunk, mLeaf, mAcorn;
        static int flightStart, tier;
        static StringBuilder log;
        static float wallL, wallR, topY;

        [MenuItem("Pop/Dev/Rebuild Mountain Level")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            log = new StringBuilder();
            T = AssetDatabase.LoadAssetAtPath<PlayerTuning>("Assets/_Game/Tuning/PlayerTuning.asset");
            mLevel = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Materials/M_Level.mat");
            mAtt = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Materials/M_Attachable.mat");
            mBackdrop = EnsureMat("M_Backdrop", new Color(0.30f, 0.34f, 0.40f));
            mTrunk = EnsureMat("M_TreeTrunk", new Color(0.42f, 0.26f, 0.12f));
            mLeaf = EnsureMat("M_TreeLeaves", new Color(0.25f, 0.55f, 0.20f));
            mAcorn = EnsureMat("M_Acorn", new Color(0.65f, 0.42f, 0.15f));

            var lv = GameObject.Find("Level");
            if (lv != null) UnityEngine.Object.DestroyImmediate(lv);
            if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath) != null) AssetDatabase.DeleteAsset(MeshAssetPath);
            root = new GameObject("Level").transform;

            path = new List<Vector2>(); phase = new List<Ph>();
            steps = new List<RouteStep>(); anchors = new List<Anchor>(); ledges = new List<Ledge>();
            sections = new List<SectionInfo>();
            meshes = new List<Mesh>(); meshCache = new Dictionary<string, Mesh>();
            tier = 0;

            try { Design(); }
            catch (Exception e) { log.AppendLine("DESIGN ERROR: " + e.Message + " at p=" + p + " v=" + v); }
            CloseSection();

            BuildEnclosure();
            BuildBackdrop();

            var rd = new GameObject("Dev_RouteData");
            rd.transform.SetParent(root, false);
            rd.AddComponent<MountainRoute>().steps = steps;

            SaveMeshes();
            Physics.SyncTransforms();
            CheckPath();
            Summary();
            return log.ToString();
        }

        // ------------------------------------------------------------------ 설계

        static void Design()
        {
            Section("S0_Start");
            Start(6f, 0f);

            // ---- 1. 기슭: 스윙 배우기 (넓은 패치, 짧은 줄) ----
            Section("S1_Base_Foothills");
            Tier(); AnchorOp(5.2f, 70f, 42f, "Knob"); AnchorOp(5.6f, 68f, 45f, "Knob"); AnchorOp(5.8f, 66f, 45f, "Knob"); TurnZone(-1);
            Tier(); for (int i = 0; i < 3; i++) AnchorOp(6.0f, 66f, 45f, "Knob"); TurnZone(+1);

            // ---- 2. 중턱: 긴 사슬, 사거리 끝 간격, 공중에서 놓고 다시 붙이기 ----
            Section("S2_Mid_Chains");
            Tier(); AnchorOp(6.5f, 64f, 48f, "Patch"); AnchorOp(6.5f, 64f, 48f, "Patch"); MidLedge(); AnchorOp(6.5f, 64f, 48f, "Patch"); TurnZone(-1);
            Tier(); AnchorOp(6.9f, 46f, 26f, "Patch"); AnchorOp(7.0f, 52f, 34f, "Patch"); MidLedge(); AnchorOp(7.0f, 66f, 50f, "Patch"); TurnZone(+1);
            Tier(); for (int i = 0; i < 3; i++) AnchorOp(7.0f, 66f, 50f, "Patch"); TurnZone(-1);

            // ---- 3. 고지대: 작은 패치, 늦게 놓으면 머리를 박는 넓은 바위 (부착 중 닿으면 기절) ----
            Section("S3_High_Crags");
            Tier(); for (int i = 0; i < 3; i++) AnchorOp(7.1f, 68f, 50f, "Slim"); TurnZone(+1);
            Tier(); AnchorOp(7.1f, 70f, 50f, "Stun"); MidLedge(); AnchorOp(7.1f, 70f, 50f, "Stun"); AnchorOp(7.1f, 70f, 50f, "Stun"); TurnZone(-1);
            Tier(); AnchorOp(7.1f, 70f, 50f, "Slim"); AnchorOp(7.1f, 70f, 50f, "Slim"); MidLedge(); AnchorOp(7.1f, 70f, 50f, "Slim"); TurnZone(+1);

            // ---- 4. 정상 질주 ----
            Section("S4_Summit_Run");
            Tier(); for (int i = 0; i < 4; i++) AnchorOp(7.2f, 68f, 52f, "Slim");
            Summit(16f, 1.0f);
        }

        static void Tier() { tier++; CurSection.tiers++; }

        /// <summary>층 가운데 작은 쉬는 발판 (경로의 일부). 그 위에서 조금 미끄러지면 여기 걸려 조금만 잃는다.</summary>
        static void MidLedge() => LedgeOp(3.5f, 1.5f);

        /// <summary>층 끝: 착지 발판 → 점프로 오르는 계단 2개 → 맨 위에서 돌아선다. 아래층 바위와 겹치지 않게 3.4m 더 올라간다.</summary>
        static void TurnZone(int newDir)
        {
            LedgeOp(5f, 1.5f);
            LedgeOp(3.5f, 0.3f);
            LedgeOp(4.5f, 0.3f, newDir);
        }

        // ------------------------------------------------------------------ 시뮬레이션 (PlayerController와 같은 순서)

        static void Record(Ph ph) { path.Add(p); phase.Add(ph); }

        static void Integrate(Ph ph) { v += G * Dt; p += v * Dt; Record(ph); }

        static void ExtraGravity()
        {
            v += G * ((T.gravityMultiplier - 1f) * Dt);
            if (v.y < -T.maxFallSpeed) v.y = -T.maxFallSpeed;
        }

        static void StepRope(Vector2 a, float L, float inp)
        {
            ExtraGravity();
            Vector2 d = p - a; float dist = d.magnitude;
            if (dist > 1e-4f)
            {
                Vector2 n = d / dist;
                if (Mathf.Abs(inp) > 0.01f)
                {
                    var t = new Vector2(-n.y, n.x); if (t.x < 0f) t = -t;
                    v += t * (inp * T.swingForce * Dt);
                }
                if (dist > L + 0.02f)
                {
                    p = a + n * L;
                    float o = Vector2.Dot(v, n); if (o > 0f) v -= n * o;
                }
                Vector2 pn = p + (v + G * Dt) * Dt; Vector2 dn = pn - a; float m = dn.magnitude;
                if (m > L && m > 1e-4f) { Vector2 oc = a + dn * (L / m); v = (oc - p) / Dt - G * Dt; }
            }
            Integrate(Ph.Rope);
        }

        static void StepFly(float inp)
        {
            if (Mathf.Abs(inp) > 0.01f)
            {
                float target = inp * T.moveSpeed;
                bool same = Mathf.Sign(v.x) == Mathf.Sign(target) && Mathf.Abs(v.x) >= Mathf.Abs(target);
                if (!same) v.x = Mathf.MoveTowards(v.x, target, T.airAccel * Dt);
            }
            ExtraGravity();
            Integrate(Ph.Fly);
        }

        static void StepGround(float top, float inp, bool jump)
        {
            v.x = Mathf.MoveTowards(v.x, inp * T.moveSpeed, T.groundAccel * Dt);
            if (jump)
            {
                v.y = T.jumpVelocity;
                ExtraGravity();
                Integrate(Ph.Fly);
                return;
            }
            v.y = 0f;
            p.x += v.x * Dt; p.y = top + HalfH;
            Record(Ph.Ground);
        }

        static void FlyToApex()
        {
            for (int k = 0; k < 2000; k++)
            {
                if (v.y <= 0f) return;
                StepFly(dir);
            }
            throw new Exception("no apex");
        }

        /// <summary>지금 비행(flightStart 이후 + 꼭짓점 뒤 계속)에서 a까지 사거리 안인 시간</summary>
        static float AttachWindow(Vector2 a)
        {
            float range = T.plungerRange;
            int n = 0;
            for (int i = flightStart; i < path.Count; i++) if ((path[i] - a).magnitude <= range) n++;
            Vector2 sp = p, sv = v; int cnt = path.Count;
            for (int k = 0; k < 150; k++) { StepFly(dir); if ((p - a).magnitude <= range) n++; }
            p = sp; v = sv;
            path.RemoveRange(cnt, path.Count - cnt); phase.RemoveRange(cnt, phase.Count - cnt);
            return n * Dt;
        }

        // ------------------------------------------------------------------ 동작

        static void Section(string name)
        {
            CloseSection();
            section = new GameObject(name).transform;
            section.SetParent(root, false);
            sections.Add(new SectionInfo { name = name, t = section, pathFrom = path.Count });
        }

        static void CloseSection()
        {
            if (sections.Count > 0) sections[sections.Count - 1].pathTo = path.Count;
        }

        static SectionInfo CurSection => sections[sections.Count - 1];

        static void Start(float x1, float top)
        {
            dir = 1;
            p = new Vector2(0f, top + HalfH + 0.01f); v = Vector2.zero;
            MakeLedge("Ledge_Start", -20f, x1, top, 8f); // 왼쪽 끝은 BuildEnclosure에서 벽까지 맞춘다
            float jumpX = x1 - 0.4f;
            var old = GameObject.Find("RespawnPoint");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var rp = new GameObject("RespawnPoint");
            rp.transform.position = new Vector3(0f, top + HalfH + 0.01f, 0f);
            steps.Add(new RouteStep { kind = RouteStepKind.RunJump, label = "start run-jump", dir = 1, jumpX = jumpX, standPos = rp.transform.position });
            RunAndJump(top, jumpX);
        }

        static void RunAndJump(float top, float jumpX)
        {
            for (int k = 0; k < 2000; k++)
            {
                if ((p.x - jumpX) * dir >= 0f) { StepGround(top, dir, true); break; }
                StepGround(top, dir, false);
            }
            flightStart = path.Count;
            FlyToApex();
        }

        static void AnchorOp(float d, float phiDeg, float th, string style)
        {
            float pr = phiDeg * Mathf.Deg2Rad;
            Vector2 a = p + d * new Vector2(Mathf.Cos(pr) * dir, Mathf.Sin(pr));
            AttachSwing(a, th, style);
        }

        static void AttachSwing(Vector2 a, float th, string style)
        {
            var anc = new Anchor { a = a, attachPos = p, style = style };
            anc.window = AttachWindow(a);
            anc.rope = (p - a).magnitude;
            anc.pathStart = path.Count;
            anc.label = $"T{tier}_A{anchors.Count:00}";
            steps.Add(new RouteStep
            {
                kind = RouteStepKind.Attach, label = anc.label, dir = dir, point = new Vector3(a.x, a.y, 0f),
                releaseAngle = th, swingInput = 1f, simAttachDistance = anc.rope, simAttachWindow = anc.window
            });
            SwingRelease(a, anc.rope, th);
            anc.pathRelease = path.Count;
            FlyToApex();
            anc.pathEnd = path.Count;
            anchors.Add(anc);
            var cs = CurSection; cs.anchors++; cs.minWindow = Mathf.Min(cs.minWindow, anc.window); cs.maxRope = Mathf.Max(cs.maxRope, anc.rope);
            MakeAnchorGeometry(anc, dir);
        }

        static void SwingRelease(Vector2 a, float L, float th)
        {
            float inp = dir;
            StepRope(a, L, inp); // 부착한 스텝 (봇은 이 스텝에 해제 판정을 하지 않는다)
            bool wasRising = false;
            for (int k = 0; k < 3000; k++)
            {
                Vector2 rr = p - a;
                float ang = Mathf.Atan2(rr.x * dir, -rr.y) * Mathf.Rad2Deg;
                bool taut = rr.magnitude >= L - 0.05f;
                if ((ang >= th && v.y > 0f) || (ang > 5f && v.y <= 0f && taut && wasRising))
                {
                    v *= T.releaseInertiaMultiplier;
                    v.y += T.jumpVelocity;
                    flightStart = path.Count;
                    StepFly(dir);
                    return;
                }
                if (ang > 0f && v.y > 0.05f && taut) wasRising = true;
                StepRope(a, L, inp);
            }
            throw new Exception("swing never released");
        }

        static void LedgeOp(float len, float drop, int newDir = 0)
        {
            float top = p.y - HalfH - drop;
            for (int k = 0; k < 2000; k++)
            {
                if (v.y < 0f && p.y - HalfH <= top) break;
                StepFly(dir);
            }
            float xl = p.x;
            p.y = top + HalfH; v.y = 0f;
            int nd = newDir == 0 ? dir : newDir;
            float near, far;
            if (nd == dir) { near = xl - 1.0f * dir; far = xl + len * dir; }
            else { near = xl + len * dir; far = xl - 1.2f * dir; } // 돌아서는 발판: 앞으로 뻗고, 착지한 쪽 끝에서 되돌아 뛴다
            int li = ledges.Count;
            string nm = $"T{tier}_Ledge_{li:00}";
            MakeLedge(nm, Mathf.Min(near, far), Mathf.Max(near, far), top, 2.5f);
            steps.Add(new RouteStep { kind = RouteStepKind.Land, label = "land " + nm, dir = dir });
            Record(Ph.Ground);
            dir = nd;
            float jumpX = far - 0.4f * nd;
            float standX = nd == 1 ? Mathf.Max(Mathf.Min(near, far) + 0.5f, jumpX - 3f) : Mathf.Min(Mathf.Max(near, far) - 0.5f, jumpX + 3f);
            steps.Add(new RouteStep { kind = RouteStepKind.RunJump, label = "run-jump " + nm, dir = nd, jumpX = jumpX, standPos = new Vector3(standX, top + HalfH + 0.01f, 0f) });
            RunAndJump(top, jumpX);
        }

        static void Summit(float len, float drop)
        {
            float top = p.y - HalfH - drop;
            for (int k = 0; k < 2000; k++)
            {
                if (v.y < 0f && p.y - HalfH <= top) break;
                StepFly(dir);
            }
            float xl = p.x;
            p.y = top + HalfH; v.y = 0f; Record(Ph.Ground);
            float x0 = Mathf.Min(xl - 1.5f * dir, xl + len * dir), x1 = Mathf.Max(xl - 1.5f * dir, xl + len * dir);
            MakeLedge("Summit_Plateau", x0, x1, top, 10f);
            steps.Add(new RouteStep { kind = RouteStepKind.Land, label = "land Summit", dir = dir });
            topY = top;

            var tree = new GameObject("AcornTree_Goal").transform;
            tree.SetParent(section, false);
            float tx = xl + len * 0.6f * dir;
            tree.position = new Vector3(tx, top, 1.0f);
            if (PlaceAcornTreeModel(tree, tx, top) == null)
            {
            // 모델이 없을 때만 예전 도형 나무 (콜라이더 없음)
            Prim(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(tx, top + 2.5f, 1.0f), new Vector3(1.0f, 2.5f, 1.0f), mTrunk, false);
            Prim(PrimitiveType.Sphere, "Canopy", tree, new Vector3(tx, top + 6.5f, 1.0f), new Vector3(7f, 5f, 5f), mLeaf, false);
            Prim(PrimitiveType.Sphere, "Acorn_1", tree, new Vector3(tx - 2.0f, top + 4.3f, -0.8f), new Vector3(0.7f, 0.85f, 0.7f), mAcorn, false);
            Prim(PrimitiveType.Sphere, "Acorn_2", tree, new Vector3(tx + 1.6f, top + 4.6f, -0.8f), new Vector3(0.7f, 0.85f, 0.7f), mAcorn, false);
            Prim(PrimitiveType.Sphere, "Acorn_3", tree, new Vector3(tx + 0.2f, top + 4.0f, -1.2f), new Vector3(0.7f, 0.85f, 0.7f), mAcorn, false);
            }
            // 골인 트리거 (정상 발판 윗면 바로 위 얇은 층, 나무 왼쪽 7 m ~ 발판 오른쪽 끝). GameUIBuilder와 같은 규칙.
            GameUIBuilder.CreateGoalTrigger(tree, new Bounds(new Vector3((x0 + x1) * 0.5f, top - 5f, 0f), new Vector3(x1 - x0, 10f, 4f)), register: false);
            log.AppendLine($"Summit plateau top y={top:F1}, x {x0:F1}..{x1:F1}, tree at x={tx:F1}");
        }

        public const string AcornTreeModelPath = "Assets/_Game/Art/Environment/AcornTree/AcornTree.fbx";
        public const float AcornTreeHeight = 9f;   // m. 모델 피벗이 밑동이라 top에 그대로 세운다
        public const float AcornTreeZ = 1.2f;      // 플레이 평면(z 0)보다 살짝 뒤

        /// <summary>
        /// 정상 나무 모델(AcornTree.fbx)을 tree 아래에 (tx, top, AcornTreeZ)로 세운다. 콜라이더 · PlungerAttachable 없음(표시만):
        /// 마지막 앵커 · 착지 경로와 뚫어뻥 Raycast를 막지 않는다. 모델이 없으면 null.
        /// </summary>
        public static GameObject PlaceAcornTreeModel(Transform tree, float tx, float top)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AcornTreeModelPath);
            if (model == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, tree);
            go.name = "AcornTree_Model";
            go.transform.position = new Vector3(tx, top, AcornTreeZ);
            go.transform.localRotation = model.transform.localRotation; // FBX 축 보정(루트 회전)은 그대로
            go.transform.localScale = model.transform.localScale;
            float h = 1f;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.y > 1e-4f) h = b.size.y;
            }
            go.transform.localScale = model.transform.localScale * (AcornTreeHeight / h);
            if (rs.Length > 0)
            {
                // 밑동이 정상 발판 윗면(top)에 오고, 가로 중심이 (tx, AcornTreeZ)에 오게 맞춘다
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += new Vector3(tx - b.center.x, top - b.min.y, AcornTreeZ - b.center.z);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------ 둘레: 벽 · 시작 분지 · 안전망

        static void BuildEnclosure()
        {
            var parent = new GameObject("Enclosure").transform;
            parent.SetParent(root, false);
            float xmin = float.PositiveInfinity, xmax = float.NegativeInfinity;
            foreach (var q in path) { xmin = Mathf.Min(xmin, q.x); xmax = Mathf.Max(xmax, q.x); }
            foreach (var l in ledges) { if (l.name == "Ledge_Start") continue; xmin = Mathf.Min(xmin, l.x0); xmax = Mathf.Max(xmax, l.x1); }
            wallL = Mathf.Min(xmin - 1.6f, -8f);
            wallR = xmax + 1.6f;
            float wallTop = topY + 25f;

            // 벽 가까이 끝나는 발판은 벽까지 늘려 좁은 틈을 없앤다
            foreach (var l in ledges)
            {
                bool changed = false;
                if (l.x0 - wallL < 5f) { l.x0 = wallL; changed = true; }
                if (wallR - l.x1 < 5f) { l.x1 = wallR; changed = true; }
                if (changed)
                {
                    l.t.position = new Vector3((l.x0 + l.x1) * 0.5f, (l.top + l.bottom) * 0.5f, 0f);
                    l.t.localScale = new Vector3(l.x1 - l.x0, l.top - l.bottom, Depth);
                }
            }

            const float WT = 6f;
            Box("Wall_Left", parent, new Vector3(wallL - WT * 0.5f, (wallTop - 20f) * 0.5f, 0f), new Vector3(WT, wallTop + 20f, Depth), mLevel, true, false);
            Box("Wall_Right", parent, new Vector3(wallR + WT * 0.5f, (wallTop - 20f) * 0.5f, 0f), new Vector3(WT, wallTop + 20f, Depth), mLevel, true, false);

            // 시작 분지: 시작 발판 오른쪽 끝(x 6)에서 오른쪽 벽까지 5° 오르막. 떨어진 몸은 여기 서서 걸어서 시작점으로 간다.
            var startLedge = ledges.Find(l => l.name == "Ledge_Start");
            float sx = startLedge.x1;
            float hR = Mathf.Tan(5f * Mathf.Deg2Rad) * (wallR - sx);
            Poly("Floor_Basin", parent, new[] { new Vector2(sx, -8f), new Vector2(wallR, -8f), new Vector2(wallR, hR), new Vector2(sx, 0f) }, mLevel, false);

            // 안전망: 벽 바깥 · 아주 아래. 레벨을 빠져나갔을 때만 닿는다 → 시작점으로
            KillBox("SafetyNet_Left", parent, wallL - 60f, wallL - WT - 2f, wallTop + 60f, wallTop + 140f);
            KillBox("SafetyNet_Right", parent, wallR + WT + 2f, wallR + 60f, wallTop + 60f, wallTop + 140f);
            KillBox("SafetyNet_Bottom", parent, wallL - 60f, wallR + 60f, -40f, 20f);
            log.AppendLine($"Enclosure: walls x {wallL:F1} / {wallR:F1} (width {wallR - wallL:F0} m), height to {wallTop:F0}, basin rises {hR:F1} m to the right wall");
        }

        static void BuildBackdrop()
        {
            var parent = new GameObject("Backdrop_Mountain").transform;
            parent.SetParent(root, false);
            const float W = 10f;
            int i = 0;
            float cx = (wallL + wallR) * 0.5f, half = (wallR - wallL) * 0.5f + 70f;
            for (float x = cx - half; x < cx + half; x += W, i++)
            {
                float t = Mathf.Abs((x + W * 0.5f) - cx) / half;   // 0 = 가운데
                float top = Mathf.Lerp(topY + 30f, 20f, t * t) + Mathf.PerlinNoise(x * 0.07f, 0.3f) * 12f;
                Prim(PrimitiveType.Cube, $"Backdrop_{i:00}", parent, new Vector3(x + W * 0.5f, (top - 40f) * 0.5f, 14f), new Vector3(W + 0.5f, top + 40f, 2f), mBackdrop, false);
            }
        }

        // ------------------------------------------------------------------ 지오메트리

        static Ledge MakeLedge(string name, float x0, float x1, float top, float thick)
        {
            var l = new Ledge { x0 = x0, x1 = x1, top = top, bottom = top - thick, name = name };
            l.t = Box(name, section, new Vector3((x0 + x1) * 0.5f, top - thick * 0.5f, 0f), new Vector3(x1 - x0, thick, Depth), mLevel, true, false).transform;
            ledges.Add(l);
            return l;
        }

        /// <summary>앵커 = 밑면에 부착 패치가 박힌 뾰족 지붕 바위. 지붕이 55°라 위에 설 수 없어 떨어지는 몸은 미끄러져 내려간다.</summary>
        static void MakeAnchorGeometry(Anchor anc, int d)
        {
            Vector2 a = anc.a;
            float rockW, patchW, offset = 0f;
            switch (anc.style)
            {
                case "Knob": rockW = 1.8f; patchW = 1.3f; break;
                case "Patch": rockW = 2.0f; patchW = 0.55f; break;
                case "Slim": rockW = 1.4f; patchW = 0.45f; break;
                case "Stun": rockW = 4.6f; patchW = 0.5f; offset = 1.6f * d; break; // 진행 방향으로 넓은 바위: 늦게 놓으면 머리를 박는다
                default: rockW = 2f; patchW = 0.55f; break;
            }
            var g = new GameObject($"Crag_{anc.label}").transform;
            g.SetParent(section, false);
            g.position = new Vector3(a.x, a.y, 0f);
            Peak("Rock", g, new Vector2(a.x + offset, a.y + 0.12f), rockW, mLevel);
            Box("Patch", g, new Vector3(a.x, a.y + 0.15f, 0f), new Vector3(patchW, 0.3f, Depth * 0.9f), mAtt, true, true);
        }

        static void Peak(string name, Transform parent, Vector2 bottomCenter, float width, Material m)
        {
            float hw = width * 0.5f, hs = 0.3f, pk = hw * Mathf.Tan(PeakDeg * Mathf.Deg2Rad);
            var pts = new[] { new Vector2(-hw, 0f), new Vector2(hw, 0f), new Vector2(hw, hs), new Vector2(0f, hs + pk), new Vector2(-hw, hs) };
            var go = PolyLocal(name, parent, pts, m, $"peak_{width:F2}");
            go.transform.position = new Vector3(bottomCenter.x, bottomCenter.y, 0f);
        }

        static GameObject Poly(string name, Transform parent, Vector2[] worldPts, Material m, bool attachable)
        {
            var go = PolyLocal(name, parent, worldPts, m, null);
            go.transform.position = Vector3.zero;
            if (attachable) go.AddComponent<PlungerAttachable>();
            return go;
        }

        /// <summary>볼록 다각형(반시계)을 z로 Depth만큼 늘인 프리즘. MeshCollider(convex).</summary>
        static GameObject PolyLocal(string name, Transform parent, Vector2[] pts, Material m, string cacheKey)
        {
            Mesh mesh = null;
            if (cacheKey != null) meshCache.TryGetValue(cacheKey, out mesh);
            if (mesh == null)
            {
                mesh = PrismMesh(pts, Depth);
                mesh.name = cacheKey ?? name;
                meshes.Add(mesh);
                if (cacheKey != null) meshCache[cacheKey] = mesh;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
            go.isStatic = true;
            return go;
        }

        static Mesh PrismMesh(Vector2[] pts, float depth)
        {
            float zf = -depth * 0.5f, zb = depth * 0.5f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            int n = pts.Length;
            int f0 = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(new Vector3(pts[i].x, pts[i].y, zf)); norms.Add(Vector3.back); }
            for (int i = 1; i + 1 < n; i++) { tris.Add(f0); tris.Add(f0 + i + 1); tris.Add(f0 + i); }
            int b0 = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(new Vector3(pts[i].x, pts[i].y, zb)); norms.Add(Vector3.forward); }
            for (int i = 1; i + 1 < n; i++) { tris.Add(b0); tris.Add(b0 + i); tris.Add(b0 + i + 1); }
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % n];
                Vector2 e = b - a; Vector3 nm = new Vector3(e.y, -e.x, 0f).normalized;
                int s = verts.Count;
                verts.Add(new Vector3(a.x, a.y, zf)); verts.Add(new Vector3(b.x, b.y, zf));
                verts.Add(new Vector3(b.x, b.y, zb)); verts.Add(new Vector3(a.x, a.y, zb));
                for (int k = 0; k < 4; k++) norms.Add(nm);
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void SaveMeshes()
        {
            if (meshes.Count == 0) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Level")) AssetDatabase.CreateFolder("Assets/_Game", "Level");
            AssetDatabase.CreateAsset(meshes[0], MeshAssetPath);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], MeshAssetPath);
            AssetDatabase.SaveAssets();
        }

        static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material m, bool collider, bool attachable)
        {
            var go = Prim(PrimitiveType.Cube, name, parent, center, size, m, collider);
            if (attachable) go.AddComponent<PlungerAttachable>();
            return go;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 center, Vector3 size, Material m, bool collider)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = true;
            return go;
        }

        static void KillBox(string name, Transform parent, float x0, float x1, float top, float h)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((x0 + x1) * 0.5f, top - h * 0.5f, 0f);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(x1 - x0, h, 6f);
            bc.isTrigger = true;
            go.AddComponent<KillZone>();
        }

        static Material EnsureMat(string name, Color c)
        {
            string mp = $"Assets/_Game/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(m, mp);
            return m;
        }

        // ------------------------------------------------------------------ 검사 · 요약

        static void CheckPath()
        {
            int hits = 0;
            var seen = new HashSet<string>();
            for (int i = 0; i < path.Count; i++)
            {
                if (phase[i] == Ph.Ground) continue;
                bool nearGround = false;
                for (int k = i - 3; k <= i + 4; k++) if (k >= 0 && k < path.Count && phase[k] == Ph.Ground) nearGround = true;
                if (nearGround) continue;
                Vector3 c = new Vector3(path[i].x, path[i].y, 0f);
                var cols = Physics.OverlapCapsule(c + Vector3.up * 0.3f, c - Vector3.up * 0.3f, 0.36f, ~0, QueryTriggerInteraction.Collide);
                foreach (var col in cols)
                {
                    string key = col.transform.parent != null ? col.transform.parent.name + "/" + col.name : col.name;
                    if (seen.Add(key + "@" + (i / 25)))
                    {
                        hits++;
                        if (hits < 40) log.AppendLine($"  PATH HIT {key} at {path[i]} phase {phase[i]} idx {i}");
                    }
                }
            }
            foreach (var a in anchors)
            {
                Vector3 o = new Vector3(a.attachPos.x, a.attachPos.y, 0f);
                Vector3 t = new Vector3(a.a.x, a.a.y, 0f);
                if (Physics.Raycast(o, (t - o).normalized, out var h, T.plungerRange, ~0, QueryTriggerInteraction.Ignore))
                {
                    bool att = h.collider.GetComponentInParent<PlungerAttachable>() != null;
                    if (!att || Mathf.Abs(h.distance - a.rope) > 0.2f) log.AppendLine($"  RAY {a.label}: hit {h.collider.name} att={att} dist {h.distance:F2} vs {a.rope:F2}");
                }
                else log.AppendLine($"  RAY {a.label}: no hit (rope {a.rope:F2})");
            }
            log.AppendLine($"path collision hits: {hits}");
        }

        static void Summary()
        {
            log.AppendLine($"steps {steps.Count}, anchors {anchors.Count}, ledges {ledges.Count}, tiers {tier}, path points {path.Count}");
            foreach (var s in sections)
            {
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity, x0 = float.PositiveInfinity, x1 = float.NegativeInfinity;
                for (int i = s.pathFrom; i < s.pathTo && i < path.Count; i++) { lo = Mathf.Min(lo, path[i].y); hi = Mathf.Max(hi, path[i].y); x0 = Mathf.Min(x0, path[i].x); x1 = Mathf.Max(x1, path[i].x); }
                log.AppendLine($"{s.name}: tiers {s.tiers} y {lo:F1}..{hi:F1}  x {x0:F1}..{x1:F1}  anchors {s.anchors}  maxRope {s.maxRope:F2}  minAttachWindow {s.minWindow:F2}s");
            }
            foreach (var l in ledges) log.Append($"{l.name}[{l.x0:F0}..{l.x1:F0} @{l.top:F1}] ");
            log.AppendLine();
        }
    }
}
