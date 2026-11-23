using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pop.EditorTools
{
    /// <summary>
    /// Mountain 레벨의 회색 상자 겉모습을 VARCO 환경 모델로 바꾼다 (Phase B).
    /// 콜라이더 · PlungerAttachable · 위치 · 부모는 건드리지 않는다. 바꾸는 오브젝트마다
    /// 자식 "EnvVisual"(콜라이더 없음, static)을 더하고 원래 MeshRenderer만 끈다.
    /// 다시 실행하면 이전 EnvVisual을 지우고 새로 만든다(멱등). 열린 씬에만 작용한다.
    ///
    /// 메뉴
    ///   Pop/Dev/Env Visuals/Setup Assets   — FBX 가져오기 설정, 머티리얼(URP Lit) 만들기
    ///   Pop/Dev/Apply Env Visuals          — 열린 씬에 적용 + 맞춤 검사 로그
    ///   Pop/Dev/Env Visuals/Fit Check      — 맞춤 검사만
    ///   Pop/Dev/Env Visuals/Remove         — EnvVisual 지우고 원래 렌더러 다시 켜기
    ///
    /// 대응 (Phase A 목록):
    ///   Crag_*/Rock (Peak 메시)            → Env_WedgeRock_Grey, Rock 메시 범위에 맞춤 · 바닥 맞춤
    ///   Crag_*/Patch                       → 그대로(사용자 결정: M_Attachable 상자가 보이게 둔다)
    ///   T*_Ledge_* 보통 발판(4.5~7.8×2.5×4) → Env_LogLedge (폭 6 m 넘으면 2개), 평평한 윗면 = 상자 윗면
    ///   T*_Ledge_* 세로로 긴 상자(h/w > 2)   → 높이 20 m 이하 Env_TrunkPillar, 그 위 Env_RockPillar 3개 쌓기
    ///   Summit_Plateau, Ledge_Start         → Env_MesaPlateau
    ///   Wall_Left/Right                     → Env_CliffWall 세로 타일
    ///   Floor_Basin                         → Env_MesaPlateau 조각을 경사에 맞춰 돌려 잇고(윗줄), 그 아래를 수평 메사로 메움(아랫줄).
    ///                                          Ledge_Start와 같은 겉모습. (BasinUsesMesa = false면 예전 방식: 같은 모양 메시, 윗면 · 가장자리 띠 풀,
    ///                                          옆 · 앞은 바위(절벽 돌) — 월드 좌표 UV, Mirror 타일 텍스처)
    ///   Backdrop_Mountain/Backdrop_*        → 앞면만 있는 절벽 면(울퉁불퉁 z, 산 능선 윤곽), 어둡고 푸르스름한 절벽 돌
    ///   도토리 나무, 골인                   → 그대로
    ///   Pop/Dev/Env Visuals/Audit           — 아직 켜진 회색 상자 렌더러 목록
    /// </summary>
    public static class EnvVisualsApplier
    {
        public const string VisualName = "EnvVisual";
        const string Root = "Assets/_Game/Art/Environment/Mountain/";

        const string Wedge = "Env_WedgeRock_Grey";
        const string Log = "Env_LogLedge";
        const string Trunk = "Env_TrunkPillar";
        const string RockPillar = "Env_RockPillar";
        const string Mesa = "Env_MesaPlateau";
        const string Cliff = "Env_CliffWall";
        static readonly string[] Models = { Wedge, Log, Trunk, RockPillar, Mesa, Cliff };

        // 서 있는 윗면 높이 = 모델 바닥 + 높이 × 이 비율. 메시를 재서 정한 값(Setup Assets 로그 flatTop 참고).
        //   통나무: 윗면을 길이 방향으로 수평하게 고르고 +z 끝 그루터기를 깎아낸 모델(가운데 줄 0.933~0.941, 중앙값 0.938). 1.0은 윗면 한쪽 옆 가장자리 껍질 턱
        //   나무 기둥 · 메사: 윗면 0.99~1.0
        //   바위 기둥: 윗면이 울퉁불퉁(0.93~1.0)해서 가운데 · 맨 위 조각은 뒤집어 평평한 바닥을 윗면으로 쓴다 → 1.0
        static readonly Dictionary<string, float> FlatTopFrac = new Dictionary<string, float>
        {
            { Log, 0.938f }, { Trunk, 0.995f }, { RockPillar, 1f }, { Mesa, 0.995f }, { Wedge, 1f }, { Cliff, 1f },
        };

        // 서는 윗면의 가로(x) 폭 / 모델 전체 폭. 맞춤 검사에서 "안쪽으로 들어감"을 잴 때 쓴다.
        //   나무 기둥은 밑동이 벌어져서 몸통(윗면) 폭이 전체의 0.823
        static readonly Dictionary<string, float> TopWidthFrac = new Dictionary<string, float>
        {
            { Log, 0.983f }, { Trunk, 0.834f }, { RockPillar, 0.97f }, { Mesa, 0.982f }, { Wedge, 1f }, { Cliff, 1f },
        };

        const float SingleLogMaxWidth = 6.01f;   // 이보다 넓은 발판은 통나무 2개를 끝과 끝으로 맞댄다
        const float LogJoinOverlap = 0.10f;      // 두 통나무가 맞닿는 곳 겹침(틈 안 보이게)
        const float StackOverlap = 0.15f;        // 쌓은 바위 기둥 · 절벽 타일 겹침
        const float CliffTileTargetH = 10.9f;    // 절벽 타일 높이(폭 6 m에서 모델 비율 그대로 = 6/0.55)
        const float TrunkTopInset = 0.05f;       // 나무 기둥 윗면(몸통)이 상자 옆면보다 한쪽에서 들어가는 양
        const float LevelMidX = 32.85f;          // 좌우 벽 가운데(통나무 그루터기를 가까운 벽 쪽 끝으로)
        const float MesaEdgeOverhang = 0.55f;    // 메사를 양옆으로 넓히는 양(둥근 윗면 가장자리 때문)
        const float TrunkBodyCenterX = -0.0237f; // 나무 기둥 몸통 가운데(모델 폭 비율, -x로 치우침)
        const float RockPillarJoinFrac = 0.09f;  // 바위 기둥 조각끼리 겹치는 높이 비율(울퉁불퉁한 끝 7%를 묻음)

        // ───────────────────────── Setup ─────────────────────────

        [MenuItem("Pop/Dev/Env Visuals/Setup Assets")]
        public static void SetupAssets()
        {
            var log = new StringBuilder("[EnvVisuals] Setup Assets\n");
            Directory.CreateDirectory(Root + "Materials");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var n in Models)
            {
                string fbx = Root + "Models/" + n + ".fbx";
                var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
                if (imp == null) { log.AppendLine("  없음: " + fbx); continue; }

                string texDir = Root + "Textures/" + n + "/";
                if (!File.Exists(texDir + "base_color.png"))
                {
                    Directory.CreateDirectory(texDir);
                    imp.ExtractTextures(texDir);
                    AssetDatabase.Refresh();
                }
                SetTex(texDir + "base_color.png", TextureImporterType.Default, true);
                SetTex(texDir + "normal.png", TextureImporterType.NormalMap, false);
                SetTex(texDir + "orm.png", TextureImporterType.Default, false);
                string ms = texDir + "metallic_smoothness.png";
                if (!File.Exists(ms)) MakeMetallicSmoothness(texDir + "orm.png", ms);
                SetTex(ms, TextureImporterType.Default, false);

                string matPath = Root + "Materials/M_" + n.Substring(4) + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(lit);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.shader = lit;
                var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "base_color.png");
                mat.SetTexture("_BaseMap", baseTex);
                mat.SetTexture("_MainTex", baseTex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "normal.png"));
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ms));
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Smoothness", 0.6f);   // M_AcornTree와 같음(맵 알파에 곱해짐)
                EditorUtility.SetDirty(mat);

                imp.bakeAxisConversion = true;       // 루트 회전 없이 Y 위 · 바닥 가운데 원점
                imp.addCollider = false;
                imp.isReadable = false;
                imp.importAnimation = false;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importBlendShapes = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>())
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), o.name), mat);
                imp.SaveAndReimport();
                log.AppendLine("  " + n + " → " + matPath);
            }
            AssetDatabase.SaveAssets();

            foreach (var n in Models)
            {
                var prefab = LoadModel(n);
                if (prefab == null) continue;
                var mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
                log.AppendLine(string.Format("  {0}: rot={1} meshBounds={2} flatTop≈{3:F3}", n,
                    prefab.transform.localEulerAngles, mesh.bounds, MeasureFlatTopFrac(prefab)));
            }
            Debug.Log(log.ToString());
        }

        static void SetTex(string path, TextureImporterType type, bool srgb)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { AssetDatabase.ImportAsset(path); ti = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (ti == null) return;
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.maxTextureSize == 2048) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }

        // M_AcornTree와 같은 방법: R = 금속(ORM의 B), A = 1 - 거칠기(ORM의 G).
        static void MakeMetallicSmoothness(string ormPath, string outPath)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            src.LoadImage(File.ReadAllBytes(ormPath));
            var px = src.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color32(c.b, 0, 0, (byte)(255 - c.g));
            }
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
            dst.SetPixels32(px);
            File.WriteAllBytes(outPath, dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(outPath);
        }

        static GameObject LoadModel(string n)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/" + n + ".fbx");
        }

        // 위쪽을 향한 삼각형 면적이 꼭대기 40% 안에서 가장 큰 높이(0~1). 참고용.
        static float MeasureFlatTopFrac(GameObject prefab)
        {
            var mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            Vector3[] v;
            int[] tri;
            try { v = mesh.vertices; tri = mesh.triangles; } catch { return 1f; }
            var m = Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale);
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            float minY = v.Min(p => p.y), maxY = v.Max(p => p.y), h = maxY - minY;
            const int bins = 100;
            var area = new float[bins];
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                var cr = Vector3.Cross(b - a, c - a);
                float ar = cr.magnitude * 0.5f;
                if (ar <= 0f || cr.normalized.y < 0.9f) continue;
                float y = (a.y + b.y + c.y) / 3f;
                area[Mathf.Clamp((int)((y - minY) / h * bins), 0, bins - 1)] += ar;
            }
            int best = bins - 1; float bestA = -1f;
            for (int i = (int)(bins * 0.6f); i < bins; i++) if (area[i] > bestA) { bestA = area[i]; best = i; }
            return (best + 0.5f) / bins;
        }

        // ───────────────────────── Apply ─────────────────────────

        public class Job
        {
            public Transform target;     // 콜라이더를 가진 원래 오브젝트(EnvVisual을 이 밑에 둔다)
            public string kind;
            public Bounds box;           // 맞출 범위(콜라이더 월드 범위)
        }

        [MenuItem("Pop/Dev/Apply Env Visuals")]
        public static void ApplyMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[EnvVisuals] Play 중에는 실행하지 않는다."); return; }
            Apply();
        }

        public static string Apply()
        {
            var log = new StringBuilder("[EnvVisuals] Apply Env Visuals — " + EditorSceneManager.GetActiveScene().path + "\n");
            var skipped = new List<string>();
            var jobs = CollectJobs(skipped);
            var prefabs = Models.ToDictionary(n => n, n => LoadModel(n));
            EnsureTileAssets();
            PrepareBackdropProfile(jobs);
            if (prefabs.Values.Any(p => p == null)) { Debug.LogError("[EnvVisuals] 모델이 없다. Setup Assets 먼저."); return "missing models"; }

            Undo.SetCurrentGroupName("Apply Env Visuals");
            int group = Undo.GetCurrentGroup();
            var counts = new Dictionary<string, int>();
            int pieces = 0;
            foreach (var j in jobs)
            {
                var container = MakeContainer(j.target);
                switch (j.kind)
                {
                    case "Wedge": BuildWedge(container, j, prefabs[Wedge]); break;
                    case "Log": BuildLogs(container, j, prefabs[Log]); break;
                    case "Trunk": BuildTrunk(container, j, prefabs[Trunk]); break;
                    case "RockPillar": BuildRockPillar(container, j, prefabs[RockPillar]); break;
                    case "Mesa": BuildMesa(container, j, prefabs[Mesa]); break;
                    case "Cliff": BuildCliff(container, j, prefabs[Cliff]); break;
                    case "Ground": if (BasinUsesMesa) BuildBasinMesa(container, j, prefabs[Mesa]); else BuildGround(container, j); break;
                    case "Backdrop": BuildBackdrop(container, j); break;
                }
                Finish(container.gameObject);
                pieces += container.childCount;
                var mr = j.target.GetComponent<MeshRenderer>();
                if (mr != null && mr.enabled) { Undo.RecordObject(mr, "Disable grey-box renderer"); mr.enabled = false; }
                int k;
                counts[j.kind] = (counts.TryGetValue(j.kind, out k) ? k : 0) + 1;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            log.AppendLine("  바꾼 것: " + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value).ToArray()) + " (모델 조각 " + pieces + "개)");
            log.AppendLine("  규칙에 안 맞아 그대로 둔 것(" + skipped.Count + "): " + string.Join(", ", skipped.ToArray()));
            log.Append(FitReport(jobs));
            Debug.Log(log.ToString());
            return log.ToString();
        }

        public static List<Job> CollectJobs(List<string> skipped)
        {
            var jobs = new List<Job>();
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (IsUnderVisual(t)) continue;
                if (HasAncestor(t, "Player") || HasAncestor(t, "GameUI") || HasAncestor(t, "AcornTree_Goal")) continue;
                var mr = t.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var col = t.GetComponent<Collider>();
                string n = t.name;

                // 손대지 않는 것
                if (n == "Patch" && t.parent != null && t.parent.name.StartsWith("Crag_")) continue; // 사용자 결정: 그대로
                if (n.StartsWith("Backdrop_") && t.parent != null && t.parent.name == "Backdrop_Mountain" && t.GetComponent<MeshFilter>() != null)
                {
                    jobs.Add(new Job { target = t, kind = "Backdrop", box = MeshWorldBounds(t) });
                    continue;
                }
                if (n == "Floor_Basin" && col != null && !col.isTrigger && t.GetComponent<MeshFilter>() != null)
                {
                    jobs.Add(new Job { target = t, kind = "Ground", box = col.bounds });
                    continue;
                }
                if (n == "Patch" && t.parent != null && t.parent.name.StartsWith("Crag_")) continue; // 사용자 결정: 그대로
                if (col == null || col.isTrigger) { skipped.Add(PathOf(t) + "(콜라이더 없음)"); continue; }

                var b = col.bounds;
                string kind = null;
                if (n == "Rock" && t.parent != null && t.parent.name.StartsWith("Crag_")) kind = "Wedge";
                else if (n == "Summit_Plateau" || n == "Ledge_Start") kind = "Mesa";
                else if (n == "Wall_Left" || n == "Wall_Right") kind = "Cliff";
                else if (n.StartsWith("T") && n.Contains("_Ledge_") && col is BoxCollider)
                {
                    float w = b.size.x, h = b.size.y;
                    if (h / w > 2f)
                        kind = h > 20f ? "RockPillar" : "Trunk";
                    else if (w >= 4.4f && w <= 8f && Mathf.Abs(h - 2.5f) < 0.05f && Mathf.Abs(b.size.z - 4f) < 0.05f)
                        kind = "Log";
                }
                if (kind == null) { skipped.Add(PathOf(t)); continue; }
                jobs.Add(new Job { target = t, kind = kind, box = b });
            }
            return jobs;
        }

        static bool IsUnderVisual(Transform t)
        {
            for (var p = t; p != null; p = p.parent) if (p.name == VisualName) return true;
            return false;
        }

        static bool HasAncestor(Transform t, string name)
        {
            for (var p = t; p != null; p = p.parent) if (p.name == name) return true;
            return false;
        }

        static string PathOf(Transform t)
        {
            return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        }

        // 이전 EnvVisual을 지우고, 월드 크기 1 · 회전 0인 빈 컨테이너를 만든다.
        static Transform MakeContainer(Transform target)
        {
            for (int i = target.childCount - 1; i >= 0; i--)
            {
                var c = target.GetChild(i);
                if (c.name == VisualName) Undo.DestroyObjectImmediate(c.gameObject);
            }
            var go = new GameObject(VisualName);
            Undo.RegisterCreatedObjectUndo(go, "EnvVisual");
            go.transform.SetParent(target, false);
            var ls = target.lossyScale;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Inverse(target.rotation);
            go.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            return go.transform;
        }

        const StaticEditorFlags StaticFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
            | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        static void Finish(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticFlags);
        }

        // 모델 하나를 놓는다. rot = 모델 회전(90° 단위), size = 렌더러 범위 크기(컨테이너 = 월드 축 기준).
        // anchorTop이면 "서는 윗면"(바닥 + 높이 × flatTop)을 y에, 아니면 바닥을 y에 맞춘다. 가로 · 깊이는 center에 맞춘다.
        static Transform Place(Transform container, GameObject prefab, string model, Quaternion rot, Vector3 size,
            Vector3 center, float y, bool anchorTop, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
            go.name = name;
            var t = go.transform;
            t.localRotation = rot * prefab.transform.localRotation;
            t.localPosition = Vector3.zero;

            var mb = go.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            var scale = Vector3.one;
            for (int i = 0; i < 3; i++)
            {
                var ax = Vector3.zero; ax[i] = 1f;
                var w = t.localRotation * ax;
                int wi = Mathf.Abs(w.x) > 0.5f ? 0 : Mathf.Abs(w.y) > 0.5f ? 1 : 2;
                scale[i] = size[wi] / Mathf.Max(1e-5f, mb.size[i]);
            }
            t.localScale = scale;

            var wb = LocalBounds(container, go);
            float frac;
            if (!FlatTopFrac.TryGetValue(model, out frac)) frac = 1f;
            var pos = new Vector3(center.x - wb.center.x, 0f, center.z - wb.center.z);
            pos.y = anchorTop ? y - (wb.min.y + wb.size.y * frac) : y - wb.min.y;
            t.localPosition = pos;
            return t;
        }

        static Quaternion RotY(float deg) { return Quaternion.Euler(0f, deg, 0f); }

        // 컨테이너 좌표(월드와 같은 축 · 크기, 원점만 다름)에서 렌더러 범위
        static Bounds LocalBounds(Transform container, GameObject go)
        {
            var b = go.GetComponentInChildren<Renderer>().bounds;
            return new Bounds(b.center - container.position, b.size);
        }

        static Vector3 Rel(Transform c, Vector3 world) { return world - c.position; }

        static void BuildWedge(Transform c, Job j, GameObject prefab)
        {
            // Rock 메시 범위(폭 · 높이 · 깊이 4)에 맞추고 바닥 맞춤. 능선 = 깊이 방향(모델 그대로).
            // 주어진 바위 모델 지붕은 바깥으로 볼록해서 범위에 꽉 맞추면 Rock 지붕(55°)보다 높이 튀어나온다(큰 바위에서 0.4~0.6 m).
            // Rock 콜라이더와 레이로 비교해 지붕 높이의 중앙값이 같아지도록 높이(y)만 줄인다(바닥 · 폭 · 깊이는 그대로).
            var b = j.box;
            var t = Place(c, prefab, Wedge, Quaternion.identity, b.size, Rel(c, b.center), Rel(c, b.min).y, false, "WedgeRock");
            var col = j.target.GetComponent<Collider>();
            for (int it = 0; it < 3; it++)
            {
                float k = RoofRatio(t, col, b);
                if (Mathf.Abs(k - 1f) < 0.005f) break;
                var s = t.localScale; s.y *= k; t.localScale = s;
                var wb = LocalBounds(c, t.gameObject);
                var p = t.localPosition; p.y += Rel(c, b.min).y - wb.min.y; t.localPosition = p;
            }
        }

        // 콜라이더 지붕 높이 / 겉모습 지붕 높이(바닥부터)의 중앙값. z = 0, 폭의 가운데 80%에서 잰다.
        static float RoofRatio(Transform vis, Collider col, Bounds b)
        {
            var vs = EditorSceneManager.NewPreviewScene();
            var cs = EditorSceneManager.NewPreviewScene();
            try
            {
                var mf = vis.GetComponentInChildren<MeshFilter>();
                var g = new GameObject("v");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, vs);
                g.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                g.transform.localScale = mf.transform.lossyScale;
                g.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                var h = new GameObject("c");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(h, cs);
                h.transform.SetPositionAndRotation(col.transform.position, col.transform.rotation);
                h.transform.localScale = col.transform.lossyScale;
                EditorUtility.CopySerialized(col, h.AddComponent(col.GetType()));
                Physics.SyncTransforms();
                var ratios = new List<float>();
                for (float x = b.min.x + b.size.x * 0.1f; x <= b.max.x - b.size.x * 0.1f; x += b.size.x * 0.02f)
                {
                    var o = new Vector3(x, b.max.y + 3f, 0f);
                    RaycastHit hv, hc;
                    if (!cs.GetPhysicsScene().Raycast(o, Vector3.down, out hc, b.size.y + 6f)) continue;
                    if (!vs.GetPhysicsScene().Raycast(o, Vector3.down, out hv, b.size.y + 6f)) continue;
                    float vh = hv.point.y - b.min.y, ch = hc.point.y - b.min.y;
                    if (vh > 0.05f) ratios.Add(ch / vh);
                }
                if (ratios.Count == 0) return 1f;
                ratios.Sort();
                return ratios[ratios.Count / 2];
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(vs);
                EditorSceneManager.ClosePreviewScene(cs);
            }
        }

        static void BuildLogs(Transform c, Job j, GameObject prefab)
        {
            // 통나무 길이 축(모델 z)을 Y로 90° 돌려 발판 폭(x) 방향으로. 평평한 윗면 = 상자 윗면, 아래는 상자 바닥까지.
            // 그루터기(모델 +z 끝)는 바깥 끝으로: 1개면 가까운 벽 쪽, 2개면 양 끝.
            var b = j.box;
            int n = b.size.x > SingleLogMaxWidth ? 2 : 1;
            float seg = (b.size.x + LogJoinOverlap * (n - 1)) / n;
            float frac = FlatTopFrac[Log];
            for (int i = 0; i < n; i++)
            {
                float x0 = b.min.x + i * (seg - LogJoinOverlap);
                var center = new Vector3(x0 + seg * 0.5f, b.center.y, b.center.z);
                bool stubPlusX = n == 2 ? i == 1 : b.center.x > LevelMidX;
                var size = new Vector3(seg, b.size.y / frac, b.size.z);
                Place(c, prefab, Log, RotY(stubPlusX ? 90f : -90f), size, Rel(c, center), Rel(c, b.max).y, true,
                    n == 1 ? "LogLedge" : "LogLedge_" + i);
            }
        }

        static void BuildTrunk(Transform c, Job j, GameObject prefab)
        {
            // 밑동이 벌어진 모양(z = 0에서 몸통 폭 = 전체의 0.834, 몸통 가운데가 -x로 폭의 2.4% 치우침):
            // 몸통(윗면) 폭 = 상자 폭 - 2×TrunkTopInset, 몸통 가운데 = 상자 가운데. 밑동은 약 0.15 m 바깥으로 나온다.
            var b = j.box;
            float r = TopWidthFrac[Trunk];
            var size = new Vector3((b.size.x - 2f * TrunkTopInset) / r, b.size.y / FlatTopFrac[Trunk], (b.size.z - 2f * TrunkTopInset) / r);
            var center = b.center - new Vector3(TrunkBodyCenterX * size.x, 0f, 0f);
            Place(c, prefab, Trunk, Quaternion.identity, size, Rel(c, center), Rel(c, b.max).y, true, "TrunkPillar");
        }

        static void BuildMesa(Transform c, Job j, GameObject prefab)
        {
            // 메사 윗면 가장자리가 둥글게 깎여 있어(17 m로 늘리면 약 0.6 m 폭), 양옆으로 MesaEdgeOverhang만큼 넓혀
            // 콜라이더 끝에서 겉모습 윗면이 너무 꺼지지 않게 한다.
            var b = j.box;
            var size = new Vector3(b.size.x + 2f * MesaEdgeOverhang, b.size.y / FlatTopFrac[Mesa], b.size.z);
            Place(c, prefab, Mesa, Quaternion.identity, size, Rel(c, b.center), Rel(c, b.max).y, true, "MesaPlateau");
        }

        static void BuildRockPillar(Transform c, Job j, GameObject prefab)
        {
            // 32~37 m 기둥: 3개를 쌓는다. 모델 윗쪽 7%가 울퉁불퉁하고 좁아지므로(아래쪽은 평평하고 꽉 찬 면)
            //   맨 아래 조각: 그대로(평평한 바닥 = 기둥 바닥)
            //   가운데 · 맨 위: Z로 180° 뒤집어 평평한 면이 위(맨 위 조각의 그 면 = 상자 윗면)
            //   이음 0-1은 울퉁불퉁한 끝끼리 만나므로 2×RockPillarJoinFrac, 이음 1-2는 RockPillarJoinFrac만큼 겹쳐 묻는다.
            var b = j.box;
            float f = RockPillarJoinFrac;
            float segH = b.size.y / (3f - 3f * f);
            var size = new Vector3(b.size.x, segH, b.size.z);
            var flip = Quaternion.Euler(0f, 0f, 180f);
            Place(c, prefab, RockPillar, Quaternion.identity, size, Rel(c, b.center), b.min.y - c.position.y, false, "RockPillar_0");
            Place(c, prefab, RockPillar, RotY(90f) * flip, size, Rel(c, b.center), b.min.y + segH * (1f - 2f * f) - c.position.y, false, "RockPillar_1");
            Place(c, prefab, RockPillar, RotY(180f) * flip, size, Rel(c, b.center), Rel(c, b.max).y, true, "RockPillar_2");
        }

        static void BuildCliff(Transform c, Job j, GameObject prefab)
        {
            // 폭 6 m에 모델 비율 그대로 ≈ 10.9 m 높이 타일을 세로로 쌓는다. 번갈아 Y 180°로 뒤집어 반복 무늬를 줄인다.
            var b = j.box;
            int n = Mathf.Max(1, Mathf.RoundToInt(b.size.y / CliffTileTargetH));
            float segH = (b.size.y + StackOverlap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                float bottom = b.min.y + i * (segH - StackOverlap);
                Place(c, prefab, Cliff, RotY(i % 2 == 0 ? 0f : 180f), new Vector3(b.size.x, segH, b.size.z), Rel(c, b.center),
                    bottom - c.position.y, false, "CliffWall_" + i.ToString("00"));
            }
        }

        // ───────────────────────── Ground · Backdrop (만든 메시) ─────────────────────────
        // VARCO 텍스처는 모델 UV 아틀라스라 그대로 반복할 수 없다. 고른 두 영역(메사 윗면 풀, 절벽 돌)을 잘라
        // Wrap = Mirror 텍스처로 만들고(이음선 없음), 월드 좌표 UV를 가진 메시를 EnvVisual 아래 만든다(메시는 씬에 저장).

        const string TileDir = Root + "Textures/Env_Tile/";
        const string TileVersion = "envtile-v2-seamless"; // 자르기 · 섞기 방식이 바뀌면 올려 다시 만든다(TextureImporter.userData)
        const string MatGrass = Root + "Materials/M_Ground_Grass.mat";
        const string MatRock = Root + "Materials/M_Ground_Rock.mat";
        const string MatBackdrop = Root + "Materials/M_Backdrop_Cliff.mat";
        // 잘라낼 영역(아틀라스 비율, 왼쪽 위 원점): x0, y0, x1, y1
        static readonly float[] GrassRect = { 0.06f, 0.21f, 0.49f, 0.43f };   // Env_MesaPlateau 윗면 풀
        static readonly float[] RockRect = { 0.015f, 0.12f, 0.54f, 0.645f };  // Env_CliffWall 돌 블록 · 이끼
        const float GrassTileX = 8f, GrassTileY = 4.1f;  // 풀 타일 크기(m, 잘라낸 비율 그대로)
        const float RockTile = 11f;                      // 바위 타일(m)
        const float BackdropTile = 30f;                  // 배경 절벽 타일(m, 멀리 있어 크게)
        const float GrassBand = 0.28f;                   // 윗면 가장자리에서 옆면으로 내려오는 풀 띠 평균 두께(m)

        static void EnsureTileAssets()
        {
            bool made = false;
            made |= CropSet("Env_MesaPlateau", "grass", GrassRect);
            made |= CropSet("Env_CliffWall", "rock", RockRect);
            if (made) AssetDatabase.Refresh();
            foreach (var p in new[] { "grass", "rock" })
            {
                SetTileTex(TileDir + p + "_base_color.png", TextureImporterType.Default, true);
                SetTileTex(TileDir + p + "_normal.png", TextureImporterType.NormalMap, false);
                SetTileTex(TileDir + p + "_metallic_smoothness.png", TextureImporterType.Default, false);
            }
            TileMat(MatGrass, "grass", Color.white, 0.25f);
            TileMat(MatRock, "rock", new Color(1.0f, 0.95f, 0.90f), 0.3f);   // 메사 옆면처럼 조금 따뜻한 돌
            TileMat(MatBackdrop, "rock", new Color(0.40f, 0.45f, 0.54f), 0.05f); // 멀리 있는 절벽: 어둡고 푸르스름하게
            AssetDatabase.SaveAssets();
        }

        static bool CropSet(string model, string prefix, float[] r)
        {
            bool made = false;
            string src = Root + "Textures/" + model + "/";
            Directory.CreateDirectory(TileDir);
            foreach (var m in new[] { "base_color", "normal", "metallic_smoothness" })
            {
                string dst = TileDir + prefix + "_" + m + ".png";
                var old = AssetImporter.GetAtPath(dst);
                if (File.Exists(dst) && old != null && old.userData == TileVersion) continue; // 이번 방식으로 이미 만듦
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                tex.LoadImage(File.ReadAllBytes(src + m + ".png"));
                int W = tex.width, H = tex.height;
                int x0 = Mathf.RoundToInt(r[0] * W), x1 = Mathf.RoundToInt(r[2] * W);
                int yTop0 = Mathf.RoundToInt(r[1] * H), yTop1 = Mathf.RoundToInt(r[3] * H);
                int w = x1 - x0, h = yTop1 - yTop0;
                var px = tex.GetPixels32();
                var outPx = new Color32[w * h];
                int yb0 = H - yTop1; // 아래 원점
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        outPx[y * w + x] = px[(yb0 + y) * W + x0 + x];
                outPx = MakeSeamless(outPx, w, h);
                var o = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                o.SetPixels32(outPx);
                File.WriteAllBytes(dst, o.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(o);
                made = true;
            }
            return made;
        }

        // 잘라낸 그림을 이음선 없이 반복되게: 원본 · x로 반 밀기 · y로 반 밀기 · 둘 다 밀기 4장을 가장자리에서 섞는다
        // (각 장의 이음선에서는 그 장의 비중이 0). 거울 반복(Mirror)의 대칭 무늬가 생기지 않는다.
        static Color32[] MakeSeamless(Color32[] s, int w, int h)
        {
            var d = new Color32[w * h];
            int hw = w / 2, hh = h / 2;
            for (int y = 0; y < h; y++)
            {
                float ay = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(y, h - 1 - y) / (h * 0.5f) / 0.6f));
                for (int x = 0; x < w; x++)
                {
                    float ax = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(x, w - 1 - x) / (w * 0.5f) / 0.6f));
                    int sx = (x + hw) % w, sy = (y + hh) % h;
                    Color a = s[y * w + x], bx = s[y * w + sx], by = s[sy * w + x], bxy = s[sy * w + sx];
                    d[y * w + x] = a * (ax * ay) + bx * ((1f - ax) * ay) + by * (ax * (1f - ay)) + bxy * ((1f - ax) * (1f - ay));
                }
            }
            return d;
        }

        static void SetTileTex(string path, TextureImporterType type, bool srgb)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { AssetDatabase.ImportAsset(path); ti = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (ti == null) return;
            if (ti.userData == TileVersion && ti.textureType == type && ti.sRGBTexture == srgb && ti.wrapMode == TextureWrapMode.Repeat && ti.anisoLevel == 4
                && ti.npotScale == TextureImporterNPOTScale.None) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.userData = TileVersion;
            ti.anisoLevel = 4;
            ti.mipmapEnabled = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }

        static void TileMat(string path, string prefix, Color tint, float smooth)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = lit;
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TileDir + prefix + "_base_color.png");
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetTexture("_MainTex", baseTex);
            mat.SetColor("_BaseColor", tint);
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TileDir + prefix + "_normal.png"));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TileDir + prefix + "_metallic_smoothness.png"));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(mat);
        }

        static Bounds MeshWorldBounds(Transform t)
        {
            var mb = t.GetComponent<MeshFilter>().sharedMesh.bounds;
            var b = new Bounds(t.TransformPoint(mb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(t.TransformPoint(c));
            }
            return b;
        }

        // 메시를 만드는 도우미. 면마다 꼭짓점을 따로(각진 면 그림자).
        class MeshBuild
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int>[] sub;
            public MeshBuild(int subs) { sub = new List<int>[subs]; for (int i = 0; i < subs; i++) sub[i] = new List<int>(); }

            // a-b-c-d 사각형, 바깥 방향 nrm. uvf = 위치 → UV.
            public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, System.Func<Vector3, Vector2> uvf)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { var tmp = b; b = d; d = tmp; }
                int i0 = v.Count;
                foreach (var p in new[] { a, b, c, d }) { v.Add(p); n.Add(nrm); uv.Add(uvf(p)); }
                sub[s].AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.subMeshCount = sub.Length;
                for (int i = 0; i < sub.Length; i++) m.SetTriangles(sub[i], i);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        static GameObject MeshObject(Transform container, string name, Mesh mesh, Material[] mats, UnityEngine.Rendering.ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(container, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = shadows;
            return go;
        }

        static float Fbm(float x, float y)
        {
            return Mathf.PerlinNoise(x, y) * 0.6f + Mathf.PerlinNoise(x * 2.1f + 5.3f, y * 2.1f + 1.7f) * 0.3f
                + Mathf.PerlinNoise(x * 4.3f + 9.1f, y * 4.3f + 3.3f) * 0.1f;
        }

        // ───────────────────────── Floor_Basin: 메사 타일 ─────────────────────────
        // 시작 발판(Ledge_Start = Env_MesaPlateau)과 같은 겉모습으로 맞춘다.
        //   윗줄: 메사 조각을 경사(윗면 꺾인선의 각 구간)에 맞춰 Z로 돌려 잇는다. 조각 크기는 Ledge_Start 메사와 비슷하게
        //         (길이 ≈ 18.8 m, 높이 8 m) 해서 풀 턱 · 돌 블록 크기가 같다. 메사 양 끝 6.5%는 윗면이 둥글게 내려가므로
        //         그만큼씩 겹쳐서 평평한 구간끼리만 이어지게 한다. 겹친 곳이 깜빡이지 않게 하나 걸러 깊이 · 높이를 조금 다르게.
        //   아랫줄: 윗줄 바닥(경사와 나란함)과 분지 바닥(y = bottom) 사이를 메우는 수평 메사. 깊이를 줄여(0.80) 풀 턱이
        //         윗줄 돌 면 뒤에 숨고, 윗면은 경사 윗면보다 1.2 m 아래라 윗줄 속에 들어간다.
        const bool BasinUsesMesa = true;          // false면 예전처럼 만든 메시(GrassGround)
        const float BasinTileLen = 18.8f;         // 조각 길이 목표(Ledge_Start 메사 길이)
        const float BasinTileH = 8f;              // 서는 면에서 조각 바닥까지(Ledge_Start 높이)
        const float MesaEndFrac = 0.065f;         // 메사 한쪽 끝에서 윗면이 내려가는 구간 / 전체 길이
        const float BasinLowerDepthFrac = 0.80f;  // 아랫줄 깊이 비율(메사 몸통 / 풀 턱 폭 = 0.83보다 작게)
        const float BasinLowerTopDrop = 1.2f;     // 아랫줄 윗면이 경사 윗면보다 내려가는 양

        static void BuildBasinMesa(Transform c, Job j, GameObject prefab)
        {
            var t = j.target;
            var src = t.GetComponent<MeshFilter>().sharedMesh;
            var sv = src.vertices; var tri = src.triangles;
            var w = new Vector3[sv.Length];
            for (int i = 0; i < sv.Length; i++) w[i] = Rel(c, t.TransformPoint(sv[i]));
            float zMin = w.Min(p => p.z), zMax = w.Max(p => p.z), bottom = w.Min(p => p.y);
            var top = new List<Vector2>();
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = w[tri[i]], b = w[tri[i + 1]], cc = w[tri[i + 2]];
                if (Vector3.Cross(b - a, cc - a).normalized.y < 0.3f) continue;
                foreach (var p in new[] { a, b, cc })
                    if (!top.Any(q => Mathf.Abs(q.x - p.x) < 1e-3f)) top.Add(new Vector2(p.x, p.y));
            }
            top.Sort((p, q) => p.x.CompareTo(q.x));
            if (top.Count < 2) { Debug.LogWarning("[EnvVisuals] 윗면을 못 찾음: " + t.name); return; }
            System.Func<float, float> topY = x =>
            {
                if (x <= top[0].x) return top[0].y;
                for (int s = 0; s < top.Count - 1; s++)
                    if (x <= top[s + 1].x + 1e-4f) return Mathf.Lerp(top[s].y, top[s + 1].y, Mathf.InverseLerp(top[s].x, top[s + 1].x, x));
                return top[top.Count - 1].y;
            };
            float zc = (zMin + zMax) * 0.5f, depth = zMax - zMin, frac = FlatTopFrac[Mesa];
            float e = MesaEndFrac;

            // 윗줄
            int idx = 0;
            for (int s = 0; s < top.Count - 1; s++)
            {
                Vector2 A = top[s], B = top[s + 1];
                float len = Vector2.Distance(A, B);
                if (len < 0.5f) continue;
                float ang = Mathf.Atan2(B.y - A.y, B.x - A.x) * Mathf.Rad2Deg;
                int n = Mathf.Max(1, Mathf.RoundToInt(len / (BasinTileLen * (1f - 2f * e))));
                float L = len / (n * (1f - 2f * e)), ov = e * L;
                for (int i = 0; i < n; i++, idx++)
                {
                    bool even = idx % 2 == 0;
                    float mid = -ov + L * 0.5f + i * (L - 2f * ov);
                    var size = new Vector3(L, BasinTileH / frac, depth * (even ? 0.985f : 1f));
                    var tr = Place(c, prefab, Mesa, Quaternion.identity, size, new Vector3(A.x + mid, 0f, zc), A.y - (even ? 0f : 0.012f), true,
                        "MesaSlope_" + idx.ToString("00"));
                    tr.RotateAround(c.position + new Vector3(A.x, A.y, zc), Vector3.forward, ang);
                }
            }

            // 아랫줄
            float xa = top[0].x - 0.5f, xb = top[top.Count - 1].x + 0.8f, range = xb - xa;
            int n2 = Mathf.Max(1, Mathf.RoundToInt(range / (BasinTileLen * (1f - 2f * e))));
            float L2 = range / (n2 * (1f - 2f * e)), ov2 = e * L2;
            for (int i = 0; i < n2; i++)
            {
                float left = xa - ov2 + i * (L2 - 2f * ov2);
                float topAt = Mathf.Min(bottom + BasinTileH, topY(left) - BasinLowerTopDrop);
                if (topAt - bottom < 1f) continue;
                var size = new Vector3(L2, (topAt - bottom) / frac, depth * BasinLowerDepthFrac * (i % 2 == 0 ? 1f : 0.97f));
                Place(c, prefab, Mesa, Quaternion.identity, size, new Vector3(left + L2 * 0.5f, 0f, zc), bottom, false, "MesaBase_" + i.ToString("00"));
            }
        }

        // Floor_Basin: z로 밀어 낸 모양(윗면 꺾인선 + 평평한 바닥). 같은 모양을 다시 만들어 윗면이 콜라이더와 정확히 같다.
        //   풀(submesh 0): 윗면 + 옆면 위쪽 띠(두께 0.16~0.40 m, 울퉁불퉁) / 바위(submesh 1): 나머지 옆면 · 바닥
        static void BuildGround(Transform c, Job j)
        {
            var t = j.target;
            var src = t.GetComponent<MeshFilter>().sharedMesh;
            var sv = src.vertices; var tri = src.triangles;
            var w = new Vector3[sv.Length];
            for (int i = 0; i < sv.Length; i++) w[i] = Rel(c, t.TransformPoint(sv[i]));
            float zMin = w.Min(p => p.z), zMax = w.Max(p => p.z), bottom = w.Min(p => p.y);
            // 윗면 꺾인선: 위를 향한 삼각형의 꼭짓점 (x, y)
            var top = new List<Vector2>();
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = w[tri[i]], b = w[tri[i + 1]], cc = w[tri[i + 2]];
                if (Vector3.Cross(b - a, cc - a).normalized.y < 0.3f) continue;
                foreach (var p in new[] { a, b, cc })
                    if (!top.Any(q => Mathf.Abs(q.x - p.x) < 1e-3f)) top.Add(new Vector2(p.x, p.y));
            }
            top.Sort((p, q) => p.x.CompareTo(q.x));
            if (top.Count < 2) { Debug.LogWarning("[EnvVisuals] 윗면을 못 찾음: " + t.name); return; }

            // 열: 꺾인점을 포함해 0.5 m 간격
            var xs = new List<float>();
            for (int s = 0; s < top.Count - 1; s++)
            {
                int k = Mathf.Max(1, Mathf.CeilToInt((top[s + 1].x - top[s].x) / 0.5f));
                for (int i = 0; i < k; i++) xs.Add(Mathf.Lerp(top[s].x, top[s + 1].x, (float)i / k));
            }
            xs.Add(top[top.Count - 1].x);
            System.Func<float, float> topY = x =>
            {
                for (int s = 0; s < top.Count - 1; s++)
                    if (x <= top[s + 1].x + 1e-4f) return Mathf.Lerp(top[s].y, top[s + 1].y, Mathf.InverseLerp(top[s].x, top[s + 1].x, x));
                return top[top.Count - 1].y;
            };
            var o = c.position;
            System.Func<float, float> band = x => Mathf.Min(GrassBand * (1f + 0.75f * (Mathf.PerlinNoise((x + o.x) * 0.6f, 3.1f) - 0.5f) * 2f), (topY(x) - bottom) * 0.4f);
            System.Func<Vector3, Vector2> uvTop = p => new Vector2((p.x + o.x) / GrassTileX, (p.z + o.z) / GrassTileY);
            System.Func<Vector3, Vector2> uvGrassX = p => new Vector2((p.x + o.x) / GrassTileX, (p.y + o.y) / GrassTileY);
            System.Func<Vector3, Vector2> uvGrassZ = p => new Vector2((p.z + o.z) / GrassTileX, (p.y + o.y) / GrassTileY);
            System.Func<Vector3, Vector2> uvRockX = p => new Vector2((p.x + o.x) / RockTile, (p.y + o.y) / RockTile);
            System.Func<Vector3, Vector2> uvRockZ = p => new Vector2((p.z + o.z) / RockTile, (p.y + o.y) / RockTile);
            System.Func<Vector3, Vector2> uvRockBottom = p => new Vector2((p.x + o.x) / RockTile, (p.z + o.z) / RockTile);

            var mb = new MeshBuild(2);
            for (int i = 0; i < xs.Count - 1; i++)
            {
                float x0 = xs[i], x1 = xs[i + 1];
                float t0 = topY(x0), t1 = topY(x1), b0 = t0 - band(x0), b1 = t1 - band(x1);
                var up = Vector3.Cross(new Vector3(0, 0, 1), new Vector3(x1 - x0, t1 - t0, 0)).normalized;
                if (up.y < 0) up = -up;
                mb.Quad(0, new Vector3(x0, t0, zMin), new Vector3(x1, t1, zMin), new Vector3(x1, t1, zMax), new Vector3(x0, t0, zMax), up, uvTop);
                foreach (var z in new[] { zMin, zMax })
                {
                    var nz = new Vector3(0, 0, z == zMin ? -1 : 1);
                    mb.Quad(0, new Vector3(x0, b0, z), new Vector3(x1, b1, z), new Vector3(x1, t1, z), new Vector3(x0, t0, z), nz, uvGrassX);
                    mb.Quad(1, new Vector3(x0, bottom, z), new Vector3(x1, bottom, z), new Vector3(x1, b1, z), new Vector3(x0, b0, z), nz, uvRockX);
                }
                mb.Quad(1, new Vector3(x0, bottom, zMin), new Vector3(x1, bottom, zMin), new Vector3(x1, bottom, zMax), new Vector3(x0, bottom, zMax), Vector3.down, uvRockBottom);
            }
            foreach (var x in new[] { xs[0], xs[xs.Count - 1] })
            {
                var nx = new Vector3(x == xs[0] ? -1 : 1, 0, 0);
                float ty = topY(x), by = ty - band(x);
                if (ty - bottom < 0.01f) continue;
                mb.Quad(0, new Vector3(x, by, zMin), new Vector3(x, by, zMax), new Vector3(x, ty, zMax), new Vector3(x, ty, zMin), nx, uvGrassZ);
                mb.Quad(1, new Vector3(x, bottom, zMin), new Vector3(x, bottom, zMax), new Vector3(x, by, zMax), new Vector3(x, by, zMin), nx, uvRockZ);
            }
            var mats = new[] { AssetDatabase.LoadAssetAtPath<Material>(MatGrass), AssetDatabase.LoadAssetAtPath<Material>(MatRock) };
            var shadows = t.GetComponent<MeshRenderer>().shadowCastingMode;
            MeshObject(c, "GrassGround", mb.ToMesh(t.name + "_EnvGround"), mats, shadows);
        }

        // 배경: 판들의 윗면을 잇는 산 능선(판 가운데 높이를 잇고 울퉁불퉁하게)과 공통 바닥.
        static List<Vector2> backdropTops = new List<Vector2>();
        static float backdropBottom;

        static void PrepareBackdropProfile(List<Job> jobs)
        {
            var bj = jobs.Where(x => x.kind == "Backdrop").ToList();
            backdropTops = bj.Select(x => new Vector2(x.box.center.x, x.box.max.y)).OrderBy(p => p.x).ToList();
            backdropBottom = bj.Count > 0 ? bj.Min(x => x.box.min.y) : 0f;
        }

        static float BackdropTopY(float x)
        {
            var p = backdropTops;
            float y;
            if (x <= p[0].x) y = p[0].y;
            else if (x >= p[p.Count - 1].x) y = p[p.Count - 1].y;
            else
            {
                int s = 0;
                while (s < p.Count - 2 && x > p[s + 1].x) s++;
                float u = Mathf.InverseLerp(p[s].x, p[s + 1].x, x);
                y = Mathf.Lerp(p[s].y, p[s + 1].y, u * u * (3f - 2f * u) * 0.5f + u * 0.5f);
            }
            return y + (Fbm(x / 9f, 7.7f) - 0.5f) * 14f + (Mathf.PerlinNoise(x / 2.3f, 2.2f) - 0.5f) * 3f;
        }

        const float BackdropRelief = 1.6f;
        static float BackdropZ(float x, float y) { return (Fbm(x / 11f, y / 11f) - 0.5f) * 2f * BackdropRelief; }

        // 앞면(-z)만. 열 1 m · 행은 열마다 바닥~능선을 같은 수(60)로 나눠 이웃 판과 경계 꼭짓점이 같다(틈 없음).
        static void BuildBackdrop(Transform c, Job j)
        {
            var b = j.box;
            float cx = b.center.x;
            int idx = backdropTops.FindIndex(p => Mathf.Abs(p.x - cx) < 1e-3f);
            float xL = idx > 0 ? (backdropTops[idx - 1].x + cx) * 0.5f : b.min.x;
            float xR = idx >= 0 && idx < backdropTops.Count - 1 ? (backdropTops[idx + 1].x + cx) * 0.5f : b.max.x;
            float zf = b.min.z;
            int nc = Mathf.Max(1, Mathf.CeilToInt(xR - xL));
            const int nr = 60;
            var o = c.position;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= nc; i++)
            {
                float x = Mathf.Lerp(xL, xR, (float)i / nc);
                float ty = BackdropTopY(x);
                for (int k = 0; k <= nr; k++)
                {
                    float y = Mathf.Lerp(backdropBottom, ty, (float)k / nr);
                    float z = zf + BackdropZ(x, y);
                    const float e = 0.25f;
                    float dx = (BackdropZ(x + e, y) - BackdropZ(x - e, y)) / (2f * e);
                    float dy = (BackdropZ(x, y + e) - BackdropZ(x, y - e)) / (2f * e);
                    v.Add(new Vector3(x, y, z) - o);
                    n.Add(new Vector3(dx, dy, -1f).normalized);
                    uv.Add(new Vector2(x / BackdropTile, y / BackdropTile));
                }
            }
            for (int i = 0; i < nc; i++)
                for (int k = 0; k < nr; k++)
                {
                    int a = i * (nr + 1) + k, bb = (i + 1) * (nr + 1) + k;
                    // (x 오른쪽, y 위)에서 -z를 향하도록: a, a+1, bb+1 / a, bb+1, bb
                    tris.AddRange(new[] { a, a + 1, bb + 1, a, bb + 1, bb });
                }
            var m = new Mesh { name = j.target.name + "_EnvBackdrop" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            var orig = j.target.GetComponent<MeshRenderer>().shadowCastingMode;
            var sh = orig == UnityEngine.Rendering.ShadowCastingMode.Off ? orig : UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            MeshObject(c, "BackdropCliff", m, new[] { AssetDatabase.LoadAssetAtPath<Material>(MatBackdrop) }, sh);
        }

        // ───────────────────────── Audit ─────────────────────────

        [MenuItem("Pop/Dev/Env Visuals/Audit")]
        public static void AuditMenu() { Debug.Log(Audit()); }

        /// <summary>켜진 MeshRenderer 중 EnvVisual · Crag Patch · Player · GameUI · 도토리 나무가 아닌 것(= 남은 회색 상자).</summary>
        public static string Audit()
        {
            var sb = new StringBuilder("[EnvVisuals] Audit — 남은 회색 상자 렌더러\n");
            int count = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                var t = mr.transform;
                if (IsUnderVisual(t) || HasAncestor(t, "Player") || HasAncestor(t, "GameUI") || HasAncestor(t, "AcornTree_Goal")) continue;
                if (t.name == "Patch" && t.parent != null && t.parent.name.StartsWith("Crag_")) continue;
                count++;
                sb.AppendLine("  " + PathOf(t) + " | " + string.Join(",", mr.sharedMaterials.Select(m => m != null ? m.name : "null").ToArray()) + " | " + mr.bounds);
            }
            sb.AppendLine("  합계 " + count);
            return sb.ToString();
        }

        // ───────────────────────── Fit check ─────────────────────────

        [MenuItem("Pop/Dev/Env Visuals/Fit Check")]
        public static void FitCheckMenu()
        {
            Debug.Log("[EnvVisuals] Fit Check\n" + FitReport(CollectJobs(new List<string>())));
        }

        /// <summary>
        /// 바꾼 오브젝트마다 겉모습 메시와 원래 콜라이더를 실제 레이로 비교한다(각각 임시 프리뷰 씬에 콜라이더로 복사).
        /// 플레이어가 있는 z = 0 평면에서:
        ///   윗면: x를 5 cm 간격으로 위에서 아래로 쏴서 (겉모습 높이 - 콜라이더 높이).
        ///         med/min/max는 양 끝 0.3 m를 뺀 안쪽, above%는 +0.05 넘는 비율(통나무 끝 그루터기 등).
        ///   inset: 서는 면에서 콜라이더 옆 끝부터 겉모습 윗면이 -0.15 안으로 들어오는 데까지 거리(L/R)
        ///   side: 옆에서 높이 10곳 이상 수평으로 쏴서 (콜라이더 면 - 겉모습 면), + = 겉모습이 바깥으로 튀어나옴
        /// 규칙: 서는 면 |윗면| ≤ 0.05, inset ≤ 0.15, side out ≤ 0.15 (옆면 안쪽 들어감은 서는 면이 아니면 참고만)
        /// </summary>
        public static string FitReport(List<Job> jobs)
        {
            var sb = new StringBuilder("  맞춤 검사 (m, 레이 비교, z = 0)\n");
            // kind → [윗면 med 최대|.|, 윗면 max, 윗면 min, above% 최대, inset 최대, side out 최대, side in 최대]
            var worst = new Dictionary<string, float[]>();
            var visScene = EditorSceneManager.NewPreviewScene();
            var colScene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var j in jobs)
                {
                    if (j.kind == "Backdrop") continue; // 콜라이더 없음(배경)
                    var vis = j.target.Find(VisualName);
                    if (vis == null) { sb.AppendLine("    EnvVisual 없음: " + PathOf(j.target)); continue; }
                    var temps = new List<GameObject>();
                    foreach (var mf in vis.GetComponentsInChildren<MeshFilter>())
                    {
                        var g = new GameObject("v");
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, visScene);
                        g.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                        g.transform.localScale = mf.transform.lossyScale;
                        g.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                        temps.Add(g);
                    }
                    var src = j.target.GetComponent<Collider>();
                    var cg = new GameObject("c");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cg, colScene);
                    cg.transform.SetPositionAndRotation(j.target.position, j.target.rotation);
                    cg.transform.localScale = j.target.lossyScale;
                    EditorUtility.CopySerialized(src, cg.AddComponent(src.GetType()));
                    temps.Add(cg);
                    Physics.SyncTransforms();
                    var pv = visScene.GetPhysicsScene();
                    var pc = colScene.GetPhysicsScene();
                    var b = j.box;
                    bool standing = j.kind != "Wedge" && j.kind != "Cliff";

                    // 윗면
                    var inner = new List<float>();
                    float tMax = float.MinValue, tMin = float.MaxValue;
                    float insetL = float.NaN, insetR = float.NaN, lastOkX = float.NaN;
                    if (j.kind != "Cliff")
                    {
                        for (float x = b.min.x + 0.025f; x < b.max.x; x += 0.05f)
                        {
                            RaycastHit hc, hv;
                            var o = new Vector3(x, b.max.y + 3f, 0f);
                            if (!pc.Raycast(o, Vector3.down, out hc, 6f + b.size.y)) continue;
                            bool vh = pv.Raycast(o, Vector3.down, out hv, 6f + b.size.y);
                            float d = vh ? hv.point.y - hc.point.y : -99f;
                            bool ok = d >= -0.15f;
                            if (ok && float.IsNaN(insetL)) insetL = x - 0.025f - b.min.x;
                            if (ok) lastOkX = x;
                            bool interior = j.kind == "Wedge" || (x > b.min.x + 0.3f && x < b.max.x - 0.3f);
                            if (interior && vh) { inner.Add(d); tMax = Mathf.Max(tMax, d); tMin = Mathf.Min(tMin, d); }
                        }
                        insetR = float.IsNaN(lastOkX) ? b.size.x : b.max.x - (lastOkX + 0.025f);
                        if (float.IsNaN(insetL)) insetL = b.size.x;
                    }
                    inner.Sort();
                    float med = inner.Count > 0 ? inner[inner.Count / 2] : 0f;
                    float abovePct = inner.Count > 0 ? 100f * inner.Count(d => d > 0.05f) / inner.Count : 0f;

                    // 옆면
                    float sOut = 0f, sIn = 0f;
                    int ny = Mathf.Clamp(Mathf.CeilToInt(b.size.y), 10, 60);
                    for (int k = 0; k < ny; k++)
                    {
                        float y = Mathf.Lerp(b.min.y + 0.05f, b.max.y - 0.05f, (k + 0.5f) / ny);
                        foreach (int dir in new[] { 1, -1 })
                        {
                            if (j.kind == "Cliff" && ((j.target.name == "Wall_Left") != (dir == -1))) continue; // 벽은 안쪽 면만
                            var o = new Vector3(dir > 0 ? b.min.x - 3f : b.max.x + 3f, y, 0f);
                            RaycastHit hc, hv;
                            if (!pc.Raycast(o, Vector3.right * dir, out hc, b.size.x + 6f)) continue;
                            if (!pv.Raycast(o, Vector3.right * dir, out hv, b.size.x + 6f)) { sIn = Mathf.Max(sIn, b.size.x * 0.5f); continue; }
                            float d = (hc.point.x - hv.point.x) * dir; // + = 겉모습이 바깥으로
                            sOut = Mathf.Max(sOut, d); sIn = Mathf.Max(sIn, -d);
                        }
                    }
                    foreach (var g in temps) Object.DestroyImmediate(g);

                    float[] w;
                    if (!worst.TryGetValue(j.kind, out w)) worst[j.kind] = w = new float[] { 0, float.MinValue, float.MaxValue, 0, 0, 0, 0 };
                    if (Mathf.Abs(med) > Mathf.Abs(w[0])) w[0] = med;
                    if (inner.Count > 0) { w[1] = Mathf.Max(w[1], tMax); w[2] = Mathf.Min(w[2], tMin); }
                    w[3] = Mathf.Max(w[3], abovePct);
                    if (standing) w[4] = Mathf.Max(w[4], Mathf.Max(insetL, insetR));
                    w[5] = Mathf.Max(w[5], sOut); w[6] = Mathf.Max(w[6], sIn);

                    // Ground(분지)는 양 끝이 시작 발판 · 오른쪽 벽 속으로 들어가 있어 옆으로 나온 양은 참고만 한다.
                    bool bad = (standing && (Mathf.Abs(med) > 0.05f || insetL > 0.15f || insetR > 0.15f)) || (sOut > 0.15f && j.kind != "Ground");
                    if (j.kind == "Cliff")
                        sb.AppendLine(string.Format("    {0}{1} [{2}] 안쪽 면 side out {3:0.00} in {4:0.00}", bad ? "!! " : "", PathOf(j.target), j.kind, sOut, sIn));
                    else
                        sb.AppendLine(string.Format("    {0}{1} [{2}] 윗면 med {3:+0.00;-0.00} min {4:+0.00;-0.00} max {5:+0.00;-0.00} above {6:0}%{7} | side out {8:0.00} in {9:0.00}",
                            bad ? "!! " : "", PathOf(j.target), j.kind, med, inner.Count > 0 ? tMin : 0f, inner.Count > 0 ? tMax : 0f, abovePct,
                            standing ? string.Format(" inset L {0:0.00} R {1:0.00}", insetL, insetR) : "", sOut, sIn));
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(visScene);
                EditorSceneManager.ClosePreviewScene(colScene);
            }
            sb.AppendLine("  종류별 최대 (윗면 med · min · max · above% · inset · side out · side in):");
            foreach (var kv in worst)
            {
                var w = kv.Value;
                sb.AppendLine(string.Format("    {0}: med {1:+0.00;-0.00} min {2:+0.00;-0.00} max {3:+0.00;-0.00} above {4:0}% inset {5:0.00} sideOut {6:0.00} sideIn {7:0.00}",
                    kv.Key, w[0], w[2] == float.MaxValue ? 0f : w[2], w[1] == float.MinValue ? 0f : w[1], w[3], w[4], w[5], w[6]));
            }
            return sb.ToString();
        }

        static string ModelOf(GameObject go)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (src == null) return null;
            return System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(src));
        }

        // ───────────────────────── Remove ─────────────────────────

        [MenuItem("Pop/Dev/Env Visuals/Remove")]
        public static void Remove()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Undo.SetCurrentGroupName("Remove Env Visuals");
            int group = Undo.GetCurrentGroup();
            int n = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == VisualName).ToList())
            {
                if (t == null) continue;
                var mr = t.parent != null ? t.parent.GetComponent<MeshRenderer>() : null;
                if (mr != null && !mr.enabled) { Undo.RecordObject(mr, "Enable grey-box renderer"); mr.enabled = true; }
                Undo.DestroyObjectImmediate(t.gameObject);
                n++;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[EnvVisuals] EnvVisual " + n + "개 지움, 원래 렌더러 다시 켬");
        }
    }
}
